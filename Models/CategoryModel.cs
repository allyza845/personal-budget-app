using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace allyza.Models;

public class CategoryModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private double _budget;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "🏷️";
    public string Color { get; set; } = "#7C6FFF";
    public bool IsCustom { get; set; } = true;

    // ── Budget — loaded from budgets collection on load ───────────────────────
    public double Budget
    {
        get => _budget;
        set { _budget = value; NotifyBudgetChanged(); }
    }

    public bool HasBudget => _budget > 0;
    public string BudgetDisplay => HasBudget ? $"₱{_budget:N2}/mo" : "No budget set";
    public string BudgetColor => HasBudget ? "#22C55E" : "#4040A0";

    /// <summary>Refresh all budget-related bindings after external set.</summary>
    public void NotifyBudgetChanged()
    {
        OnPropertyChanged(nameof(Budget));
        OnPropertyChanged(nameof(HasBudget));
        OnPropertyChanged(nameof(BudgetDisplay));
        OnPropertyChanged(nameof(BudgetColor));
    }

    // ── Category chip selection (Expenses / Fixed Expenses pickers) ───────────
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChipBg));
            OnPropertyChanged(nameof(ChipStroke));
            OnPropertyChanged(nameof(ChipTextColor));
        }
    }

    public string ChipBg => _isSelected ? Color : "#1A1A2E";
    public string ChipStroke => _isSelected ? Color : "#2A2A45";
    public string ChipTextColor => _isSelected ? "#FFFFFF" : "#9090B0";

    public event PropertyChangedEventHandler? PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string n = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}