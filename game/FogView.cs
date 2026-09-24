using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Fog of war over the world: black where nothing of the colony's has ever
/// looked, a haze where it has looked but isn't looking now, clear in sight.
/// One texel per tile, sheared into the isometric diamond and filtered, so
/// the edge of sight is soft. Repainted five times a second from the sim's
/// Vision (it's cheap: one byte loop over the map).
/// </summary>
public partial class FogView : Node2D
{
    public World World = null!;

    Image _image = null!;
    ImageTexture _texture = null!;
    byte[] _pixels = [];
    double _clock = 1;

    public override void _Ready()
    {
        int n = World.Terrain.Width;
        _pixels = new byte[n * n * 4];
        _image = Image.CreateFromData(n, n, false, Image.Format.Rgba8, _pixels);
        _texture = ImageTexture.CreateFromImage(_image);
        TextureFilter = TextureFilterEnum.Linear;
        Visible = World.Vision.Enabled;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _clock += delta;
        if (_clock < 0.2) return;
        _clock = 0;
        var v = World.Vision;
        for (int i = 0, o = 0; i < v.Explored.Length; i++, o += 4)
        {
            _pixels[o] = 6; _pixels[o + 1] = 6; _pixels[o + 2] = 10;
            _pixels[o + 3] = !v.Explored[i] ? (byte)255 : v.Visible[i] ? (byte)0 : (byte)120;
        }
        _image.SetData(_image.GetWidth(), _image.GetHeight(), false, Image.Format.Rgba8, _pixels);
        _texture.Update(_image);
        QueueRedraw();
    }

    public override void _Draw()
    {
        // Texel (u, v) covers tile (u, v): the tile's x axis runs (HalfW, HalfH) on screen, its y axis (-HalfW, HalfH).
        DrawSetTransformMatrix(new Transform2D(new Vector2(Iso.HalfW, Iso.HalfH), new Vector2(-Iso.HalfW, Iso.HalfH), Vector2.Zero));
        DrawTexture(_texture, Vector2.Zero);
        DrawSetTransformMatrix(Transform2D.Identity);
    }
}
