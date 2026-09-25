using Godot;

namespace Hellwall.Game;

/// <summary>
/// Esc (with nothing selected) or F10 in a run: the sim stops, the world dims,
/// and a small panel offers Resume, the display mode, the master volume, Quit
/// to the main menu and Exit. Quitting asks for a second click, since a run
/// isn't saved unless F5 was pressed.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    public Action Resume = null!;
    public Action QuitToMenu = null!;
    public Action Save = null!, Load = null!;
    /// <summary>When the quicksave was written, or null for none.</summary>
    public DateTime? Saved;

    public override void _Ready()
    {
        Layer = 20;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        var centre = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(centre);
        var panel = UiKit.PanelBox(UiKit.Gold);
        panel.CustomMinimumSize = new Vector2(340, 0);
        centre.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);

        var title = UiKit.Label("Paused", 22, UiKit.Gold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);

        var resume = UiKit.TextButton("Resume  (Esc)", 16);
        resume.Pressed += () => Resume();
        box.AddChild(resume);

        var saves = new HBoxContainer();
        saves.AddThemeConstantOverride("separation", 8);
        var save = UiKit.TextButton("Save  (F5)", 14);
        save.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var load = UiKit.TextButton("Load  (F9)", 14);
        load.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        load.Disabled = Saved == null;
        var when = UiKit.Label(Saved is { } t ? $"Quicksave from {t:HH:mm}" : "No quicksave yet", 12, UiKit.Muted);
        save.Pressed += () => { Save(); when.Text = $"Saved at {DateTime.Now:HH:mm}"; load.Disabled = false; };
        load.Pressed += () => Load();
        saves.AddChild(save);
        saves.AddChild(load);
        box.AddChild(saves);
        box.AddChild(when);

        box.AddChild(new HSeparator());

        var fullscreen = new CheckButton { Text = "Fullscreen", ButtonPressed = Display.Fullscreen, FocusMode = Control.FocusModeEnum.None };
        fullscreen.Toggled += on => Display.SetFullscreen(on);
        box.AddChild(fullscreen);

        var row = new HBoxContainer();
        row.AddChild(UiKit.Label("Master volume", 14));
        var volume = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = Sound.Volume, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        var percent = UiKit.Label($"{Sound.Volume * 100:0}%", 13, UiKit.Muted);
        percent.CustomMinimumSize = new Vector2(40, 0);
        volume.ValueChanged += v =>
        {
            Settings.Set("volume", (float)v);
            percent.Text = $"{v * 100:0}%";
        };
        row.AddChild(volume);
        row.AddChild(percent);
        box.AddChild(row);
        box.AddChild(Display.MusicRow());
        box.AddChild(Display.UiScaleRow());

        box.AddChild(new HSeparator());

        box.AddChild(Confirming("Quit to main menu", "Leave this run? Click again  (unsaved progress is lost)", () => QuitToMenu()));
        box.AddChild(Confirming("Exit game", "Exit to the desktop? Click again", () => GetTree().Quit()));
    }

    /// <summary>A button that asks once before it acts: the first click relabels it, the second does it.</summary>
    static Button Confirming(string text, string ask, Action act)
    {
        var b = UiKit.TextButton(text, 15);
        bool armed = false;
        b.Pressed += () =>
        {
            if (armed) { act(); return; }
            armed = true;
            b.Text = ask;
            b.AddThemeColorOverride("font_color", UiKit.Threat);
        };
        b.MouseExited += () =>
        {
            armed = false;
            b.Text = text;
            b.RemoveThemeColorOverride("font_color");
        };
        return b;
    }
}

/// <summary>Window mode, remembered in settings.cfg and put back at launch.</summary>
public static class Display
{
    public static bool Fullscreen => Settings.Get("fullscreen", false);
    /// <summary>Interface size: the whole canvas scales (the camera zooms out to match, so the world doesn't).</summary>
    public static float UiScale => Override ?? Settings.Get("ui_scale", 1f);
    /// <summary>--ui-scale for screenshots, without touching the player's settings.</summary>
    public static float? Override;

    static Window? _root;

    public static void SetUiScale(float scale)
    {
        Settings.Set("ui_scale", scale);
        Apply();
    }

    /// <summary>A slider row for the music's volume (times the master volume), for both menus.</summary>
    public static HBoxContainer MusicRow()
    {
        var row = new HBoxContainer();
        row.AddChild(UiKit.Label("Music", 13));
        var slider = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = Music.Volume, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        slider.ValueChanged += v => Settings.Set("music", (float)v);
        row.AddChild(slider);
        return row;
    }

    /// <summary>A slider row for the interface size, for both menus.</summary>
    public static HBoxContainer UiScaleRow()
    {
        var row = new HBoxContainer();
        row.AddChild(UiKit.Label("Interface size", 13));
        var slider = new HSlider { MinValue = 0.75, MaxValue = 1.75, Step = 0.05, Value = UiScale, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        var label = UiKit.Label($"{UiScale * 100:0}%", 12, UiKit.Muted);
        label.CustomMinimumSize = new Vector2(40, 0);
        // Applied on release, not while dragging: rescaling under the cursor makes the slider jump.
        slider.DragEnded += _ => SetUiScale((float)slider.Value);
        slider.ValueChanged += v => label.Text = $"{v * 100:0}%";
        row.AddChild(slider);
        row.AddChild(label);
        return row;
    }

    public static void SetFullscreen(bool on)
    {
        Settings.Set("fullscreen", on);
        Apply();
    }

    public static void Apply(Window? root = null)
    {
        if (root != null) _root = root;
        if (_root != null) _root.ContentScaleFactor = UiScale;
        if (DisplayServer.GetName() == "headless") return;
        var want = Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != want) DisplayServer.WindowSetMode(want);
    }
}
