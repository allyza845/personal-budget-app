
using allyza.Views;

using allyza.Services;

using System.Windows.Input;


namespace allyza.ViewModels;

public class LoginViewModel : BaseViewModel
{
    private readonly IFirebaseAuthService _auth;
    private string _email = "", _password = "", _errorMsg = "";

    public string Email { get => _email; set { _email = value; OnPropertyChanged(); } }
    public string Password { get => _password; set { _password = value; OnPropertyChanged(); } }
    public string ErrorMessage { get => _errorMsg; set { _errorMsg = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrEmpty(_errorMsg);

    public ICommand LoginCommand { get; }
    public ICommand GoToSignUpCommand { get; }

    public LoginViewModel(IFirebaseAuthService auth)
    {
        _auth = auth;
        Title = "Sign In";
        LoginCommand = new Command(async () => await LoginAsync());
        GoToSignUpCommand = new Command(async () => await GoToSignUpAsync());
    }

    private async Task LoginAsync()
    {
        if (IsBusy) return;
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
        { ErrorMessage = "Please enter both email and password."; return; }

        IsBusy = true;
        try
        {
            var (success, error) = await _auth.SignInAsync(Email, Password);
            if (success)
                Application.Current!.MainPage = new AppShell();
            else
                ErrorMessage = error;
        }
        finally { IsBusy = false; }
    }

    private async Task GoToSignUpAsync()
    {
        var page = IPlatformApplication.Current!.Services.GetRequiredService<SignUpPage>();
        await Application.Current!.MainPage!.Navigation.PushAsync(page);
    }
}