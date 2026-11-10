#nullable enable
using System.Collections.Generic;
using LabEscape.Core;
using UnityEngine;

namespace LabEscape.Client
{
    /// <summary>
    /// A unit's figure as a body with weight. Its place on the floor follows the moves it is given; everything else (the shove
    /// of a hit, the lean, the squash of a landing) rides on springs, so each motion overshoots and settles instead of stopping
    /// dead. Heavier units move less, swing slower and land harder.
    /// </summary>
    public sealed class UnitView
    {
        private enum Motion { None, Walk, Dash, Drag, Drop }

        private readonly List<Vector3> _path = new List<Vector3>();
        private readonly bool _small;
        private readonly bool _machine;
        private readonly float _phase;
        private readonly float _hover;
        private readonly float _alpha;
        private Motion _motion;
        private float _t;
        private float _duration;
        private int _stepsDone;
        private Vector3 _ground;
        private Vector3 _heading = Vector3.forward;
        private Spring2 _shove;
        private Spring2 _lean;
        private Spring _squash;
        private Spring _grow;
        private float _height;
        private float _hop;
        private Vector3 _lunge;
        private float _lungeT = -1f;
        private float _lungeOut;
        private bool _struck;
        private float _flash;
        private float _yaw = 135f;
        private float _targetYaw = 135f;

        public readonly Model Model;
        public readonly float Heft;

        /// <summary>The last record seen for this unit. A unit that has died keeps the one it had.</summary>
        public Unit Data;
        public Pos Tile;

        /// <summary>The HP on show, which falls when the blow lands and not when the engine resolved it.</summary>
        public int ShownHp;
        /// <summary>The HP it had before the last blow, kept on its bar for GhostLeft seconds.</summary>
        public int GhostHp;
        public float GhostLeft;
        /// <summary>1 at a blow, falling to 0: shakes the unit's tags.</summary>
        public float Jolt;
        /// <summary>A footfall or a landing since the board last looked, by weight. The board turns it into dust and a shake.</summary>
        public float Thud;

        public UnitView(Content c, Unit u, Transform parent, bool ghost = false)
        {
            Model = Figures.Unit(u.Def, u.Team, parent);
            Data = u;
            Tile = u.Pos;
            ShownHp = GhostHp = u.Hp;
            Heft = Client.Heft.Of(c, u);
            var def = c.Units.TryGetValue(u.Def, out var d) ? d : null;
            _small = def != null && def.Hatchling;
            _machine = u.Kind == UnitKind.Minion && !_small;
            _phase = u.Id * 0.37f;
            _hover = u.Flying ? 0.12f : 0f;
            _alpha = ghost ? 0.42f : 1f;
            _ground = Iso.Tile(u.Pos.X, u.Pos.Y);
            _grow.Rest(1f);
            Model.Root.transform.position = _ground;
            Model.Root.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            Model.Paint(_alpha);
        }

        public bool Moving => _motion != Motion.None;

        /// <summary>Where the figure is, shoved and lunging included.</summary>
        public Vector3 Position => Model.Root.transform.position;

        /// <summary>The point on the floor under it, and how far above that its body is: what its shadow needs.</summary>
        public Vector3 Ground => _ground + _shove.Value + _lunge * LungeReach();

        public float Height => _height + _hop;

        /// <summary>False while it is still arriving, so its tags wait for it to land.</summary>
        public bool Arrived => _motion != Motion.Drop && _grow.Value > 0.6f;

        /// <summary>Puts the figure on a tile, standing still.</summary>
        public void Snap(Pos tile)
        {
            Tile = tile;
            _path.Clear();
            _motion = Motion.None;
            _ground = Iso.Tile(tile.X, tile.Y);
            _height = _hop = 0f;
            _lungeT = -1f;
            _shove.Rest();
            _lean.Rest();
            _squash.Rest();
            _grow.Rest(1f);
            Model.Plate.localScale = Vector3.one;
            Compose(0f);
        }

