#nullable enable
using System.Linq;

namespace LabEscape.Core
{
    public static class Phases
    {
        public static World CreateWorld(Content c, GameState s, bool frames = false)
        {
            var w = new World(c, s, frames);
            Hooks.Register(w);
            return w;
        }

        public static void SetPhase(World w, Phase phase)
        {
            w.S.Phase = phase;
            w.Emit(new PhaseStarted { Round = w.S.Round, Phase = phase });
            IntercomRules.PhaseStart(w);
        }

        /// <summary>Round start: spawn any wave due, play any intercom hint, refill the player's AP and movement.</summary>
        public static void StartRound(World w, int n)
        {
            w.S.Round = n;
            w.Emit(new RoundStarted { Round = n });
            SetPhase(w, Phase.RoundStart);
            w.Root("round start", null, () => Waves.SpawnDue(w));
            IntercomRules.Trigger(w, TriggerTypes.RoundStart, round: n);
            w.S.Ap = w.C.Rules.Turn.Ap;
            w.S.Movement = w.C.Rules.Turn.Movement;
            w.S.Committed = false;
            SetPhase(w, Phase.Player);
            w.CloseFrame("player phase", 0);
        }

        /// <summary>Minion phase: each minion acts once, in summon order. Units spawned during the phase wait for next round.</summary>
        public static void RunMinionPhase(World w)
        {
            SetPhase(w, Phase.Minion);
            var ids = w.S.Units.Where(u => u.Kind == UnitKind.Minion && !u.Dead).Select(u => u.Id).ToList();
            foreach (var id in ids)
            {
                var m = w.Unit(id);
                if (m == null || m.Dead || m.Docked) continue;
                w.Root($"{m.Def} {m.Id}", m.Id, () => MinionAi.MinionTurn(w, m));
            }
        }

        /// <summary>Enemy phase: each enemy acts once, in spawn order.</summary>
        public static void RunEnemyPhase(World w)
        {
            SetPhase(w, Phase.Enemy);
            var ids = w.S.Units.Where(u => u.Team == Team.Enemy && !u.Dead).Select(u => u.Id).ToList();
            foreach (var id in ids)
            {
                var e = w.Unit(id);
                if (e == null || e.Dead) continue;
                w.Root($"{e.Def} {e.Id}", e.Id, () => EnemyAi.EnemyTurn(w, e));
            }
        }

        /// <summary>Environment phase, in order: primed hazards fire or cancel; hazards prime; statuses tick and poison spreads; eggs; cooldowns and decay.</summary>
        public static void RunEnvironmentPhase(World w)
        {
            SetPhase(w, Phase.Environment);
            w.Root("hazards fire", null, () => HazardAi.FireStep(w));
            w.Root("hazards prime", null, () => HazardAi.PrimeStep(w));
            w.Root("statuses tick", null, () => EnvironmentRules.StatusTickStep(w));
            w.Root("eggs", null, () => EnvironmentRules.EggStep(w));
            w.Root("cooldowns and decay", null, () => EnvironmentRules.DecayStep(w));
        }

        /// <summary>Everything after the player ends the turn, through to the next player phase (or the end of the fight).</summary>
        public static void EndTurn(World w)
        {
            RunMinionPhase(w);
            RunEnemyPhase(w);
            RunEnvironmentPhase(w);
            if (w.S.Round >= w.Room.RoundCap)
            {
                w.EndFight(new Outcome { Result = FightResult.Lose, Cause = $"Round {w.Room.RoundCap} ended with the fight unresolved", Round = w.S.Round });
            }
            StartRound(w, w.S.Round + 1);
        }
    }
}
