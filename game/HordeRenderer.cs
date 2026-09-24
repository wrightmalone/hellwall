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
    readonly float _tilePx;

    sealed class Layer
    {
        public required MultiMesh Mesh;
        public float[] Buffer = [];
        public int Capacity;
    }

    public HordeRenderer(float tilePx)
    {
        _tilePx = tilePx;
        var kinds = Enum.GetValues<DemonKind>();
        _layers = new Layer[kinds.Length];
        foreach (var kind in kinds)
        {
            var (size, color) = kind switch
            {
                DemonKind.Hound => (0.55f, new Color(1.0f, 0.55f, 0.15f)),
                _ => (0.62f, new Color(0.85f, 0.12f, 0.10f)),
            };
            var mesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
                Mesh = new QuadMesh { Size = new Vector2(size * tilePx, size * tilePx) },
            };
            _layers[(int)kind] = new Layer { Mesh = mesh };
            AddChild(new MultiMeshInstance2D { Multimesh = mesh, Modulate = color });
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
            float x = (horde.PrevX[i] + (horde.X[i] - horde.PrevX[i]) * alpha) * _tilePx;
            float y = (horde.PrevY[i] + (horde.Y[i] - horde.PrevY[i]) * alpha) * _tilePx;
            int o = counts[k]++ * FloatsPerInstance;
            var b = layer.Buffer;
            b[o] = 1; b[o + 1] = 0; b[o + 2] = 0; b[o + 3] = x;
            b[o + 4] = 0; b[o + 5] = 1; b[o + 6] = 0; b[o + 7] = y;
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
