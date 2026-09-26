using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Everything in world space except terrain and the horde. Buildings and
/// soldiers are sprites in the y-sorted layer (so trees and walls overlap
/// them properly); state that has to read at a glance (build progress,
/// damage, possession, dark buildings, labels, packs, gates, shots,
/// selection, ranges and the placement ghost) is drawn on an overlay above.
/// </summary>
public partial class WorldView : Node2D
{
    public World World = null!;
    public ClientState State = null!;
    /// <summary>The y-sorted layer the terrain's trees are in.</summary>
    public Node2D Sorted = null!;

    readonly Dictionary<int, Node2D> _buildings = new();
    readonly Dictionary<int, Sprite2D> _units = new();
    readonly Overlay _overlay = new();
    readonly Ground _ground = new();

    public override void _Ready()
    {
        _ground.View = this;
        _ground.ZIndex = -1; // under the sorted layer, above the terrain (z -2)
        AddChild(_ground); // footprint shadows and gates, on the ground under everything that stands
        _overlay.View = this;
        _overlay.ZIndex = 3; // above the sorted layer and the horde
        AddChild(_overlay);
    }

    public void Refresh()
    {
        SyncBuildings();
        SyncUnits();
        SyncRuins();
        _ground.QueueRedraw();
        _overlay.QueueRedraw();
    }

    readonly Dictionary<int, Node2D> _ruins = new();

    /// <summary>Ruins stand once seen; a looted one is left paler.</summary>
    void SyncRuins()
    {
        foreach (var r in World.Ruins)
        {
            if (!_ruins.TryGetValue(r.Id, out var node))
            {
                if (!World.Vision.IsExplored(r.X, r.Y)) continue;
                node = RuinSprite(r);
                _ruins[r.Id] = node;
                Sorted.AddChild(node);
            }
            node.Modulate = r.Looted ? new Color(0.8f, 0.78f, 0.76f, 0.75f) : new Color(0.78f, 0.72f, 0.7f);
        }
    }

    static Node2D RuinSprite(Ruin r)
    {
        var node = new Node2D { Position = Iso.P(r.X + 2, r.Y + 2) };
        if (Art.BakedBuilding("ruin") is { } ruin)
        {
            // A 2x2 bake, shown half again as large: an old town's worth of broken stone.
            var at = Iso.P(r.X + 0.5f, r.Y + 0.5f) - node.Position;
            node.AddChild(new Sprite2D { Texture = ruin.Texture, Centered = false, Scale = new Vector2(0.75f, 0.75f), Position = at - ruin.Base * 0.75f, TextureFilter = TextureFilterEnum.LinearWithMipmaps });
            return node;
        }
        (float dx, float dy, float size)[] layout = [(-1.4f, -0.6f, 1.2f), (0.6f, -1.2f, 0.9f), (0.2f, 0.8f, 1.0f)];
        for (int i = 0; i < layout.Length; i++)
        {
            var tex = Art.Tex(Art.RuinPieces[i]);
            float w = tex.GetWidth(), h = tex.GetHeight();
            float scale = layout[i].size * 2 * Iso.HalfW / w;
            var at = Iso.P(r.X + 0.5f + layout[i].dx, r.Y + 0.5f + layout[i].dy) - node.Position;
            node.AddChild(new Sprite2D { Texture = tex, Centered = false, Scale = new Vector2(scale, scale), Position = at + new Vector2(-w / 2 * scale, -(h - w / 4) * scale), RotationDegrees = i == 1 ? 8 : 0 });
        }
        return node;
    }

    readonly Dictionary<int, BuildingKind> _kinds = new();

    void SyncBuildings()
    {
        var seen = new HashSet<int>();
        bool changed = false;
        foreach (var b in World.Buildings)
        {
            seen.Add(b.Id);
            // An upgrade changes what stands there: throw the old picture away.
            if (_buildings.TryGetValue(b.Id, out var old) && _kinds.GetValueOrDefault(b.Id) != b.Kind)
            {
                old.QueueFree();
                _buildings.Remove(b.Id);
            }
            _kinds[b.Id] = b.Kind;
            if (!_buildings.TryGetValue(b.Id, out var sprite))
            {
                sprite = b.IsWallLike ? new WallSprite(World, b) : new BuildingSprite(b);
                _buildings[b.Id] = sprite;
                Sorted.AddChild(sprite);
                changed = true;
            }
            if (sprite is BuildingSprite built) built.Show(b.Complete);
            sprite.Modulate = !b.Complete ? new Color(0.85f, 0.85f, 0.9f, 0.9f)
                : b.Possessed ? new Color(0.75f, 0.4f, 0.95f)
                : b.Complete && !b.OnGround ? new Color(0.32f, 0.33f, 0.42f) // off holy ground: dark, and cold
                : !b.Active ? new Color(0.6f, 0.6f, 0.6f)
                : Colors.White;
        }
        foreach (var id in _buildings.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _buildings[id].QueueFree();
            _buildings.Remove(id);
            changed = true;
        }
        // Walls join their neighbours, so any change to the layout redraws them.
        if (changed)
            foreach (var sprite in _buildings.Values)
                if (sprite is WallSprite wall) wall.QueueRedraw();
    }

    readonly Dictionary<int, int> _facing = new();

