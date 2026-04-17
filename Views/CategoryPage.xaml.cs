using allyza.ViewModels;

namespace allyza.Views;

public partial class CategoryPage : ContentPage
{
    private readonly CategoryViewModel _vm;

    public CategoryPage(CategoryViewModel vm)
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