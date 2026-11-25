using System.Windows.Threading;

namespace Ognia.Wpf.ViewModels;

public sealed class NotificationViewModel : ViewModelBase
{
    private string? _message;

    public string? Message
    {
        get => _message;
        set
        {
            if (!SetProperty(ref _message, value)) return;
            OnPropertyChanged(nameof(HasMessage));
        }
    }

    private bool _isError;

    public bool IsError
    {
        get => _isError;
        set => SetProperty(ref _isError, value);
    }

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    private DispatcherTimer? _timer;

    public static TimeSpan AutoDismissDelay { get; set; }
        = TimeSpan.FromSeconds(5);

    public void Info(string message)
    {
        IsError = false;
        Message = message;

        StartTimer();
    }

    public void Error(string message)
    {
        StopTimer();

        IsError = true;
        Message = message;
    }

    public void Clear()
    {
        StopTimer();

        IsError = false;
        Message = null;
    }

    private void StartTimer()
    {
        StopTimer();

        if (AutoDismissDelay <= TimeSpan.Zero)
            return;

        _timer = new DispatcherTimer
        {
            Interval = AutoDismissDelay
        };

        _timer.Tick += (_, _) => Clear();

        _timer.Start();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }
}