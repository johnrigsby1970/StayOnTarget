using System.Windows.Input;

namespace StayOnTarget.ViewModels;

public class ToastViewModel : ViewModelBase
{
    private string _message = string.Empty;
    private bool _isVisible = true;

    public string Message { get => _message; set => SetProperty(ref _message, value); }
    public bool IsVisible { get => _isVisible; set => SetProperty(ref _isVisible, value); }

    public ICommand CloseCommand { get; }

    public ToastViewModel(string message, Action<ToastViewModel> onClose)
    {
        Message = message;
        CloseCommand = new RelayCommand(_ => onClose(this));
        
        // Auto-close after 5 seconds
        Task.Delay(5000).ContinueWith(_ => onClose(this));
    }
}