    void SyncUnits()
    {
        var seen = new HashSet<int>();
        int step = (int)(Time.GetTicksMsec() / 100); // walk frames at 10 a second
        foreach (var u in World.Units)
            Figure(seen, step, u.Id, Art.Unit(u.Kind), Art.UnitScale, u.X, u.Y, u.PrevX, u.PrevY, null);
        // Woodsmen (forest green) and miners (stone grey): their own figures, a size smaller; working, he faces the tree or rock and swings, over and over.
        var w = World.Terrain.Width;
        foreach (var m in World.Woodsmen)
        {
            bool chopping = m.State == WoodsmanState.Chopping && m.Tree >= 0;
            bool miner = World.BuildingById(m.HomeId) is { Def.Miners: true };
            string sheet = miner
                ? chopping ? "res://art/baked/unit-miner-dig.png" : "res://art/baked/unit-miner.png"
                : chopping ? "res://art/baked/unit-woodsman-chop.png" : "res://art/baked/unit-woodsman.png";
            Figure(seen, step, m.Id, sheet, Art.UnitScale * 0.8f, m.X, m.Y, m.PrevX, m.PrevY,
                chopping ? new Vector2(m.Tree % w + 0.5f - m.X, m.Tree / w + 0.5f - m.Y) : null);
        }
        // Hunters: leather brown; at the woods' edge they face the trees and loose arrows, and come home carrying the kill.
        foreach (var h in State.Hunters.All)
        {
            bool shooting = h.Doing == Hunters.Work.Shoot && h.Tree >= 0;
            string sheet = shooting ? "res://art/baked/unit-hunter-shoot.png" : h.Doing == Hunters.Work.Home ? "res://art/baked/unit-hunter-carry.png" : "res://art/baked/unit-hunter.png";
            Figure(seen, step, h.Id, sheet, Art.UnitScale * 0.8f, h.X, h.Y, h.PrevX, h.PrevY,
                shooting ? new Vector2(h.Tree % w + 0.5f - h.X, h.Tree / w + 0.5f - h.Y) : null, ticked: false);
        }
        // Fishing boats: always rocking (their "walk" is the swell), facing the way they last sailed.
        foreach (var boat in State.Fishers.All)
        {
            Figure(seen, step, boat.Id, "res://art/baked/unit-boat.png", Art.UnitScale * 1.35f, boat.X, boat.Y, boat.PrevX, boat.PrevY, new Vector2(boat.HeadX, boat.HeadY), ticked: false);
        }
        // Farmers: straw-coloured, out on the fields; at work they face the tile and stoop, water or reap.
        foreach (var h in State.Farmers.All)
        {
            string sheet = h.Doing switch
            {
                Farmers.Work.Plant => "res://art/baked/unit-farmer-plant.png",
                Farmers.Work.Water => "res://art/baked/unit-farmer-water.png",
                Farmers.Work.Reap => "res://art/baked/unit-farmer-reap.png",
                _ => "res://art/baked/unit-farmer.png",
            };
            bool working = h.Doing is Farmers.Work.Plant or Farmers.Work.Water or Farmers.Work.Reap;
            Figure(seen, step, h.Id, sheet, Art.UnitScale * 0.8f, h.X, h.Y, h.PrevX, h.PrevY,
                working ? new Vector2(h.Tile % w + 0.5f - h.X, h.Tile / w + 0.5f - h.Y) : null, ticked: false);
        }
        foreach (var id in _units.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _units[id].QueueFree();
            _units.Remove(id);
            _facing.Remove(id);
        }
    }

    /// <param name="ticked">Moved by the sim, a tick at a time (drawn between its last two places); false for the client's own figures, which move every frame.</param>
    void Figure(HashSet<int> seen, int step, int id, string sheet, float scale, float x, float y, float prevX, float prevY, Vector2? chopping, bool ticked = true)
    {
        seen.Add(id);
        if (!_units.TryGetValue(id, out var sprite))
        {
            sprite = new Sprite2D
            {
                Texture = Art.Tex(sheet),
                Centered = false,
                RegionEnabled = true,
                Scale = new Vector2(scale, scale),
                Offset = -Art.Feet, // feet on the ground point
                TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            };
            _units[id] = sprite;
            Sorted.AddChild(sprite);
        }
        var texture = Art.Tex(sheet);
        if (sprite.Texture != texture) sprite.Texture = texture; // a woodsman swaps between walking and chopping
        float dx = x - prevX, dy = y - prevY;
        bool moving = dx * dx + dy * dy > 1e-6f;
        if (chopping is { } at) _facing[id] = Art.Facing(at.X, at.Y);
        else if (moving) _facing[id] = Art.Facing(dx, dy);
        int facing = _facing.GetValueOrDefault(id, 1);
        // chopping: the swing sheet, looped. (A positive remainder: the client's own figures have negative ids.)
        int frame = chopping != null || moving ? ((step + id) % Art.Frames + Art.Frames) % Art.Frames : 0;
        sprite.RegionRect = new Rect2(frame * Art.Cell, facing * Art.Cell, Art.Cell, Art.Cell);
        sprite.Position = ticked ? Iso.P(Mathf.Lerp(prevX, x, State.Alpha), Mathf.Lerp(prevY, y, State.Alpha)) : Iso.P(x, y);
    }

