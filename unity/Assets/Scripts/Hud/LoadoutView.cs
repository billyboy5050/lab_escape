#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;
using UnityEngine.UIElements;

namespace LabEscape.Client
{
    /// <summary>
    /// The loadout screen's picks, drawn: the presets, an ability and an upgrade to a row, and the dock with what is wrong and the
    /// button to go on. It decides nothing: a press is an event, and <see cref="LoadoutPicker"/> says what may be picked.
    /// </summary>
    public sealed class LoadoutView
    {
        private readonly VisualElement _presets, _abilities, _upgrades, _dock;
        private readonly Label _abilityCount, _upgradeCount, _utilities, _status, _counts, _testerNote;
        private readonly Button _hold, _start;
        private readonly ScrollView _body;
        private readonly Dictionary<string, Button> _presetButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> _abilityRows = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> _upgradeRows = new Dictionary<string, Button>();
        private readonly Dictionary<string, Label> _upgradeNeeds = new Dictionary<string, Label>();
        private readonly Dictionary<string, VisualElement> _abilityChecks = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, VisualElement> _upgradeChecks = new Dictionary<string, VisualElement>();

        public event Action<PresetDef>? PresetClicked;
        public event Action<string>? AbilityToggled, UpgradeToggled;
        public event Action? ClearClicked, HoldWaveClicked, StartClicked, BackClicked;

        /// <summary>Any button here was pressed: the interface's click sound.</summary>
        public event Action? ButtonClicked;

        public LoadoutView(VisualElement root)
        {
            T Q<T>(string name) where T : VisualElement => root.Q<T>(name) ?? throw new InvalidOperationException($"Fight.uxml has no {name}");
            _presets = Q<VisualElement>("presets");
            _abilities = Q<VisualElement>("abilities");
            _upgrades = Q<VisualElement>("upgrades");
            _dock = Q<VisualElement>("start-dock");
            _body = Q<ScrollView>("loadout-body");
            _abilityCount = Q<Label>("ability-count");
            _upgradeCount = Q<Label>("upgrade-count");
            _utilities = Q<Label>("utilities");
            _status = Q<Label>("dock-status");
            _counts = Q<Label>("dock-counts");
            _testerNote = Q<Label>("tester-note");
            _hold = Q<Button>("hold-wave");
            _start = Q<Button>("start-fight");
            _hold.focusable = false;
            _start.focusable = false;
            _hold.clicked += () => Press(HoldWaveClicked);
            _start.clicked += () => Press(StartClicked);
            var back = Q<Button>("back-title");
            back.focusable = false;
            back.clicked += () => Press(BackClicked);
        }

        private void Press(Action? act)
        {
            ButtonClicked?.Invoke();
            act?.Invoke();
        }

        /// <summary>The body and the dock are for a game that has content; without it the screen only says why.</summary>
        public void Show(bool on)
        {
            Ui.Show(_body, on);
            Ui.Show(_dock, on);
        }

