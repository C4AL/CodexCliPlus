using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CPAD.Core.Abstractions.Management;
using CPAD.Core.Models.Management;
using CPAD.Management.DesignSystem.Controls;
using CPAD.Services.SecondaryRoutes;
using CPAD.ViewModels.Pages;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace CPAD.Views.Pages;

internal sealed class AuthFilesSecondaryRouteViewFactory : IManagementSecondaryRouteViewFactory
{
    private readonly AuthFilesPageViewModel _viewModel;
    private readonly IManagementAuthService _authService;
    private readonly Page _owner;
    private readonly AuthFilesRouteState _routeState;
    private readonly Func<Task> _refreshAsync;
    private readonly Func<string, string, bool> _navigate;

    public AuthFilesSecondaryRouteViewFactory(
        AuthFilesPageViewModel viewModel,
        IManagementAuthService authService,
        Page owner,
        AuthFilesRouteState routeState,
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
            "auth-files-oauth-excluded" => CreateOAuthExcludedDescriptor(),
            "auth-files-oauth-model-alias" => CreateOAuthModelAliasDescriptor(),
            _ => CreateDetailsDescriptor()
        };
    }

    private ManagementSecondaryRouteDescriptor CreateDetailsDescriptor()
    {
        _routeState.SetSelectedRoute(null);
        _routeState.MarkClean();

        var file = GetSelectedFile();
        if (file is null)
        {
            return new ManagementSecondaryRouteDescriptor(
                "Auth file details",
                "Select an auth file from the left list to view status, models and patchable fields.",
                new ManagementEmptyState
                {
                    Title = "No auth file selected",
                    Description = "The default route shows file details, status actions and download/delete once a file is selected."
                });
        }

        var view = new AuthFileDetailsRouteView(
            file,
            _routeState.SelectedModels,
            _routeState.SelectedPrefix,
            _routeState.SelectedProxyUrl,
            _routeState.SelectedHeaders,
            _routeState.MarkDirty);

        return new ManagementSecondaryRouteDescriptor(
            file.Label ?? file.Name,
            $"{ManagementPageSupport.FormatValue(file.Provider, "Unknown provider")} · {GetStatusText(file)}",
            view,
            BuildDetailsHeader(file),
            BuildDetailsFooter(file, view));
    }

    private StackPanel BuildDetailsHeader(ManagementAuthFileItem file)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };

        var toggleButton = new Button
        {
            Content = file.Disabled ? "Enable file" : "Disable file"
        };
        toggleButton.Click += async (_, _) =>
        {
            try
            {
                await _viewModel.SetDisabledAsync(file.Name, !file.Disabled);
                await _refreshAsync();
            }
            catch (Exception exception)
            {
                ManagementPageSupport.ShowError(_owner, "Auth file status", exception);
            }
        };
        panel.Children.Add(toggleButton);

        var downloadButton = new Button
        {
            Margin = new Thickness(12, 0, 0, 0),
            Content = "Download JSON"
        };
        downloadButton.Click += async (_, _) =>
        {
            try
            {
                var payload = await _viewModel.DownloadAsync(file.Name);
                var dialog = new SaveFileDialog
                {
                    FileName = file.Name,
                    Filter = "JSON files|*.json|All files|*.*"
                };
                if (dialog.ShowDialog(Window.GetWindow(_owner)) == true)
                {
                    File.WriteAllText(dialog.FileName, payload.Value, Encoding.UTF8);
                }
            }
            catch (Exception exception)
            {
                ManagementPageSupport.ShowError(_owner, "Auth file download", exception);
            }
        };
        panel.Children.Add(downloadButton);

        var excludedButton = new Button
        {
            Margin = new Thickness(12, 0, 0, 0),
            Content = "OAuth excluded models"
        };
        excludedButton.Click += (_, _) => _navigate("auth-files-oauth-excluded", "OAuth excluded models");
        panel.Children.Add(excludedButton);

        var aliasButton = new Button
        {
            Margin = new Thickness(12, 0, 0, 0),
            Content = "OAuth model aliases"
        };
        aliasButton.Click += (_, _) => _navigate("auth-files-oauth-model-alias", "OAuth model aliases");
        panel.Children.Add(aliasButton);

        return panel;
    }

    private StackPanel BuildDetailsFooter(ManagementAuthFileItem file, AuthFileDetailsRouteView view)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var deleteButton = new Button
        {
            Content = "Delete file"
        };
        deleteButton.Click += async (_, _) =>
        {
            if (!ManagementConfirmDialog.Confirm(Window.GetWindow(_owner), "Delete auth file", $"Delete {file.Name}?"))
            {
                return;
            }

            try
            {
                await _viewModel.DeleteAsync([file.Name]);
                _routeState.SetSelectedFile(null);
                _routeState.SetSelectedModels([]);
                _routeState.SetDraftFields(null, null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                _routeState.MarkClean();
                await _refreshAsync();
            }
            catch (Exception exception)
            {
                ManagementPageSupport.ShowError(_owner, "Auth file delete", exception);
            }
        };
        panel.Children.Add(deleteButton);

        var saveButton = new Button
        {
            Content = "Save fields",
            Background = (Brush)Application.Current.Resources["ManagementAccentBrush"],
            Foreground = Brushes.White
        };
        saveButton.Click += async (_, _) =>
        {
            try
            {
                await _authService.PatchAuthFileFieldsAsync(view.BuildPatch(file.Name));
                _routeState.MarkClean();
                await _refreshAsync();
            }
            catch (Exception exception)
            {
                ManagementPageSupport.ShowError(_owner, "Auth file save", exception);
            }
        };
        panel.Children.Add(saveButton);

        return panel;
    }

    private ManagementSecondaryRouteDescriptor CreateOAuthExcludedDescriptor()
    {
        var file = GetSelectedFile();
        _routeState.SetSelectedRoute("auth-files-oauth-excluded");
        _routeState.MarkClean();

        if (file is null)
        {
            return CreateSelectionRequiredDescriptor("OAuth excluded models");
        }

        var providerKey = NormalizeChannel(file.Provider);
        if (providerKey is null)
        {
            return CreateMissingProviderDescriptor(file, "OAuth excluded models");
        }

        var initialItems = _viewModel.ExcludedModels.TryGetValue(providerKey, out var values)
            ? values
            : [];
        var view = new EditableStringCollectionRouteView(initialItems, _routeState.MarkDirty, "Add excluded model");

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        header.Children.Add(new StatusBadge
        {
            Text = providerKey,
            Tone = BadgeTone.Neutral
        });

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var deleteButton = new Button
        {
            Content = "Delete mapping"
        };
        deleteButton.Click += async (_, _) =>
        {
            try
            {
                await _authService.DeleteOAuthExcludedModelsAsync(providerKey);
                _routeState.MarkClean();
                await _refreshAsync();
            }
            catch (Exception exception)
            {
                ManagementPageSupport.ShowError(_owner, "OAuth excluded delete", exception);
            }
        };
        footer.Children.Add(deleteButton);

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
                var models = view.BuildValues();
                if (models.Length == 0)
                {
                    await _authService.DeleteOAuthExcludedModelsAsync(providerKey);
                }
                else
                {
                    await _authService.UpdateOAuthExcludedModelsAsync(providerKey, models);
                }

                _routeState.MarkClean();
                await _refreshAsync();
            }
            catch (Exception exception)
            {
                ManagementPageSupport.ShowError(_owner, "OAuth excluded save", exception);
            }
        };
        footer.Children.Add(saveButton);

        return new ManagementSecondaryRouteDescriptor(
            "OAuth excluded models",
            $"Editing provider-level excluded models for {file.Name}.",
            view,
            header,
            footer);
    }

    private ManagementSecondaryRouteDescriptor CreateOAuthModelAliasDescriptor()
    {
        var file = GetSelectedFile();
        _routeState.SetSelectedRoute("auth-files-oauth-model-alias");
        _routeState.MarkClean();

        if (file is null)
        {
            return CreateSelectionRequiredDescriptor("OAuth model aliases");
        }

        var providerKey = NormalizeChannel(file.Provider);
        if (providerKey is null)
        {
            return CreateMissingProviderDescriptor(file, "OAuth model aliases");
        }

        var initialItems = _viewModel.ModelAliases.TryGetValue(providerKey, out var aliases)
            ? aliases
            : [];
        var view = new EditableOAuthAliasRouteView(initialItems, _routeState.MarkDirty);

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        header.Children.Add(new StatusBadge
        {
            Text = providerKey,
            Tone = BadgeTone.Neutral
        });

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var deleteButton = new Button
        {
            Content = "Delete mapping"
        };
        deleteButton.Click += async (_, _) =>
        {
            try
            {
                await _authService.DeleteOAuthModelAliasAsync(providerKey);
                _routeState.MarkClean();
                await _refreshAsync();
            }
            catch (Exception exception)
            {
                ManagementPageSupport.ShowError(_owner, "OAuth alias delete", exception);
            }
        };
        footer.Children.Add(deleteButton);

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
                var aliasesToSave = view.BuildAliases();
                if (aliasesToSave.Length == 0)
                {
                    await _authService.DeleteOAuthModelAliasAsync(providerKey);
                }
                else
                {
                    await _authService.UpdateOAuthModelAliasAsync(providerKey, aliasesToSave);
                }

                _routeState.MarkClean();
                await _refreshAsync();
            }
            catch (Exception exception)
            {
                ManagementPageSupport.ShowError(_owner, "OAuth alias save", exception);
            }
        };
        footer.Children.Add(saveButton);

        return new ManagementSecondaryRouteDescriptor(
            "OAuth model aliases",
            $"Editing model aliases for provider {providerKey}.",
            view,
            header,
            footer);
    }

    private static ManagementSecondaryRouteDescriptor CreateSelectionRequiredDescriptor(string title)
    {
        return new ManagementSecondaryRouteDescriptor(
            title,
            "Select an auth file first to enter this route.",
            new ManagementEmptyState
            {
                Title = "No auth file selected",
                Description = "Choose a file from the left list and reopen this route."
            });
    }

    private static ManagementSecondaryRouteDescriptor CreateMissingProviderDescriptor(ManagementAuthFileItem file, string title)
    {
        return new ManagementSecondaryRouteDescriptor(
            title,
            $"The selected file {file.Name} does not expose a provider/channel.",
            new ManagementEmptyState
            {
                Title = "Missing provider metadata",
                Description = "OAuth provider-level routes require a provider value on the selected auth file."
            });
    }

    private ManagementAuthFileItem? GetSelectedFile()
    {
        return _viewModel.Files.FirstOrDefault(file => string.Equals(file.Name, _routeState.SelectedFileName, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetStatusText(ManagementAuthFileItem file)
    {
        return file.Disabled
            ? "Disabled"
            : ManagementPageSupport.FormatValue(file.StatusMessage ?? file.Status, "Available");
    }

    private static string? NormalizeChannel(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    }

    private sealed class AuthFileDetailsRouteView : UserControl
    {
        private readonly TextBox _prefixTextBox = new();
        private readonly TextBox _proxyUrlTextBox = new();
        private readonly ObservableCollection<EditableKeyValueItem> _headers = [];

        public AuthFileDetailsRouteView(
            ManagementAuthFileItem file,
            IReadOnlyList<ManagementModelDescriptor> models,
            string? prefix,
            string? proxyUrl,
            IReadOnlyDictionary<string, string> headers,
            Action onDirty)
        {
            Content = BuildLayout(file, models);
            _prefixTextBox.Text = prefix ?? string.Empty;
            _proxyUrlTextBox.Text = proxyUrl ?? string.Empty;
            foreach (var header in headers)
            {
                _headers.Add(new EditableKeyValueItem { Key = header.Key, Value = header.Value });
            }

            _prefixTextBox.TextChanged += (_, _) => onDirty();
            _proxyUrlTextBox.TextChanged += (_, _) => onDirty();
            SubscribeListChanges(Content as DependencyObject, onDirty);
        }

        public ManagementAuthFileFieldPatch BuildPatch(string name)
        {
            return new ManagementAuthFileFieldPatch
            {
                Name = name,
                Prefix = NormalizeText(_prefixTextBox.Text),
                ProxyUrl = NormalizeText(_proxyUrlTextBox.Text),
                Headers = BuildHeaders(_headers)
            };
        }

        private StackPanel BuildLayout(ManagementAuthFileItem file, IReadOnlyList<ManagementModelDescriptor> models)
        {
            var root = new StackPanel();

            var info = new StackPanel();
            info.Children.Add(CreateInfoRow("File", file.Name));
            info.Children.Add(CreateInfoRow("Provider", ManagementPageSupport.FormatValue(file.Provider, "Unknown")));
            info.Children.Add(CreateInfoRow("Email", ManagementPageSupport.FormatValue(file.Email, "Not recorded")));
            info.Children.Add(CreateInfoRow("Account", ManagementPageSupport.FormatValue(file.Account, "Unknown")));
            info.Children.Add(CreateInfoRow("Status", GetStatusText(file)));
            info.Children.Add(CreateInfoRow("Updated", ManagementPageSupport.FormatDate(file.UpdatedAt)));
            info.Children.Add(CreateInfoRow("Size", ManagementPageSupport.FormatFileSize(file.Size)));
            root.Children.Add(new ManagementFormSection
            {
                Title = "Basic information",
                Description = "Read-only metadata from the selected auth file.",
                SectionContent = info
            });

            var patchable = new StackPanel();
            patchable.Children.Add(new ManagementFieldRow
            {
                Label = "Prefix",
                FieldContent = _prefixTextBox
            });
            patchable.Children.Add(new ManagementFieldRow
            {
                Label = "Proxy URL",
                FieldContent = _proxyUrlTextBox
            });
            patchable.Children.Add(new ManagementFieldRow
            {
                Label = "Headers",
                FieldContent = new EditableKeyValueList
                {
                    ItemsSource = _headers,
                    KeyHeader = "Header",
                    ValueHeader = "Value",
                    AddButtonText = "Add header"
                }
            });
            root.Children.Add(new ManagementFormSection
            {
                Title = "Patchable fields",
                Description = "Saved through PatchAuthFileFieldsAsync.",
                SectionContent = patchable
            });

            var modelList = new StackPanel();
            if (models.Count == 0)
            {
                modelList.Children.Add(new ManagementEmptyState
                {
                    Title = "No models",
                    Description = "GetAuthFileModelsAsync returned no visible models for this file."
                });
            }
            else
            {
                foreach (var model in models)
                {
                    modelList.Children.Add(CreateModelDescriptorCard(model));
                }
            }

            root.Children.Add(new ManagementFormSection
            {
                Title = "Resolved models",
                Description = "These models come from GetAuthFileModelsAsync(name).",
                SectionContent = modelList
            });

            return root;
        }

        private static ManagementFieldRow CreateInfoRow(string label, string value)
        {
            return new ManagementFieldRow
            {
                Label = label,
                FieldContent = new TextBlock
                {
                    Foreground = (Brush)Application.Current.Resources["ManagementPrimaryTextBrush"],
                    Text = value,
                    TextWrapping = TextWrapping.Wrap
                }
            };
        }
    }

    private sealed class EditableStringCollectionRouteView : UserControl
    {
        private readonly ObservableCollection<EditableStringItem> _items = [];

        public EditableStringCollectionRouteView(IEnumerable<string> initialItems, Action onDirty, string addButtonText)
        {
            foreach (var item in initialItems)
            {
                _items.Add(new EditableStringItem { Value = item });
            }

            var list = new EditableStringList
            {
                ItemsSource = _items,
                AddButtonText = addButtonText
            };
            list.Changed += (_, _) => onDirty();
            Content = new ManagementFormSection
            {
                Title = "Models",
                Description = "Editing provider-level string collections without JSON fallback.",
                SectionContent = list
            };
        }

        public string[] BuildValues()
        {
            return _items
                .Select(item => item.Value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private sealed class EditableOAuthAliasRouteView : UserControl
    {
        private readonly ObservableCollection<EditableModelAliasItem> _aliases = [];

        public EditableOAuthAliasRouteView(IEnumerable<ManagementOAuthModelAliasEntry> aliases, Action onDirty)
        {
            foreach (var alias in aliases)
            {
                _aliases.Add(new EditableModelAliasItem
                {
                    Name = alias.Name,
                    Alias = alias.Alias,
                    Fork = alias.Fork
                });
            }

            var list = new EditableModelAliasList
            {
                ItemsSource = _aliases,
                ShowFork = true,
                AddButtonText = "Add alias"
            };
            list.Changed += (_, _) => onDirty();

            Content = new ManagementFormSection
            {
                Title = "Aliases",
                Description = "Saved through UpdateOAuthModelAliasAsync(channel, aliases).",
                SectionContent = list
            };
        }

        public ManagementOAuthModelAliasEntry[] BuildAliases()
        {
            return _aliases
                .Where(item => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Alias))
                .Select(item => new ManagementOAuthModelAliasEntry
                {
                    Name = item.Name.Trim(),
                    Alias = item.Alias.Trim(),
                    Fork = item.Fork
                })
                .ToArray();
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

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(14),
            Background = (Brush)Application.Current.Resources["ManagementSurfaceSubtleBrush"],
            BorderBrush = (Brush)Application.Current.Resources["ManagementBorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
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
