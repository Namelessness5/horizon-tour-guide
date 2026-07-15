using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HorizonGuide.App;

public sealed class SettingsWindow : Window
{
    private sealed record LanguageOption(string Code, string Name);

    private static readonly LanguageOption[] Languages =
    [
        new("zh", "中文"),
        new("ja", "日本語"),
        new("en", "English"),
    ];

    private readonly PlaybackSettings _settings;
    private readonly TextBlock _volumeValue = ValueText();
    private readonly TextBlock _fontValue = ValueText();
    private readonly TextBlock _bottomValue = ValueText();
    private readonly TextBlock _labelLiftValue = ValueText();

    public SettingsWindow(PlaybackSettings settings)
    {
        _settings = settings;
        var current = settings.Snapshot();

        Title = "Horizon Guide";
        Width = 360;
        Height = 390;
        MinWidth = 340;
        MinHeight = 360;
        ResizeMode = ResizeMode.CanMinimize;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 80;
        Top = 80;

        var root = new Grid
        {
            Margin = new Thickness(18),
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });

        var audio = LanguageCombo(current.AudioLanguage);
        audio.SelectionChanged += (_, _) =>
            _settings.Update(audioLanguage: SelectedLanguage(audio));
        AddRow(root, 0, "音频", audio);

        var subtitles = LanguageCombo(current.SubtitleLanguage);
        subtitles.SelectionChanged += (_, _) =>
            _settings.Update(subtitleLanguage: SelectedLanguage(subtitles));
        AddRow(root, 1, "字幕", subtitles);

        var volume = Slider(0, 200, current.Volume * 100);
        volume.ValueChanged += (_, _) =>
        {
            _volumeValue.Text = $"{volume.Value:F0}%";
            _settings.Update(volume: (float)(volume.Value / 100.0));
        };
        _volumeValue.Text = $"{volume.Value:F0}%";
        AddRow(root, 2, "音量", volume, _volumeValue);

        var font = Slider(16, 48, current.SubtitleFontSize);
        font.ValueChanged += (_, _) =>
        {
            _fontValue.Text = $"{font.Value:F0}";
            _settings.Update(subtitleFontSize: font.Value);
        };
        _fontValue.Text = $"{font.Value:F0}";
        AddRow(root, 3, "字号", font, _fontValue);

        var bottom = Slider(30, 320, current.SubtitleBottomMargin);
        bottom.ValueChanged += (_, _) =>
        {
            _bottomValue.Text = $"{bottom.Value:F0}";
            _settings.Update(subtitleBottomMargin: bottom.Value);
        };
        _bottomValue.Text = $"{bottom.Value:F0}";
        AddRow(root, 4, "底距", bottom, _bottomValue);

        var labelLift = Slider(40, 180, current.LabelLift);
        labelLift.ValueChanged += (_, _) =>
        {
            _labelLiftValue.Text = $"{labelLift.Value:F0}";
            _settings.Update(labelLift: labelLift.Value);
        };
        _labelLiftValue.Text = $"{labelLift.Value:F0}";
        AddRow(root, 5, "地名高度", labelLift, _labelLiftValue);

        var note = new TextBlock
        {
            Text = "更改会在当前内容结束后用于下一条内容。",
            Foreground = new SolidColorBrush(Color.FromRgb(92, 92, 92)),
            FontSize = 12,
            Margin = new Thickness(0, 14, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(note, 6);
        Grid.SetColumnSpan(note, 3);
        root.Children.Add(note);

        Content = root;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private static ComboBox LanguageCombo(string selected)
    {
        var combo = new ComboBox
        {
            ItemsSource = Languages,
            DisplayMemberPath = nameof(LanguageOption.Name),
            SelectedValuePath = nameof(LanguageOption.Code),
            SelectedValue = selected,
            MinHeight = 28,
            Margin = new Thickness(0, 4, 0, 4),
        };

        return combo;
    }

    private static string SelectedLanguage(ComboBox combo) =>
        combo.SelectedValue as string ?? "zh";

    private static Slider Slider(double min, double max, double value) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = value,
        TickFrequency = Math.Max(1, (max - min) / 10),
        IsSnapToTickEnabled = false,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 6, 8, 6),
    };

    private static TextBlock ValueText() => new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Right,
        Foreground = Brushes.Black,
    };

    private static void AddRow(Grid grid, int row, string label, UIElement control, UIElement? value = null)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
        };

        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);

        if (value is null)
            return;

        Grid.SetRow(value, row);
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
    }
}
