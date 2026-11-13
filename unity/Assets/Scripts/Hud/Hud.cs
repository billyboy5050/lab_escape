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
            // Reduced, it does not pop in from a larger size: it fades.
            var scale = Motion.Reduced ? 1f : Mathf.LerpUnclamped(_from, 1f, back);
            var leaving = Mathf.Clamp01((_age - _leaveAt) / Out);
            _e.style.scale = new Scale(new Vector3(scale, scale, 1f));
            _e.style.opacity = Mathf.Clamp01(_age / 0.05f) * (1f - leaving);
            _e.style.translate = new Translate(0f, Motion.Reduced ? 0f : -14f * leaving);
        }
    }

    /// <summary>The interface around the board. The layout and styling are in UI/Fight.uxml and UI/Fight.uss; this fills them in.</summary>
    public sealed class Hud
    {
        /// <summary>Keys for the action bar, in bar order. A full loadout plus the four utilities is 12 actions.</summary>
        public static readonly string[] ActionKeyLabels = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };

        private readonly VisualElement _root;
        private readonly VisualElement _boardArea;
        private readonly VisualElement _title, _titleTerminal;
        private readonly Label _titleRoom, _titleBlurb, _titleFoot;
        private readonly Label _startKicker, _startHelp;
        private readonly VisualElement _briefing, _briefingPlan, _briefingObjective, _briefingKit, _briefingKnow;
        private readonly Label _briefingTitle, _briefingGrid, _briefingDevices, _briefingTransmission;
        private Typewriter? _briefingType;
        private readonly VisualElement _reportModal;
        private readonly ScrollView _reportBody;
        private readonly Label _reportKicker, _reportTitle;
        public readonly LoadoutView Loadout;
        private readonly List<Label> _titleLabels = new List<Label>();
        private Typewriter? _titleType;
        private float _titleClock;
        private readonly VisualElement _hpCells, _hpPills, _apCells, _moveCells, _actionRow, _infoBody, _minions, _start, _end, _logModal;
        private readonly VisualElement _alarm, _hurt, _vignette, _opener, _endCard, _endRule, _endFigures, _recentBox, _recent, _endDownloads;
        private readonly Label _endSaved;
        private readonly VisualElement[] _overBoard;
        private readonly Label _hpText, _round, _phase, _incoming, _hint, _intercom, _infoTitle, _minionCount, _banner, _alert, _toast, _endTitle, _endCause, _startTitle, _startError, _openerTitle, _openerSub;
        private readonly Entrance _bannerIn, _alertIn, _openerIn, _endIn;
        private readonly Button _undo, _endTurn, _skip, _intents, _sound, _motion, _speed0, _speed1, _speed2;
        private readonly Button _titleSound, _titleMotion;
        private readonly ScrollView _log, _logLines, _debugText;
        private readonly VisualElement _debug, _debugReplay;
        private readonly Label _debugVersion, _debugContent, _debugQueued, _debugReplayAt, _debugReplayPauseText;
        private readonly Label _editUnit, _editHp, _editAp, _editMove, _editSpawn, _editSpawnGoText;
        private readonly Button _editSetHp, _editSetAp, _editSpawnGo, _editForceWave, _editHoldWave, _editRestart;
        private readonly Button[] _editSteppers;
        private readonly Button _debugIds, _debugSpawns, _debugHazards, _debugFlamer, _debugLog, _debugStep, _debugNext, _debugNextPhase, _debugReplayPause, _debugReplayStep;
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

        /// <summary>The title screen's Begin was pressed.</summary>
        public event Action? BeginClicked;

        /// <summary>The reduce-motion button, in the fight's controls or on the title.</summary>
        public event Action? MotionClicked;

        /// <summary>The briefing's two buttons: go in, or back to the loadout screen.</summary>
        public event Action? DeployClicked, BriefingBackClicked;

        /// <summary>The end card's Full report, and the report's Close.</summary>
        public event Action? ReportClicked, ReportCloseClicked;

        /// <summary>The unit to edit was stepped (-1 or 1), or the kind to spawn (1 or -1 through <see cref="DebugSpawnCycled"/>).</summary>
        public event Action<int>? DebugUnitCycled, DebugSpawnCycled;

        /// <summary>A value to set was stepped: "hp", "ap" or "move", and by how much.</summary>
        public event Action<string, int>? DebugStepped;

        /// <summary>One of the edit buttons was pressed: "setHp", "setAp", "spawn", "forceWave", "holdWave", or "restart".</summary>
        public event Action<string>? DebugEditClicked;

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
            _debugContent = L("debug-content");
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
            _motion = B("motion", () => MotionClicked?.Invoke());
            _titleSound = B("title-sound", () => SoundClicked?.Invoke());
            _titleMotion = B("title-motion", () => MotionClicked?.Invoke());
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
            _debugReplayPause = B("debug-replay-pause", () => ReplayPauseClicked?.Invoke());
            _debugReplayStep = B("debug-replay-step", () => ReplayStepClicked?.Invoke());
            _editUnit = L("edit-unit");
            _editHp = L("edit-hp");
            _editAp = L("edit-ap");
            _editMove = L("edit-move");
            _editSpawn = L("edit-spawn");
            _editSpawnGoText = L("edit-spawn-go-text");
            var steppers = new List<Button>
            {
                B("edit-unit-prev", () => DebugUnitCycled?.Invoke(-1)),
                B("edit-unit-next", () => DebugUnitCycled?.Invoke(1)),
                B("edit-hp-down", () => DebugStepped?.Invoke("hp", -1)),
                B("edit-hp-up", () => DebugStepped?.Invoke("hp", 1)),
                B("edit-ap-down", () => DebugStepped?.Invoke("ap", -1)),
                B("edit-ap-up", () => DebugStepped?.Invoke("ap", 1)),
                B("edit-move-down", () => DebugStepped?.Invoke("move", -1)),
                B("edit-move-up", () => DebugStepped?.Invoke("move", 1)),
                B("edit-spawn-prev", () => DebugSpawnCycled?.Invoke(-1)),
                B("edit-spawn-next", () => DebugSpawnCycled?.Invoke(1)),
            };
            _editSteppers = steppers.ToArray();
            _editSetHp = B("edit-set-hp", () => DebugEditClicked?.Invoke("setHp"));
            _editSetAp = B("edit-set-ap", () => DebugEditClicked?.Invoke("setAp"));
            _editSpawnGo = B("edit-spawn-go", () => DebugEditClicked?.Invoke("spawn"));
            _editForceWave = B("edit-force-wave", () => DebugEditClicked?.Invoke("forceWave"));
            _editHoldWave = B("edit-hold-wave", () => DebugEditClicked?.Invoke("holdWave"));
            _editRestart = B("edit-restart", () => DebugEditClicked?.Invoke("restart"));
            _title = E("title");
            _titleTerminal = E("title-terminal");
            _titleRoom = L("title-room");
            _titleBlurb = L("title-blurb");
            _titleFoot = L("title-foot");
            B("title-begin", () => BeginClicked?.Invoke());
            _briefing = E("briefing");
            _briefingPlan = E("briefing-plan");
            _briefingObjective = E("briefing-objective");
            _briefingKit = E("briefing-kit");
            _briefingKnow = E("briefing-know");
            _briefingTitle = L("briefing-title");
            _briefingGrid = L("briefing-grid");
            _briefingDevices = L("briefing-devices");
            _briefingTransmission = L("briefing-transmission");
            B("briefing-deploy", () => DeployClicked?.Invoke());
            B("briefing-back", () => BriefingBackClicked?.Invoke());
            _reportModal = E("report-modal");
            _reportBody = Q<ScrollView>("report-body");
            _reportKicker = L("report-kicker");
            _reportTitle = L("report-title");
            B("report-open", () => ReportClicked?.Invoke());
            B("close-report", () => ReportCloseClicked?.Invoke());
            _startKicker = L("start-kicker");
            _startHelp = L("start-help");
            Loadout = new LoadoutView(_root);
            Loadout.ButtonClicked += () => ButtonClicked?.Invoke();
            Loadout.PresetClicked += p => PresetClicked?.Invoke(p);
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

        /// <summary>Shows whether reduce motion is on, on every button that sets it.</summary>
        public void SetMotionState(bool reduced)
        {
            _motion.EnableInClassList("on", reduced);
            _titleMotion.EnableInClassList("on", reduced);
        }

        /// <summary>The title's sound button follows the mute.</summary>
        public void SetTitleSoundState(bool muted) => _titleSound.EnableInClassList("off", muted);

        public bool TitleVisible => !_title.ClassListContains("hidden");

        /// <summary>
        /// The first screen: who you are and why you are running. The terminal prints its lines a character at a time, as the web
        /// client's does; <see cref="TickScreens"/> keeps it going, and <see cref="FinishTitleTyping"/> prints the rest.
        /// </summary>
        public void ShowTitle(string room, IReadOnlyList<string> lines, string blurb, string footer)
        {
            HideScreens();
            _titleRoom.text = room.ToUpperInvariant();
            _titleBlurb.text = blurb;
            _titleFoot.text = footer;
            _titleType = new Typewriter(lines, ScreenText.TitleCharsPerSecond, ScreenText.TitleDelay);
            _titleClock = 0f;
            if (Motion.Reduced) _titleType.Finish();
            _titleTerminal.Clear();
            _titleLabels.Clear();
            for (var i = 0; i < lines.Count; i++)
            {
                var label = Ui.Text("", i >= ScreenText.TitleStatusLines ? "terminal-line mono alert" : "terminal-line mono");
                _titleLabels.Add(label);
                _titleTerminal.Add(label);
            }
            PaintTitle();
            Ui.Show(_title, true);
        }

        public bool BriefingVisible => !_briefing.ClassListContains("hidden");

        /// <summary>
        /// Between the loadout and the fight: the room from above, the intercom's first line, the goal and the kit. It says what
        /// the rules sheet says and nothing the game hides. The fight starts only when the player goes in.
        /// </summary>
        public void ShowBriefing(Content c, RoomDef room, Loadout loadout, FightSettings settings)
        {
            HideScreens();
            var waves = BriefingText.WavesInPlay(room, settings);
            _briefingTitle.text = Fonts.Caps(room.Name);
            _briefingGrid.text = BriefingText.Grid(room, waves);
            _briefingPlan.Clear();
            _briefingPlan.Add(RoomPlan.Build(c, room));
            _briefingDevices.text = BriefingText.Devices(c, room);
            Ui.Show(_briefingDevices, _briefingDevices.text.Length > 0);
            _briefingType = new Typewriter(new[] { BriefingText.Opening(c) }, BriefingText.CharsPerSecond, BriefingText.Delay);
            if (Motion.Reduced) _briefingType.Finish();
            Lines(_briefingObjective, BriefingText.Objective(c, room, waves));
            Lines(_briefingKnow, BriefingText.KnowTheRoom);
            _briefingKit.Clear();
            foreach (var (name, kit) in BriefingText.Kit(c, loadout)) _briefingKit.Add(Ui.Text(name, $"chip kit-{kit}"));
            PaintBriefing();
            Ui.Show(_briefing, true);
        }

        public bool ReportVisible => !_reportModal.ClassListContains("hidden");

        public void HideReport() => Ui.Show(_reportModal, false);

        /// <summary>The incident report: who hurt whom, by source, and the fight's facts, over the end card.</summary>
        public void ShowReport(ReportModel m)
        {
            _reportKicker.text = Fonts.Caps(m.Kicker);
            _reportTitle.text = Fonts.Caps(m.Heading);
            _reportTitle.EnableInClassList("win", m.Won);
            _reportTitle.EnableInClassList("lose", !m.Won);
            var body = _reportBody.contentContainer;
            body.Clear();
            body.Add(Ui.Text(m.Verdict, "report-verdict"));
            var pills = Ui.El("pills");
            for (var i = 0; i < m.Pills.Count; i++) pills.Add(Ui.Text(m.Pills[i], i == m.Pills.Count - 1 && m.FriendlyFireWarns ? "pill warn" : "pill"));
            body.Add(pills);
            body.Add(Ui.Text(m.Loadout, "muted"));
            foreach (var section in m.Sections)
            {
                var box = Ui.El("report-section");
                box.Add(Ui.Text(Fonts.Caps(section.Title), "col-title display"));
                foreach (var bar in section.Bars)
                {
                    var row = Ui.El("bar-row");
                    row.Add(Ui.Text(bar.Label, "bar-label"));
                    var track = Ui.El("bar-track");
                    var fill = Ui.El("bar-fill");
                    fill.style.width = new StyleLength(new Length(100f * bar.Value / section.Max, LengthUnit.Percent));
                    fill.style.backgroundColor = BarColor(bar.Category);
                    track.Add(fill);
                    row.Add(track);
                    row.Add(Ui.Text(bar.Value.ToString(), "bar-value mono"));
                    box.Add(row);
                }
                if (section.Note != null) box.Add(Ui.Text(section.Note, section.NoteWarns ? "report-note warn" : "report-note"));
                body.Add(box);
            }
            var facts = Ui.El("report-section");
            facts.Add(Ui.Text(Fonts.Caps("Fight"), "col-title display"));
            foreach (var (label, value) in m.Facts)
            {
                var row = Ui.El("kv-row");
                row.Add(Ui.Text(label, "kv-key"));
                row.Add(Ui.Text(value, "kv-value"));
                facts.Add(row);
            }
            body.Add(facts);
            var tel = Ui.El("report-section");
            tel.Add(Ui.Text(Fonts.Caps("Telemetry"), "col-title display"));
            tel.Add(Ui.Text(m.Telemetry, "report-note"));
            body.Add(tel);
            var intercom = Ui.El("report-section");
            intercom.Add(Ui.Text(Fonts.Caps("Intercom"), "col-title display"));
            if (m.Intercom.Count == 0) intercom.Add(Ui.Text("Nothing was said.", "report-note"));
            foreach (var (round, text) in m.Intercom) intercom.Add(Ui.Text($"R{round} \"{text}\"", "kv-value"));
            body.Add(intercom);
            _reportBody.scrollOffset = Vector2.zero;
            Ui.Show(_reportModal, true);
        }

        private static Color BarColor(SourceCategory c)
        {
            switch (c)
            {
                case SourceCategory.Enemy: return Palette.Enemy;
                case SourceCategory.Hazard: return Palette.Hazard;
                case SourceCategory.Player: return Palette.Player;
                default: return Palette.Minion;
            }
        }

        private static void Lines(VisualElement into, IReadOnlyList<string> lines)
        {
            into.Clear();
            foreach (var line in lines) into.Add(Ui.Text("- " + line, "brief-line"));
        }

        private void PaintBriefing()
        {
            var text = _briefingType?.Shown().FirstOrDefault() ?? "";
            _briefingTransmission.text = text;
        }

        /// <summary>What the intercom has printed so far, for tests.</summary>
        public string Transmission => _briefingTransmission.text;

        public void FinishBriefingTyping()
        {
            _briefingType?.Finish();
            PaintBriefing();
        }

        /// <summary>Keeps the screens that animate going: the title's terminal and the briefing's intercom.</summary>
        public void TickScreens(float dt)
        {
            if (BriefingVisible && _briefingType != null)
            {
                if (Motion.Reduced) _briefingType.Finish();
                _briefingType.Tick(dt);
                PaintBriefing();
            }
            if (!TitleVisible || _titleType == null) return;
            _titleClock += dt;
            if (Motion.Reduced) _titleType.Finish();
            _titleType.Tick(dt);
            PaintTitle();
        }

        /// <summary>Prints the whole title terminal at once.</summary>
        public void FinishTitleTyping()
        {
            _titleType?.Finish();
            PaintTitle();
        }

        /// <summary>What the title's terminal shows now, one line to a line, without its cursor: for tests.</summary>
        public IReadOnlyList<string> TitleTerminal() => _titleType?.Shown() ?? new List<string>();

        public bool TitleTypingDone => _titleType == null || _titleType.Done;

        private void PaintTitle()
        {
            if (_titleType == null) return;
            var shown = _titleType.Shown();
            var typing = _titleType.TypingLine();
            // A cursor on the line being printed, or after the last once everything is, blinking twice a second.
            var cursorOn = Motion.Reduced || ((int)(_titleClock * 2f) & 1) == 0;
            var cursorAt = typing >= 0 ? typing : shown.Count - 1;
            for (var i = 0; i < _titleLabels.Count; i++)
            {
                var text = shown[i];
                var visible = text.Length > 0 || (i == 0 && typing == 0);
                _titleLabels[i].text = text + (i == cursorAt && cursorOn && visible ? "_" : "");
                Ui.Show(_titleLabels[i], visible);
            }
        }

        /// <summary>Every screen before a fight is put away, so the one asked for is the only one.</summary>
        private void HideScreens()
        {
            Ui.Show(_title, false);
            Ui.Show(_start, false);
            Ui.Show(_briefing, false);
            // The report belongs to the fight that ended: leaving it, by any road, puts the report away.
            Ui.Show(_reportModal, false);
        }

        /// <summary>
        /// The loadout screen: the presets and picks, and the saved fights to watch again. `room` is where the fight is, and
        /// `tester` the playtest's ID if there is one. The picks are drawn by <see cref="UpdateLoadout"/>.
        /// </summary>
        public void ShowStart(string room, Content c, LoadoutPicker picker, string? tester, IReadOnlyList<Telemetry.SavedFight>? saved = null)
        {
            HideScreens();
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
            // The debug overlay has no fight to describe until the next one starts.
            _debugText.Clear();
            _debugSig = "";
            Ui.Show(_debugReplay, false);
            DisableEdits();
            _startKicker.text = Fonts.Caps($"Requisition · {room}");
            _startTitle.text = Fonts.Caps("Loadout");
            _startHelp.text = $"Choose up to {picker.MaxAbilities} of {c.AbilityOrder.Count} abilities and up to {picker.MaxUpgrades} upgrades, or press a preset's number. Nothing carries between attempts.";
            Loadout.Show(true);
            Loadout.Build(c, tester);
            Loadout.Update(c, picker);
            Ui.Show(_start, true);
            Ui.Show(_end, false);
        }

        /// <summary>Redraws what is picked, after a pick.</summary>
        public void UpdateLoadout(Content c, LoadoutPicker picker) => Loadout.Update(c, picker);

        /// <summary>A line under the loadouts, such as why a replay was refused. Null clears it.</summary>
        public void ShowStartMessage(string? message)
        {
            _startError.text = message ?? "";
            Ui.Show(_startError, message != null);
        }

        public void ShowContentError(string message)
        {
            HideScreens();
            _startKicker.text = "";
            _startHelp.text = "";
            _startTitle.text = "The content did not load";
            _startError.text = message;
            Ui.Show(_startError, true);
            Loadout.Show(false);
            Ui.Show(_start, true);
        }

        public void HideStart() => HideScreens();

        public bool StartVisible => !_start.ClassListContains("hidden");

        public bool DebugVisible => !_debug.ClassListContains("hidden");

        /// <summary>
        /// Shows or hides the debug overlay's column, headed by the engine version and content hash. The board area takes the
        /// room it leaves, and the camera follows.
        /// </summary>
        public void SetDebugVisible(bool on, string header)
        {
            Ui.Show(_debug, on);
            _debugVersion.text = header;
            _debugSig = "";
        }

        /// <summary>
        /// The debug overlay's line about content hot reload: the folder being watched, or why the last save was refused (`error`).
        /// Null takes the line away, as for a build with no folder to watch.
        /// </summary>
        public void SetContentNote(string? text, bool error)
        {
            _debugContent.text = text ?? "";
            _debugContent.EnableInClassList("error", error);
            Ui.Show(_debugContent, text != null);
        }

        /// <summary>No fight to edit: the buttons that act on one are off until the next fight starts.</summary>
        private void DisableEdits()
        {
            foreach (var b in new[] { _editSetHp, _editSetAp, _editSpawnGo, _editForceWave, _editHoldWave, _editRestart }) b.SetEnabled(false);
            _editSpawnGo.RemoveFromClassList("on");
            _editSpawnGoText.text = "Spawn on a tile";
        }

        /// <summary>
        /// Fills in the edit controls. They act only while `canEdit` (the player's turn, nothing moving, no saved fight playing
        /// back); Force next wave also needs a wave to bring, and restarting needs a fight.
        /// </summary>
        public void UpdateEdits(string unit, int hp, int ap, int move, string spawn, bool spawnWaiting, bool canEdit, bool canForceWave, bool wave3Held, bool canRestart, bool canHold)
        {
            if (!DebugVisible) return;
            _editUnit.text = unit;
            _editHp.text = hp.ToString();
            _editAp.text = ap.ToString();
            _editMove.text = move.ToString();
            _editSpawn.text = spawn;
            _editSpawnGoText.text = spawnWaiting ? "Click a tile (Esc cancels)" : "Spawn on a tile";
            _editSpawnGo.EnableInClassList("on", spawnWaiting);
            _editHoldWave.EnableInClassList("on", wave3Held);
            foreach (var b in _editSteppers) b.SetEnabled(true);
            _editSetHp.SetEnabled(canEdit);
            _editSetAp.SetEnabled(canEdit);
            _editSpawnGo.SetEnabled(canEdit || spawnWaiting);
            _editForceWave.SetEnabled(canEdit && canForceWave);
            _editHoldWave.SetEnabled(canHold);
            _editRestart.SetEnabled(canRestart);
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
            _debugReplayPause.SetEnabled(canStepReplay);
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
            Ui.Show(_reportModal, false);
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
        public void JoltHp()
        {
            if (!Motion.Reduced) _hpJolt = 1f;
        }

        /// <summary>A line over the board for `seconds`: the default is long enough for a refused action, a content error needs longer.</summary>
        public void Toast(string text, float seconds = 2.2f)
        {
            _toast.text = text;
            _toastLeft = seconds;
            Ui.Show(_toast, true);
        }

        public void Flash(bool alarm, bool hurt)
        {
            if (Motion.Reduced) return;
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
            _typed = Motion.Reduced ? text.Length : _typed + dt * 60f;
            _intercom.text = text.Substring(0, Mathf.Min(text.Length, Mathf.CeilToInt(_typed)));

            _now += dt;
            _bannerIn.Tick(dt);
            _alertIn.Tick(dt);
            _openerIn.Tick(dt);
            _endIn.Tick(dt);
            if (_hpJolt > 0f)
            {
                // Turned on part-way through a jolt, the HP is still at once.
                _hpJolt = Motion.Reduced ? 0f : Mathf.Max(0f, _hpJolt - dt * 3.2f);
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
