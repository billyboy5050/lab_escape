#nullable enable
using System;
using System.Collections.Generic;
using LabEscape.Core;

namespace LabEscape.Client
{
    public enum CueKind
    {
        /// <summary>An engine event reaches the screen.</summary>
        Event,
        /// <summary>A unit gathers itself before it acts: the moment that says a blow is coming.</summary>
        Windup,
        /// <summary>A unit throws itself at the tile beside it, and its blow lands when the cue ends.</summary>
        Strike,
    }

    /// <summary>One thing the board does at one moment of a frame.</summary>
    public sealed class Cue
    {
        public CueKind Kind;
        /// <summary>Seconds after the frame reaches the screen.</summary>
        public float At;
        /// <summary>How long the thing takes to travel: a walk, a shot's flight, a fall. 0 for something instant.</summary>
        public float Duration;
        /// <summary>The event shown, for an Event cue; the event a wind-up or strike leads to, for the others.</summary>
        public GameEvent Event = null!;
        /// <summary>The event's place in its frame, which seeds decoration so a replay draws the same sparks.</summary>
        public int Index;
        /// <summary>The unit that gathers, strikes or fires; -1 when it is not a unit.</summary>
        public int Actor = -1;
        /// <summary>The tile the actor aims at.</summary>
        public Pos Toward;
        /// <summary>True when a blow came from somewhere: Origin is that place, in tile coordinates.</summary>
        public bool HasOrigin;
        public float OriginX;
        public float OriginY;
    }

    /// <summary>A frame's events laid out in time, so that a cause is seen before its effect.</summary>
    public sealed class Stage
    {
        public static readonly Stage Empty = new Stage(new List<Cue>(), 0f);

        /// <summary>In time order. Cues at the same moment keep the order of their events.</summary>
        public readonly IReadOnlyList<Cue> Cues;

        /// <summary>How long the frame holds the queue. Every cue starts within it.</summary>
        public readonly float Hold;

        public Stage(List<Cue> cues, float hold)
        {
            Cues = cues;
            Hold = hold;
        }

        public bool Has<T>() where T : GameEvent
        {
            foreach (var c in Cues)
            {
                if (c.Kind == CueKind.Event && c.Event is T) return true;
            }
            return false;
        }
    }

    /// <summary>How heavy a unit is to animate, from its data: the player is 1, a hatchling a third of that, the Warden three times it.</summary>
    public static class Heft
    {
        private const float Lightest = 0.35f;
        private const float Heaviest = 3f;

        public static float Of(Content c, Unit u)
        {
            var player = c.Units["player"];
            var k = (u.MaxHp + 2f * u.Armor) / Math.Max(1f, player.Hp + 2f * player.Armor);
            return Math.Min(Heaviest, Math.Max(Lightest, k));
        }
    }

    /// <summary>What kind of thing a projectile event is, which decides how it flies.</summary>
    public enum ShotStyle { Bolt, Lob, Cable }

    public static class Shots
    {
        public static ShotStyle Style(ProjectileFired e)
        {
            // The grapple's line is the one projectile the rules do not mark as reflected or not.
            if (!e.Reflected.HasValue) return ShotStyle.Cable;
            var src = e.Source;
            if (e.Reflected == true || src.Via.Contains("acid") || src.Def == "spitter" || src.Via.Contains("parasite")) return ShotStyle.Lob;
            return ShotStyle.Bolt;
        }

        public static Pos End(ProjectileFired e) => e.HitPos ?? (e.Path.Count > 0 ? e.Path[e.Path.Count - 1] : e.From);
    }

