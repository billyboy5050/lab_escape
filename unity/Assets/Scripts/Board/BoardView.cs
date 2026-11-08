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

    /// <summary>A unit's figure, and how it gets from one tile to the next.</summary>
    public sealed class UnitView
    {
        private readonly List<Vector3> _path = new List<Vector3>();
        private float _t;
        private float _duration;
        private float _flash;
        private float _flinch;
        private float _yaw = 135f;
        private float _targetYaw = 135f;
        private readonly float _phase;
        private readonly float _hover;

        public readonly Model Model;
        public Pos Tile;

        public UnitView(Unit u, Transform parent, bool ghost = false)
        {
            Model = Figures.Unit(u.Def, u.Team, parent);
            Tile = u.Pos;
            _phase = u.Id * 0.37f;
            _hover = u.Flying ? 0.12f : 0f;
            Model.Root.transform.position = Iso.Tile(u.Pos.X, u.Pos.Y);
            Model.Root.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (ghost) Model.Paint(0.42f);
        }

        public bool Moving => _duration > 0f;

        public Vector3 Position => Model.Root.transform.position;

        public void Snap(Pos tile)
        {
            Tile = tile;
            _path.Clear();
            _duration = 0f;
            Model.Root.transform.position = Iso.Tile(tile.X, tile.Y);
        }

        /// <summary>Walks through the tiles of a move in the given time. A second move in one frame continues from the first.</summary>
        public void Follow(Pos from, IReadOnlyList<Pos> tiles, float seconds)
        {
            if (seconds <= 0f || tiles.Count == 0)
            {
                if (tiles.Count > 0) Snap(tiles[tiles.Count - 1]);
                return;
            }
            if (_path.Count == 0 || !Moving)
            {
                _path.Clear();
                _path.Add(Iso.Tile(from.X, from.Y));
                _t = 0f;
                _duration = 0f;
            }
            foreach (var p in tiles) _path.Add(Iso.Tile(p.X, p.Y));
            _duration += seconds;
            Tile = tiles[tiles.Count - 1];
        }

        public void Face(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.0001f) return;
            _targetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        public void Flinch()
        {
            _flash = 1f;
            _flinch = 1f;
        }

        public void Tick(float dt, float time)
        {
            var root = Model.Root.transform;
            if (Moving)
            {
                _t += dt;
                var k = Mathf.Clamp01(_t / _duration);
                var eased = Mathf.SmoothStep(0f, 1f, k) * (_path.Count - 1);
                var i = Mathf.Min(Mathf.FloorToInt(eased), _path.Count - 2);
                var pos = Vector3.Lerp(_path[i], _path[i + 1], eased - i);
                root.position = pos;
                Face(_path[i + 1] - _path[i]);
                if (k >= 1f)
                {
                    _duration = 0f;
                    _path.Clear();
                }
            }
            _yaw = Mathf.MoveTowardsAngle(_yaw, _targetYaw, dt * 720f);
            root.rotation = Quaternion.Euler(0f, _yaw, 0f);
            var bob = Mathf.Sin(time * 2.6f + _phase) * 0.012f;
            var kick = _flinch > 0f ? Mathf.Sin(_flinch * Mathf.PI * 3f) * 0.05f * _flinch : 0f;
            Model.Body.localPosition = new Vector3(0f, bob + _hover, -kick);
            if (_flash > 0f || _flinch > 0f)
            {
                _flash = Mathf.Max(0f, _flash - dt * 6f);
                _flinch = Mathf.Max(0f, _flinch - dt * 4f);
                Model.Paint(1f, _flash * 0.8f);
            }
        }
    }

    /// <summary>The 3D board: draws a game state, animates the events that led to it, and answers which tile is under the pointer.</summary>
    public sealed class BoardView
    {
        private const float FloorY = 0.02f;
        private const float StandingHeight = 0.85f;

        private sealed class Leaving
        {
            public Model Model = null!;
            public float Left = 0.3f;
        }

        private readonly Content _c;
        private readonly RoomDef _room;
        private readonly IsoCamera _cam;
        private readonly Transform _root;
        private readonly RoomView _roomView;
        private readonly BoardTags _tags;
        private readonly OverlayLayer _floor;
        private readonly OverlayLayer _top;
        private readonly Dictionary<int, UnitView> _units = new Dictionary<int, UnitView>();
        private readonly Dictionary<int, Model> _objects = new Dictionary<int, Model>();
        private readonly List<Leaving> _leaving = new List<Leaving>();
        private readonly List<Model> _ghosts = new List<Model>();
        private readonly List<(int Depth, Rect Box)> _probes = new List<(int, Rect)>();
        private CommandPreview? _ghostsFor;
        private GameState _shown;

        public readonly FxSystem Fx;

        public BoardView(Content c, RoomDef room, IsoCamera cam, BoardTags tags, GameState initial)
        {
            _c = c;
            _room = room;
            _cam = cam;
            _tags = tags;
            _shown = initial;
            _root = new GameObject("Board").transform;
            _roomView = new RoomView(room, _root);
            _floor = new OverlayLayer("Floor overlay", Mats.FloorOverlay, _root);
            _top = new OverlayLayer("Top overlay", Mats.TopOverlay, _root);
            Fx = new FxSystem(_root, tags);
            _cam.Frame(_roomView.BoundsMin, _roomView.BoundsMax);
            Show(initial, System.Array.Empty<GameEvent>(), 0f, true);
        }

        public void Dispose()
        {
            Object.Destroy(_root.gameObject);
            _tags.Clear();
        }

        private static bool Visible(Unit u) => !u.Dead && !u.Docked;

        /// <summary>Moves the board to a state. With `snap`, everything jumps there; otherwise units walk the paths in the events.</summary>
        public void Show(GameState s, IReadOnlyList<GameEvent> events, float seconds, bool snap)
        {
            _shown = s;
            var moved = new HashSet<int>();
            foreach (var e in events)
            {
                switch (e)
                {
                    case UnitMoved m when _units.TryGetValue(m.Id, out var v) && !snap:
                    {
                        var perTile = Mathf.Min(seconds * 0.9f / Mathf.Max(1, m.Path.Count), 0.14f);
                        v.Follow(m.From, m.Path, perTile * m.Path.Count);
                        moved.Add(m.Id);
                        break;
                    }
                    case DamageDealt d when d.Amount > 0 && d.Target.Kind == EntityKind.Unit && _units.TryGetValue(d.Target.Id, out var hit):
                        hit.Flinch();
                        break;
                    case ProjectileFired p when p.Source.Kind == SourceKind.Unit && p.Source.TextId == null && _units.TryGetValue(p.Source.NumberId, out var shooter):
                    {
                        var end = p.HitPos ?? (p.Path.Count > 0 ? p.Path[p.Path.Count - 1] : p.From);
                        shooter.Face(Iso.Tile(end.X, end.Y) - Iso.Tile(p.From.X, p.From.Y));
                        break;
                    }
                }
            }

            var seenUnits = new HashSet<int>();
            foreach (var u in s.Units.Where(Visible))
            {
                seenUnits.Add(u.Id);
                if (!_units.TryGetValue(u.Id, out var view))
                {
                    _units[u.Id] = new UnitView(u, _root);
                }
                else if (!moved.Contains(u.Id) && (snap || !view.Moving) && view.Tile != u.Pos)
                {
                    view.Snap(u.Pos);
                }
                else if (snap)
                {
                    view.Snap(u.Pos);
                }
            }
            foreach (var id in _units.Keys.Where(id => !seenUnits.Contains(id)).ToList())
            {
                _leaving.Add(new Leaving { Model = _units[id].Model });
                _units.Remove(id);
            }

            var seenObjects = new HashSet<int>();
            foreach (var o in s.Objects.Where(x => !x.Destroyed))
            {
                seenObjects.Add(o.Id);
                if (!_objects.TryGetValue(o.Id, out var model))
                {
                    model = Figures.Object(o, _root);
                    model.Paint(1f);
                    _objects[o.Id] = model;
                }
                model.Root.transform.position = Iso.Tile(o.Pos.X, o.Pos.Y);
            }
            foreach (var id in _objects.Keys.Where(id => !seenObjects.Contains(id)).ToList())
            {
                _leaving.Add(new Leaving { Model = _objects[id] });
                _objects.Remove(id);
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

        public void Tick(float dt, BoardOverlay o)
        {
            var time = Time.unscaledTime;
            _cam.Tick();
            foreach (var v in _units.Values) v.Tick(dt, time);
            for (var i = _leaving.Count - 1; i >= 0; i--)
            {
                var l = _leaving[i];
                l.Left -= dt;
                l.Model.Paint(Mathf.Clamp01(l.Left / 0.3f));
                l.Model.Root.transform.position += Vector3.down * (dt * 0.5f);
                if (l.Left > 0f) continue;
                l.Model.Destroy();
                _leaving.RemoveAt(i);
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
            _roomView.TickFade(_cam, _probes, dt);

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
                var view = new UnitView(u, _root, ghost: true);
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
                _ghosts.Add(new UnitView(u, _root, ghost: true).Model);
            }
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
