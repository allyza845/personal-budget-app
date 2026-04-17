using System.Text;
using allyza.Models;
using Newtonsoft.Json;

namespace allyza.Services;

public class FirebaseAuthService : IFirebaseAuthService
{
    private readonly HttpClient _http = new();
    private const string UserKey = "firebase_user";

    public UserModel? CurrentUser { get; private set; }

    public FirebaseAuthService()
    {
        _ = TryRestoreSessionAsync();
    }

    private async Task TryRestoreSessionAsync()
    {
        try
        {
            var json = await SecureStorage.Default.GetAsync(UserKey);
            if (!string.IsNullOrEmpty(json))
                CurrentUser = JsonConvert.DeserializeObject<UserModel>(json);
        }
        catch { /* secure storage unavailable on simulator sometimes */ }
    }

    public bool IsUserLoggedIn() => CurrentUser != null;

    // ── SIGN UP ──────────────────────────────────────────────────────────────
    public async Task<(bool Success, string Error)> SignUpAsync(string email, string password, string displayName)
    {
        try
        {
            var (ok, json, err) = await PostAsync(FirebaseConfig.SignUpUrl,
                new { email, password, returnSecureToken = true });
            if (!ok) return (false, err);

            var auth = JsonConvert.DeserializeObject<FirebaseAuthResponse>(json)!;
            CurrentUser = new UserModel
            {
                Uid = auth.LocalId,
                Email = auth.Email,
                DisplayName = displayName,
                IdToken = auth.IdToken,
                RefreshToken = auth.RefreshToken,
            };

            await UpdateProfileAsync(displayName);
            await PersistAsync();
            return (true, string.Empty);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // ── SIGN IN ──────────────────────────────────────────────────────────────
    public async Task<(bool Success, string Error)> SignInAsync(string email, string password)
    {
        try
        {
            var (ok, json, err) = await PostAsync(FirebaseConfig.SignInUrl,
                new { email, password, returnSecureToken = true });
            if (!ok) return (false, err);

            var auth = JsonConvert.DeserializeObject<FirebaseAuthResponse>(json)!;
            CurrentUser = new UserModel
            {
                Uid = auth.LocalId,
                Email = auth.Email,
                DisplayName = auth.DisplayName,
                IdToken = auth.IdToken,
                RefreshToken = auth.RefreshToken,
            };

            await PersistAsync();
            return (true, string.Empty);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // ── SIGN OUT ─────────────────────────────────────────────────────────────
    public void SignOut()
    {
        CurrentUser = null;
        SecureStorage.Default.Remove(UserKey);
    }

    // ── UPDATE DISPLAY NAME ──────────────────────────────────────────────────
    public async Task<bool> UpdateProfileAsync(string displayName)
    {
        if (CurrentUser is null) return false;
        try
        {
            var (ok, _, _) = await PostAsync(FirebaseConfig.UpdateUrl,
                new { idToken = CurrentUser.IdToken, displayName, returnSecureToken = false });
            if (ok)
            {
                CurrentUser.DisplayName = displayName;
                await PersistAsync();
            }
            return ok;
        }
        catch { return false; }
    }

    // ── HELPERS ──────────────────────────────────────────────────────────────
    private async Task PersistAsync()
    {
        if (CurrentUser is null) return;
        await SecureStorage.Default.SetAsync(UserKey, JsonConvert.SerializeObject(CurrentUser));
    }

    private async Task<(bool IsSuccess, string Json, string ErrorMessage)> PostAsync(string url, object payload)
    {
        var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(url, content);
        var body = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode) return (true, body, string.Empty);

        var errObj = JsonConvert.DeserializeObject<FirebaseErrorResponse>(body);
        var message = errObj?.Error?.Message switch
        {
            "EMAIL_EXISTS" => "This email is already in use.",
            "INVALID_EMAIL" => "Invalid email address.",
            "EMAIL_NOT_FOUND" => "No account found with this email.",
            "INVALID_PASSWORD" => "Incorrect password.",
            "INVALID_LOGIN_CREDENTIALS" => "Invalid email or password.",
            "USER_DISABLED" => "This account has been disabled.",
            var m when m is not null && m.StartsWith("WEAK_PASSWORD") => "Password must be at least 6 characters.",
            var m => m ?? "An unknown error occurred."
        };
        return (false, body, message);
    }
}