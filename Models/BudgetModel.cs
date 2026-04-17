namespace allyza.Models;

public class BudgetModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string CategoryId { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryIcon { get; set; } = "🏷️";
    public string CategoryColor { get; set; } = "#7C6FFF";
    public double LimitAmount { get; set; }

    // Calculated at runtime — not stored in Firebase
    public double SpentAmount { get; set; }

    public double Remaining => LimitAmount - SpentAmount;
    public bool IsOverBudget => LimitAmount > 0 && SpentAmount > LimitAmount;
    public bool HasBudget => LimitAmount > 0;
    public double UsageRatio => LimitAmount > 0 ? Math.Min(SpentAmount / LimitAmount, 1.0) : 0;

    public string LimitDisplay => HasBudget ? $"₱{LimitAmount:N2}" : "No budget set";
    public string SpentDisplay => $"₱{SpentAmount:N2}";
    public string RemainingDisplay => IsOverBudget
        ? $"₱{Math.Abs(Remaining):N2} over"
        : HasBudget ? $"₱{Remaining:N2} left" : "";
    public string StatusColor => IsOverBudget ? "#EF4444"
                                    : UsageRatio > 0.8 ? "#F59E0B"
                                    : "#22C55E";
    public string StatusLabel => IsOverBudget ? "Over budget"
                                    : HasBudget ? $"{(1 - UsageRatio) * 100:F0}% remaining"
                                    : "Tap to set budget";
    public string UsageDisplay => HasBudget ? $"₱{SpentAmount:N2} / ₱{LimitAmount:N2}" : $"₱{SpentAmount:N2} spent";
}