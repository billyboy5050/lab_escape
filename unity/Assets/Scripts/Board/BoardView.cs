#nullable enable
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;
using UnityEngine;

namespace LabEscape.Client
{
    public enum HighlightKind { Move, Target, Second, Chosen }

    /// <summary>What the board overlays show this frame.</summary>
    public sealed class BoardOverlay
    {
        public readonly List<(IReadOnlyList<Pos> Tiles, HighlightKind Kind)> Highlights = new List<(IReadOnlyList<Pos>, HighlightKind)>();
        public Pos? Hover;
        public IReadOnlyList<Pos>? MovePath;
        public CommandPreview? Preview;
        public IntentReport? Intents;
        public bool ShowIntents = true;
    }

    /// <summary>
    /// The 3D board. It plays a frame's cues as their moments come, so a figure is hit when the shot reaches it and not when the
    /// engine resolved it; between frames it is brought level with the game state, so nothing staged can leave it wrong. It also
    /// answers which tile is under the pointer.
    /// </summary>
    public sealed class BoardView
    {
        private const float FloorY = 0.02f;
        private const float StandingHeight = 0.85f;

        private readonly Content _c;
        private readonly RoomDef _room;
        private readonly IsoCamera _cam;
        private readonly Transform _root;
        private readonly RoomView _roomView;
        private readonly BoardTags _tags;
        private readonly OverlayLayer _floor;
        private readonly OverlayLayer _top;
        private readonly Dictionary<int, UnitView> _units = new Dictionary<int, UnitView>();
        private readonly Dictionary<int, PropView> _objects = new Dictionary<int, PropView>();
        private readonly List<Remains> _remains = new List<Remains>();
        private readonly List<Model> _ghosts = new List<Model>();
        private readonly List<(int Depth, Rect Box)> _probes = new List<(int, Rect)>();
        private CommandPreview? _ghostsFor;
        private GameState _shown;
        private Stage _stage = Stage.Empty;
        private int _next;

        public readonly FxSystem Fx;
        public readonly Ambience Ambience = new Ambience();

        /// <summary>A cue has just played. The fight's director hangs the camera, the clock and the interface on it.</summary>
        public event System.Action<Cue>? CuePlayed;

        /// <summary>Something landed on the floor here, with this weight.</summary>
        public event System.Action<Vector3, float>? Thudded;

        /// <summary>The figures on the board now. A unit hit this frame is still here until its death cue plays.</summary>
        public IReadOnlyDictionary<int, UnitView> Units => _units;

        public IReadOnlyDictionary<int, PropView> Objects => _objects;

        public BoardView(Content c, RoomDef room, IsoCamera cam, BoardTags tags, GameState initial)
        {
            _c = c;
            _room = room;
            _cam = cam;
            _tags = tags;
            _shown = initial;
            _root = new GameObject("Board").transform;
            _roomView = new RoomView(room, _root);
            tags.Coordinates(room.Board);
            _floor = new OverlayLayer("Floor overlay", Mats.FloorOverlay, _root);
            _top = new OverlayLayer("Top overlay", Mats.TopOverlay, _root);
            Fx = new FxSystem(_root, tags) { UnitAt = UnitPosition };
            Lighting.Reset();
            _cam.Frame(_roomView.BoundsMin, _roomView.BoundsMax);
            Level(initial, true);
        }

        public void Dispose()
        {
            Object.Destroy(_root.gameObject);
            _tags.Clear();
            Lighting.Reset();
        }

        private static bool Visible(Unit u) => !u.Dead && !u.Docked;

        /// <summary>Starts a frame: its cues play as the frame's clock reaches them, and the board ends on its state.</summary>
        public void Play(GameState s, Stage stage)
        {
            FinishStage();
            Level(_shown, false);
            _shown = s;
            _stage = stage;
            _next = 0;
            // What a unit carries (statuses, ammo, armor) reads from the new state at once. Its HP waits for the blow.
            foreach (var u in s.Units)
            {
                if (_units.TryGetValue(u.Id, out var v)) v.Data = u;
            }
            foreach (var o in s.Objects)
            {
                if (_objects.TryGetValue(o.Id, out var p)) p.Data = o;
            }
        }

