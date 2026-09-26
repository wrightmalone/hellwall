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
/// Tools: Paint (left-drag), Packs and Gates (click to place; placed gates
/// replace the random ones), Erase (click a pack or gate). Right- or
/// middle-drag, WASD or the screen edges pan; the wheel zooms; [ and ] size
/// the brush; 1-6 pick the tile; Ctrl+Z (Cmd+Z) undoes a stroke or placement.
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

    enum Tool { Paint, Packs, Gates, Erase }
    Tool _tool = Tool.Paint;
    static readonly int[] PackSizes = [8, 20, 40, 80, 150];
    int _packSize = 40;
    DemonKind _packKind = DemonKind.Imp;
    readonly List<PlacedPack> _packs = new();
    readonly List<PlacedGate> _gates = new();
    CheckButton _randomPacks = null!;
    readonly List<Button> _toolButtons = new();

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
        _undo.Clear();
        _packs.Clear();
        _packs.AddRange(def.PlacedPacks);
        _gates.Clear();
        _gates.AddRange(def.PlacedGates);
        if (_randomPacks != null) _randomPacks.ButtonPressed = def.Packs != 0;
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
            PlacedPacks = [.. _packs],
            PlacedGates = [.. _gates],
            Packs = _randomPacks.ButtonPressed ? -1 : 0,
        };
    }

    void Save()
    {
        var map = Current();
        MapFiles.Save(map);
        _status.Text = $"Saved '{map.Name}' to {MapFiles.Folder}";
        RefreshSaved();
    }

    FileDialog Dialog(FileDialog.FileModeEnum mode, string title)
    {
        var dialog = new FileDialog
        {
            FileMode = mode, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = true, Title = title,
            Filters = [$"*.{MapFiles.Extension} ; Hellwall map", "*.json ; Hellwall map (JSON)"],
            CurrentDir = OS.GetSystemDir(OS.SystemDir.Desktop),
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        return dialog;
    }

    void ExportMap()
    {
        var map = Current();
        var dialog = Dialog(FileDialog.FileModeEnum.SaveFile, "Export map");
        dialog.CurrentFile = $"{map.Id}.{MapFiles.Extension}";
        dialog.FileSelected += path =>
        {
            try
            {
                MapFiles.Export(map, path);
                _status.Text = $"Exported '{map.Name}' to {path}";
            }
            catch (Exception e) { _status.Text = $"Couldn't export: {e.Message}"; }
            dialog.QueueFree();
        };
        dialog.PopupCentered(new Vector2I(820, 520));
    }

    void ImportMap()
    {
        var dialog = Dialog(FileDialog.FileModeEnum.OpenFile, "Import map");
        dialog.FileSelected += path =>
        {
            var (map, error) = MapFiles.Import(path);
            if (map == null) _status.Text = $"Couldn't import that map: {error}";
            else
            {
                Load(map);
                RefreshSaved();
                _status.Text = $"Imported '{map.Name}': it's in your maps now";
            }
            dialog.QueueFree();
        };
        dialog.PopupCentered(new Vector2I(820, 520));
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
                _painting = mb.Pressed && _tool == Tool.Paint;
                if (mb.Pressed) Click(GetGlobalMousePosition());
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
                else if (key.Keycode == Key.Z && (key.CtrlPressed || key.MetaPressed)) Undo();
                break;
        }
    }

    /// <summary>Undo: the map as it was before each stroke or placement, newest last (at most 40).</summary>
    readonly List<(Tile[] Tiles, PlacedPack[] Packs, PlacedGate[] Gates)> _undo = new();

    void Remember()
    {
        _undo.Add(((Tile[])_world.Terrain.Tiles.Clone(), [.. _packs], [.. _gates]));
        if (_undo.Count > 40) _undo.RemoveAt(0);
    }

    void Undo()
    {
        if (_undo.Count == 0) { _status.Text = "Nothing to undo"; return; }
        var (tiles, packs, gates) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        var now = _world.Terrain.Tiles;
        int w = _world.Terrain.Width;
        for (int i = 0; i < now.Length; i++)
        {
            if (now[i] == tiles[i]) continue;
            now[i] = tiles[i];
            _terrain!.PaintCell(i % w, i / w);
        }
        _packs.Clear();
        _packs.AddRange(packs);
        _gates.Clear();
        _gates.AddRange(gates);
        _dirtyMap = true;
        _status.Text = $"Undone ({_undo.Count} more)";
    }

    void Click(Vector2 at)
    {
        if (_tool != Tool.Erase || _packs.Count + _gates.Count > 0) Remember();
        var t = Iso.Tile(at);
        int x = (int)t.X, y = (int)t.Y, n = _world.Terrain.Width, c = n / 2;
        if (!_world.Terrain.InBounds(x, y)) return;
        bool nearKeep = (x - c) * (x - c) + (y - c) * (y - c) < 20 * 20;
        switch (_tool)
        {
            case Tool.Paint: Paint(at); break;
            case Tool.Packs:
                if (nearKeep) { _status.Text = "Not within 20 tiles of the Keep"; break; }
                _packs.Add(new PlacedPack(x, y, _packSize, _packKind));
                _status.Text = $"{_packs.Count} packs placed";
                break;
            case Tool.Gates:
                if (nearKeep) { _status.Text = "Not within 20 tiles of the Keep"; break; }
                _gates.Add(new PlacedGate(Math.Clamp(x - 1, 0, n - Hellgate.Size), Math.Clamp(y - 1, 0, n - Hellgate.Size)));
                _status.Text = $"{_gates.Count} Hellgates placed (placed gates replace the random ones)";
                break;
            case Tool.Erase:
                int pack = _packs.FindIndex(p => (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y) <= 16);
                if (pack >= 0) { _packs.RemoveAt(pack); break; }
                int gate = _gates.FindIndex(g => Math.Abs(g.X + 1 - x) <= 2 && Math.Abs(g.Y + 1 - y) <= 2);
                if (gate >= 0) _gates.RemoveAt(gate);
                break;
        }
    }

    /// <summary>
    /// --selftest=editor: paint a lake and place a pack and a gate through the
    /// editor's own code, save, read the file back, and start a run on it.
    /// scripts/verify.sh looks for the PASS line.
    /// </summary>
    public async void SelfTest()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        int c = _world.Terrain.Width / 2;
        _name.Text = "Selftest map";
        SetBrush(3); // water
        SetBrushSize(4);
        Click(Iso.P(c + 40.5f, c + 0.5f));
        SetTool(1);
        Click(Iso.P(c - 40.5f, c + 0.5f));
        SetTool(2);
        Click(Iso.P(c + 0.5f, c + 50.5f));
        Save();
        var saved = MapFiles.All().FirstOrDefault(m => m.Id == "selftest-map");
        bool water = saved?.DecodeTiles() is { } tiles && tiles[c * _world.Terrain.Width + c + 40] == Tile.Water;
        bool pack = saved?.PlacedPacks.Length == 1, gate = saved?.PlacedGates.Length == 1;
        var world = saved == null ? null : World.Create((saved with { Id = "map-selftest" }).Options(Rules.Default));
        bool plays = world != null && world.Terrain.Get(c + 40, c) == Tile.Water && world.Gates.Count == 1 && world.Packs.Any(p => p.X == c - 41);
        // Shared: exported to a file, imported back as a map of its own (the name's taken, so "-2"), and a doctored one turned away.
        bool shared = false;
        if (saved != null)
        {
            string file = System.IO.Path.Combine(OS.GetUserDataDir(), "selftest-export." + MapFiles.Extension);
            MapFiles.Export(saved, file);
            var (back, _) = MapFiles.Import(file);
            System.IO.File.WriteAllText(file, (saved with { PlacedPacks = [new PlacedPack(1, 1, 1_000_000)] }).ToJson());
            var (_, refused) = MapFiles.Import(file);
            shared = back is { Id: "selftest-map-2" } && back.Tiles == saved.Tiles && refused != null;
            foreach (var f in new[] { file, System.IO.Path.Combine(MapFiles.Folder, "selftest-map-2.json") }) try { System.IO.File.Delete(f); } catch (System.IO.IOException) { }
        }
        try { System.IO.File.Delete(System.IO.Path.Combine(MapFiles.Folder, "selftest-map.json")); } catch (System.IO.IOException) { }
        // Undo twice: the gate goes, then the pack; the lake stays.
        Undo();
        Undo();
        bool undone = _gates.Count == 0 && _packs.Count == 0 && _world.Terrain.Get(c + 40, c) == Tile.Water;
        Undo();
        bool dry = _world.Terrain.Get(c + 40, c) != Tile.Water;
        bool pass = water && pack && gate && plays && shared && undone && dry;
        GD.Print(pass ? "hellwall-selftest: PASS editor" : $"hellwall-selftest: FAIL editor (saved {saved != null}, water {water}, pack {pack}, gate {gate}, plays {plays}, export and import {shared}, undo {undone}, undo paint {dry})");
        GetTree().Quit();
    }

    void SetTool(int i)
    {
        _tool = (Tool)i;
        for (int b = 0; b < _toolButtons.Count; b++) _toolButtons[b].AddThemeColorOverride("font_color", b == i ? UiKit.Gold : UiKit.Text);
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

        var tools = new HBoxContainer();
        foreach (var (name, i) in new[] { ("Paint", 0), ("Packs", 1), ("Gates", 2), ("Erase", 3) })
        {
            var b = UiKit.TextButton(name, 13);
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            b.Pressed += () => SetTool(i);
            tools.AddChild(b);
            _toolButtons.Add(b);
        }
        box.AddChild(tools);
        var packRow = new HBoxContainer();
        packRow.AddChild(UiKit.Label("Pack", 13, UiKit.Muted));
        var packSize = new OptionButton();
        foreach (var p in PackSizes) packSize.AddItem($"{p} demons");
        packSize.Selected = Array.IndexOf(PackSizes, _packSize);
        packSize.ItemSelected += i => _packSize = PackSizes[i];
        packRow.AddChild(packSize);
        var packKind = new OptionButton();
        packKind.AddItem("Imps");
        packKind.AddItem("Hounds");
        packKind.ItemSelected += i => _packKind = i == 0 ? DemonKind.Imp : DemonKind.Hound;
        packRow.AddChild(packKind);
        box.AddChild(packRow);
        _randomPacks = new CheckButton { Text = "Also scatter packs at random", ButtonPressed = true };
        box.AddChild(_randomPacks);

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
        var undo = UiKit.TextButton("Undo", 14);
        undo.Pressed += Undo;
        actions.AddChild(undo);
        actions.AddChild(save);
        actions.AddChild(play);
        actions.AddChild(back);
        box.AddChild(actions);
        // Sharing: a map is one file, to send and to take in.
        var share = new HBoxContainer();
        var export = UiKit.TextButton("Export...", 13);
        export.TooltipText = "Save this map as a file to send to someone (." + MapFiles.Extension + ")";
        export.Pressed += ExportMap;
        var import = UiKit.TextButton("Import...", 13);
        import.TooltipText = "Add a map someone sent you, and open it";
        import.Pressed += ImportMap;
        var folder = UiKit.TextButton("Maps folder", 13);
        folder.TooltipText = "Open the folder your hand-made maps are kept in";
        folder.Pressed += () => { System.IO.Directory.CreateDirectory(MapFiles.Folder); OS.ShellOpen(MapFiles.Folder); };
        share.AddChild(export);
        share.AddChild(import);
        share.AddChild(folder);
        box.AddChild(share);
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
        SetTool(0);
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
            var font = ThemeDB.FallbackFont;
            foreach (var p in e._packs)
            {
                var at = new Vector2(p.X + 0.5f, p.Y + 0.5f);
                float r = MathF.Sqrt(p.Count / (MathF.PI * 0.45f)) + 0.5f;
                var tint = p.Kind == DemonKind.Hound ? new Color(1f, 0.55f, 0.15f) : new Color(0.9f, 0.15f, 0.12f);
                Iso.Ellipse(this, at, r, new Color(tint, 0.25f), filled: true);
                Iso.Ellipse(this, at, r, tint, 2);
                DrawString(font, Iso.P(at) + new Vector2(-10, 6), p.Count.ToString(), fontSize: 18, modulate: Colors.White);
            }
            foreach (var g in e._gates)
                DrawColoredPolygon(Iso.Diamond(g.X, g.Y, Hellgate.Size, Hellgate.Size), new Color(1, 0.1f, 0.5f, 0.7f));
        }
    }
}
