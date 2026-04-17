using System.Collections.ObjectModel;
using System.Windows.Input;
using allyza.Models;
using allyza.Services;

namespace allyza.ViewModels;

public class TransactionHistoryViewModel : BaseViewModel
{
    private readonly IFirestoreService _db;
    private readonly IFirebaseAuthService _auth;

    // ── PH Time ───────────────────────────────────────────────────────────────
    private static readonly TimeZoneInfo PhTz =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");

    private static DateTime PhToday => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PhTz).Date;

    // ── Filter state ──────────────────────────────────────────────────────────
    private string _activeFilter = "All";
    private string _searchText = string.Empty;

    // ── Date range filter ─────────────────────────────────────────────────────
    private DateTime _dateFrom = DateTime.MinValue;
    private DateTime _dateTo = DateTime.MaxValue;
    private bool _dateFilterActive = false;

    public DateTime DateFrom
    {
        get => _dateFrom == DateTime.MinValue ? PhToday.AddMonths(-1) : _dateFrom;
        set { _dateFrom = value; OnPropertyChanged(); UpdateFilters(); }
    }

    public DateTime DateTo
    {
        get => _dateTo == DateTime.MaxValue ? PhToday : _dateTo;
        set { _dateTo = value; OnPropertyChanged(); UpdateFilters(); }
    }

    public bool DateFilterActive
    {
        get => _dateFilterActive;
        set { _dateFilterActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(DateFilterLabel)); UpdateFilters(); }
    }

    public string DateFilterLabel => _dateFilterActive
        ? $"{DateFrom:MMM d} – {DateTo:MMM d, yyyy}"
        : "Filter by Date";

    // ── Type filter ───────────────────────────────────────────────────────────
    public string ActiveFilter
    {
        get => _activeFilter;
        set { _activeFilter = value; OnPropertyChanged(); UpdateFilters(); }
    }

    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); UpdateFilters(); }
    }

    public bool IsAll => _activeFilter == "All";
    public bool IsIncomeFilter => _activeFilter == "Income";
    public bool IsExpenseFilter => _activeFilter == "Expense";
    public bool IsFixedFilter => _activeFilter == "Fixed";
    public bool HasNoResults => FilteredTransactions.Count == 0 && !IsBusy;

    // ── Totals (filtered) ─────────────────────────────────────────────────────
    private double _filteredIncome;
    private double _filteredExpenses;

    public string TotalIncomeDisplay => $"₱{_filteredIncome:N2}";
    public string TotalExpensesDisplay => $"₱{_filteredExpenses:N2}";
    public double NetBalance => _filteredIncome - _filteredExpenses;
    public string NetBalanceDisplay => $"₱{Math.Abs(NetBalance):N2}";
    public string NetBalanceColor => NetBalance >= 0 ? "#90D8B0" : "#F0A8A8";
    public string NetBalancePrefix => NetBalance >= 0 ? "+" : "-";

    private readonly List<TransactionModel> _allTransactions = new();
    public ObservableCollection<TransactionModel> FilteredTransactions { get; } = new();

    // ── Commands ──────────────────────────────────────────────────────────────
    public ICommand LoadCommand { get; }
    public ICommand SetFilterCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand ToggleDateFilterCommand { get; }
    public ICommand ClearDateFilterCommand { get; }

    public TransactionHistoryViewModel(IFirestoreService db, IFirebaseAuthService auth)
    {
        _db = db; _auth = auth; Title = "History";
        LoadCommand = new Command(async () => await LoadAsync());
        SetFilterCommand = new Command<string>(f => ActiveFilter = f);
        DeleteCommand = new Command<TransactionModel>(async t => await DeleteAsync(t));
        ClearSearchCommand = new Command(() => SearchText = string.Empty);
        ToggleDateFilterCommand = new Command(() => DateFilterActive = !DateFilterActive);
        ClearDateFilterCommand = new Command(() =>
        {
            _dateFrom = DateTime.MinValue;
            _dateTo = DateTime.MaxValue;
            DateFilterActive = false;
            OnPropertyChanged(nameof(DateFrom));
            OnPropertyChanged(nameof(DateTo));
        });
        FilteredTransactions.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasNoResults));
    }

    // ── Load ──────────────────────────────────────────────────────────────────
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
            await Task.WhenAll(incomeTask, expenseTask, fixedTask);

            var all = new List<TransactionModel>();

            foreach (var doc in incomeTask.Result)
            {
                try
                {
                    all.Add(new TransactionModel
                    {
                        Id = Str(doc, "id"),
                        Type = "income",
                        Title = Str(doc, "source").OrD("Income"),
                        Amount = Dbl(doc, "amount"),
                        CategoryName = Str(doc, "frequency"),
                        CategoryIcon = "💰",
                        CategoryColor = "#4CAF82",
                        Notes = Str(doc, "notes"),
                        Date = Dt(doc, "date"),
                    });
                }
                catch { }
            }

            foreach (var doc in expenseTask.Result)
            {
                try
                {
                    all.Add(new TransactionModel
                    {
                        Id = Str(doc, "id"),
                        Type = "expense",
                        Title = Str(doc, "description").OrD("Expense"),
                        Amount = Dbl(doc, "amount"),
                        CategoryName = Str(doc, "categoryName"),
                        CategoryIcon = Str(doc, "categoryIcon").OrD("📦"),
                        CategoryColor = Str(doc, "categoryColor").OrD("#7C6FFF"),
                        Notes = Str(doc, "notes"),
                        Date = Dt(doc, "date"),
                    });
                }
                catch { }
            }

            var today = PhToday;
            var currentMonthKey = today.ToString("yyyy-MM");

            foreach (var doc in fixedTask.Result)
            {
                try
                {
                    var isActive = doc.TryGetValue("isActive", out var ia) && ia is bool b && b;
                    if (!isActive) continue;
                    var lastPaidMonth = Str(doc, "lastPaidMonth");
                    if (lastPaidMonth != currentMonthKey) continue;

                    var dueDay = (int)Dbl(doc, "dueDay");
                    var safeDay = Math.Min(dueDay, DateTime.DaysInMonth(today.Year, today.Month));

                    all.Add(new TransactionModel
                    {
                        Id = Str(doc, "id"),
                        Type = "fixed",
                        Title = Str(doc, "name").OrD("Fixed Expense"),
                        Amount = Dbl(doc, "amount"),
                        CategoryName = Str(doc, "categoryName").OrD("Fixed"),
                        CategoryIcon = Str(doc, "categoryIcon").OrD("📌"),
                        CategoryColor = Str(doc, "categoryColor").OrD("#D4A020"),
                        Notes = "✅ Paid this month",
                        Date = new DateTime(today.Year, today.Month, safeDay),
                    });
                }
                catch { }
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _allTransactions.Clear();
                _allTransactions.AddRange(all);
                UpdateFilters();
            });
        }
        finally { MainThread.BeginInvokeOnMainThread(() => IsBusy = false); }
    }

    // ── Filter logic ──────────────────────────────────────────────────────────
    private void UpdateFilters()
    {
        var filtered = _allTransactions.AsEnumerable();

        // Type filter
        filtered = _activeFilter switch
        {
            "Income" => filtered.Where(t => t.Type == "income"),
            "Expense" => filtered.Where(t => t.Type == "expense"),
            "Fixed" => filtered.Where(t => t.Type == "fixed"),
            _ => filtered
        };

        // Text search — title, category, notes, amount, date string
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var q = _searchText.Trim().ToLower();
            filtered = filtered.Where(t =>
                t.Title.ToLower().Contains(q)
                || t.CategoryName.ToLower().Contains(q)
                || (t.Notes ?? "").ToLower().Contains(q)
                || t.Amount.ToString("N2").Contains(q)
                || t.Date.ToString("MMMM d yyyy").ToLower().Contains(q)
                || t.Date.ToString("MMM d yyyy").ToLower().Contains(q)
                || t.Date.ToString("MM/dd/yyyy").Contains(q)
                || t.Date.ToString("yyyy-MM-dd").Contains(q));
        }

        // Date range filter
        if (_dateFilterActive)
        {
            var from = _dateFrom == DateTime.MinValue ? DateTime.MinValue : _dateFrom.Date;
            var to = _dateTo == DateTime.MaxValue ? DateTime.MaxValue : _dateTo.Date;
            filtered = filtered.Where(t => t.Date.Date >= from && t.Date.Date <= to);
        }

        var sorted = filtered.OrderByDescending(t => t.Date).ToList();

        FilteredTransactions.Clear();
        foreach (var t in sorted) FilteredTransactions.Add(t);

        // Recalculate totals from FILTERED set
        _filteredIncome = sorted.Where(t => t.Type == "income").Sum(t => t.Amount);
        _filteredExpenses = sorted.Where(t => t.Type != "income").Sum(t => t.Amount);

        foreach (var p in new[]
        {
            nameof(IsAll), nameof(IsIncomeFilter), nameof(IsExpenseFilter), nameof(IsFixedFilter),
            nameof(HasNoResults), nameof(TotalIncomeDisplay), nameof(TotalExpensesDisplay),
            nameof(NetBalance), nameof(NetBalanceDisplay), nameof(NetBalanceColor),
            nameof(NetBalancePrefix), nameof(DateFilterLabel)
        }) OnPropertyChanged(p);
    }

    // ── Delete ────────────────────────────────────────────────────────────────
    private async Task DeleteAsync(TransactionModel item)
    {
        if (item.Type == "fixed")
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                Application.Current!.MainPage!.DisplayAlert(
                    "Cannot Delete", "Fixed expenses must be managed from the Fixed Expenses page.", "OK"));
            return;
        }

        bool ok = await MainThread.InvokeOnMainThreadAsync(() =>
            Application.Current!.MainPage!.DisplayAlert(
                "Delete", $"Remove \"{item.Title}\"?", "Delete", "Cancel"));
        if (!ok) return;

        var user = _auth.CurrentUser;
        if (user is not null)
        {
            var col = item.Type == "income" ? "incomes" : "expenses";
            await _db.DeleteDocumentAsync($"users/{user.Uid}/{col}/{item.Id}", user.IdToken);
        }

        _allTransactions.Remove(item);
        await MainThread.InvokeOnMainThreadAsync(UpdateFilters);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string Str(Dictionary<string, object> d, string k) =>
        d.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";
    private static double Dbl(Dictionary<string, object> d, string k) =>
        d.TryGetValue(k, out var v) ? Convert.ToDouble(v) : 0;
    private static DateTime Dt(Dictionary<string, object> d, string k) =>
        d.TryGetValue(k, out var v) && DateTime.TryParse(v?.ToString(), out var dt) ? dt : DateTime.MinValue;
}

public class TransactionGroupModel : ObservableCollection<TransactionModel>
{
    public string GroupTitle { get; set; } = string.Empty;
}

file static class TxnHistExt
{
    public static string OrD(this string s, string def) => string.IsNullOrEmpty(s) ? def : s;
}