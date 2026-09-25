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
        // Woodsmen: a militiaman's figure, a size smaller; chopping, he faces the tree and swings (the walk cycle, slowed).
        var w = World.Terrain.Width;
        foreach (var m in World.Woodsmen)
            Figure(seen, step, m.Id, Art.Unit(UnitKind.Militia), Art.UnitScale * 0.8f, m.X, m.Y, m.PrevX, m.PrevY,
                m.State == WoodsmanState.Chopping && m.Tree >= 0 ? new Vector2(m.Tree % w + 0.5f - m.X, m.Tree / w + 0.5f - m.Y) : null);
        foreach (var id in _units.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _units[id].QueueFree();
            _units.Remove(id);
            _facing.Remove(id);
        }
    }

    void Figure(HashSet<int> seen, int step, int id, string sheet, float scale, float x, float y, float prevX, float prevY, Vector2? chopping)
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
        float dx = x - prevX, dy = y - prevY;
        bool moving = dx * dx + dy * dy > 1e-6f;
        if (chopping is { } at) _facing[id] = Art.Facing(at.X, at.Y);
        else if (moving) _facing[id] = Art.Facing(dx, dy);
        int facing = _facing.GetValueOrDefault(id, 1);
        int frame = chopping != null ? (step / 2 + id) % 2 * 2 : moving ? (step + id) % Art.Frames : 0;
        sprite.RegionRect = new Rect2(frame * Art.Cell, facing * Art.Cell, Art.Cell, Art.Cell);
        sprite.Position = Iso.P(Mathf.Lerp(prevX, x, State.Alpha), Mathf.Lerp(prevY, y, State.Alpha));
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
                if (state.SelectedBuilding == b.Id) DrawPolyline([.. Iso.Diamond(b.X, b.Y, b.W, b.H), top], Palette.Selected, 2);
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
                string tag = b.Possessed ? $"POSSESSED x{b.Occupants}" : b.Complete && !b.Active && b.OnGround ? "no crew" : ""; // off holy ground shows as darkness, not a label
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

        void Text(Font font, Vector2 at, string text, int size, Color colour)
        {
            DrawString(font, at + Vector2.One, text, fontSize: size, modulate: Colors.Black);
            DrawString(font, at, text, fontSize: size, modulate: colour);
        }
    }
}
