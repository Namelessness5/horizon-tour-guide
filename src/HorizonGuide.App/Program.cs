using System.IO;
using System.Windows;
using HorizonGuide.Core.Content;
using HorizonGuide.Core.Input;
using HorizonGuide.Core.Locations;
using HorizonGuide.Core.Roaming;
using HorizonGuide.Core.Scheduling;
using HorizonGuide.Forza;
using HorizonGuide.Playback;

namespace HorizonGuide.App;

public static class Program
{
    // 遥测超过这么久没更新就当它死了（游戏关了 / Data Out 没开）
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(2);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int handle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);

    private const int StdInputHandle = -10;
    private const uint EnableQuickEdit = 0x0040;
    private const uint EnableExtendedFlags = 0x0080;

    /// <summary>
    /// 关掉控制台的 QuickEdit。
    ///
    /// 开着 QuickEdit 时，鼠标在控制台窗口里点一下就进入"标记"模式，
    /// 此时**所有 Console.Write 会阻塞**，直到按 Enter 或 Esc。
    ///
    /// 后果不是"日志少了几行"，而是整个主循环卡死在那次写入上——
    /// 而 Log() 就在 PlayAsync 的前一行，于是音频永远播不出来。
    /// 实机第一次跑就撞上了：字幕和声音都没有，在控制台按一下 Enter 才蹦出来。
    /// </summary>
    private static void DisableQuickEdit()
    {
        var stdin = GetStdHandle(StdInputHandle);
        if (GetConsoleMode(stdin, out var mode))
            SetConsoleMode(stdin, (mode & ~EnableQuickEdit) | EnableExtendedFlags);
    }

    private static StreamWriter? _log;
    private static readonly System.Collections.Concurrent.BlockingCollection<string> _lines = new();

    /// <summary>
    /// 主循环的判断过程：现在在哪、还能待几秒、选了哪条内容。调参的时候全靠它。
    /// 同时写控制台和日志文件——WinExe 没有控制台，进游戏时看不到，事后要能翻。
    ///
    /// **写入在后台线程做，Log() 本身绝不阻塞。** 主循环里 Log() 就在 PlayAsync 前一行，
    /// 一旦写入卡住（控制台 QuickEdit、管道对端不读…），整个漫游就哑了——
    /// 一个调试功能不该有能力搞死产品功能。
    /// </summary>
    private static void Log(string line) =>
        _lines.TryAdd($"{DateTime.Now:HH:mm:ss.fff}  {line}");

    private static void StartLogWriter()
    {
        new Thread(() =>
        {
            foreach (var line in _lines.GetConsumingEnumerable())
            {
                try
                {
                    Console.WriteLine(line);
                    _log?.WriteLine(line);
                }
                catch (IOException)
                {
                    // 控制台没了就没了，不该因此崩掉漫游。
                }
            }
        })
        {
            IsBackground = true,
            Name = "Log",
        }.Start();
    }

    [STAThread]
    public static int Main(string[] args)
    {
        var root = FindProjectRoot();

        if (args.Contains("--debug"))
        {
            AllocConsole();
            DisableQuickEdit();
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var logPath = Path.Combine(root, "data", "roaming.log");
            _log = new StreamWriter(logPath, append: false) { AutoFlush = true };
            StartLogWriter();
            Log($"日志：{logPath}");
        }

        var locationsPath = Path.Combine(root, "data", "survey-drafts.json");
        var contentRoot = Path.Combine(root, "content");
        var indexPath = Path.Combine(contentRoot, "content-index.json");

        if (!File.Exists(locationsPath))
            return Fail($"找不到地点数据：{locationsPath}");

        if (!File.Exists(indexPath))
            return Fail($"找不到内容索引：{indexPath}\n\n" +
                        "先跑：python tools/content/build_index.py");

        var locations = LocationStore.Load(locationsPath);
        var store = ContentStore.Load(indexPath);

        string StringArg(string name, string fallback) =>
            args.FirstOrDefault(a => a.StartsWith($"--{name}=")) is { } a
                ? a[(name.Length + 3)..]
                : fallback;

        var lang = StringArg("lang", "zh");
        var audioLang = StringArg("audio-lang", lang);
        var subtitleLang = StringArg("subtitle-lang", lang);

        // 播放节奏的三个旋钮。做成命令行可调是因为它们纯粹是体验问题，
        // 只能实机试出来——每调一次都重新编译太慢了。
        float Arg(string name, float fallback) =>
            args.FirstOrDefault(a => a.StartsWith($"--{name}=")) is { } a
            && float.TryParse(a[(name.Length + 3)..], out var v) ? v : fallback;

        var policy = new PlaybackPolicy
        {
            SafetyMargin = Arg("margin", 1.6f),    // 预算的宽松度。>1 = 故意高估，更愿意开口
            MinBudget = Arg("min", 3f),            // 低于这么多秒就闭嘴
        };
        var silence = new SilenceTimer(TimeSpan.FromSeconds(Arg("silence", 20f)));

        // 驻留续播：玩家不走，讲完一篇歇多久再讲下一篇。
        //
        // 跟 --silence 不是一回事。--silence 是**任意两篇之间**的地板，防的是
        // 穿过密集地点时被连珠炮灌解说；--dwell 是**同一个地点里**的续播节奏，
        // 玩家停在金阁寺前面不走，说明他想听，那就接着讲——但得给他喘口气。
        var dwell = TimeSpan.FromSeconds(Arg("dwell", 30f));
        var settings = new PlaybackSettings(new PlaybackSettingsSnapshot(
            audioLang,
            subtitleLang,
            Volume: Math.Clamp(Arg("volume", 100f) / 100f, 0f, 2f),
            SubtitleFontSize: Arg("font", 26f),
            SubtitleBottomMargin: Arg("bottom", 110f),
            LabelLift: Arg("labellift", 84f)));

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var subtitle = new SubtitleWindow(
            fontSize: Arg("font", 26f),
            bottomMargin: Arg("bottom", 110f),
            labelScale: Arg("labelscale", 0.92f),
            labelLift: Arg("labellift", 84f));
        subtitle.Show();
        subtitle.HideSubtitle();
        subtitle.ApplySettings(settings.Snapshot());

        var settingsWindow = new SettingsWindow(settings);
        settingsWindow.Show();

        var receiver = new TelemetryReceiver(5300);
        var resolver = new LocationResolver(locations);
        var tracker = new LocationTracker();
        var selector = new ContentSelector(store) { Language = audioLang };
        var playback = new PlaybackService(contentRoot);
        var roaming = new RoamingController();

        playback.SubtitleShown += subtitle.ShowSubtitle;
        playback.SubtitleHidden += subtitle.HideSubtitle;
        playback.SubtitleHidden += () => subtitle.ApplySettings(settings.Snapshot());
        playback.PlaybackFailed += (c, ex) =>
            Log($"[!] 播不了 {c.Id}：{ex.Message}");

        roaming.StateChanged += state =>
        {
            if (state == RoamingState.Off)
            {
                playback.Skip();
                subtitle.HideSubtitle();
                subtitle.HideLocation();
            }

            subtitle.ShowSubtitle(state == RoamingState.Active
                ? "漫游模式：开"
                : "漫游模式：关");

            // 提示语晾两秒就收掉
            Task.Delay(2000).ContinueWith(_ =>
            {
                if (!playback.IsPlaying)
                    subtitle.HideSubtitle();
            });
        };

        roaming.SkipRequested += playback.Skip;

        using var hotkeys = new GlobalHotkeys();
        hotkeys.Bind(VirtualKeys.F6, roaming.Toggle);
        hotkeys.Bind(VirtualKeys.F10, () => settingsWindow.Dispatcher.Invoke(() =>
        {
            if (settingsWindow.IsVisible)
                settingsWindow.Hide();
            else
                settingsWindow.Show();

            if (settingsWindow.IsVisible)
                settingsWindow.Activate();
        }));
        hotkeys.Bind(VirtualKeys.F11, roaming.Skip);
        var failed = hotkeys.Start();

        Log($"地点 {locations.Count} 个，内容 {store.All.Count} 条（语言 {lang}）");
        Log($"预算系数 {policy.SafetyMargin:F2}   最低预算 {policy.MinBudget:F0}s   "
            + $"最短沉默 {silence.MinSilence.TotalSeconds:F0}s   "
            + $"驻留续播 {dwell.TotalSeconds:F0}s");
        Log("F6 开关漫游   F11 跳过当前内容");
        if (failed.Count > 0)
            Log($"[!] {failed.Count} 个热键注册失败，可能被别的程序占了");

        // --roam：启动就开着漫游。跑模拟器时按不了热键，真机调试也省一步。
        if (args.Contains("--roam"))
            roaming.Start();

        var cts = new CancellationTokenSource();
        receiver.Start();

        var loop = Task.Run(() => RunLoop(
            receiver, resolver, tracker, policy, selector,
            store, settings, silence, dwell, playback, roaming, subtitle, cts.Token));

        app.Run();

        cts.Cancel();
        receiver.Dispose();
        playback.Dispose();
        return 0;
    }

    /// <summary>设计文档 §17 的主循环。</summary>
    private static async Task RunLoop(
        TelemetryReceiver receiver,
        LocationResolver resolver,
        LocationTracker tracker,
        PlaybackPolicy policy,
        ContentSelector selector,
        ContentStore store,
        PlaybackSettings settings,
        SilenceTimer silence,
        TimeSpan dwell,
        PlaybackService playback,
        RoamingController roaming,
        SubtitleWindow subtitle,
        CancellationToken token)
    {
        // 遥测包里没有朝向，只能从两帧之间的位移推。留住上一帧的位置。
        float? lastX = null, lastZ = null;
        Heading? heading = null;

        // 这次停留里已经播过哪几篇。进入新地点时清空。
        //
        // 玩家不走就接着讲下一篇（间隔 dwell），讲完全部就安静下来。
        // 记 id 而不是只记一个 bool，是因为随机会重复：七八篇里连抽两次，
        // 撞上同一篇的概率并不低，而同一次停留里把一篇讲两遍是明显的 bug。
        var playedThisVisit = new HashSet<string>();
        var lastPieceEnded = DateTime.MinValue;
        string? shownLocationId = null;
        string? shownLocationLanguage = null;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(200, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var vehicle = receiver.LatestState;
            var now = DateTime.UtcNow;

            if (vehicle is null || now - vehicle.UpdatedAt > StaleAfter)
                continue;

            // 暂停/菜单/读盘时游戏照样发包，但坐标是 (0,0)。当成真坐标会把车
            // "传送"到地图原点，位置栈立刻乱掉。
            if (!vehicle.IsRaceOn || (vehicle.PositionX == 0 && vehicle.PositionZ == 0))
                continue;

            if (lastX is { } px && lastZ is { } pz)
                heading = Heading.FromDelta(px, pz, vehicle.PositionX, vehicle.PositionZ) ?? heading;

            lastX = vehicle.PositionX;
            lastZ = vehicle.PositionZ;

            var raw = resolver.Resolve(vehicle.PositionX, vehicle.PositionZ);

            // 确认过的位置栈变了 —— 换地名标签。
            // 这跟"播不播内容"完全无关：高速冲过去没时间讲，玩家依然该知道刚路过了哪。
            if (tracker.Update(raw, now))
            {
                playedThisVisit.Clear();   // 换地方了，这次停留还没播过
                lastPieceEnded = DateTime.MinValue;
                // 不在这里清 shownLocationId：它代表"屏幕上现在画的是谁"，
                // 只能由下面的显示/隐藏块改。清了的话，走进空地(Stable 变空)时
                // 下面的隐藏分支会误判成"本来就没显示"，导致旧标签擦不掉。

                if (tracker.Stable.Primary is { })
                    Log($"进入 {tracker.Stable}");
            }

            var currentSettings = settings.Snapshot();
            subtitle.ApplySettings(currentSettings);

            if (roaming.IsActive && tracker.Stable.Primary is { } stableLocation)
            {
                if (shownLocationId != stableLocation.Id
                    || shownLocationLanguage != currentSettings.SubtitleLanguage)
                {
                    subtitle.ShowLocation(stableLocation.DisplayName(currentSettings.SubtitleLanguage));
                    shownLocationId = stableLocation.Id;
                    shownLocationLanguage = currentSettings.SubtitleLanguage;
                }
            }
            else if (shownLocationId is not null)
            {
                subtitle.HideLocation();
                shownLocationId = null;
                shownLocationLanguage = null;
            }

            if (!roaming.IsActive || playback.IsPlaying || !silence.CanPlayNext(now))
                continue;

            // 这次停留已经讲过了 —— 玩家还没走，那就接着讲，但先歇 dwell。
            if (playedThisVisit.Count > 0 && now - lastPieceEnded < dwell)
                continue;

            var stack = tracker.Stable;
            if (stack.Primary is not { } primary)
                continue;

            // 还能在这个地点待多久。这个函数会被反复调用——播放器每放完一段都会
            // 再问一次，读的是**当时**的车辆状态。所以玩家一踩刹车，下一段就有机会继续讲。
            TimeBudget Budget()
            {
                var now = receiver.LatestState;
                if (now is null || !now.IsRaceOn)
                    return new TimeBudget(0f, "遥测断了");

                if (primary.HasPolygon && !primary.Contains(now.PositionX, now.PositionZ))
                    return new TimeBudget(0f, "已离开地点");

                return policy.Estimate(
                    primary, now.PositionX, now.PositionZ, now.Speed, heading);
            }

            var budget = Budget();
            var playbackSettings = currentSettings;
            selector.Language = playbackSettings.AudioLanguage;
            playback.Volume = playbackSettings.Volume;

            // 准入门槛是**第一段**的时长，不是整篇的总长：一篇 100 秒的内容，
            // 只要总述（8 秒）塞得进去，它就是可用的——玩家听完总述就走，那也完整。
            var content = selector.Select(stack, budget, playedThisVisit);
            if (content is null)
                continue;
            var subtitleContent = store.VariantFor(content, playbackSettings.SubtitleLanguage);

            Log($"[{stack}] 预算 {budget} -> {content.Category} "
                + $"（{content.Segments.Count} 段 / {content.TotalSeconds:F0}s，"
                + $"总述 {content.FirstSegmentSeconds:F1}s）"
                + (playedThisVisit.Count > 0 ? $"  第 {playedThisVisit.Count + 1} 篇" : ""));

            playedThisVisit.Add(content.PlaybackId);
            var played = await playback.PlayAsync(content, subtitleContent, Budget, token);

            Log($"    放了 {played}/{content.Segments.Count} 段");

            lastPieceEnded = DateTime.UtcNow;
            silence.Restart(lastPieceEnded);
        }
    }

    /// <summary>从 bin/ 往上找到项目根（有 data/ 和 content/ 的那一层）。</summary>
    private static string FindProjectRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "data")) &&
                Directory.Exists(Path.Combine(dir, "content")))
                return dir;

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        return Directory.GetCurrentDirectory();
    }

    private static int Fail(string message)
    {
        MessageBox.Show(message, "HorizonGuide", MessageBoxButton.OK, MessageBoxImage.Error);
        return 1;
    }
}
