using allyza.ViewModels;

namespace allyza.Views;

public partial class HomePage : ContentPage
{
    public HomePage(HomeViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}