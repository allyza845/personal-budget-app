namespace allyza.Services;

public interface IFirestoreService
{
    // User profile (existing)
    Task<bool> SaveUserProfileAsync(string uid, string idToken, Dictionary<string, object> data);
    Task<Dictionary<string, object>?> GetUserProfileAsync(string uid, string idToken);

    // Generic document operations (new)
    Task<bool> SetDocumentAsync(string path, string idToken, Dictionary<string, object> data);
    Task<bool> DeleteDocumentAsync(string path, string idToken);
    Task<List<Dictionary<string, object>>> GetCollectionAsync(string path, string idToken);


    // OTP — reads a document at any path (not scoped to a user collection)
    Task<Dictionary<string, object>?> GetOtpDocumentAsync(string path, string idToken);
}
