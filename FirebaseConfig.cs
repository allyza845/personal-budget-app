namespace allyza;

/// <summary>
/// Replace with your Firebase project values from:
/// Firebase Console → Project Settings → General → Web app config
/// </summary>
public static class FirebaseConfig
{
    public const string ApiKey = "AIzaSyAAen8TWAtmHbCD0EPCCyLRoLXMaw4g-do";
    public const string ProjectId = "budgetplannerapp-efdb5";

    public const string SignInUrl = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={ApiKey}";
    public const string SignUpUrl = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={ApiKey}";
    public const string UpdateUrl = $"https://identitytoolkit.googleapis.com/v1/accounts:update?key={ApiKey}";

    public static string FirestoreBase =>
        $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents";
}