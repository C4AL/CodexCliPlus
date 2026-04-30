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
                Path.Combine("internal", "config", "codexcliplus_gpt_only.go"),
                Path.Combine("internal", "config", "codexcliplus_secret_refs.go"),
                Path.Combine("internal", "watcher", "synthesizer", "config.go"),
                Path.Combine("internal", "watcher", "synthesizer", "file.go"),
                Path.Combine("internal", "api", "handlers", "management", "config_basic.go"),
                Path.Combine("internal", "api", "handlers", "management", "auth_files.go"),
                Path.Combine("internal", "api", "server.go"),
                Path.Combine("internal", "cmd", "auth_manager.go"),
                Path.Combine("internal", "runtime", "executor", "openai_compat_executor.go"),
                Path.Combine(
                    "internal",
                    "runtime",
                    "executor",
                    "codexcliplus_prompt_cache_retention.go"
                ),
                Path.Combine(
                    "internal",
                    "runtime",
                    "executor",
                    "helps",
                    "thinking_providers.go"
                ),
                Path.Combine("internal", "translator", "init.go"),
                Path.Combine("cmd", "server", "main.go"),
                Path.Combine("sdk", "auth", "filestore.go"),
                Path.Combine("sdk", "auth", "refresh_registry.go"),
                Path.Combine("sdk", "cliproxy", "providers.go"),
                Path.Combine("sdk", "cliproxy", "service.go"),
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
        ApplyCodexGptOnlyBackendPatch(sourceRoot);

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

    private static void ApplyCodexGptOnlyBackendPatch(string sourceRoot)
    {
        PatchCodexGptOnlyConfig(sourceRoot);
        PatchCodexGptOnlyAuthSynthesis(sourceRoot);
        PatchCodexGptOnlyRuntimeRegistration(sourceRoot);
        PatchCodexGptOnlyServerRoutes(sourceRoot);
        PatchCodexGptOnlyCommandEntrypoints(sourceRoot);
        PatchOpenAICompatPromptCacheRetention(sourceRoot);
        File.WriteAllText(
            Path.Combine(
                sourceRoot,
                "internal",
                "runtime",
                "executor",
                "helps",
                "thinking_providers.go"
            ),
            CodexGptOnlyThinkingProvidersGoSource,
            new UTF8Encoding(false)
        );
        File.WriteAllText(
            Path.Combine(sourceRoot, "internal", "translator", "init.go"),
            CodexGptOnlyTranslatorInitGoSource,
            new UTF8Encoding(false)
        );
    }

    private static void PatchCodexGptOnlyConfig(string sourceRoot)
    {
        var configPath = Path.Combine(sourceRoot, "internal", "config", "config.go");
        var source = File.ReadAllText(configPath, Encoding.UTF8).Replace("\r\n", "\n");
        source = ReplaceRequired(
            source,
            "if err := ResolveCodexCliPlusSecretRefs(&cfg); err != nil {\n",
            "cfg.ApplyCodexCliPlusGPTOnlyConfig()\n\n\tif err := ResolveCodexCliPlusSecretRefs(&cfg); err != nil {\n"
        );
        source = ReplaceRequired(
            source,
            "\t// Normalize global OAuth model name aliases.\n\tcfg.SanitizeOAuthModelAlias()\n",
            "\t// Normalize global OAuth model name aliases.\n\tcfg.SanitizeOAuthModelAlias()\n\n\tcfg.ApplyCodexCliPlusGPTOnlyConfig()\n"
        );
        source = ReplaceRequired(
            source,
            "persistCfg := cfg\n"
                + "if protectedCfg, err := ProtectCodexCliPlusSecretRefsForWrite(cfg); err != nil {\n"
                + "\treturn fmt.Errorf(\"failed to protect CodexCliPlus secret refs before config write: %w\", err)\n"
                + "} else if protectedCfg != nil {\n"
                + "\tpersistCfg = protectedCfg\n"
                + "}\n",
            "persistCfg := cfg\n"
                + "if gptOnlyCfg, err := CodexCliPlusGPTOnlyConfigForWrite(cfg); err != nil {\n"
                + "\treturn fmt.Errorf(\"failed to sanitize CodexCliPlus GPT-only config before config write: %w\", err)\n"
                + "} else if gptOnlyCfg != nil {\n"
                + "\tpersistCfg = gptOnlyCfg\n"
                + "}\n"
                + "if protectedCfg, err := ProtectCodexCliPlusSecretRefsForWrite(persistCfg); err != nil {\n"
                + "\treturn fmt.Errorf(\"failed to protect CodexCliPlus secret refs before config write: %w\", err)\n"
                + "} else if protectedCfg != nil {\n"
                + "\tpersistCfg = protectedCfg\n"
                + "}\n"
        );
        source = ReplaceRequired(
            source,
            "\tremoveLegacyAmpKeys(original.Content[0])\n\tremoveLegacyGenerativeLanguageKeys(original.Content[0])\n",
            "\tremoveLegacyAmpKeys(original.Content[0])\n\tremoveLegacyGenerativeLanguageKeys(original.Content[0])\n\tPruneCodexCliPlusGPTOnlyConfigYAML(original.Content[0])\n"
        );
        File.WriteAllText(configPath, source, new UTF8Encoding(false));

        File.WriteAllText(
            Path.Combine(sourceRoot, "internal", "config", "codexcliplus_gpt_only.go"),
            CodexGptOnlyConfigGoSource,
            new UTF8Encoding(false)
        );

        var secretRefsPath = Path.Combine(
            sourceRoot,
            "internal",
            "config",
            "codexcliplus_secret_refs.go"
        );
        var secretRefs = File.ReadAllText(secretRefsPath, Encoding.UTF8).Replace("\r\n", "\n");
        secretRefs = ReplaceRequired(
            secretRefs,
            "protectedCfg, err := ProtectCodexCliPlusSecretRefsForWrite(&cfg)\n"
                + "\tif err != nil {\n"
                + "\t\treturn nil, err\n"
                + "\t}\n",
            "gptOnlyCfg, err := CodexCliPlusGPTOnlyConfigForWrite(&cfg)\n"
                + "\tif err != nil {\n"
                + "\t\treturn nil, err\n"
                + "\t}\n"
                + "\tprotectedCfg, err := ProtectCodexCliPlusSecretRefsForWrite(gptOnlyCfg)\n"
                + "\tif err != nil {\n"
                + "\t\treturn nil, err\n"
                + "\t}\n"
        );
        File.WriteAllText(secretRefsPath, secretRefs, new UTF8Encoding(false));
    }

    private static void PatchCodexGptOnlyAuthSynthesis(string sourceRoot)
    {
        var configSynthPath = Path.Combine(
            sourceRoot,
            "internal",
            "watcher",
            "synthesizer",
            "config.go"
        );
        var configSynth = File.ReadAllText(configSynthPath, Encoding.UTF8).Replace("\r\n", "\n");
        configSynth = ReplaceRequired(
            configSynth,
            "\t// Gemini API Keys\n"
                + "\tout = append(out, s.synthesizeGeminiKeys(ctx)...)\n"
                + "\t// Claude API Keys\n"
                + "\tout = append(out, s.synthesizeClaudeKeys(ctx)...)\n"
                + "\t// Codex API Keys\n"
                + "\tout = append(out, s.synthesizeCodexKeys(ctx)...)\n"
                + "\t// OpenAI-compat\n"
                + "\tout = append(out, s.synthesizeOpenAICompat(ctx)...)\n"
                + "\t// Vertex-compat\n"
                + "\tout = append(out, s.synthesizeVertexCompat(ctx)...)\n",
            "\t// CodexCliPlus keeps only Codex and OpenAI-compatible/GPT API-key auths.\n"
                + "\tout = append(out, s.synthesizeCodexKeys(ctx)...)\n"
                + "\tout = append(out, s.synthesizeOpenAICompat(ctx)...)\n"
        );
        File.WriteAllText(configSynthPath, configSynth, new UTF8Encoding(false));

        var fileSynthPath = Path.Combine(
            sourceRoot,
            "internal",
            "watcher",
            "synthesizer",
            "file.go"
        );
        var fileSynth = File.ReadAllText(fileSynthPath, Encoding.UTF8).Replace("\r\n", "\n");
        fileSynth = ReplaceRequired(
            fileSynth,
            "\tif provider == \"gemini\" {\n\t\tprovider = \"gemini-cli\"\n\t}\n",
            "\tif provider == \"gemini\" {\n\t\tprovider = \"gemini-cli\"\n\t}\n\tif provider != \"codex\" {\n\t\treturn nil\n\t}\n"
        );
        File.WriteAllText(fileSynthPath, fileSynth, new UTF8Encoding(false));
    }

    private static void PatchCodexGptOnlyRuntimeRegistration(string sourceRoot)
    {
        var providersPath = Path.Combine(sourceRoot, "sdk", "cliproxy", "providers.go");
        var providers = File.ReadAllText(providersPath, Encoding.UTF8).Replace("\r\n", "\n");
        providers = ReplaceRequired(
            providers,
            "\tgeminiCount, vertexCompatCount, claudeCount, codexCount, openAICompat := watcher.BuildAPIKeyClients(cfg)\n",
            "\t_, _, _, codexCount, openAICompat := watcher.BuildAPIKeyClients(cfg)\n\tgeminiCount, vertexCompatCount, claudeCount := 0, 0, 0\n"
        );
        File.WriteAllText(providersPath, providers, new UTF8Encoding(false));

        var servicePath = Path.Combine(sourceRoot, "sdk", "cliproxy", "service.go");
        var service = File.ReadAllText(servicePath, Encoding.UTF8).Replace("\r\n", "\n");
        service = ReplaceRequired(
            service,
            "func newDefaultAuthManager() *sdkAuth.Manager {\n"
                + "\treturn sdkAuth.NewManager(\n"
                + "\t\tsdkAuth.GetTokenStore(),\n"
                + "\t\tsdkAuth.NewGeminiAuthenticator(),\n"
                + "\t\tsdkAuth.NewCodexAuthenticator(),\n"
                + "\t\tsdkAuth.NewClaudeAuthenticator(),\n"
                + "\t)\n"
                + "}\n",
            "func newDefaultAuthManager() *sdkAuth.Manager {\n"
                + "\treturn sdkAuth.NewManager(\n"
                + "\t\tsdkAuth.GetTokenStore(),\n"
                + "\t\tsdkAuth.NewCodexAuthenticator(),\n"
                + "\t)\n"
                + "}\n"
        );
        service = ReplaceRequired(
            service,
            "\tswitch strings.ToLower(a.Provider) {\n"
                + "\tcase \"gemini\":\n"
                + "\t\ts.coreManager.RegisterExecutor(executor.NewGeminiExecutor(s.cfg))\n"
                + "\tcase \"vertex\":\n"
                + "\t\ts.coreManager.RegisterExecutor(executor.NewGeminiVertexExecutor(s.cfg))\n"
                + "\tcase \"gemini-cli\":\n"
                + "\t\ts.coreManager.RegisterExecutor(executor.NewGeminiCLIExecutor(s.cfg))\n"
                + "\tcase \"aistudio\":\n"
                + "\t\tif s.wsGateway != nil {\n"
                + "\t\t\ts.coreManager.RegisterExecutor(executor.NewAIStudioExecutor(s.cfg, a.ID, s.wsGateway))\n"
                + "\t\t}\n"
                + "\t\treturn\n"
                + "\tcase \"antigravity\":\n"
                + "\t\ts.coreManager.RegisterExecutor(executor.NewAntigravityExecutor(s.cfg))\n"
                + "\tcase \"claude\":\n"
                + "\t\ts.coreManager.RegisterExecutor(executor.NewClaudeExecutor(s.cfg))\n"
                + "\tcase \"kimi\":\n"
                + "\t\ts.coreManager.RegisterExecutor(executor.NewKimiExecutor(s.cfg))\n"
                + "\tdefault:\n"
                + "\t\tproviderKey := strings.ToLower(strings.TrimSpace(a.Provider))\n"
                + "\t\tif providerKey == \"\" {\n"
                + "\t\t\tproviderKey = \"openai-compatibility\"\n"
                + "\t\t}\n"
                + "\t\ts.coreManager.RegisterExecutor(executor.NewOpenAICompatExecutor(providerKey, s.cfg))\n"
                + "\t}\n",
            "\t// Non-Codex providers are intentionally disabled in the CodexCliPlus GPT-only build.\n\treturn\n"
        );
        service = ReplaceRequired(
            service,
            "\ts.ensureWebsocketGateway()\n\tif s.server != nil && s.wsGateway != nil {\n",
            "\t// CodexCliPlus GPT-only builds do not expose AI Studio websocket providers.\n\tif false && s.server != nil && s.wsGateway != nil {\n"
        );
        File.WriteAllText(servicePath, service, new UTF8Encoding(false));

        var cmdAuthManagerPath = Path.Combine(sourceRoot, "internal", "cmd", "auth_manager.go");
        var cmdAuthManager = File.ReadAllText(cmdAuthManagerPath, Encoding.UTF8).Replace("\r\n", "\n");
        cmdAuthManager = ReplaceRequired(
            cmdAuthManager,
            "\tmanager := sdkAuth.NewManager(store,\n"
                + "\t\tsdkAuth.NewGeminiAuthenticator(),\n"
                + "\t\tsdkAuth.NewCodexAuthenticator(),\n"
                + "\t\tsdkAuth.NewClaudeAuthenticator(),\n"
                + "\t\tsdkAuth.NewAntigravityAuthenticator(),\n"
                + "\t\tsdkAuth.NewKimiAuthenticator(),\n"
                + "\t)\n",
            "\tmanager := sdkAuth.NewManager(store,\n"
                + "\t\tsdkAuth.NewCodexAuthenticator(),\n"
                + "\t)\n"
        );
        File.WriteAllText(cmdAuthManagerPath, cmdAuthManager, new UTF8Encoding(false));

        var refreshRegistryPath = Path.Combine(sourceRoot, "sdk", "auth", "refresh_registry.go");
        var refreshRegistry = File.ReadAllText(refreshRegistryPath, Encoding.UTF8).Replace("\r\n", "\n");
        refreshRegistry = ReplaceFunctionBlockRequired(
            refreshRegistry,
            "func init() {",
            "func init() {\n"
                + "\tregisterRefreshLead(\"codex\", func() Authenticator { return NewCodexAuthenticator() })\n"
                + "}\n"
        );
        File.WriteAllText(refreshRegistryPath, refreshRegistry, new UTF8Encoding(false));
    }

    private static void PatchCodexGptOnlyServerRoutes(string sourceRoot)
    {
        var serverPath = Path.Combine(sourceRoot, "internal", "api", "server.go");
        var server = File.ReadAllText(serverPath, Encoding.UTF8).Replace("\r\n", "\n");
        server = ReplaceRequired(
            server,
            "\t// Register Amp module using V2 interface with Context\n"
                + "\ts.ampModule = ampmodule.NewLegacy(accessManager, AuthMiddleware(accessManager))\n"
                + "\tctx := modules.Context{\n"
                + "\t\tEngine:         engine,\n"
                + "\t\tBaseHandler:    s.handlers,\n"
                + "\t\tConfig:         cfg,\n"
                + "\t\tAuthMiddleware: AuthMiddleware(accessManager),\n"
                + "\t}\n"
                + "\tif err := modules.RegisterModule(ctx, s.ampModule); err != nil {\n"
                + "\t\tlog.Errorf(\"Failed to register Amp module: %v\", err)\n"
                + "\t}\n",
            "\t// Amp module routes are disabled in the CodexCliPlus GPT-only build.\n"
                + "\tif false {\n"
                + "\t\ts.ampModule = ampmodule.NewLegacy(accessManager, AuthMiddleware(accessManager))\n"
                + "\t\tctx := modules.Context{\n"
                + "\t\t\tEngine:         engine,\n"
                + "\t\t\tBaseHandler:    s.handlers,\n"
                + "\t\t\tConfig:         cfg,\n"
                + "\t\t\tAuthMiddleware: AuthMiddleware(accessManager),\n"
                + "\t\t}\n"
                + "\t\tif err := modules.RegisterModule(ctx, s.ampModule); err != nil {\n"
                + "\t\t\tlog.Errorf(\"Failed to register Amp module: %v\", err)\n"
                + "\t\t}\n"
                + "\t}\n"
        );
        server = ReplaceRequired(
            server,
            "\t\tv1.GET(\"/models\", s.unifiedModelsHandler(openaiHandlers, claudeCodeHandlers))\n",
            "\t\tv1.GET(\"/models\", s.unifiedModelsHandler(openaiHandlers, claudeCodeHandlers))\n"
                + "\t\t_ = geminiHandlers\n"
                + "\t\t_ = geminiCLIHandlers\n"
        );
        server = ReplaceRequired(
            server,
            "\t\tv1.POST(\"/messages\", claudeCodeHandlers.ClaudeMessages)\n"
                + "\t\tv1.POST(\"/messages/count_tokens\", claudeCodeHandlers.ClaudeCountTokens)\n",
            ""
        );
        server = ReplaceRequired(
            server,
            "\t// Gemini compatible API routes\n"
                + "\tv1beta := s.engine.Group(\"/v1beta\")\n"
                + "\tv1beta.Use(AuthMiddleware(s.accessManager))\n"
                + "\t{\n"
                + "\t\tv1beta.GET(\"/models\", geminiHandlers.GeminiModels)\n"
                + "\t\tv1beta.POST(\"/models/*action\", geminiHandlers.GeminiHandler)\n"
                + "\t\tv1beta.GET(\"/models/*action\", geminiHandlers.GeminiGetHandler)\n"
                + "\t}\n\n",
            "\t// Gemini-compatible API routes are disabled in the CodexCliPlus GPT-only build.\n\n"
        );
        server = ReplaceRequired(
            server,
            "\ts.engine.POST(\"/v1internal:method\", geminiCLIHandlers.CLIHandler)\n\n",
            ""
        );
        server = RemoveGinRouteBlockRequired(server, "s.engine.GET(\"/anthropic/callback\"");
        server = RemoveGinRouteBlockRequired(server, "s.engine.GET(\"/google/callback\"");
        server = RemoveGinRouteBlockRequired(server, "s.engine.GET(\"/antigravity/callback\"");
        server = RemoveLineBlockRequired(
            server,
            "mgmt.GET(\"/gemini-api-key\"",
            "mgmt.DELETE(\"/gemini-api-key\""
        );
        server = RemoveLineBlockRequired(
            server,
            "mgmt.GET(\"/ampcode\"",
            "mgmt.DELETE(\"/ampcode/upstream-api-keys\""
        );
        server = RemoveLineBlockRequired(
            server,
            "mgmt.GET(\"/claude-api-key\"",
            "mgmt.DELETE(\"/claude-api-key\""
        );
        server = RemoveLineBlockRequired(
            server,
            "mgmt.GET(\"/vertex-api-key\"",
            "mgmt.DELETE(\"/vertex-api-key\""
        );
        server = RemoveLineBlockRequired(
            server,
            "mgmt.POST(\"/vertex/import\"",
            "mgmt.POST(\"/vertex/import\""
        );
        server = RemoveLineBlockRequired(
            server,
            "mgmt.GET(\"/anthropic-auth-url\"",
            "mgmt.GET(\"/kimi-auth-url\"",
            "\t\tmgmt.GET(\"/codex-auth-url\", s.mgmt.RequestCodexToken)\n"
        );
        server = ReplaceRequired(
            server,
            "\t\tuserAgent := c.GetHeader(\"User-Agent\")\n\n"
                + "\t\t// Route to Claude handler if User-Agent starts with \"claude-cli\"\n"
                + "\t\tif strings.HasPrefix(userAgent, \"claude-cli\") {\n"
                + "\t\t\t// log.Debugf(\"Routing /v1/models to Claude handler for User-Agent: %s\", userAgent)\n"
                + "\t\t\tclaudeHandler.ClaudeModels(c)\n"
                + "\t\t} else {\n"
                + "\t\t\t// log.Debugf(\"Routing /v1/models to OpenAI handler for User-Agent: %s\", userAgent)\n"
                + "\t\t\topenaiHandler.OpenAIModels(c)\n"
                + "\t\t}\n",
            "\t\t_ = claudeHandler\n\t\topenaiHandler.OpenAIModels(c)\n"
        );
        File.WriteAllText(serverPath, server, new UTF8Encoding(false));
    }

    private static void PatchCodexGptOnlyCommandEntrypoints(string sourceRoot)
    {
        var mainPath = Path.Combine(sourceRoot, "cmd", "server", "main.go");
        var main = File.ReadAllText(mainPath, Encoding.UTF8).Replace("\r\n", "\n");
        main = ReplaceRequired(
            main,
            "\tif vertexImport != \"\" {\n"
                + "\t\t// Handle Vertex service account import\n"
                + "\t\tcmd.DoVertexImport(cfg, vertexImport, vertexImportPrefix)\n"
                + "\t} else if login {\n"
                + "\t\t// Handle Google/Gemini login\n"
                + "\t\tcmd.DoLogin(cfg, projectID, options)\n"
                + "\t} else if antigravityLogin {\n"
                + "\t\t// Handle Antigravity login\n"
                + "\t\tcmd.DoAntigravityLogin(cfg, options)\n"
                + "\t} else if codexLogin {\n",
            "\tif vertexImport != \"\" || login || antigravityLogin || claudeLogin || kimiLogin {\n"
                + "\t\t_ = vertexImportPrefix\n"
                + "\t\t_ = projectID\n"
                + "\t\tlog.Error(\"unsupported provider command disabled in CodexCliPlus GPT-only build\")\n"
                + "\t\treturn\n"
                + "\t} else if codexLogin {\n"
        );
        main = ReplaceRequired(
            main,
            "\t} else if claudeLogin {\n"
                + "\t\t// Handle Claude login\n"
                + "\t\tcmd.DoClaudeLogin(cfg, options)\n"
                + "\t} else if kimiLogin {\n"
                + "\t\tcmd.DoKimiLogin(cfg, options)\n",
            ""
        );
        main = ReplaceRequired(
            main,
            "misc.StartAntigravityVersionUpdater(context.Background())\n",
            ""
        );
        File.WriteAllText(mainPath, main, new UTF8Encoding(false));
    }

    private static void PatchOpenAICompatPromptCacheRetention(string sourceRoot)
    {
        File.WriteAllText(
            Path.Combine(
                sourceRoot,
                "internal",
                "runtime",
                "executor",
                "codexcliplus_prompt_cache_retention.go"
            ),
            CodexGptOnlyPromptCacheRetentionGoSource,
            new UTF8Encoding(false)
        );

        var executorPath = Path.Combine(
            sourceRoot,
            "internal",
            "runtime",
            "executor",
            "openai_compat_executor.go"
        );
        var source = File.ReadAllText(executorPath, Encoding.UTF8).Replace("\r\n", "\n");
        source = ReplaceRequired(
            source,
            "\ttranslated = helps.ApplyPayloadConfigWithRoot(e.cfg, baseModel, to.String(), \"\", translated, originalTranslated, requestedModel, requestPath)\n"
                + "\tif opts.Alt == \"responses/compact\" {\n",
            "\ttranslated = helps.ApplyPayloadConfigWithRoot(e.cfg, baseModel, to.String(), \"\", translated, originalTranslated, requestedModel, requestPath)\n"
                + "\tcacheRetentionAutoApplied := false\n"
                + "\ttranslated, cacheRetentionAutoApplied = codexCliPlusApplyPromptCacheRetention(translated, baseModel, to.String())\n"
                + "\tif opts.Alt == \"responses/compact\" {\n"
        );
        source = ReplaceRequired(
            source,
            "\tdefer func() {\n"
                + "\t\tif errClose := httpResp.Body.Close(); errClose != nil {\n"
                + "\t\t\tlog.Errorf(\"openai compat executor: close response body error: %v\", errClose)\n"
                + "\t\t}\n"
                + "\t}()\n",
            "\tdefer func() {\n"
                + "\t\tif httpResp == nil || httpResp.Body == nil {\n"
                + "\t\t\treturn\n"
                + "\t\t}\n"
                + "\t\tif errClose := httpResp.Body.Close(); errClose != nil {\n"
                + "\t\t\tlog.Errorf(\"openai compat executor: close response body error: %v\", errClose)\n"
                + "\t\t}\n"
                + "\t}()\n"
        );
        source = ReplaceRequired(
            source,
            "\tif httpResp.StatusCode < 200 || httpResp.StatusCode >= 300 {\n"
                + "\t\tb, _ := io.ReadAll(httpResp.Body)\n"
                + "\t\thelps.AppendAPIResponseChunk(ctx, e.cfg, b)\n"
                + "\t\thelps.LogWithRequestID(ctx).Debugf(\"request error, error status: %d, error message: %s\", httpResp.StatusCode, helps.SummarizeErrorBody(httpResp.Header.Get(\"Content-Type\"), b))\n"
                + "\t\terr = statusErr{code: httpResp.StatusCode, msg: string(b)}\n"
                + "\t\treturn resp, err\n"
                + "\t}\n",
            "\tif httpResp.StatusCode < 200 || httpResp.StatusCode >= 300 {\n"
                + "\t\tb, _ := io.ReadAll(httpResp.Body)\n"
                + "\t\thelps.AppendAPIResponseChunk(ctx, e.cfg, b)\n"
                + "\t\thelps.LogWithRequestID(ctx).Debugf(\"request error, error status: %d, error message: %s\", httpResp.StatusCode, helps.SummarizeErrorBody(httpResp.Header.Get(\"Content-Type\"), b))\n"
                + "\t\tif cacheRetentionAutoApplied && codexCliPlusShouldRetryWithoutPromptCacheRetention(httpResp.StatusCode, b) {\n"
                + "\t\t\tif errClose := httpResp.Body.Close(); errClose != nil {\n"
                + "\t\t\t\tlog.Errorf(\"openai compat executor: close response body error: %v\", errClose)\n"
                + "\t\t\t}\n"
                + "\t\t\tretryPayload := codexCliPlusRemovePromptCacheRetention(translated)\n"
                + "\t\t\tretryReq, errRetryReq := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(retryPayload))\n"
                + "\t\t\tif errRetryReq == nil {\n"
                + "\t\t\t\tretryReq.Header = httpReq.Header.Clone()\n"
                + "\t\t\t\thelps.RecordAPIRequest(ctx, e.cfg, helps.UpstreamRequestLog{\n"
                + "\t\t\t\t\tURL:       url,\n"
                + "\t\t\t\t\tMethod:    http.MethodPost,\n"
                + "\t\t\t\t\tHeaders:   retryReq.Header.Clone(),\n"
                + "\t\t\t\t\tBody:      retryPayload,\n"
                + "\t\t\t\t\tProvider:  e.Identifier(),\n"
                + "\t\t\t\t\tAuthID:    authID,\n"
                + "\t\t\t\t\tAuthLabel: authLabel,\n"
                + "\t\t\t\t\tAuthType:  authType,\n"
                + "\t\t\t\t\tAuthValue: authValue,\n"
                + "\t\t\t\t})\n"
                + "\t\t\t\thttpResp = nil\n"
                + "\t\t\t\thttpResp, err = httpClient.Do(retryReq)\n"
                + "\t\t\t\tif err != nil {\n"
                + "\t\t\t\t\thelps.RecordAPIResponseError(ctx, e.cfg, err)\n"
                + "\t\t\t\t\treturn resp, err\n"
                + "\t\t\t\t}\n"
                + "\t\t\t\thelps.RecordAPIResponseMetadata(ctx, e.cfg, httpResp.StatusCode, httpResp.Header.Clone())\n"
                + "\t\t\t\tif httpResp.StatusCode >= 200 && httpResp.StatusCode < 300 {\n"
                + "\t\t\t\t\ttranslated = retryPayload\n"
                + "\t\t\t\t} else {\n"
                + "\t\t\t\t\tb, _ = io.ReadAll(httpResp.Body)\n"
                + "\t\t\t\t\thelps.AppendAPIResponseChunk(ctx, e.cfg, b)\n"
                + "\t\t\t\t\thelps.LogWithRequestID(ctx).Debugf(\"request error, error status: %d, error message: %s\", httpResp.StatusCode, helps.SummarizeErrorBody(httpResp.Header.Get(\"Content-Type\"), b))\n"
                + "\t\t\t\t\terr = statusErr{code: httpResp.StatusCode, msg: string(b)}\n"
                + "\t\t\t\t\treturn resp, err\n"
                + "\t\t\t\t}\n"
                + "\t\t\t}\n"
                + "\t\t}\n"
                + "\t\tif httpResp.StatusCode < 200 || httpResp.StatusCode >= 300 {\n"
                + "\t\t\terr = statusErr{code: httpResp.StatusCode, msg: string(b)}\n"
                + "\t\t\treturn resp, err\n"
                + "\t\t}\n"
                + "\t}\n"
        );
        source = ReplaceRequired(
            source,
            "\ttranslated = helps.ApplyPayloadConfigWithRoot(e.cfg, baseModel, to.String(), \"\", translated, originalTranslated, requestedModel, requestPath)\n\n"
                + "\ttranslated, err = thinking.ApplyThinking(translated, req.Model, from.String(), to.String(), e.Identifier())\n",
            "\ttranslated = helps.ApplyPayloadConfigWithRoot(e.cfg, baseModel, to.String(), \"\", translated, originalTranslated, requestedModel, requestPath)\n"
                + "\tcacheRetentionAutoApplied := false\n"
                + "\ttranslated, cacheRetentionAutoApplied = codexCliPlusApplyPromptCacheRetention(translated, baseModel, to.String())\n\n"
                + "\ttranslated, err = thinking.ApplyThinking(translated, req.Model, from.String(), to.String(), e.Identifier())\n"
        );
        source = ReplaceRequired(
            source,
            "\tif httpResp.StatusCode < 200 || httpResp.StatusCode >= 300 {\n"
                + "\t\tb, _ := io.ReadAll(httpResp.Body)\n"
                + "\t\thelps.AppendAPIResponseChunk(ctx, e.cfg, b)\n"
                + "\t\thelps.LogWithRequestID(ctx).Debugf(\"request error, error status: %d, error message: %s\", httpResp.StatusCode, helps.SummarizeErrorBody(httpResp.Header.Get(\"Content-Type\"), b))\n"
                + "\t\tif errClose := httpResp.Body.Close(); errClose != nil {\n"
                + "\t\t\tlog.Errorf(\"openai compat executor: close response body error: %v\", errClose)\n"
                + "\t\t}\n"
                + "\t\terr = statusErr{code: httpResp.StatusCode, msg: string(b)}\n"
                + "\t\treturn nil, err\n"
                + "\t}\n",
            "\tif httpResp.StatusCode < 200 || httpResp.StatusCode >= 300 {\n"
                + "\t\tb, _ := io.ReadAll(httpResp.Body)\n"
                + "\t\thelps.AppendAPIResponseChunk(ctx, e.cfg, b)\n"
                + "\t\thelps.LogWithRequestID(ctx).Debugf(\"request error, error status: %d, error message: %s\", httpResp.StatusCode, helps.SummarizeErrorBody(httpResp.Header.Get(\"Content-Type\"), b))\n"
                + "\t\tif cacheRetentionAutoApplied && codexCliPlusShouldRetryWithoutPromptCacheRetention(httpResp.StatusCode, b) {\n"
                + "\t\t\tif errClose := httpResp.Body.Close(); errClose != nil {\n"
                + "\t\t\t\tlog.Errorf(\"openai compat executor: close response body error: %v\", errClose)\n"
                + "\t\t\t}\n"
                + "\t\t\tretryPayload := codexCliPlusRemovePromptCacheRetention(translated)\n"
                + "\t\t\tretryReq, errRetryReq := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(retryPayload))\n"
                + "\t\t\tif errRetryReq == nil {\n"
                + "\t\t\t\tretryReq.Header = httpReq.Header.Clone()\n"
                + "\t\t\t\tretryReq.Header.Set(\"Accept\", \"text/event-stream\")\n"
                + "\t\t\t\tretryReq.Header.Set(\"Cache-Control\", \"no-cache\")\n"
                + "\t\t\t\thelps.RecordAPIRequest(ctx, e.cfg, helps.UpstreamRequestLog{\n"
                + "\t\t\t\t\tURL:       url,\n"
                + "\t\t\t\t\tMethod:    http.MethodPost,\n"
                + "\t\t\t\t\tHeaders:   retryReq.Header.Clone(),\n"
                + "\t\t\t\t\tBody:      retryPayload,\n"
                + "\t\t\t\t\tProvider:  e.Identifier(),\n"
                + "\t\t\t\t\tAuthID:    authID,\n"
                + "\t\t\t\t\tAuthLabel: authLabel,\n"
                + "\t\t\t\t\tAuthType:  authType,\n"
                + "\t\t\t\t\tAuthValue: authValue,\n"
                + "\t\t\t\t})\n"
                + "\t\t\t\thttpResp, err = httpClient.Do(retryReq)\n"
                + "\t\t\t\tif err != nil {\n"
                + "\t\t\t\t\thelps.RecordAPIResponseError(ctx, e.cfg, err)\n"
                + "\t\t\t\t\treturn nil, err\n"
                + "\t\t\t\t}\n"
                + "\t\t\t\thelps.RecordAPIResponseMetadata(ctx, e.cfg, httpResp.StatusCode, httpResp.Header.Clone())\n"
                + "\t\t\t\tif httpResp.StatusCode >= 200 && httpResp.StatusCode < 300 {\n"
                + "\t\t\t\t\ttranslated = retryPayload\n"
                + "\t\t\t\t} else {\n"
                + "\t\t\t\t\tb, _ = io.ReadAll(httpResp.Body)\n"
                + "\t\t\t\t\thelps.AppendAPIResponseChunk(ctx, e.cfg, b)\n"
                + "\t\t\t\t\thelps.LogWithRequestID(ctx).Debugf(\"request error, error status: %d, error message: %s\", httpResp.StatusCode, helps.SummarizeErrorBody(httpResp.Header.Get(\"Content-Type\"), b))\n"
                + "\t\t\t\t\tif errClose := httpResp.Body.Close(); errClose != nil {\n"
                + "\t\t\t\t\t\tlog.Errorf(\"openai compat executor: close response body error: %v\", errClose)\n"
                + "\t\t\t\t\t}\n"
                + "\t\t\t\t\terr = statusErr{code: httpResp.StatusCode, msg: string(b)}\n"
                + "\t\t\t\t\treturn nil, err\n"
                + "\t\t\t\t}\n"
                + "\t\t\t}\n"
                + "\t\t}\n"
                + "\t\tif httpResp.StatusCode < 200 || httpResp.StatusCode >= 300 {\n"
                + "\t\t\tif errClose := httpResp.Body.Close(); errClose != nil {\n"
                + "\t\t\t\tlog.Errorf(\"openai compat executor: close response body error: %v\", errClose)\n"
                + "\t\t\t}\n"
                + "\t\t\terr = statusErr{code: httpResp.StatusCode, msg: string(b)}\n"
                + "\t\t\treturn nil, err\n"
                + "\t\t}\n"
                + "\t}\n"
        );
        File.WriteAllText(executorPath, source, new UTF8Encoding(false));
    }

    private const string CodexGptOnlyConfigGoSource =
        """
        package config

        import (
            "strings"

            "gopkg.in/yaml.v3"
        )

        func (cfg *Config) ApplyCodexCliPlusGPTOnlyConfig() {
            if cfg == nil {
                return
            }

            cfg.GeminiKey = nil
            cfg.ClaudeKey = nil
            cfg.VertexCompatAPIKey = nil
            cfg.AmpCode = AmpCode{}
            cfg.ClaudeHeaderDefaults = ClaudeHeaderDefaults{}
            cfg.AntigravitySignatureCacheEnabled = nil
            cfg.AntigravitySignatureBypassStrict = nil
            cfg.QuotaExceeded.AntigravityCredits = false
            cfg.OAuthExcludedModels = codexCliPlusKeepGPTOnlyStringSlices(cfg.OAuthExcludedModels)
            cfg.OAuthModelAlias = codexCliPlusKeepGPTOnlyModelAliases(cfg.OAuthModelAlias)
        }

        func CodexCliPlusGPTOnlyConfigForWrite(cfg *Config) (*Config, error) {
            if cfg == nil {
                return cfg, nil
            }
            data, err := yaml.Marshal(cfg)
            if err != nil {
                return nil, err
            }
            var out Config
            if err := yaml.Unmarshal(data, &out); err != nil {
                return nil, err
            }
            out.ApplyCodexCliPlusGPTOnlyConfig()
            return &out, nil
        }

        func PruneCodexCliPlusGPTOnlyConfigYAML(root *yaml.Node) {
            if root == nil || root.Kind != yaml.MappingNode {
                return
            }

            for _, key := range []string{
                "gemini-api-key",
                "claude-api-key",
                "vertex-api-key",
                "ampcode",
                "claude-header-defaults",
                "antigravity-signature-cache-enabled",
                "antigravity-signature-bypass-strict",
                "generative-language-api-key",
                "amp-upstream-url",
                "amp-upstream-api-key",
                "amp-restrict-management-to-localhost",
                "amp-model-mappings",
            } {
                removeMapKey(root, key)
            }

            if idx := findMapKeyIndex(root, "quota-exceeded"); idx >= 0 && idx+1 < len(root.Content) {
                quota := root.Content[idx+1]
                removeMapKey(quota, "antigravity-credits")
                if quota == nil || quota.Kind != yaml.MappingNode || len(quota.Content) == 0 {
                    removeMapKey(root, "quota-exceeded")
                }
            }

            codexCliPlusPruneGPTOnlyProviderMapping(root, "oauth-excluded-models")
            codexCliPlusPruneGPTOnlyProviderMapping(root, "oauth-model-alias")
        }

        func codexCliPlusKeepGPTOnlyStringSlices(in map[string][]string) map[string][]string {
            if len(in) == 0 {
                return nil
            }
            out := make(map[string][]string, len(in))
            for provider, models := range in {
                key := strings.ToLower(strings.TrimSpace(provider))
                if !codexCliPlusGPTOnlyProviderAllowed(key) || len(models) == 0 {
                    continue
                }
                out[key] = models
            }
            if len(out) == 0 {
                return nil
            }
            return out
        }

        func codexCliPlusKeepGPTOnlyModelAliases(in map[string][]OAuthModelAlias) map[string][]OAuthModelAlias {
            if len(in) == 0 {
                return nil
            }
            out := make(map[string][]OAuthModelAlias, len(in))
            for provider, aliases := range in {
                key := strings.ToLower(strings.TrimSpace(provider))
                if !codexCliPlusGPTOnlyProviderAllowed(key) || len(aliases) == 0 {
                    continue
                }
                out[key] = aliases
            }
            if len(out) == 0 {
                return nil
            }
            return out
        }

        func codexCliPlusPruneGPTOnlyProviderMapping(root *yaml.Node, key string) {
            idx := findMapKeyIndex(root, key)
            if idx < 0 || idx+1 >= len(root.Content) {
                return
            }
            value := root.Content[idx+1]
            if value == nil || value.Kind != yaml.MappingNode {
                removeMapKey(root, key)
                return
            }
            for i := 0; i+1 < len(value.Content); {
                keyNode := value.Content[i]
                if keyNode == nil || !codexCliPlusGPTOnlyProviderAllowed(keyNode.Value) {
                    value.Content = append(value.Content[:i], value.Content[i+2:]...)
                    continue
                }
                i += 2
            }
            if len(value.Content) == 0 {
                removeMapKey(root, key)
            }
        }

        func codexCliPlusGPTOnlyProviderAllowed(provider string) bool {
            switch strings.ToLower(strings.TrimSpace(provider)) {
            case "codex", "openai", "openai-compatibility":
                return true
            default:
                return false
            }
        }
        """;

    private const string CodexGptOnlyPromptCacheRetentionGoSource =
        """
        package executor

        import (
            "strings"

            "github.com/tidwall/gjson"
            "github.com/tidwall/sjson"
        )

        func codexCliPlusApplyPromptCacheRetention(payload []byte, fallbackModel string, protocol string) ([]byte, bool) {
            normalizedProtocol := strings.ToLower(strings.TrimSpace(protocol))
            if normalizedProtocol != "openai" && normalizedProtocol != "openai-response" {
                return payload, false
            }
            if len(payload) == 0 {
                return payload, false
            }

            existing := gjson.GetBytes(payload, "prompt_cache_retention")
            if existing.Exists() {
                mode := strings.ToLower(strings.TrimSpace(existing.String()))
                if mode != "auto" {
                    return payload, false
                }
                model := codexCliPlusPromptCacheModel(payload, fallbackModel)
                if !codexCliPlusSupportsExtendedPromptCache(model) {
                    updated, err := sjson.DeleteBytes(payload, "prompt_cache_retention")
                    if err != nil {
                        return payload, false
                    }
                    return updated, false
                }
                updated, err := sjson.SetBytes(payload, "prompt_cache_retention", "24h")
                if err != nil {
                    return payload, false
                }
                return updated, true
            }

            model := codexCliPlusPromptCacheModel(payload, fallbackModel)
            if !codexCliPlusSupportsExtendedPromptCache(model) {
                return payload, false
            }
            updated, err := sjson.SetBytes(payload, "prompt_cache_retention", "24h")
            if err != nil {
                return payload, false
            }
            return updated, true
        }

        func codexCliPlusPromptCacheModel(payload []byte, fallbackModel string) string {
            if model := strings.TrimSpace(gjson.GetBytes(payload, "model").String()); model != "" {
                return model
            }
            return strings.TrimSpace(fallbackModel)
        }

        func codexCliPlusSupportsExtendedPromptCache(model string) bool {
            normalized := strings.ToLower(strings.TrimSpace(model))
            return strings.HasPrefix(normalized, "gpt-5.1") ||
                strings.HasPrefix(normalized, "gpt-5") ||
                strings.HasPrefix(normalized, "gpt-4.1")
        }

        func codexCliPlusShouldRetryWithoutPromptCacheRetention(status int, body []byte) bool {
            if status < 400 || len(body) == 0 {
                return false
            }
            text := strings.ToLower(string(body))
            if !strings.Contains(text, "prompt_cache_retention") {
                return false
            }
            for _, marker := range []string{
                "unsupported",
                "not supported",
                "unknown parameter",
                "unrecognized",
                "invalid",
                "unsupported_parameter",
                "invalid_request_error",
            } {
                if strings.Contains(text, marker) {
                    return true
                }
            }
            return false
        }

        func codexCliPlusRemovePromptCacheRetention(payload []byte) []byte {
            updated, err := sjson.DeleteBytes(payload, "prompt_cache_retention")
            if err != nil {
                return payload
            }
            return updated
        }
        """;

    private const string CodexGptOnlyTranslatorInitGoSource =
        """
        package translator

        import (
            _ "github.com/router-for-me/CLIProxyAPI/v6/internal/translator/codex/openai/chat-completions"
            _ "github.com/router-for-me/CLIProxyAPI/v6/internal/translator/codex/openai/responses"

            _ "github.com/router-for-me/CLIProxyAPI/v6/internal/translator/openai/openai/chat-completions"
            _ "github.com/router-for-me/CLIProxyAPI/v6/internal/translator/openai/openai/responses"
        )
        """;

    private const string CodexGptOnlyThinkingProvidersGoSource =
        """
        package helps

        import (
            _ "github.com/router-for-me/CLIProxyAPI/v6/internal/thinking/provider/codex"
            _ "github.com/router-for-me/CLIProxyAPI/v6/internal/thinking/provider/openai"
        )
        """;

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
        if (source.Contains("persistCfg := cfg", StringComparison.Ordinal))
        {
            source = ReplaceRequired(
                source,
                "persistCfg := cfg\n",
                "persistCfg := cfg\n"
                    + "if protectedCfg, err := ProtectCodexCliPlusSecretRefsForWrite(cfg); err != nil {\n"
                    + "\treturn fmt.Errorf(\"failed to protect CodexCliPlus secret refs before config write: %w\", err)\n"
                    + "} else if protectedCfg != nil {\n"
                    + "\tpersistCfg = protectedCfg\n"
                    + "}\n"
            );
        }
        File.WriteAllText(configPath, source, new UTF8Encoding(false));

        var configBasicPath = Path.Combine(
            sourceRoot,
            "internal",
            "api",
            "handlers",
            "management",
            "config_basic.go"
        );
        if (File.Exists(configBasicPath))
        {
            var configBasicSource = File.ReadAllText(configBasicPath, Encoding.UTF8).Replace("\r\n", "\n");
            const string writeConfigMarker =
                "func WriteConfig(path string, data []byte) error {\n\tdata = config.NormalizeCommentIndentation(data)\n";
            const string writeConfigMarkerWithSpaces =
                "func WriteConfig(path string, data []byte) error {\n    data = config.NormalizeCommentIndentation(data)\n";
            var writeConfigReplacement =
                "func WriteConfig(path string, data []byte) error {\n\tprotectedData, errProtect := config.ProtectCodexCliPlusConfigYAMLForWrite(data)\n\tif errProtect != nil {\n\t\treturn errProtect\n\t}\n\tdata = config.NormalizeCommentIndentation(protectedData)\n";
            configBasicSource = ReplaceRequired(
                configBasicSource,
                configBasicSource.Contains(writeConfigMarker, StringComparison.Ordinal)
                    ? writeConfigMarker
                    : writeConfigMarkerWithSpaces,
                writeConfigReplacement
            );
            File.WriteAllText(configBasicPath, configBasicSource, new UTF8Encoding(false));
        }

        var authFilesPath = Path.Combine(
            sourceRoot,
            "internal",
            "api",
            "handlers",
            "management",
            "auth_files.go"
        );
        if (File.Exists(authFilesPath))
        {
            var authFilesSource = File.ReadAllText(authFilesPath, Encoding.UTF8).Replace("\r\n", "\n");
            if (!authFilesSource.Contains("/internal/config", StringComparison.Ordinal))
            {
                authFilesSource = ReplaceRequired(
                    authFilesSource,
                    "\"github.com/gin-gonic/gin\"\n",
                    "\"github.com/gin-gonic/gin\"\n\t\"github.com/router-for-me/CLIProxyAPI/v6/internal/config\"\n"
                );
            }
            const string writeAuthFileMarker =
                "auth, err := h.buildAuthFromFileData(dst, data)\n\tif err != nil {\n\t\treturn err\n\t}\n\tif errWrite := os.WriteFile(dst, data, 0o600); errWrite != nil {\n";
            const string writeAuthFileMarkerWithSpaces =
                "auth, err := h.buildAuthFromFileData(dst, data)\n    if err != nil {\n        return err\n    }\n    if errWrite := os.WriteFile(dst, data, 0o600); errWrite != nil {\n";
            authFilesSource = ReplaceRequired(
                authFilesSource,
                authFilesSource.Contains(writeAuthFileMarker, StringComparison.Ordinal)
                    ? writeAuthFileMarker
                    : writeAuthFileMarkerWithSpaces,
                "protectedData, errProtect := config.ProtectCodexCliPlusAuthJSONForWrite(data)\n\tif errProtect != nil {\n\t\treturn errProtect\n\t}\n\tdata = protectedData\n\tauth, err := h.buildAuthFromFileData(dst, data)\n\tif err != nil {\n\t\treturn err\n\t}\n\tif errWrite := os.WriteFile(dst, data, 0o600); errWrite != nil {\n"
            );
            File.WriteAllText(authFilesPath, authFilesSource, new UTF8Encoding(false));
        }

        var fileStorePath = Path.Combine(sourceRoot, "sdk", "auth", "filestore.go");
        if (File.Exists(fileStorePath))
        {
            var fileStoreSource = File.ReadAllText(fileStorePath, Encoding.UTF8).Replace("\r\n", "\n");
            if (!fileStoreSource.Contains("/internal/config", StringComparison.Ordinal))
            {
                fileStoreSource = ReplaceRequired(
                    fileStoreSource,
                    "cliproxyauth \"github.com/router-for-me/CLIProxyAPI/v6/sdk/cliproxy/auth\"\n",
                    "\"github.com/router-for-me/CLIProxyAPI/v6/internal/config\"\n\tcliproxyauth \"github.com/router-for-me/CLIProxyAPI/v6/sdk/cliproxy/auth\"\n"
                );
            }
            const string storageSaveMarker =
                "if err = auth.Storage.SaveTokenToFile(path); err != nil {\n\t\t\treturn \"\", err\n\t\t}\n";
            const string storageSaveMarkerWithShortDeclaration =
                "if err := auth.Storage.SaveTokenToFile(path); err != nil {\n            return \"\", err\n        }\n";
            fileStoreSource = ReplaceRequired(
                fileStoreSource,
                fileStoreSource.Contains(storageSaveMarker, StringComparison.Ordinal)
                    ? storageSaveMarker
                    : storageSaveMarkerWithShortDeclaration,
                "if err := auth.Storage.SaveTokenToFile(path); err != nil {\n\t\t\treturn \"\", err\n\t\t}\n\t\tif raw, errRead := os.ReadFile(path); errRead == nil {\n\t\t\tprotectedRaw, errProtect := config.ProtectCodexCliPlusAuthJSONForWrite(raw)\n\t\t\tif errProtect != nil {\n\t\t\t\treturn \"\", fmt.Errorf(\"auth filestore: protect storage file failed: %w\", errProtect)\n\t\t\t}\n\t\t\tif !jsonEqual(raw, protectedRaw) {\n\t\t\t\tif errWrite := os.WriteFile(path, protectedRaw, 0o600); errWrite != nil {\n\t\t\t\t\treturn \"\", fmt.Errorf(\"auth filestore: write protected storage file failed: %w\", errWrite)\n\t\t\t\t}\n\t\t\t}\n\t\t} else {\n\t\t\treturn \"\", fmt.Errorf(\"auth filestore: read storage file failed: %w\", errRead)\n\t\t}\n"
            );
            const string metadataMarshalMarker =
                "if errMarshal != nil {\n\t\t\treturn \"\", fmt.Errorf(\"auth filestore: marshal metadata failed: %w\", errMarshal)\n\t\t}\n\t\tif existing, errRead := os.ReadFile(path); errRead == nil {\n";
            const string metadataMarshalMarkerWithSpaces =
                "if errMarshal != nil {\n            return \"\", fmt.Errorf(\"auth filestore: marshal metadata failed: %w\", errMarshal)\n        }\n        if existing, errRead := os.ReadFile(path); errRead == nil {\n";
            fileStoreSource = ReplaceRequired(
                fileStoreSource,
                fileStoreSource.Contains(metadataMarshalMarker, StringComparison.Ordinal)
                    ? metadataMarshalMarker
                    : metadataMarshalMarkerWithSpaces,
                "if errMarshal != nil {\n\t\t\treturn \"\", fmt.Errorf(\"auth filestore: marshal metadata failed: %w\", errMarshal)\n\t\t}\n\t\tprotectedRaw, errProtect := config.ProtectCodexCliPlusAuthJSONForWrite(raw)\n\t\tif errProtect != nil {\n\t\t\treturn \"\", fmt.Errorf(\"auth filestore: protect metadata failed: %w\", errProtect)\n\t\t}\n\t\traw = protectedRaw\n\t\tif existing, errRead := os.ReadFile(path); errRead == nil {\n"
            );
            File.WriteAllText(fileStorePath, fileStoreSource, new UTF8Encoding(false));
        }

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
            "bytes"
            "encoding/json"
            "fmt"
            "net/http"
            "net/url"
            "os"
            "strings"
            "time"

            "gopkg.in/yaml.v3"
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

        type codexCliPlusSecretSaveResponse struct {
            URI      string `json:"uri"`
            SecretID string `json:"secretId"`
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

        func ProtectCodexCliPlusConfigYAMLForWrite(data []byte) ([]byte, error) {
            resolver := newCodexCliPlusSecretResolver()
            if resolver == nil || len(bytes.TrimSpace(data)) == 0 {
                return data, nil
            }
            var cfg Config
            if err := yaml.Unmarshal(data, &cfg); err != nil {
                return nil, err
            }
            protectedCfg, err := ProtectCodexCliPlusSecretRefsForWrite(&cfg)
            if err != nil {
                return nil, err
            }
            if protectedCfg == nil {
                return data, nil
            }
            protectedData, err := yaml.Marshal(protectedCfg)
            if err != nil {
                return nil, err
            }
            return protectedData, nil
        }

        func ProtectCodexCliPlusAuthJSONForWrite(data []byte) ([]byte, error) {
            resolver := newCodexCliPlusSecretResolver()
            if resolver == nil || len(bytes.TrimSpace(data)) == 0 {
                return data, nil
            }

            var payload any
            decoder := json.NewDecoder(bytes.NewReader(data))
            decoder.UseNumber()
            if err := decoder.Decode(&payload); err != nil {
                return nil, err
            }

            changed, err := resolver.protectJSONNode("$", payload, false, "")
            if err != nil {
                return nil, err
            }
            if !changed {
                return data, nil
            }

            protectedData, err := json.MarshalIndent(payload, "", "  ")
            if err != nil {
                return nil, err
            }
            return append(protectedData, '\n'), nil
        }

        func ProtectCodexCliPlusSecretRefsForWrite(cfg *Config) (*Config, error) {
            resolver := newCodexCliPlusSecretResolver()
            if cfg == nil || resolver == nil {
                return cfg, nil
            }

            protected, err := cloneCodexCliPlusConfig(cfg)
            if err != nil {
                return nil, err
            }

            if protected.RemoteManagement.SecretKey, err = resolver.protect("remote-management.secret-key", protected.RemoteManagement.SecretKey, "ManagementKey"); err != nil {
                return nil, err
            }
            for i := range protected.APIKeys {
                if protected.APIKeys[i], err = resolver.protect("api-keys", protected.APIKeys[i], "ApiKey"); err != nil {
                    return nil, err
                }
            }
            for i := range protected.GeminiKey {
                if protected.GeminiKey[i].APIKey, err = resolver.protect(fmt.Sprintf("gemini-api-key[%d].api-key", i), protected.GeminiKey[i].APIKey, "ApiKey"); err != nil {
                    return nil, err
                }
                if err = resolver.protectMap(fmt.Sprintf("gemini-api-key[%d].headers", i), protected.GeminiKey[i].Headers, "Header"); err != nil {
                    return nil, err
                }
            }
            for i := range protected.CodexKey {
                if protected.CodexKey[i].APIKey, err = resolver.protect(fmt.Sprintf("codex-api-key[%d].api-key", i), protected.CodexKey[i].APIKey, "ApiKey"); err != nil {
                    return nil, err
                }
                if err = resolver.protectMap(fmt.Sprintf("codex-api-key[%d].headers", i), protected.CodexKey[i].Headers, "Header"); err != nil {
                    return nil, err
                }
            }
            for i := range protected.ClaudeKey {
                if protected.ClaudeKey[i].APIKey, err = resolver.protect(fmt.Sprintf("claude-api-key[%d].api-key", i), protected.ClaudeKey[i].APIKey, "ApiKey"); err != nil {
                    return nil, err
                }
                if err = resolver.protectMap(fmt.Sprintf("claude-api-key[%d].headers", i), protected.ClaudeKey[i].Headers, "Header"); err != nil {
                    return nil, err
                }
            }
            for i := range protected.VertexCompatAPIKey {
                if protected.VertexCompatAPIKey[i].APIKey, err = resolver.protect(fmt.Sprintf("vertex-api-key[%d].api-key", i), protected.VertexCompatAPIKey[i].APIKey, "ApiKey"); err != nil {
                    return nil, err
                }
                if err = resolver.protectMap(fmt.Sprintf("vertex-api-key[%d].headers", i), protected.VertexCompatAPIKey[i].Headers, "Header"); err != nil {
                    return nil, err
                }
            }
            for i := range protected.OpenAICompatibility {
                if err = resolver.protectMap(fmt.Sprintf("openai-compatibility[%d].headers", i), protected.OpenAICompatibility[i].Headers, "Header"); err != nil {
                    return nil, err
                }
                for j := range protected.OpenAICompatibility[i].APIKeyEntries {
                    if protected.OpenAICompatibility[i].APIKeyEntries[j].APIKey, err = resolver.protect(fmt.Sprintf("openai-compatibility[%d].api-key-entries[%d].api-key", i, j), protected.OpenAICompatibility[i].APIKeyEntries[j].APIKey, "ApiKey"); err != nil {
                        return nil, err
                    }
                }
            }
            if protected.AmpCode.UpstreamAPIKey, err = resolver.protect("ampcode.upstream-api-key", protected.AmpCode.UpstreamAPIKey, "ApiKey"); err != nil {
                return nil, err
            }
            for i := range protected.AmpCode.UpstreamAPIKeys {
                if protected.AmpCode.UpstreamAPIKeys[i].UpstreamAPIKey, err = resolver.protect(fmt.Sprintf("ampcode.upstream-api-keys[%d].upstream-api-key", i), protected.AmpCode.UpstreamAPIKeys[i].UpstreamAPIKey, "ApiKey"); err != nil {
                    return nil, err
                }
                for j := range protected.AmpCode.UpstreamAPIKeys[i].APIKeys {
                    if protected.AmpCode.UpstreamAPIKeys[i].APIKeys[j], err = resolver.protect(fmt.Sprintf("ampcode.upstream-api-keys[%d].api-keys[%d]", i, j), protected.AmpCode.UpstreamAPIKeys[i].APIKeys[j], "ApiKey"); err != nil {
                        return nil, err
                    }
                }
            }
            return protected, nil
        }

        func cloneCodexCliPlusConfig(cfg *Config) (*Config, error) {
            data, err := yaml.Marshal(cfg)
            if err != nil {
                return nil, err
            }
            var protected Config
            if err := yaml.Unmarshal(data, &protected); err != nil {
                return nil, err
            }
            return &protected, nil
        }

        func (r *codexCliPlusSecretResolver) protectMap(path string, values map[string]string, kind string) error {
            for key, value := range values {
                protected, err := r.protect(path+"."+key, value, kind)
                if err != nil {
                    return err
                }
                values[key] = protected
            }
            return nil
        }

        func (r *codexCliPlusSecretResolver) protect(path, value, kind string) (string, error) {
            return r.protectWithSource(path, value, kind, "backend-config-write")
        }

        func (r *codexCliPlusSecretResolver) protectWithSource(path, value, kind, source string) (string, error) {
            trimmed := strings.TrimSpace(value)
            if trimmed == "" {
                return value, nil
            }
            if _, ok := codexCliPlusSecretID(trimmed); ok {
                return value, nil
            }

            requestURL := r.baseURL + "/v1/secrets"
            payload := map[string]any{
                "value":  value,
                "kind":   kind,
                "source": source,
                "metadata": map[string]string{
                    "path": path,
                },
            }
            body, err := json.Marshal(payload)
            if err != nil {
                return "", err
            }
            req, err := http.NewRequest(http.MethodPost, requestURL, bytes.NewReader(body))
            if err != nil {
                return "", err
            }
            req.Header.Set("Authorization", "Bearer "+r.token)
            req.Header.Set("Content-Type", "application/json")
            req.Header.Set("Accept", "application/json")

            resp, err := r.client.Do(req)
            if err != nil {
                return "", fmt.Errorf("secret save unavailable for %s: %w", path, err)
            }
            defer resp.Body.Close()
            if resp.StatusCode != http.StatusCreated && resp.StatusCode != http.StatusOK {
                return "", fmt.Errorf("secret save unavailable for %s: %s", path, resp.Status)
            }
            var saved codexCliPlusSecretSaveResponse
            if err := json.NewDecoder(resp.Body).Decode(&saved); err != nil {
                return "", err
            }
            if strings.TrimSpace(saved.URI) != "" {
                return saved.URI, nil
            }
            if strings.TrimSpace(saved.SecretID) != "" {
                return "ccp-secret://" + strings.TrimSpace(saved.SecretID), nil
            }
            return "", fmt.Errorf("secret save for %s returned empty reference", path)
        }

        func (r *codexCliPlusSecretResolver) protectJSONNode(path string, value any, parentSensitive bool, parentKey string) (bool, error) {
            switch node := value.(type) {
            case map[string]any:
                changed := false
                for key, child := range node {
                    sensitive := parentSensitive || codexCliPlusSensitiveKey(key)
                    if strings.EqualFold(parentKey, "headers") && codexCliPlusSensitiveHeaderKey(key) {
                        sensitive = true
                    }
                    childPath := path + "." + key
                    if text, ok := child.(string); ok && sensitive {
                        protected, err := r.protectWithSource(childPath, text, codexCliPlusSecretKind(key, parentKey), "backend-auth-write")
                        if err != nil {
                            return false, err
                        }
                        if protected != text {
                            node[key] = protected
                            changed = true
                        }
                        continue
                    }
                    childChanged, err := r.protectJSONNode(childPath, child, sensitive, key)
                    if err != nil {
                        return false, err
                    }
                    changed = changed || childChanged
                }
                return changed, nil
            case []any:
                changed := false
                for i, child := range node {
                    childPath := fmt.Sprintf("%s[%d]", path, i)
                    if text, ok := child.(string); ok && parentSensitive {
                        protected, err := r.protectWithSource(childPath, text, codexCliPlusSecretKind(parentKey, ""), "backend-auth-write")
                        if err != nil {
                            return false, err
                        }
                        if protected != text {
                            node[i] = protected
                            changed = true
                        }
                        continue
                    }
                    childChanged, err := r.protectJSONNode(childPath, child, parentSensitive, parentKey)
                    if err != nil {
                        return false, err
                    }
                    changed = changed || childChanged
                }
                return changed, nil
            default:
                return false, nil
            }
        }

        func codexCliPlusSensitiveKey(key string) bool {
            normalized := codexCliPlusNormalizeKey(key)
            if normalized == "" {
                return false
            }
            switch normalized {
            case "api-key", "api-keys", "apikey", "access-token", "refresh-token", "id-token",
                "token", "tokens", "authorization", "cookie", "secret", "secret-key",
                "client-secret", "private-key", "upstream-api-key", "upstream-api-keys",
                "x-api-key", "proxy-authorization":
                return true
            }
            return strings.HasSuffix(normalized, "-token") ||
                strings.HasSuffix(normalized, "-secret") ||
                strings.HasSuffix(normalized, "-api-key")
        }

        func codexCliPlusSensitiveHeaderKey(key string) bool {
            normalized := strings.ToLower(strings.TrimSpace(key))
            switch normalized {
            case "authorization", "cookie", "x-api-key", "x-goog-api-key", "proxy-authorization":
                return true
            }
            return codexCliPlusSensitiveKey(key) ||
                strings.Contains(normalized, "token") ||
                strings.Contains(normalized, "secret")
        }

        func codexCliPlusNormalizeKey(key string) string {
            normalized := strings.ToLower(strings.TrimSpace(key))
            normalized = strings.ReplaceAll(normalized, "_", "-")
            normalized = strings.ReplaceAll(normalized, " ", "-")
            for strings.Contains(normalized, "--") {
                normalized = strings.ReplaceAll(normalized, "--", "-")
            }
            return normalized
        }

        func codexCliPlusSecretKind(key, parentKey string) string {
            normalizedKey := codexCliPlusNormalizeKey(key)
            normalizedParent := codexCliPlusNormalizeKey(parentKey)
            switch {
            case strings.Contains(normalizedKey, "refresh-token"):
                return "OAuthRefreshToken"
            case strings.Contains(normalizedKey, "access-token") || strings.Contains(normalizedKey, "id-token"):
                return "OAuthAccessToken"
            case strings.Contains(normalizedKey, "authorization") || strings.Contains(normalizedParent, "authorization"):
                return "AuthorizationHeader"
            case strings.Contains(normalizedKey, "cookie"):
                return "Cookie"
            case strings.Contains(normalizedKey, "api-key"):
                return "ApiKey"
            case normalizedParent == "headers":
                return "Header"
            case strings.Contains(normalizedKey, "private-key") || strings.Contains(normalizedKey, "secret"):
                return "ProviderCredential"
            case strings.Contains(normalizedKey, "token"):
                return "OAuthToken"
            default:
                return "Unknown"
            }
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
        if (source.Contains(oldValue, StringComparison.Ordinal))
        {
            return source.Replace(oldValue, newValue, StringComparison.Ordinal);
        }

        var oldValueWithSpaces = oldValue.Replace("\t", "    ", StringComparison.Ordinal);
        if (
            !string.Equals(oldValueWithSpaces, oldValue, StringComparison.Ordinal)
            && source.Contains(oldValueWithSpaces, StringComparison.Ordinal)
        )
        {
            return source.Replace(oldValueWithSpaces, newValue, StringComparison.Ordinal);
        }

        if (!source.Contains(oldValue, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Pinned backend source no longer contains expected patch fragment: {oldValue}"
            );
        }

        throw new InvalidDataException(
            $"Pinned backend source no longer contains expected patch fragment: {oldValue}"
        );
    }

    private static string ReplaceFunctionBlockRequired(
        string source,
        string functionStart,
        string replacement
    )
    {
        var startIndex = source.IndexOf(functionStart, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            throw new InvalidDataException(
                $"Pinned backend source no longer contains expected function block: {functionStart}"
            );
        }

        var endIndex = source.IndexOf("\n}\n", startIndex, StringComparison.Ordinal);
        var endLength = 3;
        if (endIndex < 0)
        {
            endIndex = source.IndexOf("\n}", startIndex, StringComparison.Ordinal);
            endLength = 2;
        }
        if (endIndex < 0)
        {
            throw new InvalidDataException(
                $"Pinned backend source function block is incomplete: {functionStart}"
            );
        }

        return source[..startIndex] + replacement + source[(endIndex + endLength)..];
    }

    private static string RemoveGinRouteBlockRequired(string source, string routeStartMarker)
    {
        var startIndex = source.IndexOf(routeStartMarker, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            throw new InvalidDataException(
                $"Pinned backend source no longer contains expected route block: {routeStartMarker}"
            );
        }

        while (startIndex > 0 && source[startIndex - 1] is ' ' or '\t')
        {
            startIndex--;
        }

        var statusIndex = source.IndexOf(
            "c.String(http.StatusOK, oauthCallbackSuccessHTML)",
            startIndex,
            StringComparison.Ordinal
        );
        if (statusIndex < 0)
        {
            throw new InvalidDataException(
                $"Pinned backend source route block is incomplete: {routeStartMarker}"
            );
        }

        var endIndex = source.IndexOf("})", statusIndex, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            throw new InvalidDataException(
                $"Pinned backend source route block is incomplete: {routeStartMarker}"
            );
        }

        endIndex += 2;
        while (endIndex < source.Length && source[endIndex] is '\r' or '\n')
        {
            endIndex++;
        }

        return source[..startIndex] + source[endIndex..];
    }

    private static string RemoveLineBlockRequired(
        string source,
        string startMarker,
        string endMarker,
        string replacement = ""
    )
    {
        var startIndex = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            throw new InvalidDataException(
                $"Pinned backend source no longer contains expected line block: {startMarker}"
            );
        }

        while (startIndex > 0 && source[startIndex - 1] is not '\n')
        {
            startIndex--;
        }

        var endMarkerIndex = source.IndexOf(endMarker, startIndex, StringComparison.Ordinal);
        if (endMarkerIndex < 0)
        {
            throw new InvalidDataException(
                $"Pinned backend source line block is incomplete: {startMarker}"
            );
        }

        var endIndex = source.IndexOf('\n', endMarkerIndex);
        if (endIndex < 0)
        {
            endIndex = source.Length;
        }
        else
        {
            endIndex++;
        }
        while (endIndex < source.Length && source[endIndex] is '\r' or '\n')
        {
            endIndex++;
        }

        return source[..startIndex] + replacement + source[endIndex..];
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
