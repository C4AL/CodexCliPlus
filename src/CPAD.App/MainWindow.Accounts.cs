using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

using CPAD.Core.Models;
using CPAD.Core.Models.Management;

using WpfClipboard = System.Windows.Clipboard;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace CPAD;

public partial class MainWindow
{
    private IReadOnlyList<ManagementAuthFileItem>? _accountsAuthFiles;
    private IReadOnlyList<string> _accountsApiKeys = [];
    private bool _accountsLoading;
    private string? _accountsError;
    private string? _accountsStatusMessage;
    private string _managementKeyDraft = string.Empty;
    private string _apiKeysDraft = string.Empty;
    private string _authJsonFileNameDraft = "auth-file.json";
    private string _authJsonContentDraft = string.Empty;
    private string? _selectedAuthFileName;
    private IReadOnlyList<ManagementModelDescriptor> _selectedAuthFileModels = [];
    private bool _selectedAuthFileModelsLoading;
    private string? _selectedAuthFileModelsError;
    private readonly Dictionary<string, OAuthProviderState> _oauthStates = CreateOAuthProviderStates();
    private readonly Dictionary<string, DispatcherTimer> _oauthPollers = new(StringComparer.OrdinalIgnoreCase);

    private UIElement BuildAccountsContent()
    {
        if (_accountsLoading && _accountsAuthFiles is null)
        {
            return CreateStatePanel(
                "Loading account and authorization data...",
                "Reading the management key, OAuth/device flow entry points, stored auth files, and backend API keys.");
        }

        if (!string.IsNullOrWhiteSpace(_accountsError) && _accountsAuthFiles is null)
        {
            return CreateStatePanel("Account data is unavailable.", _accountsError);
        }

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateAccountsHero());

        if (!string.IsNullOrWhiteSpace(_accountsStatusMessage))
        {
            root.Children.Add(CreateHintCard("Account actions", _accountsStatusMessage));
        }

        if (!string.IsNullOrWhiteSpace(_accountsError))
        {
            root.Children.Add(CreateHintCard("Refresh issue", _accountsError));
        }

        root.Children.Add(CreateSectionHeader("Management Key"));
        root.Children.Add(CreateManagementKeyCard());

        root.Children.Add(CreateSectionHeader("OAuth & Device Flow"));
        root.Children.Add(CreateOAuthProvidersPanel());

        root.Children.Add(CreateSectionHeader("Backend API Keys"));
        root.Children.Add(CreateApiKeysCard());

        root.Children.Add(CreateSectionHeader("Auth JSON / Cookie Import"));
        root.Children.Add(CreateAuthJsonImportCard());

        root.Children.Add(CreateSectionHeader("Stored Auth Files"));
        root.Children.Add(CreateAuthFilesPanel());

