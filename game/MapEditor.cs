using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// A terrain painter for hand-made maps. Start from a generated map (any
/// kind, seed and size) or a saved one, paint tiles with a round brush, name
/// it and save: it becomes a scenario file in user://maps/ that the skirmish
/// menu lists, where every other setting (days, waves, packs, fog...) is
/// chosen as for any skirmish. Packs, strays and Hellgates are placed on the
/// painted ground when a run starts, the same way as on a generated map.
///
/// Controls: left-drag paints; right- or middle-drag, WASD or the screen edges
/// pan; the wheel zooms; [ and ] size the brush; 1-6 pick the tile.
/// </summary>
public partial class MapEditor : Node2D
{
    public Action Back = null!;
    public Action<ScenarioDef> Play = null!;

    static readonly (Tile Tile, string Label)[] Brushes =
        [(Tile.Grass, "Grass"), (Tile.Forest, "Forest"), (Tile.Rock, "Rock"), (Tile.Water, "Water"), (Tile.Ore, "Iron ore"), (Tile.Silver, "Silver")];
    static readonly int[] BrushSizes = [0, 1, 2, 4, 7];
    static readonly int[] Sizes = [192, 256, 320];

    World _world = null!;
    TerrainView? _terrain;
    Node2D? _sorted;
    Camera2D _camera = null!;
    Minimap? _minimap;
    Control _minimapHolder = null!;
    Overlay _overlay = null!;

    Tile _brush = Tile.Forest;
    int _brushSize = 2;
    bool _painting, _dragging;
    Vector2 _dragFrom;
    bool _dirtyMap;
    double _minimapClock;

    LineEdit _name = null!, _seed = null!;
    OptionButton _kind = null!, _size = null!, _open = null!;
    Label _status = null!, _brushLabel = null!;
    readonly List<Button> _brushButtons = new();
    readonly List<ScenarioDef> _saved = new();

    public override void _Ready()
    {
        _camera = new Camera2D { Zoom = new Vector2(0.5f, 0.5f) };
        AddChild(_camera);
        _camera.MakeCurrent();
        _overlay = new Overlay { Editor = this, ZIndex = 5 };
        AddChild(_overlay);
        BuildUi();
        Generate();
    }

    // --- the map ---

    void Generate() => Load(new ScenarioDef { Seed = uint.TryParse(_seed.Text, out var s) ? s : 11, Map = (MapKind)_kind.Selected, MapSize = Sizes[_size.Selected] });

    void Load(ScenarioDef def)
    {
        _terrain?.QueueFree();
        _sorted?.QueueFree();
        _minimap?.QueueFree();
        // A world only for its terrain (and the Keep in the middle, for scale): no packs, no gates, never stepped.
        _world = World.Create(new WorldOptions(def.Seed, def.MapSize, 0, Rules.Default, Map: def.Map, Scenario: def.Tiles.Length > 0 ? def : null));
        _sorted = new Node2D { YSortEnabled = true };
        _terrain = new TerrainView { World = _world, Sorted = _sorted, ZIndex = -2 };
        AddChild(_terrain);
        AddChild(_sorted);
        _camera.Position = Iso.P(def.MapSize / 2f, def.MapSize / 2f);
        _minimap = new Minimap { World = _world, Camera = _camera, MoveCamera = p => _camera.Position = p };
        _minimapHolder.AddChild(_minimap);
        _status.Text = $"{def.MapSize} x {def.MapSize}, from {(def.Tiles.Length > 0 ? $"'{def.Name}'" : $"{def.Map} seed {def.Seed}")}";
    }

    void Paint(Vector2 at)
    {
        var t = Iso.Tile(at);
        int cx = (int)t.X, cy = (int)t.Y, r = _brushSize, n = _world.Terrain.Width;
        int c = n / 2, clear = Balance.KeepClearRadius;
        for (int y = cy - r; y <= cy + r; y++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (!_world.Terrain.InBounds(x, y) || (x - cx) * (x - cx) + (y - cy) * (y - cy) > r * r + r) continue;
                if ((x - c) * (x - c) + (y - c) * (y - c) <= clear * clear) continue; // the Keep's clearing stays grass
                if (_world.Terrain.Get(x, y) == _brush) continue;
                _world.Terrain.Set(x, y, _brush);
                _terrain!.PaintCell(x, y);
                _dirtyMap = true;
            }
    }

