namespace allyza.Models;

public class BudgetModel
{
    public string Id { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryIcon { get; set; } = "🏷️";
    public string CategoryColor { get; set; } = "#7C6FFF";

    public double LimitAmount { get; set; }
    public double SpentAmount { get; set; }

    // ── Derived ───────────────────────────────────────────────────────────────

    public bool HasBudget => LimitAmount > 0;

    /// <summary>
    /// Raw (unclamped) ratio of spent to limit.
    ///   < 1.0  = under budget
    ///   = 1.0  = exactly at limit
    ///   > 1.0  = over budget
    ///
    /// Example:
    ///   Limit ₱3,000 / Spent ₱4,500 → BudgetRawRatio = 1.50
    ///   Limit ₱3,000 / Spent ₱1,200 → BudgetRawRatio = 0.40
    /// </summary>
    public double BudgetRawRatio => HasBudget ? SpentAmount / LimitAmount : 0;

    /// <summary>
    /// Clamped to [0, 1] for the ProgressBar control.
    /// Use BudgetRawRatio for color thresholds so > 100% still turns red.
    /// </summary>
    public double UsageRatio => Math.Min(BudgetRawRatio, 1.0);

    public bool IsOverBudget => HasBudget && SpentAmount > LimitAmount;

    public double RemainingAmount => HasBudget ? Math.Max(LimitAmount - SpentAmount, 0) : 0;
    public double OverspentAmount => HasBudget ? Math.Max(SpentAmount - LimitAmount, 0) : 0;

    // ── Display strings ───────────────────────────────────────────────────────

    public string LimitDisplay => HasBudget ? $"₱{LimitAmount:N2}" : "No limit";

    /// <summary>
    /// Shows "₱spent / ₱limit (XX%)" when a limit exists, otherwise just the
    /// spent amount so unbudgeted categories are still informative.
    ///
    /// Examples:
    ///   HasBudget  → "₱2,400.00 / ₱3,000.00 (80%)"
    ///   No budget  → "₱1,200.00 spent"
    /// </summary>
    public string UsageDisplay =>
        HasBudget
            ? $"₱{SpentAmount:N2} / ₱{LimitAmount:N2} ({BudgetRawRatio * 100:F0}%)"
            : $"₱{SpentAmount:N2} spent";

    /// <summary>
    /// Shows remaining amount when under, overspent amount when over.
    ///
    /// Examples:
    ///   Under budget → "₱600.00 left"
    ///   Over budget  → "₱1,500.00 over"
    ///   No limit     → "—"
    /// </summary>
    public string RemainingDisplay =>
        !HasBudget ? "—"
      : IsOverBudget ? $"₱{OverspentAmount:N2} over"
      : $"₱{RemainingAmount:N2} left";

    /// <summary>
    /// Short chip label for the tap-to-set-budget button.
    ///
    /// Examples:
    ///   Over budget       → "OVER"
    ///   ≥ 80% used        → "80%"
    ///   Has budget        → "40%"
    ///   No budget set     → "Set limit"
    /// </summary>
    public string StatusLabel =>
        !HasBudget ? "Set limit"
      : IsOverBudget ? "OVER"
      : $"{BudgetRawRatio * 100:F0}%";

    /// <summary>
    /// Color for the progress bar and amount labels.
    ///   Over budget (raw ≥ 1.0) → red
    ///   Approaching (raw ≥ 0.8) → amber
    ///   Healthy                 → blue
    /// </summary>
    public string StatusColor =>
        BudgetRawRatio >= 1.0 ? "#EF4444"
      : BudgetRawRatio >= 0.8 ? "#F59E0B"
      : "#1A7BB9";
}