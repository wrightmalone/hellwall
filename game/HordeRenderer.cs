using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Draws the whole horde with one MultiMesh per demon kind: one draw call per
/// kind however many demons there are. Each frame it writes positions, taken
/// straight from the sim's arrays and interpolated between the last two
/// ticks, into a float buffer and hands it to the RenderingServer.
/// </summary>
public partial class HordeRenderer : Node2D
{
    /// <summary>Godot's 2D instance layout: x.x, y.x, pad, origin.x, x.y, y.y, pad, origin.y.</summary>
    const int FloatsPerInstance = 8;

    readonly Layer[] _layers;
    readonly float[] _lift;

    sealed class Layer
    {
        public required MultiMesh Mesh;
        public float[] Buffer = [];
        public int Capacity;
    }

    public HordeRenderer(int mapTiles)
    {
        var kinds = Enum.GetValues<DemonKind>();
        _layers = new Layer[kinds.Length];
        _lift = new float[kinds.Length];
        foreach (var kind in kinds)
        {
            var tex = Art.Tex(Art.Demon(kind));
            float size = Art.DemonSize(kind);
            // Feet on the ground point: the quad is centred on its origin, so lift it half its height (fliers more).
            _lift[(int)kind] = size / 2 + (kind == DemonKind.Gargoyle ? 16 : 0);
            var mesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
                Mesh = new QuadMesh { Size = new Vector2(size, size) },
                // A fixed box around the whole map. Otherwise Godot culls the
                // MultiMesh by bounds it computed from an earlier buffer (an
                // empty one, or a horde far away), and a horde that has since
                // walked on screen isn't drawn. It also spares recomputing the
                // bounds of 20k instances every frame.
                CustomAabb = new Aabb(new Vector3(-(mapTiles + 2) * Iso.HalfW, -64, -1), new Vector3((mapTiles + 2) * Iso.HalfW * 2, (mapTiles + 2) * Iso.HalfH * 2 + 128, 2)),
            };
            _layers[(int)kind] = new Layer { Mesh = mesh };
            AddChild(new MultiMeshInstance2D { Multimesh = mesh, Texture = tex, TextureFilter = TextureFilterEnum.Nearest });
        }
    }

    /// <param name="alpha">How far between the previous tick and the current one, 0..1.</param>
    public void Sync(Horde horde, float alpha)
    {
        Span<int> counts = stackalloc int[_layers.Length];
        for (int i = 0; i < horde.Count; i++) counts[(int)horde.Kind[i]]++;
        for (int k = 0; k < _layers.Length; k++) Reserve(_layers[k], counts[k]);

        counts.Clear();
        for (int i = 0; i < horde.Count; i++)
        {
            int k = (int)horde.Kind[i];
            var layer = _layers[k];
            float tx = horde.PrevX[i] + (horde.X[i] - horde.PrevX[i]) * alpha;
            float ty = horde.PrevY[i] + (horde.Y[i] - horde.PrevY[i]) * alpha;
            float x = (tx - ty) * Iso.HalfW, y = (tx + ty) * Iso.HalfH - _lift[k];
            int o = counts[k]++ * FloatsPerInstance;
            var b = layer.Buffer;
            // Face the way it walks: flip the figure horizontally when it heads screen-left.
            float face = horde.VX[i] - horde.VY[i] < 0 ? -1 : 1;
            b[o] = face; b[o + 1] = 0; b[o + 2] = 0; b[o + 3] = x;
            b[o + 4] = 0; b[o + 5] = -1; b[o + 6] = 0; b[o + 7] = y; // QuadMesh UVs run bottom-up in 2D
        }

        for (int k = 0; k < _layers.Length; k++)
        {
            var layer = _layers[k];
            if (layer.Capacity > 0) RenderingServer.MultimeshSetBuffer(layer.Mesh.GetRid(), layer.Buffer);
            layer.Mesh.VisibleInstanceCount = counts[k];
        }
    }

    /// <summary>Grow in doublings: changing InstanceCount reallocates GPU-side, so it must stay rare.</summary>
    static void Reserve(Layer layer, int needed)
    {
        if (needed <= layer.Capacity) return;
        int capacity = Math.Max(1024, layer.Capacity);
        while (capacity < needed) capacity *= 2;
        layer.Capacity = capacity;
        layer.Mesh.InstanceCount = capacity;
        layer.Buffer = new float[capacity * FloatsPerInstance];
    }
}
