using Godot;
using Hellwall.Sim;

namespace Hellwall.Game;

/// <summary>
/// Placeholder audio, synthesized at startup: no files, nothing to license.
/// Each cue is positional (the 2D camera is the listener, so a fight off
/// screen is quieter) and rate-limited, because a big fight fires hundreds
/// of shots a second and they must stay a texture, not a roar of clipping.
/// </summary>
public partial class Sound : Node2D
{
    public enum Cue { Shot, Boom, Horn, Howl, Burst, Built, Possessed, Fallen, Promoted, Felled, Voice, Victory }

    const int Rate = 22050;
    readonly Dictionary<Cue, AudioStreamWav> _streams = new();
    readonly Dictionary<Cue, double> _cooldown = new();
    readonly List<AudioStreamPlayer2D> _pool = new();
    readonly AudioStreamPlayer _global = new();
    readonly Random _random = new(1);

    /// <summary>Seconds between two plays of the same cue.</summary>
    static double Gap(Cue cue) => cue switch { Cue.Shot => 0.05, Cue.Boom => 0.12, Cue.Burst => 0.1, _ => 0.4 };

    public static float Volume => Settings.Get("volume", 0.7f);

    public override void _Ready()
    {
        _streams[Cue.Shot] = Make(0.07, (t, n) => n * Math.Exp(-t * 60) * 0.5);
        _streams[Cue.Boom] = Make(0.5, (t, n) => (Math.Sin(2 * Math.PI * (70 - 50 * t) * t) * 0.8 + n * 0.4) * Math.Exp(-t * 7));
        _streams[Cue.Horn] = Make(1.4, (t, _) => Saw(110, t) * 0.25 * Envelope(t, 0.1, 1.4) + Saw(165, t) * 0.18 * Envelope(t - 0.35, 0.1, 1.05));
        _streams[Cue.Howl] = Make(1.1, (t, _) => Math.Sin(2 * Math.PI * (320 + 180 * Math.Sin(Math.PI * t / 1.1) + 12 * Math.Sin(2 * Math.PI * 7 * t)) * t) * 0.3 * Envelope(t, 0.15, 1.1));
        _streams[Cue.Burst] = Make(0.3, (t, n) => (n * 0.6 + Math.Sin(2 * Math.PI * 90 * t) * 0.5) * Math.Exp(-t * 14));
        _streams[Cue.Built] = Make(0.5, (t, _) => (Math.Sin(2 * Math.PI * 660 * t) * Envelope(t, 0.01, 0.25) + Math.Sin(2 * Math.PI * 880 * t) * Envelope(t - 0.12, 0.01, 0.35)) * 0.25);
        // An alarm bell struck three times over a low drone: a building is possessed.
        _streams[Cue.Possessed] = Make(1.6, (t, _) =>
        {
            double bell = 0;
            for (int k = 0; k < 3; k++)
            {
                double s = t - k * 0.32;
                if (s < 0) continue;
                bell += (Math.Sin(2 * Math.PI * 880 * s) * 0.5 + Math.Sin(2 * Math.PI * 1320 * s) * 0.25 + Math.Sin(2 * Math.PI * 1760 * s) * 0.12) * Math.Exp(-s * 9);
            }
            return bell * 0.32 + (Math.Sin(2 * Math.PI * 98 * t) + Math.Sin(2 * Math.PI * 104 * t)) * 0.18 * Envelope(t, 0.05, 1.6);
        });
        _streams[Cue.Fallen] = Make(2.5, (t, n) => (Saw(55, t) * 0.35 + n * 0.1) * Envelope(t, 0.05, 2.5));
        // A rising three-note chime: a soldier made rank.
        _streams[Cue.Promoted] = Make(0.6, (t, _) => (Math.Sin(2 * Math.PI * 523 * t) * Envelope(t, 0.01, 0.2) + Math.Sin(2 * Math.PI * 659 * t) * Envelope(t - 0.12, 0.01, 0.2) + Math.Sin(2 * Math.PI * 784 * t) * Envelope(t - 0.24, 0.01, 0.35)) * 0.22);
        // A creak and a thump: a tree comes down.
        _streams[Cue.Felled] = Make(0.6, (t, n) => (Math.Sin(2 * Math.PI * (180 - 120 * t) * t) * 0.25 * Envelope(t, 0.02, 0.3) + (n * 0.5 + Math.Sin(2 * Math.PI * 60 * t)) * 0.4 * Math.Exp(-Math.Max(0, t - 0.3) * 12) * (t > 0.3 ? 1 : 0)));
        // A soft two-tone blip under a spoken line.
        _streams[Cue.Voice] = Make(0.25, (t, _) => (Math.Sin(2 * Math.PI * 440 * t) * Envelope(t, 0.01, 0.1) + Math.Sin(2 * Math.PI * 587 * t) * Envelope(t - 0.08, 0.01, 0.15)) * 0.15);
        // A major chord, swelling: the colony endures.
        _streams[Cue.Victory] = Make(2.2, (t, _) => (Math.Sin(2 * Math.PI * 262 * t) + Math.Sin(2 * Math.PI * 330 * t) * 0.8 + Math.Sin(2 * Math.PI * 392 * t) * 0.7) * 0.18 * Envelope(t, 0.3, 2.2));

        for (int i = 0; i < 24; i++)
        {
            var p = new AudioStreamPlayer2D { MaxDistance = 2200, Attenuation = 1.4f };
            _pool.Add(p);
            AddChild(p);
        }
        AddChild(_global);
    }

