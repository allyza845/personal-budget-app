using allyza.ViewModels;

namespace allyza.Views;

public partial class AddExpensePage : ContentPage
{
    private readonly AddExpenseViewModel _vm;

    public AddExpensePage(AddExpenseViewModel vm)
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