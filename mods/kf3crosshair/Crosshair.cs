// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Host.Window;

namespace Kf3.Mods.Crosshair;

/// <summary>
/// The crosshair: four arms with a hole in the middle over the centre of the
/// picture, drawn on the foreground draw list so it sits over the Output
/// panel's picture, as the CS-move HUD does. It is an IFloatingPanel rather
/// than IMod.DrawSettings because that only draws while the Mods popup is
/// open, and a crosshair behind a modal is not a crosshair.
///
/// It draws only while play is live (<see cref="CrosshairMod.InGame"/>: the
/// player tick running with a character in normal play -- max HP, action
/// state 0 and the play-tick counter still moving) and no runtime popup is
/// open in front of it: the title screen, its menus, the in-game menu,
/// loading screens and cutscenes stay unmarked. The mod's settings turn it
/// off and scale it. The shape and the double pass (black outline under white
/// arms) are kf3csmove's Hud's, which is where the crosshair itself comes
/// from; with both mods on, turn one of them off in its settings or the two
/// draw on top of each other.
/// </summary>
internal sealed class Crosshair : IFloatingPanel
{
    internal static readonly Crosshair Instance = new();

    public string Name => "Crosshair";
    public string TitleKey => Name;

    // A draw hook, not a window: nothing to dock, nothing to close.
    public bool IsOpen { get => true; set { } }

    internal static bool Enabled = true;
    internal static float Scale = 1f;

    // Picture-independent screen units, multiplied by Scale: a gap of 4
    // either side of the centre, arms 7 long beyond it, 2 thick.
    const float Gap0 = 4f;
    const float Arm0 = 7f;
    const float Thick0 = 2f;

    const uint CrossColor = 0xFFFFFFFFu;
    const uint OutlineColor = 0x00000000u;

    public void Draw()
    {
        if (!Enabled) return;
        if (!CrosshairMod.InGame) return;
        if (!OutputView.Valid || PopupManager.AnyOpen) return;

        var dl = ImGui.GetForegroundDrawList();
        Vector2 centre = (OutputView.Min + OutputView.Max) * 0.5f;

        float s = Scale <= 0f ? 1f : Scale;
        float gap = Gap0 * s, arm = Arm0 * s, thick = Thick0 * s;

        Arms(dl, centre, gap, arm, thick + 2f, OutlineColor);
        Arms(dl, centre, gap, arm, thick, CrossColor);
    }

    static void Arms(ImDrawListPtr dl, Vector2 c, float gap, float arm, float thick, uint col)
    {
        var inR = new Vector2(0f, gap);
        var outR = new Vector2(0f, gap + arm);
        var inL = new Vector2(gap, 0f);
        var outL = new Vector2(gap + arm, 0f);

        dl.AddLine(c - outR, c - inR, col, thick);      // up
        dl.AddLine(c + inR, c + outR, col, thick);      // down
        dl.AddLine(c - outL, c - inL, col, thick);      // left
        dl.AddLine(c + inL, c + outL, col, thick);      // right
    }
}