        /// <summary>The queue has run out: the board ends on this state, and whatever is still falling may finish.</summary>
        public void Settle(GameState s)
        {
            FinishStage();
            _shown = s;
            Level(s, false);
        }

        /// <summary>Jumps to a state with no animation: an undo, or a skip.</summary>
        public void Snap(GameState s)
        {
            _stage = Stage.Empty;
            _next = 0;
            _shown = s;
            Fx.Clear();
            Level(s, true);
        }

        /// <summary>Plays any cue of the frame that has not had its moment yet: late, but never lost.</summary>
        private void FinishStage()
        {
            while (_next < _stage.Cues.Count) Dispatch(_stage.Cues[_next++]);
            _stage = Stage.Empty;
            _next = 0;
        }

        private UnitView AddUnit(Unit u)
        {
            var v = new UnitView(_c, u, _root);
            _units[u.Id] = v;
            return v;
        }

        /// <summary>
        /// Brings the board level with a state: every unit and object in it is on the board, on its tile and showing its HP, and
        /// nothing else is. Cues do this work as a frame plays; this is what guarantees it. With `hard`, motion stops too.
        /// </summary>
        private void Level(GameState s, bool hard)
        {
            var seen = new HashSet<int>();
            foreach (var u in s.Units.Where(Visible))
            {
                seen.Add(u.Id);
                if (!_units.TryGetValue(u.Id, out var v)) v = AddUnit(u);
                v.Data = u;
                if (hard)
                {
                    v.Snap(u.Pos);
                    v.ShownHp = v.GhostHp = u.Hp;
                    v.GhostLeft = 0f;
                }
                else
                {
                    if (v.ShownHp != u.Hp) v.SetHp(u.Hp);
                    if (!v.Moving && v.Tile != u.Pos) v.Snap(u.Pos);
                }
            }
            foreach (var id in _units.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                _remains.Add(_units[id].Leave());
                _units.Remove(id);
            }

            seen.Clear();
            foreach (var o in s.Objects.Where(x => !x.Destroyed))
            {
                seen.Add(o.Id);
                if (!_objects.TryGetValue(o.Id, out var p))
                {
                    p = new PropView(o, _root);
                    _objects[o.Id] = p;
                }
                p.Data = o;
                p.ShownHp = o.Hp;
                if (hard) p.Snap();
            }
            foreach (var id in _objects.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                _remains.Add(_objects[id].Leave(RemovalReason.Decayed));
                _objects.Remove(id);
            }

            if (!hard) return;
            foreach (var r in _remains) r.Destroy();
            _remains.Clear();
        }

        private static Vector3 Between(Pos from, Pos to) => Feel.Flat(Iso.Tile(to.X, to.Y) - Iso.Tile(from.X, from.Y));

        private void Dispatch(Cue cue)
        {
            switch (cue.Kind)
            {
                case CueKind.Windup:
                    if (_units.TryGetValue(cue.Actor, out var gathering))
                    {
                        var charge = cue.Event is Charged || (cue.Event is UnitMoved move && move.Mode == MoveMode.Charge);
                        gathering.Windup(Between(gathering.Tile, cue.Toward), charge);
                    }
                    break;
                case CueKind.Strike:
                    if (_units.TryGetValue(cue.Actor, out var striking)) striking.Strike(Between(striking.Tile, cue.Toward), cue.Duration);
                    break;
                default:
                    Present(cue);
                    break;
            }
            Fx.Play(cue, _shown);
            CuePlayed?.Invoke(cue);
        }

