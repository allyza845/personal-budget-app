using allyza;
using allyza.Services;
using allyza.Views;


namespace allyza;

public partial class App : Application
{
    public App(IFirebaseAuthService authService, LoginPage loginPage)
    {
        InitializeComponent();

        if (authService.IsUserLoggedIn())
            MainPage = new AppShell();
        else
            MainPage = new NavigationPage(loginPage);
    }
}