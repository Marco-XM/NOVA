using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nova.Core.Hotkeys;

namespace Nova.App.Controls;

/// <summary>
/// Click-to-record keyboard shortcut field. Validates against reserved Windows shortcuts and common
/// app shortcuts before committing; Esc cancels, Backspace clears.
/// </summary>
public sealed class HotkeyBox : Border
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(string), typeof(HotkeyBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).Render()));
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(nameof(Message), typeof(string), typeof(HotkeyBox));
    public static readonly DependencyProperty AllowEmptyProperty = DependencyProperty.Register(nameof(AllowEmpty), typeof(bool), typeof(HotkeyBox), new PropertyMetadata(true));

    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    private bool _recording;

    public HotkeyBox()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        MinWidth = 170;
        Height = 34;
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(12, 0, 12, 0);
        Child = _text;
        SetResourceReference(BackgroundProperty, "ControlFillColorDefaultBrush");
        SetResourceReference(BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        _text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        MouseLeftButtonUp += (_, _) => StartRecording();
        LostKeyboardFocus += (_, _) => StopRecording();
        Render();
    }

    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string? Message { get => (string?)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public bool AllowEmpty { get => (bool)GetValue(AllowEmptyProperty); set => SetValue(AllowEmptyProperty, value); }

    /// <summary>Raised when recording starts/stops so global hotkeys can be paused (otherwise they'd swallow the keys).</summary>
    public event EventHandler<bool>? RecordingChanged;
    public event EventHandler<string>? Committed;

    private void StartRecording()
    {
        if (_recording) return;
        _recording = true;
        Focus();
        Keyboard.Focus(this);
        SetResourceReference(BorderBrushProperty, "AccentFillColorDefaultBrush");
        RecordingChanged?.Invoke(this, true);
        Render();
    }

    private void StopRecording()
    {
        if (!_recording) return;
        _recording = false;
        SetResourceReference(BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        RecordingChanged?.Invoke(this, false);
        Render();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_recording) { base.OnPreviewKeyDown(e); return; }
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { StopRecording(); return; }
        if ((key == Key.Back || key == Key.Delete) && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (AllowEmpty)
            {
                Value = "";
                Message = "Shortcut removed";
                Committed?.Invoke(this, "");
            }
            StopRecording();
            return;
        }

        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (Hotkey.IsModifierKey(vk) || key is Key.LWin or Key.RWin) { _text.Text = Describe(CurrentModifiers()) + "…"; return; }

        var hotkey = new Hotkey(CurrentModifiers(), vk);
        var conflict = hotkey.CheckConflicts();
        if (conflict.Level == HotkeyConflictLevel.Blocked)
        {
            Message = conflict.Message;
            return; // keep recording so the user can try another combination
        }
        Value = hotkey.ToString();
        Message = conflict.Level == HotkeyConflictLevel.Warning ? "Saved, but note: " + conflict.Message : $"{hotkey} saved";
        Committed?.Invoke(this, Value);
        StopRecording();
    }

    private static HotkeyModifiers CurrentModifiers()
    {
        var m = HotkeyModifiers.None;
        var k = Keyboard.Modifiers;
        if (k.HasFlag(ModifierKeys.Control)) m |= HotkeyModifiers.Ctrl;
        if (k.HasFlag(ModifierKeys.Alt)) m |= HotkeyModifiers.Alt;
        if (k.HasFlag(ModifierKeys.Shift)) m |= HotkeyModifiers.Shift;
        if (k.HasFlag(ModifierKeys.Windows) || Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) m |= HotkeyModifiers.Win;
        return m;
    }

    private static string Describe(HotkeyModifiers m) => new Hotkey(m, 0x41).ToString().Replace("+A", "+").TrimEnd('+') is var s && s.Length > 0 ? s + " +" : "";

    private void Render()
    {
        if (_recording)
        {
            _text.Text = "Press a shortcut…  (Esc to cancel)";
            _text.Opacity = 0.8;
            return;
        }
        _text.Opacity = 1;
        _text.Text = string.IsNullOrWhiteSpace(Value) ? "Click to set a shortcut" : Value.Replace("+", "  +  ");
    }
}
