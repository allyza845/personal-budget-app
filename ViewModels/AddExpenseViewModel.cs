using System.Collections.ObjectModel;
using System.Windows.Input;
using allyza.Models;
using allyza.Services;

namespace allyza.ViewModels;

public class AddExpenseViewModel : BaseViewModel
{
    private readonly IFirestoreService _db;
    private readonly IFirebaseAuthService _auth;

    private string _description = string.Empty;
    private string _amountText = string.Empty;
    private DateTime _date = DateTime.Today;
    private string _notes = string.Empty;
    private string _errorMsg = string.Empty;
    private string _successMsg = string.Empty;
    private CategoryModel? _selectedCategory;

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }
    public string AmountText
    {
        get => _amountText;
        set { _amountText = value; OnPropertyChanged(); }
    }
    public DateTime Date
    {
        get => _date;
        set { _date = value; OnPropertyChanged(); }
    }
    public string Notes
    {
        get => _notes;
        set { _notes = value; OnPropertyChanged(); }
    }
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
        set
        {
            _selectedCategory = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedCategory));
        }
    }

    public bool HasSelectedCategory => _selectedCategory is not null;
    public bool HasNoExpenses => Expenses.Count == 0;

    public ObservableCollection<ExpenseModel> Expenses { get; } = new();
    public ObservableCollection<CategoryModel> Categories { get; } = new();

    public ICommand SaveCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand DeleteExpenseCommand { get; }
    public ICommand SelectCategoryCommand { get; }

    public AddExpenseViewModel(IFirestoreService db, IFirebaseAuthService auth)
    {
        _db = db;
        _auth = auth;
        Title = "Expenses";

        SaveCommand = new Command(async () => await SaveAsync());
        ClearCommand = new Command(ClearForm);
        DeleteExpenseCommand = new Command<ExpenseModel>(async e => await DeleteAsync(e));

        SelectCategoryCommand = new Command<CategoryModel>(cat =>
        {
            foreach (var c in Categories) c.IsSelected = false;
            cat.IsSelected = true;
            SelectedCategory = cat;
        });
    }

    // ── LOAD ──────────────────────────────────────────────────────────────────
    public async Task LoadAsync()
    {
        var user = _auth.CurrentUser;
        if (user is null) return;

        IsBusy = true;
        try
        {
            await LoadCategoriesAsync(user);

            var docs = await _db.GetCollectionAsync($"users/{user.Uid}/expenses", user.IdToken);
            Expenses.Clear();
            foreach (var doc in docs)
            {
                try
                {
                    Expenses.Add(new ExpenseModel
                    {
                        Id = S(doc, "id"),
                        Description = S(doc, "description"),
                        Amount = Dbl(doc, "amount"),
                        CategoryId = S(doc, "categoryId"),
                        CategoryName = S(doc, "categoryName"),
                        CategoryIcon = S(doc, "categoryIcon").OrD("💸"),
                        CategoryColor = S(doc, "categoryColor").OrD("#EF4444"),
                        Notes = S(doc, "notes"),
                        Date = Dt(doc, "date"),
                    });
                }
                catch { }
            }

            var sorted = Expenses.OrderByDescending(e => e.Date).ToList();
            Expenses.Clear();
            foreach (var e in sorted) Expenses.Add(e);

            OnPropertyChanged(nameof(HasNoExpenses));
        }
        finally { IsBusy = false; }
    }

    private async Task LoadCategoriesAsync(UserModel user)
    {
        Categories.Clear();

        var hiddenDocs = await _db.GetCollectionAsync(
            $"users/{user.Uid}/hidden_defaults",
            user.IdToken);

        var hiddenIds = hiddenDocs
            .Select(d => S(d, "id"))
            .ToHashSet();

        foreach (var d in GetDefaultCategories())
        {
            if (!hiddenIds.Contains(d.Id))
                Categories.Add(d);
        }

        var docs = await _db.GetCollectionAsync(
            $"users/{user.Uid}/categories",
            user.IdToken);

        foreach (var doc in docs)
        {
            Categories.Add(new CategoryModel
            {
                Id = S(doc, "id"),
                Name = S(doc, "name"),
                Icon = S(doc, "icon").OrD("🏷️"),
                Color = S(doc, "color").OrD("#7C6FFF"),
                IsCustom = true
            });
        }

        if (SelectedCategory is null && Categories.Count > 0)
        {
            Categories[0].IsSelected = true;
            SelectedCategory = Categories[0];
        }
    }

    // ── SAVE ──────────────────────────────────────────────────────────────────
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        ErrorMessage = SuccessMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Description))
        { ErrorMessage = "Please enter a description."; return; }

        var raw = AmountText.Replace(",", "").Replace("₱", "").Trim();
        if (!double.TryParse(raw, out double amount) || amount <= 0)
        { ErrorMessage = "Please enter a valid amount."; return; }

        if (SelectedCategory is null)
        { ErrorMessage = "Please select a category."; return; }

        var user = _auth.CurrentUser;
        if (user is null) { ErrorMessage = "Not logged in."; return; }

        IsBusy = true;
        try
        {
            var expense = new ExpenseModel
            {
                Id = Guid.NewGuid().ToString(),
                Description = Description.Trim(),
                Amount = amount,
                CategoryId = SelectedCategory.Id,
                CategoryName = SelectedCategory.Name,
                CategoryIcon = SelectedCategory.Icon,
                CategoryColor = SelectedCategory.Color,
                Date = Date,
                Notes = Notes.Trim(),
            };

            var ok = await _db.SetDocumentAsync(
                $"users/{user.Uid}/expenses/{expense.Id}",
                user.IdToken,
                new Dictionary<string, object>
                {
                    ["id"] = expense.Id,
                    ["description"] = expense.Description,
                    ["amount"] = expense.Amount,
                    ["categoryId"] = expense.CategoryId,
                    ["categoryName"] = expense.CategoryName,
                    ["categoryIcon"] = expense.CategoryIcon,
                    ["categoryColor"] = expense.CategoryColor,
                    ["date"] = expense.Date.ToString("o"),
                    ["notes"] = expense.Notes,
                });

            if (ok)
            {
                Expenses.Insert(0, expense);
                OnPropertyChanged(nameof(HasNoExpenses));
                SuccessMessage = "✓ Expense saved!";
                ClearForm();
                await Task.Delay(2000);
                SuccessMessage = string.Empty;
            }
            else { ErrorMessage = "Failed to save. Try again."; }
        }
        finally { IsBusy = false; }
    }

    // ── DELETE ────────────────────────────────────────────────────────────────
    private async Task DeleteAsync(ExpenseModel item)
    {
        bool ok = await Application.Current!.MainPage!
            .DisplayAlert("Delete", "Remove this expense?", "Delete", "Cancel");
        if (!ok) return;

        var user = _auth.CurrentUser;
        if (user is not null)
            await _db.DeleteDocumentAsync($"users/{user.Uid}/expenses/{item.Id}", user.IdToken);

        Expenses.Remove(item);
        OnPropertyChanged(nameof(HasNoExpenses));
    }

    private void ClearForm()
    {
        Description = string.Empty;
        AmountText = string.Empty;
        Notes = string.Empty;
        Date = DateTime.Today;
        ErrorMessage = string.Empty;

        if (Categories.Count > 0 && SelectedCategory is null)
        {
            Categories[0].IsSelected = true;
            SelectedCategory = Categories[0];
        }
    }

    private static List<CategoryModel> GetDefaultCategories() => new()
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
    private static string S(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";
    private static double Dbl(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) ? Convert.ToDouble(v) : 0;
    private static DateTime Dt(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) && DateTime.TryParse(v?.ToString(), out var dt) ? dt : DateTime.Today;
}

file static class AddExpExt { public static string OrD(this string s, string d) => string.IsNullOrEmpty(s) ? d : s; }