    /// <summary>
    /// A building: its baked KayKit sprite standing on its footprint's centre, with
    /// scaffolding in its place while it's going up. Kinds with no baked sprite fall
    /// back to Tower Defense pieces stacked bottom first.
    /// </summary>
    sealed partial class BuildingSprite : Node2D
    {
        readonly Sprite2D? _built, _scaffold;

        public BuildingSprite(Building b)
        {
            // Sorted by the footprint's front corner, so it draws after anything standing behind it.
            Position = Iso.P(b.X + b.W, b.Y + b.H);
            var centre = Iso.P(b.X + b.W / 2f, b.Y + b.H / 2f) - Position;
            if (Art.Ground(b.Kind) is { } ground)
            {
                var tex = Art.Tex(ground);
                for (int y = 0; y < b.H; y++)
                    for (int x = 0; x < b.W; x++)
                    {
                        var at = Iso.P(b.X + x + 0.5f, b.Y + y + 0.5f) - Position;
                        AddChild(new Sprite2D { Texture = tex, Centered = false, Scale = new Vector2(0.5f, 0.5f), Position = at - new Vector2(33, 16.5f), ZIndex = -1 });
                    }
            }
            if (Art.BakedBuilding(b.Kind.ToString()) is { } baked)
            {
                // Baked at twice the tiles' scale, like the terrain: shown at half.
                _built = new Sprite2D { Texture = baked.Texture, Centered = false, Scale = new Vector2(0.5f, 0.5f), Position = centre - baked.Base * 0.5f, TextureFilter = TextureFilterEnum.LinearWithMipmaps };
                AddChild(_built);
                if (Art.BakedBuilding($"scaffold-{Math.Clamp(Math.Max(b.W, b.H), 1, 3)}") is { } scaffold)
                {
                    _scaffold = new Sprite2D { Texture = scaffold.Texture, Centered = false, Scale = new Vector2(0.5f, 0.5f), Position = centre - scaffold.Base * 0.5f, TextureFilter = TextureFilterEnum.LinearWithMipmaps };
                    AddChild(_scaffold);
                }
                Show(b.Complete);
                return;
            }
            float lift = 0;
            foreach (var path in Art.BuildingPieces(b.Kind))
            {
                var tex = Art.Tex(path);
                float w = tex.GetWidth(), h = tex.GetHeight();
                // Scaled so the piece spans that share of the footprint's diamond width.
                float scale = Art.Fill(b.Kind) * (b.W + b.H) * Iso.HalfW / w;
                // A piece's base diamond is its bottom w/2 pixels; its centre sits w/4 above the bottom edge.
                var sprite = new Sprite2D { Texture = tex, Centered = false, Scale = new Vector2(scale, scale) };
                sprite.Position = centre + new Vector2(-w / 2 * scale, -(h - w / 4) * scale - lift);
                AddChild(sprite);
                lift += (h - w / 2) * scale; // the next piece stands on this one's top
            }
        }

        /// <summary>Scaffolding until it's finished, the building after.</summary>
        public void Show(bool complete)
        {
            if (_built != null) _built.Visible = complete || _scaffold == null;
            if (_scaffold != null) _scaffold.Visible = !complete;
        }
    }

    /// <summary>On the ground: footprints, Hellgates, sleeping packs.</summary>
    sealed partial class Ground : Node2D
    {
        public WorldView View = null!;

