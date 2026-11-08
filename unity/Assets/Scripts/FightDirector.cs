#nullable enable
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;
using UnityEngine;
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
        private IsoCamera _cam = null!;
        private BoardTags? _tags;
        private BoardView? _board;
        private ActionModel? _model;
        private readonly BoardOverlay _overlay = new BoardOverlay();
        private Pos? _hover;
        private bool _showIntents = true;
        private CommandPreview? _preview;
        private string _previewKey = "";
        private bool _dragging;
        private Vector2 _dragFrom;
        private PresetDef? _preset;
        private FightSettings _settings = new FightSettings();
        private Loadout? _loadout;

        public FightFlow? Flow { get; private set; }

        public Content? Content => _c;

        public ActionModel? Model => _model;

        public BoardView? Board => _board;

        public Hud Hud => _hud;

        public IsoCamera IsoCamera => _cam;

        /// <summary>Set by a scripted run: the tile to treat as under the pointer, instead of reading the mouse.</summary>
        public bool ScriptedPointer { get; set; }

        public bool Idle => Flow != null && Flow.Idle;

        public bool EndScreenShown => _hud.EndVisible;

        private void Awake()
        {
            Instance = this;
            QualitySettings.antiAliasing = 4;
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
            _cam = new IsoCamera();
            var go = new GameObject("HUD");
            go.transform.SetParent(transform, false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = Resources.Load<PanelSettings>("UI/Panel");
            doc.visualTreeAsset = Resources.Load<VisualTreeAsset>("UI/Fight");
            _hud = new Hud(doc);
            WireHud();
            try
            {
                _c = GameContent.Load();
            }
            catch (ContentError e)
            {
                _hud.ShowContentError(e.Message);
                return;
            }
            ShowStartScreen();
            SmokeRun.AttachIfAsked(this);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void WireHud()
        {
            _hud.ActionClicked += SelectAction;
            _hud.GrappleModeClicked += m => _model?.SetGrappleMode(m);
            _hud.UndoClicked += () => TryUndo("Nothing to undo");
            _hud.EndTurnClicked += EndTurn;
            _hud.SkipClicked += () => Flow?.Skip();
            _hud.SpeedClicked += s => Flow?.SetSpeed(s);
            _hud.IntentsClicked += () => _showIntents = !_showIntents;
            _hud.OpenLogClicked += () => SetLog(true);
            _hud.CloseLogClicked += () => SetLog(false);
            _hud.PresetClicked += StartPreset;
            _hud.AgainClicked += Again;
            _hud.ChangeClicked += ShowStartScreen;
        }

        private void ShowStartScreen()
        {
            TearDownFight();
            _hud.ShowStart(_c!.DefaultRoom.Name, _c.Presets, _c);
        }

        private void StartPreset(PresetDef p)
        {
            _preset = p;
            StartFight(new Loadout(p.Abilities, p.Upgrades), new FightSettings(), p.Id);
        }

        private void Again()
        {
            if (_loadout != null) StartFight(_loadout, _settings, _preset?.Id);
        }

        /// <summary>Starts a fight in the room its settings name.</summary>
        public void StartFight(Loadout loadout, FightSettings settings, string? presetId)
        {
            TearDownFight();
            _loadout = loadout;
            _settings = settings;
            var c = _c!;
            _hud.HideStart();
            _hud.ResetFight();
            var meta = new RecordMeta { Label = "unity" };
            if (presetId != null) meta.Preset = presetId;
            Flow = new FightFlow(c, loadout, settings, meta);
            _model = new ActionModel(c);
            _tags = new BoardTags(_hud.TagLayer, c, _cam);
            _board = new BoardView(c, c.RoomFor(settings.Room), _cam, _tags, Flow.Shown);
            Flow.FrameShown += OnFrameShown;
            Flow.Snapped += OnSnapped;
            Flow.Begin();
        }

        private void TearDownFight()
        {
            _board?.Dispose();
            _board = null;
            _tags = null;
            Flow = null;
            _model = null;
            _hover = null;
            _preview = null;
            _previewKey = "";
        }

        private float FxScale => Flow!.Speed == FightSpeed.Fast ? 0.55f : 1f;

        private void OnFrameShown(GameState s, IReadOnlyList<GameEvent> events)
        {
            _board!.Show(s, events, Flow!.FrameSeconds(events), false);
            _board.Fx.Play(events, s, FxScale);
            React(events);
        }

        private void OnSnapped(GameState s)
        {
            _board!.Show(s, Flow!.FrameEvents, 0f, true);
        }

        /// <summary>What a frame's events do to the board as a whole: a shake, the alarm, the red edges when the player is hurt.</summary>
        private void React(IReadOnlyList<GameEvent> events)
        {
            bool? heavy = null;
            var alarm = false;
            var hurt = false;
            foreach (var e in events)
            {
                switch (e)
                {
                    case AreaEffect a when a.Kind == AreaKind.Blast || a.Kind == AreaKind.Burst:
                    case Charged _:
                        heavy = true;
                        break;
                    case HazardFired _:
                        alarm = true;
                        heavy ??= false;
                        break;
                    case WaveSpawned w when w.Wave > 1:
                        alarm = true;
                        _hud.Alert("Reinforcements", 1.6f);
                        break;
                    case DamageDealt d when d.Target.Kind == EntityKind.Unit && d.Target.Id == 0 && d.Amount > 0:
                        hurt = true;
                        heavy ??= false;
                        break;
                }
            }
            if (heavy.HasValue) _cam.Shake(heavy.Value);
            _hud.Flash(alarm, hurt);
        }

        // ------------------------------------------------------------------ player actions

        public void SelectAction(string id)
        {
            if (Flow == null || _model == null || !Flow.Idle) return;
            var a = _model.Statuses.FirstOrDefault(x => x.Id == id);
            if (a == null) return;
            if (!a.Usable)
            {
                _hud.Toast($"{a.Name}: {a.Reason}");
                return;
            }
            if (id == "sprint")
            {
                Commit(new SprintCommand());
                return;
            }
            _model.Select(a);
        }

        public void ClickTile(Pos tile)
        {
            if (Flow == null || _model == null || !Flow.Idle) return;
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
            if (Flow != null && Flow.Idle) Commit(new EndTurnCommand());
        }

        public void SetPointer(Pos? tile) => _hover = tile;

        private void TryUndo(string message)
        {
            if (Flow != null && Flow.Idle && !Flow.Undo()) _hud.Toast(message);
        }

        private void SetLog(bool open)
        {
            if (Flow != null) _hud.SetLogOpen(open, Flow.Shown);
        }

        // ------------------------------------------------------------------ the frame

        private void Update()
        {
            var dt = Time.unscaledDeltaTime;
            if (Flow == null)
            {
                ReadStartKeys();
                return;
            }
            ReadPointer();
            ReadKeys();
            if (Flow == null) return;
            Flow.Tick(dt);
            var live = Flow.Live;
            _model!.Refresh(live, Flow.Idle);
            UpdatePreview();
            BuildOverlay();
            _hud.SetFreeAreaOn(_cam);
            _board!.Tick(dt, _overlay);
            _tags!.Tick(dt, Flow.Shown, _board, _overlay);
            UpdateHud(dt);
        }

        private void ReadStartKeys()
        {
            if (_c == null || !_hud.StartVisible) return;
            for (var i = 0; i < _c.Presets.Count && i < ActionKeys.Length; i++)
            {
                if (Input.GetKeyDown(ActionKeys[i]))
                {
                    StartPreset(_c.Presets[i]);
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

            var inWindow = screen.x >= 0f && screen.y >= 0f && screen.x < Screen.width && screen.y < Screen.height;
            _hover = overUi || !inWindow ? (Pos?)null : _board.PickTile(screen);
            if (Input.GetMouseButtonDown(0) && _hover.HasValue) ClickTile(_hover.Value);
        }

        private void ReadKeys()
        {
            // Modified keys are the system's: Cmd+Q quits, Cmd+1 switches window.
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand) || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return;

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
                if (_hud.LogOpen) SetLog(false);
                else if (_model!.First != null) _model.ClearFirst();
                else _model.Clear();
                return;
            }
            if (Input.GetKeyDown(KeyCode.L))
            {
                SetLog(!_hud.LogOpen);
                return;
            }
            if (_hud.LogOpen) return;
            if (Input.GetKeyDown(KeyCode.Z)) TryUndo("Nothing to undo: AP spent or nothing moved");
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
            var cmd = _hover.HasValue && Flow!.Idle ? _model!.CandidateFor(_hover.Value) : null;
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
            var idle = Flow!.Idle;
            var st = m.Selected;
            _overlay.Highlights.Clear();
            if (idle && st == null) _overlay.Highlights.Add((m.Moves.Select(x => x.Tile).ToList(), HighlightKind.Move));
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
            _overlay.MovePath = idle && st == null && _hover.HasValue ? m.Moves.FirstOrDefault(x => x.Tile == _hover.Value)?.Path : null;
            _overlay.Preview = idle ? _preview : null;
            _overlay.Intents = idle ? m.Intents : null;
            _overlay.ShowIntents = _showIntents;
        }

        private void UpdateHud(float dt)
        {
            var c = _c!;
            var flow = Flow!;
            var m = _model!;
            var shown = flow.Shown;
            var live = flow.Live;
            var idle = flow.Idle;

            _hud.Tick(dt, shown);
            _hud.UpdateStatus(c, shown, idle ? m.Intents : null, flow.Busy);
            _hud.UpdateActions(c, m.Statuses, m.ModeId, !idle, m.GrappleMode);
            var hint = m.Hint(flow);
            _hud.UpdateControls(idle, flow.Session.CanUndo, flow.Busy, hint, flow.Speed, _showIntents);
            _hud.UpdateMinions(c, shown);
            _hud.UpdateLog(flow.Log);
            _hud.UpdateBanner(flow.Banner, live.Outcome != null && !flow.Busy);
            var lines = new List<InfoLine>();
            var title = BuildInfo(lines, hint);
            _hud.UpdateInfo(title, lines);
            if (live.Outcome != null && !flow.Busy && !_hud.EndVisible) _hud.ShowEnd(live.Outcome);
        }

        /// <summary>The scanner panel: the previewed action, or what is under the pointer, or the hint.</summary>
        private string BuildInfo(List<InfoLine> lines, string hint)
        {
            var c = _c!;
            var flow = Flow!;
            var shown = flow.Shown;
            var preview = flow.Idle ? _preview : null;
            if (preview != null)
            {
                DescribePreview(lines, preview, shown);
                return "Preview";
            }
            if (!_hover.HasValue)
            {
                lines.Add(new InfoLine(hint, "muted"));
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
            if (unit != null)
            {
                lines.Add(new InfoLine($"{Words.UnitName(c, unit.Def, unit.Id)}  {Tiles.Name(unit.Pos)}", "head"));
                lines.Add(new InfoLine(Words.DescribeUnit(unit, shown.Round), "muted"));
                var intent = flow.Idle ? _model!.Intents?.Intents.FirstOrDefault(i => i.Id == unit.Id) : null;
                if (intent != null) lines.Add(new InfoLine($"Intent: {intent.Label}"));
            }
            foreach (var o in objects) lines.Add(new InfoLine(Words.DescribeObject(c, o), "muted"));
            if (note != null) lines.Add(new InfoLine(note, "muted"));
            if (lines.Count == 0)
            {
                lines.Add(new InfoLine(hint, "muted"));
                return "Scanner";
            }
            return unit != null ? "Scan: unit" : "Scan: tile";
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
