using allyza.Models;

namespace allyza.Services;

public interface IFirebaseAuthService
{
    UserModel? CurrentUser { get; }
    bool IsUserLoggedIn();
    Task<(bool Success, string Error)> SignUpAsync(string email, string password, string displayName);
    Task<(bool Success, string Error)> SignInAsync(string email, string password);
    void SignOut();
    Task<bool> UpdateProfileAsync(string displayName);
}