        public override void _Draw()
        {
            var world = View.World;
            var font = ThemeDB.FallbackFont;
            foreach (var g in world.Gates)
            {
                if (!g.Alive) continue;
                DrawColoredPolygon(Iso.Diamond(g.X, g.Y, Hellgate.Size, Hellgate.Size), new Color(0.25f, 0f, 0.06f, 0.9f));
                DrawColoredPolygon(Iso.Diamond(g.X + 0.6f, g.Y + 0.6f, Hellgate.Size - 1.2f, Hellgate.Size - 1.2f), new Color(0.85f, 0.1f, 0.3f, 0.9f));
            }
            // Your soldiers stand on blue rings, so they can be found in a crowd.
            foreach (var u in world.Units)
            {
                var feet = new Vector2(Mathf.Lerp(u.PrevX, u.X, View.State.Alpha), Mathf.Lerp(u.PrevY, u.Y, View.State.Alpha));
                Iso.Ellipse(this, feet, 0.32f, new Color(0, 0, 0, 0.35f), filled: true);
                Iso.Ellipse(this, feet, 0.34f, new Color(0.35f, 0.7f, 1f, 0.95f), 2);
            }

            // Crops on the Farms' fields: furrows once sown, green shoots once watered, gold when ripe.
            int tw = world.Terrain.Width;
            foreach (var (tile, stage) in View.State.Farmers.Crops)
            {
                float cx = tile % tw, cy = tile / tw;
                Iso.Ellipse(this, new Vector2(cx + 0.5f, cy + 0.5f), 0.42f, new Color(0.36f, 0.25f, 0.13f, 0.75f), filled: true);
                for (int k = 0; k < 5; k++)
                {
                    uint hash = (uint)(tile * 2654435761u + k * 40503u);
                    var at = Iso.P(cx + 0.22f + (hash % 97) / 97f * 0.56f, cy + 0.22f + (hash / 97 % 89) / 89f * 0.56f);
                    if (stage == 1) DrawCircle(at, 1.3f, new Color(0.22f, 0.14f, 0.07f));
                    else if (stage == 2) DrawLine(at, at + new Vector2(0.5f, -4), new Color(0.45f, 0.8f, 0.3f), 1.6f);
                    else
                    {
                        DrawLine(at, at + new Vector2(0.8f, -8), new Color(0.78f, 0.66f, 0.25f), 1.6f);
                        DrawCircle(at + new Vector2(0.8f, -8.5f), 1.6f, new Color(0.93f, 0.8f, 0.35f));
                    }
                }
            }

            // A mass of demons darkens the ground under it: a shadow per crowded square, deeper the more there are.
            var crowd = View.State.Density;
            if (crowd.Length > 0)
            {
                int cs = HordeColumns.Cell, cols = world.Terrain.Width / cs;
                for (int i = 0; i < crowd.Length; i++)
                {
                    int n = crowd[i];
                    if (n < 8) continue;
                    float cx = (i % cols) * cs + cs / 2f, cy = (i / cols) * cs + cs / 2f;
                    if (!world.Vision.IsVisible(cx, cy)) continue;
                    Iso.Ellipse(this, new Vector2(cx, cy), cs * 0.75f, new Color(0.08f, 0.02f, 0.02f, Mathf.Min(0.42f, n / 90f)), filled: true);
                }
            }

            // Where demons fell: dark splashes that fade.
            foreach (var (x, y, size, age) in View.State.Marks)
            {
                float a = 1 - (float)(age / ClientState.MarkLife);
                Iso.Ellipse(this, new Vector2(x, y), size, new Color(0.22f, 0.04f, 0.03f, 0.55f * a), filled: true);
            }

            // F4: noise, one cell at a time, in the cell's diamond: orange as it builds, red at waking level.
            if (View.State.ShowNoise)
            {
                var noise = world.Noise;
                int cs = noise.CellSize;
                for (int cy = 0; cy < noise.Height; cy++)
                    for (int cx = 0; cx < noise.Width; cx++)
                    {
                        float level = noise.Level[cy * noise.Width + cx];
                        if (level < 0.15f * Balance.WakeThreshold) continue;
                        float t = Mathf.Clamp(level / Balance.WakeThreshold, 0, 1);
                        var colour = level >= Balance.WakeThreshold ? new Color(1, 0.2f, 0.15f, 0.35f) : new Color(1, 0.6f, 0.2f, 0.08f + 0.2f * t);
                        DrawColoredPolygon(Iso.Diamond(cx * cs, cy * cs, cs, cs), colour);
                    }
            }

            // Sleeping packs are drawn as the demons themselves (HordeRenderer). With a building
            // armed, the ground each keeps clear is ringed, so it's plain why you can't build there.
            if (View.State.Armed != null)
                foreach (var p in world.Packs)
                {
                    if (p.Awake || !world.Vision.IsExplored(p.X, p.Y)) continue;
                    float density = world.Rules.Wilds.SleepDensity;
                    float r = p.Stray ? p.Spread(density) + 2 : Mathf.Max(world.Rules.Wilds.ClearRadius, p.Spread(density) + 2);
                    var c = new Vector2(p.X + 0.5f, p.Y + 0.5f);
                    Iso.Ellipse(this, c, r, new Color(0.85f, 0.12f, 0.10f, 0.08f), filled: true);
                    Iso.Ellipse(this, c, r, new Color(0.85f, 0.12f, 0.10f, 0.5f), 1.5f);
                }
        }
    }

    /// <summary>What's under the cursor out in the wilds: an awake demon in sight, a sleeping pack once seen, or a ruin.</summary>
    static string? HoverText(World world, Vector2 at)
    {
        var h = world.Horde;
        int best = -1;
        float bestD = 0.8f * 0.8f;
        for (int i = 0; i < h.Count; i++)
        {
            float dx = h.X[i] - at.X, dy = h.Y[i] - at.Y, d = dx * dx + dy * dy;
            if (d < bestD && world.Vision.IsVisible(h.X[i], h.Y[i])) { bestD = d; best = i; }
        }
        if (best >= 0) return h.Kind[best].ToString();
        float density = world.Rules.Wilds.SleepDensity;
        foreach (var p in world.Packs)
        {
            if (p.Awake || !world.Vision.IsExplored(p.X, p.Y)) continue;
            float r = p.Spread(density), dx = p.X + 0.5f - at.X, dy = p.Y + 0.5f - at.Y;
            if (dx * dx + dy * dy <= r * r)
            {
                int plain = p.Count - p.EliteCount;
                string main = $"{plain} {p.Kind}{(plain == 1 ? "" : "s")}";
                return p.EliteCount > 0 ? $"Sleeping: {main} and {p.EliteCount} {p.EliteKind}{(p.EliteCount == 1 ? "" : "s")}" : $"Sleeping: {main}";
            }
        }
        foreach (var ruin in world.Ruins)
        {
            float dx = ruin.X + 0.5f - at.X, dy = ruin.Y + 0.5f - at.Y;
            if (dx * dx + dy * dy <= 6 && world.Vision.IsExplored(ruin.X, ruin.Y)) return ruin.Looted ? "Ruins (looted)" : $"Ruins: {ruin.Loot}";
        }
        return null;
    }

