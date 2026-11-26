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

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Current = this;

        DisplayTitle = AppInfo.DisplayVersion;
        Loaded += (_, _) => RefreshDisplayTitle();
        UpdateService.Instance.StateChanged += OnUpdateStateChanged;

        Api = new ApiClientService(AppSettings.Instance.Api.BaseUrl);
        Navigator = new AppNavigator(Api, view => MainContent.Content = view);

        Api.Unauthorized += OnUnauthorized;
        UpdateService.Instance.StateChanged += OnUpdateStateChanged;

        _ = InitializeAuthAsync();
    }

    private void RefreshDisplayTitle()
    => DisplayTitle = AppInfo.DisplayVersion;

    private void OnUpdateStateChanged()
        => Dispatcher.Invoke(RefreshDisplayTitle);

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

        if (w.Width > 0) Width = w.Width;
        if (w.Height > 0) Height = w.Height;

        if (w.State == WindowState.Maximized)
            WindowState = WindowState.Maximized;

        Opacity = Math.Clamp(w.MainWindowOpacity, 0.01, 1.0);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
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

    private const double MiniWidth = 420;
    private const double MiniHeight = 175;
    private const double MiniMargin = 12;
    private const double NormalMinWidth = 1200;
    private const double NormalMinHeight = 800;
    private static readonly TimeSpan MiniFadeIn = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan MiniFadeOut = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan MiniFadeOutDelay = TimeSpan.FromMilliseconds(1500);

    private WindowState _preMiniState = WindowState.Normal;
    private double _preMiniLeft;
    private double _preMiniTop;
    private double _preMiniWidth;
    private double _preMiniHeight;
    private bool _isMini;
    private System.Windows.Threading.DispatcherTimer? _miniFadeTimer;

    private void EnterMiniMode()
    {
        if (_isMini) return;
        if (MainContent.Content is not ShellView shell) return;

        _preMiniState = WindowState;
        _preMiniLeft = Left;
        _preMiniTop = Top;
        _preMiniWidth = Width;
        _preMiniHeight = Height;

        WindowState = WindowState.Normal;

        var barVm = shell.PlaybackBarDataContext as PlaybackBarViewModel;
        if (barVm == null) return;

        // Resize and style the window BEFORE swapping content so the mini
        // view lays out against the final size on its first measure pass.
        MinWidth = 0;
        MinHeight = 0;

        var wa = SystemParameters.WorkArea;
        Left = wa.Right - MiniWidth - MiniMargin;
        Top = wa.Bottom - MiniHeight - MiniMargin;
        Width = MiniWidth;
        Height = MiniHeight;

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

        MinWidth = NormalMinWidth;
        MinHeight = NormalMinHeight;

        Width = _preMiniWidth;
        Height = _preMiniHeight;
        Left = _preMiniLeft;
        Top = _preMiniTop;
        WindowState = _preMiniState == WindowState.Maximized
            ? WindowState.Maximized
            : WindowState.Normal;

        Navigator.RestoreShellContent();

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