namespace allyza.Models;

public class TransactionModel
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "expense"; // "income", "expense", "fixed"
    public string Title { get; set; } = string.Empty;
    public double Amount { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryIcon { get; set; } = "📦";
    public string CategoryColor { get; set; } = "#7C6FFF";
    public DateTime Date { get; set; } = DateTime.Today;
    public string Notes { get; set; } = string.Empty;

    public bool IsIncome => Type == "income";
    public bool IsFixed => Type == "fixed";
    public bool IsDeleteable => Type != "fixed";   // fixed items deleted from Fixed page

    public string AmountDisplay => IsIncome ? $"+₱{Amount:N2}" : $"-₱{Amount:N2}";
    public string AmountColor => IsIncome ? "#22C55E"
                                 : IsFixed ? "#F59E0B"
                                 : "#EF4444";
    public string DateDisplay => Date.ToString("MMM d, yyyy");
    public string MonthYear => Date.ToString("MMMM yyyy");
}