        /// <summary>Shows one event on the figures.</summary>
        private void Present(Cue cue)
        {
            switch (cue.Event)
            {
                case UnitMoved e:
                    if (_units.TryGetValue(e.Id, out var mover)) mover.Follow(e.From, e.Path, cue.Duration, e.Mode);
                    break;
                case UnitSpawned e:
                {
                    if (!Visible(e.Unit) || _units.ContainsKey(e.Unit.Id)) break;
                    var v = AddUnit(e.Unit);
                    if (e.Cause.StartsWith("wave", System.StringComparison.Ordinal)) v.DropIn(cue.Duration);
                    else v.PopIn();
                    if (cue.Actor >= 0 && _units.TryGetValue(cue.Actor, out var caster)) caster.Release(Between(caster.Tile, e.Unit.Pos), 0.4f);
                    break;
                }
                case ProjectileFired e:
                    if (cue.Actor >= 0 && _units.TryGetValue(cue.Actor, out var shooter)) shooter.Release(Between(e.From, Shots.End(e)), Shots.Style(e) == ShotStyle.Bolt ? 1f : 0.55f);
                    break;
                case AreaEffect e:
                    if (cue.Actor >= 0 && e.Tiles.Count > 0 && _units.TryGetValue(cue.Actor, out var source)) source.Release(Between(source.Tile, cue.Toward), 0.6f);
                    break;
                case DamageDealt e:
                    if (e.Target.Kind == EntityKind.Unit)
                    {
                        if (!_units.TryGetValue(e.Target.Id, out var hit)) break;
                        hit.SetHp(e.HpAfter);
                        hit.Hit(Feel.From(cue, e.Pos), e.Amount, e.Amount <= 0 && e.Absorbed > 0);
                    }
                    else if (_objects.TryGetValue(e.Target.Id, out var struck))
                    {
                        struck.ShownHp = e.HpAfter;
                        struck.Hit();
                    }
                    break;
                case Healed e:
                    if (_units.TryGetValue(e.Target, out var healed))
                    {
                        healed.SetHp(e.HpAfter);
                        healed.Pulse();
                    }
                    if (cue.Actor >= 0 && cue.Actor != e.Target && _units.TryGetValue(cue.Actor, out var healer) && healed != null) healer.Release(Between(healer.Tile, healed.Tile), 0.35f);
                    break;
                case UnitDied e:
                    if (!_units.TryGetValue(e.Id, out var dead)) break;
                    _units.Remove(e.Id);
                    _remains.Add(dead.Die(Feel.From(cue, e.Pos), e.Id * 13 + cue.Index));
                    break;
                case ObjectPlaced e:
                {
                    if (_objects.ContainsKey(e.Object.Id)) break;
                    var p = new PropView(e.Object, _root);
                    _objects[e.Object.Id] = p;
                    if (e.Object.Kind == ObjectKind.Corpse) p.Appear(Feel.BodyRest + 0.3f);
                    else if (e.Object.Kind == ObjectKind.Shield) p.Rise();
                    else if (cue.HasOrigin) p.Toss(Iso.Tile(cue.OriginX, cue.OriginY), cue.Duration);
                    else p.Pop();
                    if (cue.Actor >= 0 && _units.TryGetValue(cue.Actor, out var thrower)) thrower.Release(Between(thrower.Tile, e.Object.Pos), 0.4f);
                    break;
                }
                case ObjectDestroyed e:
                    if (!_objects.TryGetValue(e.Id, out var broken)) break;
                    _objects.Remove(e.Id);
                    _remains.Add(broken.Break(Feel.From(cue, e.Pos), e.Id * 17 + cue.Index));
                    break;
                case ObjectRemoved e:
                    if (!_objects.TryGetValue(e.Id, out var gone)) break;
                    _objects.Remove(e.Id);
                    _remains.Add(gone.Leave(e.Reason));
                    break;
                case ObjectTriggered e:
                    if (_objects.TryGetValue(e.Id, out var mine)) mine.Arm(cue.Duration);
                    break;
                case Charged e:
                    // A charge that had no room to run is a slam on the spot.
                    if (e.From == e.To && _units.TryGetValue(e.Id, out var charger)) charger.Strike(Between(e.To, cue.Toward), 0.07f);
                    break;
                case DroneDocked e:
                    if (!_units.TryGetValue(e.Id, out var docking)) break;
                    _units.Remove(e.Id);
                    _remains.Add(docking.Leave());
                    break;
                case DroneRedeployed e:
                {
                    var u = _shown.Units.FirstOrDefault(x => x.Id == e.Id);
                    if (u == null || !Visible(u) || _units.ContainsKey(e.Id)) break;
                    AddUnit(u).PopIn();
                    break;
                }
            }
        }

        private static bool IsFlat(BoardObject o) => o.Kind == ObjectKind.Mine || o.Kind == ObjectKind.Corpse;

