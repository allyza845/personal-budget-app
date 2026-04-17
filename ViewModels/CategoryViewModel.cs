using System.Collections.ObjectModel;
using System.Windows.Input;
using allyza.Models;
using allyza.Services;

namespace allyza.ViewModels;

public class CategoryViewModel : BaseViewModel
{
    private readonly IFirestoreService _db;
    private readonly IFirebaseAuthService _auth;

    private string _name = string.Empty;
    private string _icon = "🏷️";
    private string _color = "#7C6FFF";
    private string _budgetText = string.Empty;
    private string _errorMsg = string.Empty;
    private string _successMsg = string.Empty;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string Icon
    {
        get => _icon;
        set { _icon = value; OnPropertyChanged(); }
    }

    public string Color
    {
        get => _color;
        set { _color = value; OnPropertyChanged(); UpdateColorSelections(); }
    }

    public string BudgetText
    {
        get => _budgetText;
        set { _budgetText = value; OnPropertyChanged(); }
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

    // ── Color chip bindings ───────────────────────────────────────────────────
    public bool IsViolet { get => _color == "#C9A8E8"; set { if (value) Color = "#C9A8E8"; } }
    public bool IsGreen { get => _color == "#90D8B0"; set { if (value) Color = "#90D8B0"; } }
    public bool IsRed { get => _color == "#F0A8A8"; set { if (value) Color = "#F0A8A8"; } }
    public bool IsAmber { get => _color == "#F5D090"; set { if (value) Color = "#F5D090"; } }
    public bool IsCyan { get => _color == "#90C8E0"; set { if (value) Color = "#90C8E0"; } }
    public bool IsPink { get => _color == "#F5A8C8"; set { if (value) Color = "#F5A8C8"; } }

    // ── Default categories ────────────────────────────────────────────────────
    private static CategoryModel[] GetDefaults() => new[]
    {
    new CategoryModel { Id = "food",          Name = "Food",          Icon = "🍔", Color = "#F5D090", IsCustom = false },
    new CategoryModel { Id = "transport",     Name = "Transport",     Icon = "🚗", Color = "#90C8E0", IsCustom = false },
    new CategoryModel { Id = "housing",       Name = "Housing",       Icon = "🏠", Color = "#C9A8E8", IsCustom = false },
    new CategoryModel { Id = "health",        Name = "Health",        Icon = "💊", Color = "#90D8B0", IsCustom = false },
    new CategoryModel { Id = "entertainment", Name = "Entertainment", Icon = "🎮", Color = "#F5A8C8", IsCustom = false },
    new CategoryModel { Id = "shopping",      Name = "Shopping",      Icon = "🛍️", Color = "#F0A8A8", IsCustom = false },
    new CategoryModel { Id = "education",     Name = "Education",     Icon = "📚", Color = "#C9A8E8", IsCustom = false },
    new CategoryModel { Id = "utilities",     Name = "Utilities",     Icon = "💡", Color = "#F5D090", IsCustom = false },
    new CategoryModel { Id = "savings",       Name = "Savings",       Icon = "🏦", Color = "#90D8B0", IsCustom = false },
    new CategoryModel { Id = "other",         Name = "Other",         Icon = "📦", Color = "#B8DEFA", IsCustom = false },
};

    public ObservableCollection<CategoryModel> Categories { get; } = new();

    // ── Commands ──────────────────────────────────────────────────────────────
    public ICommand SaveCommand { get; }
    public ICommand DeleteCategoryCommand { get; }
    public ICommand SelectIconCommand { get; }
    public ICommand SetColorCommand { get; }
    public ICommand SetBudgetCommand { get; }

    public CategoryViewModel(IFirestoreService db, IFirebaseAuthService auth)
    {
        _db = db;
        _auth = auth;
        Title = "Categories";

        SaveCommand = new Command(async () => await SaveAsync());
        DeleteCategoryCommand = new Command<CategoryModel>(async c => await DeleteAsync(c));
        SelectIconCommand = new Command<string>(icon => Icon = icon);
        SetColorCommand = new Command<string>(c => Color = c);
        SetBudgetCommand = new Command<CategoryModel>(async c => await PromptBudgetAsync(c));
    }

    // ── LOAD ──────────────────────────────────────────────────────────────────
    public async Task LoadAsync()
    {
        var user = _auth.CurrentUser;
        if (user is null) return;

        IsBusy = true;
        try
        {
            // Fetch Firebase collections in parallel (categories, budgets, hidden defaults)
            var catTask = _db.GetCollectionAsync($"users/{user.Uid}/categories", user.IdToken);
            var budgetTask = _db.GetCollectionAsync($"users/{user.Uid}/budgets", user.IdToken);
            var hiddenTask = _db.GetCollectionAsync($"users/{user.Uid}/hidden_defaults", user.IdToken);
            await Task.WhenAll(catTask, budgetTask, hiddenTask);

            // Build a set of default category IDs the user has deleted
            var hiddenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var doc in hiddenTask.Result)
            {
                try { hiddenIds.Add(S(doc, "id")); } catch { }
            }

            // Build budget lookups: categoryId → amount, categoryName → amount
            var byId = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var byName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var doc in budgetTask.Result)
            {
                try
                {
                    var id = S(doc, "categoryId");
                    var name = S(doc, "categoryName");
                    var amount = Dbl(doc, "limitAmount");
                    if (!string.IsNullOrEmpty(id)) byId[id] = amount;
                    if (!string.IsNullOrEmpty(name)) byName[name] = amount;
                }
                catch { }
            }

            // Parse user-created custom categories from Firebase
            var customs = new List<CategoryModel>();
            foreach (var doc in catTask.Result)
            {
                try
                {
                    var id = S(doc, "id");
                    var name = S(doc, "name");
                    var cat = new CategoryModel
                    {
                        Id = id,
                        Name = name,
                        Icon = S(doc, "icon").OrD("🏷️"),
                        Color = S(doc, "color").OrD("#7C6FFF"),
                        IsCustom = true,
                        Budget = byId.TryGetValue(id, out var b1) ? b1
                                 : byName.TryGetValue(name, out var b2) ? b2 : 0,
                    };
                    customs.Add(cat);
                }
                catch { }
            }

            // Populate the observable collection on the main thread
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Categories.Clear();

                // ── 1. Default categories (skip user-deleted ones) ─────────────
                foreach (var def in GetDefaults())
                {
                    if (hiddenIds.Contains(def.Id)) continue;
                    def.Budget = byId.TryGetValue(def.Id, out var b1) ? b1
                               : byName.TryGetValue(def.Name, out var b2) ? b2 : 0;
                    def.NotifyBudgetChanged();
                    Categories.Add(def);
                }

                // ── 2. User-created custom categories ──────────────────────────
                foreach (var cat in customs)
                    Categories.Add(cat);
            });
        }
        finally { IsBusy = false; }
    }

    // ── SAVE NEW CUSTOM CATEGORY ──────────────────────────────────────────────
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        ErrorMessage = SuccessMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Name))
        { ErrorMessage = "Please enter a category name."; return; }

        var user = _auth.CurrentUser;
        if (user is null) { ErrorMessage = "Not logged in."; return; }

        double budget = 0;
        var rawBudget = BudgetText.Replace(",", "").Replace("₱", "").Trim();
        if (!string.IsNullOrEmpty(rawBudget))
            double.TryParse(rawBudget, out budget);

        IsBusy = true;
        try
        {
            var cat = new CategoryModel
            {
                Id = Guid.NewGuid().ToString(),
                Name = Name.Trim(),
                Icon = Icon,
                Color = Color,
                IsCustom = true,
                Budget = budget,
            };

            var ok = await _db.SetDocumentAsync(
                $"users/{user.Uid}/categories/{cat.Id}",
                user.IdToken,
                new Dictionary<string, object>
                {
                    ["id"] = cat.Id,
                    ["name"] = cat.Name,
                    ["icon"] = cat.Icon,
                    ["color"] = cat.Color,
                });

            if (!ok) { ErrorMessage = "Failed to save. Try again."; return; }

            if (budget > 0)
                await WriteBudgetDocAsync(user, cat);

            MainThread.BeginInvokeOnMainThread(() => Categories.Add(cat));

            Name = BudgetText = string.Empty;
            Icon = "🏷️";
            Color = "#7C6FFF";
            SuccessMessage = "✓ Category saved!";
            await Task.Delay(2000);
            SuccessMessage = string.Empty;
        }
        finally { IsBusy = false; }
    }

    // ── SET / UPDATE BUDGET — works for BOTH default and custom categories ────
    private async Task PromptBudgetAsync(CategoryModel cat)
    {
        var user = _auth.CurrentUser;
        if (user is null) return;

        var current = cat.HasBudget ? cat.Budget.ToString("F2") : "";
        var input = await Application.Current!.MainPage!.DisplayPromptAsync(
            $"Budget for {cat.Name}",
            "Monthly spending limit (₱).",
            initialValue: current,
            keyboard: Microsoft.Maui.Keyboard.Telephone,
            placeholder: "e.g. 3000.00");

        if (input is null) return;

        var raw = input.Replace("₱", "").Replace(",", "").Trim();
        if (!double.TryParse(raw, out double amount) || amount < 0) return;

        // Instant UI update — card refreshes without full reload
        cat.Budget = amount;
        cat.NotifyBudgetChanged();

        if (amount > 0)
            await WriteBudgetDocAsync(user, cat);
        else
            await _db.DeleteDocumentAsync($"users/{user.Uid}/budgets/{cat.Id}", user.IdToken);
    }

    // ── DELETE CATEGORY ───────────────────────────────────────────────────────
    private async Task DeleteAsync(CategoryModel cat)
    {
        if (cat == null)
            return;

        bool confirm = await Application.Current!.MainPage!.DisplayAlert(
            "Delete Category",
            $"Are you sure you want to delete \"{cat.Name}\"?",
            "Delete",
            "Cancel");

        if (!confirm)
            return;

        var user = _auth.CurrentUser;
        if (user is not null)
        {
            if (!cat.IsCustom)
            {
                // Hide default category
                await _db.SetDocumentAsync(
                    $"users/{user.Uid}/hidden_defaults/{cat.Id}",
                    user.IdToken,
                    new Dictionary<string, object>
                    {
                        ["id"] = cat.Id
                    });
            }
            else
            {
                // Delete custom category
                await _db.DeleteDocumentAsync(
                    $"users/{user.Uid}/categories/{cat.Id}",
                    user.IdToken);
            }

            // Delete budget if exists
            if (cat.HasBudget)
            {
                await _db.DeleteDocumentAsync(
                    $"users/{user.Uid}/budgets/{cat.Id}",
                    user.IdToken);
            }
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Categories.Remove(cat);
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private async Task WriteBudgetDocAsync(UserModel user, CategoryModel cat)
    {
        await _db.SetDocumentAsync(
            $"users/{user.Uid}/budgets/{cat.Id}",
            user.IdToken,
            new Dictionary<string, object>
            {
                ["id"] = cat.Id,
                ["categoryId"] = cat.Id,
                ["categoryName"] = cat.Name,
                ["categoryIcon"] = cat.Icon,
                ["categoryColor"] = cat.Color,
                ["limitAmount"] = cat.Budget,
            });
    }

    private void UpdateColorSelections()
    {
        OnPropertyChanged(nameof(IsViolet)); OnPropertyChanged(nameof(IsGreen));
        OnPropertyChanged(nameof(IsRed)); OnPropertyChanged(nameof(IsAmber));
        OnPropertyChanged(nameof(IsCyan)); OnPropertyChanged(nameof(IsPink));
    }

    private static string S(Dictionary<string, object> d, string k) =>
        d.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";
    private static double Dbl(Dictionary<string, object> d, string k) =>
        d.TryGetValue(k, out var v) ? Convert.ToDouble(v) : 0;
}

file static class CatVmExt
{
    public static string OrD(this string s, string d) => string.IsNullOrEmpty(s) ? d : s;
}