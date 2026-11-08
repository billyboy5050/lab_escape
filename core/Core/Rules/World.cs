#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace LabEscape.Core
{
    /// <summary>Thrown to unwind the engine the moment the fight is decided. The state carries the outcome.</summary>
    public sealed class FightOver : Exception
    {
        public Outcome Outcome { get; }

        public FightOver(Outcome outcome)
            : base($"Fight over: {Names.Of(outcome.Result)} ({outcome.Cause})")
        {
            Outcome = outcome;
        }
    }

    /// <summary>Thrown for a command the rules do not allow in the current state. The state is left unchanged.</summary>
    public sealed class IllegalCommand : Exception
    {
        public IllegalCommand(string message) : base(message) { }
    }

    /// <summary>The content ids of the upgrades.</summary>
    public static class Up
    {
        public const string T1 = "T1_piercing_rounds";
        public const string T2 = "T2_shot_triggered_mines";
        public const string T3 = "T3_suppressing_fire";
        public const string T4 = "T4_grapple_pin";
        public const string T5 = "T5_reflective_shield";
        public const string T6 = "T6_overcharged_drone";
        public const string A1 = "A1_spore_burst";
        public const string A2 = "A2_acid_brood";
        public const string A3 = "A3_parasite_jump";
        public const string A4 = "A4_fast_brood";
        public const string H1 = "H1_spore_mines";
        public const string H2 = "H2_plague_drone";
    }

    /// <summary>A death hook may queue follow-up effects for a unit that just died (Spore Burst, Parasite, ...).</summary>
    public delegate void DeathHook(World w, Unit dead);

    /// <summary>
    /// The mutable rules context for one step. It wraps a (cloned) GameState and owns the event list, the chain of the
    /// current root action, and every primitive rule: damage, statuses, deaths, movement, spawning and mines. Higher-level
    /// behaviour (abilities, AI, phases) is built on it.
    /// </summary>
    public sealed class World
    {
        private sealed class Job
        {
            public string Effect = "";
            public string Source = "";
            public int Gen;
            public Action Run = null!;
        }

        private sealed class Chain
        {
            public string Label = "";
            public readonly HashSet<string> Fired = new HashSet<string>(StringComparer.Ordinal);
            public readonly List<Job> Queue = new List<Job>();
            public int Gen;
        }

        public readonly Content C;
        public readonly GameState S;
        public readonly Board B;
        public readonly List<GameEvent> Events = new List<GameEvent>();
        public readonly List<Frame>? Frames;
        public readonly List<DeathHook> DeathHooks = new List<DeathHook>();

        /// <summary>True when a command triggered something (a mine), which commits the player's turn.</summary>
        public bool Triggered;

        private Chain? _chain;
        private int _frameStart;

        public World(Content c, GameState s, bool frames = false)
        {
            C = c;
            S = s;
            B = c.Board;
            Frames = frames ? new List<Frame>() : null;
        }

        // ------------------------------------------------------------------ events and frames

        public void Emit(GameEvent e) => Events.Add(e);

        /// <summary>
        /// Runs a root action: a player command, one minion or enemy action, or one environment step. It opens a chain, applies
        /// the action, processes deaths, drains the trigger queue, then checks for a win.
        /// </summary>
        public void Root(string label, int? actor, Action fn)
        {
            if (_chain != null) throw new InvalidOperationException($"Root action \"{label}\" started inside \"{_chain.Label}\"");
            _chain = new Chain { Label = label };
            try
            {
                fn();
                FlushDeaths();
                RunQueue();
                CheckWin();
            }
            finally
            {
                // Runs even when the fight ends mid-action (FightOver), so the deciding action keeps its frame
                // and a line queued by it (a win's "Warden is down") still plays.
                _chain = null;
                try
                {
                    IntercomRules.Flush(this);
                }
                finally
                {
                    CloseFrame(label, actor);
                }
            }
        }

        /// <summary>Groups every event since the last frame into one frame, with a snapshot of the state.</summary>
        public void CloseFrame(string label, int? actor)
        {
            if (Frames == null) return;
            if (Events.Count == _frameStart && label == "tail") return;
            Frames.Add(new Frame { Label = label, Phase = S.Phase, Actor = actor, Events = Events.GetRange(_frameStart, Events.Count - _frameStart), State = S.Clone() });
            _frameStart = Events.Count;
        }

        public bool InChain => _chain != null;

        /// <summary>Queues a triggered effect for the current chain. It runs after the current effect, first in first out.</summary>
        public void Enqueue(string effect, string source, Action run)
        {
            var ch = RequireChain();
            ch.Queue.Add(new Job { Effect = effect, Source = source, Gen = ch.Gen + 1, Run = run });
        }

        /// <summary>
        /// Runs a triggered effect immediately (a mine stepped on mid-move, a reflected shot), under the once-per-chain rule and
        /// the depth cap. Deaths it causes are processed before it returns.
        /// </summary>
        public bool Immediate(string source, string effect, Action fn)
        {
            var ch = RequireChain();
            var gen = ch.Gen + 1;
            if (!Admit(ch, source, effect, gen)) return false;
            var prev = ch.Gen;
            ch.Gen = gen;
            try
            {
                fn();
                FlushDeaths();
            }
            finally
            {
                ch.Gen = prev;
            }
            return true;
        }

        private bool Admit(Chain ch, string source, string effect, int gen)
        {
            if (gen > C.Rules.ChainDepthCap)
            {
                Emit(new ChainSuppressed { Effect = effect, Source = source, Reason = SuppressReason.Depth });
                return false;
            }
            var key = source + "|" + effect;
            if (ch.Fired.Contains(key))
            {
                Emit(new ChainSuppressed { Effect = effect, Source = source, Reason = SuppressReason.Repeat });
                return false;
            }
            ch.Fired.Add(key);
            return true;
        }

        private void RunQueue()
        {
            var ch = RequireChain();
            while (ch.Queue.Count > 0)
            {
                var job = Sequences.Shift(ch.Queue);
                if (!Admit(ch, job.Source, job.Effect, job.Gen)) continue;
                var prev = ch.Gen;
                ch.Gen = job.Gen;
                try
                {
                    job.Run();
                    FlushDeaths();
                }
                finally
                {
                    ch.Gen = prev;
                }
            }
        }

        private Chain RequireChain()
        {
            if (_chain == null) throw new InvalidOperationException("No chain is open: effects must run inside a root action");
            return _chain;
        }

        // ------------------------------------------------------------------ queries

        public Unit? Player
        {
            get
            {
                if (S.Units.Count > 0 && S.Units[0].Id == 0) return S.Units[0];
                return Unit(0);
            }
        }

        public Unit? Unit(int id)
        {
            foreach (var u in S.Units)
            {
                if (u.Id == id) return u;
            }
            return null;
        }

        /// <summary>Units on the board: alive and not docked.</summary>
        public List<Unit> ActiveUnits()
        {
            var output = new List<Unit>(S.Units.Count);
            foreach (var u in S.Units)
            {
                if (!u.Dead && !u.Docked) output.Add(u);
            }
            return output;
        }

        public Unit? UnitAt(Pos p)
        {
            foreach (var u in S.Units)
            {
                if (!u.Dead && !u.Docked && u.Pos == p) return u;
            }
            return null;
        }

        public List<BoardObject> ObjectsAt(Pos p)
        {
            var output = new List<BoardObject>();
            foreach (var o in S.Objects)
            {
                if (!o.Destroyed && o.Pos == p) output.Add(o);
            }
            return output;
        }

        /// <summary>Shield segments and eggs block movement and projectiles (not sight).</summary>
        public BoardObject? BlockingObjectAt(Pos p)
        {
            foreach (var o in S.Objects)
            {
                if (!o.Destroyed && (o.Kind == ObjectKind.Shield || o.Kind == ObjectKind.Egg) && o.Pos == p) return o;
            }
            return null;
        }

        public BoardObject? ObjectAt(Pos p, ObjectKind kind)
        {
            foreach (var o in S.Objects)
            {
                if (!o.Destroyed && o.Kind == kind && o.Pos == p) return o;
            }
            return null;
        }

        public BoardObject? MineAt(Pos p) => ObjectAt(p, ObjectKind.Mine);

        /// <summary>A tile a unit could end its move on: passable terrain, no unit, no blocking object.</summary>
        public bool IsFree(Pos p, int? ignoreUnit = null)
        {
            if (B.BlocksMove(p)) return false;
            if (BlockingObjectAt(p) != null) return false;
            var u = UnitAt(p);
            return u == null || u.Id == ignoreUnit;
        }

        /// <summary>A tile something can be placed on: passable terrain with no unit and no object of any kind.</summary>
        public bool IsEmpty(Pos p) => !B.BlocksMove(p) && UnitAt(p) == null && ObjectsAt(p).Count == 0;

        public List<Unit> PlayerSide() => ActiveUnits().Where(u => u.Team == Team.Player).ToList();

        public List<Unit> Enemies() => ActiveUnits().Where(u => u.Team == Team.Enemy).ToList();

        /// <summary>Minions count toward the cap while alive, including docked drones.</summary>
        public int MinionCount()
        {
            var count = 0;
            foreach (var u in S.Units)
            {
                if (!u.Dead && u.Kind == UnitKind.Minion) count++;
            }
            return count;
        }

        public bool HasUp(string upgradeId) => S.Loadout.Upgrades.Contains(upgradeId);

        public UpgradeDef UpDef(string upgradeId) => C.Upgrades[upgradeId];

        /// <summary>Piercing Rounds: kinetic damage from the Sidearm and turrets ignores some armor.</summary>
        public int Piercing(DamageType? damageType) => HasUp(Up.T1) && damageType == DamageType.Kinetic ? UpDef(Up.T1).ArmorIgnore ?? 0 : 0;

        public int ArmorOf(Unit u) => Math.Max(0, u.Armor - (u.Statuses.Corrode?.Stacks ?? 0));

        /// <summary>Movement a unit may use in its next move: Pinned stops it, Slowed reduces it.</summary>
        public int MovementAllowance(Unit u, int? baseMove = null)
        {
            if (u.Statuses.Pinned != null) return 0;
            var m = baseMove ?? C.Units[u.Def].Move;
            return Math.Max(0, m - (u.Statuses.Slowed != null ? C.Rules.Statuses.Slowed.MovementPenalty : 0));
        }

        // ------------------------------------------------------------------ sources

        public SourceRef UnitSource(Unit u, string via)
        {
            var side = u.Team == Team.Player ? SourceSide.Player : SourceSide.Enemy;
            var category = u.Kind == UnitKind.Player ? SourceCategory.Player : u.Kind == UnitKind.Minion ? SourceCategory.Minion : SourceCategory.Enemy;
            return new SourceRef(side, category, SourceKind.Unit, u.Id, u.Def, via);
        }

        /// <summary>Effects owned by the player that are not a unit's attack: mines, upgrade hooks, statuses.</summary>
        public SourceRef PlayerEffect(string via, SourceKind kind = SourceKind.Unit, int id = 0, string def = "player")
        {
            return new SourceRef(SourceSide.Player, SourceCategory.Player, kind, id, def, via);
        }

        // ------------------------------------------------------------------ damage, healing, statuses

        /// <summary>Applies damage to a unit after armor. Marks it dead at 0 HP; removal happens at the next flush.</summary>
        public int DamageUnit(Unit u, int raw, DamageType type, SourceRef src, int armorIgnore = 0)
        {
            if (u.Dead || u.Docked || raw <= 0) return 0;
            var absorbed = 0;
            if (C.Rules.ArmorAppliesTo.Contains(type)) absorbed = Math.Min(raw, Math.Max(0, ArmorOf(u) - armorIgnore));
            var amount = raw - absorbed;
            var before = u.Hp;
            u.Hp = Math.Max(0, u.Hp - amount);
            var killed = u.Hp <= 0;
            if (killed)
            {
                u.Dead = true;
                u.KilledBy = src;
            }
            Emit(new DamageDealt
            {
                Target = EntityRef.Unit(u.Id),
                TargetDef = u.Def,
                TargetSide = u.Team,
                Pos = u.Pos,
                Source = src,
                DamageType = type,
                Raw = raw,
                Absorbed = absorbed,
                Amount = amount,
                HpLost = before - u.Hp,
                HpAfter = u.Hp,
                Killed = killed,
            });
            if (!killed && amount > 0) IntercomRules.HpCheck(this, u, before);
            return amount;
        }

        /// <summary>Eggs and shield segments take damage like units, with no armor.</summary>
        public int DamageObject(BoardObject o, int raw, DamageType type, SourceRef src)
        {
            if (o.Destroyed || !o.Hp.HasValue || raw <= 0) return 0;
            var before = o.Hp.Value;
            o.Hp = Math.Max(0, before - raw);
            var killed = o.Hp.Value <= 0;
            if (killed)
            {
                o.Destroyed = true;
                o.KilledBy = src;
            }
            Emit(new DamageDealt
            {
                Target = EntityRef.Object(o.Id),
                TargetDef = Names.Of(o.Kind),
                TargetSide = o.Team ?? Team.Player,
                Pos = o.Pos,
                Source = src,
                DamageType = type,
                Raw = raw,
                Absorbed = 0,
                Amount = raw,
                HpLost = before - o.Hp.Value,
                HpAfter = o.Hp.Value,
                Killed = killed,
            });
            return raw;
        }

        public int DamageEntity(EntityRef target, int raw, DamageType type, SourceRef src, int armorIgnore = 0)
        {
            if (target.Kind == EntityKind.Unit)
            {
                var u = Unit(target.Id);
                return u != null ? DamageUnit(u, raw, type, src, armorIgnore) : 0;
            }
            var o = S.Objects.FirstOrDefault(x => x.Id == target.Id);
            return o != null ? DamageObject(o, raw, type, src) : 0;
        }

        /// <summary>Damages every unit, then every damageable object, on the given tiles. Deaths wait for the flush.</summary>
        public void AreaDamage(IReadOnlyCollection<Pos> tiles, int raw, DamageType type, SourceRef src, int? exclude = null, int armorIgnore = 0)
        {
            var units = Sequences.StableSorted(ActiveUnits().Where(u => u.Id != exclude && tiles.Contains(u.Pos)), (a, b) => a.Id - b.Id);
            foreach (var u in units) DamageUnit(u, raw, type, src, armorIgnore);
            var objects = Sequences.StableSorted(S.Objects.Where(o => !o.Destroyed && o.Hp.HasValue && tiles.Contains(o.Pos)), (a, b) => a.Id - b.Id);
            foreach (var o in objects) DamageObject(o, raw, type, src);
        }

        public int Heal(Unit u, int amount, SourceRef src)
        {
            if (u.Dead) return 0;
            var amt = Math.Min(amount, u.MaxHp - u.Hp);
            if (amt <= 0) return 0;
            u.Hp += amt;
            Emit(new Healed { Target = u.Id, Source = src, Amount = amt, HpAfter = u.Hp });
            return amt;
        }

        /// <summary>
        /// Applies a status. Poison and Parasite reapplied directly restart at the given duration; spread infections pass the
        /// duration they inherit. Corrode adds stacks.
        /// </summary>
        public void ApplyStatus(Unit u, StatusId id, int value, SourceRef src, bool spread = false)
        {
            if (u.Dead || u.Docked || value <= 0) return;
            var st = u.Statuses;
            var refreshed = st.Has(id);
            if (id == StatusId.Corrode)
            {
                var dur = C.Rules.Statuses.CorrodeDuration;
                st.Corrode = new CorrodeStatus((st.Corrode?.Stacks ?? 0) + value, dur > 0 ? dur : (int?)null, src);
            }
            else
            {
                st.SetTimed(id, new TimedStatus(value, src));
            }
            Emit(new StatusApplied { Target = u.Id, Status = id, Value = value, Source = src, Refreshed = refreshed, Spread = spread });
        }

        public void RemoveStatus(Unit u, StatusId id, string reason)
        {
            if (!u.Statuses.Has(id)) return;
            u.Statuses.Remove(id);
            Emit(new StatusRemoved { Target = u.Id, Status = id, Reason = reason });
        }

        // ------------------------------------------------------------------ deaths and the end of the fight

        /// <summary>
        /// Removes every unit and object marked dead, in unit ID order, leaving enemy corpses and queueing death hooks. The
        /// player's death ends the fight once every unit that died with them has been reported; the last enemy's death waits
        /// for CheckWin.
        /// </summary>
        public void FlushDeaths()
        {
            var deadObjects = S.Objects.Where(o => o.Destroyed).ToList();
            var deadUnits = Sequences.StableSorted(S.Units.Where(u => u.Dead), (a, b) => a.Id - b.Id);
            if (deadObjects.Count == 0 && deadUnits.Count == 0) return;
            if (deadObjects.Count > 0)
            {
                S.Objects = S.Objects.Where(o => !o.Destroyed).ToList();
                foreach (var o in deadObjects) Emit(new ObjectDestroyed { Id = o.Id, Kind = o.Kind, Pos = o.Pos, Killer = o.KilledBy });
            }
            var playerDeath = deadUnits.FirstOrDefault(u => u.Id == 0);
            foreach (var u in deadUnits)
            {
                S.Units = S.Units.Where(x => !ReferenceEquals(x, u)).ToList();
                Emit(new UnitDied { Id = u.Id, Def = u.Def, Team = u.Team, Kind = u.Kind, Pos = u.Pos, Killer = u.KilledBy });
                if (u.Id == 0)
                {
                    IntercomRules.Trigger(this, TriggerTypes.UnitDied, def: "player");
                    continue;
                }
                if (u.Team == Team.Enemy) PlaceCorpse(u);
                // Once the player is dead the fight is over: the player's line has cleared the intercom queue, and no
                // death effect or later line may add to it.
                if (playerDeath != null) continue;
                IntercomRules.Trigger(this, TriggerTypes.UnitDied, def: u.Def);
                for (var i = 0; i < DeathHooks.Count; i++) DeathHooks[i](this, u);
            }
            if (playerDeath != null)
            {
                EndFight(new Outcome { Result = FightResult.Lose, Cause = DescribeSource(playerDeath.KilledBy), Round = S.Round, Killer = playerDeath.KilledBy });
            }
        }

        /// <summary>
        /// Ends the fight in a win once every wave is in and no enemy is left. Only Root calls it, after the action and the
        /// chain's queued effects have finished, so a win never cuts an action short: a move that kills the last enemy on one
        /// mine still steps on the next, and the last enemy's Spore Burst, Parasite hatch and other death effects still play
        /// (and can add an enemy, which continues the fight). The player's death is the opposite case: it ends the fight at
        /// once, in FlushDeaths, so a loss beats a win from the same action.
        /// </summary>
        private void CheckWin()
        {
            if (S.Outcome != null || !AllEnemiesDown()) return;
            EndFight(new Outcome { Result = FightResult.Win, Cause = "All enemies are dead", Round = S.Round });
        }

        private bool AllEnemiesDown()
        {
            var inPlay = Math.Min(S.Settings.MaxWaves ?? C.Waves.Count, C.Waves.Count);
            return S.WavesSpawned >= inPlay && !S.Units.Any(u => u.Team == Team.Enemy && !u.Dead);
        }

        [DoesNotReturn]
        public void EndFight(Outcome outcome)
        {
            S.Outcome = outcome;
            S.Phase = Phase.Ended;
            Emit(new FightEnded { Result = outcome.Result, Cause = outcome.Cause, Round = outcome.Round });
            throw new FightOver(outcome);
        }

        // ------------------------------------------------------------------ objects

        /// <summary>Places an object built from the given fields, assigning its ID, kind, position and creation round.</summary>
        public BoardObject PlaceObject(ObjectKind kind, Pos pos, BoardObject fields, SourceRef? src)
        {
            fields.Id = S.NextObjectId++;
            fields.Kind = kind;
            fields.Pos = pos;
            fields.CreatedRound = S.Round;
            S.Objects.Add(fields);
            Emit(new ObjectPlaced { Object = fields.Clone(), Source = src });
            return fields;
        }

        public void RemoveObject(BoardObject o, RemovalReason reason)
        {
            S.Objects = S.Objects.Where(x => !ReferenceEquals(x, o)).ToList();
            Emit(new ObjectRemoved { Id = o.Id, Kind = o.Kind, Pos = o.Pos, Reason = reason });
        }

        public void PlaceCorpse(Unit u)
        {
            if (B.BlocksMove(u.Pos)) return;
            var old = ObjectAt(u.Pos, ObjectKind.Corpse);
            if (old != null) RemoveObject(old, RemovalReason.Replaced);
            PlaceObject(ObjectKind.Corpse, u.Pos, new BoardObject { Of = u.Def, Decay = C.Rules.CorpseDecay, Team = Team.Enemy }, null);
        }

        /// <summary>A ground unit entered a mine's tile, or a shot reached it: 3x3 explosive blast, then it is consumed.</summary>
        public void TriggerMine(BoardObject mine, Unit? by)
        {
            Triggered = true;
            Immediate("o" + mine.Id, "mine_blast", () =>
            {
                Emit(new ObjectTriggered { Id = mine.Id, Kind = ObjectKind.Mine, Pos = mine.Pos, By = by != null ? EntityRef.Unit(by.Id) : (EntityRef?)null });
                RemoveObject(mine, RemovalReason.Detonated);
                var blast = C.Abilities["proximity_mine"].Blast!;
                var tiles = B.Square(mine.Pos, blast.Radius);
                var src = PlayerEffect("proximity_mine", SourceKind.Object, mine.Id, "mine");
                Emit(new AreaEffect { Kind = AreaKind.Blast, Tiles = tiles, Source = src });
                AreaDamage(tiles, blast.Damage, blast.DamageType, src);
                if (HasUp(Up.H1))
                {
                    var st = UpDef(Up.H1).Status!;
                    var psrc = PlayerEffect(Up.H1, SourceKind.Object, mine.Id, "mine");
                    foreach (var u in Sequences.StableSorted(ActiveUnits().Where(x => tiles.Contains(x.Pos)), (a, b) => a.Id - b.Id))
                    {
                        ApplyStatus(u, st.Id, st.Duration, psrc);
                    }
                }
            });
        }

        // ------------------------------------------------------------------ movement and spawning

        /// <summary>
        /// Moves a unit tile by tile along a path. A ground unit entering a mine sets it off, and keeps going if it survives.
        /// Returns false if the unit died on the way.
        /// </summary>
        public bool MoveAlong(Unit u, IReadOnlyList<Pos> path, MoveMode mode)
        {
            var from = u.Pos;
            var seg = new List<Pos>();
            foreach (var p in path)
            {
                u.Pos = p;
                seg.Add(u.Pos);
                if (u.Flying) continue;
                var mine = MineAt(u.Pos);
                if (mine == null) continue;
                Emit(new UnitMoved { Id = u.Id, From = from, To = u.Pos, Path = seg, Mode = mode });
                from = u.Pos;
                seg = new List<Pos>();
                TriggerMine(mine, u);
                if (u.Dead) return false;
            }
            if (seg.Count > 0) Emit(new UnitMoved { Id = u.Id, From = from, To = u.Pos, Path = seg, Mode = mode });
            return true;
        }

        /// <summary>The tile itself if free, else the nearest free tile by range distance (ties: lowest row, then column).</summary>
        public Pos? FindSpawnTile(Pos p)
        {
            if (IsFree(p)) return p;
            Pos? best = null;
            var bestD = int.MaxValue;
            foreach (var t in B.AllTiles())
            {
                if (!IsFree(t)) continue;
                var d = Grid.Cheb(t, p);
                if (d < bestD || (d == bestD && best.HasValue && Grid.ComparePos(t, best.Value) < 0))
                {
                    best = t;
                    bestD = d;
                }
            }
            return best;
        }

        /// <summary>
        /// Creates a unit with the next ID. A ground unit that appears on a mine sets it off; callers placing several units at
        /// once pass triggerMines: false and call TriggerMineUnder once all are placed.
        /// </summary>
        public Unit SpawnUnit(string defId, Pos pos, string cause, bool arrival = false, bool triggerMines = true)
        {
            if (!C.Units.TryGetValue(defId, out var def)) throw new ArgumentException($"Unknown unit \"{defId}\"");
            var u = new Unit
            {
                Id = S.NextUnitId++,
                Def = defId,
                Team = def.Team,
                Kind = def.Kind,
                Pos = pos,
                Hp = def.Hp,
                MaxHp = def.Hp,
                Armor = def.Armor,
                Flying = def.Flying,
                Elite = def.Elite,
                SpawnRound = S.Round,
            };
            if (def.Ammo.HasValue) u.Ammo = u.MaxAmmo = def.Ammo.Value;
            if (def.Charges.HasValue)
            {
                u.Charges = u.MaxCharges = DroneCharges(def.Charges.Value);
                u.DroneState = DroneState.Deployed;
                u.ReturningPhases = 0;
            }
            if (def.Purge != null) u.Cooldowns["purge"] = 0;
            if (arrival) u.ArrivalRound = S.Round;
            S.Units.Add(u);
            Emit(new UnitSpawned { Unit = u.Clone(), Cause = cause });
            S.PeakMinions = Math.Max(S.PeakMinions, MinionCount());
            if (triggerMines) TriggerMineUnder(u);
            return u;
        }

        /// <summary>A ground unit standing on a mine sets it off (used after spawning).</summary>
        public void TriggerMineUnder(Unit u)
        {
            if (u.Dead || u.Flying) return;
            var mine = MineAt(u.Pos);
            if (mine != null) TriggerMine(mine, u);
        }

        public int DroneCharges(int baseCharges) => HasUp(Up.T6) ? UpDef(Up.T6).Charges ?? baseCharges : baseCharges;

        public static string DescribeSource(SourceRef? src) => src == null ? "Unknown" : $"{src.Def} ({src.Via})";

        public static string EntityKey(EntityRef target) => (target.Kind == EntityKind.Unit ? "u" : "o") + target.Id;
    }
}