        public Vector3? UnitPosition(int id) => _units.TryGetValue(id, out var v) ? v.Position : (Vector3?)null;

        /// <summary>The tile under a screen point: a figure first, so a click on one picks the tile it stands on, then the floor.</summary>
        public Pos? PickTile(Vector2 screen)
        {
            var bestDepth = int.MinValue;
            Pos? best = null;
            foreach (var u in _shown.Units.Where(Visible))
            {
                if (!_units.TryGetValue(u.Id, out var v)) continue;
                var depth = u.Pos.X + u.Pos.Y;
                if (depth > bestDepth && _cam.ScreenBox(v.Position, 0.32f, StandingHeight).Contains(screen))
                {
                    bestDepth = depth;
                    best = u.Pos;
                }
            }
            foreach (var o in _shown.Objects.Where(x => !x.Destroyed && !IsFlat(x)))
            {
                var depth = o.Pos.X + o.Pos.Y;
                if (depth > bestDepth && _cam.ScreenBox(Iso.Tile(o.Pos.X, o.Pos.Y), 0.32f, 0.6f).Contains(screen))
                {
                    bestDepth = depth;
                    best = o.Pos;
                }
            }
            if (best != null) return best;
            var g = _cam.GroundPoint(screen);
            var tile = new Pos(Mathf.FloorToInt(g.x), Mathf.FloorToInt(-g.z));
            return _room.Board.InBounds(tile) && _room.Board.TerrainId(tile) != "void" ? tile : (Pos?)null;
        }

        private void Landed(Vector3 at, float weight)
        {
            Fx.Dust(at, weight);
            Thudded?.Invoke(at, weight);
        }

        /// <summary>
        /// One frame of the board. `dt` is the board's clock, which a heavy blow stops and the 2x speed doubles; `real` is real
        /// time, for the camera and the interface; `frameTime` is how far into its frame the fight is.
        /// </summary>
        public void Tick(float dt, float real, float frameTime, BoardOverlay o)
        {
            while (_next < _stage.Cues.Count && _stage.Cues[_next].At <= frameTime) Dispatch(_stage.Cues[_next++]);

            var time = Time.unscaledTime;
            _cam.Tick(real);
            Ambience.Tick(real);
            foreach (var v in _units.Values)
            {
                v.Tick(dt, real, time);
                if (v.Thud <= 0f) continue;
                Landed(v.Ground, v.Thud);
                v.Thud = 0f;
            }
            foreach (var p in _objects.Values)
            {
                p.Tick(dt);
                if (p.Thud <= 0f) continue;
                Landed(p.Position, p.Thud);
                p.Thud = 0f;
            }
            for (var i = _remains.Count - 1; i >= 0; i--)
            {
                var r = _remains[i];
                r.Tick(dt);
                if (r.Thud > 0f)
                {
                    Landed(r.Position, r.Thud);
                    r.Thud = 0f;
                }
                if (r.Done) _remains.RemoveAt(i);
            }

            _probes.Clear();
            foreach (var u in _shown.Units.Where(Visible))
            {
                if (_units.TryGetValue(u.Id, out var v)) _probes.Add((u.Pos.X + u.Pos.Y, _cam.ScreenBox(v.Position, 0.3f, StandingHeight)));
            }
            foreach (var x in _shown.Objects.Where(x => !x.Destroyed))
            {
                _probes.Add((x.Pos.X + x.Pos.Y, _cam.ScreenBox(Iso.Tile(x.Pos.X, x.Pos.Y), 0.28f, IsFlat(x) ? 0.1f : 0.6f)));
            }
            if (o.Hover.HasValue) _probes.Add((o.Hover.Value.X + o.Hover.Value.Y, _cam.ScreenBox(Iso.Tile(o.Hover.Value.X, o.Hover.Value.Y), 0.32f, 0.1f)));
            _roomView.TickFade(_cam, _probes, real);

            SyncGhosts(o.Preview);
            DrawOverlays(o);
            Fx.Tick(dt, _shown, _cam);
        }

