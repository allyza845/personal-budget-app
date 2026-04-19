using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using allyza.Models;
using allyza.Services;

namespace allyza.ViewModels;

public class DashboardViewModel : BaseViewModel
{
    private readonly IFirestoreService _db;
    private readonly IFirebaseAuthService _auth;

    // ── PH Time ───────────────────────────────────────────────────────────────
    private static readonly TimeZoneInfo PhTz =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");

    private static DateTime PhToday => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhTz).Date;
    private static DateTime PhNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhTz);

    // ── Summary ───────────────────────────────────────────────────────────────
    public double MonthlyIncome { get; private set; }
    public double MonthlyExpenses { get; private set; }
    public double MonthlyFixed { get; private set; }

    public double NetBalance => MonthlyIncome - MonthlyExpenses - MonthlyFixed;
    public double SpentRatio => MonthlyIncome > 0
        ? Math.Min((MonthlyExpenses + MonthlyFixed) / MonthlyIncome, 1.0) : 0;

    // Percent of income saved (follows Analytics' single-month calculation)
    public double SavingsRate { get; private set; } // in percent, can be negative
    public string SavingsRateDisplay => MonthlyIncome > 0 ? $"{SavingsRate:F1}%" : "—";
    public string SavingsRateColor => SavingsRate >= 0 ? "#22C55E" : "#EF4444";

    public string GreetingText { get; private set; } = string.Empty;
    public string TodayLabel => PhToday.ToString("dddd, MMMM d yyyy");
    public string MonthLabel => PhToday.ToString("MMMM yyyy");

    public string MonthlyIncomeDisplay => $"₱{MonthlyIncome:N2}";
    public string MonthlyExpensesDisplay => $"₱{MonthlyExpenses:N2}";
    public string MonthlyFixedDisplay => $"₱{MonthlyFixed:N2}";
    public string NetBalanceDisplay => $"₱{Math.Abs(NetBalance):N2}";
    public string NetBalancePrefix => NetBalance >= 0 ? "+" : "-";
    public string NetBalanceColor => NetBalance >= 0 ? "#22C55E" : "#EF4444";
    public string SpentPercentDisplay => $"{SpentRatio * 100:F1}% of income spent";
    public string SpentBarColor => SpentRatio > 0.9 ? "#EF4444"
                                          : SpentRatio > 0.7 ? "#F59E0B" : "#22C55E";

    // ── Daily Spent ───────────────────────────────────────────────────────────
    public double TodaySpent { get; private set; }
    public double YesterdaySpent { get; private set; }
    public double WeekSpent { get; private set; }

    public string TodaySpentDisplay => $"₱{TodaySpent:N2}";
    public string YesterdaySpentDisplay => $"₱{YesterdaySpent:N2}";
    public string WeekSpentDisplay => $"₱{WeekSpent:N2}";

    public string DailyTrendIcon => TodaySpent > YesterdaySpent ? "↑"
                                   : TodaySpent < YesterdaySpent ? "↓" : "→";
    public string DailyTrendColor => TodaySpent > YesterdaySpent ? "#EF4444"
                                   : TodaySpent < YesterdaySpent ? "#22C55E" : "#5A9ABF";
    public string DailyTrendLabel => TodaySpent > YesterdaySpent
        ? $"₱{TodaySpent - YesterdaySpent:N2} more than yesterday"
        : TodaySpent < YesterdaySpent
        ? $"₱{YesterdaySpent - TodaySpent:N2} less than yesterday"
        : "Same as yesterday";

    // ── Bill counts ───────────────────────────────────────────────────────────
    public int OverdueCount { get; private set; }
    public int UpcomingCount { get; private set; }
    public int PaidCount { get; private set; }
    public bool HasOverdue => OverdueCount > 0;
    public bool HasUpcoming => UpcomingCount > 0;
    public bool HasPaid => PaidCount > 0;

    // ── Collections ───────────────────────────────────────────────────────────
    public ObservableCollection<FixedExpenseModel> OverdueBills { get; } = new();
    public ObservableCollection<FixedExpenseModel> UpcomingBills { get; } = new();
    public ObservableCollection<FixedExpenseModel> PaidBills { get; } = new();
    public ObservableCollection<FixedExpenseModel> ScheduledBills { get; } = new();
    public ObservableCollection<TransactionModel> RecentTxns { get; } = new();

    public bool HasRecentTxns => RecentTxns.Count > 0;
    public bool HasScheduled => ScheduledBills.Count > 0;

    // ── Commands ──────────────────────────────────────────────────────────────
    public ICommand LoadCommand { get; }
    public ICommand MarkPaidCommand { get; }
    public ICommand MarkUnpaidCommand { get; }

    public DashboardViewModel(IFirestoreService db, IFirebaseAuthService auth)
    {
        _db = db; _auth = auth; Title = "Dashboard";
        LoadCommand = new Command(async () => await LoadAsync());
        MarkPaidCommand = new Command<FixedExpenseModel>(async f => await MarkPaidAsync(f, true));
        MarkUnpaidCommand = new Command<FixedExpenseModel>(async f => await MarkPaidAsync(f, false));
        SetGreeting();
    }

    // ── LOAD ──────────────────────────────────────────────────────────────────
    public async Task LoadAsync()
    {
        var user = _auth.CurrentUser;
        if (user is null) return;

        MainThread.BeginInvokeOnMainThread(() => IsBusy = true);
        try
        {
            SetGreeting();

            var today = DateTime.Today;
            var yesterday = today.AddDays(-1);
            var weekStart = today.AddDays(-6);
            var monthStart = new DateTime(today.Year, today.Month, 1);

            var incomeTask = _db.GetCollectionAsync($"users/{user.Uid}/incomes", user.IdToken);
            var expenseTask = _db.GetCollectionAsync($"users/{user.Uid}/expenses", user.IdToken);
            var fixedTask = _db.GetCollectionAsync($"users/{user.Uid}/fixedExpenses", user.IdToken);
            await Task.WhenAll(incomeTask, expenseTask, fixedTask);

            // ── Parse variable expenses ───────────────────────────────────────
            var parsedExpenses = expenseTask.Result
                .Select(d => (doc: d, date: ParseDateLocal(d, "date")))
                .Where(x => x.date.HasValue)
                .Select(x => (x.doc, date: x.date!.Value.Date, amount: ParseDouble(x.doc, "amount")))
                .ToList();

            // ── Monthly income ────────────────────────────────────────────────
            double monthlyIncome = incomeTask.Result
                .Where(d => ParseDateLocal(d, "date") is DateTime dt
                            && dt.Date >= monthStart && dt.Date <= today)
                .Sum(d => ParseDouble(d, "amount"));

            // ── Monthly variable expenses ─────────────────────────────────────
            double monthlyExpenses = parsedExpenses
                .Where(x => x.date >= monthStart && x.date <= today)
                .Sum(x => x.amount);

            // ── Fixed expenses ────────────────────────────────────────────────
            var fixedModels = ParseFixedExpenses(fixedTask.Result);
            double monthlyFixed = fixedModels
                .Where(f => f.IsActive && f.IsPaidThisMonth)
                .Sum(f => f.MonthlyAmount);

            // ── Fixed bills paid on exact dates (uses PaidDate, not DueDay) ───
            // Only counts bills that were actually marked paid today/yesterday/
            // this week — not bills merely due on those days.
            double fixedPaidToday = fixedModels
                .Where(f => f.IsActive && f.PaidDate.HasValue
                            && f.PaidDate.Value.Date == today)
                .Sum(f => f.MonthlyAmount);

            double fixedPaidYesterday = fixedModels
                .Where(f => f.IsActive && f.PaidDate.HasValue
                            && f.PaidDate.Value.Date == yesterday)
                .Sum(f => f.MonthlyAmount);

            double fixedPaidThisWeek = fixedModels
                .Where(f => f.IsActive && f.PaidDate.HasValue
                            && f.PaidDate.Value.Date >= weekStart
                            && f.PaidDate.Value.Date <= today)
                .Sum(f => f.MonthlyAmount);

            // ── Combined daily / weekly (variable + fixed actually paid) ──────
            double todaySpent = parsedExpenses.Where(x => x.date == today).Sum(x => x.amount) + fixedPaidToday;
            double yesterdaySpent = parsedExpenses.Where(x => x.date == yesterday).Sum(x => x.amount) + fixedPaidYesterday;
            double weekSpent = parsedExpenses.Where(x => x.date >= weekStart && x.date <= today).Sum(x => x.amount) + fixedPaidThisWeek;

            // ── Bill lists ────────────────────────────────────────────────────
            var overdue = new List<FixedExpenseModel>();
            var upcoming = new List<FixedExpenseModel>();
            var paid = new List<FixedExpenseModel>();
            var scheduled = new List<FixedExpenseModel>();

            foreach (var f in fixedModels.Where(f => f.IsActive).OrderBy(f => f.DueDay))
            {
                if (f.IsPaidThisMonth) paid.Add(f);
                else if (f.IsOverdue) overdue.Add(f);
                else if (f.IsUpcomingSoon) upcoming.Add(f);
                else scheduled.Add(f);
            }

            // ── Recent transactions ───────────────────────────────────────────
            var all = new List<TransactionModel>();
            foreach (var d in incomeTask.Result)
                all.Add(new TransactionModel
                {
                    Id = ParseStr(d, "id"),
                    Type = "income",
                    Title = ParseStr(d, "source").OrD("Income"),
                    Amount = ParseDouble(d, "amount"),
                    CategoryIcon = "💰",
                    CategoryColor = "#22C55E",
                    CategoryName = ParseStr(d, "frequency"),
                    Date = ParseDateLocal(d, "date") ?? today,
                });

            foreach (var x in parsedExpenses)
                all.Add(new TransactionModel
                {
                    Id = ParseStr(x.doc, "id"),
                    Type = "expense",
                    Title = ParseStr(x.doc, "description").OrD("Expense"),
                    Amount = x.amount,
                    CategoryIcon = ParseStr(x.doc, "categoryIcon").OrD("📦"),
                    CategoryColor = ParseStr(x.doc, "categoryColor").OrD("#7C6FFF"),
                    CategoryName = ParseStr(x.doc, "categoryName"),
                    Date = x.date,
                });

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                MonthlyIncome = monthlyIncome;
                MonthlyExpenses = monthlyExpenses;
                MonthlyFixed = monthlyFixed;
                TodaySpent = todaySpent;
                YesterdaySpent = yesterdaySpent;
                WeekSpent = weekSpent;
                NotifyFinancials();

                OverdueBills.Clear(); foreach (var f in overdue) OverdueBills.Add(f);
                UpcomingBills.Clear(); foreach (var f in upcoming) UpcomingBills.Add(f);
                PaidBills.Clear(); foreach (var f in paid) PaidBills.Add(f);
                ScheduledBills.Clear(); foreach (var f in scheduled) ScheduledBills.Add(f);

                OverdueCount = overdue.Count;
                UpcomingCount = upcoming.Count;
                PaidCount = paid.Count;
                NotifyBillCounts();

                RecentTxns.Clear();
                foreach (var t in all.OrderByDescending(t => t.Date).Take(5))
                    RecentTxns.Add(t);

                OnPropertyChanged(nameof(HasRecentTxns));
                OnPropertyChanged(nameof(HasScheduled));
            });
        }
        finally { MainThread.BeginInvokeOnMainThread(() => IsBusy = false); }
    }

    // ── MARK PAID (from Dashboard) ────────────────────────────────────────────
    private async Task MarkPaidAsync(FixedExpenseModel item, bool paid)
    {
        var user = _auth.CurrentUser;
        if (user is null) return;

        item.LastPaidMonth = paid ? FixedExpenseModel.CurrentMonthKey : string.Empty;
        item.PaidDate = paid ? DateTime.Today : null;

        await _db.SetDocumentAsync(
            $"users/{user.Uid}/fixedExpenses/{item.Id}", user.IdToken,
            new Dictionary<string, object>
            {
                ["id"] = item.Id,
                ["name"] = item.Name,
                ["amount"] = item.Amount,
                ["frequency"] = item.Frequency,
                ["dueDay"] = (double)item.DueDay,
                ["categoryId"] = item.CategoryId,
                ["categoryName"] = item.CategoryName,
                ["categoryIcon"] = item.CategoryIcon,
                ["categoryColor"] = item.CategoryColor,
                ["isActive"] = item.IsActive,
                ["lastPaidMonth"] = item.LastPaidMonth,
                ["notes"] = item.Notes,
                ["paidDate"] = item.PaidDate.HasValue
                                    ? item.PaidDate.Value.ToString("o")
                                    : string.Empty,
            });
        await LoadAsync();
    }

    // ── HELPERS ───────────────────────────────────────────────────────────────
    private static List<FixedExpenseModel> ParseFixedExpenses(List<Dictionary<string, object>> docs)
    {
        var result = new List<FixedExpenseModel>();
        foreach (var d in docs)
        {
            try
            {
                DateTime? paidDate = null;
                if (d.TryGetValue("paidDate", out var pd) && pd?.ToString() is string pds
                    && !string.IsNullOrEmpty(pds)
                    && DateTime.TryParse(pds, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var parsedPd))
                {
                    paidDate = parsedPd.ToLocalTime().Date;
                }

                result.Add(new FixedExpenseModel
                {
                    Id = ParseStr(d, "id"),
                    Name = ParseStr(d, "name"),
                    Amount = ParseDouble(d, "amount"),
                    Frequency = "Monthly",
                    DueDay = (int)ParseDouble(d, "dueDay"),
                    CategoryId = ParseStr(d, "categoryId"),
                    CategoryName = ParseStr(d, "categoryName"),
                    CategoryIcon = ParseStr(d, "categoryIcon").OrD("🏷️"),
                    CategoryColor = ParseStr(d, "categoryColor").OrD("#7C6FFF"),
                    IsActive = d.TryGetValue("isActive", out var ia) && ia is bool b ? b : true,
                    LastPaidMonth = ParseStr(d, "lastPaidMonth"),
                    Notes = ParseStr(d, "notes"),
                    PaidDate = paidDate,
                });
            }
            catch { }
        }
        return result;
    }

    private static string ParseStr(Dictionary<string, object> d, string k) =>
        d.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";
    private static double ParseDouble(Dictionary<string, object> d, string k) =>
        d.TryGetValue(k, out var v) ? Convert.ToDouble(v) : 0;

    private static DateTime? ParseDateLocal(Dictionary<string, object> d, string k)
    {
        if (!d.TryGetValue(k, out var v)) return null;
        var s = v?.ToString();
        if (string.IsNullOrEmpty(s)) return null;
        if (DateTime.TryParseExact(s, "o", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime();
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var dt2))
            return dt2.ToLocalTime();
        return null;
    }

    private void SetGreeting()
    {
        var now = PhNow;
        var tod = now.Hour < 12 ? "Good morning" : now.Hour < 18 ? "Good afternoon" : "Good evening";
        var name = _auth.CurrentUser?.DisplayName;
        GreetingText = string.IsNullOrEmpty(name) ? $"{tod}!" : $"{tod}, {name}!";
        OnPropertyChanged(nameof(GreetingText));
    }

    private void NotifyFinancials()
    {
        foreach (var p in new[] {
            nameof(MonthlyIncome),         nameof(MonthlyExpenses),       nameof(MonthlyFixed),
            nameof(NetBalance),            nameof(SpentRatio),
            nameof(MonthlyIncomeDisplay),  nameof(MonthlyExpensesDisplay),
            nameof(MonthlyFixedDisplay),   nameof(NetBalanceDisplay),
            nameof(NetBalancePrefix),      nameof(NetBalanceColor),
            nameof(SpentPercentDisplay),   nameof(SpentBarColor),
            nameof(TodaySpent),            nameof(TodaySpentDisplay),
            nameof(YesterdaySpent),        nameof(YesterdaySpentDisplay),
            nameof(WeekSpent),             nameof(WeekSpentDisplay),
            nameof(DailyTrendIcon),        nameof(DailyTrendColor),       nameof(DailyTrendLabel),
        }) OnPropertyChanged(p);
    }

    private void NotifyBillCounts()
    {
        foreach (var p in new[] {
            nameof(OverdueCount), nameof(UpcomingCount), nameof(PaidCount),
            nameof(HasOverdue),   nameof(HasUpcoming),   nameof(HasPaid),
        }) OnPropertyChanged(p);
    }
}

file static class DashExt
{
    public static string OrD(this string s, string def) => string.IsNullOrEmpty(s) ? def : s;
}