using System.Windows.Input;
using allyza.Services;

namespace allyza.ViewModels;

public class SignUpViewModel : BaseViewModel
{
    private readonly IFirebaseAuthService _auth;
    private readonly IFirestoreService _db;
    private readonly IOtpService _otp;

    // ── Form fields ───────────────────────────────────────────────────────────
    private string _name = "", _email = "", _password = "", _confirm = "";

    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public string Email { get => _email; set { _email = value; OnPropertyChanged(); } }
    public string Password { get => _password; set { _password = value; OnPropertyChanged(); } }
    public string Confirm { get => _confirm; set { _confirm = value; OnPropertyChanged(); } }

    // ── Messages ──────────────────────────────────────────────────────────────
    private string _errorMsg = "", _successMsg = "";

    public string ErrorMessage
    {
        get => _errorMsg;
        set { _errorMsg = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
    }
    public string SuccessMessage
    {
        get => _successMsg;
        set { _successMsg = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSuccess)); }
    }
    public bool HasError => !string.IsNullOrEmpty(_errorMsg);
    public bool HasSuccess => !string.IsNullOrEmpty(_successMsg);

    // ── OTP step state ────────────────────────────────────────────────────────
    private bool _otpStepVisible = false;
    private string _otpCode = string.Empty;
    private bool _resendEnabled = true;
    private string _resendLabel = "Resend Code";
    private string _pendingIdToken = string.Empty;

    public bool OtpStepVisible { get => _otpStepVisible; private set { _otpStepVisible = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormStepVisible)); } }
    public bool FormStepVisible => !_otpStepVisible;
    public string OtpCode { get => _otpCode; set { _otpCode = value; OnPropertyChanged(); } }
    public bool ResendEnabled { get => _resendEnabled; set { _resendEnabled = value; OnPropertyChanged(); } }
    public string ResendLabel { get => _resendLabel; set { _resendLabel = value; OnPropertyChanged(); } }

    // ── Commands ──────────────────────────────────────────────────────────────
    public ICommand SignUpCommand { get; }
    public ICommand VerifyCommand { get; }
    public ICommand ResendCommand { get; }
    public ICommand BackCommand { get; }

    public SignUpViewModel(IFirebaseAuthService auth, IFirestoreService db, IOtpService otp)
    {
        _auth = auth;
        _db = db;
        _otp = otp;
        Title = "Create Account";

        SignUpCommand = new Command(async () => await SignUpAsync());
        VerifyCommand = new Command(async () => await VerifyAsync());
        ResendCommand = new Command(async () => await ResendAsync());
        BackCommand = new Command(async () =>
            await Application.Current!.MainPage!.Navigation.PopAsync());
    }

    // ── STEP 1: Create account + send OTP ────────────────────────────────────
    private async Task SignUpAsync()
    {
        if (IsBusy) return;
        ErrorMessage = SuccessMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Name)) { ErrorMessage = "Please enter your name."; return; }
        if (string.IsNullOrWhiteSpace(Email)) { ErrorMessage = "Please enter your email."; return; }
        if (Password.Length < 6) { ErrorMessage = "Password must be at least 6 characters."; return; }
        if (Password != Confirm) { ErrorMessage = "Passwords do not match."; return; }

        IsBusy = true;
        try
        {
            // Create Firebase account — matches your existing tuple return
            var (success, error) = await _auth.SignUpAsync(Email.Trim(), Password, Name.Trim());
            if (!success) { ErrorMessage = error; return; }

            var user = _auth.CurrentUser!;
            _pendingIdToken = user.IdToken;

            // Save user profile to Firestore (same as before)
            await _db.SaveUserProfileAsync(user.Uid, user.IdToken, new Dictionary<string, object>
            {
                ["displayName"] = Name.Trim(),
                ["email"] = Email.Trim(),
                ["createdAt"] = DateTime.UtcNow.ToString("o"),
            });

            // Send OTP email
            var sent = await _otp.SendOtpAsync(Email.Trim(), user.IdToken);
            if (!sent)
            {
                ErrorMessage = "Account created but failed to send verification email. Tap Resend.";
                OtpStepVisible = true;
                return;
            }

            SuccessMessage = $"Code sent to {Email.Trim()} ✉️";
            OtpStepVisible = true;
            StartResendCooldown();
        }
        finally { IsBusy = false; }
    }

    // ── STEP 2: Verify OTP ────────────────────────────────────────────────────
    private async Task VerifyAsync()
    {
        if (IsBusy) return;
        ErrorMessage = SuccessMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(OtpCode) || OtpCode.Trim().Length != 6)
        { ErrorMessage = "Please enter the 6-digit code."; return; }

        IsBusy = true;
        try
        {
            var result = await _otp.VerifyOtpAsync(Email.Trim(), OtpCode.Trim(), _pendingIdToken);
            switch (result)
            {
                case OtpVerifyResult.Success:
                    SuccessMessage = "✓ Verified! Welcome to Personal Budget Management App";
                    await Task.Delay(900);
                    Application.Current!.MainPage = new AppShell();
                    break;

                case OtpVerifyResult.InvalidCode:
                    ErrorMessage = "Incorrect code. Please try again.";
                    break;

                case OtpVerifyResult.Expired:
                    ErrorMessage = "Code has expired. Tap Resend for a new one.";
                    break;

                case OtpVerifyResult.NotFound:
                    ErrorMessage = "Verification record not found. Tap Resend.";
                    break;
            }
        }
        finally { IsBusy = false; }
    }

    // ── RESEND ────────────────────────────────────────────────────────────────
    private async Task ResendAsync()
    {
        if (IsBusy || !ResendEnabled) return;
        ErrorMessage = SuccessMessage = string.Empty;
        IsBusy = true;
        try
        {
            var sent = await _otp.SendOtpAsync(Email.Trim(), _pendingIdToken);
            if (sent)
            {
                OtpCode = string.Empty;
                SuccessMessage = "New code sent! Check your inbox.";
                StartResendCooldown();
            }
            else
            {
                ErrorMessage = "Failed to send email. Check your connection.";
            }
        }
        finally { IsBusy = false; }
    }

    // ── 60s resend cooldown ───────────────────────────────────────────────────
    private void StartResendCooldown()
    {
        ResendEnabled = false;
        var seconds = 60;
        ResendLabel = $"Resend in {seconds}s";

        var timer = new System.Timers.Timer(1000);
        timer.Elapsed += (_, _) =>
        {
            seconds--;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (seconds > 0)
                {
                    ResendLabel = $"Resend in {seconds}s";
                }
                else
                {
                    ResendLabel = "Resend Code";
                    ResendEnabled = true;
                    timer.Stop();
                    timer.Dispose();
                }
            });
        };
        timer.Start();
    }
}