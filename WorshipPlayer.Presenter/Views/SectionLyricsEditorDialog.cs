using System.Windows;
using System.Windows.Controls;

namespace WorshipPlayer.Presenter.Views;

/// <summary>
/// Code-built (no XAML) editor for ONE song section's lyric text, opened from a
/// slide thumbnail's right-click menu in the OperatorWindow (field feature
/// 2026-10-24: "edit this chorus"). The caller owns save + refresh; this dialog
/// only collects text. The slide generator shows linesPerSlide lines at a time
/// (a blank line also starts a new slide), so the hint says so.
/// </summary>
public sealed class SectionLyricsEditorDialog : Window
{
    private readonly TextBox _textBox;

    /// <summary>The text as edited (read after ShowDialog returns true).</summary>
    public string EditedText => _textBox.Text;

    public SectionLyricsEditorDialog(string songTitle, string sectionName, string currentText)
    {
        Title = $"Edit Section Lyrics — {sectionName}";
        Width = 560;
        Height = 460;
        MinWidth = 360;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = $"{songTitle} — {sectionName}",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2)
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var hint = new TextBlock
        {
            Text = "Edit the words for this whole section. On screen they show 2 lines at a time (a blank line also starts a new slide).",
            FontSize = 11,
            Opacity = 0.7,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(hint, 1);
        root.Children.Add(hint);

        _textBox = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 14,
            Text = currentText ?? ""
        };
        Grid.SetRow(_textBox, 2);
        root.Children.Add(_textBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var save = new Button
        {
            Content = "Save",
            Width = 90,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        save.Click += (_, _) => { DialogResult = true; };
        var cancel = new Button
        {
            Content = "Cancel",
            Width = 90,
            Height = 30,
            IsCancel = true
        };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) => { _textBox.Focus(); _textBox.CaretIndex = _textBox.Text.Length; };
    }
}
