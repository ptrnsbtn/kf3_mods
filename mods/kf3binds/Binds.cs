// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Modding;
using Silk.NET.Input;
// The runtime's Events namespace has its own MouseButton; the host window's
// IsMouseButtonDown takes Silk's.
using MouseButton = Silk.NET.Input.MouseButton;

namespace Kf3.Mods.Binds;

/// <summary>
/// Keyboard and mouse bindings for the game's pad buttons, from a commented
/// config beside the mod (mods/kf3binds/bindings.cfg, or the path in
/// KF3_BINDS_CONFIG). A held key or mouse button holds its button; the wheel
/// fires one press per notch, held for HoldMs so the game's frame poll cannot
/// miss it. One button may take several inputs -- repeat its line -- so Cross
/// can be the wheel and a thumb button at once. Injection is through
/// PAD_dr's event, the same path KF3_AUTOPAD and the shell's press use, so a
/// bound key reaches the game's menus and its own action code exactly as the
/// pad button would.
///
/// The config is re-read whenever its file changes (checked about once a
/// second), so a rebinding needs no restart.
///
/// The wheel is a shared drain (HostWindow.TakeMouseWheel): whoever reads it
/// first gets the notches. This mod drains it only while a wheel binding
/// exists and only while no runtime popup is open (the UI owns the wheel
/// there); with kf3csmove active its wheel-jump competes for the same
/// notches, which the config's comments spell out.
/// </summary>
internal sealed class Binds : IMod
{
    const string Version = "0.4";

    /// <summary>Where the config lives: beside the mod, overridable for a
    /// config kept elsewhere.</summary>
    static string ConfigPath =>
        Environment.GetEnvironmentVariable("KF3_BINDS_CONFIG")
        ?? Path.GetFullPath(Path.Combine("mods", "kf3binds", "bindings.cfg"));

    enum SourceKind { Key, Mouse, WheelUp, WheelDown }

    readonly struct Source
    {
        internal readonly SourceKind Kind;
        internal readonly Key Key;
        internal readonly MouseButton Button;

        private Source(SourceKind kind, Key key, MouseButton button)
        {
            Kind = kind; Key = key; Button = button;
        }

        internal static Source OfKey(Key k) => new(SourceKind.Key, k, default);
        internal static Source OfMouse(MouseButton b) => new(SourceKind.Mouse, default, b);
        internal static readonly Source WheelUp = new(SourceKind.WheelUp, default, default);
        internal static readonly Source WheelDown = new(SourceKind.WheelDown, default, default);
    }

    sealed class Binding
    {
        internal ushort Bit;
        internal Source Source;
        internal long HoldUntil;       // wheel presses, TickCount64
    }

