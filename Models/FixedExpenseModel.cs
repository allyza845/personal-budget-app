using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace allyza.Models;

public class FixedExpenseModel : INotifyPropertyChanged
{
    // ── Philippines Standard Time (UTC+8) ────────────────────────────────────
    private static readonly TimeZoneInfo PhTz =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");

    public static DateTime PhToday =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhTz).Date;

    // ── Backing fields ────────────────────────────────────────────────────────
    private bool _isActive = true;
    private string _lastPaidMonth = string.Empty; // "yyyy-MM"
    private DateTime? _paidDate;                    // exact date paid, null if unpaid

    // ── Basic properties ──────────────────────────────────────────────────────
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public double Amount { get; set; }
    public string CategoryId { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryIcon { get; set; } = "🏷️";
    public string CategoryColor { get; set; } = "#7C6FFF";
    public int DueDay { get; set; } = 1;
    public string Frequency { get; set; } = "Monthly";
    public string Notes { get; set; } = string.Empty;

    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; OnPropertyChanged(); NotifyStatus(); }
    }

    /// <summary>Format: "yyyy-MM". Set to empty string when unpaid.</summary>
    public string LastPaidMonth
    {
        get => _lastPaidMonth;
        set { _lastPaidMonth = value; OnPropertyChanged(); NotifyStatus(); }
    }

    /// <summary>
    /// The exact local date the bill was marked paid this month.
    /// Null if not yet paid. Cleared when marked unpaid.
    /// </summary>
    public DateTime? PaidDate
    {
        get => _paidDate;
        set { _paidDate = value; OnPropertyChanged(); NotifyStatus(); }
    }

    // ── Period key ────────────────────────────────────────────────────────────
    public static string CurrentMonthKey => PhToday.ToString("yyyy-MM");

    // ── Paid status ───────────────────────────────────────────────────────────
    public bool IsPaidThisMonth => _lastPaidMonth == CurrentMonthKey;

    // ── Due-day countdown (PH time) ───────────────────────────────────────────
    private int DaysUntilDueInt
    {
        get
        {
            var today = PhToday;
            int capped = Math.Min(DueDay, DateTime.DaysInMonth(today.Year, today.Month));
            return capped - today.Day;
        }
    }

    public bool IsOverdue => IsActive && !IsPaidThisMonth && DaysUntilDueInt < 0;
    public bool IsDueToday => IsActive && !IsPaidThisMonth && DaysUntilDueInt == 0;
    public bool IsUpcomingSoon => IsActive && !IsPaidThisMonth && DaysUntilDueInt > 0 && DaysUntilDueInt <= 7;
    public bool IsScheduled => IsActive && !IsPaidThisMonth && DaysUntilDueInt > 7;

    public string BillStatus =>
          IsPaidThisMonth ? "PAID"
        : !IsActive ? "INACTIVE"
        : IsOverdue ? "OVERDUE"
        : IsDueToday ? "DUE TODAY"
        : IsUpcomingSoon ? "UPCOMING"
        : "SCHEDULED";

    public string BillStatusColor =>
          IsPaidThisMonth ? "#22C55E"
        : !IsActive ? "#3A3A5A"
        : IsOverdue ? "#EF4444"
        : IsDueToday ? "#FF8C00"
        : IsUpcomingSoon ? "#F59E0B"
        : "#5050A0";

    public string BillStatusBg =>
          IsPaidThisMonth ? "#0D2818"
        : !IsActive ? "#1A1A2E"
        : IsOverdue ? "#1A0D0D"
        : IsDueToday ? "#1A0E00"
        : IsUpcomingSoon ? "#1A1000"
        : "#1A1A2E";

    public string ActiveIcon => IsPaidThisMonth ? "✅" : IsActive ? "⬜" : "⏸";
    public string AmountDisplay => $"₱{Amount:N2}";
    public string DueDayDisplay => $"Due {DueDay}{Suffix(DueDay)}";

    public string DaysUntilDue
    {
        get
        {
            if (IsPaidThisMonth) return "Paid ✓";
            if (!IsActive) return "Inactive";
            int d = DaysUntilDueInt;
            if (d < 0) return $"{-d}d overdue";
            if (d == 0) return "Due today";
            return $"Due in {d}d";
        }
    }

    public double MonthlyAmount => Amount;

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string Suffix(int d) => (d % 10) switch
    {
        1 when d != 11 => "st",
        2 when d != 12 => "nd",
        3 when d != 13 => "rd",
        _ => "th"
    };

    private void NotifyStatus()
    {
        foreach (var p in new[]
        {
            nameof(IsPaidThisMonth), nameof(IsOverdue),     nameof(IsDueToday),
            nameof(IsUpcomingSoon),  nameof(IsScheduled),   nameof(BillStatus),
            nameof(BillStatusColor), nameof(BillStatusBg),  nameof(ActiveIcon),
            nameof(DaysUntilDue),    nameof(DueDayDisplay), nameof(PaidDate),
        }) OnPropertyChanged(p);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}