namespace allyza.Models;

public class CategorySummary
{
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "📦";
    public string Color { get; set; } = "#7C6FFF";
    public double Amount { get; set; }
    public double Percentage { get; set; }
    public string AmountDisplay => $"₱{Amount:N2}";
    public string PercentDisplay => $"{Percentage:F1}%";

    // Bar width as grid column ratio — used in XAML progress bar
    public double BarRatio => Percentage / 100.0;
}