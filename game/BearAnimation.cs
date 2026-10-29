namespace Hellwall.Game;

/// <summary>Pure presentation timing. Tick times follow the sim, so pause/speed never desynchronise combat clips.</summary>
public static class BearAnimation
{
    public const int Cell = 96, Frames = 6, Directions = 8, Clips = 5;
    public const float FeetY = 76f;
    public const double TickHz = Hellwall.Sim.Balance.TickHz;
    public const double AttackSeconds = 0.78, HitSeconds = 0.36, DeathSeconds = 1.2, CorpseSeconds = 5.2;
    public enum Clip { Idle, Walk, Attack, Hit, Death }
    public readonly record struct Sample(Clip Clip, int Frame, float Flash = 0, float Opacity = 1)
    {
        public int Row(int facing) => (int)Clip * Directions + facing;
    }

    public static Sample Living(in BearPose pose, double tick, bool moving)
    {
        double hitAge = (tick - pose.HitTick) / TickHz;
        float flash = (float)Math.Clamp(1 - hitAge / 0.16, 0, 1) * 0.55f;
        double attackAge = (tick - pose.AttackTick) / TickHz;
        if (attackAge >= 0 && attackAge < AttackSeconds)
            return new(Clip.Attack, OneShot(attackAge, AttackSeconds), flash);
        if (hitAge >= 0 && hitAge < HitSeconds)
            return new(Clip.Hit, OneShot(hitAge, HitSeconds), flash);
        return moving
            ? new(Clip.Walk, (int)pose.WalkFrame % Frames, flash)
            : new(Clip.Idle, (int)(tick / TickHz * 3.2 + pose.Phase) % Frames, flash);
    }

    public static Sample Dead(double seconds) => new(Clip.Death, OneShot(seconds, DeathSeconds), 0,
        (float)Math.Clamp((CorpseSeconds - seconds) / 1.2, 0, 1));

    public static int OneShot(double seconds, double duration) => Math.Clamp((int)(seconds / duration * Frames), 0, Frames - 1);

    public static BearPose New(int slot) => new()
    {
        Facing = 1, AttackTick = -1_000_000, HitTick = -1_000_000,
        Phase = slot * 0.61803398875 % Frames,
    };
}

/// <summary>Client-only state indexed like the horde; preserved through its stable compaction.</summary>
public struct BearPose
{
    public int Facing, AttackTick, HitTick;
    public float HpBefore, CooldownBefore, XBefore, YBefore;
    public double WalkFrame, Phase;
}
