using allyza.ViewModels;

namespace allyza.Views;

public partial class FixedExpensePage : ContentPage
{
    private readonly FixedExpenseViewModel _vm;

    public FixedExpensePage(FixedExpenseViewModel vm)
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