using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using allyza.Models;
using allyza.Services;

namespace allyza.ViewModels;

public class FixedExpenseViewModel : BaseViewModel
{
    private readonly IFirestoreService _db;
    private readonly IFirebaseAuthService _auth;

    private string _name = string.Empty;
    private string _amountText = string.Empty;
    private string _frequency = "Monthly";
    private int _dueDay = 1;
    private string _notes = string.Empty;
    private string _errorMsg = string.Empty;
    private string _successMsg = string.Empty;
    private CategoryModel? _selectedCategory;

    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public string AmountText { get => _amountText; set { _amountText = value; OnPropertyChanged(); } }
    public string Frequency { get => _frequency; set { _frequency = value; OnPropertyChanged(); } }
    public string Notes { get => _notes; set { _notes = value; OnPropertyChanged(); } }
    public int DueDay { get => _dueDay; set { _dueDay = Math.Clamp(value, 1, 31); OnPropertyChanged(); } }

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

    public CategoryModel? SelectedCategory
    {
        get => _selectedCategory;
        set { _selectedCategory = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedCategory)); }
    }

    public bool HasSelectedCategory => SelectedCategory != null;
    public bool HasNoFixedExpenses => FixedExpenses.Count == 0;

    public double TotalMonthly => FixedExpenses.Where(f => f.IsActive).Sum(f => f.MonthlyAmount);
    public string TotalMonthlyDisplay => $"₱{TotalMonthly:N2}";

    public ObservableCollection<FixedExpenseModel> FixedExpenses { get; } = new();
    public ObservableCollection<CategoryModel> Categories { get; } = new();

    public ICommand SaveCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ToggleActiveCommand { get; }
    public ICommand MarkPaidToggleCommand { get; }
    public ICommand SelectCategoryCommand { get; }
    public ICommand IncreaseDayCommand { get; }
    public ICommand DecreaseDayCommand { get; }

    public FixedExpenseViewModel(IFirestoreService db, IFirebaseAuthService auth)
    {
        _db = db; _auth = auth; Title = "Fixed Expenses";

        FixedExpenses.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasNoFixedExpenses));

        SaveCommand = new Command(async () => await SaveAsync());
        ClearCommand = new Command(() => MainThread.BeginInvokeOnMainThread(ClearForm));
        DeleteCommand = new Command<FixedExpenseModel>(async f => await DeleteAsync(f));
        ToggleActiveCommand = new Command<FixedExpenseModel>(async f => await ToggleActiveAsync(f));
        MarkPaidToggleCommand = new Command<FixedExpenseModel>(async f => await MarkPaidToggleAsync(f));
        SelectCategoryCommand = new Command<CategoryModel>(c =>
        {
            foreach (var cat in Categories) cat.IsSelected = false;
            c.IsSelected = true;
            SelectedCategory = c;
        });
        IncreaseDayCommand = new Command(() => DueDay++);
        DecreaseDayCommand = new Command(() => DueDay--);
    }

    public async Task LoadAsync()
    {
        var user = _auth.CurrentUser;
        if (user is null) return;

        await MainThread.InvokeOnMainThreadAsync(() => IsBusy = true);
        try
        {
            await LoadCategoriesAsync(user);

            var docs = await _db.GetCollectionAsync($"users/{user.Uid}/fixedExpenses", user.IdToken);
            var items = new List<FixedExpenseModel>();
            foreach (var doc in docs)
            {
                try { items.Add(ParseDoc(doc)); }
                catch { }
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                FixedExpenses.Clear();
                foreach (var f in items.OrderBy(SortKey)) FixedExpenses.Add(f);
                NotifyTotals();
            });
        }
        finally { await MainThread.InvokeOnMainThreadAsync(() => IsBusy = false); }
    }

    private async Task MarkPaidToggleAsync(FixedExpenseModel item)
    {
        var user = _auth.CurrentUser;
        if (user is null) return;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (item.IsPaidThisMonth)
            {
                item.LastPaidMonth = string.Empty;
                item.PaidDate = null;
            }
            else
            {
                item.LastPaidMonth = FixedExpenseModel.CurrentMonthKey;
                item.PaidDate = DateTime.Today;
            }

            int oldIdx = FixedExpenses.IndexOf(item);
            if (oldIdx >= 0)
            {
                FixedExpenses.RemoveAt(oldIdx);
                int newIdx = 0;
                for (int i = 0; i < FixedExpenses.Count; i++)
                {
                    if (SortKey(FixedExpenses[i]) <= SortKey(item)) newIdx = i + 1;
                    else break;
                }
                FixedExpenses.Insert(newIdx, item);
            }
            NotifyTotals();
        });

        await PersistFixedExpenseAsync(user, item);
    }

    private async Task SaveAsync()
    {
        if (IsBusy) return;
        await MainThread.InvokeOnMainThreadAsync(() => { ErrorMessage = string.Empty; SuccessMessage = string.Empty; });

        if (string.IsNullOrWhiteSpace(Name))
        { await MainThread.InvokeOnMainThreadAsync(() => ErrorMessage = "Please enter a name."); return; }

        var raw = AmountText.Replace(",", "").Replace("₱", "").Replace("$", "").Trim();
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double amount) || amount <= 0)
        { await MainThread.InvokeOnMainThreadAsync(() => ErrorMessage = "Please enter a valid amount."); return; }

        if (SelectedCategory is null)
        { await MainThread.InvokeOnMainThreadAsync(() => ErrorMessage = "Please select a category."); return; }

        var user = _auth.CurrentUser;
        if (user is null)
        { await MainThread.InvokeOnMainThreadAsync(() => ErrorMessage = "Not logged in."); return; }

        await MainThread.InvokeOnMainThreadAsync(() => IsBusy = true);
        try
        {
            var fx = new FixedExpenseModel
            {
                Id = Guid.NewGuid().ToString(),
                Name = Name.Trim(),
                Amount = amount,
                Frequency = "Monthly",
                DueDay = DueDay,
                CategoryId = SelectedCategory.Id,
                CategoryName = SelectedCategory.Name,
                CategoryIcon = SelectedCategory.Icon,
                CategoryColor = SelectedCategory.Color,
                IsActive = true,
                Notes = Notes.Trim(),
            };

            bool ok = await PersistFixedExpenseAsync(user, fx);

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (ok)
                {
                    int insertAt = 0;
                    for (int i = 0; i < FixedExpenses.Count; i++)
                    {
                        if (SortKey(FixedExpenses[i]) <= SortKey(fx)) insertAt = i + 1;
                        else break;
                    }
                    FixedExpenses.Insert(insertAt, fx);
                    NotifyTotals();
                    ClearForm();
                    SuccessMessage = "✓ Fixed expense saved!";
                    await Task.Delay(2500);
                    SuccessMessage = string.Empty;
                }
                else { ErrorMessage = "Failed to save. Please try again."; }
            });
        }
        catch (Exception ex)
        { await MainThread.InvokeOnMainThreadAsync(() => ErrorMessage = $"Error: {ex.Message}"); }
        finally { await MainThread.InvokeOnMainThreadAsync(() => IsBusy = false); }
    }

    private async Task ToggleActiveAsync(FixedExpenseModel item)
    {
        var user = _auth.CurrentUser;
        if (user is null) return;
        await MainThread.InvokeOnMainThreadAsync(() => { item.IsActive = !item.IsActive; NotifyTotals(); });
        await PersistFixedExpenseAsync(user, item);
    }

    private async Task DeleteAsync(FixedExpenseModel item)
    {
        bool ok = await MainThread.InvokeOnMainThreadAsync(() =>
            Application.Current!.MainPage!.DisplayAlert(
                "Delete", $"Remove \"{item.Name}\"?", "Delete", "Cancel"));
        if (!ok) return;

        var user = _auth.CurrentUser;
        if (user is not null)
            await _db.DeleteDocumentAsync($"users/{user.Uid}/fixedExpenses/{item.Id}", user.IdToken);

        await MainThread.InvokeOnMainThreadAsync(() => { FixedExpenses.Remove(item); NotifyTotals(); });
    }

    private async Task<bool> PersistFixedExpenseAsync(Models.UserModel user, FixedExpenseModel item)
    {
        try
        {
            return await _db.SetDocumentAsync(
                $"users/{user.Uid}/fixedExpenses/{item.Id}",
                user.IdToken,
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
                    ["notes"] = item.Notes ?? string.Empty,
                    ["lastPaidMonth"] = item.LastPaidMonth ?? string.Empty,
                    ["paidDate"] = item.PaidDate.HasValue
                                        ? item.PaidDate.Value.ToString("o")
                                        : string.Empty,
                });
        }
        catch { return false; }
    }

    private static FixedExpenseModel ParseDoc(Dictionary<string, object> doc)
    {
        DateTime? paidDate = null;
        if (doc.TryGetValue("paidDate", out var pd) && pd?.ToString() is string pds
            && !string.IsNullOrEmpty(pds)
            && DateTime.TryParse(pds, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsedPd))
        {
            paidDate = parsedPd.ToLocalTime().Date;
        }

        return new FixedExpenseModel
        {
            Id = doc.TryGetValue("id", out var id) ? id?.ToString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString(),
            Name = doc.TryGetValue("name", out var nm) ? nm?.ToString() ?? "" : "",
            Amount = doc.TryGetValue("amount", out var am) ? Convert.ToDouble(am) : 0,
            Frequency = "Monthly",
            DueDay = doc.TryGetValue("dueDay", out var dd) ? Convert.ToInt32(dd) : 1,
            CategoryId = doc.TryGetValue("categoryId", out var ci) ? ci?.ToString() ?? "" : "",
            CategoryName = doc.TryGetValue("categoryName", out var cn) ? cn?.ToString() ?? "" : "",
            CategoryIcon = doc.TryGetValue("categoryIcon", out var cic) ? cic?.ToString() ?? "🏷️" : "🏷️",
            CategoryColor = doc.TryGetValue("categoryColor", out var cc) ? cc?.ToString() ?? "#C9A8E8" : "#C9A8E8",
            IsActive = doc.TryGetValue("isActive", out var act) && act is bool b ? b : true,
            Notes = doc.TryGetValue("notes", out var nt) ? nt?.ToString() ?? "" : "",
            LastPaidMonth = doc.TryGetValue("lastPaidMonth", out var lp) ? lp?.ToString() ?? "" : "",
            PaidDate = paidDate,
        };
    }

    private static int SortKey(FixedExpenseModel f)
    {
        if (!f.IsActive) return 5;
        if (f.IsPaidThisMonth) return 4;
        if (f.IsOverdue) return 1;
        if (f.IsUpcomingSoon) return 2;
        return 3;
    }

    private void NotifyTotals()
    {
        OnPropertyChanged(nameof(TotalMonthly));
        OnPropertyChanged(nameof(TotalMonthlyDisplay));
        OnPropertyChanged(nameof(HasNoFixedExpenses));
    }

    private void ClearForm()
    {
        Name = string.Empty; AmountText = string.Empty;
        Notes = string.Empty; DueDay = 1; Frequency = "Monthly";
        ErrorMessage = string.Empty;
        if (Categories.Count > 0)
        {
            foreach (var c in Categories) c.IsSelected = false;
            Categories[0].IsSelected = true;
            SelectedCategory = Categories[0];
        }
    }

    private async Task LoadCategoriesAsync(Models.UserModel user)
    {
        var hiddenDocs = await _db.GetCollectionAsync($"users/{user.Uid}/hidden_defaults", user.IdToken);
        var hiddenIds = hiddenDocs
            .Select(d => d.TryGetValue("id", out var id) ? id?.ToString() ?? "" : "")
            .ToHashSet();

        // ── Pastel default colors ─────────────────────────────────────────────
        var defaults = new List<CategoryModel>
        {
           new() { Id = "food",          Name = "Food",          Icon = "🍔", Color = "#F5D090", IsCustom = false },
    new() { Id = "transport",     Name = "Transport",     Icon = "🚗", Color = "#90C8E0", IsCustom = false },
    new() { Id = "housing",       Name = "Housing",       Icon = "🏠", Color = "#C9A8E8", IsCustom = false },
    new() { Id = "health",        Name = "Health",        Icon = "💊", Color = "#90D8B0", IsCustom = false },
    new() { Id = "entertainment", Name = "Entertainment", Icon = "🎮", Color = "#F5A8C8", IsCustom = false },
    new() { Id = "shopping",      Name = "Shopping",      Icon = "🛍️", Color = "#F0A8A8", IsCustom = false },
    new() { Id = "education",     Name = "Education",     Icon = "📚", Color = "#C9A8E8", IsCustom = false },
    new() { Id = "utilities",     Name = "Utilities",     Icon = "💡", Color = "#F5D090", IsCustom = false },
    new() { Id = "savings",       Name = "Savings",       Icon = "🏦", Color = "#90D8B0", IsCustom = false },
    new() { Id = "other",         Name = "Other",         Icon = "📦", Color = "#B8DEFA", IsCustom = false },
};

        var customDocs = await _db.GetCollectionAsync($"users/{user.Uid}/categories", user.IdToken);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Categories.Clear();
            foreach (var d in defaults)
                if (!hiddenIds.Contains(d.Id)) Categories.Add(d);

            foreach (var doc in customDocs)
                Categories.Add(new CategoryModel
                {
                    Id = doc.TryGetValue("id", out var id) ? id?.ToString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString(),
                    Name = doc.TryGetValue("name", out var name) ? name?.ToString() ?? "" : "",
                    Icon = doc.TryGetValue("icon", out var icon) ? icon?.ToString() ?? "🏷️" : "🏷️",
                    Color = doc.TryGetValue("color", out var color) ? color?.ToString() ?? "#C9A8E8" : "#C9A8E8",
                    IsCustom = true
                });

            if (SelectedCategory is null && Categories.Count > 0)
            {
                Categories[0].IsSelected = true;
                SelectedCategory = Categories[0];
            }
        });
    }
}