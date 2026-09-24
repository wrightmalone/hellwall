using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Draws the whole horde with one MultiMesh per demon kind: one draw call per
/// kind however many demons there are. Each frame it writes every demon's
/// position (interpolated between the last two ticks), facing and walk frame
/// into a float buffer and hands it to the RenderingServer; a small shader
/// (HordeSheet.gdshader) picks that cell of the kind's baked sheet. A soft
/// shadow under every demon, one more MultiMesh, grounds them on the terrain.
/// </summary>
public partial class HordeRenderer : Node2D
{
    /// <summary>Godot's 2D instance layout: x.x, y.x, pad, origin.x, x.y, y.y, pad, origin.y, then 4 floats of custom data.</summary>
    const int Floats = 12;
    const int ShadowFloats = 8;

    readonly Layer[] _layers;
    readonly Layer _shadows;
    /// <summary>Screen pixels from a demon's feet up to its quad's centre, per kind.</summary>
    readonly float[] _lift;
    double _time;

    sealed class Layer
    {
        public required MultiMesh Mesh;
        public required int Stride;
        public float[] Buffer = [];
        public int Capacity;
    }

    public HordeRenderer(int mapTiles)
    {
        var kinds = Enum.GetValues<DemonKind>();
        _layers = new Layer[kinds.Length];
        _lift = new float[kinds.Length];
        // A fixed box around the whole map. Otherwise Godot culls the
        // MultiMesh by bounds it computed from an earlier buffer (an empty
        // one, or a horde far away), and a horde that has since walked on
        // screen isn't drawn. It also spares recomputing 20k bounds a frame.
        var bounds = new Aabb(new Vector3(-(mapTiles + 2) * Iso.HalfW, -96, -1), new Vector3((mapTiles + 2) * Iso.HalfW * 2, (mapTiles + 2) * Iso.HalfH * 2 + 192, 2));

        var shadowMesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform2D, Mesh = new QuadMesh { Size = new Vector2(22, 11) }, CustomAabb = bounds };
        _shadows = new Layer { Mesh = shadowMesh, Stride = ShadowFloats };
        AddChild(new MultiMeshInstance2D { Multimesh = shadowMesh, Texture = Art.Shadow() });

