namespace allyza.Views.Controls;

public partial class BottomNavBar : ContentView
{
    // ── Colors ────────────────────────────────────────────────────────────────
    private const string ActiveBoxBg = "#A8D4F0";
    private const string InactiveBoxBg = "#5BA8D8";
    private const string ActiveLabelColor = "#FFFFFF";
    private const string InactiveLabelColor = "#A8D4F0";

    // ── Route map ─────────────────────────────────────────────────────────────
    private static readonly Dictionary<string, (int index, string name, string emoji)> RouteMap = new()
    {
        { "dashboard",  (0, "Home",       "🏠") },
        { "income",     (1, "Income",     "💰") },
            { "categories", (2, "Categories", "🗂️") },
        { "expenses",   (3, "Expenses",   "💸") },
          { "fixed",      (4, "Fixed",      "📌") },
           { "analytics",  (5, "Analytics",  "📊") },
        { "history",    (6, "History",    "🕐") },
        { "profile",    (7, "Profile",    "👤") },
          { "about",      (8, "About",      "ℹ️") },
    };

    private (Border box, Label label)[] _tabs = null!;
    private bool _isExpanded = false;

    public BottomNavBar()
    {
        InitializeComponent();

        _tabs = new[]
        {
            (HomeBox,       HomeLabel),
            (IncomeBox,     IncomeLabel),
                (CategoriesBox, CategoriesLabel),
            (ExpensesBox,   ExpensesLabel),
                (FixedBox,      FixedLabel),
                    (AnalyticsBox,  AnalyticsLabel),
            (HistoryBox,    HistoryLabel),               
            (ProfileBox,    ProfileLabel),
             (AboutBox,      AboutLabel),
        };

        SetAllInactive();
        Shell.Current.Navigated += OnShellNavigated;
    }

    private void OnMenuToggleTapped(object sender, EventArgs e)
    {
        _isExpanded = !_isExpanded;
        ExpandedPanel.IsVisible = _isExpanded;
        MenuIcon.Text = _isExpanded ? "✕" : "☰";
        MenuLabel.Text = _isExpanded ? "Close" : "Menu";
    }

    private void CloseMenu()
    {
        _isExpanded = false;
        ExpandedPanel.IsVisible = false;
        MenuIcon.Text = "☰";
        MenuLabel.Text = "Menu";
    }
    // ── Shell navigation listener ─────────────────────────────────────────────
    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        var route = e.Current?.Location?.ToString() ?? string.Empty;
        foreach (var kvp in RouteMap)
        {
            if (route.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    SetActive(kvp.Value.index);
                    CurrentPageLabel.Text = kvp.Value.name;
                    ActiveIconImage.Text = kvp.Value.emoji;
                });
                return;
            }
        }
    }

    // ── Highlight ─────────────────────────────────────────────────────────────
    private void SetActive(int index)
    {
        for (int i = 0; i < _tabs.Length; i++)
        {
            bool active = i == index;
            _tabs[i].box.BackgroundColor = Color.FromArgb(active ? ActiveBoxBg : InactiveBoxBg);
            _tabs[i].label.TextColor = Color.FromArgb(active ? ActiveLabelColor : InactiveLabelColor);
        }
    }

    private void SetAllInactive()
    {
        foreach (var (box, label) in _tabs)
        {
            box.BackgroundColor = Color.FromArgb(InactiveBoxBg);
            label.TextColor = Color.FromArgb(InactiveLabelColor);
        }
    }

    // ── Tap handlers ──────────────────────────────────────────────────────────
    private async void OnHomeTapped(object sender, EventArgs e)
    { CloseMenu(); await Shell.Current.GoToAsync("//dashboard"); }

    private async void OnIncomeTapped(object sender, EventArgs e)
    { CloseMenu(); await Shell.Current.GoToAsync("//income"); }

    private async void OnExpensesTapped(object sender, EventArgs e)
    { CloseMenu(); await Shell.Current.GoToAsync("//expenses"); }

    private async void OnHistoryTapped(object sender, EventArgs e)
    { CloseMenu(); await Shell.Current.GoToAsync("//history"); }

    private async void OnAnalyticsTapped(object sender, EventArgs e)
    { CloseMenu(); await Shell.Current.GoToAsync("//analytics"); }

    private async void OnFixedTapped(object sender, EventArgs e)
    { CloseMenu(); await Shell.Current.GoToAsync("//fixed"); }

    private async void OnCategoriesTapped(object sender, EventArgs e)
    { CloseMenu(); await Shell.Current.GoToAsync("//categories"); }

    private async void OnProfileTapped(object sender, EventArgs e)
    { CloseMenu(); await Shell.Current.GoToAsync("//profile"); }

    private async void OnAboutTapped(object sender, EventArgs e)
    {
        CloseMenu(); await Shell.Current.GoToAsync("//about");
    }
}