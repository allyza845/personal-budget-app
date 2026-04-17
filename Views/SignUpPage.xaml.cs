using allyza.ViewModels;

namespace allyza.Views;

public partial class SignUpPage : ContentPage
{
    public SignUpPage(SignUpViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}