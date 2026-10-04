using System.Windows;
using System.Windows.Controls;

namespace Nova.App.Controls;

/// <summary>One settings row: glyph, title, description and a control on the right.</summary>
public class SettingCard : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingCard));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingCard));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingCard));
    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(nameof(Footer), typeof(object), typeof(SettingCard));

    static SettingCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingCard), new FrameworkPropertyMetadata(typeof(SettingCard)));
    }

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Description { get => (string?)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    /// <summary>Optional full-width content below the row (lists, previews).</summary>
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
}
