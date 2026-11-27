using Ognia.Wpf.Authentication;
using Ognia.Wpf.Configuration;
using Ognia.Wpf.Enums;
using Ognia.Wpf.Navigation;
using Ognia.Wpf.Repos;
using Ognia.Wpf.Services;
using Ognia.Wpf.Session;
using Ognia.Wpf.ViewModels.Shell;
using Ognia.Wpf.Views.Shell;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Ognia.Wpf;

public partial class MainWindow : Window
{
    public static MainWindow? Current { get; private set; }

    public ApiClientService Api { get; }
    public IAppNavigator Navigator { get; }

    private const double DefaultWindowWidth = 1200;
    private const double DefaultWindowHeight = 800;
    private const double WindowMinWidth = 900;
    private const double WindowMinHeight = 600;

    private double _normalWidth = DefaultWindowWidth;
    private double _normalHeight = DefaultWindowHeight;

    public static readonly DependencyProperty DisplayTitleProperty =
        DependencyProperty.Register(
            nameof(DisplayTitle),
            typeof(string),
            typeof(MainWindow),
            new PropertyMetadata(string.Empty));

    public string DisplayTitle
    {
        get => (string)GetValue(DisplayTitleProperty);
        private set => SetValue(DisplayTitleProperty, value);
    }

    public static readonly DependencyProperty IsUpdateAvailableProperty =
        DependencyProperty.Register(
            nameof(IsUpdateAvailable),
            typeof(bool),
            typeof(MainWindow),
            new PropertyMetadata(false));

    public bool IsUpdateAvailable
    {
        get => (bool)GetValue(IsUpdateAvailableProperty);
        private set => SetValue(IsUpdateAvailableProperty, value);
    }

    public MainWindow()
    {
        InitializeComponent();
        MinWidth = WindowMinWidth;
        MinHeight = WindowMinHeight;
        Width = DefaultWindowWidth;
        Height = DefaultWindowHeight;
        SizeChanged += MainWindow_SizeChanged;
        DataContext = this;
        Current = this;

        DisplayTitle = AppInfo.DisplayVersion;
        Loaded += (_, _) => RefreshDisplayTitle();

        Api = new ApiClientService(AppSettings.Instance.Api.BaseUrl);
        Navigator = new AppNavigator(Api, view => MainContent.Content = view);

        Api.Unauthorized += OnUnauthorized;
        UpdateService.Instance.StateChanged += OnUpdateStateChanged;

        _ = InitializeAuthAsync();
    }

    private void RefreshDisplayTitle()
    => DisplayTitle = AppInfo.DisplayVersion;

