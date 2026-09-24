using Godot;
using Hellwall.Sim;
using Side = Hellwall.Sim.Side;
using Resource = Hellwall.Sim.Resource;

namespace Hellwall.Game;

/// <summary>
/// The HUD's look in one place: dark iron panels, gold for holy things, red
/// for threat, so what a panel is about reads before a word does. Also the
/// icons, all from art already in the game (tower pieces, baked sheets,
/// baked props).
/// </summary>
public static class UiKit
{
    public static readonly Color Panel = new(0.12f, 0.11f, 0.10f, 0.92f);
    public static readonly Color PanelLight = new(0.2f, 0.19f, 0.17f, 0.95f);
    public static readonly Color Rim = new(0.38f, 0.34f, 0.27f);
    public static readonly Color Text = new(0.95f, 0.93f, 0.88f);
    public static readonly Color Muted = new(0.72f, 0.69f, 0.62f);
    public static readonly Color Gold = new(0.94f, 0.76f, 0.36f);
    public static readonly Color Threat = new(0.92f, 0.33f, 0.3f);
    public static readonly Color ThreatDark = new(0.47f, 0.12f, 0.12f, 0.95f);
    public static readonly Color Good = new(0.6f, 0.8f, 0.36f);

    public static StyleBoxFlat Box(Color bg, Color? rim = null, int radius = 6, int pad = 6)
    {
        var box = new StyleBoxFlat { BgColor = bg, BorderColor = rim ?? Rim };
        box.SetBorderWidthAll(rim == null ? 1 : 1);
        box.SetCornerRadiusAll(radius);
        box.SetContentMarginAll(pad);
        return box;
    }

    public static PanelContainer PanelBox(Color? rim = null)
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        p.AddThemeStyleboxOverride("panel", Box(Panel, rim));
        return p;
    }

    public static Label Label(string text = "", int size = 14, Color? colour = null)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", colour ?? Text);
        return l;
    }

    /// <summary>A square icon button: picture, and the hotkey in the corner.</summary>
    public static Button IconButton(Texture2D? icon, string key, int size = 52)
    {
        var b = new Button
        {
            Icon = icon, ExpandIcon = true, IconAlignment = HorizontalAlignment.Center, VerticalIconAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(size, size), FocusMode = Control.FocusModeEnum.None, ClipText = true,
        };
        b.AddThemeStyleboxOverride("normal", Box(PanelLight, Rim, 5, 4));
        b.AddThemeStyleboxOverride("hover", Box(new Color(0.28f, 0.26f, 0.22f), Gold, 5, 4));
        b.AddThemeStyleboxOverride("pressed", Box(new Color(0.34f, 0.3f, 0.2f), Gold, 5, 4));
        b.AddThemeStyleboxOverride("disabled", Box(new Color(0.15f, 0.14f, 0.13f, 0.9f), Rim, 5, 4));
        if (key.Length > 0)
        {
            var k = Label(key, 11, Gold);
            k.Position = new Vector2(4, 1);
            b.AddChild(k);
        }
        return b;
    }

    public static Button TextButton(string text, int size = 13)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        b.AddThemeFontSizeOverride("font_size", size);
        b.AddThemeStyleboxOverride("normal", Box(PanelLight, Rim, 5, 5));
        b.AddThemeStyleboxOverride("hover", Box(new Color(0.28f, 0.26f, 0.22f), Gold, 5, 5));
        b.AddThemeStyleboxOverride("pressed", Box(new Color(0.34f, 0.3f, 0.2f), Gold, 5, 5));
        return b;
    }

    // --- icons ---

    static readonly Dictionary<string, Texture2D> Cache = new();

    /// <summary>A texture cropped to its visible pixels: sprites carry empty canvas that would shrink them in a square icon.</summary>
    public static Texture2D Trimmed(Texture2D texture, string key)
    {
        if (Cache.TryGetValue("trim-" + key, out var t)) return t;
        var used = texture.GetImage().GetUsedRect();
        return Cache["trim-" + key] = used.Size.X <= 0 ? texture : new AtlasTexture { Atlas = texture, Region = new Rect2(used.Position, used.Size) };
    }

    public static Texture2D Building(BuildingKind kind)
    {
        var pieces = Art.BuildingPieces(kind);
        string path = kind == BuildingKind.Farm && Art.Ground(kind) is { } g ? g : pieces[^1];
        return Trimmed(Art.Tex(path), "b-" + kind);
    }

    /// <summary>A soldier's portrait: the first walk frame, facing the camera.</summary>
    public static Texture2D Unit(UnitKind kind)
    {
        string key = "u-" + kind;
        if (Cache.TryGetValue(key, out var t)) return t;
        // The figure's own pixels within its cell, so it fills the button.
        var sheet = Art.Tex(Art.Unit(kind));
        var cell = new Rect2I(0, Art.Cell, Art.Cell, Art.Cell);
        var used = sheet.GetImage().GetRegion(cell).GetUsedRect();
        var region = used.Size.X > 0 ? new Rect2(cell.Position + used.Position, used.Size) : new Rect2(cell.Position, cell.Size);
        return Cache[key] = new AtlasTexture { Atlas = sheet, Region = region };
    }

    public static Texture2D Resource(Resource r) => Trimmed(r switch
    {
        Sim.Resource.Gold => Art.Tex(Art.SceneryPath("icon-gold")),
        Sim.Resource.Wood => Art.Tex(Art.SceneryPath("pine-2")),
        Sim.Resource.Stone => Art.Tex(Art.SceneryPath("rock-2")),
        Sim.Resource.Food => Art.Tex(Art.SceneryPath("icon-food")),
        _ => Art.Tex(Art.TerrainImages(Tile.Ore)[0]),
    }, "r-" + r);

    public static Texture2D Holy => Trimmed(Art.Tex(Art.SceneryPath("icon-holy")), "holy");

    public static string Money(double v) => v >= 10000 ? $"{v / 1000:0}k" : $"{v:#,0}";

    public static string Clock(double seconds) => seconds <= 0 ? "now" : $"{(int)seconds / 60}:{(int)seconds % 60:00}";

    public static string SideName(Side s) => s.ToString().ToLowerInvariant();

    /// <summary>A map side and where it lies on screen: in the isometric view north runs up and to the right.</summary>
    public static string SideOnScreen(Side s) => s switch
    {
        Side.North => "north (top right)",
        Side.East => "east (bottom right)",
        Side.South => "south (bottom left)",
        _ => "west (top left)",
    };
}
