using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace CodexCliPlus.Views.Controls;

internal enum StartupFlowScreen
{
    Loading,
    FirstRunKey,
    Login,
}

public partial class StartupFlowView : WpfUserControl
{
    public event EventHandler? LoginSubmitted;
    public event EventHandler? ResetRequested;
    public event EventHandler? FirstRunCopyRequested;
    public event EventHandler? FirstRunSaveRequested;
    public event EventHandler? FirstRunConfirmationRequested;
    public event EventHandler? FirstRunConfirmationAccepted;
    public event EventHandler? FirstRunConfirmationCancelled;
    public event EventHandler? CloseRequested;
    public event EventHandler<MouseButtonEventArgs>? WindowDragRequested;

    internal StartupFlowScreen CurrentScreen { get; private set; } = StartupFlowScreen.Login;

    public StartupFlowView()
    {
        InitializeComponent();
        SetScreen(StartupFlowScreen.Login, animate: false);
    }

    public bool IsLoadingVisible => CurrentScreen == StartupFlowScreen.Loading && IsVisible;

    public bool IsLoginVisible => CurrentScreen == StartupFlowScreen.Login && IsVisible;

    public string ManagementKey => ManagementKeyPasswordBox.Password.Trim();

    public bool RememberPassword
    {
        get => RememberPasswordCheckBox.IsChecked == true;
        set
        {
            RememberPasswordCheckBox.IsChecked = value;
            UpdatePersistenceDependencies();
        }
    }

    public bool AutoLogin
    {
        get => AutoLoginCheckBox.IsChecked == true;
        set
        {
            if (value)
            {
                RememberPasswordCheckBox.IsChecked = true;
            }

            AutoLoginCheckBox.IsChecked = value;
            UpdatePersistenceDependencies();
        }
    }

    public bool FirstRunRememberPassword =>
        FirstRunRememberPasswordCheckBox.IsChecked == true;

    public bool FirstRunAutoLogin => FirstRunAutoLoginCheckBox.IsChecked == true;

