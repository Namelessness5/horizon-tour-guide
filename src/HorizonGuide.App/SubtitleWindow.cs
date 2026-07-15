using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace HorizonGuide.App;

/// <summary>
/// 屏幕上的两样东西：地名标签，和字幕。
///
/// 无边框、置顶、鼠标穿透。鼠标穿透是必须的：窗口盖在游戏画面上，
/// 不穿透的话玩家的鼠标点在字幕上就点不到游戏了。WPF 的 IsHitTestVisible=false
/// 只管窗口内部，挡不住系统级的点击——得在 Win32 层加 WS_EX_TRANSPARENT。
///
/// 地名标签和字幕是**两个独立通道**，这是有意的：
///
///   地名走视觉，故事走音频。
///
/// 你路过涩谷十次，前九次都知道自己在哪，第十次的"欢迎来到涩谷十字路口"就是纯噪音，
/// 而音频时间是最稀缺的资源（只有 4 到 26 秒）。标签零音频成本、零时间占用，
/// 而且**不播内容的时候它也在**——高速冲过去没时间讲，但你依然知道刚路过了哪。
/// </summary>
public sealed class SubtitleWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExToolWindow = 0x80;   // 不出现在 Alt+Tab 里

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    private readonly TextBlock _subtitle;
    private readonly Border _label;
    private readonly TextBlock _labelText;
    private readonly double _labelScale;

    /// <summary>
    /// 字幕最宽占屏幕的多少。
    ///
    /// 一开始给的 0.7 太宽了——字幕横跨大半个屏幕，眼睛要来回扫，
    /// 而玩家的注意力本来就该在路上。1/3 左右一行装 15-20 个汉字，
    /// 视线基本不用移动。
    /// </summary>
    private const double SubtitleWidthRatio = 1.0 / 3.0;

    /// <param name="fontSize">字幕字号。</param>
    /// <param name="bottomMargin">字幕离屏幕底边多远。</param>
    /// <param name="labelScale">地名字号 = 字幕字号 × 这个值。</param>
    /// <param name="labelLift">地名比字幕再高多少像素。</param>
    public SubtitleWindow(
        double fontSize = 26,
        double bottomMargin = 110,
        double labelScale = 0.92,
        double labelLift = 84)
    {
        _labelScale = labelScale;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;

        // 铺满主屏。这样不用管窗口定位，只管内容对齐。
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        var maxWidth = SystemParameters.PrimaryScreenWidth * SubtitleWidthRatio;

        // 游戏画面亮起来的时候白字会糊掉。描边比半透明底板干净——
        // 底板会挡住画面，而这游戏的画面就是玩家在看的东西。
        static DropShadowEffect Outline() => new()
        {
            Color = Colors.Black,
            ShadowDepth = 0,
            BlurRadius = 8,
            Opacity = 0.9,
        };

        _labelText = new TextBlock
        {
            FontSize = fontSize * labelScale,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = maxWidth,
        };

        // 地名标签给一层浅底：它是"身份"，要和"正在说的话"区分开，
        // 不然两行白字叠在一起，玩家分不清哪句是解说。
        _label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
            BorderThickness = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(18, 6, 18, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, bottomMargin + labelLift),
            Child = _labelText,
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            Effect = Outline(),
        };

        _subtitle = new TextBlock
        {
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = maxWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, bottomMargin),
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            Effect = Outline(),
        };

        // 地名和字幕**各自独立定位**，不叠在一个 StackPanel 里。
        //
        // 叠在一起的话，字幕一出现就会把地名顶上去、字幕一消失地名又掉回来——
        // 地名会跟着解说上下跳。而地名是"你在哪"，它该待在固定的位置不动，
        // 玩家余光扫一眼就能确认，不需要重新找。
        Content = new Grid { Children = { _label, _subtitle } };

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle, style | WsExTransparent | WsExToolWindow);
        };
    }

    /// <summary>进入一个地点。标签淡入并留着——离开之前玩家随时能看到自己在哪。</summary>
    public void ShowLocation(string name) => Dispatcher.Invoke(() =>
    {
        _labelText.Text = name;
        Fade(_label, to: 1);
    });

    /// <summary>离开地点。</summary>
    public void HideLocation() => Dispatcher.Invoke(() => Fade(_label, to: 0));

    public void ShowSubtitle(string text) => Dispatcher.Invoke(() =>
    {
        _subtitle.Text = text;
        Fade(_subtitle, to: 1);
    });

    public void HideSubtitle() => Dispatcher.Invoke(() => Fade(_subtitle, to: 0));

    public void ApplySettings(PlaybackSettingsSnapshot settings) => Dispatcher.Invoke(() =>
    {
        _subtitle.FontSize = settings.SubtitleFontSize;
        _subtitle.Margin = new Thickness(0, 0, 0, settings.SubtitleBottomMargin);

        _labelText.FontSize = settings.SubtitleFontSize * _labelScale;
        _label.Margin = new Thickness(
            0, 0, 0, settings.SubtitleBottomMargin + settings.LabelLift);
    });

    /// <summary>
    /// 淡入淡出。硬切会在余光里"闪"一下，在开车时格外扎眼。
    /// 淡出结束后收成 Collapsed，避免透明元素还占着布局。
    /// </summary>
    private static void Fade(UIElement element, double to)
    {
        var duration = TimeSpan.FromMilliseconds(to > 0 ? 220 : 320);

        if (to > 0)
            element.Visibility = Visibility.Visible;

        var fade = new DoubleAnimation(to, new Duration(duration))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };

        if (to == 0)
        {
            fade.Completed += (_, _) =>
            {
                if (element.Opacity == 0)
                    element.Visibility = Visibility.Collapsed;
            };
        }

        element.BeginAnimation(OpacityProperty, fade);
    }
}
