#nullable enable
using System.Collections.Generic;
using LabEscape.Core;
using UnityEngine;
using Grid = LabEscape.Core.Grid;

namespace LabEscape.Client
{
    /// <summary>
    /// Combat effects, drawn from the engine's events for each animation frame. They read the events and never the rules. Every
    /// effect is a short-lived record, and all of them are drawn into one mesh that is rebuilt each frame.
    /// </summary>
    public sealed class FxSystem
    {
        private enum Kind { Glow, Ring, RingFloor, Line, Tracer, Blob, Spark, Bolt, Boom, Smoke, Puff, FlashTile, Flame, Beam }

        private struct Item
        {
            public Kind Kind;
            public float Start;
            public float Duration;
            public Vector3 A;
            public Vector3 B;
            public Color Colour;
            public float Size;
            public float Width;
            public int Seed;
        }

        private static readonly Color PlayerShot = Palette.Hex(0x7fd4ff);
        private static readonly Color MinionShot = Palette.Hex(0xa6ecff);
        private static readonly Color EnemyShot = Palette.Hex(0xffb36a);
        private static readonly Color HazardShot = Palette.Hex(0xffd24a);
        private static readonly Color Acid = Palette.Hex(0xb6f04a);
        private static readonly Color ParasiteShot = Palette.Hex(0xff7ad9);
        private static readonly Color SparkColour = Palette.Hex(0xfff1a8);
        private static readonly Color Fire = Palette.Hex(0xffa03a);
        private static readonly Color Green = Palette.Hex(0x8be03a);
        private static readonly Color Heal = Palette.Hex(0x5ff0a0);
        private static readonly Color Smoke = Palette.Hex(0x2b2f33);

        private readonly List<Item> _items = new List<Item>();
        private readonly OverlayLayer _layer;
        private readonly BoardTags _tags;
        private float _now;
        private float _scale = 1f;

        public FxSystem(Transform parent, BoardTags tags)
        {
            _layer = new OverlayLayer("Effects", Mats.Fx, parent);
            _tags = tags;
        }

        private static Vector3 Body(Pos p, float dy = 0f) => Iso.Tile(p.X, p.Y, 0.42f + dy);

        private static Vector3 Feet(Pos p) => Iso.Tile(p.X, p.Y, 0.04f);

        /// <summary>A fixed number per tile, for decoration only, so a replay draws the same sparks.</summary>
        private static float Noise(int x, int y, int salt)
        {
            unchecked
            {
                var h = (uint)((x + 17) * 374761393) + (uint)((y + 31) * 668265263) + (uint)((salt + 7) * -2048144777);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h ^ (h >> 16)) / 4294967296f;
            }
        }

        private void Add(Kind kind, float duration, Vector3 a, Vector3 b, Color colour, float size = 0f, float width = 0f, float delay = 0f, int seed = 0)
        {
            _items.Add(new Item { Kind = kind, Start = _now + delay * _scale, Duration = duration * _scale, A = a, B = b, Colour = colour, Size = size, Width = width, Seed = seed });
        }

        private void Glow(Vector3 at, Color c, float r, float delay = 0f) => Add(Kind.Glow, 0.34f, at, at, c, r, 0f, delay);

        private void Ring(Vector3 at, Color c, float r, float w = 0.04f, float delay = 0f, float duration = 0.42f) => Add(Kind.Ring, duration, at, at, c, r, w, delay);

        private void Sparks(Vector3 at, Color c, int n, float len, int seed)
        {
            for (var i = 0; i < n; i++)
            {
                var a = i * Mathf.PI * 2f / n + Noise(seed, i, 5) * 0.8f;
                var l = len * (0.6f + Noise(seed, i, 6) * 0.7f);
                Add(Kind.Spark, 0.42f, at, new Vector3(Mathf.Cos(a) * l, Mathf.Sin(a) * l, 0f), c);
            }
        }

        private void Bolt(Vector3 a, Vector3 b, int seed, Color c) => Add(Kind.Bolt, 0.3f, a, b, c, 0f, 0.05f, 0f, seed);