    /// <summary>Above everything: what state things are in, and what the player is doing.</summary>
    sealed partial class Overlay : Node2D
    {
        public WorldView View = null!;
        (BuildingKind, int, int, int) _cutKey;
        (int, int) _cut;

        public override void _Draw()
        {
            var world = View.World;
            var state = View.State;
            var font = ThemeDB.FallbackFont;

            foreach (var b in world.Buildings)
            {
                var top = Iso.P(b.X, b.Y);
                var front = Iso.P(b.X + b.W, b.Y + b.H);
                float width = (b.W + b.H) * Iso.HalfW;
                var barAt = new Vector2(front.X - width / 4, front.Y + 3);
                if (state.SelectedBuilding == b.Id || state.SelectedGroup.Contains(b.Id)) DrawPolyline([.. Iso.Diamond(b.X, b.Y, b.W, b.H), top], Palette.Selected, 2);
                if (!b.Complete) Bar(barAt, width / 2, b.Def.BuildSeconds <= 0 ? 1 : b.Built / b.Def.BuildSeconds, new Color(0.95f, 0.9f, 0.3f));
                if (b.Upgrading && b.Def.UpgradesTo is { } up) Bar(barAt, width / 2, b.UpgradeProgress / Math.Max(1, world.Def(up).BuildSeconds), new Color(0.6f, 0.8f, 1f));
                if (b.Hp < b.Def.Hp) Bar(barAt + new Vector2(0, 4), width / 2, b.Hp / b.Def.Hp, new Color(0.95f, 0.25f, 0.2f));
                if (b.Repairing)
                {
                    // Mending: a small green cross beside the health bar.
                    var at = barAt + new Vector2(width / 4 + 6, 5);
                    DrawLine(at - new Vector2(3, 0), at + new Vector2(3, 0), new Color(0.4f, 1, 0.4f), 2);
                    DrawLine(at - new Vector2(0, 3), at + new Vector2(0, 3), new Color(0.4f, 1, 0.4f), 2);
                }
                string tag = b.Possessed ? $"POSSESSED x{b.Occupants}" : b.Paused ? "on hold" : b.Complete && !b.Active && b.OnGround ? "no crew" : ""; // off holy ground shows as darkness, not a label
                if (tag.Length > 0) Text(font, front + new Vector2(-24, 18), tag, 11, b.Possessed ? new Color(1, 0.7f, 1) : new Color(1, 0.65f, 0.6f));
            }

            // What's under the cursor, by name: the art is placeholder and not every building is obvious.
            if (state.Armed == null && world.BuildingById(world.BuildingIdAt(state.HoveredTile.X, state.HoveredTile.Y)) is { } hovered)
                Text(font, state.MouseWorld + new Vector2(14, -6), hovered.Kind.ToString(), 14, Colors.White);
            else if (state.Armed == null && !state.AttackMoveArmed && HoverText(world, Iso.Tile(state.MouseWorld)) is { } what)
                Text(font, state.MouseWorld + new Vector2(14, -6), what, 14, new Color(1, 0.7f, 0.65f));

            foreach (var (shot, age) in state.Shots)
            {
                float a = 1 - (float)(age / ClientState.ShotLife);
                var from = Iso.P(shot.FromX, shot.FromY) - new Vector2(0, shot.FromUnit ? 12 : 30);
                var to = Iso.P(shot.ToX, shot.ToY) - new Vector2(0, 8);
                DrawLine(from, to, new Color(Palette.Tracer, a), shot.FromUnit ? 1 : 2);
                if (shot.Splash > 0) Iso.Ellipse(this, new Vector2(shot.ToX, shot.ToY), shot.Splash, new Color(1, 0.6f, 0.2f, a), 2);
            }

            // Trees whose felling would open a way in: an amber ring, pulsing.
            float pulse = 0.55f + 0.35f * Mathf.Sin((float)Time.GetTicksMsec() / 180f);
            foreach (int tree in state.EndangeredTrees)
            {
                int tw = world.Terrain.Width;
                Iso.Ellipse(this, new Vector2(tree % tw + 0.5f, tree / tw + 0.5f), 0.7f, new Color(1, 0.7f, 0.25f, pulse), 2.5f);
            }

            DrawColumns(state, font);

            // A boat's net: spreading on the water as it's cast, drawn in as it's hauled (with the catch in it); a "+" over the Fishery as it lands.
            foreach (var boat in state.Fishers.All)
            {
                if (boat.Doing is not (Fishers.Work.Cast or Fishers.Work.Haul)) continue;
                float t = boat.Doing == Fishers.Work.Cast ? 1 - boat.Timer / Fishers.CastSeconds : boat.Timer / Fishers.HaulSeconds;
                float r = 0.2f + 1.1f * Mathf.Clamp(t, 0, 1);
                var at = new Vector2(boat.X + 0.6f, boat.Y + 0.6f);
                var net = new Color(0.9f, 0.85f, 0.65f, 0.75f);
                Iso.Ellipse(this, at, r, net, 1.2f);
                Iso.Ellipse(this, at, r * 0.55f, net, 1f);
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.Tau / 8;
                    DrawLine(Iso.P(at), Iso.P(at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r), net, 1f);
                }
                if (boat.Doing == Fishers.Work.Haul)
                    for (int k = 0; k < 4; k++)
                    {
                        float a = (float)Time.GetTicksMsec() / 300f + k * 1.7f;
                        DrawCircle(Iso.P(at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.4f), 1.8f, new Color(0.8f, 0.85f, 0.95f));
                    }
            }
            // A hunter's arrow: a short streak from the bow into the trees, once a draw; "+ meat" over the lodge when he's home.
            foreach (var h in state.Hunters.All)
            {
                if (h.Doing != Hunters.Work.Shoot || h.Tree < 0) continue;
                int hw = world.Terrain.Width;
                float t = (float)(Time.GetTicksMsec() / 600.0 + h.Id * 0.37) % 1f;
                if (t < 0.55f) continue;
                var from = Iso.P(h.X, h.Y) - new Vector2(0, 16);
                var to = Iso.P(h.Tree % hw + 0.5f, h.Tree / hw + 0.5f) - new Vector2(0, 14);
                var head = from.Lerp(to, (t - 0.55f) / 0.45f);
                DrawLine(head - (to - from).Normalized() * 7, head, new Color(0.95f, 0.9f, 0.7f), 1.5f);
            }
            foreach (var (at, age) in state.Hunters.Brought)
                Text(font, Iso.P(at) + new Vector2(-12, -30 - age * 18), "+ meat", 13, new Color(1, 0.85f, 0.75f, 1 - age / 1.6f));
            foreach (var (at, age) in state.Fishers.Landed)
                Text(font, Iso.P(at) + new Vector2(-10, -30 - age * 18), "+ fish", 13, new Color(0.9f, 0.95f, 1f, 1 - age / 1.6f));