    /// <summary>
    /// Lays a frame's events out in time. The engine resolves a whole action at once and lists its events in the order they
    /// caused each other; this turns that order into moments: a unit gathers, fires, the shot flies, it lands, the target reacts,
    /// and only then does the next thing start. It reads the events and never the rules, and it holds no Unity code.
    ///
    /// Every length is in beats. One beat is rules.ui.secondsPerActor, so content keeps a single knob for the fight's pace.
    /// </summary>
    public static class Staging
    {
        /// <summary>A unit gathering before a shot, a throw or a blow.</summary>
        public const float Windup = 0.25f;
        /// <summary>The longer gathering before a charge: the heaviest blow in the game gets the longest warning.</summary>
        public const float ChargeWindup = 0.7f;
        public const float BoltPerTile = 0.05f;
        public const float BoltMin = 0.12f;
        public const float BoltMax = 0.35f;
        public const float LobPerTile = 0.14f;
        public const float LobMin = 0.4f;
        public const float LobMax = 0.75f;
        /// <summary>A melee lunge, from its start to the blow.</summary>
        public const float Strike = 0.2f;
        /// <summary>One tile of walking for a unit of the player's weight.</summary>
        public const float Step = 0.27f;
        /// <summary>One tile of a charge or a lunge.</summary>
        public const float DashStep = 0.09f;
        /// <summary>One tile of being pulled.</summary>
        public const float DragStep = 0.13f;
        /// <summary>A unit that has just arrived plants its feet before it does anything else.</summary>
        public const float Plant = 0.12f;
        /// <summary>After a blow lands, nothing new starts for this long, so the reaction is seen.</summary>
        public const float HitGap = 0.32f;
        public const float KillGap = 0.5f;
        public const float AreaGap = 0.4f;
        /// <summary>A mine between its click and its blast.</summary>
        public const float Fuse = 0.45f;
        /// <summary>A thrown or placed object, from the hand to the floor.</summary>
        public const float Toss = 0.45f;
        /// <summary>A wave unit's fall to the floor, and the gap between one unit's fall starting and the next.</summary>
        public const float Drop = 0.7f;
        public const float WaveStagger = 0.4f;
        public const float LandGap = 0.5f;
        /// <summary>A deployed minion or a hatchling growing to its size.</summary>
        public const float Pop = 0.5f;
        public const float DeployStagger = 0.25f;
        public const float ArcTime = 0.15f;
        public const float ArcStep = 0.15f;
        /// <summary>From one status tick to the next, where a blow would wait HitGap.</summary>
        public const float TickStep = 0.22f;
        /// <summary>A hazard between waking and firing.</summary>
        public const float HazardBark = 0.4f;
        public const float PrimeStep = 0.2f;
        public const float PrimeHold = 0.5f;
        /// <summary>After a phase banner lands, before that phase's first action.</summary>
        public const float PhasePause = 0.55f;
        /// <summary>A frame that only starts a phase.</summary>
        public const float PhaseHold = 0.6f;
        /// <summary>A blast reaches a tile this long after the one before it.</summary>
        public const float Ripple = 0.07f;
        /// <summary>After the blow that ends the fight, before the result is shown.</summary>
        public const float FinalHold = 1.6f;

        /// <summary>
        /// True for the damage a status does as it counts down. The engine gives it the source of whoever applied the status,
        /// with the status's own name as what did it.
        /// </summary>
        public static bool IsStatusTick(DamageDealt e)
        {
            foreach (StatusId id in Enum.GetValues(typeof(StatusId)))
            {
                if (e.Source.Via == Names.Of(id)) return true;
            }
            return false;
        }

        /// <summary>A unit's step, in beats: heavier units take longer over each one.</summary>
        public static float StepFor(float heft) => Step * (0.75f + 0.25f * heft);

        public static Stage Plan(Content c, GameState before, IReadOnlyList<GameEvent> events)
        {
            var p = new Planner(c, before, events);
            for (var i = 0; i < events.Count; i++) p.Add(i);
            return p.Finish();
        }

        private enum Carrier { None, Shot, Area, Arc, Dash, Blow }

        private sealed class Planner
        {
            private readonly Content _c;
            private readonly IReadOnlyList<GameEvent> _events;
            private readonly float _beat;
            private readonly List<Cue> _cues = new List<Cue>();
            private readonly Dictionary<int, Pos> _pos = new Dictionary<int, Pos>();
            private readonly Dictionary<int, float> _heft = new Dictionary<int, float>();
            private readonly Dictionary<EntityRef, Cue> _kills = new Dictionary<EntityRef, Cue>();
            private readonly Dictionary<Pos, Cue> _arcs = new Dictionary<Pos, Cue>();
            private readonly Dictionary<Pos, float> _deathAt = new Dictionary<Pos, float>();
            private readonly Dictionary<int, float> _removedAt = new Dictionary<int, float>();

            /// <summary>When the next thing may start.</summary>
            private float _t;
            /// <summary>The earliest a new action may start: after the last blow has been seen to land.</summary>
            private float _settle;
            /// <summary>A pause owed to a phase banner, paid by the first action after it.</summary>
            private float _pause;
            private float _end;
            private bool _visual;
            private bool _phase;
            private bool _final;