    static readonly Dictionary<string, ushort> Buttons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Select"] = Controller.Select, ["Start"] = Controller.Start,
        ["Cross"] = Controller.Cross, ["Circle"] = Controller.Circle,
        ["Square"] = Controller.Square, ["Triangle"] = Controller.Triangle,
        ["L1"] = Controller.L1, ["R1"] = Controller.R1,
        ["L2"] = Controller.L2, ["R2"] = Controller.R2,
        ["L3"] = Controller.L3, ["R3"] = Controller.R3,
        ["Up"] = Controller.Up, ["Down"] = Controller.Down,
        ["Left"] = Controller.Left, ["Right"] = Controller.Right,
    };

    readonly List<Binding> _bindings = [];
    bool _useWheel;
    int _holdMs = 120;
    DateTime _cfgWrite;
    Action<PadReadEvent>? _onPad;

    public void OnLoad()
    {
        ReadConfig();

        _onPad = OnPadRead;
        Event.AddListener(_onPad);

        Console.WriteLine($"[kf3binds] loaded; {_bindings.Count} binding(s) from {ConfigPath}");
    }

    public void OnUnload()
    {
        if (_onPad != null) { Event.RemoveListener(_onPad); _onPad = null; }
    }

    public void DrawSettings()
    {
        ImGuiNET.ImGui.Text($"config: {ConfigPath}");
        ImGuiNET.ImGui.Text($"{_bindings.Count} binding(s), hold {_holdMs} ms");
    }

    /// <summary>One PAD_dr poll of port 0: press what the config says is held.
    /// The event's button word is active low with its two bytes swapped
    /// against Controller's layout -- the same transform KF3_AUTOPAD and the
    /// shell's press make.</summary>
    void OnPadRead(PadReadEvent e)
    {
        if (e.Port != 0) return;

        // A runtime popup owns the input while it is open: no draining, no
        // pressing, so the mods panel and its ilk still scroll and click.
        if (PopupManager.AnyOpen) return;

        MaybeReload();

        float wheel = 0f;
        if (_useWheel)
        {
            wheel = HostWindow.TakeMouseWheel();
            long now = Environment.TickCount64;
            foreach (var b in _bindings)
            {
                if (b.Source.Kind == SourceKind.WheelDown && wheel < 0f)
                    b.HoldUntil = now + _holdMs;
                else if (b.Source.Kind == SourceKind.WheelUp && wheel > 0f)
                    b.HoldUntil = now + _holdMs;
            }
        }

        ushort held = 0;
        long tick = Environment.TickCount64;
        foreach (var b in _bindings)
        {
            bool down = b.Source.Kind switch
            {
                SourceKind.Key => HostWindow.IsKeyDown(b.Source.Key),
                SourceKind.Mouse => HostWindow.IsMouseButtonDown(b.Source.Button),
                _ => tick < b.HoldUntil,
            };
            if (down) held |= b.Bit;
        }

        if (held != 0)
            e.Buttons &= (ushort)~(ushort)((held >> 8) | (held << 8));
    }

    /// <summary>Re-read the config when its file changes, about once a
    /// second, so a rebinding needs no restart.</summary>
    void MaybeReload()
    {
        long now = Environment.TickCount64;
        if (now - _lastCheck < 1000) return;
        _lastCheck = now;

        try
        {
            var write = File.GetLastWriteTimeUtc(ConfigPath);
            if (write != _cfgWrite) ReadConfig();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    long _lastCheck;

    void ReadConfig()
    {
        _bindings.Clear();
        _useWheel = false;
        _holdMs = 120;

        string path = ConfigPath;
        try { _cfgWrite = File.GetLastWriteTimeUtc(path); }
        catch { _cfgWrite = default; }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"[kf3binds] no config at {path}, no bindings");
            return;
        }

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("#"))
                continue;

            int eq = line.IndexOf('=');
            if (eq < 0)
            {
                Console.Error.WriteLine($"[kf3binds] {path}: no '=' on '{line}'");
                continue;
            }

            var lhs = line[..eq].Trim();
            var rhs = line[(eq + 1)..].Trim();

            // The scalar setting.
            if (lhs.Equals("HoldMs", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(rhs, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms) && ms > 0)
                    _holdMs = ms;
                else
                    Console.Error.WriteLine($"[kf3binds] {path}: bad HoldMs '{rhs}'");
                continue;
            }

            // A binding: pad button = input.
            if (!Buttons.TryGetValue(lhs, out ushort bit))
            {
                Console.Error.WriteLine($"[kf3binds] {path}: unknown button '{lhs}'");
                continue;
            }

            if (!TrySource(rhs, out var src))
            {
                Console.Error.WriteLine($"[kf3binds] {path}: unknown input '{rhs}'");
                continue;
            }

            _bindings.Add(new Binding { Bit = bit, Source = src });
            if (src.Kind is SourceKind.WheelUp or SourceKind.WheelDown)
                _useWheel = true;
        }
    }

    /// <summary>One input name: WheelUp / WheelDown / Mouse1..Mouse9 / any
    /// Silk.NET key name, case-insensitive.</summary>
    static bool TrySource(string name, out Source src)
    {
        src = default;
        if (name.Equals("WheelUp", StringComparison.OrdinalIgnoreCase)) { src = Source.WheelUp; return true; }
        if (name.Equals("WheelDown", StringComparison.OrdinalIgnoreCase)) { src = Source.WheelDown; return true; }

        if (name.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out int mb))
        {
            // Mouse1..3 the main buttons (Silk: Left/Right/Middle), Mouse4..
            // the extra ones in driver order -- Silk's own names, Button4 up,
            // which is where Mouse4/Mouse5 land and where SDL's button 4/5
            // numbering comes from.
            if (mb is < 1 or > 12) return false;
            MouseButton button = mb switch
            {
                1 => MouseButton.Left,
                2 => MouseButton.Right,
                3 => MouseButton.Middle,
                4 => MouseButton.Button4,
                5 => MouseButton.Button5,
                6 => MouseButton.Button6,
                7 => MouseButton.Button7,
                8 => MouseButton.Button8,
                9 => MouseButton.Button9,
                10 => MouseButton.Button10,
                11 => MouseButton.Button11,
                _ => MouseButton.Button12,
            };
            src = Source.OfMouse(button);
            return true;
        }

        // Enum.Parse also accepts a raw number, which would silently bind an
        // arbitrary key; a name here is a name.
        if (name.Length == 0 || char.IsDigit(name[0])) return false;

        try
        {
            var key = Enum.Parse<Key>(name, ignoreCase: true);
            if (key == Key.Unknown) return false;
            src = Source.OfKey(key);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