        return root;
    }

    private async Task RefreshAccountsAsync(bool force)
    {
        if (_accountsLoading)
        {
            return;
        }

        if (!force && _accountsAuthFiles is not null)
        {
            return;
        }

        _accountsLoading = true;
        _accountsError = null;
        RefreshAccountsSection();

        try
        {
            var authFilesTask = _authService.GetAuthFilesAsync();
            var apiKeysTask = _authService.GetApiKeysAsync();
            await Task.WhenAll(authFilesTask, apiKeysTask);

            _accountsAuthFiles = authFilesTask.Result.Value
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _accountsApiKeys = apiKeysTask.Result.Value;
            _apiKeysDraft = string.Join(Environment.NewLine, _accountsApiKeys);
        }
        catch (Exception exception)
        {
            _accountsError = exception.Message;
        }
        finally
        {
            _accountsLoading = false;
            RefreshAccountsSection();
        }
    }

    private UIElement CreateAccountsHero()
    {
        var authFiles = _accountsAuthFiles ?? [];
        var readyAuthFiles = authFiles.Count(item => !item.Disabled && !item.Unavailable);

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };
        root.Children.Add(CreateText(
            "Native account access, management key control, and credential flows",
            22,
            FontWeights.SemiBold,
            "PrimaryTextBrush"));
        root.Children.Add(CreateText(
            "This page is wired to the audited management routes for OAuth/device flow, auth file inventory, backend API keys, and secure desktop-side management key storage.",
            13,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 8, 0, 0)));
        root.Children.Add(CreateMetricGrid(
            CreateMetricCard("API Keys", _accountsApiKeys.Count.ToString(CultureInfo.InvariantCulture), "Backend /api-keys list"),
            CreateMetricCard("Auth Files", authFiles.Count.ToString(CultureInfo.InvariantCulture), "Stored credentials from /auth-files"),
            CreateMetricCard("Ready Auths", readyAuthFiles.ToString(CultureInfo.InvariantCulture), "Enabled credentials without current availability flags")));
        return CreateCard(root, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateManagementKeyCard()
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };

        var summary = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 0, 0, 12)
        };
        AddKeyValue(summary, "Storage", "DPAPI secret reference");
        AddKeyValue(summary, "Reference", _settings.ManagementKeyReference);
        AddKeyValue(summary, "Current value", string.IsNullOrWhiteSpace(_settings.ManagementKey) ? "Auto-generated" : "Stored");
        AddKeyValue(summary, "Backend", _backendProcessManager.CurrentStatus.State.ToString());
        panel.Children.Add(summary);

        panel.Children.Add(CreateText(
            "Saving this value updates the desktop settings store and restarts the managed backend so the management API uses the new secret immediately.",
            12,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 10)));

        var passwordBox = new PasswordBox
        {
            Password = _managementKeyDraft,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 10)
        };
        passwordBox.PasswordChanged += (_, _) => _managementKeyDraft = passwordBox.Password;
        panel.Children.Add(passwordBox);

        var actions = new WrapPanel();
        actions.Children.Add(CreateActionButton("Save & Restart", SaveManagementKeyAsync));
        actions.Children.Add(CreateActionButton("Auto-generate", AutoGenerateManagementKeyAsync));
        panel.Children.Add(actions);

        return CreateCard(panel, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateOAuthProvidersPanel()
    {
        var root = new StackPanel { Orientation = WpfOrientation.Vertical };

        foreach (var provider in _oauthStates.Values)
        {
            root.Children.Add(CreateOAuthProviderCard(provider));
        }

        return root;
    }

    private UIElement CreateOAuthProviderCard(OAuthProviderState provider)
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(provider.Title, 16, FontWeights.SemiBold, "PrimaryTextBrush"));
        panel.Children.Add(CreateText(
            provider.Description,
            12,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 6, 0, 0)));

        if (provider.RequiresProjectId)
        {
            panel.Children.Add(CreateText(
                "Optional project ID",
                12,
                FontWeights.SemiBold,
                "SecondaryTextBrush",
                new Thickness(0, 10, 0, 6)));

            var projectIdBox = new WpfTextBox
            {
                Text = provider.ProjectIdDraft,
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };
            projectIdBox.TextChanged += (_, _) => provider.ProjectIdDraft = projectIdBox.Text;
            panel.Children.Add(projectIdBox);
        }

        var actions = new WrapPanel();
        actions.Children.Add(CreateActionButton(
            provider.IsBusy ? "Starting..." : "Start Login",
            () => StartOAuthAsync(provider.ProviderKey)));
        if (!string.IsNullOrWhiteSpace(provider.AuthorizationUrl))
        {
            actions.Children.Add(CreateActionButton(
                "Open Link",
                () =>
                {
                    OpenExternalLink(provider.AuthorizationUrl);
                    return Task.CompletedTask;
                }));
            actions.Children.Add(CreateActionButton(
                "Copy Link",
                () =>
                {
                    CopyTextToClipboard(provider.AuthorizationUrl);
                    return Task.CompletedTask;
                }));
        }
        panel.Children.Add(actions);

        panel.Children.Add(CreateText(
            $"Status: {provider.StatusText}",
            12,
            FontWeights.SemiBold,
            "SecondaryTextBrush",
            new Thickness(0, 8, 0, 0)));

        if (!string.IsNullOrWhiteSpace(provider.AuthorizationUrl))
        {
            panel.Children.Add(CreateText(
                provider.AuthorizationUrl,
                12,
                FontWeights.Normal,
                "SecondaryTextBrush",
                new Thickness(0, 6, 0, 0)));
        }

        if (provider.SupportsCallback)
        {
            panel.Children.Add(CreateText(
                "Redirect URL callback",
                12,
                FontWeights.SemiBold,
                "SecondaryTextBrush",
                new Thickness(0, 10, 0, 6)));
            var callbackBox = new WpfTextBox
            {
                Text = provider.CallbackUrlDraft,
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };
            callbackBox.TextChanged += (_, _) => provider.CallbackUrlDraft = callbackBox.Text;
            panel.Children.Add(callbackBox);

            var callbackActions = new WrapPanel();
            callbackActions.Children.Add(CreateActionButton(
                provider.IsSubmittingCallback ? "Submitting..." : "Submit Callback",
                () => SubmitOAuthCallbackAsync(provider.ProviderKey)));
            panel.Children.Add(callbackActions);
        }

        if (!string.IsNullOrWhiteSpace(provider.ErrorText))
        {
            panel.Children.Add(CreateText(
                provider.ErrorText,
                12,
                FontWeights.Normal,
                "SecondaryTextBrush",
                new Thickness(0, 8, 0, 0)));
        }

        return CreateCard(panel, new Thickness(0, 0, 0, 16));
    }

    private UIElement CreateApiKeysCard()
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(
            "One key per line. Saving replaces the backend API key list through the management API.",
            12,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 10)));

        var editor = new WpfTextBox
        {
            Text = _apiKeysDraft,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 120,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10)
        };
        editor.TextChanged += (_, _) => _apiKeysDraft = editor.Text;
        panel.Children.Add(editor);

        var actions = new WrapPanel();
        actions.Children.Add(CreateActionButton("Save API Keys", SaveApiKeysAsync));
        actions.Children.Add(CreateActionButton(
            "Reset Draft",
            () =>
            {
                _apiKeysDraft = string.Join(Environment.NewLine, _accountsApiKeys);
                RefreshAccountsSection();
                return Task.CompletedTask;
            }));
        panel.Children.Add(actions);

        return CreateCard(panel, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateAuthJsonImportCard()
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(
            "Paste or load an upstream-compatible auth JSON document. Cookie-based credentials are supported when the JSON carries metadata.cookie, matching the audited backend auth schema.",
            12,
            FontWeights.Normal,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 10)));

        panel.Children.Add(CreateText(
            "File name",
            12,
            FontWeights.SemiBold,
            "SecondaryTextBrush",
            new Thickness(0, 0, 0, 6)));
        var fileNameBox = new WpfTextBox
        {
            Text = _authJsonFileNameDraft,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10)
        };
        fileNameBox.TextChanged += (_, _) => _authJsonFileNameDraft = fileNameBox.Text;
        panel.Children.Add(fileNameBox);

        var actions = new WrapPanel();
        actions.Children.Add(CreateActionButton("Load JSON File", LoadAuthJsonFileAsync));
        actions.Children.Add(CreateActionButton("Import Credential", UploadAuthJsonAsync));
        actions.Children.Add(CreateActionButton(
            "Clear Draft",
            () =>
            {
                _authJsonFileNameDraft = "auth-file.json";
                _authJsonContentDraft = string.Empty;
                RefreshAccountsSection();
                return Task.CompletedTask;
            }));
        panel.Children.Add(actions);

        panel.Children.Add(CreateText(
            "Credential JSON",
            12,
            FontWeights.SemiBold,
            "SecondaryTextBrush",
            new Thickness(0, 10, 0, 6)));
        var contentBox = new WpfTextBox
        {
            Text = _authJsonContentDraft,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 180,
            Padding = new Thickness(10)
        };
        contentBox.TextChanged += (_, _) => _authJsonContentDraft = contentBox.Text;
        panel.Children.Add(contentBox);

        return CreateCard(panel, new Thickness(0, 0, 0, 18));
    }

    private UIElement CreateAuthFilesPanel()
    {
        if (_accountsAuthFiles is null)
        {
            return CreateStatePanel(
                "No auth file data loaded yet.",
                "Use Refresh by revisiting the page or complete an OAuth flow to populate credential inventory.");
        }

        if (_accountsAuthFiles.Count == 0)
        {
            return CreateStatePanel(
                "No auth files stored.",
                "Start an OAuth/device flow or import an auth JSON document to add credentials.");
        }

        var root = new StackPanel { Orientation = WpfOrientation.Vertical };

        foreach (var item in _accountsAuthFiles)
        {
            root.Children.Add(CreateAuthFileCard(item));
        }

        return root;
    }

    private UIElement CreateAuthFileCard(ManagementAuthFileItem item)
    {
        var panel = new StackPanel { Orientation = WpfOrientation.Vertical };
        panel.Children.Add(CreateText(item.Name, 15, FontWeights.SemiBold, "PrimaryTextBrush"));

        var details = new UniformGrid
        {
            Columns = 2,
            Margin = new Thickness(0, 10, 0, 10)
        };
        AddKeyValue(details, "Provider", item.Provider ?? item.Type ?? "Unknown");
        AddKeyValue(details, "Account", item.Email ?? item.Account ?? item.Label ?? "-");
        AddKeyValue(details, "Status", BuildAuthFileStatus(item));
        AddKeyValue(details, "Auth index", item.AuthIndex ?? "-");
        AddKeyValue(details, "Source", item.Source ?? "-");
        AddKeyValue(details, "Updated", FormatTimestamp(item.UpdatedAt));
        panel.Children.Add(details);

        if (!string.IsNullOrWhiteSpace(item.Path))
        {
            panel.Children.Add(CreateText(
                item.Path,
                12,
                FontWeights.Normal,
                "SecondaryTextBrush",
                new Thickness(0, 0, 0, 8)));
        }

        var actions = new WrapPanel();
        actions.Children.Add(CreateActionButton("Show Models", () => LoadAuthFileModelsAsync(item.Name)));
        actions.Children.Add(CreateActionButton(
            item.Disabled ? "Enable" : "Disable",
            () => ToggleAuthFileDisabledAsync(item)));
        actions.Children.Add(CreateActionButton("Delete", () => DeleteAuthFileAsync(item)));
        if (!string.IsNullOrWhiteSpace(item.Path))
        {
            actions.Children.Add(CreateActionButton(
                "Open Folder",
                () =>
                {
                    var directory = Path.GetDirectoryName(item.Path);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        ProcessStartFolder(directory);
                    }

                    return Task.CompletedTask;
                }));
        }
        panel.Children.Add(actions);

        if (string.Equals(_selectedAuthFileName, item.Name, StringComparison.OrdinalIgnoreCase))
        {
            if (_selectedAuthFileModelsLoading)
            {
                panel.Children.Add(CreateText(
                    "Loading models for this credential...",
                    12,
                    FontWeights.Normal,
                    "SecondaryTextBrush",
                    new Thickness(0, 10, 0, 0)));
            }
            else if (!string.IsNullOrWhiteSpace(_selectedAuthFileModelsError))
            {
                panel.Children.Add(CreateText(
                    _selectedAuthFileModelsError,
                    12,
                    FontWeights.Normal,
                    "SecondaryTextBrush",
                    new Thickness(0, 10, 0, 0)));
            }
            else if (_selectedAuthFileModels.Count == 0)
            {
                panel.Children.Add(CreateText(
                    "No model definitions were returned for this credential.",
                    12,
                    FontWeights.Normal,
                    "SecondaryTextBrush",
                    new Thickness(0, 10, 0, 0)));
            }
            else
            {
                var modelsText = string.Join(
                    Environment.NewLine,
                    _selectedAuthFileModels.Select(model => model.DisplayName ?? model.Id));
                panel.Children.Add(CreateText(
                    modelsText,
                    12,
                    FontWeights.Normal,
                    "SecondaryTextBrush",
                    new Thickness(0, 10, 0, 0)));
            }
        }

        return CreateCard(panel, new Thickness(0, 0, 0, 16));
    }

    private async Task SaveManagementKeyAsync()
    {
        try
        {
            _settings.ManagementKey = _managementKeyDraft.Trim();
            await _configurationService.SaveAsync(_settings);
            await _backendProcessManager.RestartAsync();
            _settings = await _configurationService.LoadAsync();
            _managementKeyDraft = _settings.ManagementKey;
            _accountsStatusMessage = string.IsNullOrWhiteSpace(_settings.ManagementKey)
                ? "Management key auto-generation completed on backend restart."
                : "Management key saved to DPAPI and backend restarted.";
            await RefreshAccountsAsync(force: true);
        }
        catch (Exception exception)
        {
            _accountsStatusMessage = $"Management key update failed: {exception.Message}";
            RefreshAccountsSection();
        }
    }

    private async Task AutoGenerateManagementKeyAsync()
    {
        _managementKeyDraft = string.Empty;
        await SaveManagementKeyAsync();
    }

    private async Task SaveApiKeysAsync()
    {
        try
        {
            var apiKeys = _apiKeysDraft
                .Split(["\r\n", "\n", ","], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (apiKeys.Length == 0)
            {
                throw new InvalidOperationException("At least one backend API key is required.");
            }

            await _authService.ReplaceApiKeysAsync(apiKeys);
            _accountsStatusMessage = $"Saved {apiKeys.Length} backend API key entries.";
            await RefreshAccountsAsync(force: true);
        }
        catch (Exception exception)
        {
            _accountsStatusMessage = $"API key update failed: {exception.Message}";
            RefreshAccountsSection();
        }
    }

    private async Task LoadAuthJsonFileAsync()
    {
        try
        {
            var dialog = new WpfOpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            _authJsonFileNameDraft = Path.GetFileName(dialog.FileName);
            _authJsonContentDraft = await File.ReadAllTextAsync(dialog.FileName);
            _accountsStatusMessage = $"Loaded draft from {_authJsonFileNameDraft}.";
        }
        catch (Exception exception)
        {
            _accountsStatusMessage = $"Loading auth JSON failed: {exception.Message}";
        }

        RefreshAccountsSection();
    }

    private async Task UploadAuthJsonAsync()
    {
        try
        {
            var fileName = string.IsNullOrWhiteSpace(_authJsonFileNameDraft)
                ? "auth-file.json"
                : _authJsonFileNameDraft.Trim();
            using var document = JsonDocument.Parse(_authJsonContentDraft);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Credential JSON must be an object.");
            }

            await _authService.UploadAuthFileAsync(fileName, _authJsonContentDraft);
            _accountsStatusMessage = $"Imported credential JSON as {Path.GetFileName(fileName)}.";
            _authJsonContentDraft = string.Empty;
            await RefreshAccountsAsync(force: true);
        }
        catch (Exception exception)
        {
            _accountsStatusMessage = $"Credential import failed: {exception.Message}";
            RefreshAccountsSection();
        }
    }

    private async Task LoadAuthFileModelsAsync(string authFileName)
    {
        _selectedAuthFileName = authFileName;
        _selectedAuthFileModels = [];
        _selectedAuthFileModelsError = null;
        _selectedAuthFileModelsLoading = true;
        RefreshAccountsSection();

        try
        {
            var response = await _authService.GetAuthFileModelsAsync(authFileName);
            _selectedAuthFileModels = response.Value;
        }
        catch (Exception exception)
        {
            _selectedAuthFileModelsError = exception.Message;
        }
        finally
        {
            _selectedAuthFileModelsLoading = false;
            RefreshAccountsSection();
        }
    }

    private async Task ToggleAuthFileDisabledAsync(ManagementAuthFileItem item)
    {
        try
        {
            await _authService.SetAuthFileDisabledAsync(item.Name, !item.Disabled);
            _accountsStatusMessage = item.Disabled
                ? $"Enabled auth file {item.Name}."
                : $"Disabled auth file {item.Name}.";
            await RefreshAccountsAsync(force: true);
        }
        catch (Exception exception)
        {
            _accountsStatusMessage = $"Auth file update failed: {exception.Message}";
            RefreshAccountsSection();
        }
    }

    private async Task DeleteAuthFileAsync(ManagementAuthFileItem item)
    {
        var result = System.Windows.MessageBox.Show(
            this,
            $"Delete auth file '{item.Name}'?",
            "Delete Auth File",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _authService.DeleteAuthFileAsync(item.Name);
            if (string.Equals(_selectedAuthFileName, item.Name, StringComparison.OrdinalIgnoreCase))
            {
                _selectedAuthFileName = null;
                _selectedAuthFileModels = [];
                _selectedAuthFileModelsError = null;
            }

            _accountsStatusMessage = $"Deleted auth file {item.Name}.";
            await RefreshAccountsAsync(force: true);
        }
        catch (Exception exception)
        {
            _accountsStatusMessage = $"Auth file deletion failed: {exception.Message}";
            RefreshAccountsSection();
        }
    }

    private async Task StartOAuthAsync(string providerKey)
    {
        var provider = _oauthStates[providerKey];
        provider.IsBusy = true;
        provider.ErrorText = null;
        provider.StatusText = "Starting login...";
        provider.AuthorizationUrl = null;
        provider.StateToken = null;
        RefreshAccountsSection();

        try
        {
            var projectId = provider.RequiresProjectId
                ? string.IsNullOrWhiteSpace(provider.ProjectIdDraft)
                    ? null
                    : provider.ProjectIdDraft.Trim()
                : null;
            var response = await _authService.GetOAuthStartAsync(providerKey, projectId);
            provider.AuthorizationUrl = response.Value.Url;
            provider.StateToken = response.Value.State;
            provider.StatusText = string.IsNullOrWhiteSpace(provider.StateToken)
                ? "Open the authorization link to continue."
                : "Waiting for authorization to complete...";

            if (!string.IsNullOrWhiteSpace(provider.StateToken))
            {
                StartAccountsPagePolling(providerKey, provider.StateToken);
            }
        }
        catch (Exception exception)
        {
            provider.ErrorText = exception.Message;
            provider.StatusText = "Start failed.";
        }
        finally
        {
            provider.IsBusy = false;
            RefreshAccountsSection();
        }
    }

    private async Task SubmitOAuthCallbackAsync(string providerKey)
    {
        var provider = _oauthStates[providerKey];
        if (string.IsNullOrWhiteSpace(provider.CallbackUrlDraft))
        {
            provider.ErrorText = "Redirect URL is required before callback submission.";
            RefreshAccountsSection();
            return;
        }

        provider.IsSubmittingCallback = true;
        provider.ErrorText = null;
        provider.StatusText = "Submitting callback...";
        RefreshAccountsSection();

        try
        {
            await _authService.SubmitOAuthCallbackAsync(providerKey, provider.CallbackUrlDraft.Trim());
            provider.StatusText = "Callback accepted. Waiting for refresh.";
            await RefreshAccountsAsync(force: true);
        }
        catch (Exception exception)
        {
            provider.ErrorText = exception.Message;
            provider.StatusText = "Callback failed.";
        }
        finally
        {
            provider.IsSubmittingCallback = false;
            RefreshAccountsSection();
        }
    }

    private void StartAccountsPagePolling(string providerKey, string stateToken)
    {
        StopAccountsPagePolling(providerKey);

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        timer.Tick += async (_, _) => await PollOAuthAsync(providerKey, stateToken);
        _oauthPollers[providerKey] = timer;
        timer.Start();
    }

    private async Task PollOAuthAsync(string providerKey, string stateToken)
    {
        var provider = _oauthStates[providerKey];
        if (provider.IsPollingRequestActive)
        {
            return;
        }

        provider.IsPollingRequestActive = true;
        try
        {
            var response = await _authService.GetOAuthStatusAsync(stateToken);
            switch (response.Value.Status)
            {
                case "ok":
                    provider.StatusText = "Authorization completed.";
                    provider.ErrorText = null;
                    StopAccountsPagePolling(providerKey);
                    await RefreshAccountsAsync(force: true);
                    break;
                case "error":
                    provider.StatusText = "Authorization failed.";
                    provider.ErrorText = response.Value.Error ?? "Unknown authorization error.";
                    StopAccountsPagePolling(providerKey);
                    break;
                default:
                    provider.StatusText = "Waiting for authorization to complete...";
                    break;
            }
        }
        catch (Exception exception)
        {
            provider.StatusText = "Polling failed.";
            provider.ErrorText = exception.Message;
            StopAccountsPagePolling(providerKey);
        }
        finally
        {
            provider.IsPollingRequestActive = false;
            RefreshAccountsSection();
        }
    }

    private void StopAccountsPagePolling()
    {
        foreach (var providerKey in _oauthPollers.Keys.ToArray())
        {
            StopAccountsPagePolling(providerKey);
        }
    }

    private void StopAccountsPagePolling(string providerKey)
    {
        if (_oauthPollers.Remove(providerKey, out var timer))
        {
            timer.Stop();
        }
    }

    private void OpenExternalLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void CopyTextToClipboard(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        WpfClipboard.SetText(value);
        _accountsStatusMessage = "Copied authorization link to the clipboard.";
        RefreshAccountsSection();
    }

    private void RefreshAccountsSection()
    {
        if ((NavigationList.SelectedItem as ShellSection)?.Key == "accounts")
        {
            UpdateSelectedSection();
        }
    }

    private static string BuildAuthFileStatus(ManagementAuthFileItem item)
    {
        if (item.Disabled)
        {
            return "Disabled";
        }

        if (item.Unavailable)
        {
            return string.IsNullOrWhiteSpace(item.StatusMessage)
                ? "Unavailable"
                : $"Unavailable | {item.StatusMessage}";
        }

        return string.IsNullOrWhiteSpace(item.Status)
            ? "Active"
            : item.Status;
    }

    private static string FormatTimestamp(DateTimeOffset? value)
    {
        return value is null ? "-" : value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, OAuthProviderState> CreateOAuthProviderStates()
    {
        return new Dictionary<string, OAuthProviderState>(StringComparer.OrdinalIgnoreCase)
        {
            ["codex"] = new OAuthProviderState("codex", "Codex OAuth", "Browser-based OAuth flow for Codex credentials.", supportsCallback: true),
            ["anthropic"] = new OAuthProviderState("anthropic", "Anthropic OAuth", "Browser-based OAuth flow for Claude credentials.", supportsCallback: true),
            ["antigravity"] = new OAuthProviderState("antigravity", "Antigravity OAuth", "Browser-based OAuth flow backed by the managed auth session.", supportsCallback: true),
            ["gemini-cli"] = new OAuthProviderState("gemini-cli", "Gemini CLI OAuth", "Browser-based OAuth flow with optional project scope.", supportsCallback: true, requiresProjectId: true),
            ["kimi"] = new OAuthProviderState("kimi", "Kimi Device Flow", "Device-style login flow exposed by the audited management API.", supportsCallback: false)
        };
    }

    private sealed class OAuthProviderState
    {
        public OAuthProviderState(
            string providerKey,
            string title,
            string description,
            bool supportsCallback,
            bool requiresProjectId = false)
        {
            ProviderKey = providerKey;
            Title = title;
            Description = description;
            SupportsCallback = supportsCallback;
            RequiresProjectId = requiresProjectId;
        }

        public string ProviderKey { get; }

        public string Title { get; }

        public string Description { get; }

        public bool SupportsCallback { get; }

        public bool RequiresProjectId { get; }

        public string StatusText { get; set; } = "Idle";

        public string? AuthorizationUrl { get; set; }

        public string? StateToken { get; set; }

        public string? ErrorText { get; set; }

        public string CallbackUrlDraft { get; set; } = string.Empty;

        public string ProjectIdDraft { get; set; } = string.Empty;

        public bool IsBusy { get; set; }

        public bool IsSubmittingCallback { get; set; }

        public bool IsPollingRequestActive { get; set; }
    }
}