            private Carrier _carrier;
            private float _impactAt;
            private float _ox, _oy;
            private int _carrierBy = -1;

            /// <summary>The unit whose wind-up for what comes next is already staged.</summary>
            private int _gathered = -1;
            /// <summary>The unit using an ability, which gathers before whatever the ability does first.</summary>
            private int _caster = -1;
            private int _lastSpawn = -2;
            private int _lastArc = -2;
            private int _lastPrime = -2;
            private int _lastPlaced = -2;
            private float _waveAt = -1f;

            public Planner(Content c, GameState before, IReadOnlyList<GameEvent> events)
            {
                _c = c;
                _events = events;
                _beat = (float)c.Rules.Ui.SecondsPerActor;
                foreach (var u in before.Units)
                {
                    _pos[u.Id] = u.Pos;
                    _heft[u.Id] = Heft.Of(c, u);
                }
            }

            private float B(float beats) => beats * _beat;

            private Cue Put(int i, CueKind kind, float at, float duration = 0f)
            {
                var cue = new Cue { Kind = kind, At = at, Duration = duration, Event = _events[i], Index = i };
                _cues.Add(cue);
                if (at + duration > _end) _end = at + duration;
                return cue;
            }

            /// <summary>An event that only informs: the log, the debug overlay, a counter.</summary>
            private void Note(int i) => Put(i, CueKind.Event, _t);

            private Cue Show(int i, float at, float duration = 0f)
            {
                _visual = true;
                return Put(i, CueKind.Event, at, duration);
            }

            /// <summary>The moment a new action may begin: after the last blow has settled and any banner has landed.</summary>
            private float Start()
            {
                _t = Math.Max(_t, _settle) + _pause;
                _pause = 0f;
                return _t;
            }

            private void Settle(float at)
            {
                if (at > _settle) _settle = at;
            }

            private void Carry(Carrier kind, float impactAt, float ox, float oy, int by = -1)
            {
                _carrier = kind;
                _impactAt = impactAt;
                _ox = ox;
                _oy = oy;
                _carrierBy = by;
            }

            private static int UnitOf(SourceRef? src) => src != null && src.Kind == SourceKind.Unit && src.TextId == null ? src.NumberId : -1;

            /// <summary>Stages a unit's wind-up, once, and moves the clock to its end.</summary>
            private void Gather(int i, int unit, Pos toward, float beats)
            {
                Start();
                if (unit < 0 || _gathered == unit) return;
                _visual = true;
                var cue = Put(i, CueKind.Windup, _t, B(beats));
                cue.Actor = unit;
                cue.Toward = toward;
                _t += B(beats);
                _gathered = unit;
            }

            private static float Distance(float ax, float ay, float bx, float by) => (float)Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

            /// <summary>When the thing in flight reaches a tile, and where it came from.</summary>
            private float Landing(Pos p, out bool hasOrigin, out float ox, out float oy)
            {
                hasOrigin = true;
                ox = _ox;
                oy = _oy;
                switch (_carrier)
                {
                    case Carrier.Area:
                        return _impactAt + B(Ripple) * Distance(_ox, _oy, p.X, p.Y);
                    case Carrier.Arc:
                        if (_arcs.TryGetValue(p, out var arc))
                        {
                            var from = ((ChainArc)arc.Event).From;
                            ox = from.X;
                            oy = from.Y;
                            return arc.At + arc.Duration;
                        }
                        return _impactAt;
                    case Carrier.None:
                        hasOrigin = false;
                        return _t;
                    default:
                        return _impactAt;
                }
            }

