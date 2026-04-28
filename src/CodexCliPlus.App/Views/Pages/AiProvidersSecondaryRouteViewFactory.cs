using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using CodexCliPlus.Core.Abstractions.Management;
using CodexCliPlus.Core.Models.Management;
using CodexCliPlus.Management.DesignSystem.Controls;
using CodexCliPlus.Services.SecondaryRoutes;
using CodexCliPlus.ViewModels.Pages;

using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using Control = System.Windows.Controls.Control;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace CodexCliPlus.Views.Pages;

internal sealed class AiProvidersSecondaryRouteViewFactory : IManagementSecondaryRouteViewFactory
{
    private readonly AiProvidersPageViewModel _viewModel;
    private readonly IManagementAuthService _authService;
    private readonly Page _owner;
    private readonly AiProvidersRouteState _routeState;
    private readonly Func<Task> _refreshAsync;
    private readonly Func<string, string, bool> _navigate;

    public AiProvidersSecondaryRouteViewFactory(
        AiProvidersPageViewModel viewModel,
        IManagementAuthService authService,
        Page owner,
        AiProvidersRouteState routeState,
        Func<Task> refreshAsync,
        Func<string, string, bool> navigate)
    {
        _viewModel = viewModel;
        _authService = authService;
        _owner = owner;
        _routeState = routeState;
        _refreshAsync = refreshAsync;
        _navigate = navigate;
    }

    public bool HasUnsavedChanges => _routeState.IsDirty;

    public void DiscardPendingChanges()
    {
        _routeState.MarkClean();
    }

    public ManagementSecondaryRouteDescriptor Create(string? routeKey)
    {
        return routeKey switch
        {
            "ai-providers-gemini-new" => CreateProviderEntryDescriptor("gemini", "Gemini", supportsCloak: false, supportsModels: false, isGemini: true, isNew: true),
            "ai-providers-gemini-edit" => CreateProviderEntryDescriptor("gemini", "Gemini", supportsCloak: false, supportsModels: false, isGemini: true, isNew: false),
            "ai-providers-codex-new" => CreateProviderEntryDescriptor("codex", "Codex", supportsCloak: false, supportsModels: false, isGemini: false, isNew: true),
            "ai-providers-codex-edit" => CreateProviderEntryDescriptor("codex", "Codex", supportsCloak: false, supportsModels: false, isGemini: false, isNew: false),
            "ai-providers-claude-new" => CreateProviderEntryDescriptor("claude", "Claude", supportsCloak: true, supportsModels: true, isGemini: false, isNew: true),
            "ai-providers-claude-edit" => CreateProviderEntryDescriptor("claude", "Claude", supportsCloak: true, supportsModels: true, isGemini: false, isNew: false),
            "ai-providers-claude-models" => CreateModelDefinitionsDescriptor("claude", "Claude"),
            "ai-providers-vertex-new" => CreateProviderEntryDescriptor("vertex", "Vertex", supportsCloak: false, supportsModels: false, isGemini: false, isNew: true),
            "ai-providers-vertex-edit" => CreateProviderEntryDescriptor("vertex", "Vertex", supportsCloak: false, supportsModels: false, isGemini: false, isNew: false),
            "ai-providers-openai-new" => CreateOpenAiDescriptor(isNew: true),
            "ai-providers-openai-edit" => CreateOpenAiDescriptor(isNew: false),
            "ai-providers-openai-models" => CreateModelDefinitionsDescriptor("openai", "OpenAI Compatibility"),
            "ai-providers-ampcode" => CreateAmpCodeDescriptor(),
            _ => CreateOverviewDescriptor()
        };
    }

    private ManagementSecondaryRouteDescriptor CreateOverviewDescriptor()
    {
        _routeState.MarkClean();
        var body = new ManagementEmptyState
        {
            Title = "Select a provider route",
            Description = "Choose a provider card on the left to enter the native secondary page."
        };

        return new ManagementSecondaryRouteDescriptor(
            "Provider route host",
            "Secondary routes are rendered natively instead of inline JSON editors.",
            body);
    }

    private ManagementSecondaryRouteDescriptor CreateProviderEntryDescriptor(
        string providerKey,
        string displayName,
        bool supportsCloak,
        bool supportsModels,
        bool isGemini,
        bool isNew)
    {
        var entries = GetProviderEntries(providerKey);
        var selectedIndex = isNew ? entries.Count : _routeState.GetSelectedIndex(providerKey, entries.Count);
        var currentEntry = !isNew && entries.Count > 0 && selectedIndex < entries.Count
            ? entries[selectedIndex]
            : null;

        _routeState.SetRoute(providerKey, GetProviderRouteKey(providerKey, isNew), selectedIndex);
        _routeState.MarkClean();

        if (!isNew && currentEntry is null)
        {
            return CreateProviderEmptyDescriptor(providerKey, displayName, supportsModels);
        }

        var editor = new ProviderEntryEditorView(currentEntry, supportsCloak, _routeState.MarkDirty);

        return new ManagementSecondaryRouteDescriptor(
            isNew ? $"Add {displayName}" : $"{displayName} Entry #{selectedIndex + 1}",
            isNew
                ? $"Create a new {displayName} entry using field-level controls."
                : $"Edit entry #{selectedIndex + 1} of {entries.Count} with entry-level save and delete.",
            editor,
            BuildProviderHeaderActions(providerKey, displayName, entries, selectedIndex, supportsModels),
            BuildProviderFooter(providerKey, displayName, entries.Count, selectedIndex, isNew, isGemini, editor),
            "Back to providers");
    }

    private ManagementSecondaryRouteDescriptor CreateProviderEmptyDescriptor(string providerKey, string displayName, bool supportsModels)
    {
        var addButton = new Button
        {
            Content = $"Create {displayName} entry",
            Background = (Brush)Application.Current.Resources["ManagementAccentBrush"],
            Foreground = Brushes.White
        };
        addButton.Click += (_, _) =>
        {
            _routeState.SetRoute(providerKey, GetProviderRouteKey(providerKey, isNew: true), 0);
            _navigate(GetProviderRouteKey(providerKey, isNew: true), $"Create {displayName}");
        };

        var body = new ManagementEmptyState
        {
            Title = $"No {displayName} entries yet",
            Description = "This route no longer falls back to a JSON editor. Create an entry directly from the native form.",
            ActionContent = addButton
        };

        return new ManagementSecondaryRouteDescriptor(
            $"{displayName} entries",
            "The secondary route is present, but there is no saved entry to render yet.",
            body,
            BuildProviderHeaderActions(providerKey, displayName, [], 0, supportsModels),
            BackLabel: "Back to providers");
    }

