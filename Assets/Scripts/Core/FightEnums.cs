namespace TheFighter
{
    public enum Stance
    {
        Orthodox,
        Southpaw
    }

    public enum HandRole
    {
        Lead,
        Rear
    }

    public enum PunchKind
    {
        Jab,
        Straight,
        Hook,
        Uppercut
    }

    /// Which arm a clip actually animates. Not Lead/Rear: a clip moves a *side*, and which side
    /// is the lead one depends on the stance it is played on.
    public enum ClipSide
    {
        /// Matches either. A clip that does not say which arm it swings serves both.
        Auto,
        Left,
        Right
    }

    /// Which way a punch arrived, in the *defender's* frame. A left hook from the man in front
    /// of you lands on your right, so this is worked out from the contact point rather than from
    /// which hand threw it.
    public enum HitDirection
    {
        /// Matches anything. A clip tagged Any is the fallback for every direction.
        Any,
        Front,
        Left,
        Right,
        Back
    }

    /// How hard it landed. Two steps, because three would need three times the clips to tell
    /// apart and nobody can see the difference between a 6 and a 7.
    public enum HitSeverity
    {
        Any,
        Light,
        Heavy
    }

    public enum HitZone
    {
        Head,
        Body
    }

    public enum HitResult
    {
        Clean,
        Blocked,
        Dodged
    }

    public enum ActionState
    {
        Free,
        Windup,
        Strike,
        Recovery,
        Staggered,
        Down,
        KnockedOut
    }

    public enum BoxingStyle
    {
        OutBoxer,
        InFighter,
        Slugger,
        BoxerPuncher,
        CounterPuncher,
        PressureFighter
    }
}
