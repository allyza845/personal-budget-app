using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using allyza.Models;
using allyza.Services;

namespace allyza.ViewModels;

public class AddIncomeViewModel : BaseViewModel
{
    private readonly IFirestoreService _db;
    private readonly IFirebaseAuthService _auth;

    private string _source = string.Empty;
    private string _amountText = string.Empty;
    private string _frequency = "Monthly";
    private DateTime _date = DateTime.Today;
    private string _notes = string.Empty;
    private string _errorMsg = string.Empty;
    private string _successMsg = string.Empty;

    public string Source { get => _source; set { _source = value; OnPropertyChanged(); } }
    public string AmountText { get => _amountText; set { _amountText = value; OnPropertyChanged(); } }
    public DateTime Date { get => _date; set { _date = value; OnPropertyChanged(); } }
    public string Notes { get => _notes; set { _notes = value; OnPropertyChanged(); } }

    public string Frequency
    {
        get => _frequency;
        set { _frequency = value; OnPropertyChanged(); UpdateChips(); }
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
    public bool HasNoIncomes => Incomes.Count == 0;

    // Frequency chips
    public bool IsMonthly { get => _frequency == "Monthly"; set { if (value) Frequency = "Monthly"; } }
    public bool IsWeekly { get => _frequency == "Weekly"; set { if (value) Frequency = "Weekly"; } }
    public bool IsBiweekly { get => _frequency == "Bi-weekly"; set { if (value) Frequency = "Bi-weekly"; } }
    public bool IsAnnual { get => _frequency == "Annual"; set { if (value) Frequency = "Annual"; } }
    public bool IsOneTime { get => _frequency == "One-time"; set { if (value) Frequency = "One-time"; } }

    public ObservableCollection<IncomeModel> Incomes { get; } = new();

    public ICommand SaveCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand DeleteIncomeCommand { get; }
    public ICommand SetFrequencyCommand { get; }
    public ICommand LoadCommand { get; }

    public AddIncomeViewModel(IFirestoreService db, IFirebaseAuthService auth)
    {
        _db = db;
        _auth = auth;
        Title = "Add Income";

        SaveCommand = new Command(async () => await SaveAsync());
        ClearCommand = new Command(ClearForm);
        DeleteIncomeCommand = new Command<IncomeModel>(async item => await DeleteAsync(item));
        SetFrequencyCommand = new Command<string>(f => Frequency = f);
        LoadCommand = new Command(async () => await LoadIncomesAsync());

        // ← THIS is the missing line — notifies XAML whenever list changes
        Incomes.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasNoIncomes));
    }

    // ── SAVE ─────────────────────────────────────────────────────────────────
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        ErrorMessage = SuccessMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Source))
        { ErrorMessage = "Please enter an income source."; return; }

        var rawAmount = AmountText.Replace(",", "").Replace("₱", "").Replace("$", "").Trim();
        if (!double.TryParse(rawAmount, out double amount) || amount <= 0)
        { ErrorMessage = "Please enter a valid amount."; return; }

        IsBusy = true;
        try
        {
            var user = _auth.CurrentUser;
            if (user is null) { ErrorMessage = "Not logged in."; return; }

            var income = new IncomeModel
            {
                Id = Guid.NewGuid().ToString(),
                Source = Source.Trim(),
                Amount = amount,
                Frequency = Frequency,
                Date = Date,
                Notes = Notes.Trim(),
            };

            var ok = await _db.SetDocumentAsync(
                $"users/{user.Uid}/incomes/{income.Id}",
                user.IdToken,
                new Dictionary<string, object>
                {
                    ["id"] = income.Id,
                    ["source"] = income.Source,
                    ["amount"] = income.Amount,
                    ["frequency"] = income.Frequency,
                    ["date"] = income.Date.ToString("o"),
                    ["notes"] = income.Notes,
                });

            if (ok)
            {
                Incomes.Insert(0, income);
                SuccessMessage = "✓ Income saved!";
                ClearForm();
                await Task.Delay(2500);
                SuccessMessage = string.Empty;
            }
            else { ErrorMessage = "Failed to save. Check your connection."; }
        }
        finally { IsBusy = false; }
    }

    // ── LOAD ─────────────────────────────────────────────────────────────────
    public async Task LoadIncomesAsync()
    {
        var user = _auth.CurrentUser;
        if (user is null) return;

        IsBusy = true;
        try
        {
            var docs = await _db.GetCollectionAsync($"users/{user.Uid}/incomes", user.IdToken);

            Incomes.Clear();
            foreach (var doc in docs)
            {
                try
                {
                    Incomes.Add(new IncomeModel
                    {
                        Id = doc.TryGetValue("id", out var id) ? id.ToString()! : Guid.NewGuid().ToString(),
                        Source = doc.TryGetValue("source", out var src) ? src.ToString()! : "",
                        Amount = doc.TryGetValue("amount", out var amt) ? Convert.ToDouble(amt) : 0,
                        Frequency = doc.TryGetValue("frequency", out var frq) ? frq.ToString()! : "Monthly",
                        Notes = doc.TryGetValue("notes", out var n) ? n.ToString()! : "",
                        Date = doc.TryGetValue("date", out var dt)
                                    && DateTime.TryParse(dt.ToString(), out var parsed)
                                    ? parsed : DateTime.Today,
                    });
                }
                catch { /* skip malformed doc */ }
            }

            // Sort newest first
            var sorted = Incomes.OrderByDescending(i => i.Date).ToList();
            Incomes.Clear();
            foreach (var i in sorted) Incomes.Add(i);
        }
        finally { IsBusy = false; }
    }

    // ── DELETE ────────────────────────────────────────────────────────────────
    private async Task DeleteAsync(IncomeModel item)
    {
        bool ok = await Application.Current!.MainPage!
            .DisplayAlert("Delete", "Remove income?", "Delete", "Cancel");
        if (!ok) return;

        var user = _auth.CurrentUser;
        if (user is not null)
            await _db.DeleteDocumentAsync($"users/{user.Uid}/incomes/{item.Id}", user.IdToken);

        Incomes.Remove(item);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    private void ClearForm()
    {
        Source = string.Empty;
        AmountText = string.Empty;
        Notes = string.Empty;
        Date = DateTime.Today;
        Frequency = "Monthly";
        ErrorMessage = string.Empty;
    }

    private void UpdateChips()
    {
        OnPropertyChanged(nameof(IsMonthly));
        OnPropertyChanged(nameof(IsWeekly));
        OnPropertyChanged(nameof(IsBiweekly));
        OnPropertyChanged(nameof(IsAnnual));
        OnPropertyChanged(nameof(IsOneTime));
    }
}