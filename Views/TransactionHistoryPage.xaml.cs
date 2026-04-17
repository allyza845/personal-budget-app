using allyza.ViewModels;

namespace allyza.Views;

public partial class TransactionHistoryPage : ContentPage
{
    private readonly TransactionHistoryViewModel _vm;

    public TransactionHistoryPage(TransactionHistoryViewModel vm)
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