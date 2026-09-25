using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Placeholder music, synthesized at startup like the sound effects: a slow
/// minor pad loop (Am, F, Dm, E; eight seconds each) with a few sparse bell
/// notes over it, and a battle layer (a heartbeat drum and a low drone on the
/// same chords) that swells with how many awake demons are in sight. Both
/// loop together; only the battle layer's volume moves. No files, nothing to
/// license; to be replaced by a real score.
/// </summary>
public partial class Music : Node
{
    public World? World;
    /// <summary>--dump-music=dir: write both loops there as WAV files (to listen to without the game), and print their levels.</summary>
    public static string? DumpTo;

    const int Rate = 22050;
    const double ChordSeconds = 8;
    static readonly double[][] Chords =
    [
        [110.00, 130.81, 164.81], // A minor
        [87.31, 110.00, 130.81],  // F
        [73.42, 87.31, 110.00],   // D minor
        [82.41, 103.83, 123.47],  // E (the major third pulls back to A)
    ];

    readonly AudioStreamPlayer _calm = new(), _battle = new();
    float _intensity;
    double _clock;

    public static float Volume => Settings.Get("music", 0.5f);

    public override void _Ready()
    {
        int n = (int)(Chords.Length * ChordSeconds * Rate);
        var calm = new float[n];
        var battle = new float[n];
        var random = new Random(7);
        double low = 0, lowB = 0; // one-pole low-pass states: the pads are warm, not buzzy
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate;
            int c = (int)(t / ChordSeconds) % Chords.Length;
            double inChord = t - c * ChordSeconds;
            // Crossfade the last and first second of each chord into its neighbour.
            double fade = Math.Clamp(Math.Min(inChord, ChordSeconds - inChord) / 1.2, 0, 1);
            double pad = 0;
            foreach (double hz in Chords[c])
                pad += Saw(hz, t) * 0.5 + Saw(hz * 1.004, t) * 0.5; // two slightly detuned saws per note
            low += (pad * fade * 0.19 - low) * 0.035;
            calm[i] = (float)low;

            // Battle: a double heartbeat at 72 bpm and a drone an octave under the chord's root.
            double beat = t % (60.0 / 72);
            double thump = Math.Sin(2 * Math.PI * 55 * beat) * Math.Exp(-beat * 18) + (beat > 0.22 ? Math.Sin(2 * Math.PI * 50 * (beat - 0.22)) * Math.Exp(-(beat - 0.22) * 20) * 0.7 : 0);
            double drone = Saw(Chords[c][0] / 2, t) * fade;
            lowB += (drone * 0.3 - lowB) * 0.02;
            battle[i] = (float)(thump * 0.35 + lowB * 0.7);
        }
        // Sparse bells over the calm layer: a pentatonic note now and then.
        double[] bell = [440, 523.25, 587.33, 659.25, 783.99];
        for (double at = 2; at < n / (double)Rate - 3; at += 3 + random.NextDouble() * 4)
        {
            double hz = bell[random.Next(bell.Length)];
            for (int k = 0; k < Rate * 3; k++)
            {
                int i = (int)(at * Rate) + k;
                if (i >= n) break;
                double t = k / (double)Rate;
                calm[i] += (float)(Math.Sin(2 * Math.PI * hz * t) * Math.Exp(-t * 2.2) * 0.07);
            }
        }
        _calm.Stream = Stream(calm);
        _battle.Stream = Stream(battle);
        if (DumpTo is { } dir)
        {
            ((AudioStreamWav)_calm.Stream).SaveToWav(System.IO.Path.Combine(dir, "music-calm.wav"));
            ((AudioStreamWav)_battle.Stream).SaveToWav(System.IO.Path.Combine(dir, "music-battle.wav"));
            GD.Print($"hellwall-music: calm peak {calm.Max(MathF.Abs):0.00} rms {MathF.Sqrt(calm.Average(v => v * v)):0.000}, battle peak {battle.Max(MathF.Abs):0.00} rms {MathF.Sqrt(battle.Average(v => v * v)):0.000}");
        }
        _battle.VolumeDb = -80;
        AddChild(_calm);
        AddChild(_battle);
        Apply();
        _calm.Play();
        _battle.Play();
    }

    /// <summary>Volumes from the settings (music times master).</summary>
    public void Apply() => _calm.VolumeDb = Db(Volume * Sound.Volume);

    static float Db(float linear) => linear <= 0.001f ? -80 : Mathf.LinearToDb(linear);

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_clock >= 0.5)
        {
            _clock = 0;
            float target = 0;
            if (World is { } w)
            {
                // Awake demons in sight: a dozen is unease, a few hundred is war.
                int seen = 0;
                var h = w.Horde;
                for (int i = 0; i < h.Count && seen < 300; i += 1 + h.Count / 4000) if (w.Vision.IsVisible(h.X[i], h.Y[i])) seen++;
                target = Mathf.Clamp(seen / 150f, 0, 1);
            }
            _target = target;
        }
        // Swell quickly, fade slowly.
        float rate = _target > _intensity ? 0.5f : 0.08f;
        _intensity = Mathf.MoveToward(_intensity, _target, rate * (float)delta);
        _battle.VolumeDb = Db(_intensity * Volume * Sound.Volume * 0.9f);
        _calm.VolumeDb = Db((1 - 0.4f * _intensity) * Volume * Sound.Volume);
    }

    float _target;

    static double Saw(double hz, double t) => 2 * (t * hz - Math.Floor(t * hz + 0.5));

    static AudioStreamWav Stream(float[] samples)
    {
        var data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)(Math.Clamp(samples[i], -1f, 1f) * 30000);
            data[2 * i] = (byte)(s & 0xff);
            data[2 * i + 1] = (byte)((s >> 8) & 0xff);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data, LoopMode = AudioStreamWav.LoopModeEnum.Forward, LoopBegin = 0, LoopEnd = samples.Length };
    }
}
