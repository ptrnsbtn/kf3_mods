// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Modding;

namespace Kf3.Mods.Crosshair;

/// <summary>
/// A crosshair over the centre of the picture, and the play gate it draws
/// under. The panel is Crosshair.cs, registered on the UI thread the first
/// frame the way kf3csmove's HUD and the debug mod's panels are.
///
/// The gate is the reason this is its own mod: max HP non-zero (a character
/// exists), the action state 0 (the in-game menu runs under examine's state,
/// death and cast executions have their own; charging a spell is normal-state
/// input and stays 0) and the play-tick counter 0x801B2580 still changing
/// (stage 4 increments it once per player tick; the title loop, the menu, a
/// loading screen and a cutscene that takes the update over all stop it). A
/// bare max-HP gate is not enough -- the word survives in RAM through all of
/// those, which is what an earlier cut of this gate drew the crosshair over.
/// The three are sampled on the game thread from the pad poll, so the UI
/// thread only ever reads a volatile bool.
///
/// With kf3csmove on its own HUD draws a crosshair too; turn one of the two
/// off in its settings or the two draw on top of each other.
/// </summary>
public sealed class CrosshairMod : IMod
{
    const string EnabledKey = "kf3.crosshair.enabled";
    const string ScaleKey   = "kf3.crosshair.scale";

    const string Version = "0.1";

    /// <summary>The player's max HP: non-zero means a character exists in an
    /// area.</summary>
    const uint MaxHp = 0x801B24FA;       // u16

    /// <summary>The action state: 0 is normal play; the in-game menu runs
    /// under examine's state, death is 0x11, and a cast's execution has its
    /// own (charging a spell is normal-state input and stays 0).</summary>
    const uint ActionState = 0x801B25E5; // u8

    /// <summary>The play-tick counter, incremented once at the end of stage 4
    /// (the player's tick). It stops wherever the player tick stops -- the
    /// title loop, the in-game menu, a loading screen, a cutscene that takes
    /// the update over -- so "still changing" is the liveness half of the
    /// play gate.</summary>
    const uint PlayTick = 0x801B2580;    // u32

    /// <summary>How stale the play-tick counter may be and still count as
    /// play: the counter moves every frame the player tick runs, so a third
    /// of a second is many frames of slack for a hitch.</summary>
    const long PlayTickWindowMs = 300;

    /// <summary>Whether the player tick is live and normal this frame,
    /// written on the game thread from the pad poll and read by the
    /// crosshair panel on the UI thread. The crosshair is only for play;
    /// everything else stays unmarked.</summary>
    internal static volatile bool InGame;

    static uint _lastTick;
    static long _lastTickChange;

    static bool _enabled = true;
    static float _scale = 1f;
    static float _scalePane = 1f;

    Action<PadReadEvent>? _onPad;
    Action<VSyncEvent>? _onVSync;
    bool _panelRegistered;

    public void OnLoad()
    {
        var view = RecompOne.Runtime.Runtime.View;
        _enabled = view.GetBool(EnabledKey, true);
        _scale   = view.GetFloat(ScaleKey, 1f);
        _scalePane = _scale;
        Crosshair.Enabled = _enabled;
        Crosshair.Scale = _scale;

        _onPad = OnPadRead;
        Event.AddListener(_onPad);

        // The panel draws on the UI thread; OnLoad may run on a worker.
        _onVSync = _ => RegisterPanel();
        Event.AddListener(_onVSync);

        Console.WriteLine($"[kf3crosshair] loaded; scale {_scale:0.##}");
    }

    public void OnUnload()
    {
        if (_onPad != null) { Event.RemoveListener(_onPad); _onPad = null; }
        if (_onVSync != null) { Event.RemoveListener(_onVSync); _onVSync = null; }
        if (_panelRegistered)
        {
            PanelManager.Unregister(Crosshair.Instance);
            _panelRegistered = false;
        }
    }

    public void DrawSettings()
    {
        bool on = _enabled;
        if (ImGuiNET.ImGui.Checkbox("Crosshair on", ref on))
        {
            _enabled = on;
            Crosshair.Enabled = on;
            RecompOne.Runtime.Runtime.View.SetBool(EnabledKey, on);
            RecompOne.Runtime.Runtime.SaveView();
        }

        if (ImGuiNET.ImGui.SliderFloat("Crosshair scale", ref _scalePane, 0.5f, 3f, "x%.2f"))
        {
            _scale = _scalePane;
            Crosshair.Scale = _scale;
            RecompOne.Runtime.Runtime.View.SetFloat(ScaleKey, _scale);
            RecompOne.Runtime.Runtime.SaveView();
        }

        ImGuiNET.ImGui.TextDisabled(InGame
            ? "play is live: the crosshair shows"
            : "not in play: title, menus, loading and cutscenes stay unmarked");
    }

    void RegisterPanel()
    {
        if (_panelRegistered) return;
        _panelRegistered = true;

        foreach (var p in PanelManager.Panels)
            if (ReferenceEquals(p, Crosshair.Instance)) return;

        PanelManager.Register(Crosshair.Instance);
    }

    /// <summary>One PAD_dr poll of port 0, purely as a game-thread heartbeat:
    /// the play gate's three words are read here, never from the UI
    /// thread.</summary>
    void OnPadRead(PadReadEvent e)
    {
        if (e.Port != 0) return;

        if (RecompOne.Runtime.Runtime.Mem is RecompOne.Runtime.Memory.IMemory mem)
        {
            long now = Environment.TickCount64;
            uint stageTick = mem.ReadU32(PlayTick);
            if (stageTick != _lastTick)
            {
                _lastTick = stageTick;
                _lastTickChange = now;
            }
            InGame = mem.ReadU16(MaxHp) != 0
                  && mem.ReadU8(ActionState) == 0
                  && now - _lastTickChange < PlayTickWindowMs;
        }
    }
}
