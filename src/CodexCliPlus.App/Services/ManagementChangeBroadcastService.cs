using System.IO;
using CodexCliPlus.Core.Abstractions.Logging;
using CodexCliPlus.Core.Abstractions.Paths;

namespace CodexCliPlus.Services;

public sealed class ManagementChangeBroadcastService : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250);

    private readonly IPathService _pathService;
    private readonly IAppLogger _logger;
    private readonly object _gate = new();
    private readonly HashSet<string> _pendingScopes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly System.Threading.Timer _debounceTimer;

    private bool _started;
    private bool _disposed;
    private long _sequence;

    public ManagementChangeBroadcastService(IPathService pathService, IAppLogger logger)
    {
        _pathService = pathService;
        _logger = logger;
        _debounceTimer = new System.Threading.Timer(
            FlushPendingChanges,
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan
        );
    }

    public event EventHandler<ManagementDataChangedEventArgs>? DataChanged;

    public void Start()
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            WatchFile(_pathService.Directories.BackendConfigFilePath, "config", "providers", "quota");
            WatchDirectory(
                Path.Combine(_pathService.Directories.BackendDirectory, "auth"),
                includeSubdirectories: true,
                "auth-files",
                "quota",
                "providers"
            );
            WatchDirectory(_pathService.Directories.LogsDirectory, includeSubdirectories: true, "logs");
            WatchDirectory(
                _pathService.Directories.PersistenceDirectory,
                includeSubdirectories: true,
                "usage",
                "logs",
                "persistence"
            );
        }
    }

    public void Broadcast(params string[] scopes)
    {
        Broadcast((IEnumerable<string>)scopes);
    }

    public void Broadcast(IEnumerable<string> scopes)
    {
        if (_disposed)
        {
            return;
        }

        var normalized = NormalizeScopes(scopes);
        if (normalized.Length == 0)
        {
            return;
        }

        var sequence = Interlocked.Increment(ref _sequence);
        DataChanged?.Invoke(this, new ManagementDataChangedEventArgs(sequence, normalized));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _debounceTimer.Dispose();
        lock (_gate)
        {
            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }

            _watchers.Clear();
            _pendingScopes.Clear();
        }
    }

    private void WatchFile(string filePath, params string[] scopes)
    {
        var directory = Path.GetDirectoryName(filePath);
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        Watch(directory, fileName, includeSubdirectories: false, scopes);
    }

    private void WatchDirectory(
        string directory,
        bool includeSubdirectories,
        params string[] scopes
    )
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        Watch(directory, "*", includeSubdirectories, scopes);
    }

    private void Watch(
        string directory,
        string filter,
        bool includeSubdirectories,
        params string[] scopes
    )
    {
        try
        {
            var watcher = new FileSystemWatcher(directory, filter)
            {
                IncludeSubdirectories = includeSubdirectories,
                NotifyFilter =
                    NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size
                    | NotifyFilters.CreationTime,
            };
            FileSystemEventHandler fileHandler = (_, _) => Queue(scopes);
            RenamedEventHandler renameHandler = (_, _) => Queue(scopes);
            watcher.Changed += fileHandler;
            watcher.Created += fileHandler;
            watcher.Deleted += fileHandler;
            watcher.Renamed += renameHandler;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch (Exception exception)
        {
            _logger.Warn($"Failed to watch management changes in {directory}: {exception.Message}");
        }
    }

    private void Queue(IEnumerable<string> scopes)
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var scope in NormalizeScopes(scopes))
            {
                _pendingScopes.Add(scope);
            }

            _debounceTimer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void FlushPendingChanges(object? state)
    {
        string[] scopes;
        lock (_gate)
        {
            scopes = _pendingScopes.ToArray();
            _pendingScopes.Clear();
        }

        Broadcast(scopes);
    }

    private static string[] NormalizeScopes(IEnumerable<string> scopes)
    {
        return scopes
            .Select(scope => scope.Trim())
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

public sealed class ManagementDataChangedEventArgs : EventArgs
{
    public ManagementDataChangedEventArgs(long sequence, IReadOnlyList<string> scopes)
    {
        Sequence = sequence;
        Scopes = scopes;
    }

    public long Sequence { get; }

    public IReadOnlyList<string> Scopes { get; }
}