            public void Add(int i)
            {
                var ev = _events[i];
                switch (ev)
                {
                    case PhaseStarted e:
                        if (e.Phase == Phase.RoundStart)
                        {
                            Note(i);
                            break;
                        }
                        Put(i, CueKind.Event, Math.Max(_t, _settle));
                        _phase = true;
                        _pause = B(PhasePause);
                        break;

                    case UnitSpawned e:
                    {
                        var wave = e.Cause.StartsWith("wave", StringComparison.Ordinal);
                        if (_lastSpawn == i - 1) _t += B(wave ? WaveStagger : DeployStagger);
                        else if (wave || _caster < 0) Start();
                        else Gather(i, _caster, e.Unit.Pos, Windup);
                        _lastSpawn = i;
                        var fall = B(wave ? Drop : Pop);
                        var cue = Show(i, _t, fall);
                        cue.Actor = wave ? -1 : _caster;
                        Settle(_t + fall + (wave ? B(LandGap) : 0f));
                        _pos[e.Unit.Id] = e.Unit.Pos;
                        _heft[e.Unit.Id] = Heft.Of(_c, e.Unit);
                        if (wave && _waveAt < 0f) _waveAt = _t;
                        break;
                    }

                    case WaveSpawned _:
                        Put(i, CueKind.Event, _waveAt >= 0f ? _waveAt : _t);
                        break;

                    case AbilityUsed e:
                        Start();
                        Note(i);
                        _caster = e.Actor;
                        _carrier = Carrier.None;
                        break;

                    case ProjectileFired e:
                    {
                        // A shot sent back by a Reflective Shield keeps the source of the shot it returns, but it leaves the
                        // shield, not the unit that fired it. That unit neither gathers nor recoils a second time; the shot
                        // simply starts from the shield when the first one has arrived there.
                        var shooter = e.Reflected == true ? -1 : UnitOf(e.Source);
                        var end = Shots.End(e);
                        Gather(i, shooter, end, Windup);
                        var style = Shots.Style(e);
                        var dist = Distance(e.From.X, e.From.Y, end.X, end.Y);
                        var flight = style == ShotStyle.Lob ? Clamp(dist * LobPerTile, LobMin, LobMax) : Clamp(dist * BoltPerTile, BoltMin, BoltMax);
                        var cue = Show(i, _t, B(flight));
                        cue.Actor = shooter;
                        cue.Toward = end;
                        _t += B(flight);
                        Carry(Carrier.Shot, _t, e.From.X, e.From.Y, shooter);
                        // A shot that hits nothing still lands somewhere before the next thing starts.
                        Settle(_t);
                        _gathered = -1;
                        break;
                    }

                    case ChainArc e:
                    {
                        if (_lastArc == i - 1) _t += B(ArcStep);
                        else Start();
                        _lastArc = i;
                        var cue = Show(i, _t, B(ArcTime));
                        _arcs[e.To] = cue;
                        Carry(Carrier.Arc, _t + B(ArcTime), e.From.X, e.From.Y, UnitOf(e.Source));
                        Settle(_t + B(ArcTime));
                        break;
                    }

                    case AreaEffect e:
                    {
                        float cx = 0f, cy = 0f;
                        foreach (var tile in e.Tiles)
                        {
                            cx += tile.X;
                            cy += tile.Y;
                        }
                        if (e.Tiles.Count > 0)
                        {
                            cx /= e.Tiles.Count;
                            cy /= e.Tiles.Count;
                        }
                        var by = UnitOf(e.Source);
                        var alive = by >= 0 && _pos.ContainsKey(by);
                        if (alive) Gather(i, by, new Pos((int)Math.Round(cx), (int)Math.Round(cy)), Windup);
                        else Start();
                        var cue = Show(i, _t);
                        cue.Actor = alive ? by : -1;
                        cue.Toward = new Pos((int)Math.Round(cx), (int)Math.Round(cy));
                        // A sweep pushes away from the unit that makes it; a blast pushes out from its middle.
                        if (alive && e.Kind == AreaKind.Sweep) Carry(Carrier.Area, _t, _pos[by].X, _pos[by].Y, by);
                        else Carry(Carrier.Area, _t, cx, cy, by);
                        Settle(_t + B(AreaGap));
                        _gathered = -1;
                        break;
                    }

                    case ObjectTriggered _:
                        Start();
                        Show(i, _t, B(Fuse));
                        _t += B(Fuse);
                        break;

                    case ObjectRemoved e:
                        if (e.Reason != RemovalReason.Detonated) Start();
                        Show(i, _t);
                        _removedAt[e.Id] = _t;
                        Settle(_t + B(Plant));
                        break;

                    case ObjectPlaced e:
                    {
                        if (e.Object.Kind == ObjectKind.Corpse)
                        {
                            // The body's fall is the corpse arriving, so the corpse belongs to the moment of the death.
                            Show(i, _deathAt.TryGetValue(e.Object.Pos, out var died) ? died : _t);
                            break;
                        }
                        var by = UnitOf(e.Source);
                        if (by < 0) by = _caster;
                        if (_lastPlaced != i - 1) Gather(i, by, e.Object.Pos, Windup);
                        _lastPlaced = i;
                        var cue = Show(i, _t, B(Toss));
                        cue.Actor = by;
                        if (by >= 0 && _pos.TryGetValue(by, out var hand))
                        {
                            cue.HasOrigin = true;
                            cue.OriginX = hand.X;
                            cue.OriginY = hand.Y;
                        }
                        Settle(_t + B(Toss));
                        break;
                    }

                    case UnitMoved e:
                        Move(i, e);
                        break;

                    case Charged e:
                    {
                        float at;
                        if (_carrier == Carrier.Dash && _carrierBy == e.Id) at = _impactAt;
                        else
                        {
                            // A charge with nowhere to run is still a blow: the unit rears back and slams where it stands.
                            Gather(i, e.Id, Grid.Add(e.To, e.Dir), ChargeWindup);
                            at = _t;
                            _gathered = -1;
                        }
                        var cue = Show(i, at);
                        cue.Actor = e.Id;
                        cue.Toward = Grid.Add(e.To, e.Dir);
                        Carry(Carrier.Dash, at, e.To.X - e.Dir.X, e.To.Y - e.Dir.Y, e.Id);
                        Settle(at + B(HitGap));
                        break;
                    }

                    case DamageDealt e:
                        Damage(i, e);
                        break;

                    case UnitDied e:
                    {
                        var key = EntityRef.Unit(e.Id);
                        Cue cue;
                        if (_kills.TryGetValue(key, out var blow))
                        {
                            cue = Show(i, blow.At);
                            cue.HasOrigin = blow.HasOrigin;
                            cue.OriginX = blow.OriginX;
                            cue.OriginY = blow.OriginY;
                        }
                        else cue = Show(i, Math.Max(_t, _settle));
                        _deathAt[e.Pos] = cue.At;
                        Settle(cue.At + B(KillGap));
                        _pos.Remove(e.Id);
                        break;
                    }

                    case ObjectDestroyed e:
                    {
                        var at = _kills.TryGetValue(EntityRef.Object(e.Id), out var blow) ? blow.At : Math.Max(_t, _settle);
                        var cue = Show(i, at);
                        if (blow != null)
                        {
                            cue.HasOrigin = blow.HasOrigin;
                            cue.OriginX = blow.OriginX;
                            cue.OriginY = blow.OriginY;
                        }
                        Settle(at + B(HitGap));
                        break;
                    }

                    case Healed e:
                    {
                        var healer = UnitOf(e.Source);
                        if (healer >= 0 && healer != e.Target && _pos.TryGetValue(e.Target, out var at)) Gather(i, healer, at, Windup);
                        else Start();
                        var cue = Show(i, _t);
                        cue.Actor = healer;
                        Settle(_t + B(HitGap));
                        _gathered = -1;
                        break;
                    }

                    case StatusApplied e:
                    {
                        var at = _pos.TryGetValue(e.Target, out var where) ? Landing(where, out _, out _, out _) : _t;
                        Show(i, Math.Max(at, 0f));
                        break;
                    }

                    case EggHatched e:
                        Show(i, _removedAt.TryGetValue(e.Id, out var cracked) ? cracked : _t);
                        break;

                    case HazardFired _:
                        Start();
                        Show(i, _t, B(HazardBark));
                        _t += B(HazardBark);
                        break;

                    case HazardPrimed _:
                        if (_lastPrime == i - 1) _t += B(PrimeStep);
                        else Start();
                        _lastPrime = i;
                        Show(i, _t);
                        Settle(_t + B(PrimeHold));
                        break;

                    case DroneDocked _:
                    case DroneRedeployed _:
                    case Reloaded _:
                    case Purged _:
                        Start();
                        Show(i, _t);
                        Settle(_t + B(HitGap));
                        break;

                    case FightEnded _:
                        _final = true;
                        Put(i, CueKind.Event, Math.Max(_t, _settle));
                        break;

                    default:
                        Note(i);
                        break;
                }
            }

