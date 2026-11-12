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

    /// <summary>
    /// How a banner arrives and leaves. It lands big and shrinks to its size in a sixth of a second, a little past it and back,
    /// so it reads as something set down hard; it leaves by fading upward. Driven by hand each frame, on real time.
    /// </summary>
    public sealed class Entrance
    {
        private const float In = 0.17f;
        private const float Out = 0.24f;

        private readonly VisualElement _e;
        private readonly float _from;
        private float _age = -1f;
        private float _leaveAt = float.MaxValue;

        public Entrance(VisualElement e, float from = 1.6f)
        {
            _e = e;
            _from = from;
        }

        public bool Showing => _age >= 0f;

        public void Show()
        {
            _age = 0f;
            _leaveAt = float.MaxValue;
            Ui.Show(_e, true);
            Apply();
        }

        /// <summary>Starts it leaving, once it has been up for at least `least` seconds.</summary>
        public void Hide(float least = 0f)
        {
            if (_age < 0f || _leaveAt < float.MaxValue) return;
            _leaveAt = Mathf.Max(_age, least);
        }

        public void HideNow()
        {
            _age = -1f;
            Ui.Show(_e, false);
        }

        public void Tick(float dt)
        {
            if (_age < 0f) return;
            _age += dt;
            if (_age >= _leaveAt + Out)
            {
                HideNow();
                return;
            }
            Apply();
        }

        private void Apply()
        {
            var k = Mathf.Clamp01(_age / In);
            // Ease out with a little overshoot.
            var back = 1f + 2.2f * Mathf.Pow(k - 1f, 3f) + 1.2f * Mathf.Pow(k - 1f, 2f);
            var scale = Mathf.LerpUnclamped(_from, 1f, back);
            var leaving = Mathf.Clamp01((_age - _leaveAt) / Out);
            _e.style.scale = new Scale(new Vector3(scale, scale, 1f));
            _e.style.opacity = Mathf.Clamp01(_age / 0.05f) * (1f - leaving);
            _e.style.translate = new Translate(0f, -14f * leaving);
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
        private readonly VisualElement _alarm, _hurt, _vignette, _opener, _endCard, _endRule, _endFigures, _recentBox, _recent, _endDownloads;
        private readonly Label _endSaved;
        private readonly VisualElement[] _overBoard;
        private readonly Label _hpText, _round, _phase, _incoming, _hint, _intercom, _infoTitle, _minionCount, _banner, _alert, _toast, _endTitle, _endCause, _startTitle, _startError, _openerTitle, _openerSub;
        private readonly Entrance _bannerIn, _alertIn, _openerIn, _endIn;
        private readonly Button _undo, _endTurn, _skip, _intents, _sound, _speed0, _speed1, _speed2;
        private readonly ScrollView _log, _logLines, _debugText;
        private readonly VisualElement _debug, _debugReplay;
        private readonly Label _debugVersion, _debugQueued, _debugReplayAt, _debugReplayPauseText;
        private readonly Button _debugIds, _debugSpawns, _debugHazards, _debugFlamer, _debugLog, _debugStep, _debugNext, _debugNextPhase, _debugReplayStep;
        private string _statusSig = "", _actionsSig = "", _infoSig = "", _minionSig = "", _intercomText = "", _bannerText = "", _debugSig = "";
        private int _logShown;
        private float _typed;
        private float _toastLeft;
        private float _hpJolt;
        private float _now;
        private static Texture2D? _vignetteTexture;
        private static Texture2D? _panelTexture;
        private static Texture2D? _glowTexture;

        public readonly VisualElement TagLayer;

        public event Action<string>? ActionClicked;
        /// <summary>The pointer has come onto an action button (its id) or left one (null).</summary>
        public event Action<string?>? ActionHovered;
        public event Action<string>? GrappleModeClicked;
        public event Action? UndoClicked, EndTurnClicked, SkipClicked, IntentsClicked, SoundClicked, OpenLogClicked, CloseLogClicked, AgainClicked, ChangeClicked;
        /// <summary>Any button but an action, a preset or End turn, which have sounds of their own: it clicks.</summary>
        public event Action? ButtonClicked;
        public event Action<FightSpeed>? SpeedClicked;
        public event Action<PresetDef>? PresetClicked;
        /// <summary>A saved fight on the start screen was chosen: its folder.</summary>
        public event Action<string>? SavedFightClicked;
        /// <summary>One of the end card's downloads was chosen: the file's name, such as replay.json.</summary>
        public event Action<string>? DownloadClicked;
        /// <summary>The debug overlay's buttons: hide it, turn step mode on or off, go to the next action or phase, and pause or step a replay.</summary>
        public event Action? DebugCloseClicked, StepModeClicked, ReplayPauseClicked, ReplayStepClicked;
        /// <summary>The debug overlay's Next buttons: true for Next phase.</summary>
        public event Action<bool>? NextClicked;
        /// <summary>One of the debug overlay's toggles was flipped: ids, spawns, hazards, flamer or log.</summary>
        public event Action<string>? DebugToggleClicked;

        public Hud(UIDocument doc)
        {
            _root = doc.rootVisualElement;
            T Q<T>(string name) where T : VisualElement => _root.Q<T>(name) ?? throw new InvalidOperationException($"Fight.uxml has no {name}");
            VisualElement E(string name) => Q<VisualElement>(name);
            Label L(string name) => Q<Label>(name);
            Button B(string name, Action act, bool click = true)
            {
                var b = Q<Button>(name);
                b.focusable = false;
                b.clicked += act;
                if (click) b.clicked += () => ButtonClicked?.Invoke();
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
            _vignette = E("vignette");
            _opener = E("opener");
            _endCard = E("end-card");
            _endRule = E("end-rule");
            _endFigures = E("end-figures");
            _recentBox = E("recent-box");
            _recent = E("recent");
            _endDownloads = E("end-downloads");
            _endSaved = L("end-saved");
            _openerTitle = L("opener-title");
            _openerSub = L("opener-sub");
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
            _debug = E("debug");
            _debugReplay = E("debug-replay");
            _debugText = Q<ScrollView>("debug-text");
            _debugVersion = L("debug-version");
            _debugQueued = L("debug-queued");
            _debugReplayAt = L("debug-replay-at");
            _debugReplayPauseText = L("debug-replay-pause-text");
            _log.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _log.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _bannerIn = new Entrance(_banner);
            _alertIn = new Entrance(_alert);
            _openerIn = new Entrance(_opener, 1.35f);
            _endIn = new Entrance(_endCard, 1.3f);
            _vignette.style.backgroundImage = new StyleBackground(Vignette());
            _alarm.style.backgroundImage = new StyleBackground(EdgeGlow());
            _hurt.style.backgroundImage = new StyleBackground(EdgeGlow());
            _overBoard = new[] { _vignette, _alarm, _hurt };
            Fonts.ApplyAll(_root);
            // Every panel and card gets the web client's frame: its shade from top to bottom, and a tick at two corners.
            _root.Query(className: "panel").ForEach(Frame);
            _root.Query(className: "card").ForEach(Frame);

            _undo = B("undo", () => UndoClicked?.Invoke());
            _endTurn = B("end-turn", () => EndTurnClicked?.Invoke(), false);
            _skip = B("skip", () => SkipClicked?.Invoke());
            _intents = B("intents", () => IntentsClicked?.Invoke());
            _sound = B("sound", () => SoundClicked?.Invoke());
            _speed1 = B("speed-1", () => SpeedClicked?.Invoke(FightSpeed.Normal));
            _speed2 = B("speed-2", () => SpeedClicked?.Invoke(FightSpeed.Fast));
            _speed0 = B("speed-0", () => SpeedClicked?.Invoke(FightSpeed.Skip));
            B("open-log", () => OpenLogClicked?.Invoke());
            B("close-log", () => CloseLogClicked?.Invoke());
            B("again", () => AgainClicked?.Invoke());
            B("download-replay", () => DownloadClicked?.Invoke("replay.json"));
            B("download-summary", () => DownloadClicked?.Invoke("summary.json"));
            B("download-events", () => DownloadClicked?.Invoke("events.jsonl"));
            B("change", () => ChangeClicked?.Invoke());
            B("debug-close", () => DebugCloseClicked?.Invoke());
            _debugIds = B("debug-ids", () => DebugToggleClicked?.Invoke("ids"));
            _debugSpawns = B("debug-spawns", () => DebugToggleClicked?.Invoke("spawns"));
            _debugHazards = B("debug-hazards", () => DebugToggleClicked?.Invoke("hazards"));
            _debugFlamer = B("debug-flamer", () => DebugToggleClicked?.Invoke("flamer"));
            _debugLog = B("debug-log", () => DebugToggleClicked?.Invoke("log"));
            _debugStep = B("debug-step", () => StepModeClicked?.Invoke());
            _debugNext = B("debug-next", () => NextClicked?.Invoke(false));
            _debugNextPhase = B("debug-next-phase", () => NextClicked?.Invoke(true));
            B("debug-replay-pause", () => ReplayPauseClicked?.Invoke());
            _debugReplayStep = B("debug-replay-step", () => ReplayStepClicked?.Invoke());
        }

        private static void Frame(VisualElement panel)
        {
            panel.style.backgroundImage = new StyleBackground(PanelShade());
            panel.Add(Ui.El("tick tl"));
            panel.Add(Ui.El("tick br"));
        }

        /// <summary>The web client's panel gradient, from its second panel colour at the top to its panel colour at the bottom.</summary>
        private static Texture2D PanelShade()
        {
            if (_panelTexture != null) return _panelTexture;
            const int n = 32;
            var t = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            Color top = Palette.Hex(0x131b20, 0.95f), bottom = Palette.Hex(0x0e1418, 0.95f);
            var px = new Color32[n];
            // Texture rows run from the bottom up.
            for (var y = 0; y < n; y++) px[y] = Color.Lerp(bottom, top, y / (n - 1f));
            t.SetPixels32(px);
            t.Apply();
            _panelTexture = t;
            return t;
        }

        /// <summary>
        /// White light at the edges, gone by the middle: an inset glow, tinted by Fight.uss. Its 64 pixels on each side are slices,
        /// so the glow keeps its width on a board of any shape.
        /// </summary>
        private static Texture2D EdgeGlow()
        {
            if (_glowTexture != null) return _glowTexture;
            const int n = 130, slice = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            {
                for (var x = 0; x < n; x++)
                {
                    float gx = Mathf.Pow(1f - Mathf.Clamp01(Mathf.Min(x, n - 1 - x) / (float)slice), 2.2f);
                    float gy = Mathf.Pow(1f - Mathf.Clamp01(Mathf.Min(y, n - 1 - y) / (float)slice), 2.2f);
                    px[y * n + x] = new Color(1f, 1f, 1f, 0.62f * (1f - (1f - gx) * (1f - gy)));
                }
            }
            t.SetPixels32(px);
            t.Apply();
            _glowTexture = t;
            return t;
        }

        /// <summary>A soft darkening towards the corners of the board, made in code so the project keeps no image files.</summary>
        private static Texture2D Vignette()
        {
            if (_vignetteTexture != null) return _vignetteTexture;
            const int n = 96;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
            {
                for (var x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    px[y * n + x] = new Color(0f, 0f, 0f, 0.62f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.35f, r)));
                }
            }
            t.SetPixels32(px);
            t.Apply();
            _vignetteTexture = t;
            return t;
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

        public void ShowStart(string title, IEnumerable<PresetDef> presets, Content c, IReadOnlyList<Telemetry.SavedFight>? saved = null)
        {
            _recent.Clear();
            foreach (var f in saved ?? Array.Empty<Telemetry.SavedFight>())
            {
                var folder = f.Folder;
                var b = new Button(() =>
                {
                    ButtonClicked?.Invoke();
                    SavedFightClicked?.Invoke(folder);
                }) { focusable = false, text = f.Label };
                b.AddToClassList("recent-btn");
                b.AddToClassList("mono");
                Fonts.Apply(b);
                _recent.Add(b);
            }
            Ui.Show(_recentBox, _recent.childCount > 0);
            ShowStartMessage(null);
            _startTitle.text = title;
            _presets.Clear();
            var i = 1;
            foreach (var p in presets)
            {
                var chosen = p;
                var b = new Button(() =>
                {
                    ButtonClicked?.Invoke();
                    PresetClicked?.Invoke(chosen);
                }) { focusable = false, text = "" };
                b.AddToClassList("preset-btn");
                b.Add(Ui.Text($"{i}  {p.Name.ToUpperInvariant()}", "preset-name display"));
                b.Add(Ui.Text(string.Join(", ", p.Abilities.Select(a => c.Abilities[a].Name)), "preset-kit"));
                _presets.Add(b);
                i++;
            }
            Ui.Show(_start, true);
            Ui.Show(_end, false);
        }

        /// <summary>A line under the loadouts, such as why a replay was refused. Null clears it.</summary>
        public void ShowStartMessage(string? message)
        {
            _startError.text = message ?? "";
            Ui.Show(_startError, message != null);
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

        public bool DebugVisible => !_debug.ClassListContains("hidden");

        /// <summary>Shows or hides the debug overlay's column. The board area takes the room it leaves, and the camera follows.</summary>
        public void SetDebugVisible(bool on)
        {
            Ui.Show(_debug, on);
            _debugSig = "";
        }

        /// <summary>
        /// Fills in the debug overlay. `lines` are DebugReport.Panel's; `replayAt` is null when no saved fight is playing back.
        /// </summary>
        public void UpdateDebug(string version, DebugToggles t, IReadOnlyList<(string Text, string Style)> lines, bool stepMode, bool busy, bool holding, int pending, string? replayAt, bool replayPaused, bool canStepReplay)
        {
            if (!DebugVisible) return;
            _debugVersion.text = version;
            _debugIds.EnableInClassList("on", t.Ids);
            _debugSpawns.EnableInClassList("on", t.Spawns);
            _debugHazards.EnableInClassList("on", t.Hazards);
            _debugFlamer.EnableInClassList("on", t.Flamer);
            _debugLog.EnableInClassList("on", t.Log);
            _debugStep.EnableInClassList("on", stepMode);
            _debugNext.SetEnabled(stepMode && busy);
            _debugNextPhase.SetEnabled(stepMode && busy);
            _debugQueued.text = !stepMode ? "off" : holding ? $"holding · {pending} queued" : busy ? $"{pending} queued" : "on";
            Ui.Show(_debugReplay, replayAt != null);
            _debugReplayAt.text = replayAt ?? "";
            _debugReplayPauseText.text = replayPaused ? "Play" : "Pause";
            _debugReplayStep.SetEnabled(canStepReplay);

            var sig = string.Join("\n", lines.Select(l => l.Style + ":" + l.Text));
            if (sig == _debugSig) return;
            _debugSig = sig;
            _debugText.Clear();
            foreach (var (text, style) in lines) _debugText.Add(Ui.Text(text, $"debug-line {(style == "section" ? "display" : "mono")} {style}"));
        }

        public void ResetFight()
        {
            _log.Clear();
            _logShown = 0;
            _statusSig = _actionsSig = _infoSig = _minionSig = _intercomText = _bannerText = "";
            Ui.Show(_end, false);
            Ui.Show(_logModal, false);
            _bannerIn.HideNow();
            _alertIn.HideNow();
            _openerIn.HideNow();
            _endIn.HideNow();
            _hpJolt = 0f;
        }

        /// <summary>
        /// The end of the fight: who won, why, the fight in four figures from its summary, and what became of its telemetry:
        /// the folder it was saved to, the web build's downloads, or nothing for a replay.
        /// </summary>
        public void ShowEnd(Outcome o, FightSummary sum, GameState s, string? savedTo = null, bool offerDownloads = false, string? note = null)
        {
            _endSaved.text = savedTo != null ? $"Saved to {savedTo}" : note ?? "";
            Ui.Show(_endSaved, _endSaved.text.Length > 0);
            Ui.Show(_endDownloads, offerDownloads);
            var win = o.Result == FightResult.Win;
            _endTitle.text = win ? "VICTORY" : "DEFEAT";
            _endTitle.EnableInClassList("win", win);
            _endTitle.EnableInClassList("lose", !win);
            _endRule.EnableInClassList("lose", !win);
            _endCause.text = o.Cause;

            var player = s.Units.FirstOrDefault(u => u.Id == 0);
            var taken = sum.PlayerDamageTaken.Total;
            _endFigures.Clear();
            _endFigures.Add(Figure(sum.Rounds.ToString(), sum.Rounds == 1 ? "ROUND" : "ROUNDS"));
            _endFigures.Add(Figure($"{Math.Max(0, player?.Hp ?? 0)}/{player?.MaxHp ?? 0}", "HP LEFT"));
            _endFigures.Add(Figure(sum.Kills.Values.Sum().ToString(), "KILLS"));
            _endFigures.Add(Figure(taken.ToString(), "DAMAGE TAKEN", sum.FriendlyFireToPlayer > 0 ? $"{sum.FriendlyFireToPlayer} of it your own fire" : null));
            Ui.Show(_end, true);
            _endIn.Show();
        }

        private static VisualElement Figure(string value, string label, string? note = null)
        {
            var f = Ui.El("figure");
            f.Add(Ui.Text(value, "figure-value mono"));
            f.Add(Ui.Text(label, "figure-label display"));
            if (note != null) f.Add(Ui.Text(note, "figure-note"));
            return f;
        }

        /// <summary>The fight's title, set down over the board as the lights come on. It leaves by itself.</summary>
        public void ShowOpener(string title, string sub, float seconds)
        {
            _openerTitle.text = title.ToUpperInvariant();
            _openerSub.text = sub.ToUpperInvariant();
            Ui.Show(_openerSub, sub.Length > 0);
            _openerIn.Show();
            _openerIn.Hide(seconds);
        }

        public void HideOpener() => _openerIn.HideNow();

        public bool EndVisible => !_end.ClassListContains("hidden");

        public bool LogOpen => !_logModal.ClassListContains("hidden");

        public void SetLogOpen(bool open, GameState s)
        {
            Ui.Show(_logModal, open);
            if (!open) return;
            _logLines.Clear();
            foreach (var l in s.Intercom.Log) _logLines.Add(Ui.Text($"R{l.Round:00} · “{l.Text}”", "modal-line mono"));
        }

        private static void FillCells(VisualElement row, int n, int max, string classes, int ghost = 0)
        {
            row.Clear();
            for (var i = 0; i < Math.Max(max, n); i++) row.Add(Ui.El(i < n ? $"cell-pip {classes} full" : i < ghost ? $"cell-pip {classes} ghost" : $"cell-pip {classes}"));
        }

        /// <summary>
        /// `shownHp` is the player's HP as the board shows it, which falls when a blow lands and not when the engine resolved it;
        /// `ghostHp` is what it was before that blow, for as long as the board keeps it on show. Null for both reads the state.
        /// </summary>
        public void UpdateStatus(Content c, GameState s, IntentReport? intents, bool busy, int? shownHp = null, int? ghostHp = null)
        {
            var p = s.Units.FirstOrDefault(u => u.Id == 0);
            var hp = shownHp ?? p?.Hp ?? 0;
            var ghost = ghostHp ?? hp;
            var maxHp = p?.MaxHp ?? c.Units["player"].Hp;
            var incoming = intents?.PlayerDamage ?? 0;
            var lethal = intents?.PlayerDies == true;
            var showIncoming = !busy && s.Phase == Phase.Player && intents != null;
            var sig = $"{hp}/{ghost}/{maxHp}/{p?.Statuses.Poison?.Remaining}/{p?.Statuses.Parasite?.Remaining}/{s.Ap}/{s.Movement}/{s.Round}/{s.Phase}/{showIncoming}/{incoming}/{lethal}";
            if (sig == _statusSig) return;
            _statusSig = sig;

            var low = hp <= maxHp / 3f;
            FillCells(_hpCells, hp, maxHp, low ? "hp low" : "hp", ghost);
            _hpText.text = $"{hp}/{maxHp}";
            _hpPills.Clear();
            if (p?.Statuses.Poison != null) _hpPills.Add(Pill($"Poisoned {p.Statuses.Poison.Remaining}", Palette.Poison));
            if (p?.Statuses.Parasite != null) _hpPills.Add(Pill($"Parasite {p.Statuses.Parasite.Remaining}", Palette.Parasite));
            FillCells(_apCells, s.Ap, c.Rules.Turn.Ap, "");
            FillCells(_moveCells, s.Movement, c.Rules.Turn.Movement, "");
            _round.text = s.Round.ToString("00");
            _phase.text = PhaseName(s.Phase).ToUpperInvariant();
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
            // A reason every action shares, such as "Not your turn", is not written on each button: the bar dims instead.
            var shared = statuses.Count > 0 && statuses.All(x => !x.Usable && x.Reason == statuses[0].Reason) ? statuses[0].Reason : null;
            for (var i = 0; i < statuses.Count; i++)
            {
                var st = statuses[i];
                var id = st.Id;
                var kit = c.Abilities.TryGetValue(st.Id, out var def) ? def.Kit : "utility";
                // An action that cannot be used stays enabled, so hovering it can say why and clicking it says so too.
                var b = new Button(() => ActionClicked?.Invoke(id)) { focusable = false, text = "" };
                b.AddToClassList("action-btn");
                b.AddToClassList(kit);
                b.EnableInClassList("selected", selected == st.Id);
                b.EnableInClassList("off", disabled || !st.Usable);
                b.RegisterCallback<PointerEnterEvent>(_ => ActionHovered?.Invoke(id));
                b.RegisterCallback<PointerLeaveEvent>(_ => ActionHovered?.Invoke(null));
                var top = Ui.El("ab-top");
                top.Add(Ui.Text(Fonts.Caps(st.Name), "ab-name display"));
                top.Add(Ui.Text(i < ActionKeyLabels.Length ? ActionKeyLabels[i] : "", "ab-key mono"));
                b.Add(top);
                b.Add(Ui.Text(!st.Usable && st.Reason != null && shared == null ? $"{st.Ap} AP · {st.Reason}" : $"{st.Ap} AP", "ab-cost"));
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
            var b = new Button(() =>
            {
                ButtonClicked?.Invoke();
                GrappleModeClicked?.Invoke(mode);
            }) { focusable = false, text = text };
            b.AddToClassList("seg-btn");
            b.EnableInClassList("on", on);
            return b;
        }

        public void UpdateControls(bool idle, bool canUndo, bool busy, string hint, FightSpeed speed, bool showIntents, bool muted)
        {
            _sound.EnableInClassList("off", muted);
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
            _infoTitle.text = title.ToUpperInvariant();
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
                row.Add(Ui.Text($"{m.Hp}/{m.MaxHp}  {extra}", "faint mono"));
                _minions.Add(row);
            }
        }

        public void UpdateLog(IReadOnlyList<LogLine> lines)
        {
            if (lines.Count <= 0 || (_logShown > 0 && lines[lines.Count - 1].Id == _logShown)) return;
            foreach (var l in lines.Where(x => x.Id > _logShown || _logShown == 0))
            {
                _log.Add(Ui.Text(l.Text, $"log-line mono {KindClass(l.Kind)}"));
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
                case LogKind.Debug: return "debug";
                default: return "";
            }
        }

        /// <summary>
        /// The phase banner. It is set down when a phase starts, and stays up a moment after the queue has run out, so "Your turn"
        /// can be read while the player is already free to act.
        /// </summary>
        public void UpdateBanner(string? text, bool over)
        {
            if (text == null || over)
            {
                if (over) _bannerIn.HideNow();
                else _bannerIn.Hide(0.75f);
                _bannerText = "";
                return;
            }
            if (text == _bannerText) return;
            _bannerText = text;
            _banner.text = text.ToUpperInvariant();
            _banner.EnableInClassList("minion", text == "Minion phase");
            _banner.EnableInClassList("enemy", text == "Enemy phase");
            _banner.EnableInClassList("environment", text == "Environment");
            _bannerIn.Show();
        }

        public void Alert(string text, float seconds)
        {
            _alert.text = text.ToUpperInvariant();
            _alertIn.Show();
            _alertIn.Hide(seconds);
        }

        /// <summary>The player has been hit: the HP row jumps.</summary>
        public void JoltHp() => _hpJolt = 1f;

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

            _now += dt;
            _bannerIn.Tick(dt);
            _alertIn.Tick(dt);
            _openerIn.Tick(dt);
            _endIn.Tick(dt);
            if (_hpJolt > 0f)
            {
                _hpJolt = Mathf.Max(0f, _hpJolt - dt * 3.2f);
                var j = _hpJolt * _hpJolt;
                _hpCells.style.translate = new Translate(Mathf.Sin(_now * 67f) * 7f * j, Mathf.Cos(_now * 49f) * 4f * j);
                _hpText.style.scale = new Scale(new Vector3(1f + 0.5f * j, 1f + 0.5f * j, 1f));
            }
            // The vignette and the flashes sit over the board and nothing else.
            var b = _boardArea.worldBound;
            if (b.width > 1f)
            {
                foreach (var e in _overBoard)
                {
                    e.style.left = b.x;
                    e.style.top = b.y;
                    e.style.width = b.width;
                    e.style.height = b.height;
                }
            }
            if (_toastLeft > 0f)
            {
                _toastLeft -= dt;
                if (_toastLeft <= 0f) Ui.Show(_toast, false);
            }
        }
    }
}
