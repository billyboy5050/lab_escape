#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LabEscape.Core;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace LabEscape.Client
{
    /// <summary>Starts the game when any scene loads. Scenes stay empty; everything is built from data and code.</summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            new GameObject("Fight").AddComponent<FightDirector>();
        }
    }

    /// <summary>
    /// Runs the fight screen: reads input, feeds commands to the engine through FightFlow, and tells the board and HUD what to show.
    /// It decides no rules: what is legal comes from the engine, and previews and intents run the engine on a copy of the state.
    /// </summary>
    public sealed class FightDirector : MonoBehaviour
    {
        public static FightDirector? Instance { get; private set; }

        private static readonly KeyCode[] ActionKeys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8,
            KeyCode.Alpha9, KeyCode.Alpha0, KeyCode.Minus, KeyCode.Equals,
        };

        private Content? _c;
        private Hud _hud = null!;
        private SoundPlayer _sound = null!;
        private IsoCamera _cam = null!;
        private BoardTags? _tags;
        private BoardView? _board;
        private ActionModel? _model;
        private readonly BoardOverlay _overlay = new BoardOverlay();
        private Pos? _hover;
        private string? _hoverAction;
        private bool _showIntents = true;
        private CommandPreview? _preview;
        private string _previewKey = "";
        private bool _dragging;
        private Vector2 _dragFrom;
        private PresetDef? _preset;
        private FightSettings _settings = new FightSettings();
        private Loadout? _loadout;
        private readonly BoardClock _clock = new BoardClock();
        private bool _finalFrame;
        private string? _tester;
        /// <summary>The saved fight being played back, and the next of its commands to play; null in a fight of the player's own.</summary>
        private FightRecord? _replay;
        private int _replayNext;
        private float _replayWait;
        /// <summary>A saved fight held between commands (P), and one command asked for while it is held (N), played when the board is still.</summary>
        private bool _replayPaused;
        private bool _replayStep;
        /// <summary>The debug overlay (backtick): whether it is open, what it marks on the board, and whether step mode is on.</summary>
        private bool _debugOpen;
        private readonly DebugToggles _toggles = new DebugToggles();
        /// <summary>The debug overlay's edit controls: what they would set, and the kind of unit waiting for a tile to be clicked.</summary>
        private readonly DebugEdits _edits = new DebugEdits();
        private bool _stepMode;
        private GameScreen _screen = GameScreen.Title;
        /// <summary>The loadout screen's picks, which outlive a fight and are remembered between sessions.</summary>
        private LoadoutPicker? _picker;
        private IntentReport? _scoredFor;
        private List<ScoredOptions> _scored = new List<ScoredOptions>();
        /// <summary>When the fight ended, for the names of the web build's downloads.</summary>
        private double _endedAt;
        /// <summary>Counts fights started, so a ?replay= that arrives after the player has started one is dropped.</summary>
        private int _starts;
        /// <summary>Content hot reload: the watch on the content folder (null where there is none), and why the last save was refused.</summary>
        private ContentWatch? _watch;
        private float _watchWait;
        private IReadOnlyList<string>? _contentProblems;

        /// <summary>A replay plays its next command this long after the board is still, as the web client's does.</summary>
        private const float ReplayGap = 0.12f;

        /// <summary>How often the content folder is looked at, in real seconds.</summary>
        private const float WatchEvery = 0.5f;

        /// <summary>How many saved fights the start screen offers to play back.</summary>
        private const int SavedFightsShown = 6;

        public FightFlow? Flow { get; private set; }

        public Content? Content => _c;

        public ActionModel? Model => _model;

        public BoardView? Board => _board;

        public Hud Hud => _hud;

        public IsoCamera IsoCamera => _cam;

        /// <summary>Set by a scripted run: the tile to treat as under the pointer, instead of reading the mouse.</summary>
        public bool ScriptedPointer { get; set; }

        /// <summary>Set by a filmed run: every frame advances the game by exactly this long, however long it took to draw.</summary>
        public float? FixedStep { get; set; }

        public bool Idle => Flow != null && Flow.Idle;

        /// <summary>The player may act: the fight is idle and no saved fight is playing back. Highlights, previews and intents show only then.</summary>
        private bool CanAct => Flow != null && Flow.Idle && !Replaying;

        public bool EndScreenShown => _hud.EndVisible;

        /// <summary>True while a saved fight is playing back: the player watches, and only skip and speed act.</summary>
        public bool Replaying => _replay != null && _replayNext < _replay.Commands.Count;

        /// <summary>Who is playing, for the telemetry folder and the replay's meta; null for nobody.</summary>
        public string? Tester => _tester;

        /// <summary>The next of the saved fight's commands to play.</summary>
        public int ReplayNext => _replayNext;

        /// <summary>A saved fight is held between its commands until P, or N for one command.</summary>
        public bool ReplayPaused => _replay != null && _replayPaused;

        public bool DebugOpen => _debugOpen;

        /// <summary>The loadout screen's picks, for tests.</summary>
        public LoadoutPicker? Picker => _picker;

        /// <summary>Where the player is: the title, the loadout screen, the briefing, or a fight.</summary>
        public GameScreen CurrentScreen => _screen;

        /// <summary>The debug overlay's edit controls, for tests.</summary>
        public DebugEdits Edits => _edits;

        /// <summary>The fight's settings: the room, and how many waves are in play (2 when wave 3 is held back).</summary>
        public FightSettings Settings => _settings;

        /// <summary>The folder watched for saved content, or null when there is none to watch.</summary>
        public string? WatchedContent => _watch?.Folder;

        /// <summary>Why the last save to the content folder was refused, or null if it was not or has been put right.</summary>
        public IReadOnlyList<string>? ContentProblems => _contentProblems;

        /// <summary>What the debug overlay marks on the board, and whether the log writes its debug lines.</summary>
        public DebugToggles DebugToggles => _toggles;

        /// <summary>Step mode, from the debug overlay. It acts only while the overlay is open, so a closed overlay never holds a fight.</summary>
        public bool StepMode
        {
            get => _stepMode;
            set => _stepMode = value;
        }

        private bool Stepping => _debugOpen && _stepMode;

        private void Awake()
        {
            Instance = this;
            QualitySettings.antiAliasing = 4;
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
            Lighting.Reset();
            _cam = new IsoCamera();
            var go = new GameObject("HUD");
            go.transform.SetParent(transform, false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = Resources.Load<PanelSettings>("UI/Panel");
            doc.visualTreeAsset = Resources.Load<VisualTreeAsset>("UI/Fight");
            _hud = new Hud(doc);
            _sound = new SoundPlayer(transform);
            WireHud();
            var folder = GameContent.Folder;
            // Made before the first load, so a save in between is read.
            if (folder != null) _watch = new ContentWatch(folder);
            try
            {
                _c = GameContent.Load();
            }
            catch (Exception e)
            {
                // Whatever is wrong with the files, the game waits on the start screen, and starts once a save makes them valid.
                ShowContentFailure(e is ContentError ce ? ce.Problems : new[] { "Content could not be read: " + e.Message });
                return;
            }
            if (_watch != null) _watch.Hash = _c.Hash;
            Begin();
        }

        /// <summary>Everything that needs content: the start screen, and whatever the command line or the page's address asks to play.</summary>
        private void Begin()
        {
            SetDebugOpen(Array.IndexOf(Environment.GetCommandLineArgs(), "-labDebug") >= 0);
            ShowContentNote();
            SmokeRun.IsolateTelemetryIfAsked();
            _picker = NewPicker(_c!);
            Motion.Reduced = Motion.Load(TelemetrySink.Prefs);
            _hud.SetMotionState(Motion.Reduced);
            _tester = Telemetry.ChooseTester(Telemetry.Arg(Environment.GetCommandLineArgs(), "-labTester") ?? Telemetry.QueryParam(Application.absoluteURL, "tester"), TelemetrySink.Prefs);
            ShowTitle();
            // A saved fight to play back at once: a file on the command line, or ?replay= in the web build.
            var file = Telemetry.Arg(Environment.GetCommandLineArgs(), "-labReplay");
            if (file != null) PlayReplayFile(file);
            var url = Telemetry.QueryParam(Application.absoluteURL, "replay");
            if (!string.IsNullOrEmpty(url)) StartCoroutine(FetchReplay(url!));
            SmokeRun.AttachIfAsked(this);
        }

        /// <summary>The sounds this frame's cues and clicks asked for play together, once everything has had its Update.</summary>
        private void LateUpdate() => _sound?.Flush(Time.unscaledTime);

        public void ToggleSound() => _sound.SetMuted(!_sound.Muted);

        /// <summary>Turns the reduce-motion setting on or off, and remembers it.</summary>
        public void SetReducedMotion(bool on)
        {
            Motion.Reduced = on;
            Motion.Save(TelemetrySink.Prefs, on);
            _hud.SetMotionState(on);
            // What was already shaking settles at once.
            if (on) _cam.Still();
        }

        public void ToggleReducedMotion()
        {
            SetReducedMotion(!Motion.Reduced);
            _sound.Ask(Sfx.Ui);
            _hud.Toast(Motion.Reduced ? "Reduce motion on." : "Reduce motion off.");
        }

        public bool Muted => _sound.Muted;

        public SoundPlayer Sound => _sound;

        private void OnDestroy()
        {
            _sound?.Dispose();
            if (Instance == this) Instance = null;
        }

        private void WireHud()
        {
            _hud.ActionClicked += SelectAction;
            _hud.ActionHovered += SetActionPointer;
            _hud.GrappleModeClicked += m => _model?.SetGrappleMode(m);
            _hud.UndoClicked += () => TryUndo("Nothing to undo");
            _hud.EndTurnClicked += EndTurn;
            _hud.SkipClicked += () => Flow?.Skip();
            _hud.SpeedClicked += s => Flow?.SetSpeed(s);
            _hud.IntentsClicked += () => _showIntents = !_showIntents;
            _hud.SoundClicked += ToggleSound;
            _hud.ButtonClicked += () => _sound.Ask(Sfx.Ui);
            _hud.OpenLogClicked += () => SetLog(true);
            _hud.CloseLogClicked += () => SetLog(false);
            _hud.PresetClicked += SelectPreset;
            _hud.Loadout.AbilityToggled += id => Pick(p => p.ToggleAbility(id));
            _hud.Loadout.UpgradeToggled += id => Pick(p => p.ToggleUpgrade(id));
            _hud.Loadout.ClearClicked += () => Pick(p => p.Clear());
            _hud.Loadout.HoldWaveClicked += () => Pick(p => p.HoldWave3 = !p.HoldWave3);
            _hud.Loadout.StartClicked += StartFromPicker;
            _hud.Loadout.BackClicked += ShowTitle;
            _hud.SavedFightClicked += folder => PlayReplayFile(Path.Combine(folder, "replay.json"));
            _hud.DownloadClicked += f =>
            {
                if (Flow != null) TelemetrySink.Download(Flow.Session, f, _endedAt);
            };
            _hud.AgainClicked += Again;
            _hud.ChangeClicked += ShowStartScreen;
            _hud.DebugCloseClicked += () => SetDebugOpen(false);
            _hud.DebugToggleClicked += ToggleMark;
            _hud.StepModeClicked += () => _stepMode = !_stepMode;
            _hud.NextClicked += Next;
            _hud.ReplayPauseClicked += () => SetReplayPaused(!_replayPaused);
            _hud.ReplayStepClicked += StepReplay;
            _hud.DebugUnitCycled += d => { if (Flow != null && _c != null) _edits.CycleUnit(Flow.Shown, d); };
            _hud.DebugSpawnCycled += d => { if (_c != null) _edits.CycleSpawn(_c, d); };
            _hud.DebugStepped += (which, by) =>
            {
                switch (which)
                {
                    case "hp": _edits.StepHp(by); break;
                    case "ap": _edits.StepAp(by); break;
                    case "move": _edits.StepMove(by); break;
                }
            };
            _hud.DebugEditClicked += DebugEdit;
            _hud.BeginClicked += ShowStartScreen;
            _hud.MotionClicked += ToggleReducedMotion;
            _hud.DeployClicked += Deploy;
            _hud.BriefingBackClicked += ShowStartScreen;
            _hud.ReportClicked += OpenReport;
            _hud.ReportCloseClicked += _hud.HideReport;
        }

        // ------------------------------------------------------------------ content hot reload

        /// <summary>Looks at the content folder on a timer. Saving a content file while the game runs reloads it, as the web client does.</summary>
        private void TickWatch(float dt)
        {
            if (_watch == null) return;
            _watchWait -= dt;
            if (_watchWait > 0f) return;
            _watchWait = WatchEvery;
            CheckContent();
        }

        /// <summary>
        /// One look at the content folder (the timer's, and a test's). Content that has changed and is valid replaces the game's and
        /// restarts the fight, so one fight's log never mixes two sets of values. Content that is not valid is refused with its
        /// reason, and the game goes on with what it has.
        /// </summary>
        public void CheckContent()
        {
            var r = _watch?.Poll() ?? WatchResult.Nothing;
            switch (r.Kind)
            {
                case WatchKind.Reloaded:
                    ApplyContent(r.Content!);
                    break;
                case WatchKind.Rejected:
                    RejectContent(r);
                    break;
                case WatchKind.Restored:
                    _contentProblems = null;
                    ShowContentNote();
                    if (_c != null) _hud.Toast("Content is back to what the game has: nothing to reload.", ContentToastSeconds);
                    break;
            }
        }

        /// <summary>
        /// Points the hot reload at another folder, or with null back at the default, and loads what is there, as a reload would. For
        /// tests and scripted runs, which edit a copy of content/ and must put the game back as it was.
        /// </summary>
        public void WatchContentIn(string? folder)
        {
            GameContent.FolderOverride = folder;
            var at = GameContent.Folder;
            _watch = at != null ? new ContentWatch(at) : null;
            if (_watch != null) _watch.Hash = _c?.Hash;
            _contentProblems = null;
            var next = GameContent.Load();
            if (_c == null || next.Hash != _c.Hash) ApplyContent(next);
            else ShowContentNote();
        }

        /// <summary>A reload's message stays up longer than a refused click's.</summary>
        private const float ContentToastSeconds = 4f;

        private void ApplyContent(Content next)
        {
            var first = _c == null;
            var presetId = _preset?.Id;
            Debug.Log($"[Content] reloaded: content {_c?.Hash ?? "none"} -> {next.Hash}");
            _c = next;
            _contentProblems = null;
            if (_watch != null) _watch.Hash = next.Hash;
            if (first)
            {
                // The game started on content that would not load, and this is the first that does.
                Begin();
                _hud.Toast("Content loaded.", ContentToastSeconds);
                return;
            }
            // The hash in the overlay's header, and the line under it.
            SetDebugOpen(_debugOpen);
            ShowContentNote();
            // A selected preset follows its edited picks, so the fight and its telemetry match what was played.
            _preset = presetId == null ? null : next.Presets.FirstOrDefault(p => p.Id == presetId);
            // The loadout screen's picks follow the new content: a preset's edited picks, or the picks if they still fit.
            var hold = _picker?.HoldWave3 ?? false;
            if (_picker != null) LoadoutPrefs.Save(TelemetrySink.Prefs, _picker.ToLoadout(), _picker.PresetId);
            _picker = NewPicker(next);
            _picker.HoldWave3 = hold;
            if (Flow == null && _screen == GameScreen.Title)
            {
                ShowTitle();
                _hud.Toast("Content reloaded.", ContentToastSeconds);
                return;
            }
            if (Flow == null)
            {
                ShowStartScreen();
                _hud.ShowStartMessage("Content reloaded.");
                return;
            }
            if (_replay != null)
            {
                // A saved fight is only valid against the content it was recorded with.
                ShowStartScreen();
                _hud.ShowStartMessage("Content reloaded. The saved fight was recorded with other values, so it was stopped.");
                return;
            }
            RestartFight(next, presetId);
        }

        /// <summary>Starts the player's fight again from the new content, or puts the start screen back with the reason it cannot.</summary>
        private void RestartFight(Content next, string? presetId)
        {
            var loadout = _loadout!;
            if (_preset != null) loadout = new Loadout(_preset.Abilities, _preset.Upgrades);
            else if (presetId != null)
            {
                ShowStartScreen();
                _hud.ShowStartMessage($"Content reloaded. The {presetId} preset is gone, so pick again.");
                return;
            }
            var problems = Engine.LoadoutProblems(next, loadout);
            if (problems.Count > 0)
            {
                ShowStartScreen();
                _hud.ShowStartMessage($"Content reloaded. The loadout no longer fits ({string.Join("; ", problems)}), so pick again.");
                return;
            }
            try
            {
                StartFight(loadout, _settings, _preset?.Id);
            }
            catch (Exception e)
            {
                // A fight that cannot start leaves no fight behind and the start screen up, saying why.
                ShowStartScreen();
                _hud.ShowStartMessage($"Content reloaded, but the fight could not start: {e.Message}");
                return;
            }
            _hud.Toast("Content reloaded: the fight restarted with the new values.", ContentToastSeconds);
        }

        private void RejectContent(WatchResult r)
        {
            _contentProblems = r.Problems;
            Debug.LogWarning("[Content] not reloaded, keeping the old values:\n" + ContentWatch.FormatProblems(r.Problems, int.MaxValue));
            ShowContentNote();
            if (_c == null) ShowContentFailure(r.Problems);
            else _hud.Toast("Content error, keeping the old values: " + r.Summary, ContentToastSeconds + 2f);
        }

        /// <summary>The game has no content to run on: the start screen says why, and, if a folder is watched, what will start it.</summary>
        private void ShowContentFailure(IReadOnlyList<string> problems)
        {
            _contentProblems = problems;
            var watching = _watch != null ? $"\n\nWatching {_watch.Folder}: the game starts when it reads valid content." : "";
            _hud.ShowContentError("Content failed validation:\n" + ContentWatch.FormatProblems(problems) + watching);
        }

        /// <summary>The debug overlay's line about hot reload: where the game looks, or what it refused.</summary>
        private void ShowContentNote()
        {
            if (_watch == null) _hud.SetContentNote(null, false);
            else if (_contentProblems != null) _hud.SetContentNote("Content refused, old values kept:\n" + ContentWatch.FormatProblems(_contentProblems), true);
            else _hud.SetContentNote($"Watching {_watch.Folder}. Save a content file and the fight restarts.", false);
        }

        // ------------------------------------------------------------------ the debug overlay

        /// <summary>Opens or closes the debug overlay (backtick). Closed, it marks nothing, holds no frame and writes no debug lines.</summary>
        public void SetDebugOpen(bool open)
        {
            _debugOpen = open;
            if (!open) _edits.Armed = null;
            _hud.SetDebugVisible(open, _c != null ? DebugReport.Header(_c) : "");
        }

        /// <summary>One of the edit buttons. Each of the first four is a command, so it is in the fight's log and its replay.</summary>
        private void DebugEdit(string which)
        {
            if (Flow == null || _c == null) return;
            switch (which)
            {
                case "setHp":
                    if (CanAct) Commit(_edits.SetHp());
                    break;
                case "setAp":
                    if (CanAct) Commit(_edits.SetApAndMove());
                    break;
                case "spawn":
                    // The spawn waits for a click on a tile. Pressing again takes it back.
                    if (_edits.Armed != null) _edits.Armed = null;
                    else if (CanAct) _edits.Armed = _edits.SpawnDef;
                    break;
                case "forceWave":
                    if (CanAct && DebugEdits.CanForceWave(_c, Flow.Live)) Commit(_edits.ForceWave());
                    break;
                case "holdWave":
                    HoldBackWave3(_settings.MaxWaves != 2);
                    break;
                case "restart":
                    RestartFight();
                    break;
            }
        }

        /// <summary>Spawns the kind of unit the overlay holds on a tile, or the nearest free one. The spawn is a command, recorded in the replay.</summary>
        public void DebugSpawnAt(Pos tile)
        {
            var def = _edits.Armed ?? _edits.SpawnDef;
            _edits.Armed = null;
            if (CanAct) Commit(_edits.Spawn(def, tile));
        }

        /// <summary>
        /// Holds wave 3 back, or lets it come, and starts the fight again, as the web client's checkbox does: the setting is the
        /// fight's, so it is in its replay, and a fight cannot change it half-way. Not for a saved fight, whose settings are its own.
        /// </summary>
        public void HoldBackWave3(bool hold)
        {
            if (Flow == null || _replay != null || _loadout == null) return;
            var settings = _settings.Clone();
            settings.MaxWaves = hold ? 2 : (int?)null;
            StartFight(_loadout, settings, _preset?.Id);
        }

        /// <summary>Starts the fight again with the same loadout and settings. A saved fight is played from its first command.</summary>
        public void RestartFight()
        {
            if (Flow == null) return;
            var rec = _replay;
            if (rec != null) StartFight(rec.Loadout, rec.Settings, rec.Meta?.Preset, rec);
            else if (_loadout != null) StartFight(_loadout, _settings, _preset?.Id);
        }

        private void ToggleMark(string which)
        {
            switch (which)
            {
                case "ids": _toggles.Ids = !_toggles.Ids; break;
                case "spawns": _toggles.Spawns = !_toggles.Spawns; break;
                case "hazards": _toggles.Hazards = !_toggles.Hazards; break;
                case "flamer": _toggles.Flamer = !_toggles.Flamer; break;
                case "log": _toggles.Log = !_toggles.Log; break;
            }
        }

        /// <summary>
        /// N: in step mode, the next frame of the fight, or with `untilPhase` (Shift) the frames up to the next phase. Otherwise, with
        /// a saved fight paused, its next command.
        /// </summary>
        public void Next(bool untilPhase)
        {
            if (Flow == null) return;
            if (Stepping && Flow.Busy)
            {
                Flow.Advance(untilPhase);
                return;
            }
            if (ReplayPaused) StepReplay();
        }

        /// <summary>Holds a saved fight between its commands, or lets it play on. The command playing now finishes either way.</summary>
        public void SetReplayPaused(bool paused)
        {
            if (!Replaying) return;
            _replayPaused = paused;
            _replayStep = false;
            _replayWait = ReplayGap;
        }

        /// <summary>Plays a saved fight's next command, as soon as the board is still, and holds it there.</summary>
        public void StepReplay()
        {
            if (!Replaying) return;
            _replayPaused = true;
            _replayStep = true;
        }

        /// <summary>The AI's scored options from these intents, worked out once for each new report.</summary>
        private IReadOnlyList<ScoredOptions> ScoredFrom(IntentReport? rep)
        {
            if (!ReferenceEquals(rep, _scoredFor))
            {
                _scoredFor = rep;
                _scored = DebugReport.Scored(rep);
            }
            return _scored;
        }

        /// <summary>The first screen. Begin goes on to the loadout screen.</summary>
        public void ShowTitle()
        {
            TearDownFight();
            _screen = GameScreen.Title;
            _hud.ShowTitle(_c!.DefaultRoom.Name, ScreenText.TitleLines(_c), ScreenText.TitleBlurb, ScreenText.TitleFooter(_c));
        }

        /// <summary>The loadout screen, where a fight is chosen and saved fights are watched again.</summary>
        public void ShowStartScreen()
        {
            TearDownFight();
            _screen = GameScreen.Loadout;
            _picker ??= NewPicker(_c!);
            _hud.ShowStart(_c!.DefaultRoom.Name, _c, _picker, _tester, Telemetry.Recent(TelemetrySink.Root, SavedFightsShown));
        }

        /// <summary>Plays back a saved fight from a replay file. One that cannot be read or does not fit this game is refused, with why.</summary>
        public void PlayReplayFile(string path)
        {
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                RefuseReplay(path, e.Message);
                return;
            }
            PlayReplayText(text, path);
        }

        /// <summary>Plays back a saved fight from its JSON, which is untrusted: Telemetry.LoadReplay checks it before anything plays.</summary>
        public void PlayReplayText(string text, string from)
        {
            if (_c == null) return;
            FightRecord rec;
            try
            {
                rec = Telemetry.LoadReplay(_c, text);
            }
            catch (Exception e)
            {
                RefuseReplay(from, e.Message);
                return;
            }
            // "Fight again" after a replay starts its loadout as a fight of the player's own.
            _preset = _c.Presets.FirstOrDefault(p => p.Id == rec.Meta?.Preset);
            try
            {
                StartFight(rec.Loadout, rec.Settings, rec.Meta?.Preset, rec);
            }
            catch (Exception e)
            {
                // A fight that cannot start leaves no fight behind and the start screen up, saying why.
                RefuseReplay(from, e.Message);
            }
        }

        private void RefuseReplay(string from, string why)
        {
            ShowStartScreen();
            _hud.ShowStartMessage($"Could not play back {ReplayName(from)}: {why}");
        }

        /// <summary>A saved fight by its folder's name, any other replay file by its own name.</summary>
        public static string ReplayName(string from)
        {
            var name = Path.GetFileName(from);
            if (name == "replay.json")
            {
                var folder = Path.GetFileName(Path.GetDirectoryName(from) ?? "");
                if (!string.IsNullOrEmpty(folder)) name = folder;
            }
            return string.IsNullOrEmpty(name) ? from : name;
        }

        /// <summary>The web build's ?replay=: a replay file fetched from beside the page.</summary>
        private IEnumerator FetchReplay(string path)
        {
            var starts = _starts;
            Uri uri;
            try
            {
                uri = new Uri(new Uri(Application.absoluteURL), path);
            }
            catch (Exception e)
            {
                RefuseReplay(path, e.Message);
                yield break;
            }
            using (var req = UnityWebRequest.Get(uri))
            {
                yield return req.SendWebRequest();
                // The player started a fight while it loaded: that fight stands, and the replay is dropped.
                if (_starts != starts) yield break;
                if (req.result != UnityWebRequest.Result.Success) RefuseReplay(path, req.error);
                else PlayReplayText(req.downloadHandler.text, path);
            }
        }

        /// <summary>The picks the loadout screen opens on: the last session's if it is still a loadout, else the default preset's.</summary>
        private static LoadoutPicker NewPicker(Content c)
        {
            var (loadout, presetId) = LoadoutPrefs.Load(TelemetrySink.Prefs, c);
            return new LoadoutPicker(c, loadout, presetId);
        }

        /// <summary>A pick on the loadout screen: made, remembered and drawn.</summary>
        private void Pick(Action<LoadoutPicker> change)
        {
            if (_c == null || _picker == null) return;
            change(_picker);
            LoadoutPrefs.Save(TelemetrySink.Prefs, _picker.ToLoadout(), _picker.PresetId);
            _hud.UpdateLoadout(_c, _picker);
        }

        /// <summary>A preset picked on the loadout screen: its abilities and upgrades are picked, and nothing starts.</summary>
        public void SelectPreset(PresetDef p) => Pick(picker => picker.SelectPreset(p));

        /// <summary>Starts the fight the loadout screen has picked, if what it picked can be fought.</summary>
        public void StartFromPicker()
        {
            if (_picker == null || !_picker.CanStart) return;
            ShowBriefing();
        }

        /// <summary>The briefing for the fight the picks make. Going in starts it; the fight's turn clock starts then.</summary>
        public void ShowBriefing()
        {
            if (_c == null || _picker == null) return;
            TearDownFight();
            _screen = GameScreen.Briefing;
            _hud.ShowBriefing(_c, _c.RoomFor(_picker.ToSettings().Room), _picker.ToLoadout(), _picker.ToSettings());
        }

        /// <summary>Goes in: the briefing's fight starts with the picks it was shown for.</summary>
        public void Deploy()
        {
            if (_screen != GameScreen.Briefing || _picker == null || !_picker.CanStart) return;
            StartFight(_picker.ToLoadout(), _picker.ToSettings(), _picker.PresetId);
        }

        private void Again()
        {
            if (_loadout != null) StartFight(_loadout, _settings, _preset?.Id);
        }

        /// <summary>
        /// Starts a fight in the room its settings name. With `replay`, the fight is that saved fight played back: its commands
        /// play by themselves, its meta is the recording's, and nothing is saved at its end.
        /// </summary>
        public void StartFight(Loadout loadout, FightSettings settings, string? presetId, FightRecord? replay = null)
        {
            TearDownFight();
            _loadout = loadout;
            _settings = settings;
            var c = _c!;
            // The preset a fight came from, so a hot reload can follow its edited picks.
            _preset = presetId == null ? null : c.Presets.FirstOrDefault(p => p.Id == presetId);
            _hud.HideStart();
            _hud.ResetFight();
            RecordMeta meta;
            if (replay?.Meta != null) meta = RecordMeta.FromToken(replay.Meta.ToToken());
            else
            {
                meta = new RecordMeta { Label = "unity", View = "iso" };
                if (presetId != null) meta.Preset = presetId;
                if (_tester != null) meta.Tester = _tester;
            }
            _starts++;
            _screen = GameScreen.Fight;
            _replay = replay;
            _replayNext = 0;
            _replayWait = ReplayGap;
            _replayPaused = false;
            _replayStep = false;
            Flow = new FightFlow(c, loadout, settings, meta, timed: replay == null);
            _model = new ActionModel(c);
            _tags = new BoardTags(_hud.TagLayer, c, _cam);
            var room = c.RoomFor(settings.Room);
            _board = new BoardView(c, room, _cam, _tags, Flow.Shown);
            _board.CuePlayed += OnCue;
            _board.Thudded += OnThud;
            Flow.FrameShown += OnFrameShown;
            Flow.Settled += OnSettled;
            Flow.Snapped += OnSnapped;
            _clock.Reset();
            _finalFrame = false;
            _edits.Reset(c);
            Open(room);
            // A fight can start after this frame's Update has run. Frame the room now, or its first frame is drawn through a
            // camera that has not been told where the board is.
            _hud.SetFreeAreaOn(_cam);
            _cam.Tick(0f);
        }

        /// <summary>
        /// The fight's entrance: the lights strike, the room's name is set down, and the camera pulls back from the player to the
        /// room while the first wave falls in. Space skips it like any other animation.
        /// </summary>
        private void Open(RoomDef room)
        {
            var flow = Flow!;
            var player = flow.Shown.Units.FirstOrDefault(u => u.Id == 0);
            var hostiles = flow.Session.StartFrames.SelectMany(f => f.Events).OfType<WaveSpawned>().FirstOrDefault()?.Units.Count ?? 0;
            if (player != null) _cam.Open(Iso.Tile(player.Pos.X, player.Pos.Y));
            _board!.Ambience.Strike();
            _hud.ShowOpener(room.Name, hostiles > 0 ? $"{hostiles} hostiles inbound" : "", Feel.OpenerTitle);
            flow.Begin(Feel.OpenerLead);
        }

        private void TearDownFight()
        {
            _board?.Dispose();
            _board = null;
            _tags = null;
            Flow = null;
            _model = null;
            _hover = null;
            _hoverAction = null;
            _replay = null;
            _preview = null;
            _previewKey = "";
            _cam.Still();
        }

        private void OnFrameShown(GameState s, Stage stage)
        {
            _finalFrame = stage.Has<FightEnded>();
            _board!.Play(s, stage);
        }

        private void OnSettled(GameState s) => _board!.Settle(s);

        private void OnSnapped(GameState s)
        {
            _board!.Snap(s);
            _board.Ambience.Steady();
            _clock.Reset();
            _cam.Still();
            _hud.HideOpener();
        }

        /// <summary>
        /// What a cue does beyond the board: the blow stops the clock for a moment, shakes and kicks the camera, and the alarm
        /// and the red edges flash. This is where a hit gets most of its weight.
        /// </summary>
        private void OnCue(Cue cue)
        {
            var sfx = SoundCues.For(cue);
            if (sfx.HasValue) _sound.Ask(sfx.Value);
            if (cue.Kind != CueKind.Event || Flow == null) return;
            // At 2x the stops are half as long, like everything else.
            var pace = 1f / Flow.TimeScale;
            switch (cue.Event)
            {
                case DamageDealt d when d.Target.Kind == EntityKind.Unit:
                {
                    var onPlayer = d.Target.Id == 0;
                    _clock.Freeze(Feel.HitStop(d.Amount, d.Killed, onPlayer) * pace);
                    if (d.Amount <= 0) break;
                    var size = Mathf.Min(d.Amount, 6);
                    _cam.Shake(Feel.HitTrauma(d.Amount, d.Killed, onPlayer));
                    _cam.Kick(Feel.From(cue, d.Pos), Feel.KickBase + Feel.KickPerDamage * size);
                    if (onPlayer)
                    {
                        _hud.Flash(false, true);
                        _hud.JoltHp();
                    }
                    if (d.Killed && _finalFrame)
                    {
                        // The blow that ends the fight is the one to linger on.
                        _clock.Slow(Feel.FinalSlow, Feel.FinalSlowSeconds);
                        _cam.Punch(0.08f);
                    }
                    break;
                }
                case AreaEffect a when a.Kind == AreaKind.Blast || a.Kind == AreaKind.Burst:
                    _clock.Freeze(Feel.BlastStop * pace);
                    _cam.Shake(Feel.BlastTrauma);
                    _cam.Punch(Feel.BlastPunch);
                    break;
                case AreaEffect a when a.Kind == AreaKind.Panel || a.Kind == AreaKind.Sweep:
                    _cam.Shake(Feel.HazardTrauma);
                    break;
                case Charged c:
                    _clock.Freeze(Feel.SlamStop * pace);
                    _cam.Shake(Feel.SlamTrauma);
                    _cam.Kick(Iso.Tile(c.Dir.X, c.Dir.Y) - Iso.Tile(0f, 0f), Feel.SlamKick);
                    _cam.Punch(Feel.BlastPunch);
                    break;
                case HazardFired _:
                    _hud.Flash(true, false);
                    _board!.Ambience.SoundAlarm();
                    break;
                case WaveSpawned w:
                    _board!.Ambience.SoundAlarm();
                    if (w.Wave <= 1) break;
                    _hud.Flash(true, false);
                    _hud.Alert("Reinforcements", 1.6f);
                    break;
            }
        }

        /// <summary>Something heavy landed: the floor passes it on to the camera. A light footstep does not.</summary>
        private void OnThud(Vector3 at, float weight)
        {
            if (weight < 0.9f) return;
            _sound.Ask(Sfx.Land);
            _cam.Shake(weight * Feel.ThudTrauma);
            _cam.Kick(Vector3.down, Mathf.Min(Feel.ThudKickMost, weight * Feel.ThudKick));
        }

        // ------------------------------------------------------------------ player actions

        public void SelectAction(string id)
        {
            if (Flow == null || _model == null || !Flow.Idle || Replaying) return;
            var a = _model.Statuses.FirstOrDefault(x => x.Id == id);
            if (a == null) return;
            if (!a.Usable)
            {
                _hud.Toast($"{a.Name}: {a.Reason}");
                return;
            }
            _sound.Ask(Sfx.Select);
            if (id == "sprint")
            {
                Commit(new SprintCommand());
                return;
            }
            _model.Select(a);
        }

        public void ClickTile(Pos tile)
        {
            if (Flow == null || _model == null || !Flow.Idle || Replaying) return;
            if (_edits.Armed != null)
            {
                DebugSpawnAt(tile);
                return;
            }
            var cmd = _model.Click(tile);
            if (cmd != null) Commit(cmd);
        }

        public StepResult? Commit(Command cmd)
        {
            if (Flow == null || _model == null) return null;
            var r = Flow.Apply(cmd);
            if (!r.Ok) _hud.Toast(r.Error ?? "Not allowed");
            else _model.Clear();
            return r;
        }

        public void EndTurn()
        {
            if (Flow != null && Flow.Idle && !Replaying && Commit(new EndTurnCommand())?.Ok == true) _sound.Ask(Sfx.EndTurn);
        }

        public void SetPointer(Pos? tile) => _hover = tile;

        /// <summary>The action button under the pointer, whose rule the Scanner shows; null when there is none.</summary>
        public void SetActionPointer(string? id) => _hoverAction = id;

        private void TryUndo(string message)
        {
            if (Flow != null && Flow.Idle && !Replaying && !Flow.Undo()) _hud.Toast(message);
        }

        /// <summary>The incident report for the fight that has ended, over its end card.</summary>
        public void OpenReport()
        {
            if (Flow == null || _c == null || Flow.Live.Outcome == null) return;
            var room = _c.RoomFor(Flow.Live.Settings.Room);
            _hud.ShowReport(ReportModel.From(_c, room, Flow.Session.Summary(), Flow.Live, Flow.Session.Record));
        }

        private void SetLog(bool open)
        {
            if (Flow != null) _hud.SetLogOpen(open, Flow.Shown);
        }

        // ------------------------------------------------------------------ the frame

        private void Update()
        {
            // A long hitch is not played back as one huge step, which would throw every spring across the room.
            var real = FixedStep ?? Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            TickWatch(real);
            _hud.TickScreens(real);
            if (Flow == null)
            {
                ReadStartKeys();
                _hud.SetTitleSoundState(_sound.Muted);
                return;
            }
            ReadPointer();
            ReadKeys();
            if (Flow == null) return;
            Flow.StepMode = Stepping;
            Flow.DebugLog = _debugOpen && _toggles.Log;
            // The board runs on its own clock: a heavy blow stops it and the 2x speed doubles it. The interface keeps real time.
            var dt = _clock.Step(real) * Flow.TimeScale;
            Flow.Tick(dt);
            TickReplay(real);
            var live = Flow.Live;
            _model!.Refresh(live, CanAct);
            UpdatePreview();
            BuildOverlay();
            _hud.SetFreeAreaOn(_cam);
            _board!.Tick(dt, real, Flow.FrameTime, _overlay);
            _tags!.Tick(real, Flow.Shown, _board, _overlay);
            UpdateHud(real);
        }

        /// <summary>Plays a saved fight's next command once the board is still and the player's turn has come.</summary>
        private void TickReplay(float dt)
        {
            var rec = _replay;
            var flow = Flow;
            if (rec == null || flow == null || !Replaying || !flow.Idle) return;
            if (_replayPaused && !_replayStep) return;
            if (!_replayStep)
            {
                _replayWait -= dt;
                if (_replayWait > 0f) return;
            }
            _replayStep = false;
            _replayWait = ReplayGap;
            var r = flow.Apply(rec.Commands[_replayNext]);
            if (!r.Ok)
            {
                _hud.Toast($"Replay stopped: command {_replayNext + 1} of {rec.Commands.Count} was refused ({r.Error ?? "not allowed"})");
                _replayNext = rec.Commands.Count;
                return;
            }
            _replayNext++;
            _model?.Clear();
        }

        /// <summary>Control, Command or Alt is down: the key is the system's (see <see cref="KeyRules"/>).</summary>
        private static bool ModifierHeld() =>
            KeyRules.Modified(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl), Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand), Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));

        private void ReadStartKeys()
        {
            if (_c == null || ModifierHeld()) return;
            if (Input.GetKeyDown(KeyCode.BackQuote)) SetDebugOpen(!_debugOpen);
            if (Input.GetKeyDown(KeyCode.M)) ToggleSound();
            if (Input.GetKeyDown(KeyCode.C)) ToggleReducedMotion();
            if (_screen == GameScreen.Title)
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) ShowStartScreen();
                return;
            }
            if (_screen == GameScreen.Briefing)
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Deploy();
                else if (Input.GetKeyDown(KeyCode.Escape)) ShowStartScreen();
                return;
            }
            if (_screen != GameScreen.Loadout) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ShowTitle();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                StartFromPicker();
                return;
            }
            for (var i = 0; i < _c.Presets.Count && i < ActionKeys.Length; i++)
            {
                if (Input.GetKeyDown(ActionKeys[i]))
                {
                    SelectPreset(_c.Presets[i]);
                    return;
                }
            }
        }

        private void ReadPointer()
        {
            if (ScriptedPointer || _board == null) return;
            var screen = (Vector2)Input.mousePosition;
            var overUi = _hud.PointerOverUi(screen);

            _cam.Zoom(overUi ? 0f : Input.mouseScrollDelta.y);
            if (!overUi && (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)))
            {
                _dragging = true;
                _dragFrom = screen;
            }
            if (_dragging)
            {
                if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
                {
                    _cam.PanPixels(screen - _dragFrom);
                    _dragFrom = screen;
                }
                else _dragging = false;
            }

            // A button that is rebuilt under the pointer never says the pointer left it; leaving the interface does.
            if (!overUi) _hoverAction = null;
            var inWindow = screen.x >= 0f && screen.y >= 0f && screen.x < Screen.width && screen.y < Screen.height;
            _hover = overUi || !inWindow ? (Pos?)null : _board.PickTile(screen);
            if (Input.GetMouseButtonDown(0) && _hover.HasValue) ClickTile(_hover.Value);
        }

        private void ReadKeys()
        {
            // Modified keys are the system's: Cmd+Q quits, Cmd+1 switches window.
            if (ModifierHeld()) return;

            var pan = Vector2.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) pan.y -= 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) pan.y += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) pan.x += 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) pan.x -= 1f;
            if (pan != Vector2.zero) _cam.PanPixels(pan * (Time.unscaledDeltaTime * Screen.height * 0.8f));

            if (_hud.EndVisible && Input.GetKeyDown(KeyCode.R))
            {
                Again();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_edits.Armed != null) _edits.Armed = null;
                else if (_hud.ReportVisible) _hud.HideReport();
                else if (_hud.LogOpen) SetLog(false);
                else if (_model!.First != null) _model.ClearFirst();
                else _model.Clear();
                return;
            }
            if (Input.GetKeyDown(KeyCode.M)) ToggleSound();
            if (Input.GetKeyDown(KeyCode.C)) ToggleReducedMotion();
            if (Input.GetKeyDown(KeyCode.BackQuote)) SetDebugOpen(!_debugOpen);
            if (Input.GetKeyDown(KeyCode.P)) SetReplayPaused(!_replayPaused);
            if (Input.GetKeyDown(KeyCode.N)) Next(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            if (Input.GetKeyDown(KeyCode.L))
            {
                SetLog(!_hud.LogOpen);
                return;
            }
            if (_hud.LogOpen || _hud.ReportVisible) return;
            if (Input.GetKeyDown(KeyCode.Z) && KeyRules.Undoes(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) TryUndo("Nothing to undo: AP spent or nothing moved");
            if (Input.GetKeyDown(KeyCode.Space)) Flow!.Skip();
            if (Input.GetKeyDown(KeyCode.F)) Flow!.SetSpeed(Flow.Speed == FightSpeed.Normal ? FightSpeed.Fast : Flow.Speed == FightSpeed.Fast ? FightSpeed.Skip : FightSpeed.Normal);
            if (Input.GetKeyDown(KeyCode.I)) _showIntents = !_showIntents;
            if (Input.GetKeyDown(KeyCode.Tab) && _model!.ModeId == "grapple_hook") _model.ToggleGrappleMode();
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) EndTurn();
            for (var i = 0; i < ActionKeys.Length; i++)
            {
                if (!Input.GetKeyDown(ActionKeys[i])) continue;
                if (i < _model!.Statuses.Count) SelectAction(_model.Statuses[i].Id);
                break;
            }
        }

        private void UpdatePreview()
        {
            var cmd = _hover.HasValue && CanAct && _edits.Armed == null ? _model!.CandidateFor(_hover.Value) : null;
            var key = cmd?.ToJson() ?? "";
            if (key == _previewKey && ReferenceEquals(_previewBase, Flow!.Live)) return;
            _previewKey = key;
            _previewBase = Flow!.Live;
            _previewCommand = cmd;
            _preview = cmd != null ? Previews.PreviewCommand(_c!, Flow.Live, cmd, true) : null;
        }

        private GameState? _previewBase;
        private Command? _previewCommand;

        private void BuildOverlay()
        {
            var m = _model!;
            var idle = CanAct;
            var st = m.Selected;
            _overlay.Highlights.Clear();
            var placing = _edits.Armed != null;
            if (idle && st == null && !placing) _overlay.Highlights.Add((m.Moves.Select(x => x.Tile).ToList(), HighlightKind.Move));
            if (idle && st != null && st.Id != "sprint")
            {
                if (m.TwoStep && m.First.HasValue)
                {
                    _overlay.Highlights.Add((new List<Pos> { m.First.Value }, HighlightKind.Chosen));
                    _overlay.Highlights.Add((m.SecondTiles, HighlightKind.Second));
                }
                else _overlay.Highlights.Add((m.FirstTiles, HighlightKind.Target));
            }
            _overlay.Hover = _hover;
            _overlay.MovePath = idle && st == null && !placing && _hover.HasValue ? m.Moves.FirstOrDefault(x => x.Tile == _hover.Value)?.Path : null;
            _overlay.Preview = idle ? _preview : null;
            _overlay.Intents = idle ? m.Intents : null;
            _overlay.ShowIntents = _showIntents;
            _overlay.Debug = _debugOpen ? _toggles : null;
            _overlay.Scored = idle && _debugOpen ? ScoredFrom(m.Intents) : null;
        }

        private void UpdateHud(float dt)
        {
            var c = _c!;
            var flow = Flow!;
            var m = _model!;
            var shown = flow.Shown;
            var live = flow.Live;
            var idle = CanAct;

            _hud.Tick(dt, shown);
            var me = _board!.Units.TryGetValue(0, out var view) ? view : null;
            _hud.UpdateStatus(c, shown, idle ? m.Intents : null, flow.Busy, me?.ShownHp, me != null && me.GhostLeft > 0f ? me.GhostHp : me?.ShownHp);
            _hud.UpdateActions(c, m.Statuses, m.ModeId, !idle, m.GrappleMode);
            // Step mode's hint wins while it holds a fight, a saved fight's included.
            var hint = _edits.Armed != null ? $"Click a tile to spawn {DebugEdits.SpawnName(c, _edits.Armed)} there, or the nearest free one. Esc cancels."
                : !Replaying || (flow.Busy && flow.StepMode) ? m.Hint(flow)
                : _replayPaused ? $"Saved fight paused before command {_replayNext + 1} of {_replay!.Commands.Count}. P plays on, N plays the next command."
                : $"Playing back a saved fight: command {_replayNext + 1} of {_replay!.Commands.Count}. P pauses, Space skips, F changes the speed.";
            _hud.UpdateControls(idle, flow.Session.CanUndo, flow.Busy, hint, flow.Speed, _showIntents, _sound.Muted);
            _hud.UpdateMinions(c, shown);
            _hud.UpdateLog(flow.Log);
            _hud.UpdateBanner(flow.Banner, live.Outcome != null && !flow.Busy);
            var lines = new List<InfoLine>();
            var title = BuildInfo(lines);
            _hud.UpdateInfo(title, lines);
            if (_debugOpen)
            {
                var report = DebugReport.Panel(c, shown, _board!.Hazards, _board.WavesShown, idle ? ScoredFrom(m.Intents) : (IReadOnlyList<ScoredOptions>)Array.Empty<ScoredOptions>(), flow.Session.Events);
                var replayAt = _replay != null ? $"{Math.Min(_replayNext, _replay.Commands.Count)}/{_replay.Commands.Count} commands{(Replaying ? "" : ", finished")}" : null;
                _hud.UpdateDebug(DebugReport.Header(c), _toggles, report, _stepMode, flow.Busy, flow.Holding, flow.Pending, replayAt, _replayPaused, Replaying);
                _edits.Sync(c, shown);
                _hud.UpdateEdits(DebugEdits.UnitLabel(c, shown, _edits.UnitId), _edits.Hp, _edits.Ap, _edits.Move, DebugEdits.SpawnName(c, _edits.SpawnDef), _edits.Armed != null,
                    idle, DebugEdits.CanForceWave(c, live), _settings.MaxWaves == 2, canRestart: true, canHold: _replay == null);
            }
            if (live.Outcome != null && !flow.Busy && !_hud.EndVisible)
            {
                var (saved, note) = SaveTelemetry(flow.Session);
                _hud.ShowEnd(live.Outcome, flow.Session.Summary(), live, saved, _replay == null && !TelemetrySink.HasDisk, note);
                _sound.Ask(live.Outcome.Result == FightResult.Win ? Sfx.Win : Sfx.Lose);
            }
        }

        /// <summary>
        /// Saves a fight of the player's own when it ends: its attempt is numbered first, so the folder and the replay carry it.
        /// A replay is not saved again. Returns the folder, or a note on why there is none.
        /// </summary>
        private (string? Saved, string? Note) SaveTelemetry(FightSession session)
        {
            _endedAt = TelemetrySink.NowMs();
            if (_replay != null) return (null, "A saved fight played back: nothing new was saved.");
            if (session.Record.Meta != null) Telemetry.AssignAttempt(session.Record.Meta, TelemetrySink.Prefs);
            try
            {
                return (TelemetrySink.Save(session, _endedAt), null);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Telemetry] not saved: {e.Message}");
                return (null, $"Not saved: {e.Message}");
            }
        }

        /// <summary>
        /// The scanner panel: the action button under the pointer, the previewed action, or the unit or tile under the pointer.
        /// The hint is in the controls row, so it is not repeated here.
        /// </summary>
        private string BuildInfo(List<InfoLine> lines)
        {
            var c = _c!;
            var flow = Flow!;
            var shown = flow.Shown;
            if (_hoverAction != null && DescribeAction(lines, _hoverAction)) return "Action";
            var preview = CanAct ? _preview : null;
            if (preview != null)
            {
                DescribePreview(lines, preview, shown);
                return "Preview";
            }
            if (!_hover.HasValue)
            {
                lines.Add(new InfoLine(shown.Outcome != null ? "The fight is over." : "Point at a unit, a tile or an action to scan it.", "muted"));
                return "Scanner";
            }
            var hover = _hover.Value;
            var unit = shown.Units.FirstOrDefault(u => !u.Dead && !u.Docked && u.Pos == hover);
            var objects = shown.Objects.Where(o => o.Pos == hover && !o.Destroyed).ToList();
            var hazard = shown.Hazards.FirstOrDefault(h => h.Tiles.Contains(hover));
            var room = c.RoomFor(shown.Settings.Room);
            string? note = null;
            if (hazard != null)
            {
                var def = c.HazardDefs.TryGetValue(hazard.Def, out var d) ? d : null;
                note = hazard.State == HazardState.Primed ? $"{def?.Name ?? hazard.Def}. {def?.Cue}" : $"{def?.Name ?? hazard.Def}.";
            }
            else if (room.Board.TerrainId(hover) == "shutter") note = "Shutter: impassable; blocks projectiles but not sight.";
            // A tile with no unit on it is named first, as the board's coordinates name it.
            if (unit == null)
            {
                var terrain = room.Terrain.TryGetValue(room.Board.TerrainId(hover), out var t) ? t.Name : "";
                lines.Add(new InfoLine(hazard != null || terrain.Length == 0 ? Tiles.Name(hover) : $"{Tiles.Name(hover)}  {terrain}", "head"));
            }
            if (unit != null)
            {
                lines.Add(new InfoLine($"{Words.UnitName(c, unit.Def, unit.Id)}  {Tiles.Name(unit.Pos)}", "head"));
                lines.Add(new InfoLine(Words.DescribeUnit(unit, shown.Round), "muted"));
                var intent = CanAct ? _model!.Intents?.Intents.FirstOrDefault(i => i.Id == unit.Id) : null;
                if (intent != null) lines.Add(new InfoLine($"Intent: {intent.Label}"));
            }
            foreach (var o in objects) lines.Add(new InfoLine(Words.DescribeObject(c, o), "muted"));
            if (note != null) lines.Add(new InfoLine(note, "muted"));
            return unit != null ? "Scan: unit" : "Scan: tile";
        }

        /// <summary>An action's rule, which tooltips cannot show in a built game: its name and cost, its text, and why it cannot be used.</summary>
        private bool DescribeAction(List<InfoLine> lines, string id)
        {
            var c = _c!;
            var st = _model?.Statuses.FirstOrDefault(x => x.Id == id);
            if (st == null) return false;
            var text = c.Abilities.TryGetValue(id, out var a) ? a.Text : c.Utilities.TryGetValue(id, out var u) ? u.Text : "";
            lines.Add(new InfoLine($"{st.Name}  ·  {st.Ap} AP", "head"));
            if (text.Length > 0) lines.Add(new InfoLine(text));
            if (!st.Usable && st.Reason != null) lines.Add(new InfoLine($"{st.Reason}.", "warn"));
            return true;
        }

        private string PreviewTitle()
        {
            var c = _c!;
            switch (_previewCommand)
            {
                case MoveCommand m:
                    return $"Move to {m.Path[m.Path.Count - 1]}";
                case AbilityCommand a:
                    return $"{(c.Abilities.TryGetValue(a.Ability, out var d) ? d.Name : a.Ability)}{(a.Mode != null ? $" (pull {a.Mode})" : "")} → {a.Target}{(a.Target2 != null ? $" / {a.Target2}" : "")}";
                case ReloadCommand r:
                    return $"{_model!.Selected?.Name ?? ""} → {r.Target}";
                case PickUpMineCommand p:
                    return $"{_model!.Selected?.Name ?? ""} → {p.Target}";
                case RedeployCommand r:
                    return $"{_model!.Selected?.Name ?? ""} → {r.Target}";
                default:
                    return "";
            }
        }

        private void DescribePreview(List<InfoLine> lines, CommandPreview p, GameState shown)
        {
            var c = _c!;
            if (!p.Ok)
            {
                lines.Add(new InfoLine(p.Error ?? "Not allowed", "warn"));
                return;
            }
            lines.Add(new InfoLine(PreviewTitle(), "head"));
            if (p.Hits.Count == 0 && p.Moves.Count == 0 && p.Placed.Count == 0 && p.Spawned.Count == 0) lines.Add(new InfoLine("No effect on any unit.", "muted"));
            foreach (var h in p.Hits)
            {
                var text = $"{(h.Friendly ? "⚠ " : "")}{Words.TargetName(c, h.Ref.Kind, h.Ref.Id, h.Def)}: ";
                if (h.Damage > 0 || h.Absorbed > 0) text += $"{h.Damage} damage{(h.Absorbed != 0 ? $" ({h.Absorbed} absorbed)" : "")}";
                if (h.Healed != 0) text += $" +{h.Healed} HP";
                if (h.Statuses.Count > 0) text += " +" + string.Join(", +", h.Statuses.Select(s => Names.Of(s)));
                if (h.Killed) text += " — dies";
                lines.Add(new InfoLine(text, h.Friendly ? "warn" : ""));
            }
            foreach (var o in p.Placed) lines.Add(new InfoLine(Words.DescribeObject(c, o, true)));
            foreach (var u in p.Spawned) lines.Add(new InfoLine($"{Words.UnitName(c, u.Def, u.Id)} appears at {Tiles.Name(u.Pos)}."));
            foreach (var m in p.Moves)
            {
                if (m.Id == 0)
                {
                    lines.Add(new InfoLine($"You end on {Tiles.Name(m.To)}."));
                    continue;
                }
                var u = p.State?.Units.FirstOrDefault(x => x.Id == m.Id) ?? shown.Units.FirstOrDefault(x => x.Id == m.Id);
                lines.Add(new InfoLine($"{(u != null ? Words.UnitName(c, u.Def, u.Id) : $"Unit {m.Id}")} ends on {Tiles.Name(m.To)}."));
            }
            if (p.MinesTriggered.Count > 0) lines.Add(new InfoLine($"Sets off a mine at {string.Join(", ", p.MinesTriggered.Select(Tiles.Name))}.", "warn"));
            if (p.FriendlyFire) lines.Add(new InfoLine("Friendly fire: your own units are hit.", "warn"));
            if (p.Outcome != null) lines.Add(new InfoLine(p.Outcome.Result == FightResult.Win ? "This wins the fight." : "This kills you.", p.Outcome.Result == FightResult.Win ? "" : "warn"));
            if (p.Intents != null)
            {
                var then = p.Intents.PlayerDies ? "you die" : p.Intents.PlayerDamage > 0 ? $"you take {p.Intents.PlayerDamage}" : "no attacks reach you";
                lines.Add(new InfoLine($"Then, if you end the turn: {then}.", p.Intents.PlayerDies ? "warn" : "muted"));
            }
        }
    }
}