        private void Shot(Vector3 from, Vector3 to, ProjectileFired e, int seed)
        {
            var src = e.Source;
            Color c;
            var lobbed = false;
            if (e.Reflected == true)
            {
                c = ParasiteShot;
                lobbed = true;
            }
            else if (src.Via.Contains("acid") || src.Def == "spitter")
            {
                c = Acid;
                lobbed = true;
            }
            else if (src.Via.Contains("parasite"))
            {
                c = ParasiteShot;
                lobbed = true;
            }
            else if (src.Side == SourceSide.Hazard) c = HazardShot;
            else if (src.Side == SourceSide.Enemy) c = EnemyShot;
            else c = src.Category == SourceCategory.Minion ? MinionShot : PlayerShot;

            if (lobbed)
            {
                Add(Kind.Blob, 0.26f, from, to, c, 0.1f);
                Glow(to, c, 0.32f, 0.22f);
                Sparks(to, c, 6, 0.3f, seed);
                return;
            }
            Add(Kind.Line, 0.28f, from, to, Palette.WithAlpha(c, 0.4f), 0f, 0.025f);
            Add(Kind.Tracer, 0.2f, from, to, c, 0.5f, 0.07f);
            Glow(from, c, 0.24f);
            Glow(to, Color.white, 0.22f, 0.12f);
            Ring(to, c, 0.22f, 0.03f, 0.12f);
        }

        private void Area(AreaKind kind, List<Pos> tiles, int seed)
        {
            if (tiles.Count == 0) return;
            var centre = Vector3.zero;
            foreach (var t in tiles) centre += Body(t);
            centre /= tiles.Count;
            for (var i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                var p = Body(t);
                switch (kind)
                {
                    case AreaKind.Blast:
                    case AreaKind.Burst:
                        Add(Kind.Boom, 0.42f, p, p, kind == AreaKind.Burst && i % 2 == 1 ? Acid : Fire, 0.5f, 0f, (i % 3) * 0.04f);
                        if (i < 5) Add(Kind.Smoke, 0.8f, p, p, Smoke, 0.32f, 0f, 0.15f + i * 0.05f);
                        break;
                    case AreaKind.Spore:
                        for (var j = 0; j < 3; j++)
                        {
                            var o = new Vector3(Noise(t.X, t.Y, j) - 0.5f, 0.05f * j, Noise(t.Y, t.X, j) - 0.5f) * 0.7f;
                            Add(Kind.Puff, 0.7f, p + o, p + o, Palette.WithAlpha(Palette.Hex(0x7fd36a), 0.55f), 0.16f + j * 0.05f, 0f, j * 0.08f);
                        }
                        break;
                    case AreaKind.Gas:
                        Add(Kind.Puff, 1.1f, p, p, Palette.WithAlpha(Palette.Poison, 0.5f), 0.5f);
                        Add(Kind.Puff, 1.1f, p + Vector3.right * 0.14f, p, Palette.WithAlpha(Palette.Poison, 0.42f), 0.42f, 0f, 0.1f);
                        break;
                    case AreaKind.Panel:
                        Add(Kind.FlashTile, 0.4f, Iso.Tile(t.X, t.Y, 0.04f), Vector3.zero, HazardShot, 0.46f);
                        Bolt(Iso.Tile(t.X - 0.35f, t.Y - 0.3f, 0.1f), Iso.Tile(t.X + 0.35f, t.Y + 0.3f, 0.1f), seed + i * 3, HazardShot);
                        Bolt(Iso.Tile(t.X + 0.32f, t.Y - 0.32f, 0.1f), Iso.Tile(t.X - 0.32f, t.Y + 0.3f, 0.1f), seed + i * 3 + 1, HazardShot);
                        break;
                    case AreaKind.Sweep:
                        Add(Kind.Flame, 0.5f, Iso.Tile(t.X, t.Y, 0.08f), Vector3.zero, Fire, 0.3f, 0f, i * 0.06f, seed + i);
                        break;
                }
            }
            if (kind == AreaKind.Blast || kind == AreaKind.Burst)
            {
                Glow(centre, Color.white, 0.8f);
                Ring(centre, kind == AreaKind.Burst ? Acid : Fire, 1.5f, 0.06f, 0f, 0.55f);
                Sparks(centre, SparkColour, 12, 1.2f, seed);
            }
            else if (kind == AreaKind.Sweep) Glow(centre, Fire, 1f);
        }

