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
}
