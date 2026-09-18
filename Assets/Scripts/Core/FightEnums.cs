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
