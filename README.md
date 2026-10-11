# Key Binds and Crosshair for Verdite3

Two small runtime-loaded mods for [Verdite3](https://github.com/Voicedrew11/verdite3),
the static recompilation of *King's Field III* (the Japanese numbering; the
North American release, `SLUS-00255`). They need no build and no recompile:
the game compiles each mod on first launch and remembers it.

| mod | folder | id | what |
|---|---|---|---|
| Key Binds | `mods/kf3binds` | `kf3.binds` | the game's pad buttons on keyboard and mouse, from a config file |
| Crosshair | `mods/kf3crosshair` | `kf3.crosshair` | a crosshair over the picture, only while you are actually playing |

They are independent: run either alone or both together.

---

## Key Binds (`kf3.binds`)

Keyboard and mouse bindings for the game's PlayStation pad buttons. A held
key or mouse button holds its button; the mouse wheel fires one press per
notch, held briefly (`HoldMs`, 120 ms default) so the game's frame poll
cannot miss it.

Injection goes through the pad-read event — the same path the runtime's
`KF3_AUTOPAD` switch and its `press` shell command take — so a bound key
reaches the game's menus and action code exactly as the physical pad button
would.

**Default binding:**

```
Cross = WheelDown
```

Cross is this game's action button (confirm, pick up, open).

### The config

`mods/kf3binds/bindings.cfg`, next to the mod. It is re-read about once a
second when the file changes — save it and the new bindings apply, no
restart. Set `KF3_BINDS_CONFIG` to read the file from somewhere else.

```
// One binding per line:   <Button> = <Input>
// Lines starting with // or # are comments.

Cross = WheelDown     // the action button on the wheel
F = Square            // a keyboard key
Mouse5 = Circle       // a thumb button
R = Start
```

**Buttons** (the pad names):

```
Select  Start  Cross  Circle  Square  Triangle
L1  R1  L2  R2  L3  R3
Up  Down  Left  Right
```

**Inputs:**

- *Keyboard*: any Silk.NET key name, case-insensitive — letters `A`..`Z`,
  number row `D1`..`D9`,`D0`, `ControlLeft`, `ShiftRight`, `Space`, `F1`..`F12`,
  and so on.
- *Mouse*: `Mouse1` (left), `Mouse2` (right), `Mouse3` (middle), `Mouse4` /
  `Mouse5` (the thumb buttons — if yours fire the wrong way round, swap the
  names), `Mouse6`..`Mouse12`.
- *Wheel*: `WheelUp`, `WheelDown` — one press per notch.

**One button, several inputs**: repeat the button's line — either input acts
as the button:

```
Cross = WheelDown
Cross = Mouse5
```

**Settings** (in the config, not bindings):

```
HoldMs = 120    // how long a wheel press is held, in milliseconds
```

### Note on the wheel

The wheel is a shared drain: this mod and the CS 1.6 movement mod's
wheel-jump read the same notches, so with both on, whichever polls first gets
them. If you bind a wheel notch here, rebind jump off the wheel in the CS mod
(or vice versa).

No hooks: the mod acts only on the pad-read event and reports `0 hook(s)` in
the log.

---

## Crosshair (`kf3.crosshair`)

A crosshair — four arms with a hole in the middle, white on a black outline —
over the centre of the picture, drawn on the foreground so it sits over the
game's picture, the way a source-port crosshair does.

It draws **only while play is live**. All three of these must hold:

- **A character exists**: max HP non-zero.
- **Normal play owns the player**: the action state is 0 — the in-game menu
  runs under the examine state, death and cast executions have their own;
  charging a spell is normal-state input and does not hide the crosshair.
- **The player tick is running**: the game's play-tick counter is still
  moving. The title loop, the in-game menu, a loading screen and a cutscene
  that takes the update over all stop it.

Checking only the health word is not enough — the game never clears it, so it
survives in RAM through menus, loading screens and cutscenes; a crosshair
drawn on that alone shows over all of them. The three words are sampled on
the game's own pad poll, and the draw thread reads a single flag.

So: the title screen, its menus, the in-game menu, loading screens and
cutscenes stay unmarked; the crosshair appears when you are actually in play.

### Settings

In the mod's settings page (the Mods popup):

- **Crosshair on** — on/off (persisted as `kf3.crosshair.enabled`)
- **Crosshair scale** — 0.5× to 3× (persisted as `kf3.crosshair.scale`)

With the CS 1.6 movement mod (`kf3.csmove`) on, its own HUD draws a
crosshair too; turn one of the two off or they draw on top of each other.

No hooks: `0 hook(s)`.

---

## Installing

1. Copy the two folders into the game's `mods/` directory, beside the other
   mods:

   ```
   <install>/mods/kf3binds/
   <install>/mods/kf3crosshair/
   ```

2. Enable them in `interface.ini`, under the `[RecompOne]` section:

   ```ini
   [RecompOne]
   mods.kf3.binds.enabled=True
   mods.kf3.crosshair.enabled=True
   ```

   (Or switch them on in the Mods popup in-game; the launcher writes the same
   keys.)

3. Launch. Each mod compiles itself on first launch; the log shows:

   ```
   [kf3binds] loaded; N binding(s) from .../mods/kf3binds/bindings.cfg
   [Mods] kf3.binds v0.4: 0 hook(s)
   [kf3crosshair] loaded; scale 1
   [Mods] kf3.crosshair v0.1: 0 hook(s)
   ```

A mod that fails to compile reports `[Mods] kf3.binds:` (or `kf3.crosshair:`)
with the compiler error on the console and keeps the game running without it.

---

## Log lines

| line | meaning |
|---|---|
| `[kf3binds] loaded; N binding(s) from <path>` | the config parsed, N bindings active |
| `[kf3binds] <path>: unknown button '<name>'` | a config line names no pad button |
| `[kf3binds] <path>: unknown input '<name>'` | a config line names no key, mouse button or wheel |
| `[kf3crosshair] loaded; scale <s>` | the crosshair mod is up |

## License / credits

Part of the Verdite3 project. The crosshair's shape is the CS movement mod's
HUD crosshair, which is where it comes from.
