using System.Windows.Controls;
using System.Windows.Input;

namespace Nova.App.Notch.Views;

public partial class CalculatorView : UserControl, IKeyboardView
{
    public CalculatorView()
    {
        InitializeComponent();
        PreviewTextInput += OnTextInput;
        PreviewKeyDown += OnKeyDown;
    }

    private NotchViewModel? Vm => DataContext as NotchViewModel;

    public void FocusInput()
    {
        Focus();
        Keyboard.Focus(this);
    }

    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (var ch in e.Text)
        {
            var key = ch switch
            {
                >= '0' and <= '9' => ch.ToString(),
                '.' or ',' => ".",
                '+' or '-' or '(' or ')' or '%' or '^' => ch.ToString(),
                '*' or 'x' or 'X' => "×",
                '/' => "÷",
                '=' => "=",
                _ => null,
            };
            if (key != null) Vm?.CalcKeyCommand.Execute(key);
        }
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: Vm?.CalcKeyCommand.Execute("="); e.Handled = true; break;
            case Key.Back: Vm?.CalcKeyCommand.Execute("back"); e.Handled = true; break;
            case Key.Delete: Vm?.CalcKeyCommand.Execute("C"); e.Handled = true; break;
            case Key.C when Keyboard.Modifiers == ModifierKeys.Control: Vm?.CalcKeyCommand.Execute("copy"); e.Handled = true; break;
        }
    }
}
