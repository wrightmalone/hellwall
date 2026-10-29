using Godot;

namespace Hellwall.Game;

/// <summary>Live inspection of all 240 atlas cells. Uses the same texture and clip durations as the horde.</summary>
public partial class BearGallery : Control
{
    Texture2D _atlas = null!;
    double _time;
    bool _capturing;
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        _atlas = Art.Tex("res://art/bear/bear-animations.png");
        var capture = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--bear-capture="));
        if (capture != null) CaptureFrames(capture["--bear-capture=".Length..]);
    }
    public override void _Process(double delta)
    {
        if (!Visible) return;
        if (!_capturing) _time += delta / Math.Max(0.01, Engine.TimeScale);
        QueueRedraw();
    }
    async void CaptureFrames(string path)
    {
        _capturing = true;
        DirAccess.MakeDirRecursiveAbsolute(path);
        for (int frame = 0; frame < 48; frame++)
        {
            _time = frame / 8.0;
            QueueRedraw();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(path, $"frame-{frame:000}.png"));
        }
        GD.Print("hellwall: bear gallery captured 48 frames at 8 fps");
        GetTree().Quit();
    }
    public override void _Draw()
    {
        var screen = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, screen), new Color(0.055f, 0.075f, 0.065f));
        var font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(26, 38), "CORRUPTED BEAR  /  8 DIRECTIONS x 5 ANIMATIONS x 6 FRAMES", fontSize: 23, modulate: new Color(0.9f, 0.86f, 0.73f));
        DrawString(font, new Vector2(26, 66), "F2: return to live combat   |   Five drawn directions, three mirrored   |   Death holds, then resets here for inspection", fontSize: 17, modulate: new Color(0.65f, 0.73f, 0.65f));
        string[] directions = ["SE", "S", "SW", "W", "NW", "N", "NE", "E"];
        string[] clips = ["IDLE", "WALK", "ATTACK", "HIT", "DEATH"];
        float col = (screen.X - 170) / 8, row = (screen.Y - 135) / 5;
        float size = Math.Min(row - 15, col - 14);
        for (int d = 0; d < 8; d++)
            DrawString(font, new Vector2(170 + d * col + col / 2 - 12, 107), directions[d], fontSize: 18, modulate: Colors.Beige);
        for (int c = 0; c < 5; c++)
        {
            float y = 118 + c * row;
            DrawString(font, new Vector2(26, y + row / 2), clips[c], fontSize: 18, modulate: Colors.Beige);
            double duration = c switch { 0 => 1.875, 1 => 0.86, 2 => BearAnimation.AttackSeconds, 3 => BearAnimation.HitSeconds, _ => BearAnimation.DeathSeconds };
            double cycle = c == 4 ? 2.6 : duration + (c >= 2 ? 0.4 : 0);
            int frame = BearAnimation.OneShot(_time % cycle, duration);
            for (int d = 0; d < 8; d++)
            {
                var dest = new Rect2(170 + d * col + (col - size) / 2, y + (row - size) / 2, size, size);
                DrawTextureRectRegion(_atlas, dest, new Rect2(frame * 96, (c * 8 + d) * 96, 96, 96));
            }
        }
    }
}