    private StackPanel BuildProviderHeaderActions(
        string providerKey,
        string displayName,
        IReadOnlyList<ManagementProviderKeyConfiguration> entries,
        int selectedIndex,
        bool supportsModels)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };

        panel.Children.Add(new StatusBadge
        {
            Text = entries.Count == 0
                ? "No saved entries"
                : $"Entry {Math.Min(selectedIndex + 1, entries.Count)}/{entries.Count}",
            Tone = BadgeTone.Neutral
        });

        var entrySelector = new ComboBox
        {
            Width = 220,
            Margin = new Thickness(12, 0, 0, 0),
            IsEnabled = entries.Count > 0
        };

        for (var index = 0; index < entries.Count; index++)
        {
            entrySelector.Items.Add(new ComboBoxItem
            {
                Content = $"{displayName} #{index + 1} · {MaskSecret(entries[index].ApiKey)}",
                Tag = index
            });
        }

        if (entries.Count > 0)
        {
            entrySelector.SelectedIndex = Math.Clamp(selectedIndex, 0, entries.Count - 1);
            entrySelector.SelectionChanged += (_, _) =>
            {
                if (entrySelector.SelectedItem is not ComboBoxItem { Tag: int index })
                {
                    return;
                }

                _routeState.SetRoute(providerKey, GetProviderRouteKey(providerKey, isNew: false), index);
                _navigate(GetProviderRouteKey(providerKey, isNew: false), $"{displayName} entry #{index + 1}");
            };
        }

        panel.Children.Add(entrySelector);

        var newButton = new Button
        {
            Margin = new Thickness(12, 0, 0, 0),
            Content = "New entry"
        };
        newButton.Click += (_, _) =>
        {
            _routeState.SetRoute(providerKey, GetProviderRouteKey(providerKey, isNew: true), entries.Count);
            _navigate(GetProviderRouteKey(providerKey, isNew: true), $"Create {displayName}");
        };
        panel.Children.Add(newButton);

        if (supportsModels)
        {
            var modelsButton = new Button
            {
                Margin = new Thickness(12, 0, 0, 0),
                Content = "Model definitions"
            };
            modelsButton.Click += (_, _) =>
            {
                _routeState.SetRoute(providerKey, GetModelsRouteKey(providerKey), selectedIndex);
                _navigate(GetModelsRouteKey(providerKey), $"{displayName} model definitions");
            };
            panel.Children.Add(modelsButton);
        }

        return panel;
    }

    private StackPanel BuildProviderFooter(
        string providerKey,
        string displayName,
        int existingCount,
        int selectedIndex,
        bool isNew,
        bool isGemini,
        ProviderEntryEditorView editor)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var reloadButton = new Button
        {
            Content = "Reload"
        };
        reloadButton.Click += async (_, _) => await _refreshAsync();
        panel.Children.Add(reloadButton);

        if (!isNew && existingCount > 0)
        {
            var deleteButton = new Button
            {
                Content = "Delete"
            };
            deleteButton.Click += async (_, _) =>
            {
                if (!ManagementConfirmDialog.Confirm(Window.GetWindow(_owner), $"Delete {displayName}", $"Delete {displayName} entry #{selectedIndex + 1}?"))
                {
                    return;
                }

                try
                {
                    var entry = GetProviderEntries(providerKey)[selectedIndex];
                    await DeleteProviderEntryAsync(providerKey, entry);
                    _routeState.MarkClean();
                    var remainingCount = Math.Max(existingCount - 1, 0);
                    if (remainingCount == 0)
                    {
                        _routeState.SetRoute(providerKey, GetProviderRouteKey(providerKey, isNew: true), 0);
                    }
                    else
                    {
                        _routeState.SetRoute(providerKey, GetProviderRouteKey(providerKey, isNew: false), Math.Min(selectedIndex, remainingCount - 1));
                    }

                    await _refreshAsync();
                }
                catch (Exception exception)
                {
                    ManagementPageSupport.ShowError(_owner, $"{displayName} delete", exception);
                }
            };
            panel.Children.Add(deleteButton);
        }

        var saveButton = new Button
        {
            Content = isNew ? "Create" : "Save",
            Background = (Brush)Application.Current.Resources["ManagementAccentBrush"],
            Foreground = Brushes.White
        };
        saveButton.Click += async (_, _) =>
        {
            try
            {
                var targetIndex = isNew ? existingCount : selectedIndex;
                await SaveProviderEntryAsync(providerKey, targetIndex, isGemini, editor);
                _routeState.MarkClean();
                _routeState.SetRoute(providerKey, GetProviderRouteKey(providerKey, isNew: false), targetIndex);
                await _refreshAsync();
                if (isNew)
                {
                    _navigate(GetProviderRouteKey(providerKey, isNew: false), $"{displayName} entry #{targetIndex + 1}");
                }
            }
            catch (Exception exception)
            {
                ManagementPageSupport.ShowError(_owner, $"{displayName} save", exception);
            }
        };
        panel.Children.Add(saveButton);

        return panel;
    }

    private async Task SaveProviderEntryAsync(string providerKey, int targetIndex, bool isGemini, ProviderEntryEditorView editor)
    {
        if (isGemini)
        {
            await _authService.UpdateGeminiKeyAsync(targetIndex, editor.BuildGeminiConfiguration());
            return;
        }

        var configuration = editor.BuildProviderConfiguration();
        switch (providerKey)
        {
            case "codex":
                await _authService.UpdateCodexKeyAsync(targetIndex, configuration);
                break;
            case "claude":
                await _authService.UpdateClaudeKeyAsync(targetIndex, configuration);
                break;
            case "vertex":
                await _authService.UpdateVertexKeyAsync(targetIndex, configuration);
                break;
        }
    }

    private async Task DeleteProviderEntryAsync(string providerKey, ManagementProviderKeyConfiguration configuration)
    {
        switch (providerKey)
        {
            case "gemini":
                await _authService.DeleteGeminiKeyAsync(configuration.ApiKey, configuration.BaseUrl);
                break;
            case "codex":
                await _authService.DeleteCodexKeyAsync(configuration.ApiKey, configuration.BaseUrl);
                break;
            case "claude":
                await _authService.DeleteClaudeKeyAsync(configuration.ApiKey, configuration.BaseUrl);
                break;
            case "vertex":
                await _authService.DeleteVertexKeyAsync(configuration.ApiKey, configuration.BaseUrl);
                break;
        }
    }

    private ManagementSecondaryRouteDescriptor CreateOpenAiDescriptor(bool isNew)
    {
        var entries = _viewModel.OpenAi;
        var selectedIndex = isNew ? entries.Count : _routeState.GetSelectedIndex("openai", entries.Count);
        var currentEntry = !isNew && entries.Count > 0 && selectedIndex < entries.Count
            ? entries[selectedIndex]
            : null;

        _routeState.SetRoute("openai", isNew ? "ai-providers-openai-new" : "ai-providers-openai-edit", selectedIndex);
        _routeState.MarkClean();

        if (!isNew && currentEntry is null)
        {
            var action = new Button
            {
                Content = "Create OpenAI compatibility entry",
                Background = (Brush)Application.Current.Resources["ManagementAccentBrush"],
                Foreground = Brushes.White
            };
            action.Click += (_, _) =>
            {
                _routeState.SetRoute("openai", "ai-providers-openai-new", entries.Count);
                _navigate("ai-providers-openai-new", "Create OpenAI compatibility entry");
            };

            return new ManagementSecondaryRouteDescriptor(
                    "OpenAI compatibility",
                    "No saved compatibility entry is available yet.",
                    new ManagementEmptyState
                    {
                        Title = "No OpenAI compatibility entries",
                        Description = "Create an entry directly from the native form instead of editing raw JSON.",
                        ActionContent = action
                    },
                    BuildOpenAiHeader(entries, selectedIndex),
                    BackLabel: "Back to providers");
        }

        var editor = new OpenAiCompatibilityEditorView(currentEntry, _routeState.MarkDirty);

        return new ManagementSecondaryRouteDescriptor(
            isNew ? "Add OpenAI compatibility" : $"OpenAI compatibility #{selectedIndex + 1}",
            isNew
                ? "Create a compatibility provider entry with native controls."
                : $"Edit compatibility entry #{selectedIndex + 1} of {entries.Count}.",
            editor,
            BuildOpenAiHeader(entries, selectedIndex),
            BuildOpenAiFooter(entries.Count, selectedIndex, isNew, editor),
            "Back to providers");
    }

    private StackPanel BuildOpenAiHeader(IReadOnlyList<ManagementOpenAiCompatibilityEntry> entries, int selectedIndex)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };

        panel.Children.Add(new StatusBadge
        {
            Text = entries.Count == 0
                ? "No saved entries"
                : $"Entry {Math.Min(selectedIndex + 1, entries.Count)}/{entries.Count}",
            Tone = BadgeTone.Neutral
        });

        var selector = new ComboBox
        {
            Width = 220,
            Margin = new Thickness(12, 0, 0, 0),
            IsEnabled = entries.Count > 0
        };

        for (var index = 0; index < entries.Count; index++)
        {
            selector.Items.Add(new ComboBoxItem
            {
                Content = $"{entries[index].Name} · {entries[index].BaseUrl}",
                Tag = index
            });
        }

        if (entries.Count > 0)
        {
            selector.SelectedIndex = Math.Clamp(selectedIndex, 0, entries.Count - 1);
            selector.SelectionChanged += (_, _) =>
            {
                if (selector.SelectedItem is not ComboBoxItem { Tag: int index })
                {
                    return;
                }

                _routeState.SetRoute("openai", "ai-providers-openai-edit", index);
                _navigate("ai-providers-openai-edit", $"OpenAI compatibility #{index + 1}");
            };
        }

        panel.Children.Add(selector);

        var newButton = new Button
        {
            Margin = new Thickness(12, 0, 0, 0),
            Content = "New entry"
        };
        newButton.Click += (_, _) =>
        {
            _routeState.SetRoute("openai", "ai-providers-openai-new", entries.Count);
            _navigate("ai-providers-openai-new", "Create OpenAI compatibility entry");
        };
        panel.Children.Add(newButton);

        var modelsButton = new Button
        {
            Margin = new Thickness(12, 0, 0, 0),
            Content = "Model definitions"
        };
        modelsButton.Click += (_, _) =>
        {
            _routeState.SetRoute("openai", "ai-providers-openai-models", selectedIndex);
            _navigate("ai-providers-openai-models", "OpenAI model definitions");
        };
        panel.Children.Add(modelsButton);

        return panel;
    }

    private StackPanel BuildOpenAiFooter(int existingCount, int selectedIndex, bool isNew, OpenAiCompatibilityEditorView editor)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var reloadButton = new Button
        {
            Content = "Reload"
        };
        reloadButton.Click += async (_, _) => await _refreshAsync();
        panel.Children.Add(reloadButton);

        if (!isNew && existingCount > 0)
        {
            var deleteButton = new Button
            {
                Content = "Delete"
            };
            deleteButton.Click += async (_, _) =>
            {
                if (!ManagementConfirmDialog.Confirm(Window.GetWindow(_owner), "Delete OpenAI compatibility", $"Delete entry #{selectedIndex + 1}?"))
                {
                    return;
                }

                try
                {
                    await _authService.DeleteOpenAiCompatibilityAsync(_viewModel.OpenAi[selectedIndex].Name);
                    _routeState.MarkClean();
                    var remainingCount = Math.Max(existingCount - 1, 0);
                    if (remainingCount == 0)
                    {
                        _routeState.SetRoute("openai", "ai-providers-openai-new", 0);
                    }
                    else
                    {
                        _routeState.SetRoute("openai", "ai-providers-openai-edit", Math.Min(selectedIndex, remainingCount - 1));
                    }

                    await _refreshAsync();
                }
                catch (Exception exception)
                {
                    ManagementPageSupport.ShowError(_owner, "OpenAI delete", exception);
                }
            };
            panel.Children.Add(deleteButton);
        }

        var saveButton = new Button
        {
            Content = isNew ? "Create" : "Save",
            Background = (Brush)Application.Current.Resources["ManagementAccentBrush"],
            Foreground = Brushes.White
        };
        saveButton.Click += async (_, _) =>
        {
            try
            {
                var targetIndex = isNew ? existingCount : selectedIndex;
                await _authService.UpdateOpenAiCompatibilityAsync(targetIndex, editor.BuildEntry());
                _routeState.MarkClean();
                _routeState.SetRoute("openai", "ai-providers-openai-edit", targetIndex);
                await _refreshAsync();
                if (isNew)
                {
                    _navigate("ai-providers-openai-edit", $"OpenAI compatibility #{targetIndex + 1}");
                }
            }
            catch (Exception exception)
            {
                ManagementPageSupport.ShowError(_owner, "OpenAI save", exception);
            }
        };
        panel.Children.Add(saveButton);

        return panel;
    }

    private ManagementSecondaryRouteDescriptor CreateModelDefinitionsDescriptor(string channel, string displayName)
    {
        _routeState.MarkClean();

        var view = new ModelDefinitionsRouteView(_authService, channel);
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };

        header.Children.Add(new StatusBadge
        {
            Text = "Source: GetModelDefinitionsAsync",
            Tone = BadgeTone.Neutral
        });

        var editButton = new Button
        {
            Margin = new Thickness(12, 0, 0, 0),
            Content = "Back to entries"
        };
        editButton.Click += (_, _) =>
        {
            var routeKey = GetEditableRouteKey(channel);
            _routeState.SetRoute(channel, routeKey, _routeState.GetSelectedIndex(channel, Math.Max(GetProviderEntryCount(channel), 1)));
            _navigate(routeKey, $"{displayName} entries");
        };
        header.Children.Add(editButton);

        return new ManagementSecondaryRouteDescriptor(
            $"{displayName} model definitions",
            "Definitions come from GetModelDefinitionsAsync(channel). Empty results stay as an empty state.",
            view,
            header,
            BackLabel: "Back to providers");
    }

    private ManagementSecondaryRouteDescriptor CreateAmpCodeDescriptor()
    {
        var editor = new AmpCodeEditorView(_viewModel.AmpCode ?? new ManagementAmpCodeConfiguration(), _routeState.MarkDirty);
        _routeState.SetRoute("ampcode", "ai-providers-ampcode", 0);
        _routeState.MarkClean();

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        header.Children.Add(new StatusBadge
        {
            Text = $"{editor.ModelMappingCount} model mappings · {editor.UpstreamMappingCount} upstream mappings",
            Tone = BadgeTone.Neutral
        });

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var reloadButton = new Button
        {
            Content = "Reload"
        };
        reloadButton.Click += async (_, _) => await _refreshAsync();
        footer.Children.Add(reloadButton);

        var saveButton = new Button
        {
            Content = "Save",
            Background = (Brush)Application.Current.Resources["ManagementAccentBrush"],
            Foreground = Brushes.White
        };
        saveButton.Click += async (_, _) =>
        {
            try
            {
                var draft = editor.BuildConfiguration();
                await _authService.UpdateAmpUpstreamUrlAsync(draft.UpstreamUrl);
                await _authService.UpdateAmpUpstreamApiKeyAsync(draft.UpstreamApiKey);
                await _authService.SetAmpForceModelMappingsAsync(draft.ForceModelMappings == true);
                await _authService.ReplaceAmpModelMappingsAsync(draft.ModelMappings);
                await _authService.ReplaceAmpUpstreamApiKeysAsync(draft.UpstreamApiKeys);
                _routeState.MarkClean();
                await _refreshAsync();
            }
            catch (Exception exception)
            {
                ManagementPageSupport.ShowError(_owner, "AmpCode save", exception);
            }
        };
        footer.Children.Add(saveButton);

        return new ManagementSecondaryRouteDescriptor(
            "AmpCode",
            "Base settings, model mappings and upstream key mappings are edited in one native secondary page.",
            editor,
            header,
            footer,
            "Back to providers");
    }

    private IReadOnlyList<ManagementProviderKeyConfiguration> GetProviderEntries(string providerKey)
    {
        return providerKey switch
        {
            "gemini" => _viewModel.Gemini.Cast<ManagementProviderKeyConfiguration>().ToArray(),
            "codex" => _viewModel.Codex,
            "claude" => _viewModel.Claude,
            "vertex" => _viewModel.Vertex,
            _ => []
        };
    }

    private int GetProviderEntryCount(string providerKey)
    {
        return providerKey switch
        {
            "gemini" => _viewModel.Gemini.Count,
            "codex" => _viewModel.Codex.Count,
            "claude" => _viewModel.Claude.Count,
            "vertex" => _viewModel.Vertex.Count,
            "openai" => _viewModel.OpenAi.Count,
            _ => 0
        };
    }

    private static string GetProviderRouteKey(string providerKey, bool isNew)
    {
        return providerKey switch
        {
            "gemini" => isNew ? "ai-providers-gemini-new" : "ai-providers-gemini-edit",
            "codex" => isNew ? "ai-providers-codex-new" : "ai-providers-codex-edit",
            "claude" => isNew ? "ai-providers-claude-new" : "ai-providers-claude-edit",
            "vertex" => isNew ? "ai-providers-vertex-new" : "ai-providers-vertex-edit",
            _ => "ai-providers"
        };
    }

    private static string GetEditableRouteKey(string providerKey)
    {
        return providerKey switch
        {
            "openai" => "ai-providers-openai-edit",
            _ => GetProviderRouteKey(providerKey, isNew: false)
        };
    }

    private static string GetModelsRouteKey(string providerKey)
    {
        return providerKey switch
        {
            "claude" => "ai-providers-claude-models",
            "openai" => "ai-providers-openai-models",
            _ => "ai-providers"
        };
    }

    private static string MaskSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "empty";
        }

        return value.Length <= 8
            ? value
            : $"{value[..4]}...{value[^4..]}";
    }

    private static ManagementFieldRow CreateFieldRow(string label, Control control, string? hint = null)
    {
        return new ManagementFieldRow
        {
            Label = label,
            Hint = hint ?? string.Empty,
            FieldContent = control
        };
    }

    private sealed class ProviderEntryEditorView : UserControl
    {
        private readonly TextBox _apiKeyTextBox = new();
        private readonly TextBox _priorityTextBox = new();
        private readonly TextBox _prefixTextBox = new();
        private readonly TextBox _baseUrlTextBox = new();
        private readonly CheckBox _webSocketsCheckBox = new();
        private readonly TextBox _proxyUrlTextBox = new();
        private readonly TextBox _authIndexTextBox = new();
        private readonly ObservableCollection<EditableKeyValueItem> _headers = [];
        private readonly ObservableCollection<EditableModelAliasItem> _models = [];
        private readonly ObservableCollection<EditableStringItem> _excludedModels = [];
        private readonly TextBox? _cloakModeTextBox;
        private readonly CheckBox? _cloakStrictModeCheckBox;
        private readonly ObservableCollection<EditableStringItem>? _cloakSensitiveWords;

        public ProviderEntryEditorView(ManagementProviderKeyConfiguration? configuration, bool supportsCloak, Action onDirty)
        {
            if (supportsCloak)
            {
                _cloakModeTextBox = new TextBox();
                _cloakStrictModeCheckBox = new CheckBox();
                _cloakSensitiveWords = [];
            }

            Content = BuildLayout(supportsCloak);
            Apply(configuration);
            WireDirtyTracking(onDirty);
        }

        public ManagementProviderKeyConfiguration BuildProviderConfiguration()
        {
            var cloak = _cloakModeTextBox is null || _cloakSensitiveWords is null || _cloakStrictModeCheckBox is null
                ? null
                : BuildCloak();

            return new ManagementProviderKeyConfiguration
            {
                ApiKey = _apiKeyTextBox.Text.Trim(),
                Priority = ParseNullableInt(_priorityTextBox.Text),
                Prefix = NormalizeText(_prefixTextBox.Text),
                BaseUrl = NormalizeText(_baseUrlTextBox.Text),
                WebSockets = _webSocketsCheckBox.IsChecked == true,
                ProxyUrl = NormalizeText(_proxyUrlTextBox.Text),
                Headers = BuildHeaders(_headers),
                Models = BuildModelAliases(_models),
                ExcludedModels = BuildStrings(_excludedModels),
                Cloak = cloak,
                AuthIndex = NormalizeText(_authIndexTextBox.Text)
            };
        }

        public ManagementGeminiKeyConfiguration BuildGeminiConfiguration()
        {
            var provider = BuildProviderConfiguration();
            return new ManagementGeminiKeyConfiguration
            {
                ApiKey = provider.ApiKey,
                Priority = provider.Priority,
                Prefix = provider.Prefix,
                BaseUrl = provider.BaseUrl,
                WebSockets = provider.WebSockets,
                ProxyUrl = provider.ProxyUrl,
                Headers = provider.Headers,
                Models = provider.Models,
                ExcludedModels = provider.ExcludedModels,
                Cloak = provider.Cloak,
                AuthIndex = provider.AuthIndex
            };
        }

        private StackPanel BuildLayout(bool supportsCloak)
        {
            var root = new StackPanel();
            var connection = new StackPanel();
            connection.Children.Add(CreateFieldRow("API key", _apiKeyTextBox));
            connection.Children.Add(CreateFieldRow("Priority", _priorityTextBox, "Optional integer."));
            connection.Children.Add(CreateFieldRow("Prefix", _prefixTextBox));
            connection.Children.Add(CreateFieldRow("Base URL", _baseUrlTextBox));
            connection.Children.Add(CreateFieldRow("WebSockets", _webSocketsCheckBox));
            connection.Children.Add(CreateFieldRow("Proxy URL", _proxyUrlTextBox));
            connection.Children.Add(CreateFieldRow("Auth index", _authIndexTextBox));
            root.Children.Add(new ManagementFormSection
            {
                Title = "Connection",
                Description = "Field-level edits are saved through Update*KeyAsync instead of Replace*.",
                SectionContent = connection
            });

            root.Children.Add(new ManagementFormSection
            {
                Title = "Headers",
                Description = "Additional request headers sent with this provider entry.",
                SectionContent = new EditableKeyValueList
                {
                    ItemsSource = _headers,
                    KeyHeader = "Header",
                    ValueHeader = "Value",
                    AddButtonText = "Add header"
                }
            });

            root.Children.Add(new ManagementFormSection
            {
                Title = "Model aliases",
                Description = "Optional aliases, priorities and test models.",
                SectionContent = new EditableModelAliasList
                {
                    ItemsSource = _models,
                    ShowPriority = true,
                    ShowTestModel = true,
                    AddButtonText = "Add alias"
                }
            });

            root.Children.Add(new ManagementFormSection
            {
                Title = "Excluded models",
                Description = "Model ids excluded for this provider entry.",
                SectionContent = new EditableStringList
                {
                    ItemsSource = _excludedModels,
                    AddButtonText = "Add excluded model"
                }
            });

            if (supportsCloak && _cloakModeTextBox is not null && _cloakStrictModeCheckBox is not null && _cloakSensitiveWords is not null)
            {
                var cloak = new StackPanel();
                cloak.Children.Add(CreateFieldRow("Mode", _cloakModeTextBox));
                cloak.Children.Add(CreateFieldRow("Strict mode", _cloakStrictModeCheckBox));
                cloak.Children.Add(new ManagementFieldRow
                {
                    Label = "Sensitive words",
                    FieldContent = new EditableStringList
                    {
                        ItemsSource = _cloakSensitiveWords,
                        AddButtonText = "Add sensitive word"
                    }
                });

                root.Children.Add(new ManagementFormSection
                {
                    Title = "Cloak",
                    Description = "Claude-specific cloak controls stay on the native route.",
                    SectionContent = cloak
                });
            }

            return root;
        }

        private void Apply(ManagementProviderKeyConfiguration? configuration)
        {
            if (configuration is null)
            {
                return;
            }

            _apiKeyTextBox.Text = configuration.ApiKey;
            _priorityTextBox.Text = configuration.Priority?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            _prefixTextBox.Text = configuration.Prefix ?? string.Empty;
            _baseUrlTextBox.Text = configuration.BaseUrl ?? string.Empty;
            _webSocketsCheckBox.IsChecked = configuration.WebSockets == true;
            _proxyUrlTextBox.Text = configuration.ProxyUrl ?? string.Empty;
            _authIndexTextBox.Text = configuration.AuthIndex ?? string.Empty;

            foreach (var header in configuration.Headers)
            {
                _headers.Add(new EditableKeyValueItem { Key = header.Key, Value = header.Value });
            }

            foreach (var model in configuration.Models)
            {
                _models.Add(new EditableModelAliasItem
                {
                    Name = model.Name,
                    Alias = model.Alias ?? string.Empty,
                    Priority = model.Priority?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    TestModel = model.TestModel ?? string.Empty
                });
            }

            foreach (var model in configuration.ExcludedModels)
            {
                _excludedModels.Add(new EditableStringItem { Value = model });
            }

            if (_cloakModeTextBox is not null && _cloakStrictModeCheckBox is not null && _cloakSensitiveWords is not null)
            {
                _cloakModeTextBox.Text = configuration.Cloak?.Mode ?? string.Empty;
                _cloakStrictModeCheckBox.IsChecked = configuration.Cloak?.StrictMode == true;
                foreach (var word in configuration.Cloak?.SensitiveWords ?? [])
                {
                    _cloakSensitiveWords.Add(new EditableStringItem { Value = word });
                }
            }
        }

        private void WireDirtyTracking(Action onDirty)
        {
            _apiKeyTextBox.TextChanged += (_, _) => onDirty();
            _priorityTextBox.TextChanged += (_, _) => onDirty();
            _prefixTextBox.TextChanged += (_, _) => onDirty();
            _baseUrlTextBox.TextChanged += (_, _) => onDirty();
            _webSocketsCheckBox.Click += (_, _) => onDirty();
            _proxyUrlTextBox.TextChanged += (_, _) => onDirty();
            _authIndexTextBox.TextChanged += (_, _) => onDirty();
            SubscribeListChanges(Content as DependencyObject, onDirty);

            if (_cloakModeTextBox is not null)
            {
                _cloakModeTextBox.TextChanged += (_, _) => onDirty();
            }

            if (_cloakStrictModeCheckBox is not null)
            {
                _cloakStrictModeCheckBox.Click += (_, _) => onDirty();
            }
        }

        private ManagementCloakConfiguration? BuildCloak()
        {
            var mode = NormalizeText(_cloakModeTextBox?.Text);
            var strictMode = _cloakStrictModeCheckBox?.IsChecked == true;
            var sensitiveWords = _cloakSensitiveWords is null ? [] : BuildStrings(_cloakSensitiveWords);

            if (mode is null && !strictMode && sensitiveWords.Length == 0)
            {
                return null;
            }

            return new ManagementCloakConfiguration
            {
                Mode = mode,
                StrictMode = strictMode,
                SensitiveWords = sensitiveWords
            };
        }
    }

    private sealed class OpenAiCompatibilityEditorView : UserControl
    {
        private readonly TextBox _nameTextBox = new();
        private readonly TextBox _prefixTextBox = new();
        private readonly TextBox _baseUrlTextBox = new();
        private readonly TextBox _priorityTextBox = new();
        private readonly TextBox _testModelTextBox = new();
        private readonly TextBox _authIndexTextBox = new();
        private readonly ObservableCollection<EditableKeyValueItem> _headers = [];
        private readonly ObservableCollection<EditableModelAliasItem> _models = [];
        private readonly ObservableCollection<OpenAiApiKeyEntryDraft> _apiKeyEntries = [];
        private readonly ItemsControl _apiKeyEntriesHost = new();

        public OpenAiCompatibilityEditorView(ManagementOpenAiCompatibilityEntry? entry, Action onDirty)
        {
            Content = BuildLayout();
            Apply(entry);
            WireDirtyTracking(onDirty);
        }

        public ManagementOpenAiCompatibilityEntry BuildEntry()
        {
            return new ManagementOpenAiCompatibilityEntry
            {
                Name = _nameTextBox.Text.Trim(),
                Prefix = NormalizeText(_prefixTextBox.Text),
                BaseUrl = _baseUrlTextBox.Text.Trim(),
                Headers = BuildHeaders(_headers),
                Models = BuildModelAliases(_models),
                Priority = ParseNullableInt(_priorityTextBox.Text),
                TestModel = NormalizeText(_testModelTextBox.Text),
                AuthIndex = NormalizeText(_authIndexTextBox.Text),
                ApiKeyEntries = _apiKeyEntries
                    .Where(item => !string.IsNullOrWhiteSpace(item.ApiKey))
                    .Select(item => new ManagementApiKeyEntry
                    {
                        ApiKey = item.ApiKey.Trim(),
                        ProxyUrl = NormalizeText(item.ProxyUrl),
                        Headers = BuildHeaders(item.Headers),
                        AuthIndex = NormalizeText(item.AuthIndex)
                    })
                    .ToArray()
            };
        }

        private StackPanel BuildLayout()
        {
            var root = new StackPanel();
            var basic = new StackPanel();
            basic.Children.Add(CreateFieldRow("Name", _nameTextBox));
            basic.Children.Add(CreateFieldRow("Prefix", _prefixTextBox));
            basic.Children.Add(CreateFieldRow("Base URL", _baseUrlTextBox));
            basic.Children.Add(CreateFieldRow("Priority", _priorityTextBox));
            basic.Children.Add(CreateFieldRow("Test model", _testModelTextBox));
            basic.Children.Add(CreateFieldRow("Auth index", _authIndexTextBox));
            root.Children.Add(new ManagementFormSection
            {
                Title = "Provider",
                Description = "OpenAI compatibility providers are edited entry by entry.",
                SectionContent = basic
            });

            root.Children.Add(new ManagementFormSection
            {
                Title = "Headers",
                Description = "Headers applied to the compatibility provider itself.",
                SectionContent = new EditableKeyValueList
                {
                    ItemsSource = _headers,
                    KeyHeader = "Header",
                    ValueHeader = "Value",
                    AddButtonText = "Add header"
                }
            });

            root.Children.Add(new ManagementFormSection
            {
                Title = "Model aliases",
                Description = "Compatibility aliases and optional priority/test-model metadata.",
                SectionContent = new EditableModelAliasList
                {
                    ItemsSource = _models,
                    ShowPriority = true,
                    ShowTestModel = true,
                    AddButtonText = "Add alias"
                }
            });

            _apiKeyEntriesHost.ItemTemplate = BuildOpenAiApiKeyEntryTemplate();
            _apiKeyEntriesHost.ItemsSource = _apiKeyEntries;

            var apiKeyEntriesPanel = new StackPanel();
            apiKeyEntriesPanel.Children.Add(_apiKeyEntriesHost);
            var addButton = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Content = "Add API key entry"
            };
            addButton.Click += (_, _) => _apiKeyEntries.Add(new OpenAiApiKeyEntryDraft());
            apiKeyEntriesPanel.Children.Add(addButton);

            root.Children.Add(new ManagementFormSection
            {
                Title = "API key entries",
                Description = "Each entry can carry its own proxy, auth index and headers.",
                SectionContent = apiKeyEntriesPanel
            });

            return root;
        }

        private static DataTemplate BuildOpenAiApiKeyEntryTemplate()
        {
            const string xaml = """
<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
              xmlns:ds="clr-namespace:CodexCliPlus.Management.DesignSystem.Controls;assembly=CodexCliPlus.Management.DesignSystem">
    <Border Margin="0,0,0,12"
            Padding="14"
            Background="{StaticResource ManagementSurfaceBrush}"
            BorderBrush="{StaticResource ManagementBorderBrush}"
            BorderThickness="1"
            CornerRadius="14">
        <StackPanel>
            <Grid Margin="0,0,0,10">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <TextBox Text="{Binding ApiKey, UpdateSourceTrigger=PropertyChanged}" />
                <TextBox Grid.Column="1"
                         Margin="8,0,0,0"
                         Text="{Binding ProxyUrl, UpdateSourceTrigger=PropertyChanged}" />
                <TextBox Grid.Column="2"
                         Margin="8,0,0,0"
                         Text="{Binding AuthIndex, UpdateSourceTrigger=PropertyChanged}" />
            </Grid>
            <ds:EditableKeyValueList ItemsSource="{Binding Headers}"
                                     KeyHeader="Header"
                                     ValueHeader="Value"
                                     AddButtonText="Add header" />
        </StackPanel>
    </Border>
</DataTemplate>
""";

            return (DataTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        private void Apply(ManagementOpenAiCompatibilityEntry? entry)
        {
            if (entry is null)
            {
                return;
            }

            _nameTextBox.Text = entry.Name;
            _prefixTextBox.Text = entry.Prefix ?? string.Empty;
            _baseUrlTextBox.Text = entry.BaseUrl;
            _priorityTextBox.Text = entry.Priority?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            _testModelTextBox.Text = entry.TestModel ?? string.Empty;
            _authIndexTextBox.Text = entry.AuthIndex ?? string.Empty;

            foreach (var header in entry.Headers)
            {
                _headers.Add(new EditableKeyValueItem { Key = header.Key, Value = header.Value });
            }

            foreach (var model in entry.Models)
            {
                _models.Add(new EditableModelAliasItem
                {
                    Name = model.Name,
                    Alias = model.Alias ?? string.Empty,
                    Priority = model.Priority?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    TestModel = model.TestModel ?? string.Empty
                });
            }

            foreach (var apiKeyEntry in entry.ApiKeyEntries)
            {
                var draft = new OpenAiApiKeyEntryDraft
                {
                    ApiKey = apiKeyEntry.ApiKey,
                    ProxyUrl = apiKeyEntry.ProxyUrl ?? string.Empty,
                    AuthIndex = apiKeyEntry.AuthIndex ?? string.Empty
                };

                foreach (var header in apiKeyEntry.Headers)
                {
                    draft.Headers.Add(new EditableKeyValueItem { Key = header.Key, Value = header.Value });
                }

                _apiKeyEntries.Add(draft);
            }
        }

        private void WireDirtyTracking(Action onDirty)
        {
            _nameTextBox.TextChanged += (_, _) => onDirty();
            _prefixTextBox.TextChanged += (_, _) => onDirty();
            _baseUrlTextBox.TextChanged += (_, _) => onDirty();
            _priorityTextBox.TextChanged += (_, _) => onDirty();
            _testModelTextBox.TextChanged += (_, _) => onDirty();
            _authIndexTextBox.TextChanged += (_, _) => onDirty();
            SubscribeListChanges(Content as DependencyObject, onDirty);

            _apiKeyEntries.CollectionChanged += (_, _) => onDirty();
            foreach (var draft in _apiKeyEntries)
            {
                HookOpenAiDraft(draft, onDirty);
            }

            _apiKeyEntries.CollectionChanged += (_, args) =>
            {
                foreach (var draft in args.NewItems?.OfType<OpenAiApiKeyEntryDraft>() ?? [])
                {
                    HookOpenAiDraft(draft, onDirty);
                }
            };
        }

        private static void HookOpenAiDraft(OpenAiApiKeyEntryDraft draft, Action onDirty)
        {
            draft.PropertyChanged += (_, _) => onDirty();
            draft.Headers.CollectionChanged += (_, _) => onDirty();
            foreach (var header in draft.Headers)
            {
                header.PropertyChanged += (_, _) => onDirty();
            }

            draft.Headers.CollectionChanged += (_, args) =>
            {
                foreach (var header in args.NewItems?.OfType<EditableKeyValueItem>() ?? [])
                {
                    header.PropertyChanged += (_, _) => onDirty();
                }
            };
        }
    }

    private sealed class ModelDefinitionsRouteView : UserControl
    {
        private readonly IManagementAuthFilesService _authFilesService;
        private readonly string _channel;
        private readonly StackPanel _root = new();
        private bool _loaded;

        public ModelDefinitionsRouteView(IManagementAuthFilesService authFilesService, string channel)
        {
            _authFilesService = authFilesService;
            _channel = channel;
            Content = _root;
            Loaded += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _root.Children.Clear();
            _root.Children.Add(new TextBlock
            {
                Text = "Loading model definitions...",
                Foreground = (Brush)Application.Current.Resources["ManagementSecondaryTextBrush"]
            });

            try
            {
                var definitions = (await _authFilesService.GetModelDefinitionsAsync(_channel)).Value;
                _root.Children.Clear();

                if (definitions.Count == 0)
                {
                    _root.Children.Add(new ManagementEmptyState
                    {
                        Title = "No model definitions",
                        Description = "GetModelDefinitionsAsync(channel) returned an empty collection."
                    });
                    return;
                }

                foreach (var definition in definitions)
                {
                    _root.Children.Add(CreateModelDescriptorCard(definition));
                }
            }
            catch (Exception exception)
            {
                _root.Children.Clear();
                _root.Children.Add(new TextBlock
                {
                    Text = exception.Message,
                    Foreground = (Brush)Application.Current.Resources["ManagementDangerBrush"]
                });
            }
        }
    }

    private sealed class AmpCodeEditorView : UserControl
    {
        private readonly TextBox _upstreamUrlTextBox = new();
        private readonly TextBox _upstreamApiKeyTextBox = new();
        private readonly CheckBox _forceModelMappingsCheckBox = new();
        private readonly ObservableCollection<EditableKeyValueItem> _modelMappings = [];
        private readonly ObservableCollection<EditableKeyValueItem> _upstreamKeyMappings = [];

        public AmpCodeEditorView(ManagementAmpCodeConfiguration configuration, Action onDirty)
        {
            Content = BuildLayout();
            Apply(configuration);
            WireDirtyTracking(onDirty);
        }

        public int ModelMappingCount => _modelMappings.Count;

        public int UpstreamMappingCount => _upstreamKeyMappings.Count;

        public ManagementAmpCodeConfiguration BuildConfiguration()
        {
            return new ManagementAmpCodeConfiguration
            {
                UpstreamUrl = NormalizeText(_upstreamUrlTextBox.Text),
                UpstreamApiKey = NormalizeText(_upstreamApiKeyTextBox.Text),
                ForceModelMappings = _forceModelMappingsCheckBox.IsChecked == true,
                ModelMappings = _modelMappings
                    .Where(item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
                    .Select(item => new ManagementAmpCodeModelMapping
                    {
                        From = item.Key.Trim(),
                        To = item.Value.Trim()
                    })
                    .ToArray(),
                UpstreamApiKeys = _upstreamKeyMappings
                    .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                    .Select(item => new ManagementAmpCodeUpstreamApiKeyMapping
                    {
                        UpstreamApiKey = item.Key.Trim(),
                        ApiKeys = item.Value
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray()
                    })
                    .Where(item => item.ApiKeys.Count > 0)
                    .ToArray()
            };
        }

        private StackPanel BuildLayout()
        {
            var root = new StackPanel();
            var basic = new StackPanel();
            basic.Children.Add(CreateFieldRow("Upstream URL", _upstreamUrlTextBox));
            basic.Children.Add(CreateFieldRow("Upstream API key", _upstreamApiKeyTextBox));
            basic.Children.Add(CreateFieldRow("Force model mappings", _forceModelMappingsCheckBox));
            root.Children.Add(new ManagementFormSection
            {
                Title = "Base settings",
                Description = "Saved through UpdateAmp* and SetAmpForceModelMappingsAsync.",
                SectionContent = basic
            });

            root.Children.Add(new ManagementFormSection
            {
                Title = "Model mappings",
                Description = "Saved through ReplaceAmpModelMappingsAsync.",
                SectionContent = new EditableKeyValueList
                {
                    ItemsSource = _modelMappings,
                    KeyHeader = "From",
                    ValueHeader = "To",
                    AddButtonText = "Add mapping"
                }
            });

            root.Children.Add(new ManagementFormSection
            {
                Title = "Upstream key mappings",
                Description = "Value accepts comma-separated local API keys.",
                SectionContent = new EditableKeyValueList
                {
                    ItemsSource = _upstreamKeyMappings,
                    KeyHeader = "Upstream API key",
                    ValueHeader = "API keys",
                    AddButtonText = "Add upstream mapping"
                }
            });

            return root;
        }

        private void Apply(ManagementAmpCodeConfiguration configuration)
        {
            _upstreamUrlTextBox.Text = configuration.UpstreamUrl ?? string.Empty;
            _upstreamApiKeyTextBox.Text = configuration.UpstreamApiKey ?? string.Empty;
            _forceModelMappingsCheckBox.IsChecked = configuration.ForceModelMappings == true;

            foreach (var mapping in configuration.ModelMappings)
            {
                _modelMappings.Add(new EditableKeyValueItem { Key = mapping.From, Value = mapping.To });
            }

            foreach (var mapping in configuration.UpstreamApiKeys)
            {
                _upstreamKeyMappings.Add(new EditableKeyValueItem
                {
                    Key = mapping.UpstreamApiKey,
                    Value = string.Join(", ", mapping.ApiKeys)
                });
            }
        }

        private void WireDirtyTracking(Action onDirty)
        {
            _upstreamUrlTextBox.TextChanged += (_, _) => onDirty();
            _upstreamApiKeyTextBox.TextChanged += (_, _) => onDirty();
            _forceModelMappingsCheckBox.Click += (_, _) => onDirty();

            SubscribeListChanges(Content as DependencyObject, onDirty);
        }
    }

    private sealed class OpenAiApiKeyEntryDraft : EditableItemBase
    {
        private string _apiKey = string.Empty;
        private string _proxyUrl = string.Empty;
        private string _authIndex = string.Empty;

        public ObservableCollection<EditableKeyValueItem> Headers { get; } = [];

        public string ApiKey
        {
            get => _apiKey;
            set => SetProperty(ref _apiKey, value);
        }

        public string ProxyUrl
        {
            get => _proxyUrl;
            set => SetProperty(ref _proxyUrl, value);
        }

        public string AuthIndex
        {
            get => _authIndex;
            set => SetProperty(ref _authIndex, value);
        }
    }

    private static Border CreateModelDescriptorCard(ManagementModelDescriptor definition)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["ManagementPrimaryTextBrush"],
            Text = definition.Id
        });

        var meta = string.Join(
            " · ",
            new[]
            {
                definition.DisplayName,
                definition.Type,
                definition.OwnedBy,
                definition.Alias
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

        if (!string.IsNullOrWhiteSpace(meta))
        {
            stack.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                Style = (Style)Application.Current.Resources["ManagementHintTextStyle"],
                Text = meta
            });
        }

        if (!string.IsNullOrWhiteSpace(definition.Description))
        {
            stack.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 10, 0, 0),
                Text = definition.Description,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["ManagementPrimaryTextBrush"]
            });
        }

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(16),
            Background = (Brush)Application.Current.Resources["ManagementSurfaceSubtleBrush"],
            BorderBrush = (Brush)Application.Current.Resources["ManagementBorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Child = stack
        };
    }

    private static Dictionary<string, string> BuildHeaders(IEnumerable<EditableKeyValueItem> items)
    {
        return items
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value.Trim(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static ManagementModelAlias[] BuildModelAliases(IEnumerable<EditableModelAliasItem> items)
    {
        return items
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => new ManagementModelAlias
            {
                Name = item.Name.Trim(),
                Alias = NormalizeText(item.Alias),
                Priority = ParseNullableInt(item.Priority),
                TestModel = NormalizeText(item.TestModel)
            })
            .ToArray();
    }

    private static string[] BuildStrings(IEnumerable<EditableStringItem> items)
    {
        return items
            .Select(item => item.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int? ParseNullableInt(string? text)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string? NormalizeText(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static void SubscribeListChanges(DependencyObject? root, Action onDirty)
    {
        if (root is null)
        {
            return;
        }

        foreach (var child in EnumerateVisualTree(root))
        {
            switch (child)
            {
                case EditableKeyValueList keyValueList:
                    keyValueList.Changed += (_, _) => onDirty();
                    break;
                case EditableStringList stringList:
                    stringList.Changed += (_, _) => onDirty();
                    break;
                case EditableModelAliasList modelAliasList:
                    modelAliasList.Changed += (_, _) => onDirty();
                    break;
            }
        }
    }

    private static IEnumerable<DependencyObject> EnumerateVisualTree(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in EnumerateVisualTree(child))
            {
                yield return descendant;
            }
        }
    }
}