            // A farmer watering: drops falling from the can onto the tile.
            foreach (var h in state.Farmers.All)
            {
                if (h.Doing != Farmers.Work.Water || h.Tile < 0) continue;
                int fw = world.Terrain.Width;
                var to = Iso.P(h.Tile % fw + 0.5f, h.Tile / fw + 0.5f);
                var from = Iso.P(h.X, h.Y) - new Vector2(0, 14);
                for (int k = 0; k < 4; k++)
                {
                    float t = ((float)Time.GetTicksMsec() / 600f + k * 0.25f) % 1f;
                    DrawCircle(from.Lerp(to, t) - new Vector2(0, 10 * t * (1 - t)), 1.4f, new Color(0.5f, 0.75f, 1f, 0.9f));
                }
            }

            foreach (var (px, py, attack, age) in state.OrderPings)
            {
                float t = (float)(age / 0.5);
                Iso.Ellipse(this, new Vector2(px, py), 0.9f * (1 - t) + 0.2f, attack ? new Color(1, 0.35f, 0.3f, 1 - t) : new Color(0.4f, 1, 0.45f, 1 - t), 2);
            }

            // Spit: a green gob on a low arc, with a splash where it lands.
            foreach (var (spit, age) in state.Spits)
            {
                float t = (float)(age / 0.45);
                var from = Iso.P(spit.FromX, spit.FromY) - new Vector2(0, 14);
                var to = Iso.P(spit.ToX, spit.ToY) - new Vector2(0, 10);
                var at = from.Lerp(to, t) - new Vector2(0, 40 * t * (1 - t) * 4 * 0.5f);
                DrawCircle(at, 3.5f, new Color(0.6f, 0.95f, 0.2f, 0.95f));
                if (t > 0.85f) Iso.Ellipse(this, new Vector2(spit.ToX, spit.ToY), 0.5f, new Color(0.6f, 0.95f, 0.2f, 0.6f), 2);
            }

            foreach (var (howl, age) in state.Howls)
            {
                float t = (float)(age / 1.2);
                Iso.Ellipse(this, new Vector2(howl.X, howl.Y), howl.Radius * 0.5f * t, new Color(0.9f, 0.3f, 0.9f, 0.6f * (1 - t)), 1.5f);
            }

            foreach (var (burst, age) in state.Bursts)
            {
                float t = (float)(age / 0.4);
                Iso.Ellipse(this, new Vector2(burst.X, burst.Y), burst.Radius * (0.4f + 0.6f * t), new Color(0.6f, 0.9f, 0.2f, 0.5f * (1 - t)), filled: true);
            }

            foreach (var u in world.Units)
            {
                var feet = new Vector2(Mathf.Lerp(u.PrevX, u.X, state.Alpha), Mathf.Lerp(u.PrevY, u.Y, state.Alpha));
                var p = Iso.P(feet);
                if (state.SelectedUnits.Contains(u.Id)) Iso.Ellipse(this, feet, 0.45f, Palette.Selected, 1.5f);
                if (u.Hp < u.MaxHp) Bar(p + new Vector2(-10, -Art.UnitSize - 5), 20, u.Hp / u.MaxHp, new Color(0.3f, 1, 0.3f));
                // Rank: a gold chevron per rank over the head.
                for (int r = 0; r < u.Rank; r++)
                {
                    var c = p + new Vector2(-4 * (u.Rank - 1) + r * 8, -Art.UnitSize - 11);
                    DrawPolyline([c + new Vector2(-3, 0), c + new Vector2(0, -3), c + new Vector2(3, 0)], UiKit.Gold, 1.6f);
                }
            }

