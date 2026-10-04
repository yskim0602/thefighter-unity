using System.Collections.Generic;
using UnityEngine;

namespace TheFighter
{
    /// Touch controls, one thumb per hand, built so neither thumb ever has to reach.
    ///
    ///   LEFT  - a floating stick. Where you first touch becomes the centre; drag to step in and
    ///           out or circle. Always relative to the opponent, never to the camera, so the
    ///           broadcast camera can drift without inverting your footwork.
    ///
    ///   RIGHT - one thumb does everything, by gesture:
    ///           tap            -> jab (left half of the zone) or straight (right half)
    ///           tap high / low -> head or body. This is the mouse-aim mechanic carried over to
    ///                             touch: where you tap is where the punch goes.
    ///           swipe up       -> uppercut
    ///           swipe sideways -> hook
    ///           swipe down     -> slip
    ///           hold           -> guard (held, releases the moment you throw)
    ///
    /// Thresholds are in inches off Screen.dpi so a swipe means the same travel on every device.
    /// In the editor the mouse stands in for a single finger, so the scheme can be tried before
    /// there is a device build.
    public class TouchBrain : MonoBehaviour, IFighterBrain
    {
        [Header("Zones - normalised screen space, origin bottom-left")]
        public Rect MoveZone = new Rect(0f, 0f, 0.40f, 0.78f);
        public Rect PunchZone = new Rect(0.44f, 0f, 0.56f, 0.88f);

        [Header("Stick")]
        public float StickRadiusInches = 0.42f;
        public float StickDeadzone = 0.16f;

        [Header("Gestures")]
        public float SwipeInches = 0.20f;
        /// Kept equal to GuardHoldSeconds so a slow tap is either a punch or a guard, never both.
        public float TapMaxSeconds = 0.20f;
        public float GuardHoldSeconds = 0.20f;

        [Header("Aim")]
        /// Fraction of the punch zone's height that reads as a pure body shot.
        public float BodyBandTop = 0.34f;
        /// Above this fraction, a tap is a pure head shot.
        public float HeadBandBottom = 0.66f;

        [Header("Editor")]
        public bool SimulateWithMouse = true;

        // --- overlay readouts ---------------------------------------------
        public bool StickActive { get; private set; }
        public Vector2 StickOrigin { get; private set; }
        public Vector2 StickTip { get; private set; }
        public bool GuardActive { get; private set; }
        public string LastGesture { get; private set; }

        class Pointer
        {
            public Vector2 Start;
            public Vector2 Current;
            public float StartTime;
            public bool InMoveZone;
            public bool InPunchZone;
            public bool Consumed;
        }

        struct RawPointer
        {
            public int Id;
            public Vector2 Position;
            public bool Began;
            public bool Ended;
        }

        readonly Dictionary<int, Pointer> _pointers = new Dictionary<int, Pointer>();
        readonly List<RawPointer> _raw = new List<RawPointer>();
        readonly HashSet<int> _seen = new HashSet<int>();
        readonly List<int> _stale = new List<int>();

        Fighter _self;
        float _aim = 1f;

        public void Attach(Fighter self, Fighter opponent)
        {
            _self = self;
            LastGesture = "";
        }

        float PixelsPerInch
        {
            get
            {
                float dpi = Screen.dpi;
                return dpi > 1f ? dpi : 160f;
            }
        }

        public FighterIntent Think(float deltaTime)
        {
            FighterIntent intent = FighterIntent.Neutral();
            intent.AimHeight = _aim;

            if (_self != null && _self.IsFinished)
            {
                Reset();
                return intent;
            }

            ReadRawPointers();

            Vector2 move = Vector2.zero;
            bool guard = false;
            bool dodge = false;
            bool punch = false;
            PunchKind kind = PunchKind.Jab;

            StickActive = false;
            _seen.Clear();

            for (int i = 0; i < _raw.Count; i++)
            {
                RawPointer raw = _raw[i];
                _seen.Add(raw.Id);

                Pointer pointer;
                if (raw.Began || !_pointers.TryGetValue(raw.Id, out pointer))
                {
                    pointer = new Pointer();
                    pointer.Start = raw.Position;
                    pointer.StartTime = Time.unscaledTime;
                    pointer.InMoveZone = Contains(MoveZone, raw.Position);
                    pointer.InPunchZone = !pointer.InMoveZone && Contains(PunchZone, raw.Position);
                    _pointers[raw.Id] = pointer;
                }

                pointer.Current = raw.Position;

                if (pointer.InMoveZone)
                {
                    move += ReadStick(pointer);
                }
                else if (pointer.InPunchZone)
                {
                    ReadPunchGesture(pointer, raw.Ended, ref guard, ref dodge, ref punch, ref kind);
                }

                if (raw.Ended)
                {
                    _pointers.Remove(raw.Id);
                    _seen.Remove(raw.Id);
                }
            }

            PruneLostPointers();

            intent.Move = Vector2.ClampMagnitude(move, 1f);
            intent.Guard = guard;
            intent.Dodge = dodge;
            intent.AimHeight = _aim;

            if (punch)
            {
                intent.ThrowPunch = true;
                intent.Punch = kind;
                // A tap is also how you beat the count.
                intent.MashGetUp = true;
            }

            GuardActive = guard;
            return intent;
        }

