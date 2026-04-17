using allyza.ViewModels;

namespace allyza.Views;

public partial class AddIncomePage : ContentPage
{
    private readonly AddIncomeViewModel _vm;

    public AddIncomePage(AddIncomeViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadIncomesAsync();   // Fetch from Firestore every time tab is opened
    }
}



    