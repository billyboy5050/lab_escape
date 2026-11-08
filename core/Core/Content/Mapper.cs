#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace LabEscape.Core
{
    /// <summary>
    /// Turns validated JSON into the typed definitions. Written out field by field instead of using JSON.NET's reflection,
    /// because reflection needs extra configuration to survive code stripping in Unity's web build.
    /// </summary>
    internal static class Mapper
    {
        public static Content Map(ContentFiles files)
        {
            var units = Table(files["units"]!, Unit);
            var abilities = Table(J.Get(files["abilities"], "abilities")!, Ability);
            var utilities = Table(J.Get(files["abilities"], "utilities")!, Utility);
            var upgrades = Table(files["upgrades"]!, Upgrade);
            var hazards = Table(files["hazards"]!, Hazard);
            var armory = RoomOf(files["map"]!, files["waves"]!, Int(files["rules"], "roundCap"));
            var rooms = new OrderedMap<string, RoomDef>(StringComparer.Ordinal);
            rooms[armory.Id] = armory;
            if (files.Root.Property("rooms") != null)
            {
                foreach (var file in (JArray)files["rooms"]!)
                {
                    var resolved = ContentLoader.ResolveRoom(files["map"]!, file);
                    var room = RoomOf(resolved, J.Get(resolved, "waves")!, Int(resolved, "roundCap"));
                    rooms[room.Id] = room;
                }
            }
            return new Content
            {
                Files = files,
                Rules = Rules(files["rules"]!),
                Units = units,
                Abilities = abilities,
                AbilityOrder = ((JArray)J.Get(files["abilities"], "abilities")!).Select(a => Str(a, "id")).ToList(),
                Utilities = utilities,
                Upgrades = upgrades,
                UpgradeOrder = ((JArray)files["upgrades"]!).Select(u => Str(u, "id")).ToList(),
                HazardDefs = hazards,
                Rooms = rooms,
                DefaultRoom = armory,
                Intercom = Intercom(files["intercom"]!),
                Presets = ((JArray)files["presets"]!).Select(Preset).ToList(),
                Hash = Hash.HashJson(files.Root),
            };
        }

        private static OrderedMap<string, T> Table<T>(JToken list, Func<JToken, T> map)
        {
            var table = new OrderedMap<string, T>(StringComparer.Ordinal);
            foreach (var item in (JArray)list) table[Str(item, "id")] = map(item);
            return table;
        }

        private static ContentError Bad(string message) => new ContentError(new[] { message });

        private static int Int(JToken? o, params string[] path)
        {
            var v = J.Get(o, path);
            if (!J.IsInteger(v)) throw Bad($"{string.Join(".", path)} must be a whole number (got {Hash.Stringify(v)})");
            return checked((int)J.Number(v!));
        }

        private static int? OptInt(JToken? o, string key) => J.IsNullish(J.Get(o, key)) ? (int?)null : Int(o, key);

        private static string Str(JToken? o, params string[] path)
        {
            var v = J.StrOf(J.Get(o, path));
            if (v == null) throw Bad($"{string.Join(".", path)} must be text");
            return v;
        }

        private static string? OptStr(JToken? o, string key) => J.StrOf(J.Get(o, key));
        private static bool Flag(JToken? o, string key) => J.Truthy(J.Get(o, key));

        private static T Enum<T>(JToken? o, params string[] path) where T : struct, Enum
        {
            var name = Str(o, path);
            if (!Names.IsName<T>(name)) throw Bad($"{string.Join(".", path)} has unknown value \"{name}\"");
            return Names.Parse<T>(name);
        }

        private static T? OptEnum<T>(JToken? o, string key) where T : struct, Enum => J.IsNullish(J.Get(o, key)) ? (T?)null : Enum<T>(o, key);

        private static T? Block<T>(JToken? o, string key, Func<JToken, T> map) where T : class
        {
            var v = J.Get(o, key);
            return J.Truthy(v) ? map(v!) : null;
        }

        private static List<string> StrList(JToken? t) => ((JArray)t!).Select(x => x.Value<string>()!).ToList();

        private static RulesDef Rules(JToken r)
        {
            var eggs = J.Get(r, "eggs");
            var hosts = new OrderedMap<string, string?>(StringComparer.Ordinal);
            foreach (var host in ((JObject)J.Get(eggs, "corpseHosts")!).Properties()) hosts[host.Name] = J.StrOf(host.Value);
            return new RulesDef
            {
                Turn = new ApMovement { Ap = Int(r, "turn", "ap"), Movement = Int(r, "turn", "movement") },
                Sprint = new ApMovement { Ap = Int(r, "sprint", "ap"), Movement = Int(r, "sprint", "movement") },
                Caps = new CapsDef
                {
                    Minions = Int(r, "caps", "minions"),
                    Eggs = Int(r, "caps", "eggs"),
                    Mines = Int(r, "caps", "mines"),
                    ShieldPlacements = Int(r, "caps", "shieldPlacements"),
                },
                Statuses = new StatusesDef
                {
                    Poison = new PoisonDef
                    {
                        Damage = Int(r, "statuses", "poison", "damage"),
                        Duration = Int(r, "statuses", "poison", "duration"),
                        SpreadCap = Int(r, "statuses", "poison", "spreadCap"),
                        DamageType = Enum<DamageType>(r, "statuses", "poison", "damageType"),
                    },
                    Parasite = new ParasiteDef
                    {
                        Damage = Int(r, "statuses", "parasite", "damage"),
                        Duration = Int(r, "statuses", "parasite", "duration"),
                        DamageType = Enum<DamageType>(r, "statuses", "parasite", "damageType"),
                    },
                    CorrodeDuration = Int(r, "statuses", "corrode", "duration"),
                    PinnedDuration = Int(r, "statuses", "pinned", "duration"),
                    Slowed = new SlowedDef
                    {
                        Duration = Int(r, "statuses", "slowed", "duration"),
                        MovementPenalty = Int(r, "statuses", "slowed", "movementPenalty"),
                    },
                },
                ArmorAppliesTo = ((JArray)J.Get(r, "armorAppliesTo")!).Select(d => Names.Parse<DamageType>(d.Value<string>()!)).ToList(),
                CorpseDecay = Int(r, "corpseDecay"),
                Eggs = new EggsDef
                {
                    Hp = Int(eggs, "hp"),
                    FloorTimer = Int(eggs, "floorTimer"),
                    CorpseTimer = Int(eggs, "corpseTimer"),
                    FloorHatchlings = Int(eggs, "floorHatchlings"),
                    FloorVariant = Str(eggs, "floorVariant"),
                    CorpseHosts = hosts,
                },
                ParasiteHatchling = Str(r, "parasiteHatchling"),
                ChainDepthCap = Int(r, "chainDepthCap"),
                RoundCap = Int(r, "roundCap"),
                ArrivalRound = Enum<ArrivalRound>(r, "arrivalRound"),
                EarlyWaveWhenEmpty = J.Get(r, "earlyWaveWhenEmpty")!.Value<bool>(),
                Loadout = new LoadoutLimits { Abilities = Int(r, "loadout", "abilities"), Upgrades = Int(r, "loadout", "upgrades") },
                Ui = new UiDef
                {
                    ShowHazardCooldowns = J.Get(r, "ui", "showHazardCooldowns")!.Value<bool>(),
                    SecondsPerActor = J.Number(J.Get(r, "ui", "secondsPerActor")!),
                },
            };
        }

        private static AttackDef Attack(JToken t) => new AttackDef { Range = Int(t, "range"), Damage = Int(t, "damage"), DamageType = Enum<DamageType>(t, "damageType") };
        private static HitDef HitOf(JToken t) => new HitDef { Damage = Int(t, "damage"), DamageType = Enum<DamageType>(t, "damageType") };
        private static BlastDef BlastOf(JToken t) => new BlastDef { Radius = Int(t, "radius"), Damage = Int(t, "damage"), DamageType = Enum<DamageType>(t, "damageType") };
        private static StatusDef StatusOf(JToken t) => new StatusDef { Id = Enum<StatusId>(t, "id"), Duration = Int(t, "duration") };
        private static AreaDef AreaOf(JToken t) => new AreaDef { Shape = OptStr(t, "shape") ?? "", Radius = Int(t, "radius") };

        private static UnitDef Unit(JToken u)
        {
            OrderedMap<string, int>? ai = null;
            if (J.Get(u, "ai") is JObject aiBlock)
            {
                ai = new OrderedMap<string, int>(StringComparer.Ordinal);
                foreach (var pair in aiBlock.Properties()) ai[pair.Name] = Int(aiBlock, pair.Name);
            }
            return new UnitDef
            {
                Id = Str(u, "id"),
                Name = OptStr(u, "name") ?? "",
                Team = Enum<Team>(u, "team"),
                Kind = Enum<UnitKind>(u, "kind"),
                Role = OptStr(u, "role"),
                Elite = Flag(u, "elite"),
                Hatchling = Flag(u, "hatchling"),
                Flying = Flag(u, "flying"),
                Hp = Int(u, "hp"),
                Armor = Int(u, "armor"),
                Move = Int(u, "move"),
                Attack = Block(u, "attack", Attack),
                Melee = Block(u, "melee", HitOf),
                Heal = Block(u, "heal", t => new HealDef { Amount = Int(t, "amount"), Range = Int(t, "range") }),
                Sweep = Block(u, "sweep", t => new SweepDef { Length = Int(t, "length"), Damage = Int(t, "damage"), DamageType = Enum<DamageType>(t, "damageType") }),
                Charge = Block(u, "charge", t => new ChargeDef { MinDistance = Int(t, "minDistance"), MaxMove = Int(t, "maxMove"), Damage = Int(t, "damage"), DamageType = Enum<DamageType>(t, "damageType") }),
                Purge = Block(u, "purge", t => new PurgeDef { Cooldown = Int(t, "cooldown") }),
                Ammo = OptInt(u, "ammo"),
                Charges = OptInt(u, "charges"),
                Zap = Block(u, "zap", t => new ZapDef { Range = Int(t, "range"), Damage = Int(t, "damage"), ChainDamage = Int(t, "chainDamage"), DamageType = Enum<DamageType>(t, "damageType") }),
                ReturnMove = OptInt(u, "returnMove"),
                ReturnPhases = OptInt(u, "returnPhases"),
                Blast = Block(u, "blast", BlastOf),
                Ai = ai,
            };
        }

        private static AbilityDef Ability(JToken a) => new AbilityDef
        {
            Id = Str(a, "id"),
            Name = OptStr(a, "name") ?? "",
            Kit = OptStr(a, "kit") ?? "",
            Ap = Int(a, "ap"),
            Targeting = new TargetingDef { Kind = OptStr(J.Get(a, "targeting"), "kind") ?? "", Range = OptInt(J.Get(a, "targeting"), "range") },
            Damage = OptInt(a, "damage"),
            DamageType = OptEnum<DamageType>(a, "damageType"),
            Corrode = OptInt(a, "corrode"),
            Blast = Block(a, "blast", BlastOf),
            Area = Block(a, "area", AreaOf),
            Status = Block(a, "status", StatusOf),
            Summon = OptStr(a, "summon"),
            SegmentHp = OptInt(a, "segmentHp"),
            Tags = J.Get(a, "tags") is JArray tags ? tags.Select(x => x.Value<string>()!).ToList() : new List<string>(),
            Text = OptStr(a, "text") ?? "",
        };

        private static UtilityDef Utility(JToken u) => new UtilityDef
        {
            Id = Str(u, "id"),
            Name = OptStr(u, "name") ?? "",
            Ap = Int(u, "ap"),
            Movement = OptInt(u, "movement"),
            Range = OptInt(u, "range"),
            RequiresAbility = OptStr(u, "requiresAbility"),
            Text = OptStr(u, "text") ?? "",
        };

        internal static RequiresDef Requires(JToken r) => new RequiresDef
        {
            All = J.Get(r, "all") is JArray all ? all.Select(x => J.StrOf(x) ?? "").ToList() : null,
            Any = J.Get(r, "any") is JArray any ? any.Select(x => J.StrOf(x) ?? "").ToList() : null,
        };

        private static UpgradeDef Upgrade(JToken u) => new UpgradeDef
        {
            Id = Str(u, "id"),
            Code = OptStr(u, "code") ?? "",
            Name = OptStr(u, "name") ?? "",
            Kit = OptStr(u, "kit") ?? "",
            Requires = Requires(J.Get(u, "requires")!),
            Hook = OptStr(u, "hook"),
            ArmorIgnore = OptInt(u, "armorIgnore"),
            Damage = OptInt(u, "damage"),
            DamageType = OptEnum<DamageType>(u, "damageType"),
            Status = Block(u, "status", StatusOf),
            Area = Block(u, "area", AreaOf),
            Corrode = OptInt(u, "corrode"),
            Range = OptInt(u, "range"),
            Charges = OptInt(u, "charges"),
            ExtraChainHops = OptInt(u, "extraChainHops"),
            CorpseTimer = OptInt(u, "corpseTimer"),
            Text = OptStr(u, "text") ?? "",
        };

        private static HazardDef Hazard(JToken h) => new HazardDef
        {
            Id = Str(h, "id"),
            Name = OptStr(h, "name") ?? "",
            Shape = Enum<HazardShape>(h, "shape"),
            Radius = OptInt(h, "radius"),
            Damage = Int(h, "damage"),
            DamageType = Enum<DamageType>(h, "damageType"),
            Cooldown = Int(h, "cooldown"),
            CancelCooldown = Int(h, "cancelCooldown"),
            Cue = OptStr(h, "cue") ?? "",
        };

        private static RoomDef RoomOf(JToken m, JToken waves, int roundCap)
        {
            var legend = new OrderedMap<string, string>(StringComparer.Ordinal);
            foreach (var pair in ((JObject)J.Get(m, "legend")!).Properties()) legend[pair.Name] = pair.Value.Value<string>()!;
            var terrain = new OrderedMap<string, TerrainDef>(StringComparer.Ordinal);
            foreach (var pair in ((JObject)J.Get(m, "terrain")!).Properties())
            {
                terrain[pair.Name] = new TerrainDef
                {
                    Name = OptStr(pair.Value, "name") ?? "",
                    BlocksMove = Flag(pair.Value, "blocksMove"),
                    BlocksSight = Flag(pair.Value, "blocksSight"),
                    BlocksProjectile = Flag(pair.Value, "blocksProjectile"),
                };
            }
            var room = new RoomDef
            {
                Id = OptStr(m, "id") ?? "",
                Name = OptStr(m, "name") ?? "",
                Width = Int(m, "width"),
                Height = Int(m, "height"),
                Rows = StrList(J.Get(m, "rows")),
                Legend = legend,
                Terrain = terrain,
                PlayerStart = Str(m, "playerStart"),
                Hazards = ((JArray)J.Get(m, "hazards")!).Select(h => new MapHazard
                {
                    Id = Str(h, "id"),
                    Def = Str(h, "def"),
                    Tiles = StrList(J.Get(h, "tiles")),
                    Direction = OptStr(h, "direction"),
                }).ToList(),
                Waves = ((JArray)waves).Select(Wave).ToList(),
                RoundCap = roundCap,
            };
            room.Board = new Board(room);
            return room;
        }

        private static WaveDef Wave(JToken w) => new WaveDef
        {
            Round = Int(w, "round"),
            Units = ((JArray)J.Get(w, "units")!).Select(u => new WaveUnit { Def = Str(u, "def"), Tile = Str(u, "tile") }).ToList(),
        };

        private static IntercomDef Intercom(JToken i) => new IntercomDef
        {
            Lexicon = J.Get(i, "lexicon") is JArray lexicon ? lexicon.Select(e => new LexiconEntry { Code = OptStr(e, "code") ?? "", Means = OptStr(e, "means") ?? "" }).ToList() : new List<LexiconEntry>(),
            Lines = ((JArray)J.Get(i, "lines")!).Select(l => new IntercomLineDef
            {
                Id = Str(l, "id"),
                Trigger = new IntercomTrigger
                {
                    Type = OptStr(J.Get(l, "trigger"), "type") ?? "",
                    Round = OptInt(J.Get(l, "trigger"), "round"),
                    Def = OptStr(J.Get(l, "trigger"), "def"),
                    Threshold = OptInt(J.Get(l, "trigger"), "threshold"),
                },
                Text = OptStr(l, "text") ?? "",
                Immediate = Flag(l, "immediate"),
            }).ToList(),
        };

        private static PresetDef Preset(JToken p) => new PresetDef
        {
            Id = Str(p, "id"),
            Name = OptStr(p, "name") ?? "",
            Abilities = StrList(J.Get(p, "abilities")),
            Upgrades = StrList(J.Get(p, "upgrades")),
            Expected = OptStr(p, "expected"),
        };
    }
}