        /// <summary>Starts the effects for one frame's events. `scale` shortens them when the fight runs faster.</summary>
        public void Play(IReadOnlyList<GameEvent> events, GameState s, float scale)
        {
            _scale = scale;
            var hitsAt = new Dictionary<Pos, int>();
            Pos? UnitPos(int id)
            {
                foreach (var u in s.Units)
                {
                    if (u.Id == id) return u.Pos;
                }
                return null;
            }
            for (var i = 0; i < events.Count; i++)
            {
                var seed = i * 7 + 3;
                switch (events[i])
                {
                    case ProjectileFired e:
                    {
                        Pos? end = e.HitPos ?? (e.Path.Count > 0 ? e.Path[e.Path.Count - 1] : (Pos?)null);
                        if (end.HasValue) Shot(Body(e.From), Body(end.Value), e, seed);
                        break;
                    }
                    case ChainArc e:
                        Bolt(Body(e.From), Body(e.To), seed, Palette.Hex(0xfff27a));
                        Glow(Body(e.To), Color.white, 0.24f);
                        break;
                    case AreaEffect e:
                        Area(e.Kind, e.Tiles, seed);
                        break;
                    case DamageDealt e:
                    {
                        hitsAt.TryGetValue(e.Pos, out var n);
                        hitsAt[e.Pos] = n + 1;
                        var text = e.Amount > 0 ? $"-{e.Amount}" : e.Absorbed > 0 ? "BLOCK" : "0";
                        var colour = e.Amount > 0 ? Palette.Hex(0xff5a4a) : Palette.Hex(0xb9c4c9);
                        _tags.Float(Body(e.Pos, 0.3f), text, colour, e.Amount >= 3 ? 30 : 25, n * 20, n * 0.05f, scale);
                        if (e.Amount > 0) Glow(Body(e.Pos), e.TargetSide == Team.Enemy ? Fire : Palette.Danger, 0.34f);
                        break;
                    }
                    case Healed e:
                    {
                        var p = UnitPos(e.Target);
                        if (!p.HasValue) break;
                        _tags.Float(Body(p.Value, 0.3f), $"+{e.Amount}", Heal, 25, 0, 0f, scale);
                        Ring(Body(p.Value), Heal, 0.42f);
                        break;
                    }
                    case UnitDied e:
                    {
                        var at = Body(e.Pos);
                        var big = e.Def == "warden" ? 1.5f : 1f;
                        var c = e.Def == "player" ? Palette.Danger : e.Team == Team.Enemy ? Fire : Figures.IsSmall(e.Def) ? Green : PlayerShot;
                        Glow(at, c, 0.5f * big);
                        Ring(at, c, 0.52f * big, 0.05f);
                        Sparks(at, c, 10, 0.55f, seed);
                        Add(Kind.Smoke, 0.8f, at, at, Smoke, 0.34f);
                        break;
                    }
                    case Charged e:
                        Add(Kind.Line, 0.4f, Body(e.From), Body(e.To), Palette.WithAlpha(Palette.Danger, 0.4f), 0f, 0.3f);
                        Add(Kind.Line, 0.4f, Body(e.From), Body(e.To), Palette.Hex(0xffd0c0), 0f, 0.06f);
                        Ring(Body(e.To), Palette.Hex(0xff6a4a), 0.6f, 0.06f, 0f, 0.55f);
                        break;
                    case UnitSpawned e:
                    {
                        var p = e.Unit.Pos;
                        if (e.Cause.StartsWith("wave", System.StringComparison.Ordinal))
                        {
                            Add(Kind.Beam, 0.6f, Feet(p), Feet(p), Palette.Danger, 1.8f, 0.5f);
                            Add(Kind.RingFloor, 0.5f, Feet(p), Feet(p), Palette.Hex(0xff6a4a), 0.5f, 0.04f);
                        }
                        else if (e.Cause == "egg" || e.Cause == "parasite")
                        {
                            Glow(Body(p), Green, 0.44f);
                            Sparks(Body(p), Green, 8, 0.4f, seed);
                        }
                        else
                        {
                            Add(Kind.Beam, 0.5f, Feet(p), Feet(p), PlayerShot, 1.1f, 0.6f);
                            Add(Kind.RingFloor, 0.45f, Feet(p), Feet(p), PlayerShot, 0.5f, 0.04f);
                        }
                        break;
                    }
                    case ObjectPlaced e:
                        if (e.Object.Kind != ObjectKind.Corpse) Ring(Body(e.Object.Pos), e.Object.Kind == ObjectKind.Egg ? Green : PlayerShot, 0.4f);
                        break;
                    case ObjectDestroyed e:
                        Sparks(Body(e.Pos), Palette.Hex(0xc8d0d4), 8, 0.4f, seed);
                        Ring(Body(e.Pos), Palette.Hex(0xc8d0d4), 0.36f);
                        break;
                    case ObjectTriggered e:
                        Glow(Body(e.Pos), Color.white, 0.5f);
                        break;
                    case EggHatched e:
                        Glow(Body(e.Pos), Green, 0.5f);
                        Sparks(Body(e.Pos), Palette.Hex(0x4fc890), 7, 0.5f, seed);
                        break;
                    case StatusApplied e:
                    {
                        var p = UnitPos(e.Target);
                        if (p.HasValue) _tags.Float(Body(p.Value, 0.5f), Names.Of(e.Status).ToUpperInvariant(), Palette.StatusColor(e.Status), 15, 0, 0.08f, scale);
                        break;
                    }
                    case HazardFired e:
                        foreach (var h in s.Hazards)
                        {
                            if (h.Id == e.Hazard && h.Def == "wall_gun" && h.Tiles.Count > 0) Glow(Muzzle(h), HazardShot, 0.5f);
                        }
                        break;
                    case DroneDocked e:
                        Ring(Body(e.Pos), PlayerShot, 0.32f);
                        break;
                    case DroneRedeployed e:
                        Ring(Body(e.Pos), PlayerShot, 0.32f);
                        break;
                    case Reloaded e:
                    {
                        var p = UnitPos(e.Id);
                        if (p.HasValue) _tags.Float(Body(p.Value, 0.3f), "RELOAD", PlayerShot, 15, 0, 0f, scale);
                        break;
                    }
                    case Purged e:
                    {
                        var p = UnitPos(e.Id);
                        if (!p.HasValue) break;
                        Ring(Body(p.Value), Color.white, 0.5f);
                        _tags.Float(Body(p.Value, 0.3f), "PURGE", Color.white, 15, 0, 0f, scale);
                        break;
                    }
                }
            }
        }

