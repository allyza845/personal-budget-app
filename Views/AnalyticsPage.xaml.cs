using allyza.ViewModels;

namespace allyza.Views;

public partial class AnalyticsPage : ContentPage
{
    private readonly AnalyticsViewModel _vm;

    public AnalyticsPage(AnalyticsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}