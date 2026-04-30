using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexCliPlus.Core.Constants;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CodexCliPlus.BuildTool;

public static class AssetCommands
{
    private const string BackendSourceRepositoryUrl =
        "https://github.com/router-for-me/CLIProxyAPI.git";

    private const string PatchedBackendBuildDate = "2026-04-30";

    private sealed record BackendAssetFileMapping(
        string RepositoryFileName,
        string ArchiveEntryName,
        string TargetFileName
    );

    private static readonly BackendAssetFileMapping[] RequiredBackendFiles =
    [
        new(
            BackendExecutableNames.ManagedExecutableFileName,
            BackendExecutableNames.UpstreamExecutableFileName,
            BackendExecutableNames.ManagedExecutableFileName
        ),
        new("LICENSE", "LICENSE", "LICENSE"),
        new("README.md", "README.md", "README.md"),
        new("README_CN.md", "README_CN.md", "README_CN.md"),
        new("config.example.yaml", "config.example.yaml", "config.example.yaml"),
    ];

    private static readonly string[] RequiredBackendFileNames = RequiredBackendFiles
        .Select(file => file.TargetFileName)
        .ToArray();

    public static async Task<int> FetchAssetsAsync(BuildContext context)
    {
        var sourceDirectory = Path.Combine(
            context.Options.RepositoryRoot,
            "resources",
            "backend",
            "windows-x64"
        );
        SafeFileSystem.CleanDirectory(context.AssetsRoot, context.Options.OutputRoot);
        var backendTarget = Path.Combine(context.AssetsRoot, "backend", "windows-x64");

        if (
            Directory.Exists(sourceDirectory)
            && RequiredBackendFiles.All(file =>
                File.Exists(Path.Combine(sourceDirectory, file.RepositoryFileName))
            )
        )
        {
            foreach (var file in RequiredBackendFiles)
            {
                var sourcePath = Path.Combine(sourceDirectory, file.RepositoryFileName);
                Directory.CreateDirectory(backendTarget);
                File.Copy(
                    sourcePath,
                    Path.Combine(backendTarget, file.TargetFileName),
                    overwrite: true
                );
                context.Logger.Info($"asset copied: backend/windows-x64/{file.TargetFileName}");
            }
        }
        else
        {
            if (!BackendReleaseMetadata.RemoteArchiveFallbackEnabled)
            {
                context.Logger.Info(
                    "local patched backend resources are incomplete; building patched backend from source."
                );
                await BuildPatchedBackendFromSourceAsync(context, backendTarget);
                sourceDirectory =
                    $"{BackendSourceRepositoryUrl}@{BackendReleaseMetadata.SourceCommit}";
            }
            else
            {
                context.Logger.Info(
                    $"local backend resources are incomplete; downloading {BackendReleaseMetadata.ArchiveUrl}"
                );
                await DownloadBackendArchiveAsync(backendTarget, context.Logger);
                sourceDirectory = BackendReleaseMetadata.ArchiveUrl;
            }
        }

        var manifest = await BuildAssetManifest.CreateAsync(
            context.Options.Version,
            context.Options.Runtime,
            sourceDirectory,
            context.AssetsRoot,
            cancellationToken: default
        );
        Directory.CreateDirectory(context.AssetsRoot);
        await manifest.WriteAsync(context.AssetManifestPath);
        context.Logger.Info($"asset manifest: {context.AssetManifestPath}");
        return 0;
    }

    public static async Task<int> VerifyAssetsAsync(BuildContext context)
    {
        if (!File.Exists(context.AssetManifestPath))
        {
            context.Logger.Info(
                $"asset manifest not found; fetching assets: {context.AssetManifestPath}"
            );
            var fetchCode = await FetchAssetsAsync(context);
            if (fetchCode != 0)
            {
                return fetchCode;
            }
        }

        var manifest = await BuildAssetManifest.ReadAsync(context.AssetManifestPath);
        var failures = manifest.Verify(context.AssetsRoot);
        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            {
                context.Logger.Error(failure);
            }

            return 1;
        }

