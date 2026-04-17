namespace allyza.Services;

public interface IOtpService
{
    Task<bool> SendOtpAsync(string email, string idToken);
    Task<OtpVerifyResult> VerifyOtpAsync(string email, string code, string idToken);
}

public enum OtpVerifyResult
{
    Success,
    InvalidCode,
    Expired,
    NotFound,
}