            // Where the selected soldiers are going, and what's queued after: a line through each point (red for attack-moves).
            foreach (var u in world.Units)
            {
                if (!state.SelectedUnits.Contains(u.Id) || u.Order is not (OrderKind.Move or OrderKind.AttackMove or OrderKind.Patrol)) continue;
                var at = Iso.P(u.X, u.Y);
                var leg = Iso.P(u.SlotX, u.SlotY);
                var colour = u.Order == OrderKind.Move ? new Color(0.4f, 1, 0.45f, 0.35f) : new Color(1, 0.4f, 0.3f, 0.35f);
                DrawLine(at, leg, colour, 1);
                foreach (var p in u.Waypoints)
                {
                    var next = Iso.P(p.SlotX, p.SlotY);
                    DrawLine(leg, next, p.Order == OrderKind.Move ? new Color(0.4f, 1, 0.45f, 0.35f) : new Color(1, 0.4f, 0.3f, 0.35f), 1);
                    DrawCircle(next, 2.5f, p.Order == OrderKind.Move ? new Color(0.4f, 1, 0.45f, 0.7f) : new Color(1, 0.4f, 0.3f, 0.7f));
                    leg = next;
                }
            }

            // The selected Barracks' rally point: a line from its door and a flag.
            if (state.SelectedBuilding is { } rs && world.BuildingById(rs) is { RallyX: >= 0 } rally)
            {
                var from = Iso.P(rally.CentreX, rally.CentreY);
                var to = Iso.P(rally.RallyX + 0.5f, rally.RallyY + 0.5f);
                DrawDashedLine(from, to, new Color(UiKit.Gold, 0.8f), 1.5f, 6);
                DrawLine(to, to - new Vector2(0, 22), UiKit.Text, 2);
                DrawColoredPolygon([to - new Vector2(0, 22), to + new Vector2(12, -18), to - new Vector2(0, 14)], UiKit.Gold);
            }

            if (state.SelectedBuilding is { } sb && world.BuildingById(sb) is { Def.Weapon: { } w } tower)
                Iso.Ellipse(this, new Vector2(tower.CentreX, tower.CentreY), w.Range, new Color(1, 1, 1, 0.4f), 1);
            if (state.SelectedBuilding is { } sb2 && world.BuildingById(sb2) is { Def.SlowRadius: > 0 } bell)
                Iso.Ellipse(this, new Vector2(bell.CentreX, bell.CentreY), bell.Def.SlowRadius, new Color(0.6f, 0.8f, 1, 0.4f), 1);

            if (state.AttackMoveArmed)
            {
                Iso.Ellipse(this, new Vector2(state.HoveredTile.X + 0.5f, state.HoveredTile.Y + 0.5f), 0.45f, new Color(1, 0.35f, 0.3f, 0.9f), 2);
                Text(font, state.MouseWorld + new Vector2(14, -6), state.PatrolArmed ? "Patrol" : "Attack-move", 14, new Color(1, 0.55f, 0.5f));
            }

            if (state.DragStart is { } ds && state.Armed == null && !state.AttackMoveArmed)
            {
                var r = new Rect2(ds, state.MouseWorld - ds).Abs();
                DrawRect(r, new Color(0.35f, 1, 0.35f, 0.08f));
                DrawRect(r, Palette.Selected, filled: false, width: 1);
            }

            if (state.Armed is { } kind)
            {
                var def = world.Rules[kind];
                foreach (var (tx, ty) in state.GhostTiles())
                {
                    string? why = world.CheckPlacement(kind, tx, ty);
                    DrawColoredPolygon(Iso.Diamond(tx, ty, def.W, def.H), why == null ? Palette.GhostOk : Palette.GhostBad);
                }
                var (hx, hy) = state.HoveredTile;
                string? reason = world.CheckPlacement(kind, hx, hy);
                double free = 1;
                string note = reason ?? (def.Produces is { } res ? $"+{world.EstimateGathering(kind, hx, hy, out free):0.00} {res.ToString().ToLowerInvariant()}/s" : "");
                // Gatherers split their ground: say so when most of it is already worked, before a crew is wasted on it.
                if (reason == null && free < 0.6) note += free <= 0.05 ? "  (all its ground is already worked)" : $"  (only {free:P0} of its ground is free)";
                // Lanes: warn before a building shuts soldiers out of part of the town (a building, or a patch of holy ground).
                if (reason == null)
                {
                    if (_cutKey != (kind, hx, hy, world.Tick / 20)) { _cutKey = (kind, hx, hy, world.Tick / 20); _cut = world.CutOff(kind, hx, hy); }
                    var (cutBuildings, cutTiles) = _cut;
                    if (kind is BuildingKind.Wall or BuildingKind.StoneWall) cutTiles = 0; // walls are meant to shut ground out; only a building cut off matters
                    if (cutBuildings > 0 || cutTiles >= 4)
                    {
                        string what = cutBuildings > 0 ? $"{cutBuildings} building{(cutBuildings == 1 ? "" : "s")}" : $"{cutTiles} tiles of holy ground";
                        note += (note.Length > 0 ? "  " : "") + $"(soldiers couldn't reach {what})";
                        free = 0; // amber, like the shared-ground warning
                    }
                }
                if (def.Weapon is { } weapon) Iso.Ellipse(this, new Vector2(hx + def.W / 2f, hy + def.H / 2f), weapon.Range, new Color(1, 1, 1, 0.35f), 1);
                if (def.SlowRadius > 0) Iso.Ellipse(this, new Vector2(hx + def.W / 2f, hy + def.H / 2f), def.SlowRadius, new Color(0.6f, 0.8f, 1, 0.35f), 1);
                if (note.Length > 0) Text(font, Iso.P(hx + def.W, hy) + new Vector2(8, 0), note, 13, reason != null ? new Color(1, 0.6f, 0.6f) : free < 0.6 ? new Color(1, 0.8f, 0.4f) : Colors.White);
            }
        }

