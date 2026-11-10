#nullable enable
using System.Collections.Generic;
using LabEscape.Core;
using UnityEngine;
using Grid = LabEscape.Core.Grid;

namespace LabEscape.Client
{
    /// <summary>
    /// Combat effects, drawn from the engine's events as their cues play. They read the events and never the rules. Every effect
    /// is a short-lived record whose shape is worked out from its age alone, and all of them are drawn into two meshes rebuilt
    /// each frame: one over everything, for light, and one on the floor, for dust, scorch marks and shockwaves. Sparks and debris
    /// fall and bounce, because nothing says weight like the things a blow knocks loose.
    /// </summary>
    public sealed class FxSystem
    {
        private enum Kind
        {
            Glow, Ring, RingFloor, Line, Tracer, Blob, Spark, Bolt, Boom, Smoke, Puff, FlashTile, Flame, Beam,
            /// <summary>A spark with weight: thrown, pulled down, and bouncing once.</summary>
            Ember,
            /// <summary>A solid piece of something, thrown the same way.</summary>
            Chunk,
            /// <summary>A glow that swells instead of fading: something charging.</summary>
            Charge,
            /// <summary>Light thrown on the room and the figures.</summary>
            Light,
            // On the floor:
            Dust, Scorch, Shock, Telegraph,
        }

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
        private static readonly Color DustColour = Palette.Hex(0xa9b4b8);
        private static readonly Color Rubble = Palette.Hex(0x39444a);

        private readonly List<Item> _items = new List<Item>();
        private readonly List<(Vector3 At, float Radius, Color Colour)> _lights = new List<(Vector3, float, Color)>();
        private readonly OverlayLayer _ground;
        private readonly OverlayLayer _layer;
        private readonly BoardTags _tags;
        private float _now;

        /// <summary>Where a unit's figure is now, so an effect on a unit lands on the figure and not on the tile it left.</summary>
        public System.Func<int, Vector3?>? UnitAt;

        public FxSystem(Transform parent, BoardTags tags)
        {
            _ground = new OverlayLayer("Floor effects", Mats.FloorOverlay, parent);
            _layer = new OverlayLayer("Effects", Mats.Fx, parent);
            _tags = tags;
        }

        private static Vector3 Body(Pos p, float dy = 0f) => Iso.Tile(p.X, p.Y, 0.42f + dy);

        private static Vector3 Feet(Pos p) => Iso.Tile(p.X, p.Y, 0.04f);

        /// <summary>A unit's chest, where its figure is; null when it has no figure.</summary>
        private Vector3? Chest(int id, float dy = 0f)
        {
            var at = UnitAt?.Invoke(id);
            return at.HasValue ? at.Value + Vector3.up * (0.42f + dy) : (Vector3?)null;
        }

        private static float Noise(int x, int y, int salt) => Feel.Noise(x, y, salt);

        private void Add(Kind kind, float duration, Vector3 a, Vector3 b, Color colour, float size = 0f, float width = 0f, float delay = 0f, int seed = 0)
        {
            _items.Add(new Item { Kind = kind, Start = _now + delay, Duration = duration, A = a, B = b, Colour = colour, Size = size, Width = width, Seed = seed });
        }

        /// <summary>Drops every effect in flight: the board has jumped to another state.</summary>
        public void Clear()
        {
            _items.Clear();
            Lighting.SetPoints(null);
        }

        private void Glow(Vector3 at, Color c, float r, float delay = 0f) => Add(Kind.Glow, 0.34f, at, at, c, r, 0f, delay);

        private void Ring(Vector3 at, Color c, float r, float w = 0.04f, float delay = 0f, float duration = 0.42f) => Add(Kind.Ring, duration, at, at, c, r, w, delay);

        private void Light(Vector3 at, Color c, float strength, float radius, float seconds, float delay = 0f) => Add(Kind.Light, seconds, at, at, c * strength, radius, 0f, delay);

        private void Sparks(Vector3 at, Color c, int n, float len, int seed, float delay = 0f)
        {
            for (var i = 0; i < n; i++)
            {
                var a = i * Mathf.PI * 2f / n + Noise(seed, i, 5) * 0.8f;
                var l = len * (0.6f + Noise(seed, i, 6) * 0.7f);
                Add(Kind.Spark, 0.42f, at, new Vector3(Mathf.Cos(a) * l, Mathf.Sin(a) * l, 0f), c, 0f, 0f, delay);
            }
        }

