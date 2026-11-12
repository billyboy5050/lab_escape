#nullable enable
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace LabEscape.Client
{
    /// <summary>Small helpers for building interface elements from code.</summary>
    public static class Ui
    {
        public static VisualElement El(string classes, bool pick = false)
        {
            var e = new VisualElement();
            AddClasses(e, classes);
            e.pickingMode = pick ? PickingMode.Position : PickingMode.Ignore;
            return e;
        }

        public static Label Text(string text, string classes, bool pick = false)
        {
            var l = new Label(text);
            AddClasses(l, classes);
            l.pickingMode = pick ? PickingMode.Position : PickingMode.Ignore;
            return l;
        }

        private static void AddClasses(VisualElement e, string classes)
        {
            foreach (var c in classes.Split(' '))
            {
                if (c.Length > 0) e.AddToClassList(c);
            }
        }

        public static void Show(VisualElement e, bool on) => e.EnableInClassList("hidden", !on);
    }

    /// <summary>
    /// Everything anchored to a place on the board: HP bars, armor, ammo and status chips, preview badges, incoming-damage tags and
    /// damage numbers. They live on the interface layer, so nothing standing in front of a unit can hide them.
    /// </summary>
    public sealed class BoardTags
    {
        private sealed class UnitTag
        {
            public readonly VisualElement Feet = Ui.El("anchor");
            public readonly VisualElement Head = Ui.El("anchor");
            private readonly VisualElement _bar = Ui.El("hp-bar");
            private readonly Label _hp = Ui.Text("", "hp-number");
            private readonly VisualElement _row = Ui.El("head-row");
            private string _sig = "";

            public UnitTag(VisualElement layer)
            {
                var feet = Ui.El("feet-row");
                feet.Add(_bar);
                feet.Add(_hp);
                Feet.Add(feet);
                Head.Add(_row);
                layer.Add(Feet);
                layer.Add(Head);
            }

            public void Remove()
            {
                Feet.RemoveFromHierarchy();
                Head.RemoveFromHierarchy();
            }

            /// <summary>`hp` is the HP on show; `ghost` is the HP before the last blow while that still shows, else the same.</summary>
            public void Update(Content c, Unit u, int hp, int ghost)
            {
                var st = u.Statuses;
                var armor = Mathf.Max(0, u.Armor - (st.Corrode?.Stacks ?? 0));
                var def = c.Units[u.Def];
                var sig = $"{hp}/{ghost}/{u.MaxHp}/{armor}/{u.Ammo}/{u.MaxAmmo}/{u.Charges}/{u.MaxCharges}/{u.DroneState}/{st.Poison?.Remaining}/{st.Parasite?.Remaining}/{st.Corrode?.Stacks}/{st.Pinned != null}/{st.Slowed != null}";
                if (sig == _sig) return;
                _sig = sig;

                _bar.Clear();
                var low = hp <= u.MaxHp / 3f;
                var shown = Mathf.Max(0, hp);
                var lost = Mathf.Max(shown, Mathf.Min(u.MaxHp, ghost));
                if (u.MaxHp <= 10)
                {
                    for (var i = 0; i < u.MaxHp; i++) _bar.Add(Ui.El($"hp-seg{(i < shown ? " full" : i < lost ? " ghost" : "")}{(low ? " low" : "")}"));
                }
                else
                {
                    var fill = Ui.El($"hp-fill{(low ? " low" : "")}");
                    fill.style.width = Length.Percent(100f * shown / u.MaxHp);
                    _bar.Add(fill);
                    if (lost > shown)
                    {
                        var chunk = Ui.El("hp-fill ghost");
                        chunk.style.width = Length.Percent(100f * (lost - shown) / u.MaxHp);
                        _bar.Add(chunk);
                    }
                }
                _hp.text = hp.ToString();

                _row.Clear();
                if (u.MaxAmmo.HasValue) _row.Add(Pips(u.MaxAmmo.Value, u.Ammo ?? 0, "ammo"));
                if (u.MaxCharges.HasValue) _row.Add(Pips(u.MaxCharges.Value, u.Charges ?? 0, "charge"));
                if (u.DroneState == DroneState.Returning) _row.Add(Ui.Text("RTN", "tag-chip"));
                if (def.Armor > 0 || armor > 0) _row.Add(Ui.Text($"ARM {armor}", "tag-chip"));
                if (st.Poison != null) _row.Add(Status($"PSN {st.Poison.Remaining}", StatusId.Poison));
                if (st.Parasite != null) _row.Add(Status($"PAR {st.Parasite.Remaining}", StatusId.Parasite));
                if (st.Corrode != null) _row.Add(Status($"COR {st.Corrode.Stacks}", StatusId.Corrode));
                if (st.Pinned != null) _row.Add(Status("PIN", StatusId.Pinned));
                if (st.Slowed != null) _row.Add(Status("SLW", StatusId.Slowed));
            }

            private static VisualElement Pips(int max, int n, string kind)
            {
                var row = Ui.El("tag-pips");
                for (var i = 0; i < max; i++) row.Add(Ui.El(i < n ? $"tag-pip {kind}" : "tag-pip"));
                return row;
            }

            private static Label Status(string text, StatusId id)
            {
                var l = Ui.Text(text, "tag-chip");
                var col = Palette.StatusColor(id);
                l.style.color = col;
                l.style.borderTopColor = l.style.borderBottomColor = l.style.borderLeftColor = l.style.borderRightColor = col;
                return l;
            }
        }

        private sealed class ObjectTag
        {
            public readonly VisualElement Anchor = Ui.El("anchor");
            private readonly VisualElement _row = Ui.El("head-row");
            private string _sig = "";

            public ObjectTag(VisualElement layer)
            {
                Anchor.Add(_row);
                layer.Add(Anchor);
            }

            public void Update(BoardObject o, int? hp)
            {
                var sig = $"{o.Kind}/{o.Timer}/{hp}/{o.Decay}";
                if (sig == _sig) return;
                _sig = sig;
                _row.Clear();
                switch (o.Kind)
                {
                    case ObjectKind.Egg:
                        _row.Add(Ui.Text($"T{o.Timer}", "tag-chip"));
                        _row.Add(Ui.Text($"{hp} HP", "tag-chip"));
                        break;
                    case ObjectKind.Corpse:
                        _row.Add(Ui.Text($"{o.Decay}", "tag-chip"));
                        break;
                    case ObjectKind.Shield:
                        _row.Add(Ui.Text($"{hp} HP", "tag-chip"));
                        break;
                }
            }
        }

        private sealed class FloatingText
        {
            public Label Label = null!;
            public Vector3 World;
            public float Start;
            public float Duration;
            /// <summary>Pixels a second across the screen, and whether it is thrown up to fall back or simply rises.</summary>
            public float Drift;
            public bool Thrown;
        }

        private readonly VisualElement _layer;
        private readonly Content _c;
        private readonly IsoCamera _cam;
        private readonly Dictionary<int, UnitTag> _units = new Dictionary<int, UnitTag>();
        private readonly Dictionary<int, ObjectTag> _objects = new Dictionary<int, ObjectTag>();
        private readonly List<(VisualElement El, Vector3 World)> _chips = new List<(VisualElement, Vector3)>();
        private readonly List<FloatingText> _floats = new List<FloatingText>();
        private readonly List<(VisualElement El, Vector3 World)> _coords = new List<(VisualElement, Vector3)>();
        private object? _chipsFor;
        private bool _chipsShowIntents;
        private float _now;

        public BoardTags(VisualElement layer, Content c, IsoCamera cam)
        {
            _layer = layer;
            _c = c;
            _cam = cam;
        }

        public void Clear()
        {
            foreach (var t in _units.Values) t.Remove();
            _units.Clear();
            foreach (var t in _objects.Values) t.Anchor.RemoveFromHierarchy();
            _objects.Clear();
            ClearChips();
            foreach (var f in _floats) f.Label.RemoveFromHierarchy();
            _floats.Clear();
            foreach (var (el, _) in _coords) el.RemoveFromHierarchy();
            _coords.Clear();
        }

        /// <summary>
        /// The board's coordinates, as the log and previews name tiles (`Tiles.Name`): columns from A along the near-left edge and
        /// rows from 1 along the near-right edge, just off the slab.
        /// </summary>
        public void Coordinates(Board board)
        {
            foreach (var (el, _) in _coords) el.RemoveFromHierarchy();
            _coords.Clear();
            for (var x = 0; x < board.Width; x++) AddCoord(((char)('A' + x)).ToString(), Iso.Floor(x + 0.5f, board.Height + RoomView.CoordOut, RoomView.CoordHeight));
            for (var y = 0; y < board.Height; y++) AddCoord((y + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Iso.Floor(board.Width + RoomView.CoordOut, y + 0.5f, RoomView.CoordHeight));
        }

        private void AddCoord(string text, Vector3 world)
        {
            var anchor = Ui.El("anchor");
            anchor.Add(Ui.Text(text, "coord display"));
            // Behind every other tag, so a unit's bar on the edge row is never covered by a letter.
            _layer.Insert(0, anchor);
            _coords.Add((anchor, world));
        }

        private void ClearChips()
        {
            foreach (var (el, _) in _chips) el.RemoveFromHierarchy();
            _chips.Clear();
        }

        /// <summary>
        /// Starts a number or word at a place on the board. A `thrown` one, a damage number, lands big, shrinks to its size, and
        /// is thrown the way the blow went to fall back under its own weight; the rest rise and fade.
        /// </summary>
        public void Float(Vector3 world, string text, Color colour, int size, Vector3 dir, bool thrown)
        {
            var l = Ui.Text(text, "float-text");
            l.style.color = colour;
            l.style.fontSize = size;
            l.style.opacity = 0f;
            _layer.Add(l);
            // Numbers on one unit in quick succession fan out instead of stacking.
            var fan = (_floats.Count % 3 - 1) * 26f;
            var across = Vector3.Dot(dir, _cam.Right);
            _floats.Add(new FloatingText { Label = l, World = world, Start = _now, Duration = thrown ? 0.95f : 0.85f, Drift = across * 95f + fan, Thrown = thrown });
        }

        private Vector2 ToPanel(Vector3 world) => RuntimePanelUtils.CameraTransformWorldToPanel(_layer.panel, world, _cam.Cam);

        private static void Place(VisualElement e, Vector2 at) => e.style.translate = new Translate(at.x, at.y);

        public void Tick(float dt, GameState s, BoardView board, BoardOverlay o)
        {
            _now += dt;
            if (_layer.panel == null) return;

            // Tags follow the figures on the board, not the state: a unit keeps its bar until the blow that kills it has landed.
            var seen = new HashSet<int>();
            foreach (var pair in board.Units)
            {
                var v = pair.Value;
                if (!v.Arrived) continue;
                seen.Add(pair.Key);
                if (!_units.TryGetValue(pair.Key, out var tag))
                {
                    tag = new UnitTag(_layer);
                    _units[pair.Key] = tag;
                }
                var u = v.Data;
                tag.Update(_c, u, v.ShownHp, v.GhostLeft > 0f ? v.GhostHp : v.ShownHp);
                // A blow rattles the bar as well as the body.
                var rattle = v.Jolt > 0f ? new Vector2(Mathf.Sin(_now * 71f), Mathf.Cos(_now * 53f)) * (5f * v.Jolt * v.Jolt) : Vector2.zero;
                Place(tag.Feet, ToPanel(v.Position) + rattle);
                var height = u.Def == "warden" ? 1.05f : Figures.IsSmall(u.Def) ? 0.55f : u.Flying ? 0.78f : 0.82f;
                Place(tag.Head, ToPanel(v.Position + Vector3.up * height) + rattle);
            }
            foreach (var id in _units.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _units[id].Remove();
                _units.Remove(id);
            }

            seen.Clear();
            foreach (var pair in board.Objects)
            {
                var x = pair.Value.Data;
                if (x.Kind == ObjectKind.Mine || !pair.Value.Arrived) continue;
                seen.Add(pair.Key);
                if (!_objects.TryGetValue(pair.Key, out var tag))
                {
                    tag = new ObjectTag(_layer);
                    _objects[pair.Key] = tag;
                }
                tag.Update(x, pair.Value.ShownHp);
                Place(tag.Anchor, ToPanel(Iso.Tile(x.Pos.X, x.Pos.Y, x.Kind == ObjectKind.Corpse ? 0.15f : 0.68f)));
            }
            foreach (var id in _objects.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _objects[id].Anchor.RemoveFromHierarchy();
                _objects.Remove(id);
            }

            foreach (var (el, world) in _coords) Place(el, ToPanel(world));
            RebuildChips(s, o);
            foreach (var (el, world) in _chips) Place(el, ToPanel(world));

            for (var i = _floats.Count - 1; i >= 0; i--)
            {
                var f = _floats[i];
                var age = _now - f.Start;
                var t = age / f.Duration;
                if (t >= 1f)
                {
                    f.Label.RemoveFromHierarchy();
                    _floats.RemoveAt(i);
                    continue;
                }
                var p = ToPanel(f.World);
                var w = f.Label.resolvedStyle.width;
                var h = f.Label.resolvedStyle.height;
                var w2 = float.IsNaN(w) ? 0f : w * 0.5f;
                var h2 = float.IsNaN(h) ? 0f : h * 0.5f;
                // Thrown up at 150 pixels a second and pulled back at 330 a second each second; the others rise evenly.
                var rise = f.Thrown ? 150f * age - 165f * age * age : 46f * t;
                Place(f.Label, new Vector2(p.x - w2 + f.Drift * age, p.y - h2 - rise));
                f.Label.style.opacity = t < 0.72f ? 1f : (1f - t) / 0.28f;
                if (!f.Thrown) continue;
                // It lands at nearly twice its size and settles in a tenth of a second.
                var land = Mathf.Clamp01(age / 0.11f);
                var scale = Mathf.Lerp(1.9f, 1f, 1f - (1f - land) * (1f - land));
                f.Label.style.scale = new Scale(new Vector3(scale, scale, 1f));
            }
        }

        private void RebuildChips(GameState s, BoardOverlay o)
        {
            var pv = o.Preview != null && o.Preview.Ok ? o.Preview : null;
            object? source = (object?)pv ?? o.Intents;
            if (ReferenceEquals(source, _chipsFor) && _chipsShowIntents == o.ShowIntents) return;
            _chipsFor = source;
            _chipsShowIntents = o.ShowIntents;
            ClearChips();
            if (o.ShowIntents)
            {
                if (pv == null && o.Intents != null) AddIntentChips(o.Intents, s, false);
                else if (pv?.Intents != null) AddIntentChips(pv.Intents, pv.State ?? s, true);
            }
            if (pv != null) AddHitBadges(pv);
        }

        private void AddChip(VisualElement child, Vector3 world)
        {
            var anchor = Ui.El("anchor");
            anchor.Add(child);
            _layer.Add(anchor);
            _chips.Add((anchor, world));
        }

        /// <summary>The damage each unit takes this round if the turn ends now, and a cross on any unit that dies before it acts.</summary>
        private void AddIntentChips(IntentReport rep, GameState s, bool faint)
        {
            var incoming = new OrderedMap<string, (Pos Pos, int Damage, bool Killed, EntityRef Ref)>(System.StringComparer.Ordinal);
            foreach (var it in rep.Intents)
            {
                foreach (var h in it.Hits)
                {
                    if (h.Damage <= 0 && !h.Killed) continue;
                    var key = h.Ref.ToString();
                    var cur = incoming.TryGetValue(key, out var v) ? v : (h.Pos, 0, false, h.Ref);
                    incoming[key] = (cur.Item1, cur.Item2 + h.Damage, cur.Item3 || h.Killed, h.Ref);
                }
            }
            foreach (var pair in incoming)
            {
                var v = pair.Value;
                var unit = v.Ref.Kind == EntityKind.Unit ? s.Units.FirstOrDefault(u => u.Id == v.Ref.Id) : null;
                var pos = unit?.Pos ?? v.Pos;
                var label = Ui.Text($"▼{v.Damage}{(v.Killed ? " KO" : "")}", faint ? "incoming-chip faint" : "incoming-chip");
                AddChip(label, Iso.Tile(pos.X, pos.Y, 0.6f));
            }
            foreach (var it in rep.Intents.Where(i => i.DiesFirst))
            {
                AddChip(Ui.Text("✕", "dies-chip"), Iso.Tile(it.From.X, it.From.Y, 0.6f));
            }
        }

        private void AddHitBadges(CommandPreview pv)
        {
            foreach (var h in pv.Hits)
            {
                var row = Ui.El("badge-row");
                if (h.Friendly) row.Add(Ui.Text("!", "badge friendly"));
                if (h.Damage > 0 || h.Absorbed > 0) row.Add(Ui.Text(h.Damage > 0 ? $"-{h.Damage}" : "0", h.Damage > 0 ? "badge damage" : "badge none"));
                if (h.Healed > 0) row.Add(Ui.Text($"+{h.Healed}", "badge heal"));
                foreach (var st in h.Statuses) row.Add(Ui.Text(st == StatusId.Poison ? "+PSN" : st == StatusId.Parasite ? "+PAR" : st == StatusId.Corrode ? "+COR" : st == StatusId.Pinned ? "PIN" : "SLW", "badge status"));
                if (h.Killed) row.Add(Ui.Text("KO", "badge ko"));
                if (row.childCount == 0) continue;
                var pos = h.Ref.Kind == EntityKind.Unit ? pv.State?.Units.FirstOrDefault(u => u.Id == h.Ref.Id)?.Pos ?? h.Pos : h.Pos;
                AddChip(row, Iso.Tile(pos.X, pos.Y, 1.02f));
            }
        }
    }
}
