using Newtonsoft.Json;

namespace allyza.Models;

public class FirebaseAuthResponse
{
    [JsonProperty("idToken")] public string IdToken { get; set; } = string.Empty;
    [JsonProperty("email")] public string Email { get; set; } = string.Empty;
    [JsonProperty("refreshToken")] public string RefreshToken { get; set; } = string.Empty;
    [JsonProperty("localId")] public string LocalId { get; set; } = string.Empty;
    [JsonProperty("displayName")] public string DisplayName { get; set; } = string.Empty;
}

public class FirebaseErrorResponse
{
    [JsonProperty("error")] public FirebaseError? Error { get; set; }
}

public class FirebaseError
{
    [JsonProperty("message")] public string Message { get; set; } = string.Empty;
    [JsonProperty("code")] public int Code { get; set; }
}