        /// <summary>Sparks knocked loose by a blow: thrown the way it went, then down to the floor.</summary>
        private void Embers(Vector3 at, Vector3 dir, Color c, int n, float speed, int seed, float delay = 0f)
        {
            var side = dir.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, dir) : Vector3.right;
            for (var i = 0; i < n; i++)
            {
                var a = Noise(seed, i, 11) * Mathf.PI * 2f;
                var spread = dir.sqrMagnitude > 0.0001f ? side * ((Noise(seed, i, 12) - 0.5f) * 1.5f) + dir * (0.5f + Noise(seed, i, 13)) : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.4f + 0.6f * Noise(seed, i, 13));
                var v = spread * speed + Vector3.up * (1.2f + 3.2f * Noise(seed, i, 14));
                Add(Kind.Ember, 0.5f + 0.35f * Noise(seed, i, 15), at, v, c, 0.028f, 0f, delay);
            }
        }

        /// <summary>Solid pieces thrown out from a point.</summary>
        private void Chunks(Vector3 at, Vector3 dir, Color c, int n, float speed, int seed, float delay = 0f)
        {
            for (var i = 0; i < n; i++)
            {
                var a = Noise(seed, i, 21) * Mathf.PI * 2f;
                var v = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (speed * (0.35f + 0.65f * Noise(seed, i, 22))) + dir * (speed * 0.6f) + Vector3.up * (2f + 3.5f * Noise(seed, i, 23));
                Add(Kind.Chunk, 0.75f + 0.4f * Noise(seed, i, 24), at, v, Palette.Shade(c, 0.6f + 0.4f * Noise(seed, i, 25)), 0.035f + 0.035f * Noise(seed, i, 26), 0f, delay);
            }
        }

        /// <summary>Dust thrown up by something landing: a ring of puffs along the floor, bigger the heavier it was.</summary>
        public void Dust(Vector3 at, float weight)
        {
            if (weight < 0.45f) return;
            at.y = 0.03f;
            var n = Mathf.Clamp(Mathf.RoundToInt(4f + weight * 3f), 5, 12);
            var seed = Mathf.RoundToInt(at.x * 31f + at.z * 17f);
            var reach = 0.22f + 0.16f * Mathf.Min(weight, 4f);
            for (var i = 0; i < n; i++)
            {
                var a = i * Mathf.PI * 2f / n + Noise(seed, i, 31) * 0.7f;
                var out2 = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (reach * (0.7f + 0.6f * Noise(seed, i, 32)));
                Add(Kind.Dust, 0.38f + 0.1f * Mathf.Min(weight, 3f), at, out2, DustColour, 0.07f + 0.035f * Mathf.Min(weight, 4f));
            }
            if (weight >= 1.5f) Add(Kind.Shock, 0.3f, at, at, Palette.WithAlpha(DustColour, 0.7f), 0.5f + 0.18f * Mathf.Min(weight, 5f), 0.07f);
        }

        private void Bolt(Vector3 a, Vector3 b, int seed, Color c) => Add(Kind.Bolt, 0.3f, a, b, c, 0f, 0.05f, 0f, seed);

        private static Color ShotColour(ProjectileFired e)
        {
            var src = e.Source;
            if (e.Reflected == true) return ParasiteShot;
            if (src.Via.Contains("acid") || src.Def == "spitter") return Acid;
            if (src.Via.Contains("parasite")) return ParasiteShot;
            if (src.Side == SourceSide.Hazard) return HazardShot;
            if (src.Side == SourceSide.Enemy) return EnemyShot;
            return src.Category == SourceCategory.Minion ? MinionShot : PlayerShot;
        }

        /// <summary>A shot leaves, flies for `flight` seconds and lands. What it does to its target is the damage cue's to show.</summary>
        private void Shot(ProjectileFired e, float flight, int seed)
        {
            var end = Shots.End(e);
            var from = Body(e.From);
            var to = Body(end);
            var c = ShotColour(e);
            switch (Shots.Style(e))
            {
                case ShotStyle.Lob:
                    // A glob with two smaller ones trailing it, on an arc.
                    for (var i = 0; i < 3; i++) Add(Kind.Blob, flight, from, to, c, 0.11f - i * 0.028f, 0.5f, i * 0.028f);
                    Glow(from, c, 0.22f);
                    Glow(to, c, 0.36f, flight);
                    Sparks(to, c, 7, 0.34f, seed, flight);
                    Light(to, c, 1.3f, 1.7f, 0.2f, flight);
                    return;
                case ShotStyle.Cable:
                    Add(Kind.Tracer, flight, from, to, Color.white, 0.5f, 0.05f);
                    Add(Kind.Line, flight + 0.3f, from, to, Palette.WithAlpha(c, 0.8f), 0f, 0.03f);
                    Ring(to, c, 0.22f, 0.03f, flight);
                    return;
            }
            // The flash at the muzzle, the round, the streak it leaves, and the flash where it lands.
            Glow(from, Color.white, 0.2f);
            Glow(from, c, 0.34f);
            Light(from, c, 1.5f, 1.9f, 0.1f);
            Add(Kind.Tracer, flight, from, to, Color.white, 0.55f, 0.05f);
            Add(Kind.Tracer, flight, from, to, c, 0.9f, 0.11f);
            Add(Kind.Line, flight + 0.2f, from, to, Palette.WithAlpha(c, 0.42f), 0f, 0.028f);
            Glow(to, Color.white, 0.26f, flight);
            Ring(to, c, 0.36f, 0.04f, flight, 0.14f);
            Light(to, c, 1.3f, 1.6f, 0.14f, flight);
        }

        private void Area(AreaKind kind, List<Pos> tiles, int seed)
        {
            if (tiles.Count == 0) return;
            var centre = Vector3.zero;
            foreach (var t in tiles) centre += Body(t);
            centre /= tiles.Count;
            var blast = kind == AreaKind.Blast || kind == AreaKind.Burst;
            for (var i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                var p = Body(t);
                // A blast spreads: the tiles further from its middle go a moment later.
                var reach = Feel.Flat(p - centre).magnitude * 0.03f;
                switch (kind)
                {
                    case AreaKind.Blast:
                    case AreaKind.Burst:
                    {
                        var c = kind == AreaKind.Burst && i % 2 == 1 ? Acid : Fire;
                        Add(Kind.Boom, 0.42f, p, p, c, 0.56f, 0f, reach);
                        if (i < 6) Add(Kind.Smoke, 1.1f, p, p, Smoke, 0.34f, 0f, 0.14f + i * 0.05f);
                        Add(Kind.Scorch, 7f, Iso.Tile(t.X, t.Y, 0.022f), Vector3.zero, Color.black, 0.46f, 0f, reach);
                        break;
                    }
                    case AreaKind.Spore:
                        for (var j = 0; j < 3; j++)
                        {
                            var o = new Vector3(Noise(t.X, t.Y, j) - 0.5f, 0.05f * j, Noise(t.Y, t.X, j) - 0.5f) * 0.7f;
                            Add(Kind.Puff, 0.7f, p + o, p + o, Palette.WithAlpha(Palette.Hex(0x7fd36a), 0.55f), 0.16f + j * 0.05f, 0f, reach + j * 0.08f);
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
                        Embers(Iso.Tile(t.X, t.Y, 0.1f), Vector3.zero, HazardShot, 5, 1.3f, seed + i * 5);
                        break;
                    case AreaKind.Sweep:
                        Add(Kind.Flame, 0.5f, Iso.Tile(t.X, t.Y, 0.08f), Vector3.zero, Fire, 0.3f, 0f, i * 0.06f, seed + i);
                        Add(Kind.Scorch, 5f, Iso.Tile(t.X, t.Y, 0.022f), Vector3.zero, Color.black, 0.36f, 0f, i * 0.06f);
                        break;
                }
            }
            var floor = new Vector3(centre.x, 0.03f, centre.z);
            if (blast)
            {
                var c = kind == AreaKind.Burst ? Acid : Fire;
                Glow(centre, Color.white, 0.95f);
                Ring(centre, c, 1.5f, 0.06f, 0f, 0.5f);
                Add(Kind.Shock, 0.42f, floor, floor, Palette.WithAlpha(Color.Lerp(c, Color.white, 0.5f), 0.95f), 2.1f, 0.16f);
                Embers(centre, Vector3.zero, SparkColour, 16, 3.4f, seed);
                Chunks(centre, Vector3.zero, Rubble, 9, 2.6f, seed + 1);
                Light(centre, c, 2.3f, 4.2f, 0.4f);
            }
            else if (kind == AreaKind.Sweep)
            {
                Glow(centre, Fire, 1f);
                Light(centre, Fire, 2.2f, 3.2f, 0.5f);
            }
            else if (kind == AreaKind.Panel) Light(centre, HazardShot, 2.4f, 3f, 0.3f);
        }

        /// <summary>A blow lands: its number, the flash, and what it knocks loose, thrown the way the blow went.</summary>
        private void Damage(Cue cue, DamageDealt e, int seed)
        {
            var at = (e.Target.Kind == EntityKind.Unit ? Chest(e.Target.Id) : null) ?? Body(e.Pos);
            var dir = Feel.From(cue, e.Pos);
            var text = e.Amount > 0 ? $"-{e.Amount}" : e.Absorbed > 0 ? "BLOCK" : "0";
            var colour = e.Amount > 0 ? Palette.Hex(0xff5a4a) : Palette.Hex(0xb9c4c9);
            var size = e.Amount >= 5 ? 38 : e.Amount >= 3 ? 32 : e.Amount > 0 ? 27 : 19;
            _tags.Float(at + Vector3.up * 0.3f, text, colour, e.Killed ? size + 4 : size, dir, e.Amount > 0);
            if (e.Amount <= 0)
            {
                if (e.Absorbed <= 0) return;
                // Armor took it all: a hard white ring and a few sparks glancing off.
                Ring(at, Palette.Hex(0xdfe8ec), 0.3f, 0.03f, 0f, 0.25f);
                Embers(at, -dir, Palette.Hex(0xdfe8ec), 4, 1.6f, seed);
                return;
            }
            var tint = e.TargetSide == Team.Enemy ? Fire : Palette.Danger;
            // One short burst of light. The figure flashes white and is thrown; a glow that lingered would smother both.
            Add(Kind.Glow, 0.12f, at, at, Color.Lerp(Color.white, tint, 0.3f), 0.26f + 0.04f * Mathf.Min(e.Amount, 6));
            Light(at, tint, 1.2f, 1.6f, 0.14f);
            if (dir.sqrMagnitude < 0.0001f) return;
            Embers(at, dir, e.TargetSide == Team.Enemy ? SparkColour : Palette.Hex(0xffb0a0), 4 + 2 * Mathf.Min(e.Amount, 6), 2.2f + 0.35f * Mathf.Min(e.Amount, 6), seed);
            if (e.Amount >= 3) Chunks(at, dir, Palette.UnitColor(e.TargetDef, e.TargetSide), Mathf.Min(e.Amount, 6) - 1, 1.8f, seed + 2);
        }

        /// <summary>Starts the effects for one cue. `s` is the state the frame ends on.</summary>
        public void Play(Cue cue, GameState s)
        {
            var seed = cue.Index * 7 + 3;
            if (cue.Kind == CueKind.Windup)
            {
                // Only the charge announces itself on the floor: a ring closing on the Warden as it rears back.
                var charge = cue.Event is Charged || (cue.Event is UnitMoved m && m.Mode == MoveMode.Charge);
                var feet = UnitAt?.Invoke(cue.Actor);
                if (!charge || !feet.HasValue) return;
                var at = new Vector3(feet.Value.x, 0.03f, feet.Value.z);
                Add(Kind.Telegraph, cue.Duration, at, at, Palette.Danger, 0.95f, 0.07f);
                Dust(at, 1.6f);
                return;
            }
            if (cue.Kind != CueKind.Event) return;
            switch (cue.Event)
            {
                case ProjectileFired e:
                    Shot(e, cue.Duration, seed);
                    break;
                case ChainArc e:
                    Bolt(Body(e.From), Body(e.To), seed, Palette.Hex(0xfff27a));
                    Glow(Body(e.To), Color.white, 0.26f, cue.Duration);
                    Light(Body(e.To), Palette.Hex(0xfff27a), 1.6f, 2f, 0.2f);
                    break;
                case AreaEffect e:
                    Area(e.Kind, e.Tiles, seed);
                    break;
                case DamageDealt e:
                    Damage(cue, e, seed);
                    break;
                case Healed e:
                {
                    var p = Chest(e.Target);
                    if (!p.HasValue) break;
                    _tags.Float(p.Value + Vector3.up * 0.3f, $"+{e.Amount}", Heal, 27, Vector3.zero, true);
                    Ring(p.Value, Heal, 0.46f);
                    Glow(p.Value, Heal, 0.4f);
                    Light(p.Value, Heal, 1.1f, 1.8f, 0.3f);
                    break;
                }
                case UnitDied e:
                {
                    var at = Body(e.Pos);
                    var big = e.Def == "warden" ? 1.5f : 1f;
                    var c = e.Def == "player" ? Palette.Danger : e.Team == Team.Enemy ? Fire : Figures.IsSmall(e.Def) ? Green : PlayerShot;
                    var dir = Feel.From(cue, e.Pos);
                    Embers(at, dir, c, 8, 2.6f, seed);
                    Chunks(at, dir, Palette.UnitColor(e.Def, e.Team), Figures.IsSmall(e.Def) ? 6 : 4, 2.2f, seed + 1);
                    Add(Kind.Smoke, 0.9f, at, at, Smoke, 0.3f * big, 0f, 0.3f);
                    break;
                }
                case Charged e:
                {
                    var from = Body(e.From);
                    var to = Body(e.To);
                    var floor = Iso.Tile(e.To.X + e.Dir.X * 0.5f, e.To.Y + e.Dir.Y * 0.5f, 0.03f);
                    Add(Kind.Line, 0.4f, from, to, Palette.WithAlpha(Palette.Danger, 0.4f), 0f, 0.3f);
                    Add(Kind.Line, 0.4f, from, to, Palette.Hex(0xffd0c0), 0f, 0.06f);
                    Ring(to, Palette.Hex(0xff6a4a), 0.7f, 0.07f, 0f, 0.5f);
                    Add(Kind.Shock, 0.4f, floor, floor, Palette.WithAlpha(Palette.Hex(0xffc8b0), 0.95f), 1.7f, 0.15f);
                    Embers(floor + Vector3.up * 0.4f, Iso.Tile(e.Dir.X, e.Dir.Y) - Iso.Tile(0f, 0f), SparkColour, 12, 3.2f, seed);
                    Chunks(floor, Vector3.zero, Rubble, 7, 2.4f, seed + 1);
                    Light(to, Palette.Danger, 2.4f, 3.2f, 0.3f);
                    break;
                }
                case UnitSpawned e:
                {
                    var p = e.Unit.Pos;
                    if (e.Cause.StartsWith("wave", System.StringComparison.Ordinal))
                    {
                        // The floor shows where it will land, a streak follows it down, and the room flashes red as it hits.
                        Add(Kind.Telegraph, cue.Duration, Feet(p), Feet(p), Palette.Danger, 0.72f, 0.05f);
                        Add(Kind.Beam, cue.Duration + 0.2f, Feet(p), Feet(p), Palette.Danger, Feel.DropHeight, 0.34f);
                        Light(Body(p), Palette.Danger, 1.8f, 2.4f, 0.3f, cue.Duration);
                    }
                    else if (e.Cause == "egg" || e.Cause == "parasite")
                    {
                        Glow(Body(p), Green, 0.44f);
                        Embers(Body(p), Vector3.zero, Green, 8, 1.6f, seed);
                    }
                    else
                    {
                        Add(Kind.Beam, 0.5f, Feet(p), Feet(p), PlayerShot, 1.1f, 0.6f);
                        Add(Kind.RingFloor, 0.45f, Feet(p), Feet(p), PlayerShot, 0.5f, 0.04f);
                        Light(Body(p), PlayerShot, 1.2f, 2f, 0.3f);
                    }
                    break;
                }
                case ObjectPlaced e:
                    if (e.Object.Kind != ObjectKind.Corpse) Add(Kind.RingFloor, 0.4f, Feet(e.Object.Pos), Feet(e.Object.Pos), e.Object.Kind == ObjectKind.Egg ? Green : PlayerShot, 0.46f, 0.04f, cue.Duration);
                    break;
                case ObjectDestroyed e:
                    Embers(Body(e.Pos), Feel.From(cue, e.Pos), Palette.Hex(0xc8d0d4), 8, 2.2f, seed);
                    Ring(Body(e.Pos), Palette.Hex(0xc8d0d4), 0.4f);
                    break;
                case ObjectTriggered e:
                    // Three red blinks, each brighter, while the fuse runs.
                    for (var i = 0; i < 3; i++)
                    {
                        Glow(Body(e.Pos, -0.2f), Palette.Danger, 0.26f + 0.1f * i, cue.Duration * i / 3f);
                        Light(Body(e.Pos), Palette.Danger, 0.9f + 0.5f * i, 1.6f, 0.12f, cue.Duration * i / 3f);
                    }
                    break;
                case EggHatched e:
                    Glow(Body(e.Pos), Green, 0.5f);
                    Chunks(Body(e.Pos), Vector3.zero, Palette.Hex(0x4fc890), 7, 1.9f, seed);
                    break;
                case StatusApplied e:
                {
                    var p = Chest(e.Target, 0.5f);
                    if (p.HasValue) _tags.Float(p.Value, Names.Of(e.Status).ToUpperInvariant(), Palette.StatusColor(e.Status), 16, Vector3.zero, false);
                    break;
                }
                case HazardFired e:
                    foreach (var h in s.Hazards)
                    {
                        if (h.Id != e.Hazard || h.Tiles.Count == 0) continue;
                        if (h.Def == "wall_gun")
                        {
                            Add(Kind.Charge, cue.Duration, Muzzle(h), Muzzle(h), HazardShot, 0.6f);
                            Light(Muzzle(h), HazardShot, 2f, 2.6f, 0.2f, cue.Duration);
                        }
                        else
                        {
                            foreach (var t in h.Tiles) Add(Kind.Charge, cue.Duration, Iso.Tile(t.X, t.Y, 0.12f), Vector3.zero, h.Def == "gas_vent" ? Palette.Poison : HazardShot, 0.5f);
                        }
                    }
                    break;
                case HazardPrimed e:
                    // The tell: the hazard wakes, a round before it fires.
                    foreach (var h in s.Hazards)
                    {
                        if (h.Id != e.Hazard) continue;
                        foreach (var t in h.Tiles)
                        {
                            var at = h.Def == "wall_gun" ? Muzzle(h) : Iso.Tile(t.X, t.Y, 0.06f);
                            Add(Kind.RingFloor, 0.5f, Feet(t), Feet(t), Palette.Hazard, 0.62f, 0.05f);
                            Glow(at, Palette.Hazard, 0.4f);
                            Light(at, Palette.Hazard, 1.3f, 2.2f, 0.4f);
                        }
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
                    var p = Chest(e.Id, 0.3f);
                    if (p.HasValue) _tags.Float(p.Value, "RELOAD", PlayerShot, 16, Vector3.zero, false);
                    break;
                }
                case Purged e:
                {
                    var p = Chest(e.Id);
                    if (!p.HasValue) break;
                    Ring(p.Value, Color.white, 0.6f, 0.05f);
                    Light(p.Value, Color.white, 1.4f, 2.4f, 0.3f);
                    _tags.Float(p.Value + Vector3.up * 0.3f, "PURGE", Color.white, 16, Vector3.zero, false);
                    break;
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

        /// <summary>Where something thrown is after `age` seconds: it falls, bounces once off the floor and lies still.</summary>
        private static Vector3 Thrown(Vector3 from, Vector3 v, float age)
        {
            const float g = Feel.Gravity;
            var fall = Mathf.Sqrt(v.y * v.y + 2f * g * Mathf.Max(0f, from.y));
            var landAt = (v.y + fall) / g;
            if (age <= landAt) return from + v * age + Vector3.down * (0.5f * g * age * age);
            var flat = new Vector3(v.x, 0f, v.z);
            var up = fall * 0.34f;
            var since = Mathf.Min(age - landAt, 2f * up / g);
            return new Vector3(from.x, 0f, from.z) + flat * landAt + flat * (0.45f * since) + Vector3.up * Mathf.Max(0f, up * since - 0.5f * g * since * since);
        }

        private static Color32 Tint(Color c, float alpha) => new Color(c.r, c.g, c.b, Mathf.Clamp01(c.a * alpha));

        public void Tick(float dt, GameState shown, IsoCamera cam)
        {
            _now += dt;
            var r = cam.Right;
            var u = cam.Up;
            var fwd = cam.Forward;
            var floorRight = Vector3.right;
            var floorUp = Vector3.forward;
            _ground.Begin();
            _layer.Begin();
            _lights.Clear();
            DrawCues(shown, r, u, fwd);
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                if (_now >= _items[i].Start + _items[i].Duration) _items.RemoveAt(i);
            }
            foreach (var f in _items)
            {
                if (_now < f.Start) continue;
                var age = _now - f.Start;
                var t = Mathf.Clamp01(age / f.Duration);
                switch (f.Kind)
                {
                    case Kind.Glow:
                        DiscAt(f.A, f.Size * (0.6f + 0.5f * t), f.Colour, (1f - t) * (1f - t), true, r, u);
                        break;
                    case Kind.Charge:
                        DiscAt(f.A, f.Size * (0.2f + 0.8f * t), f.Colour, t * t, true, r, u);
                        DiscAt(f.A, f.Size * 0.25f * t, Color.white, t, true, r, u);
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
                        // Width is how high the arc rises.
                        DiscAt(Vector3.Lerp(f.A, f.B, t) + Vector3.up * (f.Width * 4f * t * (1f - t)), f.Size, f.Colour, 1f, false, r, u);
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
                    case Kind.Ember:
                    {
                        var head = Thrown(f.A, f.B, age);
                        var tail = Thrown(f.A, f.B, Mathf.Max(0f, age - 0.035f));
                        var fade = 1f - t * t;
                        var streak = head - tail;
                        if (streak.sqrMagnitude > 0.00002f) _layer.B.Strip(tail, head, f.Size * 1.3f, Vector3.Cross(streak, fwd).normalized, Premul(f.Colour, fade, true));
                        DiscAt(head, f.Size, Color.Lerp(Color.white, f.Colour, t), fade, true, r, u);
                        break;
                    }
                    case Kind.Chunk:
                        _layer.B.Disc(Thrown(f.A, f.B, age) + Vector3.up * f.Size, f.Size, r, u, Premul(f.Colour, t < 0.7f ? 1f : (1f - t) / 0.3f, false), Premul(Palette.Shade(f.Colour, 0.6f), t < 0.7f ? 1f : (1f - t) / 0.3f, false), 5);
                        break;
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
                        DiscAt(f.A + Vector3.up * (t * 0.75f), f.Size * (0.6f + 0.9f * t), f.Colour, 0.6f * (1f - t), false, r, u);
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
                    case Kind.Light:
                        _lights.Add((f.A, f.Size, f.Colour * ((1f - t) * (1f - t))));
                        break;
                    case Kind.Dust:
                    {
                        // Thrown out fast along the floor, then slowing as it spreads and thins.
                        var ease = 1f - (1f - t) * (1f - t);
                        _ground.B.Disc(f.A + f.B * ease, f.Size * (0.6f + 1.1f * t), floorRight, floorUp, Tint(f.Colour, 0.5f * (1f - t)), Tint(f.Colour, 0f), 10);
                        break;
                    }
                    case Kind.Scorch:
                    {
                        var fade = t < 0.6f ? 1f : (1f - t) / 0.4f;
                        _ground.B.Disc(f.A, f.Size, floorRight, floorUp, Tint(f.Colour, 0.62f * fade), Tint(f.Colour, 0f), 14);
                        break;
                    }
                    case Kind.Shock:
                    {
                        var ease = 1f - (1f - t) * (1f - t) * (1f - t);
                        var radius = f.Size * (0.15f + 0.85f * ease);
                        _ground.B.Ring(f.A, Mathf.Max(0f, radius - f.Width * (1f - t)), radius, floorRight, floorUp, Tint(f.Colour, 1f - t), 36);
                        break;
                    }
                    case Kind.Telegraph:
                    {
                        var radius = f.Size * Mathf.Lerp(1f, 0.45f, t);
                        _ground.B.Ring(f.A, radius - f.Width, radius, floorRight, floorUp, Tint(f.Colour, 0.25f + 0.75f * t), 32);
                        break;
                    }
                }
            }
            _ground.End();
            _layer.End();
            // The shader takes four lights: keep the brightest.
            _lights.Sort((a, b) => (b.Colour.r + b.Colour.g + b.Colour.b).CompareTo(a.Colour.r + a.Colour.g + a.Colour.b));
            Lighting.SetPoints(_lights);
        }
    }
}
