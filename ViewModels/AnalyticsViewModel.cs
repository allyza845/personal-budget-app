using System.Collections.ObjectModel;
using System.Windows.Input;
using allyza.Models;
using allyza.Services;

namespace allyza.ViewModels;

public class AnalyticsViewModel : BaseViewModel
{
    private readonly IFirestoreService _db;
    private readonly IFirebaseAuthService _auth;

    // ── Philippines Standard Time ─────────────────────────────────────────────
    private static readonly TimeZoneInfo PhTz =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");
    private static DateTime PhToday => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhTz).Date;

    private string _selectedPeriod = "This Month";

    public string SelectedPeriod
    {
        get => _selectedPeriod;
        set { _selectedPeriod = value; OnPropertyChanged(); UpdatePeriodChips(); _ = LoadAsync(); }
    }

    public bool IsThisMonth => _selectedPeriod == "This Month";
    public bool IsLastMonth => _selectedPeriod == "Last Month";
    public bool IsThisYear => _selectedPeriod == "This Year";
    public bool IsAllTime => _selectedPeriod == "All Time";

    // ── Summary ───────────────────────────────────────────────────────────────
    public double TotalIncome { get; private set; }
    public double TotalExpenses { get; private set; }
    public double TotalFixed { get; private set; }
    public double TotalBudget { get; private set; }

    public double NetSavings => TotalIncome - TotalExpenses - TotalFixed;
    public double SavingsRate => TotalIncome > 0 ? NetSavings / TotalIncome * 100 : 0;
    public double BudgetUsed => TotalExpenses;
    public double BudgetRemaining => Math.Max(TotalBudget - TotalExpenses, 0);
    public double BudgetOverspent => Math.Max(TotalExpenses - TotalBudget, 0);
    public bool IsOverBudget => TotalBudget > 0 && TotalExpenses > TotalBudget;
    public double BudgetRatio => TotalBudget > 0 ? Math.Min(TotalExpenses / TotalBudget, 1.0) : 0;

    public string TotalIncomeDisplay => $"₱{TotalIncome:N2}";
    public string TotalExpensesDisplay => $"₱{TotalExpenses:N2}";
    public string TotalFixedDisplay => $"₱{TotalFixed:N2}";
    public string NetSavingsDisplay => $"₱{Math.Abs(NetSavings):N2}";
    public string SavingsRateDisplay => $"{SavingsRate:F1}%";
    public string NetSavingsColor => NetSavings >= 0 ? "#166534" : "#EF4444";
    public string NetSavingsLabel => NetSavings >= 0 ? "Saved" : "Overspent";
    public string BudgetRemainingDisplay => $"₱{BudgetRemaining:N2}";
    public string BudgetOverspentDisplay => $"₱{BudgetOverspent:N2}";
    public string BudgetStatusColor =>
    IsOverBudget ? "#EF4444"
    : BudgetRatio > 0.8 ? "#F59E0B"
    : "#166534";
    public string BudgetStatusLabel => IsOverBudget
        ? $"₱{BudgetOverspent:N2} over budget"
        : $"₱{BudgetRemaining:N2} remaining";
    public bool HasBudgets => BudgetItems.Any(b => b.HasBudget);

    public double SpentRatio => TotalIncome > 0
        ? Math.Min((TotalExpenses + TotalFixed) / TotalIncome, 1.0) : 0;
    public string SpentBarColor =>
     SpentRatio > 0.9 ? "#EF4444"
     : SpentRatio > 0.7 ? "#F59E0B"
     : "#166534";
    public string SpentRatioDisplay => $"{SpentRatio * 100:F0}% of income used";

    public string InsightText { get; private set; } = string.Empty;
    public bool HasInsight => !string.IsNullOrEmpty(InsightText);

    public ObservableCollection<BudgetModel> BudgetItems { get; } = new();
    public ObservableCollection<CategorySummary> TopCategories { get; } = new();
    public ObservableCollection<MonthlyTrendModel> MonthlyTrend { get; } = new();

    public bool HasCategories => TopCategories.Count > 0;
    public bool HasTrend => MonthlyTrend.Count > 0;

    public ICommand LoadCommand { get; }
    public ICommand SetPeriodCommand { get; }
    public ICommand SetBudgetCommand { get; }

    public AnalyticsViewModel(IFirestoreService db, IFirebaseAuthService auth)
    {
        _db = db;
        _auth = auth;
        Title = "Analytics";

        LoadCommand = new Command(async () => await LoadAsync());
        SetPeriodCommand = new Command<string>(p => SelectedPeriod = p);
        SetBudgetCommand = new Command<BudgetModel>(async b => await SetBudgetAsync(b));
    }

    // ── LOAD ──────────────────────────────────────────────────────────────────
    public async Task LoadAsync()
    {
        var user = _auth.CurrentUser;
        if (user is null) return;

        MainThread.BeginInvokeOnMainThread(() => IsBusy = true);
        try
        {
            var incomeTask = _db.GetCollectionAsync($"users/{user.Uid}/incomes", user.IdToken);
            var expenseTask = _db.GetCollectionAsync($"users/{user.Uid}/expenses", user.IdToken);
            var fixedTask = _db.GetCollectionAsync($"users/{user.Uid}/fixedExpenses", user.IdToken);
            var budgetTask = _db.GetCollectionAsync($"users/{user.Uid}/budgets", user.IdToken);
            await Task.WhenAll(incomeTask, expenseTask, fixedTask, budgetTask);

            var allIncomes = ParseIncomes(incomeTask.Result);
            var allExpenses = ParseExpenses(expenseTask.Result);

            var (start, end) = GetPeriodRange();
            var incomes = allIncomes.Where(i => i.Date >= start && i.Date <= end).ToList();
            var expenses = allExpenses.Where(e => e.Date >= start && e.Date <= end).ToList();
            var fixedExp = ParseFixed(fixedTask.Result);

            double totalIncome = incomes.Sum(i => i.Amount);
            double totalExpenses = expenses.Sum(e => e.Amount);
            double totalFixed = fixedExp
                .Where(f => f.IsActive && f.IsPaidThisMonth)
                .Sum(f => f.Amount);

            var budgetItems = BuildBudgetItemsList(budgetTask.Result, expenses);
            double totalBudget = budgetItems.Where(b => b.HasBudget).Sum(b => b.LimitAmount);

            var categoryItems = BuildCategoryList(expenses);
            var trendItems = BuildTrendList(allIncomes, allExpenses);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                TotalIncome = totalIncome;
                TotalExpenses = totalExpenses;
                TotalFixed = totalFixed;
                TotalBudget = totalBudget;
                NotifyAll();

                BudgetItems.Clear();
                foreach (var b in budgetItems) BudgetItems.Add(b);
                OnPropertyChanged(nameof(HasBudgets));

                TopCategories.Clear();
                foreach (var c in categoryItems) TopCategories.Add(c);
                OnPropertyChanged(nameof(HasCategories));

                MonthlyTrend.Clear();
                foreach (var t in trendItems) MonthlyTrend.Add(t);
                OnPropertyChanged(nameof(HasTrend));

                BuildInsight();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Analytics] CRASH: {ex}");
            await MainThread.InvokeOnMainThreadAsync(() =>
                Application.Current?.MainPage?.DisplayAlert(
                    "Error", $"{ex.GetType().Name}: {ex.Message}\n\nInner: {ex.InnerException?.Message}", "OK"));
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() => IsBusy = false);
        }
    }

    // ── SET BUDGET ────────────────────────────────────────────────────────────
    private async Task SetBudgetAsync(BudgetModel budget)
    {
        var input = await MainThread.InvokeOnMainThreadAsync(() =>
            Application.Current!.MainPage!.DisplayPromptAsync(
                $"Budget for {budget.CategoryName}",
                $"Enter monthly budget limit (current: {budget.LimitDisplay})",
                initialValue: budget.HasBudget ? budget.LimitAmount.ToString("F2") : "",
                keyboard: Microsoft.Maui.Keyboard.Numeric,
                placeholder: "0.00"));

        if (input is null) return;

        var raw = input.Replace("₱", "").Replace(",", "").Trim();
        if (!double.TryParse(raw, out double amount) || amount < 0)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                Application.Current!.MainPage!.DisplayAlert("Invalid", "Please enter a valid amount.", "OK"));
            return;
        }

        var user = _auth.CurrentUser;
        if (user is null) return;

        budget.LimitAmount = amount;

        await _db.SetDocumentAsync(
            $"users/{user.Uid}/budgets/{budget.CategoryId}",
            user.IdToken,
            new Dictionary<string, object>
            {
                ["id"] = budget.CategoryId,
                ["categoryId"] = budget.CategoryId,
                ["categoryName"] = budget.CategoryName,
                ["categoryIcon"] = budget.CategoryIcon,
                ["categoryColor"] = budget.CategoryColor,
                ["limitAmount"] = budget.LimitAmount,
            });

        await LoadAsync();
    }

    // ── BUILD BUDGET ITEMS ────────────────────────────────────────────────────
    private static List<BudgetModel> BuildBudgetItemsList(
        List<Dictionary<string, object>> budgetDocs,
        List<ExpenseEntry> expenses)
    {
        var limits = new Dictionary<string, (string name, string icon, string color, double limit)>();
        foreach (var d in budgetDocs)
        {
            var catId = Str(d, "categoryId");
            if (string.IsNullOrEmpty(catId)) continue;
            limits[catId] = (
                Str(d, "categoryName"),
                Str(d, "categoryIcon").OrD("🏷️"),
                Str(d, "categoryColor").OrD("#7C6FFF"),
                Dbl(d, "limitAmount"));
        }

        var spentByCategory = expenses
            .GroupBy(e => e.CategoryName)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<BudgetModel>();

        foreach (var (catId, (name, icon, color, limit)) in limits)
        {
            var spent = spentByCategory.TryGetValue(name, out var list) ? list.Sum(e => e.Amount) : 0;
            result.Add(new BudgetModel
            {
                Id = catId,
                CategoryId = catId,
                CategoryName = name,
                CategoryIcon = icon,
                CategoryColor = color,
                LimitAmount = limit,
                SpentAmount = spent,
            });
        }

        foreach (var (catName, list) in spentByCategory)
        {
            if (result.Any(b => b.CategoryName == catName)) continue;
            var first = list.First();
            result.Add(new BudgetModel
            {
                Id = first.CategoryId,
                CategoryId = first.CategoryId,
                CategoryName = catName,
                CategoryIcon = first.CategoryIcon,
                CategoryColor = first.CategoryColor,
                LimitAmount = 0,
                SpentAmount = list.Sum(e => e.Amount),
            });
        }

        return result
            .OrderByDescending(b => b.IsOverBudget)
            .ThenByDescending(b => b.UsageRatio)
            .ThenByDescending(b => b.SpentAmount)
            .ToList();
    }

    // ── BUILD CATEGORY LIST ───────────────────────────────────────────────────
    private static List<CategorySummary> BuildCategoryList(List<ExpenseEntry> expenses)
    {
        if (!expenses.Any()) return new();

        var total = expenses.Sum(e => e.Amount);
        return expenses
            .GroupBy(e => e.CategoryName)
            .Select(g => new CategorySummary
            {
                Name = g.Key,
                Icon = g.First().CategoryIcon,
                Color = g.First().CategoryColor,
                Amount = g.Sum(e => e.Amount),
                Percentage = total > 0 ? g.Sum(e => e.Amount) / total * 100 : 0,
            })
            .OrderByDescending(c => c.Amount)
            .Take(7)
            .ToList();
    }

    // ── BUILD TREND LIST ──────────────────────────────────────────────────────
    private static List<MonthlyTrendModel> BuildTrendList(
        List<IncomeEntry> incomes, List<ExpenseEntry> expenses)
    {
        var phTz = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phTz).Date;
        double max = 0;
        var items = new List<MonthlyTrendModel>();

        for (int i = 5; i >= 0; i--)
        {
            var m = now.AddMonths(-i);
            var mi = incomes.Where(x => x.Date.Year == m.Year && x.Date.Month == m.Month).Sum(x => x.Amount);
            var me = expenses.Where(x => x.Date.Year == m.Year && x.Date.Month == m.Month).Sum(x => x.Amount);
            max = Math.Max(max, Math.Max(mi, me));
            items.Add(new MonthlyTrendModel { MonthLabel = m.ToString("MMM"), Income = mi, Expenses = me });
        }

        foreach (var e in items)
        {
            e.IncomeRatio = max > 0 ? e.Income / max : 0;
            e.ExpenseRatio = max > 0 ? e.Expenses / max : 0;
            e.IncomeDisplay = e.Income > 0 ? $"₱{e.Income:N0}" : "—";
            e.ExpenseDisplay = e.Expenses > 0 ? $"₱{e.Expenses:N0}" : "—";
        }

        return items;
    }

    // ── INSIGHT ───────────────────────────────────────────────────────────────
    private void BuildInsight()
    {
        if (TotalIncome == 0 && TotalExpenses == 0)
        {
            InsightText = string.Empty;
            OnPropertyChanged(nameof(InsightText));
            OnPropertyChanged(nameof(HasInsight));
            return;
        }

        var overBudgetCats = BudgetItems.Where(b => b.IsOverBudget).ToList();

        if (IsOverBudget)
            InsightText = $"⚠️ Total spending exceeded your budget by ₱{BudgetOverspent:N2}.";
        else if (overBudgetCats.Any())
            InsightText = $"⚠️ {overBudgetCats.First().CategoryName} is over budget. Consider cutting back.";
        else if (NetSavings < 0)
            InsightText = $"💸 You overspent by ₱{Math.Abs(NetSavings):N2}. Review your spending.";
        else if (SavingsRate >= 30)
            InsightText = $"🎉 Excellent! You saved {SavingsRate:F1}% of your income this period.";
        else if (SavingsRate >= 10)
            InsightText = $"👍 You saved {SavingsRate:F1}%. Aim for 20%+ for a stronger safety net.";
        else
            InsightText = $"💡 {SavingsRate:F1}% saved. Setting category budgets can help control spending.";

        OnPropertyChanged(nameof(InsightText));
        OnPropertyChanged(nameof(HasInsight));
    }

    // ── PERIOD RANGE ──────────────────────────────────────────────────────────
    private (DateTime, DateTime) GetPeriodRange()
    {
        var now = PhToday;
        return _selectedPeriod switch
        {
            "This Month" => (new DateTime(now.Year, now.Month, 1), now),
            "Last Month" => (new DateTime(now.Year, now.Month, 1).AddMonths(-1),
                              new DateTime(now.Year, now.Month, 1).AddDays(-1)),
            "This Year" => (new DateTime(now.Year, 1, 1), now),
            _ => (DateTime.MinValue, DateTime.MaxValue),
        };
    }

    // ── PARSERS ───────────────────────────────────────────────────────────────
    private record IncomeEntry { public double Amount; public DateTime Date; }
    private record ExpenseEntry
    {
        public double Amount; public string CategoryId = ""; public string CategoryName = "";
        public string CategoryIcon = "📦"; public string CategoryColor = "#7C6FFF";
        public DateTime Date;
    }
    private record FixedEntry
    {
        public double Amount; public bool IsActive; public string LastPaidMonth = "";

        private static readonly TimeZoneInfo PhTz3 =
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");
        private static DateTime PhToday3 => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhTz3).Date;
        private static string CurrentMonthKey => PhToday3.ToString("yyyy-MM");

        public bool IsPaidThisMonth => LastPaidMonth == CurrentMonthKey;
    }

    private static List<IncomeEntry> ParseIncomes(List<Dictionary<string, object>> docs) =>
        docs.Select(d =>
        {
            try { return new IncomeEntry { Amount = Dbl(d, "amount"), Date = DblDate(d, "date") }; }
            catch { return null!; }
        })
        .Where(x => x != null).ToList();

    private static List<ExpenseEntry> ParseExpenses(List<Dictionary<string, object>> docs) =>
        docs.Select(d =>
        {
            try
            {
                return new ExpenseEntry
                {
                    Amount = Dbl(d, "amount"),
                    CategoryId = Str(d, "categoryId"),
                    CategoryName = Str(d, "categoryName"),
                    CategoryIcon = Str(d, "categoryIcon").OrD("📦"),
                    CategoryColor = Str(d, "categoryColor").OrD("#7C6FFF"),
                    Date = DblDate(d, "date"),
                };
            }
            catch { return null!; }
        })
        .Where(x => x != null).ToList();

    private static List<FixedEntry> ParseFixed(List<Dictionary<string, object>> docs) =>
        docs.Select(d =>
        {
            try
            {
                return new FixedEntry
                {
                    Amount = Dbl(d, "amount"),
                    IsActive = d.TryGetValue("isActive", out var ia) && ia is bool b ? b : true,
                    LastPaidMonth = Str(d, "lastPaidMonth"),
                };
            }
            catch { return null!; }
        })
        .Where(x => x != null).ToList();

    // ── HELPERS ───────────────────────────────────────────────────────────────
    private static string Str(Dictionary<string, object> d, string k) =>
        d.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";

    private static double Dbl(Dictionary<string, object> d, string k)
    {
        if (!d.TryGetValue(k, out var v) || v is null) return 0;
        return v switch
        {
            double dbl => dbl,
            long l => (double)l,
            int i => (double)i,
            float f => (double)f,
            _ => double.TryParse(v.ToString(),
                     System.Globalization.NumberStyles.Any,
                     System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 0,
        };
    }

    private static DateTime DblDate(Dictionary<string, object> d, string k) =>
        d.TryGetValue(k, out var v) && DateTime.TryParse(v?.ToString(), out var dt) ? dt : DateTime.Today;

    // ── NOTIFY ────────────────────────────────────────────────────────────────
    private void NotifyAll()
    {
        foreach (var p in new[]
        {
            nameof(TotalIncome),      nameof(TotalExpenses),    nameof(TotalFixed),      nameof(TotalBudget),
            nameof(NetSavings),       nameof(SavingsRate),      nameof(BudgetUsed),      nameof(BudgetRemaining),
            nameof(BudgetOverspent),  nameof(IsOverBudget),     nameof(BudgetRatio),
            nameof(TotalIncomeDisplay),     nameof(TotalExpensesDisplay), nameof(TotalFixedDisplay),
            nameof(NetSavingsDisplay),      nameof(SavingsRateDisplay),   nameof(NetSavingsColor),
            nameof(NetSavingsLabel),        nameof(BudgetRemainingDisplay),nameof(BudgetOverspentDisplay),
            nameof(BudgetStatusColor),      nameof(BudgetStatusLabel),
            nameof(SpentRatio),       nameof(SpentBarColor),    nameof(SpentRatioDisplay), nameof(HasBudgets),
        }) OnPropertyChanged(p);
    }

    private void UpdatePeriodChips()
    {
        OnPropertyChanged(nameof(IsThisMonth)); OnPropertyChanged(nameof(IsLastMonth));
        OnPropertyChanged(nameof(IsThisYear)); OnPropertyChanged(nameof(IsAllTime));
    }
}

file static class Ext2
{
    public static string OrD(this string s, string def) => string.IsNullOrEmpty(s) ? def : s;
}