            private void Move(int i, UnitMoved e)
            {
                var steps = Math.Max(1, e.Path.Count);
                var heft = _heft.TryGetValue(e.Id, out var h) ? h : 1f;
                Cue cue;
                switch (e.Mode)
                {
                    case MoveMode.Charge:
                    case MoveMode.Lunge:
                    {
                        // A mine on the way splits one dash into two moves; only the first gathers.
                        if (!(_carrier == Carrier.Dash && _carrierBy == e.Id)) Gather(i, e.Id, e.To, e.Mode == MoveMode.Charge ? ChargeWindup : Windup);
                        var run = B(steps * DashStep);
                        cue = Show(i, _t, run);
                        _t += run;
                        Carry(Carrier.Dash, _t, e.From.X, e.From.Y, e.Id);
                        _gathered = -1;
                        break;
                    }
                    case MoveMode.Pull:
                    case MoveMode.Grapple:
                    {
                        // The cable has landed; what it holds comes with it at once.
                        var run = B(steps * DragStep);
                        cue = Show(i, _t, run);
                        _t += run;
                        _carrier = Carrier.None;
                        break;
                    }
                    default:
                    {
                        Start();
                        var run = B(steps * StepFor(heft));
                        cue = Show(i, _t, run);
                        _t += run;
                        _carrier = Carrier.None;
                        break;
                    }
                }
                cue.Actor = e.Id;
                cue.Toward = e.To;
                _pos[e.Id] = e.To;
                _caster = -1;
                Settle(_t + B(Plant));
            }