        /// <summary>Takes the figure through the tiles of a move in the given time, in the manner of the move.</summary>
        public void Follow(Pos from, IReadOnlyList<Pos> tiles, float seconds, MoveMode mode)
        {
            if (tiles.Count == 0) return;
            if (seconds <= 0f)
            {
                Snap(tiles[tiles.Count - 1]);
                return;
            }
            _path.Clear();
            // A move that starts before the last has quite ended carries on from where the figure is.
            _path.Add(_motion == Motion.None ? Iso.Tile(from.X, from.Y) : _ground);
            foreach (var p in tiles) _path.Add(Iso.Tile(p.X, p.Y));
            _t = 0f;
            _duration = seconds;
            _stepsDone = 0;
            _height = 0f;
            Tile = tiles[tiles.Count - 1];
            Stand();
            switch (mode)
            {
                case MoveMode.Charge:
                case MoveMode.Lunge:
                    _motion = Motion.Dash;
                    _squash.Target = Feel.DashStretch;
                    break;
                case MoveMode.Pull:
                case MoveMode.Grapple:
                    _motion = Motion.Drag;
                    break;
                default:
                    _motion = Motion.Walk;
                    break;
            }
        }

        /// <summary>Starts the figure high above its tile, to fall and land when the time is up.</summary>
        public void DropIn(float seconds)
        {
            _motion = Motion.Drop;
            _t = 0f;
            _duration = Mathf.Max(0.01f, seconds);
            _height = Feel.DropHeight;
            _squash.Rest(Feel.DropStretch);
            Compose(0f);
        }

        /// <summary>Starts the figure small, to spring up to its size.</summary>
        public void PopIn()
        {
            _grow.Value = 0.05f;
            _grow.Velocity = 0f;
            _grow.Target = 1f;
            Compose(0f);
        }

