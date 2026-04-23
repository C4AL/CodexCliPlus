using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using DesktopHost.Core.Abstractions.Configuration;
using DesktopHost.Core.Abstractions.Paths;
using DesktopHost.Core.Models;

namespace DesktopHost.Infrastructure.Configuration;

public sealed class JsonDesktopConfigurationService : IDesktopConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IPathService _pathService;

    public JsonDesktopConfigurationService(IPathService pathService)
    {
        _pathService = pathService;
    }

    public async Task<DesktopSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _pathService.EnsureCreatedAsync(cancellationToken);

        if (!File.Exists(_pathService.Directories.DesktopConfigFilePath))
        {
            return new DesktopSettings();
        }

        await using var stream = File.OpenRead(_pathService.Directories.DesktopConfigFilePath);
        var settings = await JsonSerializer.DeserializeAsync<DesktopSettings>(stream, JsonOptions, cancellationToken);
        return settings ?? new DesktopSettings();
    }

    public async Task SaveAsync(DesktopSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _pathService.EnsureCreatedAsync(cancellationToken);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(
            _pathService.Directories.DesktopConfigFilePath,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }
}