        /// <summary>Makes a row for every preset, ability and upgrade the content has. `tester` is the playtest's ID, if there is one.</summary>
        public void Build(Content c, string? tester)
        {
            _presets.Clear();
            _presetButtons.Clear();
            var i = 1;
            foreach (var p in c.Presets)
            {
                var preset = p;
                var b = new Button(() => Press(() => PresetClicked?.Invoke(preset))) { focusable = false, text = "" };
                b.AddToClassList("preset-btn");
                b.Add(Ui.Text($"{i}", "preset-key mono"));
                b.Add(Ui.Text(Fonts.Caps(p.Name), "preset-name display"));
                _presets.Add(b);
                _presetButtons[p.Id] = b;
                i++;
            }
            var clear = new Button(() => Press(ClearClicked)) { focusable = false, text = "" };
            clear.AddToClassList("preset-btn");
            clear.Add(Ui.Text(Fonts.Caps("Clear"), "preset-name display"));
            _presets.Add(clear);

            _abilities.Clear();
            _abilityRows.Clear();
            _abilityChecks.Clear();
            foreach (var id in c.AbilityOrder)
            {
                var a = c.Abilities[id];
                var captured = id;
                var row = Row(a.Kit, out var check);
                row.clicked += () => Press(() => AbilityToggled?.Invoke(captured));
                var text = Ui.El("choice-text");
                var head = Ui.El("choice-head");
                head.Add(Ui.Text(Fonts.Caps(a.Name), "choice-name display"));
                head.Add(Ui.Text(a.Kit, $"kit-tag mono kit-{a.Kit}"));
                text.Add(head);
                text.Add(Ui.Text(a.Text, "choice-rule"));
                row.Add(text);
                row.Add(Ui.Text($"{a.Ap} AP", "choice-cost mono"));
                _abilities.Add(row);
                _abilityRows[id] = row;
                _abilityChecks[id] = check;
            }

            _upgrades.Clear();
            _upgradeRows.Clear();
            _upgradeNeeds.Clear();
            _upgradeChecks.Clear();
            foreach (var id in c.UpgradeOrder)
            {
                var u = c.Upgrades[id];
                var captured = id;
                var row = Row(u.Kit, out var check);
                row.clicked += () => Press(() => UpgradeToggled?.Invoke(captured));
                var text = Ui.El("choice-text");
                var head = Ui.El("choice-head");
                head.Add(Ui.Text(Fonts.Caps(u.Name), "choice-name display"));
                head.Add(Ui.Text(u.Code, $"kit-tag mono kit-{u.Kit}"));
                text.Add(head);
                text.Add(Ui.Text(u.Text, "choice-rule"));
                var needs = Ui.Text("", "choice-rule choice-needs");
                text.Add(needs);
                row.Add(text);
                _upgrades.Add(row);
                _upgradeRows[id] = row;
                _upgradeNeeds[id] = needs;
                _upgradeChecks[id] = check;
            }

            _testerNote.text = tester != null
                ? $"Tester ID {tester}: fights are labelled with it and an attempt number."
                : "No tester ID. Give one with -labTester, or ?tester= in the web build.";
        }

        private static Button Row(string kit, out VisualElement check)
        {
            var row = new Button { focusable = false, text = "" };
            row.AddToClassList("choice");
            row.AddToClassList($"kit-{kit}");
            check = Ui.El("choice-check");
            row.Add(check);
            return row;
        }

        /// <summary>Marks what is picked, takes off what cannot be, and fills in the dock.</summary>
        public void Update(Content c, LoadoutPicker p)
        {
            foreach (var pair in _presetButtons) pair.Value.EnableInClassList("on", p.PresetId == pair.Key);
            foreach (var pair in _abilityRows)
            {
                var on = p.AbilityOn(pair.Key);
                pair.Value.EnableInClassList("on", on);
                _abilityChecks[pair.Key].EnableInClassList("on", on);
                pair.Value.SetEnabled(p.CanToggleAbility(pair.Key));
            }
            foreach (var pair in _upgradeRows)
            {
                var on = p.UpgradeOn(pair.Key);
                var allowed = p.UpgradeAllowed(pair.Key);
                pair.Value.EnableInClassList("on", on);
                _upgradeChecks[pair.Key].EnableInClassList("on", on);
                pair.Value.SetEnabled(p.CanToggleUpgrade(pair.Key));
                _upgradeNeeds[pair.Key].text = allowed || on ? "" : $"Needs {p.NeedsOf(pair.Key)}";
                Ui.Show(_upgradeNeeds[pair.Key], !allowed && !on);
            }
            _abilityCount.text = $"{p.Abilities.Count}/{p.MaxAbilities}";
            _upgradeCount.text = $"{p.Upgrades.Count}/{p.MaxUpgrades}";
            _utilities.text = p.UtilitiesLine();
            _hold.EnableInClassList("on", p.HoldWave3);
            _counts.text = $"Abilities {p.Abilities.Count}/{p.MaxAbilities} · upgrades {p.Upgrades.Count}/{p.MaxUpgrades}";
            var problems = p.Problems();
            _status.text = p.Status();
            _status.EnableInClassList("warn", problems.Count > 0);
            _start.SetEnabled(p.CanStart);
        }

        public bool CanStartEnabled => _start.enabledSelf;
    }
}
