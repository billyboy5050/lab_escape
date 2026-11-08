#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using LabEscape.Core;
using Newtonsoft.Json.Linq;

namespace LabEscape.Sim
{
    /// <summary>A preset ID, a fixed loadout, or a sweep of random valid loadouts (one per seed).</summary>
    public sealed class LoadoutChoice
    {
        public string? Preset;
        public Loadout? Custom;
        public bool Sweep;
        public int? Size;

        public static LoadoutChoice OfPreset(string id) => new LoadoutChoice { Preset = id };
        public static LoadoutChoice OfCustom(Loadout loadout) => new LoadoutChoice { Custom = loadout };
        public static LoadoutChoice OfSweep(int? size = null) => new LoadoutChoice { Sweep = true, Size = size };
    }

    public sealed class BatchSpec
    {
        public string Bot = Batch.GreedyKind;
        public LoadoutChoice Loadout = null!;
        public IReadOnlyList<long> Seeds = new long[0];
        public FightSettings? Settings;
        /// <summary>Keep each fight's command log in the rows (for saving winning lines).</summary>
        public bool KeepRecords;
    }

    /// <summary>One fight's figures, as a report and the per-fight CSV use them.</summary>
    public sealed class FightRow
    {
        public long Seed;
        public string Bot = "";
        public string? Preset;
        public IReadOnlyList<string> Abilities = new string[0];
        public IReadOnlyList<string> Upgrades = new string[0];
        public FightOutcome Outcome;
        public int Rounds;
        public string? Cause;
        public SourceCategory? DeathCategory;
        public string? DeathSource;
        public DamageTally PlayerDamage = new DamageTally();
        public double FriendlyFireShare;
        public DamageTally EnemyDamage = new DamageTally();
        public int HazardFires;
        public int HazardFiresHitPlayer;
        public int PeakMinions;
        public int WavesSpawned;
        public int? WardenReachedPlayerRound;
        public int? WardenDiedRound;
        public int Kills;
        public int Commands;
        public OrderedMap<string, int> AbilitiesUsed = new OrderedMap<string, int>(StringComparer.Ordinal);
        public string? Error;
        public FightRecord? Record;
    }

    public static class Batch
    {
        public const string RandomKind = "random";
        public const string GreedyKind = "greedy";
        public const string GreedyNaiveKind = "greedy-naive";
        public static readonly IReadOnlyList<string> BotKinds = new[] { RandomKind, GreedyKind, GreedyNaiveKind };

        /// <summary>The content with tuning overrides applied, e.g. "units.warden.hp" = 16.</summary>
        public static Content ContentFor(ContentFiles files, OrderedMap<string, JToken>? overrides = null)
        {
            return ContentLoader.Build(overrides != null && overrides.Count > 0 ? ContentLoader.WithOverrides(files, overrides) : files);
        }

        public static IBot MakeBot(string kind, long seed)
        {
            if (kind == RandomKind) return new RandomBot(seed);
            return new GreedyBot(seed, heedCues: kind == GreedyKind);
        }

        private static List<string> InOrder(IEnumerable<string> items, List<string> order)
        {
            return Sequences.StableSorted(items, (a, b) => order.IndexOf(a) - order.IndexOf(b));
        }

        /// <summary>A random valid loadout: size abilities from the pool, then up to the upgrade limit among those whose requirements are met.</summary>
        public static Loadout RandomLoadout(Content c, long seed, int? size = null)
        {
            var rng = new Rng(unchecked((int)seed) ^ 0x5eed); // JavaScript's ^ works on 32-bit signed integers.
            var wanted = Math.Min(size ?? c.Rules.Loadout.Abilities, c.AbilityOrder.Count);
            var abilities = InOrder(rng.Shuffle(new List<string>(c.AbilityOrder)).Take(wanted), c.AbilityOrder);
            var eligible = c.UpgradeOrder.Where(u => ContentLoader.UpgradeAllowed(c.Upgrades[u].Requires, abilities)).ToList();
            var upgrades = InOrder(rng.Shuffle(eligible).Take(c.Rules.Loadout.Upgrades), c.UpgradeOrder);
            return new Loadout(abilities, upgrades);
        }