    public override void _Process(double delta)
    {
        foreach (var cue in _cooldown.Keys.ToList()) _cooldown[cue] -= delta;
    }

    /// <summary>Play at a world tile position, or everywhere (announcements) when at is null.</summary>
    public void Play(Cue cue, Vector2? at = null)
    {
        if (Volume <= 0 || _cooldown.GetValueOrDefault(cue) > 0) return;
        _cooldown[cue] = Gap(cue);
        float db = Mathf.LinearToDb(Volume);
        if (at is not { } tile)
        {
            _global.Stream = _streams[cue];
            _global.VolumeDb = db;
            _global.Play();
            return;
        }
        var player = _pool.FirstOrDefault(p => !p.Playing) ?? _pool[_random.Next(_pool.Count)];
        player.Stream = _streams[cue];
        player.VolumeDb = db;
        player.PitchScale = 0.92f + (float)_random.NextDouble() * 0.16f; // a little variety
        player.GlobalPosition = Iso.P(tile);
        player.Play();
    }

    public void See(SimEvent e)
    {
        switch (e)
        {
            case ShotFired s: Play(s.Splash > 0 ? Cue.Boom : Cue.Shot, new Vector2(s.FromX, s.FromY)); break;
            case WaveAnnounced: Play(Cue.Horn); break;
            case CorruptionAnnounced: Play(Cue.Possessed); break;
            case DemonHowled h: Play(Cue.Howl, new Vector2(h.X, h.Y)); break;
            case DemonBurst b: Play(Cue.Burst, new Vector2(b.X, b.Y)); break;
            case BuildingCompleted: Play(Cue.Built); break;
            case BuildingPossessed: Play(Cue.Possessed); break;
            case OutcomeChanged { Outcome: Outcome.Lost }: Play(Cue.Fallen); break;
            case OutcomeChanged { Outcome: Outcome.Won }: Play(Cue.Victory); break;
            case UnitPromoted p: Play(Cue.Promoted, new Vector2(p.X, p.Y)); break;
            case RuinLooted: Play(Cue.Built); break;
            case PatronOffered: Play(Cue.Promoted); break;
            case TreeFelled f: Play(Cue.Felled, new Vector2(f.X, f.Y)); break;
            case ScenarioMessage { Text.Length: > 0 }: Play(Cue.Voice); break;
        }
    }

    static double Saw(double hz, double t) => 2 * (t * hz - Math.Floor(t * hz + 0.5));

    /// <summary>A quick rise, then a fade to nothing at `length`.</summary>
    static double Envelope(double t, double attack, double length) =>
        t < 0 || t > length ? 0 : Math.Min(1, t / attack) * (1 - t / length);

    AudioStreamWav Make(double seconds, Func<double, double, double> wave)
    {
        int n = (int)(seconds * Rate);
        var data = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            double v = Math.Clamp(wave(i / (double)Rate, _random.NextDouble() * 2 - 1), -1, 1);
            short sample = (short)(v * 32000);
            data[2 * i] = (byte)(sample & 0xff);
            data[2 * i + 1] = (byte)((sample >> 8) & 0xff);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data };
    }
}