        public void Face(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            _heading = direction.normalized;
            _targetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        /// <summary>Gathers before a blow: the figure leans back from its target and crouches, and holds that until it acts.</summary>
        public void Windup(Vector3 toward, bool heavy)
        {
            Face(toward);
            var k = heavy ? 1.9f : 1f;
            _lean.Aim(-_heading * (Feel.WindupLean * k));
            _shove.Aim(-_heading * (Feel.WindupBack * k));
            _squash.Target = Feel.WindupCrouch * k;
        }

        /// <summary>Lets go of the gathered pose.</summary>
        private void Stand()
        {
            _lean.Aim(Vector3.zero);
            _shove.Aim(Vector3.zero);
            _squash.Target = 0f;
        }

        /// <summary>Fires: the pose is let go and the weapon kicks the figure back.</summary>
        public void Release(Vector3 toward, float kick)
        {
            Face(toward);
            Stand();
            var give = Feel.Give(Heft) * kick;
            _shove.Push(-_heading * (Feel.RecoilShove * give));
            _lean.Push(-_heading * (Feel.RecoilTip * give));
            _squash.Velocity += Feel.RecoilSquash * kick;
        }

        /// <summary>Throws itself at the next tile and comes back. The blow lands when `seconds` is up.</summary>
        public void Strike(Vector3 toward, float seconds)
        {
            Face(toward);
            Stand();
            _lunge = _heading;
            _lungeT = 0f;
            _lungeOut = Mathf.Max(0.01f, seconds);
            _struck = false;
            _lean.Aim(_heading * 18f);
            _squash.Target = 0.12f;
        }

        private float LungeReach()
        {
            if (_lungeT < 0f) return 0f;
            if (_lungeT < _lungeOut)
            {
                var k = _lungeT / _lungeOut;
                return Feel.StrikeReach * k * k;
            }
            var back = (_lungeT - _lungeOut) / Feel.StrikeReturn;
            return back >= 1f ? 0f : Feel.StrikeReach * (1f - Mathf.SmoothStep(0f, 1f, back));
        }

        /// <summary>Changes the HP on show. A loss keeps what was lost on the bar for a moment.</summary>
        public void SetHp(int hp)
        {
            if (hp < ShownHp)
            {
                if (GhostLeft <= 0f) GhostHp = ShownHp;
                GhostLeft = 0.5f;
            }
            ShownHp = hp;
        }

        /// <summary>Takes a blow from a direction: shoved, tipped and squashed by its size, less the heavier the unit is.</summary>
        public void Hit(Vector3 dir, int amount, bool blocked)
        {
            _flash = blocked ? 0.45f : 1f;
            Jolt = blocked ? 0.4f : 1f;
            var give = Feel.Give(Heft);
            if (blocked)
            {
                _squash.Velocity += Feel.BlockSquash;
                _lean.Push(dir * (70f * give));
                return;
            }
            var size = Mathf.Min(amount, 6);
            if (dir.sqrMagnitude > 0.0001f)
            {
                _shove.Push(dir * ((Feel.ShoveBase + Feel.ShovePerDamage * size) * give));
                _lean.Push(dir * ((Feel.TipBase + Feel.TipPerDamage * size) * give));
            }
            _squash.Velocity += Feel.HitSquash + Feel.HitSquashPerDamage * size;
        }

        /// <summary>A heal, or anything else that should make the figure swell for a moment.</summary>
        public void Pulse()
        {
            _squash.Velocity += Feel.PulseSquash;
        }

        private void Footfall(float weight)
        {
            _squash.Velocity += Feel.FootfallSquash;
            Thud = Mathf.Max(Thud, Heft * weight);
        }

        public void Tick(float dt, float real, float time)
        {
            if (GhostLeft > 0f) GhostLeft -= real;
            if (Jolt > 0f) Jolt = Mathf.Max(0f, Jolt - real * 5f);
            Travel(dt);

            if (_lungeT >= 0f)
            {
                _lungeT += dt;
                if (_lungeT >= _lungeOut && !_struck)
                {
                    // The blow has landed: the body pitches on past its feet, then recovers.
                    _struck = true;
                    Stand();
                    _lean.Push(_heading * 240f);
                    _squash.Velocity += Feel.BlowSquash;
                }
                if (_lungeT >= _lungeOut + Feel.StrikeReturn) _lungeT = -1f;
            }

            var swing = Feel.Swing(Heft);
            _shove.Tick(dt, Feel.ShoveHz * swing, Feel.ShoveDamping);
            _lean.Tick(dt, Feel.LeanHz * swing, Feel.LeanDamping);
            _squash.Tick(dt, Feel.SquashHz * swing, Feel.SquashDamping);
            _grow.Tick(dt, 5.2f, 0.42f);
            _yaw = Mathf.MoveTowardsAngle(_yaw, _targetYaw, dt * 900f);
            // White for the length of the stop, then gone in a tenth of a second: a flash, not a glow.
            if (_flash > 0f) _flash = Mathf.Max(0f, _flash - dt * 10f);
            Compose(time);
        }

        private Vector3 Along(float e, out Vector3 dir, out int seg, out float frac)
        {
            var segs = _path.Count - 1;
            var at = Mathf.Clamp(e, 0f, 1f) * segs;
            seg = Mathf.Min(Mathf.FloorToInt(at), segs - 1);
            frac = at - seg;
            var step = _path[seg + 1] - _path[seg];
            dir = step.sqrMagnitude > 0.0001f ? step.normalized : _heading;
            return Vector3.Lerp(_path[seg], _path[seg + 1], frac);
        }

        private void Travel(float dt)
        {
            if (_motion == Motion.None) return;
            _t += dt;
            var k = Mathf.Clamp01(_t / _duration);
            switch (_motion)
            {
                case Motion.Walk:
                {
                    _ground = Along(Mathf.SmoothStep(0f, 1f, k), out var dir, out var seg, out var frac);
                    Face(dir);
                    if (Data.Flying)
                    {
                        _lean.Aim(dir * (Feel.WalkLean * 1.7f));
                    }
                    else
                    {
                        // Each tile is one step: up off the floor and down again, with a footfall where it lands.
                        var light = Mathf.Lerp(1.5f, 0.5f, Mathf.InverseLerp(0.35f, 3f, Heft));
                        _hop = Feel.WalkHop * light * 4f * frac * (1f - frac);
                        _lean.Aim(dir * Feel.WalkLean);
                        if (seg > _stepsDone)
                        {
                            _stepsDone = seg;
                            Footfall(0.5f);
                        }
                    }
                    if (k < 1f) break;
                    _hop = 0f;
                    _lean.Aim(Vector3.zero);
                    if (!Data.Flying) Footfall(0.5f);
                    Arrive();
                    break;
                }
                case Motion.Dash:
                {
                    // It gathers speed all the way and stops dead, so its full speed arrives with the blow.
                    _ground = Along(k * k, out var dir, out _, out _);
                    Face(dir);
                    _lean.Aim(dir * Feel.DashLean);
                    if (k < 1f) break;
                    _lean.Aim(Vector3.zero);
                    _lean.Push(dir * 320f);
                    _squash.Target = 0f;
                    _squash.Velocity += Feel.SlamSquash;
                    Thud = Mathf.Max(Thud, Heft * 1.4f);
                    Arrive();
                    break;
                }
                case Motion.Drag:
                {
                    _ground = Along(1f - (1f - k) * (1f - k), out var dir, out _, out _);
                    _lean.Aim(dir * 16f);
                    if (k < 1f) break;
                    _lean.Aim(Vector3.zero);
                    _lean.Push(dir * 220f);
                    Footfall(0.7f);
                    Arrive();
                    break;
                }
                case Motion.Drop:
                {
                    _height = Feel.DropHeight * (1f - k * k);
                    Model.Plate.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, k);
                    if (k < 1f) break;
                    _height = 0f;
                    Model.Plate.localScale = Vector3.one;
                    _squash.Target = 0f;
                    _squash.Value = -0.28f;
                    _squash.Velocity = Feel.LandSquash;
                    Thud = Mathf.Max(Thud, Heft * 2.2f);
                    _motion = Motion.None;
                    break;
                }
            }
        }

