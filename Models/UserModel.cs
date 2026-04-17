namespace allyza.Models;

public class UserModel
{
    public string Uid { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string IdToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
}