        void Bar(Vector2 at, float width, float fraction, Color colour)
        {
            DrawRect(new Rect2(at, new Vector2(width, 3)), new Color(0, 0, 0, 0.7f));
            DrawRect(new Rect2(at, new Vector2(width * Mathf.Clamp(fraction, 0, 1), 3)), colour);
        }

        /// <summary>
        /// A horned head over the centre of each wave column marching in, with how many are in it, so you can
        /// see where they're coming from and meet them. Off screen, it waits at the screen's edge, pointing.
        /// Drawn at a constant size on screen, whatever the zoom; seen through fog (the Keep's scouts cry it).
        /// </summary>
        void DrawColumns(ClientState state, Font font)
        {
            if (state.Columns.Count == 0) return;
            var toWorld = GetCanvasTransform().AffineInverse();
            var screen = GetViewportRect();
            float scale = toWorld.X.Length(); // world pixels per screen pixel
            var inset = new Vector2(46, 46);
            var lo = toWorld * (screen.Position + inset + new Vector2(0, 30)); // clear of the top bar
            var hi = toWorld * (screen.End - inset - new Vector2(0, 150)); // and of the build card
            float pulse = 0.8f + 0.2f * Mathf.Sin((float)Time.GetTicksMsec() / 220f);
            foreach (var (_, centre, count) in state.Columns)
            {
                var at = Iso.P(centre) - new Vector2(0, 40 * scale);
                var pinned = new Vector2(Mathf.Clamp(at.X, lo.X, hi.X), Mathf.Clamp(at.Y, lo.Y, hi.Y));
                bool off = pinned != at;
                float r = 14 * scale;
                var red = new Color(0.95f, 0.22f, 0.15f);
                if (off)
                {
                    // An arrowhead from the pinned badge toward where the column really is.
                    var dir = (at - pinned).Normalized();
                    var tip = pinned + dir * (r + 14 * scale);
                    var side = new Vector2(-dir.Y, dir.X) * 7 * scale;
                    DrawColoredPolygon([tip, pinned + dir * (r + 3 * scale) + side, pinned + dir * (r + 3 * scale) - side], red);
                }
                // Horns, then the head over them, then two ember eyes.
                var hornL = new[] { pinned + new Vector2(-r * 0.8f, -r * 0.3f), pinned + new Vector2(-r * 1.25f, -r * 1.45f), pinned + new Vector2(-r * 0.25f, -r * 0.75f) };
                var hornR = new[] { pinned + new Vector2(r * 0.8f, -r * 0.3f), pinned + new Vector2(r * 1.25f, -r * 1.45f), pinned + new Vector2(r * 0.25f, -r * 0.75f) };
                DrawColoredPolygon(hornL, new Color(0.9f, 0.85f, 0.75f));
                DrawColoredPolygon(hornR, new Color(0.9f, 0.85f, 0.75f));
                DrawCircle(pinned, r + 2 * scale, new Color(0, 0, 0, 0.6f));
                DrawCircle(pinned, r, new Color(0.35f, 0.04f, 0.03f));
                DrawArc(pinned, r, 0, Mathf.Tau, 24, new Color(red, pulse), 2 * scale);
                DrawCircle(pinned + new Vector2(-r * 0.38f, -r * 0.1f), r * 0.17f, new Color(1, 0.75f, 0.2f, pulse));
                DrawCircle(pinned + new Vector2(r * 0.38f, -r * 0.1f), r * 0.17f, new Color(1, 0.75f, 0.2f, pulse));
                DrawLine(pinned + new Vector2(-r * 0.35f, r * 0.45f), pinned + new Vector2(r * 0.35f, r * 0.45f), new Color(1, 0.75f, 0.2f, 0.8f), 1.5f * scale);
                string label = count.ToString();
                int size = (int)Mathf.Round(15 * scale);
                var width = font.GetStringSize(label, fontSize: size).X;
                Text(font, pinned + new Vector2(-width / 2, r + 17 * scale), label, size, new Color(1, 0.8f, 0.75f));
            }
        }

        void Text(Font font, Vector2 at, string text, int size, Color colour)
        {
            DrawString(font, at + Vector2.One, text, fontSize: size, modulate: Colors.Black);
            DrawString(font, at, text, fontSize: size, modulate: colour);
        }
    }
}