    public void ShowLoading(double progress, string title, string description, string status)
    {
        SetScreen(StartupFlowScreen.Loading);
        LoadingTitleText.Text = title;
        LoadingDescriptionText.Text = description;
        LoadingStatusText.Text = status;

        var normalizedProgress = Math.Clamp(progress, 0, 100);
        PreparationProgressBar.BeginAnimation(
            RangeBase.ValueProperty,
            new DoubleAnimation(normalizedProgress, new Duration(TimeSpan.FromMilliseconds(320)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            }
        );
        PreparationProgressFillScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(
                normalizedProgress / 100,
                new Duration(TimeSpan.FromMilliseconds(420))
            )
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            }
        );
    }

    public void ShowFirstRunKey(string key, bool rememberPassword, bool autoLogin)
    {
        FirstRunSecurityKeyTextBox.Text = key;
        FirstRunSecurityKeyTextBox.CaretIndex = 0;
        FirstRunSecurityKeyTextBox.ScrollToHome();
        FirstRunRememberPasswordCheckBox.IsChecked = rememberPassword || autoLogin;
        FirstRunAutoLoginCheckBox.IsChecked = autoLogin;
        UpdatePersistenceDependencies();
        FirstRunEnterManagementButton.IsEnabled = true;
        SetFirstRunConfirmVisible(visible: false, buttonText: "确认", canConfirm: false);
        SetScreen(StartupFlowScreen.FirstRunKey);
        FocusFirstRunKey();
    }

    public void ShowLogin(string? errorMessage, bool rememberPassword, bool autoLogin)
    {
        SetLoginPersistenceOptions(rememberPassword, autoLogin);
        SetLoginBusy(false);
        SetLoginError(errorMessage);
        SetScreen(StartupFlowScreen.Login);
        FocusLoginKey();
    }

    public void SetLoginPersistenceOptions(bool rememberPassword, bool autoLogin)
    {
        RememberPasswordCheckBox.IsChecked = rememberPassword || autoLogin;
        AutoLoginCheckBox.IsChecked = autoLogin;
        UpdatePersistenceDependencies();
    }

    public void SetLoginBusy(bool isBusy)
    {
        LoginButton.IsEnabled = !isBusy;
        AuthenticationMenuButton.IsEnabled = !isBusy;
    }

    public void SetLoginError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            LoginErrorText.Text = string.Empty;
            LoginErrorText.ToolTip = null;
            LoginErrorText.Visibility = Visibility.Hidden;
            return;
        }

        LoginErrorText.Text = message;
        LoginErrorText.ToolTip = message;
        LoginErrorText.Visibility = Visibility.Visible;
    }

    public void SetFirstRunConfirmVisible(bool visible, string buttonText, bool canConfirm)
    {
        FirstRunConfirmPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        FirstRunConfirmContinueButton.Content = buttonText;
        FirstRunConfirmContinueButton.IsEnabled = canConfirm;
        FirstRunConfirmCloseButton.IsEnabled = true;
    }

    public void SetFirstRunConfirmActions(bool canConfirm, bool canClose)
    {
        FirstRunConfirmContinueButton.IsEnabled = canConfirm;
        FirstRunConfirmCloseButton.IsEnabled = canClose;
    }

    public void ClearLoginPassword()
    {
        ManagementKeyPasswordBox.Password = string.Empty;
    }

    public void ClearFirstRunKey()
    {
        FirstRunSecurityKeyTextBox.Text = string.Empty;
    }

    private void SetScreen(StartupFlowScreen screen, bool animate = true)
    {
        CurrentScreen = screen;
        StartupStepHost.Visibility =
            screen == StartupFlowScreen.Loading ? Visibility.Visible : Visibility.Collapsed;
        CompactAuthenticationHost.Visibility =
            screen == StartupFlowScreen.Loading ? Visibility.Collapsed : Visibility.Visible;
        LoadingPanel.Visibility =
            screen == StartupFlowScreen.Loading ? Visibility.Visible : Visibility.Collapsed;
        FirstRunKeyPanel.Visibility =
            screen == StartupFlowScreen.FirstRunKey ? Visibility.Visible : Visibility.Collapsed;
        LoginPanel.Visibility =
            screen == StartupFlowScreen.Login ? Visibility.Visible : Visibility.Collapsed;

        if (animate && IsLoaded)
        {
            AnimateStepIn();
        }
    }

    private void AnimateStepIn()
    {
        var stepHost =
            CurrentScreen == StartupFlowScreen.Loading
                ? (UIElement)StartupStepHost
                : CompactAuthenticationHost;
        var translateTransform =
            CurrentScreen == StartupFlowScreen.Loading
                ? StartupStepTranslateTransform
                : CompactAuthenticationTranslateTransform;

        stepHost.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0.88, 1, new Duration(TimeSpan.FromMilliseconds(140)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            }
        );
        translateTransform.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, new Duration(TimeSpan.FromMilliseconds(160)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            }
        );
    }

    private void FocusLoginKey()
    {
        Dispatcher.BeginInvoke(
            () => ManagementKeyPasswordBox.Focus(),
            DispatcherPriority.Background
        );
    }

    private void FocusFirstRunKey()
    {
        Dispatcher.BeginInvoke(
            () => FirstRunSecurityKeyTextBox.Focus(),
            DispatcherPriority.Background
        );
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        LoginSubmitted?.Invoke(this, EventArgs.Empty);
    }

    private void ManagementKeyPasswordBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        LoginSubmitted?.Invoke(this, EventArgs.Empty);
    }

    private void AuthenticationMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (AuthenticationMenuButton.ContextMenu is not { } menu)
        {
            return;
        }

        menu.PlacementTarget = AuthenticationMenuButton;
        menu.IsOpen = true;
    }

    private void AuthenticationMenuResetItem_Click(object sender, RoutedEventArgs e)
    {
        ResetRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AuthenticationCloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AuthenticationDragRegion_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        WindowDragRequested?.Invoke(this, e);
    }

    private void FirstRunCopyKeyButton_Click(object sender, RoutedEventArgs e)
    {
        FirstRunCopyRequested?.Invoke(this, EventArgs.Empty);
    }

    private void FirstRunSaveToDesktopButton_Click(object sender, RoutedEventArgs e)
    {
        FirstRunSaveRequested?.Invoke(this, EventArgs.Empty);
    }

    private void FirstRunEnterManagementButton_Click(object sender, RoutedEventArgs e)
    {
        FirstRunConfirmationRequested?.Invoke(this, EventArgs.Empty);
    }

    private void FirstRunConfirmContinueButton_Click(object sender, RoutedEventArgs e)
    {
        FirstRunConfirmationAccepted?.Invoke(this, EventArgs.Empty);
    }

    private void FirstRunConfirmCloseButton_Click(object sender, RoutedEventArgs e)
    {
        FirstRunConfirmationCancelled?.Invoke(this, EventArgs.Empty);
    }

    private void PersistenceOptionLabel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WpfCheckBox checkBox })
        {
            checkBox.IsChecked = checkBox.IsChecked != true;
            e.Handled = true;
        }
    }

    private void PersistenceOptionCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender == FirstRunAutoLoginCheckBox && FirstRunAutoLoginCheckBox.IsChecked == true)
        {
            FirstRunRememberPasswordCheckBox.IsChecked = true;
        }
        else if (sender == AutoLoginCheckBox && AutoLoginCheckBox.IsChecked == true)
        {
            RememberPasswordCheckBox.IsChecked = true;
        }

        UpdatePersistenceDependencies();
    }

    private void UpdatePersistenceDependencies()
    {
        if (
            FirstRunRememberPasswordCheckBox is null
            || FirstRunAutoLoginCheckBox is null
            || RememberPasswordCheckBox is null
            || AutoLoginCheckBox is null
        )
        {
            return;
        }

        SyncPersistencePair(FirstRunRememberPasswordCheckBox, FirstRunAutoLoginCheckBox);
        SyncPersistencePair(RememberPasswordCheckBox, AutoLoginCheckBox);
    }

    private static void SyncPersistencePair(WpfCheckBox rememberPassword, WpfCheckBox autoLogin)
    {
        autoLogin.IsEnabled = rememberPassword.IsChecked == true;
        if (rememberPassword.IsChecked != true)
        {
            autoLogin.IsChecked = false;
        }
    }
}