    ScenarioDef Current()
    {
        string name = _name.Text.Trim().Length > 0 ? _name.Text.Trim() : "Untitled";
        return new ScenarioDef
        {
            Id = MapFiles.IdFor(name),
            Name = name,
            Seed = uint.TryParse(_seed.Text, out var s) ? s : 11,
            Map = (MapKind)_kind.Selected,
            MapSize = _world.Terrain.Width,
            Tiles = ScenarioDef.EncodeTiles(_world.Terrain.Tiles),
        };
    }

    void Save()
    {
        var map = Current();
        MapFiles.Save(map);
        _status.Text = $"Saved '{map.Name}' to {MapFiles.Folder}";
        RefreshSaved();
    }

    void RefreshSaved()
    {
        _saved.Clear();
        _saved.AddRange(MapFiles.All());
        _open.Clear();
        _open.AddItem("Open a saved map...");
        foreach (var m in _saved) _open.AddItem(m.Name);
        _open.Selected = 0;
    }

    // --- input ---

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _painting = mb.Pressed;
                if (mb.Pressed) Paint(GetGlobalMousePosition());
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right or MouseButton.Middle } mb:
                _dragging = mb.Pressed;
                _dragFrom = mb.Position;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                Zoom(1.15f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                Zoom(1 / 1.15f);
                break;
            case InputEventMouseMotion mm:
                if (_painting) Paint(GetGlobalMousePosition());
                if (_dragging)
                {
                    _camera.Position -= (mm.Position - _dragFrom) / _camera.Zoom;
                    _dragFrom = mm.Position;
                }
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (key.Keycode == Key.Bracketleft) SetBrushSize(Math.Max(0, Array.IndexOf(BrushSizes, _brushSize) - 1));
                else if (key.Keycode == Key.Bracketright) SetBrushSize(Math.Min(BrushSizes.Length - 1, Array.IndexOf(BrushSizes, _brushSize) + 1));
                else if (key.Keycode is >= Key.Key1 and <= Key.Key6) SetBrush((int)(key.Keycode - Key.Key1));
                else if (key.Keycode == Key.Escape) Leave();
                break;
        }
    }

    void Zoom(float factor)
    {
        float z = Mathf.Clamp(_camera.Zoom.X * factor, 0.12f, 3f);
        _camera.Zoom = new Vector2(z, z);
    }

    public override void _Process(double delta)
    {
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
        if (Main.EdgeScroll && GetViewport().GetVisibleRect().HasPoint(GetViewport().GetMousePosition()))
        {
            var m = GetViewport().GetMousePosition();
            var size = GetViewport().GetVisibleRect().Size;
            if (m.X < 4) dir.X -= 1;
            if (m.X > size.X - 5) dir.X += 1;
            if (m.Y < 4) dir.Y -= 1;
            if (m.Y > size.Y - 5) dir.Y += 1;
        }
        if (dir != Vector2.Zero) _camera.Position += dir.Normalized() * (float)(900 * delta) / _camera.Zoom.X;

        _minimapClock += delta;
        if (_dirtyMap && _minimapClock > 0.4)
        {
            _minimap?.Repaint();
            _dirtyMap = false;
            _minimapClock = 0;
        }
        _overlay.QueueRedraw();
    }

    void Leave()
    {
        QueueFree();
        Back();
    }

    // --- panel ---

    void BuildUi()
    {
        var layer = new CanvasLayer { Layer = 5 };
        AddChild(layer);
        var panel = UiKit.PanelBox();
        panel.Position = new Vector2(8, 8);
        panel.CustomMinimumSize = new Vector2(300, 0);
        layer.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);

        box.AddChild(UiKit.Label("Map editor", 18, UiKit.Gold));
        _name = new LineEdit { PlaceholderText = "Map name", Text = "My map" };
        box.AddChild(_name);

        box.AddChild(UiKit.Label("Start from", 13, UiKit.Muted));
        _kind = new OptionButton();
        foreach (var k in Enum.GetNames<MapKind>()) _kind.AddItem(k);
        box.AddChild(_kind);
        var row = new HBoxContainer();
        _seed = new LineEdit { Text = "11", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Seed" };
        row.AddChild(_seed);
        _size = new OptionButton();
        foreach (var s in Sizes) _size.AddItem($"{s}");
        _size.Selected = 1;
        row.AddChild(_size);
        box.AddChild(row);
        var generate = UiKit.TextButton("Generate (replaces the map)", 13);
        generate.Pressed += Generate;
        box.AddChild(generate);
        _open = new OptionButton();
        _open.ItemSelected += i =>
        {
            if (i <= 0 || i > _saved.Count) return;
            var m = _saved[(int)i - 1];
            _name.Text = m.Name;
            _kind.Selected = (int)m.Map;
            _seed.Text = m.Seed.ToString();
            _size.Selected = Math.Max(0, Array.IndexOf(Sizes, m.MapSize));
            Load(m);
        };
        box.AddChild(_open);
        RefreshSaved();

        box.AddChild(UiKit.Label("Paint  (1-6, [ ] brush size)", 13, UiKit.Muted));
        var grid = new GridContainer { Columns = 3 };
        for (int i = 0; i < Brushes.Length; i++)
        {
            int index = i;
            var b = UiKit.TextButton($"{i + 1} {Brushes[i].Label}", 12);
            b.CustomMinimumSize = new Vector2(92, 0);
            b.Pressed += () => SetBrush(index);
            grid.AddChild(b);
            _brushButtons.Add(b);
        }
        box.AddChild(grid);
        _brushLabel = UiKit.Label("", 13);
        box.AddChild(_brushLabel);

        var actions = new HBoxContainer();
        var save = UiKit.TextButton("Save", 14);
        save.Pressed += Save;
        var play = UiKit.TextButton("Save and play", 14);
        play.Pressed += () => { Save(); var map = Current(); QueueFree(); Play(map); };
        var back = UiKit.TextButton("Back", 14);
        back.Pressed += Leave;
        actions.AddChild(save);
        actions.AddChild(play);
        actions.AddChild(back);
        box.AddChild(actions);
        _status = UiKit.Label("", 12, UiKit.Muted);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.CustomMinimumSize = new Vector2(280, 0);
        box.AddChild(_status);
        box.AddChild(UiKit.Label("Packs, stragglers and Hellgates are placed on the painted ground when a run starts; pick days, waves and the rest in Skirmish.", 11, UiKit.Muted));
        foreach (var l in box.GetChildren().OfType<Label>().Where(l => l.Text.StartsWith("Packs"))) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(280, 0); }

        _minimapHolder = new Control { Position = new Vector2(8, 0) };
        layer.AddChild(_minimapHolder);
        _minimapHolder.Resized += () => { };
        layer.Ready += () => { };
        SetBrush(1);
        SetBrushSize(2);
    }

    void SetBrush(int i)
    {
        _brush = Brushes[i].Tile;
        for (int b = 0; b < _brushButtons.Count; b++) _brushButtons[b].AddThemeColorOverride("font_color", b == i ? UiKit.Gold : UiKit.Text);
        UpdateBrushLabel();
    }

    void SetBrushSize(int index)
    {
        _brushSize = BrushSizes[index];
        UpdateBrushLabel();
    }

    void UpdateBrushLabel()
    {
        if (_brushLabel != null) _brushLabel.Text = $"Brush: {Brushes.First(b => b.Tile == _brush).Label}, {_brushSize * 2 + 1} tiles across";
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMSizeChanged || what == NotificationReady) PlaceMinimap();
    }

    void PlaceMinimap()
    {
        if (_minimapHolder == null) return;
        _minimapHolder.Position = new Vector2(8, GetViewport().GetVisibleRect().Size.Y - 190);
    }

    /// <summary>The brush under the cursor, and the Keep's clearing, which can't be painted.</summary>
    sealed partial class Overlay : Node2D
    {
        public MapEditor Editor = null!;

        public override void _Draw()
        {
            var e = Editor;
            if (e._world == null) return;
            var t = Iso.Tile(GetGlobalMousePosition());
            Iso.Ellipse(this, new Vector2((int)t.X + 0.5f, (int)t.Y + 0.5f), e._brushSize + 0.6f, new Color(1, 1, 1, 0.8f), 2);
            int c = e._world.Terrain.Width / 2;
            Iso.Ellipse(this, new Vector2(c + 0.5f, c + 0.5f), Balance.KeepClearRadius, new Color(0.94f, 0.76f, 0.36f, 0.6f), 2);
        }
    }
}
