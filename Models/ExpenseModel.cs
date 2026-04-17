namespace allyza.Models;

public class ExpenseModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Description { get; set; } = string.Empty;
    public double Amount { get; set; }
    public string CategoryId { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryIcon { get; set; } = "🏷️";
    public string CategoryColor { get; set; } = "#7C6FFF";
    public DateTime Date { get; set; } = DateTime.Today;
    public string Notes { get; set; } = string.Empty;
    public string AmountDisplay => $"₱{Amount:N2}";
    public string DateDisplay => Date.ToString("MMM d, yyyy");
}