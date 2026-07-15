using System.Runtime.InteropServices;

namespace HorizonGuide.Core.Input;

/// <summary>
/// 全局热键。游戏在前台占着焦点，控制台读不到按键，只能用 RegisterHotKey。
///
/// hWnd 传 IntPtr.Zero 时，WM_HOTKEY 会投递到注册它的那个线程的消息队列，
/// 所以注册和消息循环必须在同一个线程上。
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const int WmHotkey = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg msg, IntPtr hWnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    private readonly Dictionary<int, (uint VirtualKey, Action Handler)> _bindings = [];
    private readonly List<int> _registered = [];
    private Thread? _pump;
    private uint _threadId;
    private int _nextId = 1;

    /// <summary>在 <see cref="Start"/> 之前绑定。vk 是虚拟键码，例如 F8 = 0x77。</summary>
    public void Bind(uint virtualKey, Action handler) =>
        _bindings[_nextId++] = (virtualKey, handler);

    /// <summary>返回注册失败的热键 id 列表（通常是被别的程序占用了）。</summary>
    public IReadOnlyList<uint> Start()
    {
        var failed = new List<uint>();
        using var ready = new ManualResetEventSlim();

        _pump = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();

            foreach (var (id, (vk, _)) in _bindings)
            {
                if (RegisterHotKey(IntPtr.Zero, id, 0, vk))
                    _registered.Add(id);
                else
                    failed.Add(vk);
            }

            ready.Set();

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.Message != WmHotkey)
                    continue;

                var id = msg.WParam.ToInt32();
                if (_bindings.TryGetValue(id, out var binding))
                    binding.Handler();
            }

            foreach (var id in _registered)
                UnregisterHotKey(IntPtr.Zero, id);
        })
        {
            IsBackground = true,
            Name = "GlobalHotkeys",
        };

        _pump.Start();
        ready.Wait();
        return failed;
    }

    public void Dispose()
    {
        if (_threadId != 0)
            PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        _pump?.Join(TimeSpan.FromSeconds(1));
    }
}

public static class VirtualKeys
{
    public const uint F6 = 0x75;
    public const uint F7 = 0x76;
    public const uint F8 = 0x77;
    public const uint F9 = 0x78;
    public const uint F10 = 0x79;
    public const uint F11 = 0x7A;
}
