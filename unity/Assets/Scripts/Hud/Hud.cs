#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace LabEscape.Client
{
    /// <summary>A line in the scanner panel.</summary>
    public readonly struct InfoLine
    {
        public readonly string Text;
        public readonly string Style;

        public InfoLine(string text, string style = "")
        {
            Text = text;
            Style = style;
        }
    }

    /// <summary>The interface around the board. The layout and styling are in UI/Fight.uxml and UI/Fight.uss; this fills them in.</summary>
    public sealed class Hud
    {
        /// <summary>Keys for the action bar, in bar order. A full loadout plus the four utilities is 12 actions.</summary>
        public static readonly string[] ActionKeyLabels = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };

        private readonly VisualElement _root;
        private readonly VisualElement _boardArea;
        private readonly VisualElement _hpCells, _hpPills, _apCells, _moveCells, _actionRow, _infoBody, _minions, _start, _presets, _end, _logModal;
        private readonly VisualElement _alarm, _hurt;
        private readonly Label _hpText, _round, _phase, _incoming, _hint, _intercom, _infoTitle, _minionCount, _banner, _alert, _toast, _endTitle, _endCause, _startTitle, _startError;
        private readonly Button _undo, _endTurn, _skip, _intents, _speed0, _speed1, _speed2;
        private readonly ScrollView _log, _logLines;
        private string _statusSig = "", _actionsSig = "", _infoSig = "", _minionSig = "", _intercomText = "", _bannerText = "";
        private int _logShown;
        private float _typed;
        private float _alertLeft, _toastLeft;

        public readonly VisualElement TagLayer;

        public event Action<string>? ActionClicked;
        public event Action<string>? GrappleModeClicked;
        public event Action? UndoClicked, EndTurnClicked, SkipClicked, IntentsClicked, OpenLogClicked, CloseLogClicked, AgainClicked, ChangeClicked;
        public event Action<FightSpeed>? SpeedClicked;
        public event Action<PresetDef>? PresetClicked;

        public Hud(UIDocument doc)
        {
            _root = doc.rootVisualElement;
            T Q<T>(string name) where T : VisualElement => _root.Q<T>(name) ?? throw new InvalidOperationException($"Fight.uxml has no {name}");
            VisualElement E(string name) => Q<VisualElement>(name);
            Label L(string name) => Q<Label>(name);
            Button B(string name, Action act)
            {
                var b = Q<Button>(name);
                b.focusable = false;
                b.clicked += act;
                return b;
            }

            TagLayer = E("tags");
            _boardArea = E("board-area");
            _hpCells = E("hp-cells");
            _hpPills = E("hp-pills");
            _apCells = E("ap-cells");
            _moveCells = E("move-cells");
            _actionRow = E("action-row");
            _infoBody = E("info-body");
            _minions = E("minions");
            _start = E("start");
            _presets = E("presets");
            _end = E("end");
            _logModal = E("log-modal");
            _alarm = E("flash-alarm");
            _hurt = E("flash-hurt");
            _hpText = L("hp-text");
            _round = L("round");
            _phase = L("phase");
            _incoming = L("incoming");
            _hint = L("hint");
            _intercom = L("intercom");
            _infoTitle = L("info-title");
            _minionCount = L("minion-count");
            _banner = L("banner");
            _alert = L("alert");
            _toast = L("toast");
            _endTitle = L("end-title");
            _endCause = L("end-cause");
            _startTitle = L("start-title");
            _startError = L("start-error");
            _log = Q<ScrollView>("log");
            _logLines = Q<ScrollView>("log-lines");
            _log.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _log.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            _undo = B("undo", () => UndoClicked?.Invoke());
            _endTurn = B("end-turn", () => EndTurnClicked?.Invoke());
            _skip = B("skip", () => SkipClicked?.Invoke());
            _intents = B("intents", () => IntentsClicked?.Invoke());
            _speed1 = B("speed-1", () => SpeedClicked?.Invoke(FightSpeed.Normal));
            _speed2 = B("speed-2", () => SpeedClicked?.Invoke(FightSpeed.Fast));
            _speed0 = B("speed-0", () => SpeedClicked?.Invoke(FightSpeed.Skip));
            B("open-log", () => OpenLogClicked?.Invoke());
            B("close-log", () => CloseLogClicked?.Invoke());
            B("again", () => AgainClicked?.Invoke());
            B("change", () => ChangeClicked?.Invoke());
        }

        public bool PointerOverUi(Vector2 screen)
        {
            var panel = _root.panel;
            if (panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            return panel.Pick(p) != null;
        }

        /// <summary>The part of the screen not covered by panels, in pixels from the bottom left.</summary>
        public Rect FreeScreenArea()
        {
            var layout = _root.panel?.visualTree.layout ?? default;
            if (layout.width <= 0f) return new Rect(0, 0, Screen.width, Screen.height);
            var k = Screen.width / layout.width;
            var b = _boardArea.worldBound;
            return new Rect(b.x * k, Screen.height - b.yMax * k, b.width * k, b.height * k);
        }

        /// <summary>Tells the camera which part of the screen the panels leave for the board.</summary>
        public void SetFreeAreaOn(IsoCamera cam) => cam.SetFreeArea(FreeScreenArea());

        public void ShowStart(string title, IEnumerable<PresetDef> presets, Content c)
        {
            _startTitle.text = title;
            _presets.Clear();
            var i = 1;
            foreach (var p in presets)
            {
                var chosen = p;
                var b = new Button(() => PresetClicked?.Invoke(chosen)) { focusable = false, text = "" };
                b.AddToClassList("preset-btn");
                b.Add(Ui.Text($"{i}  {p.Name}", "preset-name"));
                b.Add(Ui.Text(string.Join(", ", p.Abilities.Select(a => c.Abilities[a].Name)), "preset-kit"));
                _presets.Add(b);
                i++;
            }
            Ui.Show(_start, true);
            Ui.Show(_end, false);
        }

        public void ShowContentError(string message)
        {
            _startTitle.text = "The content did not load";
            _startError.text = message;
            Ui.Show(_startError, true);
            Ui.Show(_start, true);
        }

        public void HideStart() => Ui.Show(_start, false);

        public bool StartVisible => !_start.ClassListContains("hidden");

        public void ResetFight()
        {
            _log.Clear();
            _logShown = 0;
            _statusSig = _actionsSig = _infoSig = _minionSig = _intercomText = _bannerText = "";
            Ui.Show(_end, false);
            Ui.Show(_logModal, false);
            Ui.Show(_banner, false);
            Ui.Show(_alert, false);
            _alertLeft = 0f;
        }

        public void ShowEnd(Outcome o)
        {
            var win = o.Result == FightResult.Win;
            _endTitle.text = win ? "Victory" : "Defeat";
            _endTitle.EnableInClassList("win", win);
            _endTitle.EnableInClassList("lose", !win);
            _endCause.text = o.Cause;
            Ui.Show(_end, true);
        }

        public bool EndVisible => !_end.ClassListContains("hidden");

        public bool LogOpen => !_logModal.ClassListContains("hidden");

        public void SetLogOpen(bool open, GameState s)
        {
            Ui.Show(_logModal, open);
            if (!open) return;
            _logLines.Clear();
            foreach (var l in s.Intercom.Log) _logLines.Add(Ui.Text($"R{l.Round:00} · “{l.Text}”", "modal-line"));
        }

        private static void FillCells(VisualElement row, int n, int max, string classes)
        {
            row.Clear();
            for (var i = 0; i < Math.Max(max, n); i++) row.Add(Ui.El(i < n ? $"cell-pip {classes} full" : $"cell-pip {classes}"));
        }

        public void UpdateStatus(Content c, GameState s, IntentReport? intents, bool busy)
        {
            var p = s.Units.FirstOrDefault(u => u.Id == 0);
            var hp = p?.Hp ?? 0;
            var maxHp = p?.MaxHp ?? c.Units["player"].Hp;
            var incoming = intents?.PlayerDamage ?? 0;
            var lethal = intents?.PlayerDies == true;
            var showIncoming = !busy && s.Phase == Phase.Player && intents != null;
            var sig = $"{hp}/{maxHp}/{p?.Statuses.Poison?.Remaining}/{p?.Statuses.Parasite?.Remaining}/{s.Ap}/{s.Movement}/{s.Round}/{s.Phase}/{showIncoming}/{incoming}/{lethal}";
            if (sig == _statusSig) return;
            _statusSig = sig;

            var low = hp <= maxHp / 3f;
            FillCells(_hpCells, hp, maxHp, low ? "hp low" : "hp");
            _hpText.text = $"{hp}/{maxHp}";
            _hpPills.Clear();
            if (p?.Statuses.Poison != null) _hpPills.Add(Pill($"Poisoned {p.Statuses.Poison.Remaining}", Palette.Poison));
            if (p?.Statuses.Parasite != null) _hpPills.Add(Pill($"Parasite {p.Statuses.Parasite.Remaining}", Palette.Parasite));
            FillCells(_apCells, s.Ap, c.Rules.Turn.Ap, "");
            FillCells(_moveCells, s.Movement, c.Rules.Turn.Movement, "");
            _round.text = s.Round.ToString("00");
            _phase.text = PhaseName(s.Phase);
            _incoming.text = !showIncoming ? "" : lethal ? "Ending the turn now is lethal" : incoming > 0 ? $"End turn now: −{incoming} HP" : "No incoming attacks";
            _incoming.EnableInClassList("bad", showIncoming && incoming > 0);
            _incoming.EnableInClassList("lethal", showIncoming && lethal);
        }

        private static Label Pill(string text, Color c)
        {
            var l = Ui.Text(text, "pill");
            l.style.color = c;
            l.style.borderTopColor = l.style.borderBottomColor = l.style.borderLeftColor = l.style.borderRightColor = c;
            return l;
        }

        private static string PhaseName(Phase p)
        {
            switch (p)
            {
                case Phase.RoundStart: return "Round start";
                case Phase.Player: return "Your turn";
                case Phase.Minion: return "Minions";
                case Phase.Enemy: return "Enemies";
                case Phase.Environment: return "Environment";
                default: return "Fight over";
            }
        }

        public void UpdateActions(Content c, IReadOnlyList<ActionStatus> statuses, string? selected, bool disabled, string grappleMode)
        {
            var sig = $"{selected}|{disabled}|{grappleMode}|" + string.Join(";", statuses.Select(s => $"{s.Id},{s.Usable},{s.Reason}"));
            if (sig == _actionsSig) return;
            _actionsSig = sig;
            _actionRow.Clear();
            for (var i = 0; i < statuses.Count; i++)
            {
                var st = statuses[i];
                var id = st.Id;
                var kit = c.Abilities.TryGetValue(st.Id, out var def) ? def.Kit : "utility";
                var b = new Button(() => ActionClicked?.Invoke(id)) { focusable = false, text = "", tooltip = st.Reason ?? st.Name };
                b.AddToClassList("action-btn");
                b.AddToClassList(kit);
                b.EnableInClassList("selected", selected == st.Id);
                b.SetEnabled(!disabled && st.Usable);
                var top = Ui.El("ab-top");
                top.Add(Ui.Text(st.Name, "ab-name"));
                top.Add(Ui.Text(i < ActionKeyLabels.Length ? ActionKeyLabels[i] : "", "ab-key"));
                b.Add(top);
                b.Add(Ui.Text(!st.Usable && st.Reason != null ? $"{st.Ap} AP · {st.Reason}" : $"{st.Ap} AP", "ab-cost"));
                _actionRow.Add(b);
                if (st.Id != "grapple_hook" || selected != st.Id) continue;
                var seg = Ui.El("seg", true);
                seg.Add(SegButton("Pull unit", grappleMode == "unit", "unit"));
                seg.Add(SegButton("Pull self", grappleMode == "self", "self"));
                _actionRow.Add(seg);
            }
        }

        private Button SegButton(string text, bool on, string mode)
        {
            var b = new Button(() => GrappleModeClicked?.Invoke(mode)) { focusable = false, text = text };
            b.AddToClassList("seg-btn");
            b.EnableInClassList("on", on);
            return b;
        }

        public void UpdateControls(bool idle, bool canUndo, bool busy, string hint, FightSpeed speed, bool showIntents)
        {
            _undo.SetEnabled(idle && canUndo);
            _endTurn.SetEnabled(idle);
            Ui.Show(_skip, busy);
            _hint.text = hint;
            _speed1.EnableInClassList("on", speed == FightSpeed.Normal);
            _speed2.EnableInClassList("on", speed == FightSpeed.Fast);
            _speed0.EnableInClassList("on", speed == FightSpeed.Skip);
            _intents.EnableInClassList("off", !showIntents);
        }

        public void UpdateInfo(string title, IReadOnlyList<InfoLine> lines)
        {
            var sig = title + "\n" + string.Join("\n", lines.Select(l => l.Style + ":" + l.Text));
            if (sig == _infoSig) return;
            _infoSig = sig;
            _infoTitle.text = title;
            _infoBody.Clear();
            foreach (var l in lines) _infoBody.Add(Ui.Text(l.Text, $"info-line {l.Style}"));
        }

        public void UpdateMinions(Content c, GameState s)
        {
            var minions = s.Units.Where(u => u.Kind == UnitKind.Minion && !u.Dead).ToList();
            var sig = string.Join(";", minions.Select(m => $"{m.Id},{m.Hp},{m.Pos},{m.Ammo},{m.Charges},{m.DroneState}"));
            if (sig == _minionSig && _minionCount.text.Length > 0) return;
            _minionSig = sig;
            _minionCount.text = $"{minions.Count}/{c.Rules.Caps.Minions}";
            _minions.Clear();
            if (minions.Count == 0) _minions.Add(Ui.Text("None deployed.", "muted"));
            foreach (var m in minions)
            {
                var where = m.Docked ? "docked" : Tiles.Name(m.Pos);
                var extra = m.Ammo.HasValue ? $"ammo {m.Ammo}" : m.Charges.HasValue ? (m.DroneState == DroneState.Deployed ? $"{m.Charges} chg" : Names.Of(m.DroneState!.Value)) : "";
                var row = Ui.El("minion-row");
                row.Add(Ui.Text($"{Words.UnitName(c, m.Def, m.Id)}  {where}", "info-line"));
                row.Add(Ui.Text($"{m.Hp}/{m.MaxHp}  {extra}", "faint"));
                _minions.Add(row);
            }
        }

        public void UpdateLog(IReadOnlyList<LogLine> lines)
        {
            if (lines.Count <= 0 || (_logShown > 0 && lines[lines.Count - 1].Id == _logShown)) return;
            foreach (var l in lines.Where(x => x.Id > _logShown || _logShown == 0))
            {
                _log.Add(Ui.Text(l.Text, $"log-line {KindClass(l.Kind)}"));
            }
            _logShown = lines[lines.Count - 1].Id;
            while (_log.contentContainer.childCount > 300) _log.contentContainer.RemoveAt(0);
            _log.schedule.Execute(() => _log.scrollOffset = new Vector2(0f, float.MaxValue));
        }

        private static string KindClass(LogKind k)
        {
            switch (k)
            {
                case LogKind.Round: return "round";
                case LogKind.Phase: return "phase";
                case LogKind.FriendlyFire: return "ff";
                case LogKind.Hazard: return "hazard";
                case LogKind.Intercom: return "intercom";
                case LogKind.End: return "end";
                default: return "";
            }
        }

        public void UpdateBanner(string? text, bool over)
        {
            var show = text != null && !over;
            Ui.Show(_banner, show);
            if (!show || text == _bannerText) return;
            _bannerText = text!;
            _banner.text = text;
            _banner.EnableInClassList("minion", text == "Minion phase");
            _banner.EnableInClassList("enemy", text == "Enemy phase");
            _banner.EnableInClassList("environment", text == "Environment");
        }

        public void Alert(string text, float seconds)
        {
            _alert.text = text;
            _alertLeft = seconds;
            Ui.Show(_alert, true);
        }

        public void Toast(string text)
        {
            _toast.text = text;
            _toastLeft = 2.2f;
            Ui.Show(_toast, true);
        }

        public void Flash(bool alarm, bool hurt)
        {
            void Restart(VisualElement e, bool on)
            {
                if (!on) return;
                e.AddToClassList("on");
                e.schedule.Execute(() => e.RemoveFromClassList("on")).ExecuteLater(60);
            }
            Restart(_alarm, alarm);
            Restart(_hurt, hurt);
        }

        public void Tick(float dt, GameState s)
        {
            var last = s.Intercom.Log.Count > 0 ? s.Intercom.Log[s.Intercom.Log.Count - 1] : null;
            var text = last != null ? $"“{last.Text}”" : "Static.";
            if (text != _intercomText)
            {
                _intercomText = text;
                _typed = 0f;
            }
            _typed += dt * 60f;
            _intercom.text = text.Substring(0, Mathf.Min(text.Length, Mathf.CeilToInt(_typed)));

            if (_alertLeft > 0f)
            {
                _alertLeft -= dt;
                if (_alertLeft <= 0f) Ui.Show(_alert, false);
            }
            if (_toastLeft > 0f)
            {
                _toastLeft -= dt;
                if (_toastLeft <= 0f) Ui.Show(_toast, false);
            }
        }
    }
}