        private void SyncGhosts(CommandPreview? pv)
        {
            if (ReferenceEquals(pv, _ghostsFor)) return;
            _ghostsFor = pv;
            foreach (var g in _ghosts) g.Destroy();
            _ghosts.Clear();
            if (pv == null || !pv.Ok) return;
            foreach (var m in pv.Moves)
            {
                var u = pv.State?.Units.FirstOrDefault(x => x.Id == m.Id) ?? _shown.Units.FirstOrDefault(x => x.Id == m.Id);
                if (u == null) continue;
                var view = new UnitView(_c, u, _root, ghost: true);
                view.Snap(m.To);
                _ghosts.Add(view.Model);
            }
            foreach (var o in pv.Placed)
            {
                if (o.Kind == ObjectKind.Corpse) continue;
                var model = Figures.Object(o, _root);
                model.Root.transform.position = Iso.Tile(o.Pos.X, o.Pos.Y);
                model.Paint(0.5f);
                _ghosts.Add(model);
            }
            foreach (var u in pv.Spawned)
            {
                _ghosts.Add(new UnitView(_c, u, _root, ghost: true).Model);
            }
        }

        /// <summary>A soft dark patch under a figure. It shrinks and fades as the figure rises, so a falling unit's landing is seen coming.</summary>
        private void Shadow(UnitView v)
        {
            var size = v.Data.Def == "warden" ? 1.2f : Figures.IsSmall(v.Data.Def) ? 0.7f : 1f;
            var near = 1f - Mathf.Clamp01(v.Height / Feel.DropHeight);
            var at = v.Ground;
            at.y = FloorY;
            Color32 inner = new Color(0f, 0f, 0f, 0.8f * Mathf.Lerp(0.35f, 1f, near));
            Color32 outer = new Color(0f, 0f, 0f, 0f);
            _floor.B.Disc(at, 0.58f * size * Mathf.Lerp(0.5f, 1f, near), Vector3.right, Vector3.forward, inner, outer, 20);
        }

        private static Color HighlightColour(HighlightKind k)
        {
            switch (k)
            {
                case HighlightKind.Move: return Palette.Move;
                case HighlightKind.Target: return Palette.Target;
                case HighlightKind.Second: return Palette.Minion;
                default: return Palette.Player;
            }
        }

        private void TileFill(Pos t, float inset, Color col, float pulse = 0f)
        {
            _floor.B.Quad(Iso.Floor(t.X + inset, t.Y + 1f - inset, FloorY), Iso.Floor(t.X + inset, t.Y + inset, FloorY), Iso.Floor(t.X + 1f - inset, t.Y + inset, FloorY), Iso.Floor(t.X + 1f - inset, t.Y + 1f - inset, FloorY), col, pulse);
        }

        private void FloorRect(float x0, float y0, float x1, float y1, Color col, float pulse = 0f)
        {
            _floor.B.Quad(Iso.Floor(x0, y1, FloorY), Iso.Floor(x0, y0, FloorY), Iso.Floor(x1, y0, FloorY), Iso.Floor(x1, y1, FloorY), col, pulse);
        }

        /// <summary>Four corner brackets around a tile: the targeting reticle for hover and highlights.</summary>
        private void Brackets(Pos t, float inset, float len, float width, Color col, float pulse = 0f)
        {
            float x0 = t.X + inset, y0 = t.Y + inset, x1 = t.X + 1f - inset, y1 = t.Y + 1f - inset;
            FloorRect(x0, y0, x0 + len, y0 + width, col, pulse);
            FloorRect(x0, y0, x0 + width, y0 + len, col, pulse);
            FloorRect(x1 - len, y0, x1, y0 + width, col, pulse);
            FloorRect(x1 - width, y0, x1, y0 + len, col, pulse);
            FloorRect(x0, y1 - width, x0 + len, y1, col, pulse);
            FloorRect(x0, y1 - len, x0 + width, y1, col, pulse);
            FloorRect(x1 - len, y1 - width, x1, y1, col, pulse);
            FloorRect(x1 - width, y1 - len, x1, y1, col, pulse);
        }