        var shader = GD.Load<Shader>("res://HordeSheet.gdshader");
        float cell = Art.Cell * Art.DemonScale;
        foreach (var kind in kinds)
        {
            // The quad is centred on its origin; the feet sit (Feet.Y - Cell/2) below the centre.
            _lift[(int)kind] = (Art.Feet.Y - Art.Cell / 2f) * Art.DemonScale + (kind == DemonKind.Gargoyle ? 18 : 0);
            var mesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
                UseCustomData = true,
                Mesh = new QuadMesh { Size = new Vector2(cell, cell) },
                CustomAabb = bounds,
            };
            _layers[(int)kind] = new Layer { Mesh = mesh, Stride = Floats };
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("cols", (float)Art.Frames);
            material.SetShaderParameter("rows", (float)Art.Directions);
            material.SetShaderParameter("quad", cell);
            AddChild(new MultiMeshInstance2D { Multimesh = mesh, Texture = Art.Tex(Art.Demon(kind)), Material = material, TextureFilter = TextureFilterEnum.LinearWithMipmaps });
        }
    }

    /// <param name="alpha">How far between the previous tick and the current one, 0..1.</param>
    /// <summary>
    /// Sleeping demons on explored ground, standing at their spots: rebuilt
    /// when a pack wakes or more of the map is explored, not every frame.
    /// </summary>
    readonly List<(int Kind, float X, float Y, int Facing)> _sleepers = new();
    int _sleepersRevision = -1, _sleepersAwake = -1;

    void RefreshSleepers(World world)
    {
        int awake = 0;
        foreach (var p in world.Packs) if (p.Awake) awake++;
        if (awake == _sleepersAwake && world.Vision.Revision == _sleepersRevision) return;
        _sleepersAwake = awake;
        _sleepersRevision = world.Vision.Revision;
        _sleepers.Clear();
        float density = world.Rules.Wilds.SleepDensity;
        foreach (var p in world.Packs)
        {
            if (p.Awake) continue;
            for (int i = 0; i < p.Count; i++)
            {
                var (x, y, facing) = p.Spot(i, density);
                if (!world.Vision.IsExplored((int)x, (int)y) || !world.IsWalkable((int)x, (int)y)) continue;
                _sleepers.Add(((int)p.Kind, x, y, facing));
            }
        }
    }

    public void Sync(World world, float alpha, double delta)
    {
        var horde = world.Horde;
        var vision = world.Vision;
        RefreshSleepers(world);
        _time += delta;
        Span<int> counts = stackalloc int[_layers.Length];
        for (int i = 0; i < horde.Count; i++) counts[(int)horde.Kind[i]]++;
        foreach (var s in _sleepers) counts[s.Kind]++;
        for (int k = 0; k < _layers.Length; k++) Reserve(_layers[k], counts[k]);
        Reserve(_shadows, horde.Count + _sleepers.Count);
        var sb = _shadows.Buffer;
        int step = (int)(_time * 10); // walk frames at 10 a second
        int shadows = 0;

        counts.Clear();
        // Asleep: still, where they stand, a slow shuffle of the first two frames now and then.
        for (int n = 0; n < _sleepers.Count; n++)
        {
            var (k, tx, ty, facing) = _sleepers[n];
            float x = (tx - ty) * Iso.HalfW, ground = (tx + ty) * Iso.HalfH;
            int o = counts[k]++ * Floats;
            var b = _layers[k].Buffer;
            b[o] = 1; b[o + 1] = 0; b[o + 2] = 0; b[o + 3] = x;
            b[o + 4] = 0; b[o + 5] = -1; b[o + 6] = 0; b[o + 7] = ground - _lift[k];
            b[o + 8] = (step / 7 + n) % 23 == 0 ? 1 : 0;
            b[o + 9] = facing;
            b[o + 10] = 0; b[o + 11] = 0;
            float big = k is (int)DemonKind.Brute or (int)DemonKind.Broodmother or (int)DemonKind.Bloater ? 1.6f : 1f;
            int so = shadows++ * ShadowFloats;
            sb[so] = big; sb[so + 1] = 0; sb[so + 2] = 0; sb[so + 3] = x;
            sb[so + 4] = 0; sb[so + 5] = big; sb[so + 6] = 0; sb[so + 7] = ground;
        }

        for (int i = 0; i < horde.Count; i++)
        {
            int k = (int)horde.Kind[i];
            if (!vision.IsVisible(horde.X[i], horde.Y[i])) continue; // out of sight: fog hides the horde
            float tx = horde.PrevX[i] + (horde.X[i] - horde.PrevX[i]) * alpha;
            float ty = horde.PrevY[i] + (horde.Y[i] - horde.PrevY[i]) * alpha;
            float x = (tx - ty) * Iso.HalfW, ground = (tx + ty) * Iso.HalfH;
            float vx = horde.VX[i], vy = horde.VY[i];
            bool moving = vx * vx + vy * vy > 0.01f;
            int o = counts[k]++ * Floats;
            var b = _layers[k].Buffer;
            b[o] = 1; b[o + 1] = 0; b[o + 2] = 0; b[o + 3] = x;
            b[o + 4] = 0; b[o + 5] = -1; b[o + 6] = 0; b[o + 7] = ground - _lift[k]; // QuadMesh UVs run bottom-up in 2D
            b[o + 8] = moving ? (step + i) % Art.Frames : 0;
            b[o + 9] = Art.Facing(vx, vy);
            b[o + 10] = 0; b[o + 11] = 0;

            float big = k is (int)DemonKind.Brute or (int)DemonKind.Broodmother or (int)DemonKind.Bloater ? 1.6f : 1f;
            int so = shadows++ * ShadowFloats;
            sb[so] = big; sb[so + 1] = 0; sb[so + 2] = 0; sb[so + 3] = x;
            sb[so + 4] = 0; sb[so + 5] = big; sb[so + 6] = 0; sb[so + 7] = ground;
        }

        for (int k = 0; k < _layers.Length; k++) Upload(_layers[k], counts[k]);
        Upload(_shadows, shadows);
    }

    static void Upload(Layer layer, int count)
    {
        if (layer.Capacity > 0) RenderingServer.MultimeshSetBuffer(layer.Mesh.GetRid(), layer.Buffer);
        layer.Mesh.VisibleInstanceCount = count;
    }

    /// <summary>Grow in doublings: changing InstanceCount reallocates GPU-side, so it must stay rare.</summary>
    static void Reserve(Layer layer, int needed)
    {
        if (needed <= layer.Capacity) return;
        int capacity = Math.Max(1024, layer.Capacity);
        while (capacity < needed) capacity *= 2;
        layer.Capacity = capacity;
        layer.Mesh.InstanceCount = capacity;
        layer.Buffer = new float[capacity * layer.Stride];
    }
}