        private void Arrive()
        {
            _motion = Motion.None;
            _path.Clear();
        }

        private void Compose(float time)
        {
            var root = Model.Root.transform;
            root.position = Ground;
            root.rotation = Quaternion.Euler(0f, _yaw, 0f);
            var bob = Mathf.Sin(time * 2.6f + _phase) * 0.012f;
            Model.Body.localPosition = new Vector3(0f, bob + _hover + _height + _hop, 0f);
            var lean = _lean.Value;
            var tilt = lean.magnitude;
            Model.Body.rotation = tilt > 0.01f ? Quaternion.AngleAxis(Mathf.Min(tilt, 55f), Vector3.Cross(Vector3.up, lean / tilt)) * root.rotation : root.rotation;
            var tall = 1f + Mathf.Clamp(_squash.Value, -0.5f, 0.6f);
            var wide = 1f / Mathf.Sqrt(tall);
            Model.Body.localScale = new Vector3(wide, tall, wide) * Mathf.Max(0.001f, _grow.Value);
            Model.Paint(_alpha, _flash * 0.85f);
        }

        /// <summary>The unit is dead: what is left of it is knocked the way the blow went, and falls.</summary>
        public Remains Die(Vector3 dir, int seed)
        {
            var style = _machine || Data.Flying ? Remains.Style.Wreck : _small ? Remains.Style.Splat : Remains.Style.Tumble;
            if (dir.sqrMagnitude < 0.0001f) return new Remains(Model, style, _heading, 0.35f, Heft, seed);
            return new Remains(Model, style, dir, 1f, Heft, seed);
        }

        /// <summary>The unit leaves without dying: a drone docking, or a figure the state no longer has.</summary>
        public Remains Leave() => new Remains(Model, Remains.Style.Shrink, Vector3.zero, 0f, Heft, 0);
    }