            private void Damage(int i, DamageDealt e)
            {
                float at;
                bool hasOrigin;
                float ox, oy;
                var by = UnitOf(e.Source);
                var self = e.Target.Kind == EntityKind.Unit && e.Target.Id == by;
                var tick = IsStatusTick(e);
                if (tick)
                {
                    // A status counting down. Its source is whoever applied it, perhaps rounds ago and perhaps standing next
                    // to it now, so it must be told apart before anything below takes it for a blow. It belongs to nothing in
                    // flight either: a burst set off by the unit before it in the line did not reach this one.
                    Start();
                    _carrier = Carrier.None;
                    at = _t;
                    hasOrigin = false;
                    ox = oy = 0f;
                }
                else if (_carrier != Carrier.None)
                {
                    at = Landing(e.Pos, out hasOrigin, out ox, out oy);
                }
                else if (by >= 0 && !self && _pos.TryGetValue(by, out var from) && Grid.Cheb(from, e.Pos) <= 1)
                {
                    // A blow from the next tile with nothing in flight is a melee attack: gather, lunge, land.
                    Gather(i, by, e.Pos, Windup);
                    var strike = Put(i, CueKind.Strike, _t, B(Strike));
                    strike.Actor = by;
                    strike.Toward = e.Pos;
                    _t += B(Strike);
                    at = _t;
                    hasOrigin = true;
                    ox = from.X;
                    oy = from.Y;
                    Carry(Carrier.Blow, at, ox, oy, by);
                    _gathered = -1;
                }
                else
                {
                    // Nothing carried it and nobody is beside it: it simply happens.
                    Start();
                    at = _t;
                    hasOrigin = false;
                    ox = oy = 0f;
                }
                var cue = Show(i, at);
                cue.Actor = tick ? -1 : by;
                cue.HasOrigin = hasOrigin && !(Math.Abs(ox - e.Pos.X) < 0.01f && Math.Abs(oy - e.Pos.Y) < 0.01f);
                cue.OriginX = ox;
                cue.OriginY = oy;
                if (e.Killed) _kills[e.Target] = cue;
                // Ticks follow each other faster than blows do, so poison counts down a line of units.
                Settle(at + B(e.Killed ? KillGap : tick ? TickStep : HitGap));
            }

            private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

            public Stage Finish()
            {
                float hold;
                if (_visual) hold = Math.Max(_end, _settle);
                else hold = _phase ? B(PhaseHold) : 0f;
                if (_final) hold = Math.Max(hold, _settle) + B(FinalHold);
                // A stable sort: cues at the same moment stay in the order their events happened.
                var order = new List<Cue>(_cues);
                var place = new Dictionary<Cue, int>();
                for (var k = 0; k < order.Count; k++) place[order[k]] = k;
                order.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At) : place[a].CompareTo(place[b]));
                foreach (var cue in order)
                {
                    if (cue.At > hold) hold = cue.At;
                }
                return new Stage(order, hold);
            }
        }
    }
}
