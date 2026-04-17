using allyza.Services;


namespace allyza.ViewModels;

public class HomeViewModel : BaseViewModel
{
    private string _greeting = string.Empty;

    public string Greeting
    {
        get => _greeting;
        set { _greeting = value; OnPropertyChanged(); }
    }

    public HomeViewModel(IFirebaseAuthService auth)
    {
        Title = "Home";
        var hour = DateTime.Now.Hour;
        var tod = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
        var name = auth.CurrentUser?.DisplayName;
        Greeting = string.IsNullOrEmpty(name) ? $"{tod}!" : $"{tod}, {name}!";
    }
}