        private static Vector3 Muzzle(Hazard h)
        {
            var t = h.Tiles[0];
            var d = h.Dir ?? Grid.W;
            return Iso.Tile(t.X + d.X * 0.62f, t.Y + d.Y * 0.62f, 0.33f);
        }

        private static Color32 Premul(Color c, float alpha, bool additive)
        {
            var a = Mathf.Clamp01(c.a * alpha);
            return new Color(c.r * a, c.g * a, c.b * a, additive ? 0f : a);
        }

        private void DiscAt(Vector3 at, float r, Color c, float alpha, bool additive, Vector3 right, Vector3 up)
        {
            _layer.B.Disc(at, r, right, up, Premul(c, alpha, additive), Premul(c, 0f, additive));
        }

        private void Polyline(Vector3 a, Vector3 b, int seed, float width, Color32 c, Vector3 forward, float amp, int steps)
        {
            var side = Vector3.Cross(b - a, forward).normalized;
            var prev = a;
            for (var i = 1; i <= steps; i++)
            {
                var t = i / (float)steps;
                var off = i == steps ? 0f : (Noise(seed, i, 9) - 0.5f) * 2f * amp;
                var next = Vector3.Lerp(a, b, t) + side * off;
                _layer.B.Strip(prev, next, width, side, c);
                prev = next;
            }
        }