    /// <summary>A mine, an egg, a shield segment or a corpse, and how it arrives and takes a blow.</summary>
    public sealed class PropView
    {
        private Spring _squash;
        private Spring _grow;
        private readonly Vector3 _at;
        private Vector3 _from;
        private float _toss = -1f;
        private float _tossFor;
        private float _appearIn;
        private float _fade = 1f;
        private float _flash;
        private float _arm = -1f;
        private float _armFor;
        private bool _rise;

        public readonly Model Model;
        public BoardObject Data;
        public int? ShownHp;
        public float Thud;

        public PropView(BoardObject o, Transform parent)
        {
            Model = Figures.Object(o, parent);
            Data = o;
            ShownHp = o.Hp;
            _at = Iso.Tile(o.Pos.X, o.Pos.Y);
            _grow.Rest(1f);
            Model.Root.transform.position = _at;
            Model.Paint(1f);
        }

        public Vector3 Position => Model.Root.transform.position;

        /// <summary>False while a corpse waits for its body to fall, so its tag waits too.</summary>
        public bool Arrived => _appearIn <= 0f && _toss < 0f;

        /// <summary>Thrown from a hand: it arcs to its tile and lands.</summary>
        public void Toss(Vector3 from, float seconds)
        {
            _from = from + Vector3.up * 0.4f;
            _toss = 0f;
            _tossFor = Mathf.Max(0.01f, seconds);
            Compose();
        }

        /// <summary>Grows up out of the floor.</summary>
        public void Rise()
        {
            _rise = true;
            _grow.Value = 0.02f;
            _grow.Velocity = 0f;
            _grow.Target = 1f;
            Compose();
        }

        public void Pop()
        {
            _grow.Value = 0.1f;
            _grow.Velocity = 0f;
            _grow.Target = 1f;
            Compose();
        }

        /// <summary>Stays unseen for a while, then fades in: a corpse, once the body that left it has fallen.</summary>
        public void Appear(float after)
        {
            _appearIn = after;
            _fade = 0f;
            Compose();
        }

        /// <summary>A mine that has been set off: it jumps and blinks until it bursts.</summary>
        public void Arm(float seconds)
        {
            _arm = 0f;
            _armFor = Mathf.Max(0.01f, seconds);
        }

        public void Hit()
        {
            _flash = 1f;
            _squash.Velocity -= 9f;
        }

        public void Tick(float dt)
        {
            if (_toss >= 0f)
            {
                _toss += dt;
                if (_toss >= _tossFor)
                {
                    _toss = -1f;
                    _squash.Value = -0.25f;
                    _squash.Velocity = -2.5f;
                    Thud = 0.45f;
                }
            }
            if (_appearIn > 0f) _appearIn -= dt;
            else if (_fade < 1f) _fade = Mathf.Min(1f, _fade + dt * 4f);
            if (_arm >= 0f) _arm += dt;
            if (_flash > 0f) _flash = Mathf.Max(0f, _flash - dt * 5f);
            _squash.Tick(dt, 6f, 0.4f);
            _grow.Tick(dt, 5.5f, 0.45f);
            Compose();
        }

        private void Compose()
        {
            var pos = _at;
            var size = Mathf.Max(0.001f, _grow.Value);
            if (_toss >= 0f)
            {
                var k = Mathf.Clamp01(_toss / _tossFor);
                pos = Vector3.Lerp(_from, _at, k) + Vector3.up * (0.9f * 4f * k * (1f - k));
                size = Mathf.Lerp(0.5f, 1f, k);
            }
            var flash = _flash;
            if (_arm >= 0f)
            {
                var k = Mathf.Clamp01(_arm / _armFor);
                pos += Vector3.up * (0.34f * Mathf.Sin(k * Mathf.PI * 0.5f));
                // Blinks faster as the fuse runs down.
                flash = Mathf.Max(flash, Mathf.Sin(k * k * 22f) > 0f ? 0.9f : 0f);
            }
            Model.Root.transform.position = pos;
            var tall = 1f + Mathf.Clamp(_squash.Value, -0.5f, 0.5f);
            var wide = 1f / Mathf.Sqrt(tall);
            Model.Body.localScale = _rise ? new Vector3(1f, tall * size, 1f) : new Vector3(wide * size, tall * size, wide * size);
            Model.Paint(_fade, flash * 0.9f);
        }

