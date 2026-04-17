using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace allyza.ViewModels;

public class BaseViewModel : INotifyPropertyChanged
{
    private bool _isBusy;
    private string _title = string.Empty;

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); }
    }
    public bool IsNotBusy => !IsBusy;
    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}