        /// <summary>Hazard cues: a cue and nothing more, never a danger zone.</summary>
        private void DrawCues(GameState s, Vector3 right, Vector3 up, Vector3 forward)
        {
            foreach (var h in s.Hazards)
            {
                if (h.State != HazardState.Primed) continue;
                foreach (var t in h.Tiles)
                {
                    switch (h.Def)
                    {
                        case "gas_vent":
                            for (var i = 0; i < 3; i++)
                            {
                                var phase = Mathf.Repeat(_now / 1.8f + i / 3f, 1f);
                                DiscAt(Iso.Tile(t.X, t.Y, 0.08f + phase * 0.7f), 0.1f + phase * 0.14f, Palette.Poison, 0.7f * (1f - phase), false, right, up);
                            }
                            break;
                        case "wall_gun":
                        {
                            var pulse = 0.5f + 0.5f * Mathf.Sin(_now * 5.7f);
                            var m = Muzzle(h);
                            DiscAt(m, 0.3f, Palette.Hazard, 0.35f + 0.65f * pulse, true, right, up);
                            DiscAt(m, 0.05f + 0.04f * pulse, Palette.Hex(0xffe08a), 1f, false, right, up);
                            break;
                        }
                        default:
                            if (Mathf.Repeat(_now * 3.3f, 1f) < 0.62f)
                            {
                                var seed = Mathf.FloorToInt(_now * 9f) + t.X * 3 + t.Y * 5;
                                Polyline(Iso.Tile(t.X - 0.36f, t.Y, 0.06f), Iso.Tile(t.X + 0.36f, t.Y, 0.06f), seed, 0.035f, Premul(Palette.Target, 1f, true), forward, 0.12f, 6);
                            }
                            break;
                    }
                }
            }
        }

