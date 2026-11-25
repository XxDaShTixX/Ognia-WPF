namespace Ognia.Wpf.Services;

public interface INotificationService
{
    void Info(string message);
    void Error(string message);
    void Clear();
}