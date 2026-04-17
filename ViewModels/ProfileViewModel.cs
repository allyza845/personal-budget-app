using allyza.Views;

using allyza.Services;

using System.Windows.Input;


namespace allyza.ViewModels;

public class ProfileViewModel : BaseViewModel
{
    private readonly IFirebaseAuthService _auth;
    private readonly IFirestoreService _db;

    private string _displayName = "", _email = "", _uid = "", _memberSince = "", _successMsg = "";

    public string DisplayName { get => _displayName; set { _displayName = value; OnPropertyChanged(); } }
    public string Email { get => _email; set { _email = value; OnPropertyChanged(); } }
    public string Uid { get => _uid; set { _uid = value; OnPropertyChanged(); } }
    public string MemberSince { get => _memberSince; set { _memberSince = value; OnPropertyChanged(); } }
    public string SuccessMessage
    {
        get => _successMsg;
        set { _successMsg = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSuccess)); }
    }
    public bool HasSuccess => !string.IsNullOrEmpty(_successMsg);

    public ICommand SaveNameCommand { get; }
    public ICommand LogoutCommand { get; }

    public ProfileViewModel(IFirebaseAuthService auth, IFirestoreService db)
    {
        _auth = auth;
        _db = db;
        Title = "Profile";
        SaveNameCommand = new Command(async () => await SaveNameAsync());
        LogoutCommand = new Command(async () => await LogoutAsync());
    }

    public async Task LoadAsync()
    {
        if (_auth.CurrentUser is null) return;
        IsBusy = true;
        try
        {
            var user = _auth.CurrentUser;
            Email = user.Email;
            Uid = user.Uid;

            var data = await _db.GetUserProfileAsync(user.Uid, user.IdToken);
            DisplayName = data is not null && data.TryGetValue("displayName", out var dn)
                ? dn?.ToString() ?? user.DisplayName
                : user.DisplayName;

            if (data is not null && data.TryGetValue("createdAt", out var ca)
                && DateTime.TryParse(ca?.ToString(), out var dt))
                MemberSince = $"Member since {dt:MMMM d, yyyy}";
        }
        finally { IsBusy = false; }
    }

    private async Task SaveNameAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        SuccessMessage = string.Empty;
        try
        {
            var user = _auth.CurrentUser!;
            await _auth.UpdateProfileAsync(DisplayName);
            await _db.SaveUserProfileAsync(user.Uid, user.IdToken, new Dictionary<string, object>
            {
                ["displayName"] = DisplayName,
                ["email"] = user.Email,
            });
            SuccessMessage = "✓ Profile updated!";
            await Task.Delay(2500);
            SuccessMessage = string.Empty;
        }
        finally { IsBusy = false; }
    }

    private async Task LogoutAsync()
    {
        bool ok = await Application.Current!.MainPage!
            .DisplayAlert("Sign Out", "Are you sure?", "Yes", "No");
        if (!ok) return;

        _auth.SignOut();
        Application.Current.MainPage = new NavigationPage(
            IPlatformApplication.Current!.Services.GetRequiredService<LoginPage>());
    }
}