using System.Runtime.InteropServices;
using NAudio.Midi;
using SoundDeck.Core;

namespace SoundDeck.Audio;

public sealed class MidiInputService : IMidiInputService
{
    private MidiIn? _input;
    public event EventHandler<int>? NoteReceived;

    public IReadOnlyList<string> GetDeviceNames() =>
        Enumerable.Range(0, MidiIn.NumberOfDevices)
            .Select(index => MidiIn.DeviceInfo(index).ProductName)
            .ToArray();

    public void Connect(int deviceIndex)
    {
        Disconnect();
        if (deviceIndex < 0 || deviceIndex >= MidiIn.NumberOfDevices)
            throw new ArgumentOutOfRangeException(nameof(deviceIndex));

        _input = new MidiIn(deviceIndex);
        _input.MessageReceived += OnMessageReceived;
        _input.ErrorReceived += (_, _) => { };
        _input.Start();
    }

    private void OnMessageReceived(object? sender, MidiInMessageEventArgs args)
    {
        if (args.MidiEvent is NoteOnEvent { Velocity: > 0 } note)
            NoteReceived?.Invoke(this, note.NoteNumber);
    }

    public void Disconnect()
    {
        if (_input is null)
            return;
        _input.Stop();
        _input.MessageReceived -= OnMessageReceived;
        _input.Dispose();
        _input = null;
    }

    public void Dispose() => Disconnect();
}

public sealed class GlobalHotkeyService : IHotkeyService
{
    private const uint WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private readonly Dictionary<int, Guid?> _registrations = [];
    private readonly SubclassProc _windowProc;
    private nint _window;
    private int _nextId = 100;

    public GlobalHotkeyService() => _windowProc = WindowProc;

    public event EventHandler<Guid>? SoundRequested;
    public event EventHandler? StopRequested;

    public void Attach(nint windowHandle)
    {
        if (_window == windowHandle)
            return;
        if (_window != 0)
            RemoveWindowSubclass(_window, _windowProc, 1);
        _window = windowHandle;
        if (_window != 0 && !SetWindowSubclass(_window, _windowProc, 1, 0))
            throw new InvalidOperationException("No se pudo conectar el receptor de atajos.");
    }

    public bool RegisterSound(Guid soundId, string gesture) => Register(soundId, gesture);

    public bool RegisterStop(string gesture) => Register(null, gesture);

    private bool Register(Guid? soundId, string gesture)
    {
        if (_window == 0)
            return false;
        var (modifiers, key) = ParseGesture(gesture);
        var id = _nextId++;
        if (!RegisterHotKey(_window, id, modifiers | ModNoRepeat, key))
            return false;
        _registrations[id] = soundId;
        return true;
    }

    public void Clear()
    {
        if (_window != 0)
        {
            foreach (var id in _registrations.Keys)
                UnregisterHotKey(_window, id);
        }
        _registrations.Clear();
        _nextId = 100;
    }

    private nint WindowProc(nint window, uint message, nint wParam, nint lParam, nuint id, nint data)
    {
        if (message == WmHotkey && _registrations.TryGetValue(wParam.ToInt32(), out var soundId))
        {
            if (soundId.HasValue)
                SoundRequested?.Invoke(this, soundId.Value);
            else
                StopRequested?.Invoke(this, EventArgs.Empty);
            return 0;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    private static (uint Modifiers, uint Key) ParseGesture(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
            throw new FormatException("El atajo está vacío.");
        var parts = gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        uint modifiers = 0;
        uint key = 0;
        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL": modifiers |= ModControl; break;
                case "ALT": modifiers |= ModAlt; break;
                case "SHIFT": modifiers |= ModShift; break;
                case "WIN":
                case "WINDOWS": modifiers |= ModWin; break;
                default: key = ParseKey(part); break;
            }
        }
        if (key == 0)
            throw new FormatException("El atajo necesita una tecla.");
        return (modifiers, key);
    }

    private static uint ParseKey(string value)
    {
        var upper = value.ToUpperInvariant();
        if (upper.Length == 1 && char.IsLetterOrDigit(upper[0]))
            return upper[0];
        if (upper.StartsWith('F') && int.TryParse(upper[1..], out var number) && number is >= 1 and <= 24)
            return (uint)(0x70 + number - 1);
        return upper switch
        {
            "SPACE" or "ESPACIO" => 0x20,
            "ESC" or "ESCAPE" => 0x1B,
            "PAUSE" => 0x13,
            "MEDIASTOP" => 0xB2,
            _ => throw new FormatException($"Tecla no compatible: {value}")
        };
    }

    public void Dispose()
    {
        Clear();
        if (_window != 0)
            RemoveWindowSubclass(_window, _windowProc, 1);
        _window = 0;
    }

    private delegate nint SubclassProc(nint window, uint message, nint wParam, nint lParam, nuint id, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint id, nint data);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint id);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);
}
