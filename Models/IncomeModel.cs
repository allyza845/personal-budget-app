namespace allyza.Models;

public class IncomeModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Source { get; set; } = string.Empty;
    public double Amount { get; set; }
    public string Frequency { get; set; } = "Monthly";   // Monthly, Weekly, One-time, etc.
    public DateTime Date { get; set; } = DateTime.Today;
    public string Notes { get; set; } = string.Empty;
    public string DateDisplay => Date.ToString("MMM d, yyyy");
    public string AmountDisplay => $"₱{Amount:N2}";       // change ₱ to $ if preferred
}