        /// <summary>Destroyed: it breaks into its pieces, thrown the way the blow went.</summary>
        public Remains Break(Vector3 dir, int seed)
        {
            return new Remains(Model, Remains.Style.Wreck, dir.sqrMagnitude < 0.0001f ? Vector3.zero : dir, 0.8f, 0.5f, seed);
        }

        /// <summary>Taken off the board without being destroyed.</summary>
        public Remains Leave(RemovalReason reason)
        {
            switch (reason)
            {
                case RemovalReason.Hatched: return new Remains(Model, Remains.Style.Wreck, Vector3.zero, 0.6f, 0.4f, Data.Id);
                case RemovalReason.Detonated: return new Remains(Model, Remains.Style.Gone, Vector3.zero, 0f, 0.4f, 0);
                default: return new Remains(Model, Remains.Style.Shrink, Vector3.zero, 0f, 0.4f, 0);
            }
        }
    }

    /// <summary>
    /// What a unit or object leaves on the board for a moment after it is gone: a body that is knocked off its feet and falls, a
    /// machine that breaks into its parts, a hatchling that bursts. It owns the model until it has faded, then destroys it.
    /// </summary>
    public sealed class Remains
    {
        public enum Style { Tumble, Splat, Wreck, Shrink, Gone }

        private struct Shard
        {
            public Transform T;
            public Vector3 Velocity;
            public Vector3 Axis;
            public float Spin;
            public int Bounces;
        }

        private readonly Model _model;
        private readonly Style _style;
        private readonly float _heft;
        private readonly Vector3 _root;
        private readonly Quaternion _stood;
        private readonly Vector3 _axis;
        private readonly List<Shard> _shards = new List<Shard>();
        private Vector3 _offset;
        private Vector3 _velocity;
        private float _angle;
        private float _spin;
        private int _bounces;
        private bool _resting;
        private float _rest;
        private float _age;
        private bool _landed;

        public bool Done { get; private set; }

        /// <summary>A landing since the board last looked, by weight.</summary>
        public float Thud;

        public Vector3 Position => _root + Feel.Flat(_offset);

        public Remains(Model model, Style style, Vector3 dir, float force, float heft, int seed)
        {
            _model = model;
            _style = style;
            _heft = heft;
            _root = model.Root.transform.position;
            _stood = model.Body.rotation;
            _offset = model.Body.position - _root;
            var give = Mathf.Sqrt(Feel.Give(heft));
            switch (style)
            {
                case Style.Tumble:
                    _axis = Vector3.Cross(Vector3.up, dir);
                    _velocity = dir * (Feel.DeathFling * force * give) + Vector3.up * (Feel.DeathPop * give * Mathf.Lerp(0.45f, 1f, force));
                    _spin = Feel.DeathSpin * Feel.Swing(heft) * Mathf.Lerp(0.55f, 1f, force);
                    break;
                case Style.Wreck:
                {
                    var parts = model.BodyParts;
                    var centre = model.Body.position + Vector3.up * 0.3f;
                    for (var i = 0; i < parts.Count; i++)
                    {
                        var t = parts[i];
                        t.SetParent(model.Root.transform, true);
                        var away = t.position - centre;
                        away.y = 0f;
                        var a = Feel.Noise(seed, i, 1) * Mathf.PI * 2f;
                        away = away.sqrMagnitude > 0.0004f ? away.normalized : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        var v = away * (0.8f + 1.5f * Feel.Noise(seed, i, 2)) + dir * (1.6f * force) + Vector3.up * (2.2f + 2.4f * Feel.Noise(seed, i, 3));
                        var axis = new Vector3(Feel.Noise(seed, i, 4) - 0.5f, Feel.Noise(seed, i, 5) - 0.5f, Feel.Noise(seed, i, 6) - 0.5f);
                        _shards.Add(new Shard { T = t, Velocity = v, Axis = axis.sqrMagnitude > 0.001f ? axis.normalized : Vector3.right, Spin = 300f + 500f * Feel.Noise(seed, i, 7) });
                    }
                    break;
                }
            }
        }