        private static (Loadout Loadout, string? Preset) LoadoutFor(Content c, BatchSpec spec, long seed)
        {
            var l = spec.Loadout;
            if (l.Preset != null)
            {
                var p = c.Presets.FirstOrDefault(x => x.Id == l.Preset);
                if (p == null) throw new ArgumentException($"Unknown preset \"{l.Preset}\"");
                return (new Loadout(p.Abilities, p.Upgrades), p.Id);
            }
            if (l.Custom != null) return (l.Custom, null);
            return (RandomLoadout(c, seed, l.Size), null);
        }

        private static FightRow RunOne(BatchSpec spec, Content c, long seed)
        {
            var (loadout, preset) = LoadoutFor(c, spec, seed);
            var bot = MakeBot(spec.Bot, seed);
            try
            {
                var r = Runner.RunFight(c, loadout, bot, spec.Settings, meta: new RecordMeta { Preset = preset });
                return RowOf(seed, bot.Name, preset, r.Summary, r.Commands, spec.KeepRecords ? r.Record : null);
            }
            catch (Exception e)
            {
                return new FightRow { Seed = seed, Bot = bot.Name, Preset = preset, Abilities = loadout.Abilities, Upgrades = loadout.Upgrades, Outcome = FightOutcome.Error, Error = e.Message, Record = (e as BotError)?.Record };
            }
        }

        private static FightRow RowOf(long seed, string bot, string? preset, FightSummary s, int commands, FightRecord? record)
        {
            return new FightRow
            {
                Seed = seed,
                Bot = bot,
                Preset = preset,
                Abilities = s.Loadout.Abilities,
                Upgrades = s.Loadout.Upgrades,
                Outcome = s.Outcome,
                Rounds = s.Rounds,
                Cause = s.Cause,
                DeathCategory = s.CauseOfDeath?.Category,
                DeathSource = s.CauseOfDeath != null ? $"{s.CauseOfDeath.Source}/{s.CauseOfDeath.Via}" : null,
                PlayerDamage = s.PlayerDamageTaken,
                FriendlyFireShare = s.FriendlyFireShare,
                EnemyDamage = s.EnemyDamageTaken,
                HazardFires = s.HazardFires.Count,
                HazardFiresHitPlayer = s.HazardFires.Count(h => h.Victims.Contains("player")),
                PeakMinions = s.PeakMinions,
                WavesSpawned = s.WavesSpawned,
                WardenReachedPlayerRound = s.WardenReachedPlayerRound,
                WardenDiedRound = s.WardenDiedRound,
                Kills = s.Kills.Values.Sum(),
                Commands = commands,
                AbilitiesUsed = s.AbilitiesUsed,
                Record = record,
            };
        }

        /// <summary>Runs a batch on the current thread. Rows come back in seed order; the same spec always gives the same rows.</summary>
        public static List<FightRow> RunBatch(BatchSpec spec, Content c) => spec.Seeds.Select(seed => RunOne(spec, c, seed)).ToList();

        /// <summary>
        /// Runs a batch across threads. Each fight is independent and goes into its seed's slot, so the result is identical to a
        /// single-threaded run. The content is only read, so the threads share it.
        /// </summary>
        public static List<FightRow> RunBatchParallel(BatchSpec spec, Content c, int? workers = null, Action<int, int>? onProgress = null)
        {
            if (workers.HasValue && workers.Value < 1) throw new ArgumentException($"workers must be a whole number of at least 1 (got {workers})");
            var total = spec.Seeds.Count;
            var threads = Math.Max(1, Math.Min(workers ?? Math.Max(1, Environment.ProcessorCount - 1), (total + 3) / 4));
            if (threads <= 1)
            {
                var rows = RunBatch(spec, c);
                onProgress?.Invoke(rows.Count, rows.Count);
                return rows;
            }
            var slots = new FightRow[total];
            var done = 0;
            var gate = new object();
            try
            {
                Parallel.For(0, total, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
                {
                    slots[i] = RunOne(spec, c, spec.Seeds[i]);
                    lock (gate) onProgress?.Invoke(++done, total);
                });
            }
            catch (AggregateException e)
            {
                ExceptionDispatchInfo.Capture(e.InnerExceptions[0]).Throw();
            }
            return slots.ToList();
        }

        public static List<long> SeedRange(long from, int count) => Enumerable.Range(0, count).Select(i => from + i).ToList();
    }
}