    private void OnUpdateStateChanged()
        => Dispatcher.Invoke(() =>
        {
            RefreshDisplayTitle();
            IsUpdateAvailable = UpdateService.Instance.PendingVersion != null;
        });

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsUpdateAvailable) return;

        var version = UpdateService.Instance.PendingVersion ?? "available";
        var result = MessageBox.Show(
            $"Update to v{version}? The app will restart.",
            "Update Ognia",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        if (!UpdateService.Instance.PendingIsDownloaded)
        {
            var downloadedVersion = await UpdateService.Instance.CheckAsync(downloadIfFound: true);
            if (downloadedVersion == null)
            {
                MessageBox.Show(
                    "The update could not be downloaded.",
                    "Update Ognia",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }

        UpdateService.Instance.ApplyAndRestart();
    }

    /// <summary>
    /// Window round edges
    /// </summary>
    /// <param name="hwnd"></param>
    /// <param name="attr"></param>
    /// <param name="value"></param>
    /// <param name="size"></param>
    /// <returns></returns>
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void RestoreWindowBounds()
    {
        var w = AppSettings.Instance.Window;

        Width = w.Width >= WindowMinWidth ? w.Width : DefaultWindowWidth;
        Height = w.Height >= WindowMinHeight ? w.Height : DefaultWindowHeight;

        _normalWidth = Width;
        _normalHeight = Height;

        if (w.State == WindowState.Maximized)
            WindowState = WindowState.Maximized;

        Opacity = Math.Clamp(w.MainWindowOpacity, 0.01, 1.0);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        var bounds = _isMini
            ? (_preMiniState == WindowState.Normal
                ? new Rect(_preMiniLeft, _preMiniTop, _preMiniWidth, _preMiniHeight)
                : RestoreBounds)
            : WindowState == WindowState.Normal
                ? new Rect(Left, Top, _normalWidth, _normalHeight)
                : RestoreBounds;

        AppSettings.Instance.Window.Width = bounds.Width;
        AppSettings.Instance.Window.Height = bounds.Height;
        AppSettings.Instance.Window.State = WindowState == WindowState.Minimized
            ? WindowState.Normal
            : WindowState;

        PersistPlaybackPosition();

        AppSettings.Instance.Save();
        PlaybackSessionState.Instance.Save();

        UpdateService.Instance.StateChanged -= OnUpdateStateChanged;
        Api.Unauthorized -= OnUnauthorized;

        base.OnClosing(e);
    }

    private async Task InitializeAuthAsync()
    {
        if (AppSettings.Instance.Mode == AppMode.Guest)
        {
            Navigator.EnterGuestMode();
            return;
        }

        if (!TokenStore.IsAuthenticated)
        {
            Navigator.ShowLanding();
            return;
        }

        var ok = await Api.TryRefreshAsync();
        if (ok)
            Navigator.ShowShell(new ApiMusicRepo(Api));
        else
            Navigator.ShowLanding();
    }

    private void OnUnauthorized()
    {
        TokenStore.Clear();
        Application.Current.Dispatcher.Invoke(Navigator.ShowLogin);
    }

    #region Window chrome
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);

        int preference = 2;
        DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));

        RestoreWindowBounds();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                // Maximize to the monitor's work area (excludes taskbar / auto-hide).
                var work = info.rcWork;
                var monitorRect = info.rcMonitor;

                mmi.ptMaxPosition.X = work.Left - monitorRect.Left;
                mmi.ptMaxPosition.Y = work.Top - monitorRect.Top;
                mmi.ptMaxSize.X = work.Right - work.Left;
                mmi.ptMaxSize.Y = work.Bottom - work.Top;

                mmi.ptMinTrackSize.X = (int)MinWidth;
                mmi.ptMinTrackSize.Y = (int)MinHeight;
            }
        }

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isMini) ExitMiniMode();
        WindowState = WindowState.Minimized;
    }

    private void MiniButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isMini) ExitMiniMode();
        else EnterMiniMode();
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isMini)
        {
            ExitMiniMode();
            return;
        }

        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        PersistPlaybackPosition();
        Navigator.DisposeShell();
        Close();
    }

    private void PersistPlaybackPosition()
    {
        var playback = Navigator.Playback;
        if (playback?.CurrentTrack == null) return;

        PlaybackSessionState.Instance.LastTrackPosition =
            playback.CurrentPosition.TotalSeconds;
        PlaybackSessionState.Instance.Save();
    }
    #endregion

    #region Mini player

    private const double MiniWidth = 450;
    private const double MiniHeight = 200;
    private const double MiniMargin = 12;
    private static readonly TimeSpan MiniFadeIn = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan MiniFadeOut = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan MiniFadeOutDelay = TimeSpan.FromMilliseconds(1500);

    private WindowState _preMiniState = WindowState.Normal;
    private double _preMiniLeft;
    private double _preMiniTop;
    private double _preMiniWidth;
    private double _preMiniHeight;
    private bool _isMini;
    private bool _isChangingWindowSize;
    private System.Windows.Threading.DispatcherTimer? _miniFadeTimer;

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isMini || _isChangingWindowSize || WindowState != WindowState.Normal)
            return;

        _normalWidth = ActualWidth;
        _normalHeight = ActualHeight;
    }

    private void EnterMiniMode()
    {
        if (_isMini) return;
        if (MainContent.Content is not ShellView shell) return;
        if (shell.PlaybackBarDataContext is not PlaybackBarViewModel barVm) return;

        _preMiniState = WindowState;
        var normalBounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, _normalWidth, _normalHeight)
            : RestoreBounds;
        _preMiniLeft = normalBounds.Left;
        _preMiniTop = normalBounds.Top;
        _preMiniWidth = normalBounds.Width;
        _preMiniHeight = normalBounds.Height;

        _isMini = true;
        _isChangingWindowSize = true;
        WindowState = WindowState.Normal;

        // Resize and style the window BEFORE swapping content so the mini
        // view lays out against the final size on its first measure pass.
        MinWidth = 0;
        MinHeight = 0;

        var wa = SystemParameters.WorkArea;
        Left = wa.Right - MiniWidth - MiniMargin;
        Top = wa.Bottom - MiniHeight - MiniMargin;
        Width = MiniWidth;
        Height = MiniHeight;
        _isChangingWindowSize = false;

        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;

        // Now swap content — it measures against the mini geometry.
        var mini = new MiniPlayerView { DataContext = barVm };
        MainContent.Content = mini;

        Opacity = AppSettings.Instance.Window.MiniRestingOpacity;
        MouseEnter += Mini_HoverEnter;
        MouseLeave += Mini_HoverLeave;

        _isMini = true;
    }

    private void ExitMiniMode()
    {
        if (!_isMini) return;

        MouseEnter -= Mini_HoverEnter;
        MouseLeave -= Mini_HoverLeave;
        StopMiniFadeTimer();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1.0;

        Topmost = false;
        ShowInTaskbar = true;
        ResizeMode = ResizeMode.CanResize;

        _isChangingWindowSize = true;
        MinWidth = WindowMinWidth;
        MinHeight = WindowMinHeight;

        Width = _preMiniWidth;
        Height = _preMiniHeight;
        Left = _preMiniLeft;
        Top = _preMiniTop;
        WindowState = _preMiniState == WindowState.Maximized
            ? WindowState.Maximized
            : WindowState.Normal;

        Navigator.RestoreShellContent();

        _isChangingWindowSize = false;
        _isMini = false;
    }

    private void Mini_HoverEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        StopMiniFadeTimer();
        AnimateWindowOpacity(1.0, MiniFadeIn);
    }

    private void Mini_HoverLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        StopMiniFadeTimer();

        _miniFadeTimer = new System.Windows.Threading.DispatcherTimer { Interval = MiniFadeOutDelay };
        _miniFadeTimer.Tick += (_, _) =>
        {
            StopMiniFadeTimer();
            AnimateWindowOpacity(AppSettings.Instance.Window.MiniRestingOpacity, MiniFadeOut);
        };
        _miniFadeTimer.Start();
    }

    private void StopMiniFadeTimer()
    {
        if (_miniFadeTimer == null) return;
        _miniFadeTimer.Stop();
        _miniFadeTimer = null;
    }

    private void AnimateWindowOpacity(double target, TimeSpan duration)
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = target,
            Duration = new Duration(duration),
            FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd
        };
        BeginAnimation(OpacityProperty, anim);
    }

    #endregion
}