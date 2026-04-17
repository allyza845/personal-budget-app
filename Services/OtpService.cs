using System.Security.Cryptography;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace allyza.Services;

public class OtpService : IOtpService
{
    private readonly IFirestoreService _db;

    private const string SmtpHost = "smtp.gmail.com";
    private const int SmtpPort = 587;
    private const string SmtpUser = "azylla.667@gmail.com"; // ← replace
    private const string SmtpPassword = "fazpunsimjndwuha";    // ← replace, no spaces
    private const string FromName = "Personal Budget App";

    private static readonly TimeSpan OtpExpiry = TimeSpan.FromMinutes(10);

    public OtpService(IFirestoreService db) => _db = db;

    // ── SEND ──────────────────────────────────────────────────────────────────
    public async Task<bool> SendOtpAsync(string email, string idToken)
    {
        try
        {
            var code = GenerateCode();
            var hash = HashCode(code);
            var expires = DateTime.UtcNow.Add(OtpExpiry).ToString("o");
            var docKey = SanitiseEmail(email);

            System.Diagnostics.Debug.WriteLine($"[OTP] Saving to Firestore: otps/{docKey}");

            var saved = await _db.SetDocumentAsync(
                $"otps/{docKey}",
                idToken,
                new Dictionary<string, object>
                {
                    ["email"] = email,
                    ["codeHash"] = hash,
                    ["expiresAt"] = expires,
                });

            if (!saved)
            {
                System.Diagnostics.Debug.WriteLine("[OTP] Firestore save FAILED.");
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"[OTP] Firestore OK. Sending email to {email}...");
            await SendEmailAsync(email, code);
            System.Diagnostics.Debug.WriteLine("[OTP] Email sent OK.");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OTP] SEND ERROR: {ex.GetType().Name} — {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[OTP] Inner: {ex.InnerException?.Message}");
            System.Diagnostics.Debug.WriteLine($"[OTP] Inner2: {ex.InnerException?.InnerException?.Message}");
            return false;
        }
    }

    // ── VERIFY ────────────────────────────────────────────────────────────────
    public async Task<OtpVerifyResult> VerifyOtpAsync(string email, string code, string idToken)
    {
        try
        {
            var docKey = SanitiseEmail(email);
            var doc = await _db.GetOtpDocumentAsync($"otps/{docKey}", idToken);

            if (doc is null)
            {
                System.Diagnostics.Debug.WriteLine("[OTP] VERIFY: doc not found.");
                return OtpVerifyResult.NotFound;
            }

            if (!doc.TryGetValue("expiresAt", out var expiresRaw)
                || !DateTime.TryParse(expiresRaw?.ToString(), out var expires))
            {
                System.Diagnostics.Debug.WriteLine("[OTP] VERIFY: could not parse expiresAt.");
                return OtpVerifyResult.NotFound;
            }

            if (DateTime.UtcNow > expires)
            {
                System.Diagnostics.Debug.WriteLine("[OTP] VERIFY: expired.");
                await _db.DeleteDocumentAsync($"otps/{docKey}", idToken);
                return OtpVerifyResult.Expired;
            }

            if (!doc.TryGetValue("codeHash", out var storedHash)
                || storedHash?.ToString() != HashCode(code.Trim()))
            {
                System.Diagnostics.Debug.WriteLine("[OTP] VERIFY: wrong code.");
                return OtpVerifyResult.InvalidCode;
            }

            await _db.DeleteDocumentAsync($"otps/{docKey}", idToken);
            System.Diagnostics.Debug.WriteLine("[OTP] VERIFY: success.");
            return OtpVerifyResult.Success;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OTP] VERIFY ERROR: {ex.Message}");
            return OtpVerifyResult.NotFound;
        }
    }

    // ── EMAIL via MailKit ─────────────────────────────────────────────────────
    private static async Task SendEmailAsync(string toEmail, string code)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(FromName, SmtpUser));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = $"{code} is your personal budget app verification code";
        message.Body = new TextPart("html")
        {
            Text = $"""
                <div style="font-family:sans-serif;max-width:480px;margin:auto;padding:32px;
                            background:#D6EEFA;border-radius:16px;border:1.5px solid #7BBDE0;">
                  <h2 style="color:#1A4F72;margin-bottom:8px;">Personal Budget App</h2>
                  <p style="color:#2E6A90;font-size:15px;">Your one-time verification code is:</p>
                  <div style="font-size:42px;font-weight:bold;letter-spacing:12px;
                              color:#0A0A0A;text-align:center;padding:24px 0;">{code}</div>
                  <p style="color:#2E6A90;font-size:13px;">
                    This code expires in <strong>10 minutes</strong>.
                    If you didn't request this, you can safely ignore this email.
                  </p>
                </div>
                """
        };

        using var client = new SmtpClient();

        // ── Bypass OCSP/revocation check ──────────────────────────────────────
        // The error "certificate revocation status could not be determined" is a
        // known issue on Android/iOS where OCSP responders are unreachable.
        // This callback accepts the cert if the ONLY errors are revocation-related.
        client.ServerCertificateValidationCallback = (sender, certificate, chain, sslErrors) =>
        {
            // No errors at all — always accept
            if (sslErrors == System.Net.Security.SslPolicyErrors.None)
                return true;

            // Log what errors we're seeing
            System.Diagnostics.Debug.WriteLine($"[OTP] SSL errors: {sslErrors}");
            if (chain != null)
            {
                foreach (var status in chain.ChainStatus)
                    System.Diagnostics.Debug.WriteLine($"[OTP] Chain status: {status.Status} — {status.StatusInformation}");
            }

            // Accept if the ONLY issue is revocation status (OCSP unreachable)
            // This is safe for smtp.gmail.com which is a well-known trusted server
            if (chain != null)
            {
                var hasNonRevocationError = false;
                foreach (var status in chain.ChainStatus)
                {
                    if (status.Status != System.Security.Cryptography.X509Certificates.X509ChainStatusFlags.NoError
                        && status.Status != System.Security.Cryptography.X509Certificates.X509ChainStatusFlags.RevocationStatusUnknown
                        && status.Status != System.Security.Cryptography.X509Certificates.X509ChainStatusFlags.OfflineRevocation)
                    {
                        hasNonRevocationError = true;
                        break;
                    }
                }
                if (!hasNonRevocationError) return true;
            }

            return false;
        };

        await client.ConnectAsync(SmtpHost, SmtpPort, SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(SmtpUser, SmtpPassword);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    // ── HELPERS ───────────────────────────────────────────────────────────────
    private static string GenerateCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(4);
        var num = BitConverter.ToUInt32(bytes, 0) % 1_000_000;
        return num.ToString("D6");
    }

    private static string HashCode(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string SanitiseEmail(string email)
        => email.ToLowerInvariant().Replace("@", "_at_").Replace(".", "_");
}