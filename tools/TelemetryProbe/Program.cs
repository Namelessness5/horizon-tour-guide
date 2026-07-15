using HorizonGuide.Core.Input;
using HorizonGuide.Core.Locations;
using HorizonGuide.Forza;

namespace HorizonGuide.Tools.TelemetryProbe;

/// <summary>
/// 第一阶段的遥测探针。
///
/// 两种模式：
///   watch（默认）——包长度能匹配已知布局时，直接显示坐标和速度；
///   scan          ——包长度未知（FH6 很可能就是这种情况）时，把每个 4 字节偏移
///                    都当成 float 打出来，边开车边看哪三个像坐标。
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // Windows 控制台默认是 GBK，源文件是 UTF-8，不设这两个中文会是乱码
        // （输出是显示乱码，输入是把地点中文名存成乱码）。
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.InputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException)
        {
            // 没有真实控制台（输出被重定向）时设不了，不影响功能。
        }

        var port = GetIntArg(args, "--port") ?? 5300;
        var forceScan = args.Contains("--scan");
        var recordPath = GetStringArg(args, "--record");
        var survey = args.Contains("--survey");
        var draftsPath = GetStringArg(args, "--drafts") ?? "data/survey-drafts.json";

        // 离线模式：只检查草稿几何，不碰 UDP。
        if (args.Contains("--check"))
            return DraftChecker.Run(draftsPath, write: args.Contains("--write"));

        Console.WriteLine("FH6 遥测探针");
        Console.WriteLine($"监听 UDP 端口 {port}");
        Console.WriteLine();
        Console.WriteLine("游戏内设置：设置 → HUD 与游戏体验 → Data Out");
        Console.WriteLine("  Data Out：开");
        Console.WriteLine("  IP：127.0.0.1（游戏和本程序在同一台机器上）");
        Console.WriteLine($"  端口：{port}");
        Console.WriteLine("本机 IPv4：" + string.Join(", ", TelemetryReceiver.LocalIPv4Addresses()));
        Console.WriteLine();
        Console.WriteLine("等待数据包……（游戏需要处于开车状态，暂停或菜单里通常不发包）");
        Console.WriteLine("Ctrl+C 退出");
        Console.WriteLine();

        using var receiver = new TelemetryReceiver(port);
        var scanner = new PacketScanner();
        FileStream? recording = null;

        if (recordPath is not null)
        {
            recording = File.Create(recordPath);
            Console.WriteLine($"原始包写入：{Path.GetFullPath(recordPath)}");
            Console.WriteLine("（格式：每包 4 字节小端长度 + 包体）");
            Console.WriteLine();
        }

        receiver.UnknownPacketLength += length =>
            Console.WriteLine($"[!] 收到未知长度的包：{length} 字节。已知：311 (FM7) / 324 (FH4/FH5)。自动进入扫描模式。");

        receiver.PacketReceived += packet =>
        {
            scanner.Observe(packet);
            if (recording is not null)
            {
                Span<byte> header = stackalloc byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, packet.Length);
                lock (recording)
                {
                    recording.Write(header);
                    recording.Write(packet);
                }
            }
        };

        try
        {
            receiver.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[x] 无法监听端口 {port}：{ex.Message}");
            Console.WriteLine("    端口可能被别的程序占用，换一个：TelemetryProbe --port 5301");
            return 1;
        }

        using var quit = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            quit.Cancel();
        };

        SurveySession? session = null;
        using var hotkeys = new GlobalHotkeys();

        if (survey)
        {
            session = new SurveySession(draftsPath);
            hotkeys.Bind(VirtualKeys.F8, () => session.MarkCenter(receiver.LatestState));
            hotkeys.Bind(VirtualKeys.F9, () => session.AddBoundaryPoint(receiver.LatestState));
            hotkeys.Bind(VirtualKeys.F7, session.UndoBoundaryPoint);
            hotkeys.Bind(VirtualKeys.F10, session.Complete);

            var failed = hotkeys.Start();
            if (failed.Count > 0)
                Console.WriteLine($"[!] 这些热键注册失败（被别的程序占用了）：{string.Join(", ", failed.Select(vk => $"0x{vk:X2}"))}");

            Console.WriteLine($"勘景模式：草稿写入 {Path.GetFullPath(draftsPath)}");
            Console.WriteLine();
        }

        LocationResolver? resolver = null;
        LocationTracker? tracker = null;

        if (args.Contains("--locate"))
        {
            var locations = LocationStore.Load(draftsPath);
            resolver = new LocationResolver(locations);
            tracker = new LocationTracker();

            Console.WriteLine($"定位模式：{locations.Count} 个地点，来自 {Path.GetFullPath(draftsPath)}");
            foreach (var location in locations)
            {
                var parent = location.ParentId is null ? "" : $"  ⊂ {location.ParentId}";
                Console.WriteLine($"  {location.Name} [{location.Type}] 优先级 {location.Priority}{parent}");
            }
            Console.WriteLine();
        }

        try
        {
            RenderLoop(receiver, scanner, session, resolver, tracker, forceScan, quit.Token);
        }
        catch (OperationCanceledException)
        {
            // 正常退出。
        }
        finally
        {
            recording?.Dispose();
        }

        Console.WriteLine();
        Console.WriteLine($"共收到 {receiver.PacketCount} 个包。");
        return 0;
    }

    private static void RenderLoop(
        TelemetryReceiver receiver,
        PacketScanner scanner,
        SurveySession? session,
        LocationResolver? resolver,
        LocationTracker? tracker,
        bool forceScan,
        CancellationToken cancellationToken)
    {
        var sawAnyPacket = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            Thread.Sleep(200);

            if (receiver.PacketCount == 0)
                continue;

            // 等待输入名字时暂停刷新，把控制台让给 ReadLine，否则会把用户正在打的字冲掉。
            if (session is { AwaitingName: true })
            {
                PromptForName(session);
                sawAnyPacket = false;
                continue;
            }

            if (!sawAnyPacket)
            {
                sawAnyPacket = true;
                if (!Console.IsOutputRedirected)
                    Console.Clear();
            }

            var layout = receiver.Layout;
            var useScan = forceScan || layout is null;

            if (Console.IsOutputRedirected)
                Console.WriteLine("----");
            else
                Console.SetCursorPosition(0, 0);

            if (useScan)
                RenderScan(receiver, scanner);
            else
                RenderWatch(receiver, layout!);

            if (resolver is not null && tracker is not null)
                RenderLocate(receiver, resolver, tracker);

            if (session is not null)
                RenderSurvey(session);
        }
    }

    private static void RenderLocate(
        TelemetryReceiver receiver,
        LocationResolver resolver,
        LocationTracker tracker)
    {
        WriteLinePadded("");
        WriteLinePadded("── 定位 ──────────────────────────────────────");

        var vehicle = receiver.LatestState;
        if (vehicle is null || !vehicle.IsRaceOn)
        {
            WriteLinePadded("游戏不在驾驶状态，坐标无效。");
            WriteLinePadded("");
            WriteLinePadded("");
            WriteLinePadded("");
            return;
        }

        var now = DateTime.UtcNow;
        var raw = resolver.Resolve(vehicle.PositionX, vehicle.PositionZ);
        tracker.Update(raw, now);

        WriteLinePadded($"瞬时 : {raw}");
        WriteLinePadded($"确认 : {tracker.Stable}");

        var pending = tracker.Pending;
        WriteLinePadded(pending is null
            ? ""
            : $"待确认 : {pending}   还需 {tracker.RemainingToConfirm(now).TotalSeconds:F1} 秒");

        var primary = tracker.Stable.Primary;
        WriteLinePadded(primary is null
            ? ""
            : $"主要地点 : {primary.Name}   距中心点 {primary.DistanceFrom(vehicle.PositionX, vehicle.PositionZ):F0} m");
    }

    /// <summary>
    /// F10 之后在这里给地点命名。切回控制台窗口输入，回车保存，采集随即继续。
    /// </summary>
    private static void PromptForName(SurveySession session)
    {
        if (!Console.IsOutputRedirected)
            Console.Clear();

        Console.WriteLine("── 保存地点 ──────────────────────────────────");
        Console.WriteLine(session.Message);
        Console.WriteLine();
        Console.WriteLine("输入：id 名称 [region|landmark]");
        Console.WriteLine("例如：shibuya_crossing 涩谷十字路口 landmark");
        Console.WriteLine("      （不写类型默认 landmark；直接回车用自动 id；输入 cancel 退回继续加点）");
        Console.WriteLine();
        Console.Write("> ");

        session.ApplyName(Console.ReadLine());

        Console.WriteLine();
        Console.WriteLine(session.Message);
        Console.WriteLine("回到游戏继续采集下一个地点。");
        Thread.Sleep(1500);
    }

    private static void RenderSurvey(SurveySession session)
    {
        var draft = session.Current;

        WriteLinePadded("");
        WriteLinePadded("── 勘景 ──────────────────────────────────────");
        WriteLinePadded("F8 记中心点   F9 加边界点   F7 撤销   F10 完成并保存");
        WriteLinePadded("");

        var center = draft.Center is { } c
            ? $"X {c.X,9:F1}   Z {c.Z,9:F1}   海拔 {draft.CenterY,6:F0}"
            : "（未记录）";
        WriteLinePadded($"当前中心点 : {center}");
        WriteLinePadded($"当前边界点 : {draft.Boundary.Count} 个" +
            (draft.Boundary.Count > 0 && draft.Boundary.Count < 3 ? "（至少要 3 个才成多边形）" : ""));

        if (draft.Boundary.Count > 0)
        {
            var last = draft.Boundary[^1];
            WriteLinePadded($"最后一个   : X {last.X,9:F1}   Z {last.Z,9:F1}");
        }
        else
        {
            WriteLinePadded("");
        }

        WriteLinePadded("");
        WriteLinePadded($"提示 : {session.Message}");
        WriteLinePadded("");

        var completed = session.Completed;
        WriteLinePadded($"已保存草稿 : {completed.Count} 个" +
            (completed.Count > 0 ? $"（最近：{completed[^1].Id}）" : ""));
    }

    private static void RenderWatch(TelemetryReceiver receiver, ForzaPacketLayout layout)
    {
        var state = receiver.LatestState;

        WriteLinePadded($"布局：{layout.Name}   {layout.PacketLength} 字节   坐标偏移 {layout.PositionXOffset}/{layout.PositionYOffset}/{layout.PositionZOffset}");
        WriteLinePadded($"收包：{receiver.PacketCount}   {(receiver.IsStale ? "过期（游戏可能已暂停）" : "实时")}");
        WriteLinePadded("");

        if (state is null)
        {
            WriteLinePadded("尚未解析出车辆状态。");
            return;
        }

        WriteLinePadded($"IsRaceOn : {(state.IsRaceOn ? "是" : "否（暂停 / 菜单）")}");
        WriteLinePadded($"游戏时间 : {state.TimestampMs} ms");
        WriteLinePadded("");
        WriteLinePadded($"X : {state.PositionX,12:F2}");
        WriteLinePadded($"Y : {state.PositionY,12:F2}");
        WriteLinePadded($"Z : {state.PositionZ,12:F2}");
        WriteLinePadded($"速度 : {state.Speed,9:F2} m/s   ({state.Speed * 3.6f:F1} km/h)");
        WriteLinePadded("");
        WriteLinePadded("坐标是否随开车连续变化？速度是否和车速表对得上？对得上就说明布局正确。");
    }

    private static void RenderScan(TelemetryReceiver receiver, PacketScanner scanner)
    {
        var snapshot = scanner.Snapshot();
        if (snapshot.Length == 0)
            return;

        WriteLinePadded($"扫描模式   包长度 {scanner.PacketLength} 字节   收包 {receiver.PacketCount}");
        WriteLinePadded("把每个 4 字节偏移当作 float 显示。* = 最近在变。");
        WriteLinePadded("找“开车时连续平滑变化、停车时不动、量级在几百到几万”的三个相邻偏移 —— 那就是 X/Y/Z。");
        WriteLinePadded("紧跟其后的通常是速度（m/s）。");
        WriteLinePadded("");

        const int columns = 3;
        for (var row = 0; row < (snapshot.Length + columns - 1) / columns; row++)
        {
            var line = "";
            for (var col = 0; col < columns; col++)
            {
                var i = row + col * ((snapshot.Length + columns - 1) / columns);
                if (i >= snapshot.Length)
                    continue;

                var f = snapshot[i];
                var mark = f.Changing ? "*" : " ";
                line += $"{f.Offset,4}{mark}{FormatFloat(f.Value),14}   ";
            }
            WriteLinePadded(line);
        }
    }

    private static string FormatFloat(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return "--";
        var magnitude = Math.Abs(value);
        if (magnitude != 0 && (magnitude < 1e-3f || magnitude > 1e7f))
            return value.ToString("E2");
        return value.ToString("F2");
    }

    private static void WriteLinePadded(string text)
    {
        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(text);
            return;
        }

        var width = Math.Max(Console.WindowWidth - 1, 20);
        if (text.Length > width)
            text = text[..width];
        Console.WriteLine(text.PadRight(width));
    }

    private static string? GetStringArg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int? GetIntArg(string[] args, string name) =>
        int.TryParse(GetStringArg(args, name), out var value) ? value : null;
}
