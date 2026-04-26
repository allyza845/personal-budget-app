using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using allyza.Models;
using allyza.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

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

    public bool IsSingleMonthPeriod =>
        _selectedPeriod == "This Month" || _selectedPeriod == "Last Month";

    // ── Raw totals ────────────────────────────────────────────────────────────
    public double TotalIncome { get; private set; }
    public double TotalExpenses { get; private set; }

    /// <summary>
    /// Single-month: SUM of ALL active fixed (monthly committed).
    /// Multi-month: SUM of fixed amounts PAID within the period.
    /// </summary>
    public double TotalFixed { get; private set; }

    /// <summary>
    /// Sum of fixed amounts actually paid within the selected period.
    /// </summary>
    public double TotalFixedPaid { get; private set; }

    public double TotalBudget { get; private set; }

    // Presence flags to avoid showing ₱0.00 when nothing exists in period
    public bool HasIncome { get; private set; }
    public bool HasExpenses { get; private set; }
    public bool HasFixed { get; private set; }

    // ── Derived calculations ──────────────────────────────────────────────────
    public double NetSavings => TotalIncome - TotalExpenses - TotalFixed;

    public double SavingsRate =>
        TotalIncome > 0
            ? Math.Max(-100, Math.Min(NetSavings / TotalIncome * 100, 100))
            : 0;

    public double BudgetUsed => TotalExpenses;
    public double BudgetRemaining => TotalBudget > 0 ? Math.Max(TotalBudget - TotalExpenses, 0) : 0;
    public double BudgetOverspent => TotalBudget > 0 ? Math.Max(TotalExpenses - TotalBudget, 0) : 0;
    public bool IsOverBudget => TotalBudget > 0 && TotalExpenses > TotalBudget;

    public bool HasTotalBudget => TotalBudget > 0;
    public double BudgetRawRatio => TotalBudget > 0 ? TotalExpenses / TotalBudget : 0;
    public double BudgetRatio => Math.Min(BudgetRawRatio, 1.0);

    public double SpentRatio =>
        TotalIncome > 0
            ? Math.Min((TotalExpenses + TotalFixed) / TotalIncome, 1.0)
            : 0;

    // ── Display strings ───────────────────────────────────────────────────────
    public string TotalIncomeDisplay => HasIncome ? $"₱{TotalIncome:N2}" : "—";
    public string TotalExpensesDisplay => HasExpenses ? $"₱{TotalExpenses:N2}" : "—";
    public string TotalFixedDisplay => HasFixed ? $"₱{TotalFixed:N2}" : "—";
    public string TotalFixedPaidDisplay => HasFixed ? $"₱{TotalFixedPaid:N2} paid" : "—";

    // ── SAVINGS DISPLAY — only show when income > 0 was recorded in the period ──
    private bool HasActualIncome => HasIncome && TotalIncome > 0;

    public string NetSavingsDisplay =>
        HasActualIncome ? $"₱{Math.Abs(NetSavings):N2}" : "—";

    public string NetSavingsLabel =>
        HasActualIncome ? (NetSavings >= 0 ? "Saved" : "Overspent") : "—";

    public string NetSavingsColor =>
        HasActualIncome ? (NetSavings >= 0 ? "#166534" : "#EF4444") : "#6B7280";

    public string SavingsRateDisplay =>
        (HasActualIncome && (HasExpenses || HasFixed)) ? $"{Math.Abs(SavingsRate):F1}%" : "—";

    public string BudgetRemainingDisplay => HasTotalBudget ? $"₱{BudgetRemaining:N2}" : "—";
    public string BudgetOverspentDisplay => HasTotalBudget ? $"₱{BudgetOverspent:N2}" : "—";

    public string BudgetStatusColor =>
        BudgetRawRatio >= 1.0 ? "#EF4444"
      : BudgetRawRatio >= 0.8 ? "#F59E0B"
      : "#166534";

    public string BudgetStatusLabel =>
        HasTotalBudget
            ? (IsOverBudget ? $"₱{BudgetOverspent:N2} over budget" : $"₱{BudgetRemaining:N2} remaining")
            : "—";

    public string BudgetRemainingLabel =>
        IsOverBudget ? "Overspent" : "Remaining";

    public string SpentBarColor =>
        SpentRatio >= 0.9 ? "#EF4444"
      : SpentRatio >= 0.7 ? "#F59E0B"
      : "#166534";

    public string SpentRatioDisplay => HasIncome ? $"{SpentRatio * 100:F1}% of income used" : "—";

    public string InsightText { get; private set; } = string.Empty;
    public bool HasInsight => !string.IsNullOrEmpty(InsightText);
    public bool HasBudgets => BudgetItems.Any(b => b.HasBudget);

    // ── Collections ───────────────────────────────────────────────────────────
    public ObservableCollection<BudgetModel> BudgetItems { get; } = new();
    public ObservableCollection<CategorySummary> TopCategories { get; } = new();
    public ObservableCollection<MonthlyTrendModel> MonthlyTrend { get; } = new();

    public bool HasCategories => TopCategories.Count > 0;
    public bool HasTrend => MonthlyTrend.Count > 0;

    // ── Commands ──────────────────────────────────────────────────────────────
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
            var allFixed = ParseFixed(fixedTask.Result);

            var (start, end) = GetPeriodRange();

            var incomes = allIncomes.Where(i => i.Date >= start && i.Date <= end).ToList();
            var expenses = allExpenses.Where(e => e.Date >= start && e.Date <= end).ToList();

            bool hasIncome = incomes.Any(i => i.Amount > 0);
            bool hasExpenses = expenses.Any();

            // ── Budget scaling: how many months does the period represent? ────
            int monthsCountForBudgetScaling;

            if (IsThisMonth || IsLastMonth)
            {
                // Always exactly 1 month
                monthsCountForBudgetScaling = 1;
            }
            else if (IsThisYear)
            {
                // Jan 1 → current month (inclusive), e.g. April = 4
                monthsCountForBudgetScaling = PhToday.Month;
            }
            else // All Time
            {
                // All Time scales by months elapsed since Jan 1 of the earliest transaction year.
                // If all activity is within the current year, this matches "This Year" exactly.
                var allDates = allIncomes.Select(i => i.Date)
                    .Concat(allExpenses.Select(e => e.Date))
                    .Where(d => d > DateTime.MinValue)
                    .Select(d => d.Kind == DateTimeKind.Utc
                        ? TimeZoneInfo.ConvertTimeFromUtc(d, PhTz)
                        : d)
                    .ToList();

                var phNow = PhToday;

                if (allDates.Count == 0)
                {
                    monthsCountForBudgetScaling = 1;
                }
                else
                {
                    int earliestYear = allDates.Min(d => d.Year);

                    if (earliestYear == phNow.Year)
                    {
                        // Same year as now → identical to "This Year"
                        monthsCountForBudgetScaling = phNow.Month;
                    }
                    else
                    {
                        // Spans multiple years → Jan of earliest year → current month
                        var earliestMonth = new DateTime(earliestYear, 1, 1);
                        var nowMonth = new DateTime(phNow.Year, phNow.Month, 1);
                        monthsCountForBudgetScaling =
                            (nowMonth.Year - earliestMonth.Year) * 12
                            + (nowMonth.Month - earliestMonth.Month) + 1;
                        monthsCountForBudgetScaling = Math.Max(1, monthsCountForBudgetScaling);
                    }
                }
            }

            double totalIncome = Math.Round(incomes.Where(i => i.Amount > 0).Sum(i => i.Amount), 2);
            double totalExpenses = Math.Round(expenses.Sum(e => e.Amount), 2);

            // ── Fixed expense logic per period ────────────────────────────────
            string periodMonthKey = new DateTime(start.Year, start.Month, 1).ToString("yyyy-MM");
            double totalFixed = 0;
            double totalFixedPaid = 0;

            if (IsSingleMonthPeriod)
            {
                totalFixed = Math.Round(allFixed.Where(f => f.IsActive).Sum(f => f.Amount), 2);
                totalFixedPaid = Math.Round(allFixed.Where(f => f.IsActive && f.LastPaidMonth == periodMonthKey).Sum(f => f.Amount), 2);
            }
            else
            {
                DateTime monthStart = new DateTime(start.Year, start.Month, 1);
                DateTime monthEnd = new DateTime(end.Year, end.Month, 1);
                double paidSum = 0;
                foreach (var f in allFixed.Where(f => f.IsActive))
                {
                    if (TryParseYearMonth(f.LastPaidMonth, out var paidMonth))
                        if (paidMonth >= monthStart && paidMonth <= monthEnd)
                            paidSum += f.Amount;
                }
                totalFixed = Math.Round(paidSum, 2);
                totalFixedPaid = totalFixed;
            }

            var budgetItems = BuildBudgetItemsList(budgetTask.Result, expenses, monthsCountForBudgetScaling);
            double totalBudget = Math.Round(budgetItems.Where(b => b.HasBudget).Sum(b => b.LimitAmount), 2);

            var categoryItems = BuildCategoryList(expenses);
            var trendItems = BuildTrendList(allIncomes, allExpenses);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                TotalIncome = totalIncome;
                TotalExpenses = totalExpenses;
                TotalFixed = totalFixed;
                TotalFixedPaid = totalFixedPaid;
                TotalBudget = totalBudget;

                HasIncome = hasIncome;
                HasExpenses = hasExpenses;
                HasFixed = allFixed.Any(f => f.IsActive) || totalFixedPaid > 0;

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
        if (!double.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out double amount) || amount < 0)
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
        List<ExpenseEntry> expenses,
        int monthsCount)
    {
        var limits = new Dictionary<string, (string name, string icon, string color, double monthlyLimit)>();
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

        var spentByCategoryId = expenses.GroupBy(e => e.CategoryId).ToDictionary(g => g.Key, g => g.ToList());
        var spentByCategoryName = expenses.GroupBy(e => e.CategoryName).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<BudgetModel>();

        foreach (var (catId, (name, icon, color, monthlyLimit)) in limits)
        {
            double spent = 0;
            if (!string.IsNullOrEmpty(catId) && spentByCategoryId.TryGetValue(catId, out var listById))
                spent = listById.Sum(e => e.Amount);
            else if (!string.IsNullOrEmpty(name) && spentByCategoryName.TryGetValue(name, out var listByName))
                spent = listByName.Sum(e => e.Amount);

            // Scale the monthly budget limit by the number of months in the period
            double scaledLimit = monthsCount > 0 ? Math.Round(monthlyLimit * monthsCount, 2) : 0;

            result.Add(new BudgetModel
            {
                Id = catId,
                CategoryId = catId,
                CategoryName = name,
                CategoryIcon = icon,
                CategoryColor = color,
                LimitAmount = scaledLimit,
                SpentAmount = Math.Round(spent, 2),
            });
        }

        // Add categories that have spending but no budget set
        foreach (var (catId, list) in spentByCategoryId)
        {
            if (string.IsNullOrEmpty(catId)) continue;
            if (result.Any(b => !string.IsNullOrEmpty(b.CategoryId) && b.CategoryId == catId)) continue;
            var first = list.First();
            result.Add(new BudgetModel
            {
                Id = first.CategoryId,
                CategoryId = first.CategoryId,
                CategoryName = first.CategoryName,
                CategoryIcon = first.CategoryIcon,
                CategoryColor = first.CategoryColor,
                LimitAmount = 0,
                SpentAmount = Math.Round(list.Sum(e => e.Amount), 2),
            });
        }

        foreach (var (catName, list) in spentByCategoryName)
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
                SpentAmount = Math.Round(list.Sum(e => e.Amount), 2),
            });
        }

        return result
            .OrderByDescending(b => b.IsOverBudget)
            .ThenByDescending(b => b.BudgetRawRatio)
            .ThenByDescending(b => b.SpentAmount)
            .ToList();
    }

    // ── BUILD CATEGORY LIST ───────────────────────────────────────────────────
    private static List<CategorySummary> BuildCategoryList(List<ExpenseEntry> expenses)
    {
        if (!expenses.Any()) return new();

        double total = expenses.Sum(e => e.Amount);
        return expenses
            .GroupBy(e => e.CategoryName)
            .Select(g =>
            {
                double amount = g.Sum(e => e.Amount);
                double percentage = total > 0 ? amount / total * 100 : 0;
                return new CategorySummary
                {
                    Name = g.Key,
                    Icon = g.First().CategoryIcon,
                    Color = g.First().CategoryColor,
                    Amount = amount,
                    Percentage = percentage,
                };
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
            double mi = incomes.Where(x => x.Date.Year == m.Year && x.Date.Month == m.Month).Sum(x => x.Amount);
            double me = expenses.Where(x => x.Date.Year == m.Year && x.Date.Month == m.Month).Sum(x => x.Amount);
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
        if (!HasIncome && !HasExpenses && !HasFixed)
        {
            InsightText = string.Empty;
            OnPropertyChanged(nameof(InsightText));
            OnPropertyChanged(nameof(HasInsight));
            return;
        }

        var overBudgetCats = BudgetItems.Where(b => b.IsOverBudget).ToList();

        if (IsOverBudget)
            InsightText = $"⚠️ Total spending exceeded your budget by {BudgetOverspent:N2}.";
        else if (overBudgetCats.Any())
            InsightText = $"⚠️ {overBudgetCats.First().CategoryName} is over budget. Consider cutting back.";
        else if (!HasIncome && HasExpenses)
            InsightText = "💡 No income recorded yet. Add your income to see accurate savings insights.";
        else if (NetSavings < 0 && HasActualIncome)
            InsightText = $"💸 You overspent by {Math.Abs(NetSavings):N2}. Review your spending.";
        else if (SavingsRate >= 30 && HasActualIncome)
            InsightText = $"🎉 Excellent! You saved {SavingsRate:F1}% of your income this period.";
        else if (SavingsRate >= 10 && HasActualIncome)
            InsightText = $"👍 You saved {SavingsRate:F1}%. Aim for 20%+ for a stronger safety net.";
        else
            InsightText = HasActualIncome
                ? $"💡 {SavingsRate:F1}% saved. Setting category budgets can help control spending."
                : string.Empty;

        OnPropertyChanged(nameof(InsightText));
        OnPropertyChanged(nameof(HasInsight));
    }

    // ── PERIOD RANGE ──────────────────────────────────────────────────────────
    private (DateTime start, DateTime end) GetPeriodRange()
    {
        var now = PhToday;
        return _selectedPeriod switch
        {
            "This Month" => (new DateTime(now.Year, now.Month, 1), now),
            "Last Month" => (new DateTime(now.Year, now.Month, 1).AddMonths(-1),
                               new DateTime(now.Year, now.Month, 1).AddDays(-1)),
            "This Year" => (new DateTime(now.Year, 1, 1), now),
            _ => (DateTime.MinValue, DateTime.MaxValue),   // All Time
        };
    }

    // ── PARSERS ───────────────────────────────────────────────────────────────
    private record IncomeEntry { public double Amount; public DateTime Date; }
    private record ExpenseEntry
    {
        public double Amount;
        public string CategoryId = "";
        public string CategoryName = "";
        public string CategoryIcon = "📦";
        public string CategoryColor = "#7C6FFF";
        public DateTime Date;
    }
    private record FixedEntry
    {
        public double Amount;
        public bool IsActive;
        public string LastPaidMonth = "";
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
                     NumberStyles.Any,
                     CultureInfo.InvariantCulture, out var r) ? r : 0,
        };
    }

    // treat missing/invalid dates as MinValue so they don't match the selected period
    private static DateTime DblDate(Dictionary<string, object> d, string k) =>
        d.TryGetValue(k, out var v) && DateTime.TryParse(v?.ToString(), out var dt) ? dt : DateTime.MinValue;

    private static bool TryParseYearMonth(string ym, out DateTime month)
    {
        month = default;
        if (string.IsNullOrEmpty(ym)) return false;
        return DateTime.TryParseExact(ym, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out month)
            ? (month = new DateTime(month.Year, month.Month, 1)) == month
            : false;
    }

    // ── NOTIFY ────────────────────────────────────────────────────────────────
    private void NotifyAll()
    {
        foreach (var p in new[]
        {
            nameof(TotalIncome),            nameof(TotalExpenses),         nameof(TotalFixed),
            nameof(TotalFixedPaid),          nameof(TotalBudget),           nameof(HasTotalBudget),
            nameof(NetSavings),              nameof(SavingsRate),           nameof(BudgetUsed),
            nameof(BudgetRemaining),         nameof(BudgetOverspent),       nameof(IsOverBudget),
            nameof(BudgetRawRatio),          nameof(BudgetRatio),
            nameof(TotalIncomeDisplay),      nameof(TotalExpensesDisplay),
            nameof(TotalFixedDisplay),       nameof(TotalFixedPaidDisplay),
            nameof(NetSavingsDisplay),       nameof(SavingsRateDisplay),
            nameof(NetSavingsColor),         nameof(NetSavingsLabel),
            nameof(BudgetRemainingDisplay),  nameof(BudgetOverspentDisplay),
            nameof(BudgetStatusColor),       nameof(BudgetStatusLabel),     nameof(BudgetRemainingLabel),
            nameof(SpentRatio),              nameof(SpentBarColor),         nameof(SpentRatioDisplay),
            nameof(HasBudgets),              nameof(IsSingleMonthPeriod),
            nameof(HasIncome),               nameof(HasExpenses),           nameof(HasFixed),
        }) OnPropertyChanged(p);
    }

    private void UpdatePeriodChips()
    {
        OnPropertyChanged(nameof(IsThisMonth)); OnPropertyChanged(nameof(IsLastMonth));
        OnPropertyChanged(nameof(IsThisYear)); OnPropertyChanged(nameof(IsAllTime));
        OnPropertyChanged(nameof(IsSingleMonthPeriod));
    }
}

file static class Ext2
{
    public static string OrD(this string s, string def) => string.IsNullOrEmpty(s) ? def : s;
}