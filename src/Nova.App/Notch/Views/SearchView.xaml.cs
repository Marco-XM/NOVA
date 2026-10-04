using System.Windows.Controls;
using System.Windows.Input;

namespace Nova.App.Notch.Views;

public partial class SearchView : UserControl, IKeyboardView
{
    public SearchView()
    {
        InitializeComponent();
        Query.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && DataContext is NotchViewModel vm)
            {
                vm.SearchCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    public void FocusInput()
    {
        Query.Focus();
        Keyboard.Focus(Query);
        Query.SelectAll();
    }
}