        /// <summary>Diagonal stripes across a tile: the tiles an enemy will hit.</summary>
        private void Hatch(Pos t, Color col)
        {
            const float inset = 0.06f;
            const int stripes = 5;
            var side = 1f - 2f * inset;
            for (var i = 1; i <= stripes * 2 - 1; i++)
            {
                var d = i * side / stripes;
                var ax = d <= side ? d : side;
                var ay = d <= side ? 0f : d - side;
                var bx = d <= side ? 0f : d - side;
                var by = d <= side ? d : side;
                var a = Iso.Floor(t.X + inset + ax, t.Y + inset + ay, FloorY + 0.002f);
                var b = Iso.Floor(t.X + inset + bx, t.Y + inset + by, FloorY + 0.002f);
                FloorLine(a, b, 0.045f, col);
            }
        }

        private void FloorLine(Vector3 a, Vector3 b, float width, Color col)
        {
            var dir = b - a;
            if (dir.sqrMagnitude < 0.0001f) return;
            var side = new Vector3(-dir.z, 0f, dir.x).normalized;
            _floor.B.Strip(a, b, width, side, col);
        }

        private void FloorDashes(Vector3 a, Vector3 b, float width, float dash, float gap, Color col)
        {
            var length = (b - a).magnitude;
            if (length < 0.001f) return;
            var dir = (b - a) / length;
            for (var d = 0f; d < length; d += dash + gap) FloorLine(a + dir * d, a + dir * Mathf.Min(length, d + dash), width, col);
        }

        private void FloorArrow(Vector3 from, Vector3 to, Color col, float size = 0.14f)
        {
            var dir = to - from;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
            var side = new Vector3(-dir.z, 0f, dir.x);
            var b = _floor.B;
            var n = Vector3.up;
            Color32 c = col;
            int i = b.Vert(to, n, c), j = b.Vert(to - dir * size + side * (size * 0.55f), n, c), k = b.Vert(to - dir * size - side * (size * 0.55f), n, c);
            b.Tri(i, j, k);
        }

        private static Vector3 OnFloor(Pos p) => Iso.Tile(p.X, p.Y, FloorY + 0.004f);

        private void DrawLine(LinePreview l, Color baseColour, float width, bool dashed)
        {
            var a = OnFloor(l.From);
            var b = OnFloor(l.To);
            var colour = l.Kind == LineKind.Reflect ? Palette.Parasite : l.Kind == LineKind.Charge ? Palette.Danger : l.Kind == LineKind.Chain ? Palette.Target : baseColour;
            var w = l.Kind == LineKind.Charge ? width * 2f : width;
            if (dashed || l.Kind == LineKind.Chain) FloorDashes(a, b, w, 0.12f, 0.07f, colour);
            else FloorLine(a, b, w, colour);
            FloorArrow(a, b, colour, l.Kind == LineKind.Charge ? 0.22f : 0.15f);
        }

        private void DrawIntents(IntentReport rep, float alpha)
        {
            foreach (var it in rep.Intents)
            {
                foreach (var t in it.Tiles) Hatch(t, Palette.WithAlpha(Palette.Danger, 0.5f * alpha));
            }
            foreach (var it in rep.Intents)
            {
                var col = it.Team == Team.Enemy ? Palette.Enemy : Palette.Player;
                if (it.Path.Count > 0)
                {
                    var c = Palette.WithAlpha(col, 0.9f * alpha);
                    var prev = OnFloor(it.From);
                    foreach (var p in it.Path)
                    {
                        var next = OnFloor(p);
                        FloorDashes(prev, next, 0.04f, 0.09f, 0.06f, c);
                        prev = next;
                    }
                    if (it.Arrival) _floor.B.Ring(OnFloor(it.To), 0.1f, 0.135f, Vector3.right, Vector3.forward, c);
                    else _floor.B.Disc(OnFloor(it.To), 0.08f, Vector3.right, Vector3.forward, c, c, 12);
                }
                if (it.Arrival) continue;
                var line = Palette.WithAlpha(it.Team == Team.Enemy ? Palette.Danger : Palette.Player, 0.95f * alpha);
                foreach (var l in it.Lines) DrawLine(l, line, 0.04f, false);
            }
        }

