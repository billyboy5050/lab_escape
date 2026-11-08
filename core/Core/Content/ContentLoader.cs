#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace LabEscape.Core
{
    public sealed class ContentError : Exception
    {
        public IReadOnlyList<string> Problems { get; }

        public ContentError(IReadOnlyList<string> problems)
            : base("Content failed validation:\n  - " + string.Join("\n  - ", problems))
        {
            Problems = problems;
        }
    }

    public static class ContentLoader
    {
        private static readonly string[] DamageTypes = { "kinetic", "explosive", "fire", "electric", "acid", "poison", "parasite" };
        private static readonly string[] StatusIds = { "poison", "parasite", "corrode", "pinned", "slowed" };
        private static readonly string[] DirectionNames = { "N", "E", "S", "W" };

        /// <summary>Parses content JSON from any source: the file system here, TextAssets in Unity.</summary>
        public static ContentFiles Parse(Func<string, string> readFile)
        {
            var root = new JObject();
            foreach (var name in ContentFiles.FileNames) root[name] = JsonIn.Parse(readFile(name));
            return new ContentFiles(root);
        }

        /// <summary>Reads the JSON files in a content/ folder.</summary>
        public static ContentFiles FilesFromDirectory(string directory)
        {
            return Parse(name => File.ReadAllText(Path.Combine(directory, name + ".json")));
        }

        public static Content FromDirectory(string directory) => Build(FilesFromDirectory(directory));

        /// <summary>Validates the raw content files and builds the lookup tables the engine uses. Throws ContentError.</summary>
        public static Content Build(ContentFiles files)
        {
            var problems = Validate(files);
            if (problems.Count > 0) throw new ContentError(problems);
            return Mapper.Map(files);
        }

        /// <summary>Applies dotted-path overrides such as {"units.warden.hp", 16} to a copy of the files, in the order given.</summary>
        public static ContentFiles WithOverrides(ContentFiles files, IEnumerable<KeyValuePair<string, JToken>> overrides)
        {
            var output = files.DeepClone();
            foreach (var pair in overrides)
            {
                var path = pair.Key;
                var parts = path.Split('.');
                JToken? node = output.Root;
                for (var i = 0; i < parts.Length - 1; i++) node = Descend(node!, parts[i], path);
                var last = parts[parts.Length - 1];
                if (node is JArray array)
                {
                    var index = array.ToList().FindIndex(x => x is JObject o && J.StrOf(o["id"]) == last);
                    if (index < 0) throw new ArgumentException($"Override path {path}: no element with id \"{last}\"");
                    array[index] = pair.Value.DeepClone();
                }
                else if (node is JObject obj)
                {
                    if (obj.Property(last) == null) throw new ArgumentException($"Override path {path}: unknown key \"{last}\"");
                    obj[last] = pair.Value.DeepClone();
                }
                else throw new ArgumentException($"Override path {path} does not lead to an object");
            }
            return output;
        }

        private static JToken Descend(JToken node, string key, string path)
        {
            if (node is JArray array)
            {
                var found = array.FirstOrDefault(x => x is JObject o && J.StrOf(o["id"]) == key);
                if (found == null && key.Length > 0 && key.All(c => c >= '0' && c <= '9'))
                {
                    var index = int.Parse(key, CultureInfo.InvariantCulture);
                    if (index < array.Count) found = array[index];
                }
                if (found == null) throw new ArgumentException($"Override path {path}: no element \"{key}\"");
                return found;
            }
            if (node is JObject obj && obj.Property(key) != null) return obj[key]!;
            throw new ArgumentException($"Override path {path}: unknown key \"{key}\"");
        }

        public static bool UpgradeAllowed(RequiresDef requires, IReadOnlyCollection<string> abilities)
        {
            var all = requires.All ?? new List<string>();
            var any = requires.Any ?? new List<string>();
            return all.All(abilities.Contains) && (any.Count == 0 || any.Any(abilities.Contains));
        }

        /// <summary>Returns a list of human-readable problems; empty when the content is valid.</summary>
        public static List<string> Validate(ContentFiles f)
        {
            return new Validator(f).Run();
        }

        private sealed class Validator
        {
            private readonly ContentFiles _f;
            private readonly List<string> _p = new List<string>();

            public Validator(ContentFiles f)
            {
                _f = f;
            }

            private void Int(JToken? v, string where, int min = 0)
            {
                if (!J.IsInteger(v) || J.Number(v!) < min) _p.Add($"{where} must be an integer >= {min} (got {Hash.Stringify(v)})");
            }

            private void DamageTypeOf(JToken? v, string where)
            {
                if (!Contains(DamageTypes, v)) _p.Add($"{where} has unknown damage type {Hash.Stringify(v)}");
            }

            private static bool Contains(string[] names, JToken? v)
            {
                var s = J.StrOf(v);
                return s != null && Array.IndexOf(names, s) >= 0;
            }

            /// <summary>Adds a problem for each dotted path that is absent from an object, for fields the rules code needs.</summary>
            private void RequireFields(JToken? o, string where, OrderedMap<string, string[]> table, string? id)
            {
                if (id != null && table.TryGetValue(id, out var paths)) RequireFields(o, where, paths);
            }

            private void RequireFields(JToken? o, string where, string[] paths)
            {
                foreach (var path in paths)
                {
                    if (J.IsNullish(J.Get(o, path.Split('.')))) _p.Add($"{where}.{path} is missing");
                }
            }

            /// <summary>The shape of an ability or upgrade `status` block, when there is one.</summary>
            private void StatusBlock(JToken? v, string where)
            {
                if (v == null) return;
                var id = J.Get(v, "id");
                if (!Contains(StatusIds, id)) _p.Add($"{where}.status has unknown status {Hash.Stringify(id)}");
                Int(J.Get(v, "duration"), $"{where}.status.duration", 1);
            }

            /// <summary>
            /// A collection the rules read without a fallback. A missing or non-list one is a problem to report, never an empty
            /// list: accepting it would let a bad hot reload through, and the engine would crash on the first read.
            /// </summary>
            private IReadOnlyList<JToken> List(JToken? v, string where)
            {
                if (v is JArray a) return a.ToList();
                _p.Add($"{where} must be a list");
                return Array.Empty<JToken>();
            }

            private static IReadOnlyList<JToken> ListOrEmpty(JToken? v) => v is JArray a ? (IReadOnlyList<JToken>)a.ToList() : Array.Empty<JToken>();

            public List<string> Run()
            {
                var f = _f;
                var p = _p;

                // Rules
                var r = f["rules"];
                if (!J.Truthy(r)) return new List<string> { "rules.json is missing" };
                Int(J.Get(r, "turn", "ap"), "rules.turn.ap");
                Int(J.Get(r, "turn", "movement"), "rules.turn.movement");
                Int(J.Get(r, "sprint", "ap"), "rules.sprint.ap");
                Int(J.Get(r, "sprint", "movement"), "rules.sprint.movement");
                foreach (var k in new[] { "minions", "eggs", "mines", "shieldPlacements" }) Int(J.Get(r, "caps", k), $"rules.caps.{k}");
                Int(J.Get(r, "statuses", "poison", "damage"), "rules.statuses.poison.damage");
                Int(J.Get(r, "statuses", "poison", "duration"), "rules.statuses.poison.duration", 1);
                Int(J.Get(r, "statuses", "poison", "spreadCap"), "rules.statuses.poison.spreadCap");
                DamageTypeOf(J.Get(r, "statuses", "poison", "damageType"), "rules.statuses.poison");
                Int(J.Get(r, "statuses", "parasite", "damage"), "rules.statuses.parasite.damage");
                Int(J.Get(r, "statuses", "parasite", "duration"), "rules.statuses.parasite.duration", 1);
                DamageTypeOf(J.Get(r, "statuses", "parasite", "damageType"), "rules.statuses.parasite");
                Int(J.Get(r, "statuses", "corrode", "duration"), "rules.statuses.corrode.duration");
                Int(J.Get(r, "statuses", "pinned", "duration"), "rules.statuses.pinned.duration", 1);
                Int(J.Get(r, "statuses", "slowed", "duration"), "rules.statuses.slowed.duration", 1);
                Int(J.Get(r, "statuses", "slowed", "movementPenalty"), "rules.statuses.slowed.movementPenalty");
                // Every damage calculation reads this list, so a missing or non-list value must be rejected, not skipped.
                if (J.Get(r, "armorAppliesTo") is JArray armorTypes)
                {
                    for (var i = 0; i < armorTypes.Count; i++) DamageTypeOf(armorTypes[i], $"rules.armorAppliesTo[{i}]");
                }
                else p.Add("rules.armorAppliesTo must be a list of damage types");
                Int(J.Get(r, "corpseDecay"), "rules.corpseDecay", 1);
                Int(J.Get(r, "eggs", "hp"), "rules.eggs.hp", 1);
                Int(J.Get(r, "eggs", "floorTimer"), "rules.eggs.floorTimer", 1);
                Int(J.Get(r, "eggs", "corpseTimer"), "rules.eggs.corpseTimer", 1);
                Int(J.Get(r, "eggs", "floorHatchlings"), "rules.eggs.floorHatchlings");
                Int(J.Get(r, "chainDepthCap"), "rules.chainDepthCap", 1);
                Int(J.Get(r, "roundCap"), "rules.roundCap", 1);
                if (!Contains(new[] { "all", "first", "none" }, J.Get(r, "arrivalRound"))) p.Add("rules.arrivalRound must be all, first or none");
                Int(J.Get(r, "loadout", "abilities"), "rules.loadout.abilities", 1);
                Int(J.Get(r, "loadout", "upgrades"), "rules.loadout.upgrades");
                if (!J.IsBool(J.Get(r, "earlyWaveWhenEmpty"))) p.Add("rules.earlyWaveWhenEmpty must be true or false");
                // The board reads showHazardCooldowns on every render, and every animation frame's delay is secondsPerActor.
                if (!J.IsBool(J.Get(r, "ui", "showHazardCooldowns"))) p.Add("rules.ui.showHazardCooldowns must be true or false");
                var secondsPerActor = J.Get(r, "ui", "secondsPerActor");
                if (!J.IsNumber(secondsPerActor) || !J.IsFinite(secondsPerActor!) || J.Number(secondsPerActor!) < 0)
                {
                    p.Add($"rules.ui.secondsPerActor must be a number of seconds, 0 or more (got {Hash.Stringify(secondsPerActor)})");
                }

                // Units
                var unitIds = new HashSet<string?>(StringComparer.Ordinal);
                foreach (var u in List(f["units"], "units"))
                {
                    var id = J.StrOf(J.Get(u, "id"));
                    var w = $"units.{J.Show(J.Get(u, "id"))}";
                    if (unitIds.Contains(id)) p.Add($"{w} is defined twice");
                    unitIds.Add(id);
                    var team = J.StrOf(J.Get(u, "team"));
                    if (team != "player" && team != "enemy") p.Add($"{w}.team must be player or enemy");
                    if (!Contains(new[] { "player", "minion", "enemy" }, J.Get(u, "kind"))) p.Add($"{w}.kind must be player, minion or enemy");
                    Int(J.Get(u, "hp"), $"{w}.hp", 1);
                    Int(J.Get(u, "armor"), $"{w}.armor");
                    Int(J.Get(u, "move"), $"{w}.move");
                    foreach (var key in new[] { "attack", "melee", "sweep", "charge", "zap", "blast" })
                    {
                        var block = J.Get(u, key);
                        if (!J.Truthy(block)) continue;
                        Int(J.Get(block, "damage"), $"{w}.{key}.damage");
                        DamageTypeOf(J.Get(block, "damageType"), $"{w}.{key}");
                    }
                    if (J.Truthy(J.Get(u, "attack"))) Int(J.Get(u, "attack", "range"), $"{w}.attack.range", 1);
                    if (J.Truthy(J.Get(u, "zap")))
                    {
                        Int(J.Get(u, "zap", "range"), $"{w}.zap.range", 1);
                        Int(J.Get(u, "zap", "chainDamage"), $"{w}.zap.chainDamage");
                    }
                    if (J.Truthy(J.Get(u, "charge")))
                    {
                        Int(J.Get(u, "charge", "minDistance"), $"{w}.charge.minDistance", 2);
                        Int(J.Get(u, "charge", "maxMove"), $"{w}.charge.maxMove", 1);
                    }
                    if (J.Truthy(J.Get(u, "heal")))
                    {
                        Int(J.Get(u, "heal", "amount"), $"{w}.heal.amount", 1);
                        Int(J.Get(u, "heal", "range"), $"{w}.heal.range", 1);
                    }
                    if (J.Truthy(J.Get(u, "sweep"))) Int(J.Get(u, "sweep", "length"), $"{w}.sweep.length", 1);
                    if (J.Truthy(J.Get(u, "blast"))) Int(J.Get(u, "blast", "radius"), $"{w}.blast.radius");
                    if (J.Truthy(J.Get(u, "purge"))) Int(J.Get(u, "purge", "cooldown"), $"{w}.purge.cooldown", 1);
                    if (J.Get(u, "ammo") != null) Int(J.Get(u, "ammo"), $"{w}.ammo");
                    if (J.Get(u, "charges") != null) Int(J.Get(u, "charges"), $"{w}.charges", 1);
                    RequireFields(u, w, RequiredFields.Unit, id);
                    foreach (var pair in RequiredFields.Block)
                    {
                        if (J.Truthy(J.Get(u, pair.Key))) RequireFields(u, w, pair.Value);
                    }
                }
                foreach (var req in new[] { "player", "guard", "medic", "flamer", "warden", "turret", "drone", "hatchling", "spitter", "burster" })
                {
                    if (!unitIds.Contains(req)) p.Add($"units.json is missing \"{req}\"");
                }
                var floorVariant = J.Get(r, "eggs", "floorVariant");
                if (!unitIds.Contains(J.StrOf(floorVariant)) || J.StrOf(floorVariant) == null) p.Add($"rules.eggs.floorVariant \"{J.Show(floorVariant)}\" is not a unit");
                var parasiteHatchling = J.Get(r, "parasiteHatchling");
                if (!unitIds.Contains(J.StrOf(parasiteHatchling)) || J.StrOf(parasiteHatchling) == null) p.Add($"rules.parasiteHatchling \"{J.Show(parasiteHatchling)}\" is not a unit");
                if (J.Get(r, "eggs", "corpseHosts") is JObject corpseHosts)
                {
                    foreach (var host in corpseHosts.Properties())
                    {
                        if (!unitIds.Contains(host.Name)) p.Add($"rules.eggs.corpseHosts key \"{host.Name}\" is not a unit");
                        if (host.Value.Type != JTokenType.Null && !unitIds.Contains(J.StrOf(host.Value))) p.Add($"rules.eggs.corpseHosts.{host.Name} \"{J.Show(host.Value)}\" is not a unit");
                    }
                }
                else p.Add("rules.eggs.corpseHosts must be an object");

                // Abilities and utilities
                var abilityIds = new HashSet<string?>(StringComparer.Ordinal);
                foreach (var a in List(J.Get(f["abilities"], "abilities"), "abilities.abilities"))
                {
                    var id = J.StrOf(J.Get(a, "id"));
                    var w = $"abilities.{J.Show(J.Get(a, "id"))}";
                    abilityIds.Add(id);
                    Int(J.Get(a, "ap"), $"{w}.ap");
                    if (J.Get(a, "damage") != null) Int(J.Get(a, "damage"), $"{w}.damage");
                    if (J.Get(a, "damageType") != null) DamageTypeOf(J.Get(a, "damageType"), w);
                    if (J.Get(a, "targeting", "range") != null) Int(J.Get(a, "targeting", "range"), $"{w}.targeting.range", 1);
                    RequireFields(a, w, RequiredFields.Ability, id);
                    StatusBlock(J.Get(a, "status"), w);
                    var summon = J.Get(a, "summon");
                    if (J.Truthy(summon) && !unitIds.Contains(J.StrOf(summon))) p.Add($"{w}.summon \"{J.Show(summon)}\" is not a unit");
                    if (J.Get(a, "segmentHp") != null) Int(J.Get(a, "segmentHp"), $"{w}.segmentHp", 1);
                    if (J.Truthy(J.Get(a, "area"))) Int(J.Get(a, "area", "radius"), $"{w}.area.radius");
                    if (J.Truthy(J.Get(a, "blast")))
                    {
                        Int(J.Get(a, "blast", "radius"), $"{w}.blast.radius");
                        Int(J.Get(a, "blast", "damage"), $"{w}.blast.damage");
                        DamageTypeOf(J.Get(a, "blast", "damageType"), $"{w}.blast");
                    }
                }
                foreach (var req in new[] { "sidearm", "grapple_hook", "proximity_mine", "auto_turret", "scout_drone", "barrier_shield", "acid_spit", "lunge", "spore_pod", "brood_egg", "parasite" })
                {
                    if (!abilityIds.Contains(req)) p.Add($"abilities.json is missing \"{req}\"");
                }
                var utilities = List(J.Get(f["abilities"], "utilities"), "abilities.utilities");
                var utilityIds = new HashSet<string?>(utilities.Select(u => J.StrOf(J.Get(u, "id"))), StringComparer.Ordinal);
                foreach (var req in RequiredFields.Utilities)
                {
                    if (!utilityIds.Contains(req)) p.Add($"abilities.json is missing utility \"{req}\"");
                }
                // Sprint is defined twice: the engine charges rules.sprint, while the action list shows the utility entry.
                var sprint = utilities.FirstOrDefault(u => J.StrOf(J.Get(u, "id")) == "sprint");
                var ruleSprint = J.Get(r, "sprint");
                if (sprint != null && J.Truthy(ruleSprint))
                {
                    if (!J.StrictEquals(J.Get(sprint, "ap"), J.Get(ruleSprint, "ap"))) p.Add($"utilities.sprint.ap ({J.Show(J.Get(sprint, "ap"))}) must equal rules.sprint.ap ({J.Show(J.Get(ruleSprint, "ap"))})");
                    if (J.Get(sprint, "movement") != null && !J.StrictEquals(J.Get(sprint, "movement"), J.Get(ruleSprint, "movement"))) p.Add($"utilities.sprint.movement ({J.Show(J.Get(sprint, "movement"))}) must equal rules.sprint.movement ({J.Show(J.Get(ruleSprint, "movement"))})");
                }
                foreach (var u in utilities)
                {
                    var id = J.Show(J.Get(u, "id"));
                    Int(J.Get(u, "ap"), $"utilities.{id}.ap");
                    RequireFields(u, $"utilities.{id}", RequiredFields.Utility, J.StrOf(J.Get(u, "id")));
                    var requiresAbility = J.Get(u, "requiresAbility");
                    if (J.Truthy(requiresAbility) && !abilityIds.Contains(J.StrOf(requiresAbility))) p.Add($"utilities.{id}.requiresAbility \"{J.Show(requiresAbility)}\" is not an ability");
                }

                // Upgrades
                var upgradeIds = new HashSet<string?>(StringComparer.Ordinal);
                var upgrades = List(f["upgrades"], "upgrades");
                // Upgrades whose `requires` block is sound, so later checks can ask them what they require.
                var requirementsOk = new HashSet<string?>(StringComparer.Ordinal);
                foreach (var u in upgrades)
                {
                    var id = J.StrOf(J.Get(u, "id"));
                    var shown = J.Show(J.Get(u, "id"));
                    upgradeIds.Add(id);
                    RequireFields(u, $"upgrades.{shown}", RequiredFields.Upgrade, id);
                    StatusBlock(J.Get(u, "status"), $"upgrades.{shown}");
                    // The loadout screen asks every upgrade what it requires, so a missing block would crash it.
                    var req = J.Get(u, "requires");
                    if (!(req is JObject)) p.Add($"upgrades.{shown}.requires must be an object");
                    else
                    {
                        var sound = true;
                        foreach (var k in new[] { "all", "any" })
                        {
                            var names = J.Get(req, k);
                            if (names == null) continue;
                            if (names is JArray list)
                            {
                                foreach (var a in list)
                                {
                                    if (!abilityIds.Contains(J.StrOf(a))) p.Add($"upgrades.{shown} requires unknown ability \"{J.Show(a)}\"");
                                }
                            }
                            else
                            {
                                p.Add($"upgrades.{shown}.requires.{k} must be a list");
                                sound = false;
                            }
                        }
                        if (sound) requirementsOk.Add(id);
                    }
                }

                // Hazards and map
                var hazardDefs = List(f["hazards"], "hazards");
                var hazardIds = new HashSet<string?>(hazardDefs.Select(h => J.StrOf(J.Get(h, "id"))), StringComparer.Ordinal);
                foreach (var h in hazardDefs)
                {
                    var id = J.Show(J.Get(h, "id"));
                    Int(J.Get(h, "damage"), $"hazards.{id}.damage");
                    DamageTypeOf(J.Get(h, "damageType"), $"hazards.{id}");
                    Int(J.Get(h, "cooldown"), $"hazards.{id}.cooldown", 1);
                    Int(J.Get(h, "cancelCooldown"), $"hazards.{id}.cancelCooldown", 1);
                    var shape = J.Get(h, "shape");
                    var shapeName = J.StrOf(shape);
                    if (shapeName != null && RequiredFields.HazardShape.TryGetValue(shapeName, out var shapeFields)) RequireFields(h, $"hazards.{id}", shapeFields);
                    else p.Add($"hazards.{id}.shape must be one of {string.Join(", ", RequiredFields.HazardShape.Keys)} (got {Hash.Stringify(shape)})");
                }
                var m = f["map"];
                if (!J.Truthy(m))
                {
                    p.Add("map.json is missing");
                    return p;
                }
                var width = J.Get(m, "width");
                var height = J.Get(m, "height");
                var rows = ListOrEmpty(J.Get(m, "rows"));
                if (!J.StrictEquals(new JValue(rows.Count), height)) p.Add($"map has {rows.Count} rows but height {J.Show(height)}");
                var legend = J.Get(m, "legend");
                var terrain = J.Get(m, "terrain");
                for (var y = 0; y < rows.Count; y++)
                {
                    var row = J.StrOf(rows[y]) ?? "";
                    if (!J.StrictEquals(new JValue(row.Length), width)) p.Add($"map row {y + 1} has {row.Length} tiles but width {J.Show(width)}");
                    foreach (var ch in row)
                    {
                        var key = ch.ToString();
                        var t = J.StrOf(J.Get(legend, key));
                        if (string.IsNullOrEmpty(t)) p.Add($"map legend has no entry for \"{key}\"");
                        else if (!J.Truthy(J.Get(terrain, t!))) p.Add($"map terrain has no definition for \"{t}\"");
                    }
                }
                void TileOk(JToken? name, string where)
                {
                    var text = J.StrOf(name);
                    if (text == null || !Tiles.IsName(text))
                    {
                        p.Add($"{where} \"{J.Show(name)}\" is not a tile name");
                        return;
                    }
                    var tile = Tiles.Parse(text);
                    if (tile.X < 0 || tile.Y < 0 || !J.IsNumber(width) || tile.X >= J.Number(width!) || !J.IsNumber(height) || tile.Y >= J.Number(height!)) p.Add($"{where} \"{text}\" is off the map");
                }
                TileOk(J.Get(m, "playerStart"), "map.playerStart");
                foreach (var hz in List(J.Get(m, "hazards"), "map.hazards"))
                {
                    var hzId = J.Show(J.Get(hz, "id"));
                    var defName = J.Get(hz, "def");
                    if (!hazardIds.Contains(J.StrOf(defName)) || J.StrOf(defName) == null) p.Add($"map hazard {hzId} uses unknown hazard def \"{J.Show(defName)}\"");
                    // Every hazard is placed, drawn and (for a lane) fired from its first tile, so one with none would crash the board.
                    if (J.Get(hz, "tiles") is JArray tiles && tiles.Count > 0)
                    {
                        foreach (var t in tiles) TileOk(t, $"map hazard {hzId} tile");
                    }
                    else p.Add($"map hazard {hzId} needs at least one tile");
                    // A direction that is not N, E, S or W would resolve to nothing, and the first lane shot would crash on it.
                    var direction = J.Get(hz, "direction");
                    if (J.Truthy(direction) && !Contains(DirectionNames, direction)) p.Add($"map hazard {hzId} has unknown direction {Hash.Stringify(direction)} (use N, E, S or W)");
                    var hazardDef = hazardDefs.FirstOrDefault(h => J.StrictEquals(J.Get(h, "id"), defName));
                    if (hazardDef != null && J.StrOf(J.Get(hazardDef, "shape")) == "lane" && !J.Truthy(direction)) p.Add($"map hazard {hzId} is a lane and needs a direction");
                }

                // Waves
                var lastRound = 0.0;
                var waves = ListOrEmpty(f["waves"]);
                for (var i = 0; i < waves.Count; i++)
                {
                    var wave = waves[i];
                    var round = J.Get(wave, "round");
                    Int(round, $"waves[{i}].round", 1);
                    if (J.IsNumber(round))
                    {
                        if (J.Number(round!) < lastRound) p.Add($"waves[{i}] arrives before the wave listed above it");
                        lastRound = J.Number(round!);
                    }
                    foreach (var u in ListOrEmpty(J.Get(wave, "units")))
                    {
                        var unit = J.StrOf(J.Get(u, "def"));
                        if (unit == null || !unitIds.Contains(unit)) p.Add($"waves[{i}] uses unknown unit \"{J.Show(J.Get(u, "def"))}\"");
                        TileOk(J.Get(u, "tile"), $"waves[{i}] spawn tile");
                    }
                }
                if (waves.Count == 0) p.Add("waves.json has no waves");

                // Intercom
                var lineIds = new HashSet<string?>(StringComparer.Ordinal);
                foreach (var l in List(J.Get(f["intercom"], "lines"), "intercom.lines"))
                {
                    var id = J.StrOf(J.Get(l, "id"));
                    var shown = J.Show(J.Get(l, "id"));
                    if (lineIds.Contains(id)) p.Add($"intercom line \"{shown}\" is defined twice");
                    lineIds.Add(id);
                    var def = J.Get(l, "trigger", "def");
                    if (J.Truthy(def) && !unitIds.Contains(J.StrOf(def))) p.Add($"intercom line \"{shown}\" names unknown unit \"{J.Show(def)}\"");
                }

                // Presets
                foreach (var pr in List(f["presets"], "presets"))
                {
                    var prId = J.Show(J.Get(pr, "id"));
                    var abilities = ListOrEmpty(J.Get(pr, "abilities"));
                    var chosenUpgrades = ListOrEmpty(J.Get(pr, "upgrades"));
                    foreach (var a in abilities)
                    {
                        if (!abilityIds.Contains(J.StrOf(a))) p.Add($"preset {prId} uses unknown ability \"{J.Show(a)}\"");
                    }
                    var abilityNames = abilities.Select(J.StrOf).Where(x => x != null).Select(x => x!).ToList();
                    foreach (var u in chosenUpgrades)
                    {
                        var upgradeId = J.StrOf(u);
                        if (!upgradeIds.Contains(upgradeId)) p.Add($"preset {prId} uses unknown upgrade \"{J.Show(u)}\"");
                        var def = upgrades.FirstOrDefault(x => J.StrOf(J.Get(x, "id")) == upgradeId);
                        if (def != null && requirementsOk.Contains(upgradeId) && !ContentLoader.UpgradeAllowed(Mapper.Requires(J.Get(def, "requires")!), abilityNames))
                        {
                            p.Add($"preset {prId} takes {J.Show(u)} without its required abilities");
                        }
                    }
                    var maxAbilities = J.Get(r, "loadout", "abilities");
                    var maxUpgrades = J.Get(r, "loadout", "upgrades");
                    if (J.IsNumber(maxAbilities) && abilities.Count > J.Number(maxAbilities!)) p.Add($"preset {prId} has more than {J.Show(maxAbilities)} abilities");
                    if (J.IsNumber(maxUpgrades) && chosenUpgrades.Count > J.Number(maxUpgrades!)) p.Add($"preset {prId} has more than {J.Show(maxUpgrades)} upgrades");
                }
                return p;
            }
        }
    }

    /// <summary>JavaScript-style questions about a parsed JSON value. A C# null means the value is absent (undefined).</summary>
    internal static class J
    {
        public static JToken? Get(JToken? token, params string[] path)
        {
            var node = token;
            foreach (var key in path)
            {
                if (!(node is JObject o)) return null;
                node = o[key];
            }
            return node;
        }

        public static bool IsNullish(JToken? t) => t == null || t.Type == JTokenType.Null;
        public static bool IsBool(JToken? t) => t != null && t.Type == JTokenType.Boolean;
        public static bool IsNumber(JToken? t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float);
        public static double Number(JToken t) => Hash.ToDouble((JValue)t);
        public static bool IsFinite(JToken t) => !double.IsNaN(Number(t)) && !double.IsInfinity(Number(t));
        public static bool IsInteger(JToken? t) => IsNumber(t) && IsFinite(t!) && Math.Floor(Number(t!)) == Number(t!);
        public static string? StrOf(JToken? t) => t != null && t.Type == JTokenType.String ? t.Value<string>() : null;

        /// <summary>What a JavaScript template literal prints for the value.</summary>
        public static string Show(JToken? t)
        {
            if (t == null) return "undefined";
            if (t.Type == JTokenType.String) return t.Value<string>()!;
            return Hash.Stringify(t);
        }

        public static bool Truthy(JToken? t)
        {
            if (t == null) return false;
            switch (t.Type)
            {
                case JTokenType.Null: return false;
                case JTokenType.Boolean: return t.Value<bool>();
                case JTokenType.String: return t.Value<string>()!.Length > 0;
                case JTokenType.Integer:
                case JTokenType.Float: return Number(t) != 0 && !double.IsNaN(Number(t));
                default: return true;
            }
        }

        /// <summary>The === operator on JSON values.</summary>
        public static bool StrictEquals(JToken? a, JToken? b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (IsNumber(a) && IsNumber(b)) return Number(a) == Number(b);
            if (a.Type != b.Type) return false;
            switch (a.Type)
            {
                case JTokenType.String: return a.Value<string>() == b.Value<string>();
                case JTokenType.Boolean: return a.Value<bool>() == b.Value<bool>();
                case JTokenType.Null: return true;
                default: return ReferenceEquals(a, b);
            }
        }
    }
}