        public void Tick(float dt, GameState shown, IsoCamera cam)
        {
            _now += dt;
            var r = cam.Right;
            var u = cam.Up;
            var fwd = cam.Forward;
            var floorRight = Vector3.right;
            var floorUp = Vector3.forward;
            _layer.Begin();
            DrawCues(shown, r, u, fwd);
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                if (_now >= _items[i].Start + _items[i].Duration) _items.RemoveAt(i);
            }
            foreach (var f in _items)
            {
                if (_now < f.Start) continue;
                var t = Mathf.Clamp01((_now - f.Start) / f.Duration);
                switch (f.Kind)
                {
                    case Kind.Glow:
                        DiscAt(f.A, f.Size * (0.6f + 0.5f * t), f.Colour, (1f - t) * (1f - t), true, r, u);
                        break;
                    case Kind.Ring:
                    {
                        var radius = f.Size * (0.3f + 0.7f * t);
                        _layer.B.Ring(f.A, radius - f.Width, radius, r, u, Premul(f.Colour, 1f - t, true));
                        break;
                    }
                    case Kind.RingFloor:
                    {
                        var radius = f.Size * (0.3f + 0.7f * t);
                        _layer.B.Ring(f.A, radius - f.Width, radius, floorRight, floorUp, Premul(f.Colour, 1f - t, true));
                        break;
                    }
                    case Kind.Line:
                    {
                        var side = Vector3.Cross(f.B - f.A, fwd).normalized;
                        _layer.B.Strip(f.A, f.B, f.Width, side, Premul(f.Colour, 1f - t, true));
                        break;
                    }
                    case Kind.Tracer:
                    {
                        var length = (f.B - f.A).magnitude;
                        if (length < 0.001f) break;
                        var seg = Mathf.Min(f.Size, length);
                        var head = Mathf.Lerp(seg, length, t) / length;
                        var tail = Mathf.Max(0f, head - seg / length);
                        var side = Vector3.Cross(f.B - f.A, fwd).normalized;
                        _layer.B.Strip(Vector3.Lerp(f.A, f.B, tail), Vector3.Lerp(f.A, f.B, head), f.Width, side, Premul(f.Colour, 1f, true));
                        break;
                    }
                    case Kind.Blob:
                        DiscAt(Vector3.Lerp(f.A, f.B, t), f.Size, f.Colour, 1f, false, r, u);
                        break;
                    case Kind.Spark:
                    {
                        var ease = 1f - (1f - t) * (1f - t);
                        var off = r * f.B.x + u * f.B.y;
                        var head = f.A + off * ease;
                        var tail = f.A + off * Mathf.Max(0f, ease - 0.16f);
                        var side = Vector3.Cross(head - tail, fwd).normalized;
                        _layer.B.Strip(tail, head, 0.03f, side, Premul(f.Colour, 1f - t, true));
                        break;
                    }
                    case Kind.Bolt:
                        if (Mathf.Repeat(_now * 22f, 1f) < 0.72f)
                        {
                            Polyline(f.A, f.B, f.Seed + Mathf.FloorToInt(_now * 16f), f.Width * 2.4f, Premul(f.Colour, 0.45f * (1f - t), true), fwd, 0.1f, 6);
                            Polyline(f.A, f.B, f.Seed + Mathf.FloorToInt(_now * 16f), f.Width * 0.8f, Premul(Color.white, 1f - t, true), fwd, 0.1f, 6);
                        }
                        break;
                    case Kind.Boom:
                        DiscAt(f.A, f.Size * (0.4f + 0.8f * t), Color.Lerp(Color.white, f.Colour, Mathf.Min(1f, t * 3f)), (1f - t) * 1.1f, false, r, u);
                        break;
                    case Kind.Smoke:
                        DiscAt(f.A + Vector3.up * (t * 0.6f), f.Size * (0.6f + 0.8f * t), f.Colour, 0.6f * (1f - t), false, r, u);
                        break;
                    case Kind.Puff:
                        DiscAt(f.A + Vector3.up * (t * 0.3f), f.Size * (0.5f + 0.9f * t), f.Colour, Mathf.Sin(t * Mathf.PI), false, r, u);
                        break;
                    case Kind.FlashTile:
                    {
                        var c = Premul(f.Colour, 1f - t, true);
                        var a = f.A + new Vector3(-f.Size, 0f, -f.Size);
                        var b = f.A + new Vector3(-f.Size, 0f, f.Size);
                        var c2 = f.A + new Vector3(f.Size, 0f, f.Size);
                        var d = f.A + new Vector3(f.Size, 0f, -f.Size);
                        _layer.B.Quad(a, b, c2, d, c);
                        break;
                    }
                    case Kind.Flame:
                        for (var j = 0; j < 3; j++)
                        {
                            var o = Noise(f.Seed, j, 2) - 0.5f;
                            var rise = Mathf.Repeat(t * 1.4f + j / 3f, 1f);
                            DiscAt(f.A + r * (o * 0.45f) + Vector3.up * (rise * 0.75f), f.Size * (1f - 0.6f * rise), j == 0 ? Color.white : f.Colour, (1f - t) * (1f - rise * 0.6f), true, r, u);
                        }
                        break;
                    case Kind.Beam:
                    {
                        var w = r * (f.Width * 0.5f * (1f - t * 0.6f));
                        var top = f.A + Vector3.up * f.Size;
                        var inner = Premul(f.Colour, 1f - t, true);
                        var outer = Premul(f.Colour, 0f, true);
                        var b = _layer.B;
                        int v0 = b.Vert(f.A - w, Vector3.up, outer), v1 = b.Vert(top - w, Vector3.up, outer), v2 = b.Vert(top, Vector3.up, outer), v3 = b.Vert(f.A, Vector3.up, inner);
                        int v4 = b.Vert(top + w, Vector3.up, outer), v5 = b.Vert(f.A + w, Vector3.up, outer);
                        b.Tri(v0, v1, v2);
                        b.Tri(v0, v2, v3);
                        b.Tri(v3, v2, v4);
                        b.Tri(v3, v4, v5);
                        break;
                    }
                }
            }
            _layer.End();
        }
    }
}
