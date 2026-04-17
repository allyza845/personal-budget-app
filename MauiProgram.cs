using allyza.Services;
using allyza.ViewModels;
using allyza.Views;



namespace allyza;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Services (singleton — shared across app lifetime)
        builder.Services.AddSingleton<IFirebaseAuthService, FirebaseAuthService>();
        builder.Services.AddSingleton<IFirestoreService, FirestoreService>();
        builder.Services.AddSingleton<IOtpService, OtpService>();

        // ViewModels (transient — fresh per page)
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<SignUpViewModel>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<ProfileViewModel>();
        builder.Services.AddTransient<AddIncomeViewModel>();
        builder.Services.AddTransient<CategoryViewModel>();
        builder.Services.AddTransient<AddExpenseViewModel>();
        builder.Services.AddTransient<FixedExpenseViewModel>();
        builder.Services.AddTransient<TransactionHistoryViewModel>();
        builder.Services.AddTransient<AnalyticsViewModel>();
        builder.Services.AddTransient<DashboardViewModel>();
  

        // Views
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<SignUpPage>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<ProfilePage>();
        builder.Services.AddTransient<AddIncomePage>();
        builder.Services.AddTransient<CategoryPage>();
        builder.Services.AddTransient<AddExpensePage>();
        builder.Services.AddTransient<FixedExpensePage>();
        builder.Services.AddTransient<TransactionHistoryPage>();
        builder.Services.AddTransient<AnalyticsPage>();
        builder.Services.AddTransient<DashboardPage>();
      
        return builder.Build();
    }
}