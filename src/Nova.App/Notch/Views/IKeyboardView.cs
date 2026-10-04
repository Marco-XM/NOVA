namespace Nova.App.Notch.Views;

/// <summary>A notch view that needs keyboard input; the notch window is activated while it is shown.</summary>
public interface IKeyboardView
{
    void FocusInput();
}
