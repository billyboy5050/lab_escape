#nullable enable
using System;
using LabEscape.Core;

namespace LabEscape.Sim
{
    public sealed class FightRun
    {
        public long? Seed;
        public string Bot = "";
        public FightRecord Record = null!;
        public FightSummary Summary = null!;
        public GameState State = null!;
        public int Commands;
    }

    public sealed class BotError : Exception
    {
        public FightRecord Record { get; }

        public BotError(string message, FightRecord record) : base(message)
        {
            Record = record;
        }
    }

    public static class Runner
    {
        private const int DefaultMaxCommands = 2000;

        /// <summary>
        /// Plays one fight headlessly. A bot that issues an illegal command, or a fight that exceeds the command limit (a likely
        /// infinite loop), raises a BotError carrying the command log so far, for a bug report.
        /// </summary>
        public static FightRun RunFight(Content c, Loadout loadout, IBot bot, FightSettings? settings = null, int? maxCommands = null, RecordMeta? meta = null)
        {
            settings ??= new FightSettings();
            var g = Engine.NewGame(c, loadout, settings);
            var record = Records.NewRecord(c, loadout, settings, new RecordMeta { Bot = bot.Name, Seed = bot.Seed }.MergedWith(meta));
            var summary = new SummaryBuilder(loadout);
            summary.Add(g.Events);
            var s = g.State;
            var max = maxCommands ?? DefaultMaxCommands;
            var n = 0;
            while (s.Outcome == null)
            {
                if (++n > max) throw new BotError($"Command limit ({max}) reached in round {s.Round}: possible infinite loop", record);
                var cmd = bot.Decide(c, s);
                if (cmd == null) break;
                var r = Engine.Step(c, s, cmd);
                if (!r.Ok) throw new BotError($"Bot {bot.Name} issued an illegal command in round {s.Round}: {r.Error} ({cmd.ToJson()})", record);
                record.Commands.Add(cmd);
                summary.Add(r.Events);
                s = r.State;
            }
            return new FightRun { Seed = bot.Seed, Bot = bot.Name, Record = record, Summary = summary.Finish(s), State = s, Commands = record.Commands.Count };
        }
    }
}