        Vector2 ReadStick(Pointer pointer)
        {
            Vector2 delta = pointer.Current - pointer.Start;
            Vector2 value = delta / (StickRadiusInches * PixelsPerInch);

            if (value.magnitude < StickDeadzone)
            {
                value = Vector2.zero;
            }

            StickActive = true;
            StickOrigin = pointer.Start;
            StickTip = pointer.Current;

            return Vector2.ClampMagnitude(value, 1f);
        }

        void ReadPunchGesture(Pointer pointer, bool ended,
            ref bool guard, ref bool dodge, ref bool punch, ref PunchKind kind)
        {
            Vector2 delta = pointer.Current - pointer.Start;
            float held = Time.unscaledTime - pointer.StartTime;
            float swipe = SwipeInches * PixelsPerInch;

            if (!pointer.Consumed && delta.magnitude >= swipe)
            {
                pointer.Consumed = true;

                if (Mathf.Abs(delta.y) > Mathf.Abs(delta.x))
                {
                    if (delta.y > 0f)
                    {
                        punch = true;
                        kind = PunchKind.Uppercut;
                        _aim = AimFromHeight(pointer.Start);
                        LastGesture = "SWIPE UP - UPPERCUT";
                    }
                    else
                    {
                        dodge = true;
                        LastGesture = "SWIPE DOWN - SLIP";
                    }
                }
                else
                {
                    punch = true;
                    kind = PunchKind.Hook;
                    _aim = AimFromHeight(pointer.Start);
                    LastGesture = "SWIPE SIDE - HOOK";
                }

                return;
            }

            if (ended)
            {
                if (!pointer.Consumed && held < TapMaxSeconds)
                {
                    punch = true;
                    kind = IsLeadHalf(pointer.Start) ? PunchKind.Jab : PunchKind.Straight;
                    _aim = AimFromHeight(pointer.Start);
                    LastGesture = (kind == PunchKind.Jab ? "TAP L - JAB " : "TAP R - STRAIGHT ")
                        + (_aim > 0.5f ? "HEAD" : "BODY");
                }
                return;
            }

            if (!pointer.Consumed && held >= GuardHoldSeconds)
            {
                guard = true;
            }
        }

        /// Where you tap vertically inside the punch zone decides head or body, with a blend in
        /// between so a careless tap still lands somewhere sensible.
        float AimFromHeight(Vector2 screenPosition)
        {
            float zoneBottom = PunchZone.yMin * Screen.height;
            float zoneTop = PunchZone.yMax * Screen.height;
            float t = Mathf.InverseLerp(zoneBottom, zoneTop, screenPosition.y);
            return Mathf.Clamp01(Mathf.InverseLerp(BodyBandTop, HeadBandBottom, t));
        }

        bool IsLeadHalf(Vector2 screenPosition)
        {
            float left = PunchZone.xMin * Screen.width;
            float right = PunchZone.xMax * Screen.width;
            return Mathf.InverseLerp(left, right, screenPosition.x) < 0.5f;
        }

        static bool Contains(Rect normalised, Vector2 screenPosition)
        {
            float x = screenPosition.x / Mathf.Max(1f, Screen.width);
            float y = screenPosition.y / Mathf.Max(1f, Screen.height);
            return normalised.Contains(new Vector2(x, y));
        }

        void ReadRawPointers()
        {
            _raw.Clear();

            int count = Input.touchCount;
            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    RawPointer raw = new RawPointer();
                    raw.Id = touch.fingerId;
                    raw.Position = touch.position;
                    raw.Began = touch.phase == TouchPhase.Began;
                    raw.Ended = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;
                    _raw.Add(raw);
                }
                return;
            }

            if (!SimulateWithMouse)
            {
                return;
            }

            bool held = Input.GetMouseButton(0);
            bool released = Input.GetMouseButtonUp(0);
            if (!held && !released)
            {
                return;
            }

            RawPointer mouse = new RawPointer();
            mouse.Id = -1;
            mouse.Position = Input.mousePosition;
            mouse.Began = Input.GetMouseButtonDown(0);
            mouse.Ended = released;
            _raw.Add(mouse);
        }

        /// A finger can vanish without an Ended phase (app switch, gesture steal), so drop any
        /// state we did not see this frame instead of letting it pin a stale guard on.
        void PruneLostPointers()
        {
            _stale.Clear();
            foreach (KeyValuePair<int, Pointer> entry in _pointers)
            {
                if (!_seen.Contains(entry.Key))
                {
                    _stale.Add(entry.Key);
                }
            }
            for (int i = 0; i < _stale.Count; i++)
            {
                _pointers.Remove(_stale[i]);
            }
        }

        void Reset()
        {
            _pointers.Clear();
            StickActive = false;
            GuardActive = false;
        }
    }
}