        public void Destroy() => _model.Destroy();

        public void Tick(float dt)
        {
            if (Done) return;
            _age += dt;
            _model.Plate.localScale = Vector3.one * Mathf.Clamp01(1f - _age / 0.16f);
            switch (_style)
            {
                case Style.Tumble:
                    Tumble(dt);
                    break;
                case Style.Wreck:
                    Scatter(dt);
                    break;
                case Style.Splat:
                {
                    var k = Mathf.Clamp01(_age / 0.1f);
                    _model.Body.localScale = new Vector3(1f + 0.8f * k, Mathf.Lerp(1f, 0.1f, k), 1f + 0.8f * k);
                    if (!_landed && k >= 1f)
                    {
                        _landed = true;
                        Thud = _heft * 0.8f;
                    }
                    Fade((_age - 0.3f) / 0.25f);
                    break;
                }
                case Style.Shrink:
                {
                    var k = Mathf.Clamp01(_age / 0.18f);
                    _model.Root.transform.localScale = Vector3.one * (1f - k);
                    if (k >= 1f) Finish();
                    break;
                }
                default:
                    Finish();
                    break;
            }
        }

        private void Tumble(float dt)
        {
            if (!_resting)
            {
                _velocity.y -= Feel.Gravity * dt;
                _offset += _velocity * dt;
                _angle = Mathf.Min(90f, _angle + _spin * dt);
                if (_offset.y <= 0f && _velocity.y < 0f)
                {
                    _offset.y = 0f;
                    Thud = _heft * (_bounces == 0 ? 1.7f : 0.6f);
                    if (_bounces == 0 && _velocity.y < -1.2f)
                    {
                        _velocity = new Vector3(_velocity.x * 0.4f, -_velocity.y * Feel.Bounce, _velocity.z * 0.4f);
                        _bounces++;
                    }
                    else _resting = true;
                }
            }
            else
            {
                _angle = Mathf.MoveTowards(_angle, 90f, 700f * dt);
                _rest += dt;
                var k = (_rest - Feel.BodyRest) / Feel.BodyFade;
                if (k > 0f) _offset.y = -0.12f * Mathf.Clamp01(k);
                Fade(k);
            }
            // Lying down, the body rests on its back instead of half under the floor.
            var lift = Vector3.up * (0.11f * Mathf.Sin(_angle * Mathf.Deg2Rad));
            _model.Body.position = _root + _offset + lift;
            _model.Body.rotation = Quaternion.AngleAxis(_angle, _axis) * _stood;
        }

        private void Scatter(float dt)
        {
            for (var i = 0; i < _shards.Count; i++)
            {
                var s = _shards[i];
                if (s.Bounces < 3)
                {
                    s.Velocity.y -= Feel.Gravity * dt;
                    var p = s.T.position + s.Velocity * dt;
                    s.T.Rotate(s.Axis, s.Spin * dt, Space.World);
                    if (p.y <= 0.01f && s.Velocity.y < 0f)
                    {
                        p.y = 0.01f;
                        s.Bounces = s.Velocity.y > -1.4f ? 3 : s.Bounces + 1;
                        s.Velocity = new Vector3(s.Velocity.x * 0.5f, -s.Velocity.y * 0.34f, s.Velocity.z * 0.5f);
                        s.Spin *= 0.4f;
                        if (!_landed)
                        {
                            _landed = true;
                            Thud = _heft * 0.9f;
                        }
                    }
                    s.T.position = p;
                    _shards[i] = s;
                }
            }
            Fade((_age - 0.6f) / 0.3f);
        }

        private void Fade(float k)
        {
            if (k <= 0f) return;
            _model.Paint(Mathf.Clamp01(1f - k));
            if (k >= 1f) Finish();
        }

        private void Finish()
        {
            Done = true;
            _model.Destroy();
        }
    }
}