        private void DrawOverlays(BoardOverlay o)
        {
            _floor.Begin();
            _top.Begin();
            foreach (var v in _units.Values) Shadow(v);
            foreach (var (tiles, kind) in o.Highlights)
            {
                var col = HighlightColour(kind);
                var pulse = kind == HighlightKind.Target ? 0.6f : 0f;
                var fill = kind == HighlightKind.Move ? 0.16f : kind == HighlightKind.Chosen ? 0.34f : 0.24f;
                foreach (var t in tiles)
                {
                    TileFill(t, 0.05f, Palette.WithAlpha(col, fill), pulse);
                    Brackets(t, 0.07f, 0.17f, 0.04f, col, pulse);
                }
            }
            if (o.Hover.HasValue) Brackets(o.Hover.Value, 0.02f, 0.22f, 0.045f, Palette.WithAlpha(Palette.Ink, 0.9f));

            var pv = o.Preview != null && o.Preview.Ok ? o.Preview : null;
            if (o.ShowIntents)
            {
                if (pv == null && o.Intents != null) DrawIntents(o.Intents, 1f);
                if (pv?.Intents != null) DrawIntents(pv.Intents, 0.55f);
            }

            if (pv != null)
            {
                var area = Palette.Hazard;
                foreach (var t in pv.AreaTiles)
                {
                    TileFill(t, 0.05f, Palette.WithAlpha(area, 0.2f));
                    Brackets(t, 0.05f, 0.14f, 0.035f, area);
                }
            }

            if (o.MovePath != null && o.MovePath.Count > 0)
            {
                var player = _shown.Units.FirstOrDefault(u => u.Id == 0);
                var prev = player != null ? OnFloor(player.Pos) : OnFloor(o.MovePath[0]);
                foreach (var p in o.MovePath)
                {
                    var next = OnFloor(p);
                    FloorDashes(prev, next, 0.06f, 0.05f, 0.09f, Palette.Move);
                    prev = next;
                }
                _floor.B.Ring(prev, 0.15f, 0.2f, Vector3.right, Vector3.forward, Palette.Move);
            }

            if (pv != null) DrawPreviewLines(pv);
            _floor.End();
            _top.End();
        }

        private Vector3 Chest(Pos p) => Iso.Tile(p.X, p.Y, 0.4f);

        private void DrawPreviewLines(CommandPreview pv)
        {
            var fwd = _cam.Forward;
            void Line(Vector3 a, Vector3 b, float width, Color col)
            {
                var dir = b - a;
                if (dir.sqrMagnitude < 0.0001f) return;
                _top.B.Strip(a, b, width, Vector3.Cross(dir, fwd).normalized, col);
            }

            foreach (var l in pv.Lines)
            {
                var a = Chest(l.From);
                var b = Chest(l.To);
                var col = l.Kind == LineKind.Reflect ? Palette.Parasite : l.Kind == LineKind.Charge ? Palette.Danger : l.Kind == LineKind.Chain ? Palette.Target : Palette.Ink;
                Line(a, b, l.Kind == LineKind.Charge ? 0.07f : 0.035f, col);
                var dir = b - a;
                if (dir.sqrMagnitude < 0.0001f) continue;
                dir.Normalize();
                var side = Vector3.Cross(dir, fwd).normalized * 0.075f;
                var tip = _top.B.Vert(b, Vector3.up, col);
                var left = _top.B.Vert(b - dir * 0.15f + side, Vector3.up, col);
                var right = _top.B.Vert(b - dir * 0.15f - side, Vector3.up, col);
                _top.B.Tri(tip, left, right);
            }
            foreach (var m in pv.Moves)
            {
                var prev = Iso.Tile(m.From.X, m.From.Y, 0.05f);
                foreach (var p in m.Path)
                {
                    var next = Iso.Tile(p.X, p.Y, 0.05f);
                    Line(prev, next, 0.025f, Palette.WithAlpha(Palette.Ink, 0.8f));
                    prev = next;
                }
            }
            foreach (var h in pv.Hits)
            {
                if (!h.Friendly) continue;
                var pos = h.Ref.Kind == EntityKind.Unit ? pv.State?.Units.FirstOrDefault(u => u.Id == h.Ref.Id)?.Pos ?? h.Pos : h.Pos;
                _top.B.Ring(Iso.Tile(pos.X, pos.Y, 0.05f), 0.4f, 0.46f, Vector3.right, Vector3.forward, Palette.Danger, 28, 0.5f);
            }
        }
    }
}
