using allyza.ViewModels;

namespace allyza.Views;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _vm;

    public DashboardPage(DashboardViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
    private async void OnProfileTapped(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//profile");
    }
}