using System.Runtime.InteropServices;

namespace SoundDeck_App;

internal sealed class TrayIconService : IDisposable
{
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint WmApp = 0x8000;
    private const uint CallbackMessage = WmApp + 42;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint MfString = 0;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private readonly SubclassProc _windowProc;
    private readonly nint _window;
    private readonly nint _icon;
    private NotifyIconData _data;

    public TrayIconService(nint window, string iconPath)
    {
        _window = window;
        _windowProc = WindowProc;
        _icon = LoadImage(0, iconPath, 1, 0, 0, 0x0010);
        _data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = window,
            Id = 1,
            Flags = NifMessage | NifIcon | NifTip,
            CallbackMessage = CallbackMessage,
            Icon = _icon,
            Tip = "SoundDeck"
        };
        SetWindowSubclass(window, _windowProc, 42, 0);
        ShellNotifyIcon(NimAdd, ref _data);
    }

    public event EventHandler? OpenRequested;
    public event EventHandler? ToggleMuteRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? ExitRequested;

    private nint WindowProc(nint window, uint message, nint wParam, nint lParam, nuint id, nint data)
    {
        if (message == CallbackMessage)
        {
            var mouseMessage = unchecked((uint)lParam.ToInt64());
            if (mouseMessage == WmLButtonDblClk)
                OpenRequested?.Invoke(this, EventArgs.Empty);
            else if (mouseMessage == WmRButtonUp)
                ShowMenu();
            return 0;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, MfString, 1, "Abrir SoundDeck");
        AppendMenu(menu, MfString, 2, "Silenciar/activar micrófono");
        AppendMenu(menu, MfString, 3, "Detener sonido");
        AppendMenu(menu, MfString, 4, "Salir");
        GetCursorPos(out var point);
        SetForegroundWindow(_window);
        var command = TrackPopupMenuEx(menu, TpmRightButton | TpmReturnCmd, point.X, point.Y, _window, 0);
        DestroyMenu(menu);
        switch (command)
        {
            case 1: OpenRequested?.Invoke(this, EventArgs.Empty); break;
            case 2: ToggleMuteRequested?.Invoke(this, EventArgs.Empty); break;
            case 3: StopRequested?.Invoke(this, EventArgs.Empty); break;
            case 4: ExitRequested?.Invoke(this, EventArgs.Empty); break;
        }
    }

    public void Dispose()
    {
        ShellNotifyIcon(NimDelete, ref _data);
        RemoveWindowSubclass(_window, _windowProc, 42);
        if (_icon != 0)
            DestroyIcon(_icon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public uint InfoFlags;
        public Guid GuidItem;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    private delegate nint SubclassProc(nint window, uint message, nint wParam, nint lParam, nuint id, nint data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    private static bool ShellNotifyIcon(uint message, ref NotifyIconData data) => Shell_NotifyIcon(message, ref data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint load);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, uint id, string text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint id, nint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);
}