        context.Logger.Info($"asset verification passed: {manifest.Files.Count} file(s)");
        return 0;
    }

    private static async Task BuildPatchedBackendFromSourceAsync(
        BuildContext context,
        string backendTarget
    )
    {
        Directory.CreateDirectory(backendTarget);
        var sourceRoot = Path.Combine(context.Options.OutputRoot, "temp", "backend-source");
        SafeFileSystem.CleanDirectory(sourceRoot, context.Options.OutputRoot);

        await RunRequiredAsync(
            context,
            "git",
            ["clone", "--no-checkout", BackendSourceRepositoryUrl, sourceRoot],
            context.Options.RepositoryRoot,
            "clone CLIProxyAPI source"
        );
        await RunRequiredAsync(
            context,
            "git",
            ["checkout", BackendReleaseMetadata.SourceCommit],
            sourceRoot,
            "checkout pinned CLIProxyAPI commit"
        );

        ApplyPatchedBackendSourceChanges(sourceRoot);

        await RunRequiredAsync(
            context,
            "go",
            [
                "get",
                "github.com/jackc/pgx/v5@v5.9.2",
                "github.com/cloudflare/circl@v1.6.3",
                "github.com/go-git/go-git/v6@v6.0.0-alpha.2",
                "golang.org/x/crypto@v0.50.0",
                "golang.org/x/net@v0.53.0",
                "golang.org/x/sync@v0.20.0",
            ],
            sourceRoot,
            "apply patched backend dependency versions"
        );
        await RunRequiredAsync(
            context,
            "go",
            ["mod", "tidy"],
            sourceRoot,
            "tidy patched backend module"
        );
        await RunRequiredAsync(
            context,
            "gofmt",
            [
                "-w",
                Path.Combine("internal", "store", "gitstore.go"),
                Path.Combine("internal", "config", "config.go"),
                Path.Combine("internal", "config", "codexcliplus_secret_refs.go"),
            ],
            sourceRoot,
            "format patched backend source"
        );

        var executablePath = Path.Combine(
            backendTarget,
            BackendExecutableNames.ManagedExecutableFileName
        );
        var shortCommit = BackendReleaseMetadata.SourceCommit[..7];
        var ldflags =
            $"-s -w -X 'main.Version={BackendReleaseMetadata.Version}' "
            + $"-X 'main.Commit={shortCommit}-deps' "
            + $"-X 'main.BuildDate={PatchedBackendBuildDate}'";
        await RunRequiredAsync(
            context,
            "go",
            ["build", "-trimpath", "-ldflags", ldflags, "-o", executablePath, "./cmd/server/"],
            sourceRoot,
            "build patched backend executable",
            new Dictionary<string, string?>
            {
                ["CGO_ENABLED"] = "0",
                ["GOOS"] = "windows",
                ["GOARCH"] = "amd64",
            }
        );

        foreach (
            var file in RequiredBackendFiles.Where(file =>
                !string.Equals(
                    file.TargetFileName,
                    BackendExecutableNames.ManagedExecutableFileName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
        {
            File.Copy(
                Path.Combine(sourceRoot, file.ArchiveEntryName),
                Path.Combine(backendTarget, file.TargetFileName),
                overwrite: true
            );
            context.Logger.Info($"asset built: backend/windows-x64/{file.TargetFileName}");
        }

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"Patched backend build did not create {BackendExecutableNames.ManagedExecutableFileName}.",
                executablePath
            );
        }

        context.Logger.Info(
            $"asset built: backend/windows-x64/{BackendExecutableNames.ManagedExecutableFileName}"
        );
    }

    private static async Task RunRequiredAsync(
        BuildContext context,
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string description,
        IReadOnlyDictionary<string, string?>? environment = null
    )
    {
        var exitCode = await context.ProcessRunner.RunAsync(
            fileName,
            arguments,
            workingDirectory,
            context.Logger,
            environment: environment
        );
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"{description} failed with exit code {exitCode}.");
        }
    }

    private static void ApplyPatchedBackendSourceChanges(string sourceRoot)
    {
        ApplySecretRefSupportPatch(sourceRoot);

        var gitStorePath = Path.Combine(sourceRoot, "internal", "store", "gitstore.go");
        var source = File.ReadAllText(gitStorePath, Encoding.UTF8).Replace("\r\n", "\n");
        source = ReplaceRequired(
            source,
            "\"github.com/go-git/go-git/v6/plumbing\"\n",
            "\"github.com/go-git/go-git/v6/plumbing\"\n\t\"github.com/go-git/go-git/v6/plumbing/client\"\n"
        );
        source = ReplaceRequired(source, "Auth: authMethod", "ClientOptions: authMethod");
        source = ReplaceRequired(source, "Auth: s.gitAuth()", "ClientOptions: s.gitAuth()");
        source = ReplaceRequired(source, "transport.AuthMethod", "[]client.Option");
        source = ReplaceRequired(
            source,
            "return &http.BasicAuth{Username: user, Password: s.password}",
            "return []client.Option{client.WithHTTPAuth(&http.BasicAuth{Username: user, Password: s.password})}"
        );
        File.WriteAllText(gitStorePath, source, new UTF8Encoding(false));
    }

    private static void ApplySecretRefSupportPatch(string sourceRoot)
    {
        var configPath = Path.Combine(sourceRoot, "internal", "config", "config.go");
        var source = File.ReadAllText(configPath, Encoding.UTF8).Replace("\r\n", "\n");
        source = InsertBeforeRequiredMarker(
            source,
            "// NOTE: Startup legacy key migration is intentionally disabled.",
            "if err := ResolveCodexCliPlusSecretRefs(&cfg); err != nil {\n"
                + "\tif optional {\n"
                + "\t\treturn &Config{}, nil\n"
                + "\t}\n"
                + "\treturn nil, fmt.Errorf(\"failed to resolve CodexCliPlus secret refs: %w\", err)\n"
                + "}\n\n"
        );
        File.WriteAllText(configPath, source, new UTF8Encoding(false));

        var resolverPath = Path.Combine(
            sourceRoot,
            "internal",
            "config",
            "codexcliplus_secret_refs.go"
        );
        File.WriteAllText(resolverPath, SecretRefResolverGoSource, new UTF8Encoding(false));
    }

    private const string SecretRefResolverGoSource =
        """
        package config

        import (
            "encoding/json"
            "fmt"
            "net/http"
            "net/url"
            "os"
            "strings"
            "time"
        )

        const (
            codexCliPlusSecretBrokerURLEnv   = "CCP_SECRET_BROKER_URL"
            codexCliPlusSecretBrokerTokenEnv = "CCP_SECRET_BROKER_TOKEN"
        )

        type codexCliPlusSecretResolver struct {
            baseURL string
            token   string
            client  *http.Client
        }

        type codexCliPlusSecretResponse struct {
            Value string `json:"value"`
        }

        func newCodexCliPlusSecretResolver() *codexCliPlusSecretResolver {
            baseURL := strings.TrimRight(strings.TrimSpace(os.Getenv(codexCliPlusSecretBrokerURLEnv)), "/")
            token := strings.TrimSpace(os.Getenv(codexCliPlusSecretBrokerTokenEnv))
            if baseURL == "" || token == "" {
                return nil
            }
            return &codexCliPlusSecretResolver{
                baseURL: baseURL,
                token:   token,
                client:  &http.Client{Timeout: 5 * time.Second},
            }
        }

        func ResolveCodexCliPlusSecretRefs(cfg *Config) error {
            resolver := newCodexCliPlusSecretResolver()
            if cfg == nil || resolver == nil {
                return nil
            }

            var err error
            if cfg.RemoteManagement.SecretKey, err = resolver.resolve("remote-management.secret-key", cfg.RemoteManagement.SecretKey); err != nil {
                return err
            }
            for i := range cfg.APIKeys {
                if cfg.APIKeys[i], err = resolver.resolve(fmt.Sprintf("api-keys[%d]", i), cfg.APIKeys[i]); err != nil {
                    return err
                }
            }
            for i := range cfg.GeminiKey {
                if cfg.GeminiKey[i].APIKey, err = resolver.resolve(fmt.Sprintf("gemini-api-key[%d].api-key", i), cfg.GeminiKey[i].APIKey); err != nil {
                    return err
                }
                if err = resolver.resolveMap(fmt.Sprintf("gemini-api-key[%d].headers", i), cfg.GeminiKey[i].Headers); err != nil {
                    return err
                }
            }
            for i := range cfg.CodexKey {
                if cfg.CodexKey[i].APIKey, err = resolver.resolve(fmt.Sprintf("codex-api-key[%d].api-key", i), cfg.CodexKey[i].APIKey); err != nil {
                    return err
                }
                if err = resolver.resolveMap(fmt.Sprintf("codex-api-key[%d].headers", i), cfg.CodexKey[i].Headers); err != nil {
                    return err
                }
            }
            for i := range cfg.ClaudeKey {
                if cfg.ClaudeKey[i].APIKey, err = resolver.resolve(fmt.Sprintf("claude-api-key[%d].api-key", i), cfg.ClaudeKey[i].APIKey); err != nil {
                    return err
                }
                if err = resolver.resolveMap(fmt.Sprintf("claude-api-key[%d].headers", i), cfg.ClaudeKey[i].Headers); err != nil {
                    return err
                }
            }
            for i := range cfg.VertexCompatAPIKey {
                if cfg.VertexCompatAPIKey[i].APIKey, err = resolver.resolve(fmt.Sprintf("vertex-api-key[%d].api-key", i), cfg.VertexCompatAPIKey[i].APIKey); err != nil {
                    return err
                }
                if err = resolver.resolveMap(fmt.Sprintf("vertex-api-key[%d].headers", i), cfg.VertexCompatAPIKey[i].Headers); err != nil {
                    return err
                }
            }
            for i := range cfg.OpenAICompatibility {
                if err = resolver.resolveMap(fmt.Sprintf("openai-compatibility[%d].headers", i), cfg.OpenAICompatibility[i].Headers); err != nil {
                    return err
                }
                for j := range cfg.OpenAICompatibility[i].APIKeyEntries {
                    if cfg.OpenAICompatibility[i].APIKeyEntries[j].APIKey, err = resolver.resolve(fmt.Sprintf("openai-compatibility[%d].api-key-entries[%d].api-key", i, j), cfg.OpenAICompatibility[i].APIKeyEntries[j].APIKey); err != nil {
                        return err
                    }
                }
            }
            if cfg.AmpCode.UpstreamAPIKey, err = resolver.resolve("ampcode.upstream-api-key", cfg.AmpCode.UpstreamAPIKey); err != nil {
                return err
            }
            for i := range cfg.AmpCode.UpstreamAPIKeys {
                if cfg.AmpCode.UpstreamAPIKeys[i].UpstreamAPIKey, err = resolver.resolve(fmt.Sprintf("ampcode.upstream-api-keys[%d].upstream-api-key", i), cfg.AmpCode.UpstreamAPIKeys[i].UpstreamAPIKey); err != nil {
                    return err
                }
                for j := range cfg.AmpCode.UpstreamAPIKeys[i].APIKeys {
                    if cfg.AmpCode.UpstreamAPIKeys[i].APIKeys[j], err = resolver.resolve(fmt.Sprintf("ampcode.upstream-api-keys[%d].api-keys[%d]", i, j), cfg.AmpCode.UpstreamAPIKeys[i].APIKeys[j]); err != nil {
                        return err
                    }
                }
            }
            return nil
        }

        func (r *codexCliPlusSecretResolver) resolveMap(path string, values map[string]string) error {
            for key, value := range values {
                resolved, err := r.resolve(path+"."+key, value)
                if err != nil {
                    return err
                }
                values[key] = resolved
            }
            return nil
        }

        func (r *codexCliPlusSecretResolver) resolve(path, value string) (string, error) {
            secretID, ok := codexCliPlusSecretID(value)
            if !ok {
                return value, nil
            }
            requestURL := r.baseURL + "/v1/secrets/" + url.PathEscape(secretID)
            req, err := http.NewRequest(http.MethodGet, requestURL, nil)
            if err != nil {
                return "", err
            }
            req.Header.Set("Authorization", "Bearer "+r.token)

            resp, err := r.client.Do(req)
            if err != nil {
                return "", fmt.Errorf("secret_ref %s unavailable for %s: %w", secretID, path, err)
            }
            defer resp.Body.Close()
            if resp.StatusCode != http.StatusOK {
                return "", fmt.Errorf("secret_ref %s unavailable for %s: %s", secretID, path, resp.Status)
            }
            var payload codexCliPlusSecretResponse
            if err := json.NewDecoder(resp.Body).Decode(&payload); err != nil {
                return "", err
            }
            if strings.TrimSpace(payload.Value) == "" {
                return "", fmt.Errorf("secret_ref %s for %s resolved to empty value", secretID, path)
            }
            return payload.Value, nil
        }

        func codexCliPlusSecretID(value string) (string, bool) {
            trimmed := strings.TrimSpace(value)
            for _, prefix := range []string{"ccp-secret://", "vault://"} {
                if strings.HasPrefix(strings.ToLower(trimmed), prefix) {
                    id := strings.TrimSpace(trimmed[len(prefix):])
                    return id, id != ""
                }
            }
            return "", false
        }
        """;

    private static string ReplaceRequired(string source, string oldValue, string newValue)
    {
        if (!source.Contains(oldValue, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Pinned backend source no longer contains expected patch fragment: {oldValue}"
            );
        }

        return source.Replace(oldValue, newValue, StringComparison.Ordinal);
    }

    private static string InsertBeforeRequiredMarker(
        string source,
        string marker,
        string insertion
    )
    {
        var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new InvalidDataException(
                $"Pinned backend source no longer contains expected patch marker: {marker}"
            );
        }

        var lineStart = source.LastIndexOf('\n', markerIndex);
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var indent = source[lineStart..markerIndex];
        var indentedInsertion = string.Join(
            "\n",
            insertion.TrimEnd('\n')
                .Split('\n')
                .Select(line => line.Length == 0 ? line : indent + line)
        );
        return source.Insert(lineStart, indentedInsertion + "\n");
    }

    private static async Task DownloadBackendArchiveAsync(string backendTarget, BuildLogger logger)
    {
        Directory.CreateDirectory(backendTarget);

        using var httpClient = new HttpClient();
        byte[]? archiveBytes = null;
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                logger.Info($"backend archive download attempt {attempt}/3");
                archiveBytes = await httpClient.GetByteArrayAsync(
                    BackendReleaseMetadata.ArchiveUrl
                );
                break;
            }
            catch (Exception exception)
            {
                lastError = exception;
                logger.Error(
                    $"backend archive download attempt {attempt}/3 failed: {exception.Message}"
                );
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt));
                }
            }
        }

        if (archiveBytes is null)
        {
            throw new InvalidOperationException(
                $"Failed to download backend archive after 3 attempts: {lastError?.Message}",
                lastError
            );
        }

        var actualSha256 = Convert.ToHexStringLower(SHA256.HashData(archiveBytes));
        if (
            !string.Equals(
                actualSha256,
                BackendReleaseMetadata.ArchiveSha256,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new InvalidDataException(
                $"Downloaded backend archive hash mismatch. Expected {BackendReleaseMetadata.ArchiveSha256}, got {actualSha256}."
            );
        }

        using var archiveStream = new MemoryStream(archiveBytes);
        ExtractBackendArchive(archiveStream, backendTarget, logger);
    }

    private static void ExtractBackendArchive(
        Stream archiveStream,
        string backendTarget,
        BuildLogger logger
    )
    {
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        foreach (var requiredFile in RequiredBackendFiles)
        {
            var entry = archive.Entries.FirstOrDefault(item =>
                string.Equals(
                    item.Name,
                    requiredFile.ArchiveEntryName,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (entry is null)
            {
                throw new InvalidDataException(
                    $"Downloaded backend archive is missing {requiredFile.ArchiveEntryName}."
                );
            }

            entry.ExtractToFile(
                Path.Combine(backendTarget, requiredFile.TargetFileName),
                overwrite: true
            );
            logger.Info($"asset downloaded: backend/windows-x64/{requiredFile.TargetFileName}");
        }
    }

    public static IReadOnlyList<string> RequiredFiles => RequiredBackendFileNames;
}
