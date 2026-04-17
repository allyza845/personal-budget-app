namespace allyza.Models;

public class MonthlyTrendModel
{
    public string MonthLabel { get; set; } = string.Empty;
    public double Income { get; set; }
    public double Expenses { get; set; }
    public double IncomeRatio { get; set; }
    public double ExpenseRatio { get; set; }
    public string IncomeDisplay { get; set; } = string.Empty;
    public string ExpenseDisplay { get; set; } = string.Empty;
}