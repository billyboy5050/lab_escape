#nullable enable
using System.Collections.Generic;

namespace LabEscape.Core
{
    // Events. The client animates them, telemetry and the simulator consume them. Each writes itself with the same keys in
    // the same order as the TypeScript engine, so an event log can be compared byte for byte against a golden one.

    public abstract class GameEvent
    {
        /// <summary>The event's name, as written in the "t" field of its JSON.</summary>
        public string T { get; }

        protected GameEvent(string t)
        {
            T = t;
        }

        protected abstract void WriteFields(JsonOut o);

        /// <summary>This event as JSON.stringify would write it.</summary>
        public string ToJson()
        {
            var o = new JsonOut();
            o.BeginObject().Key("t").Value(T);
            WriteFields(o);
            o.EndObject();
            return o.ToString();
        }

        protected static void WriteSource(JsonOut o, string key, SourceRef? source)
        {
            o.Key(key);
            if (source != null) source.Write(o);
            else o.Null();
        }

        protected static void WriteEntity(JsonOut o, string key, EntityRef? entity)
        {
            o.Key(key);
            if (entity.HasValue) entity.Value.Write(o);
            else o.Null();
        }
    }

    public sealed class RoundStarted : GameEvent
    {
        public int Round;
        public RoundStarted() : base(nameof(RoundStarted)) { }
        protected override void WriteFields(JsonOut o) => o.Key("round").Value(Round);
    }

    public sealed class PhaseStarted : GameEvent
    {
        public int Round;
        public Phase Phase;
        public PhaseStarted() : base(nameof(PhaseStarted)) { }
        protected override void WriteFields(JsonOut o) => o.Key("round").Value(Round).Key("phase").Value(Names.Of(Phase));
    }

    public sealed class WaveSpawned : GameEvent
    {
        public int Wave;
        public List<int> Units = new List<int>();
        public WaveSpawned() : base(nameof(WaveSpawned)) { }
        protected override void WriteFields(JsonOut o) => o.Key("wave").Value(Wave).Key("units").Ints(Units);
    }

    public sealed class UnitSpawned : GameEvent
    {
        public Unit Unit = null!;
        public string Cause = "";
        public UnitSpawned() : base(nameof(UnitSpawned)) { }

        protected override void WriteFields(JsonOut o)
        {
            o.Key("unit");
            Unit.Write(o);
            o.Key("cause").Value(Cause);
        }
    }

    public sealed class UnitMoved : GameEvent
    {
        public int Id;
        public Pos From;
        public Pos To;
        public List<Pos> Path = new List<Pos>();
        public MoveMode Mode;
        public UnitMoved() : base(nameof(UnitMoved)) { }

        protected override void WriteFields(JsonOut o)
        {
            o.Key("id").Value(Id).Key("from").Pos(From).Key("to").Pos(To).Key("path").PosList(Path).Key("mode").Value(Names.Of(Mode));
        }
    }

    public sealed class AbilityUsed : GameEvent
    {
        public string Ability = "";
        public int Actor;
        public Pos? Target;
        public Pos? Target2;
        public string? Mode;
        public int Ap;
        public AbilityUsed() : base(nameof(AbilityUsed)) { }

        protected override void WriteFields(JsonOut o)
        {
            o.Key("ability").Value(Ability).Key("actor").Value(Actor);
            if (Target.HasValue) o.Key("target").Pos(Target.Value);
            if (Target2.HasValue) o.Key("target2").Pos(Target2.Value);
            o.OptText("mode", Mode);
            o.Key("ap").Value(Ap);
        }
    }

    public sealed class ProjectileFired : GameEvent
    {
        public SourceRef Source = null!;
        public Pos From;
        public List<Pos> Path = new List<Pos>();
        public EntityRef? Hit;
        public Pos? HitPos;
        /// <summary>Written only when the rules set it: projectiles say true or false, while the grapple's line leaves it out.</summary>
        public bool? Reflected;
        public ProjectileFired() : base(nameof(ProjectileFired)) { }

        protected override void WriteFields(JsonOut o)
        {
            WriteSource(o, "source", Source);
            o.Key("from").Pos(From).Key("path").PosList(Path);
            WriteEntity(o, "hit", Hit);
            o.Key("hitPos");
            if (HitPos.HasValue) o.Pos(HitPos.Value);
            else o.Null();
            if (Reflected.HasValue) o.Key("reflected").Value(Reflected.Value);
        }
    }

    public sealed class DamageDealt : GameEvent
    {
        public EntityRef Target;
        public string TargetDef = "";
        public Team TargetSide;
        public Pos Pos;
        public SourceRef Source = null!;
        public DamageType DamageType;
        public int Raw;
        public int Absorbed;
        /// <summary>Damage after armor: the size of the hit.</summary>
        public int Amount;
        /// <summary>HP actually removed (the hit without overkill). Summaries count this.</summary>
        public int HpLost;
        public int HpAfter;
        public bool Killed;
        public DamageDealt() : base(nameof(DamageDealt)) { }

        protected override void WriteFields(JsonOut o)
        {
            WriteEntity(o, "target", Target);
            o.Key("targetDef").Value(TargetDef).Key("targetSide").Value(Names.Of(TargetSide)).Key("pos").Pos(Pos);
            WriteSource(o, "source", Source);
            o.Key("damageType").Value(Names.Of(DamageType)).Key("raw").Value(Raw).Key("absorbed").Value(Absorbed).Key("amount").Value(Amount);
            o.Key("hpLost").Value(HpLost).Key("hpAfter").Value(HpAfter).Key("killed").Value(Killed);
        }
    }

    public sealed class Healed : GameEvent
    {
        public int Target;
        public SourceRef Source = null!;
        public int Amount;
        public int HpAfter;
        public Healed() : base(nameof(Healed)) { }

        protected override void WriteFields(JsonOut o)
        {
            o.Key("target").Value(Target);
            WriteSource(o, "source", Source);
            o.Key("amount").Value(Amount).Key("hpAfter").Value(HpAfter);
        }
    }

    public sealed class UnitDied : GameEvent
    {
        public int Id;
        public string Def = "";
        public Team Team;
        public UnitKind Kind;
        public Pos Pos;
        public SourceRef? Killer;
        public UnitDied() : base(nameof(UnitDied)) { }

        protected override void WriteFields(JsonOut o)
        {
            o.Key("id").Value(Id).Key("def").Value(Def).Key("team").Value(Names.Of(Team)).Key("kind").Value(Names.Of(Kind)).Key("pos").Pos(Pos);
            WriteSource(o, "killer", Killer);
        }
    }

    public sealed class ObjectPlaced : GameEvent
    {
        public BoardObject Object = null!;
        public SourceRef? Source;
        public ObjectPlaced() : base(nameof(ObjectPlaced)) { }

        protected override void WriteFields(JsonOut o)
        {
            o.Key("object");
            Object.Write(o);
            WriteSource(o, "source", Source);
        }
    }

    public sealed class ObjectDestroyed : GameEvent
    {
        public int Id;
        public ObjectKind Kind;
        public Pos Pos;
        public SourceRef? Killer;
        public ObjectDestroyed() : base(nameof(ObjectDestroyed)) { }

        protected override void WriteFields(JsonOut o)
        {
            o.Key("id").Value(Id).Key("kind").Value(Names.Of(Kind)).Key("pos").Pos(Pos);
            WriteSource(o, "killer", Killer);
        }
    }

    public sealed class ObjectRemoved : GameEvent
    {
        public int Id;
        public ObjectKind Kind;
        public Pos Pos;
        public RemovalReason Reason;
        public ObjectRemoved() : base(nameof(ObjectRemoved)) { }
        protected override void WriteFields(JsonOut o) => o.Key("id").Value(Id).Key("kind").Value(Names.Of(Kind)).Key("pos").Pos(Pos).Key("reason").Value(Names.Of(Reason));
    }

    public sealed class ObjectTriggered : GameEvent
    {
        public int Id;
        public ObjectKind Kind;
        public Pos Pos;
        public EntityRef? By;
        public ObjectTriggered() : base(nameof(ObjectTriggered)) { }

        protected override void WriteFields(JsonOut o)
        {
            o.Key("id").Value(Id).Key("kind").Value(Names.Of(Kind)).Key("pos").Pos(Pos);
            WriteEntity(o, "by", By);
        }
    }

    public sealed class AreaEffect : GameEvent
    {
        public AreaKind Kind;
        public List<Pos> Tiles = new List<Pos>();
        public SourceRef Source = null!;
        public AreaEffect() : base(nameof(AreaEffect)) { }

        protected override void WriteFields(JsonOut o)
        {
            o.Key("kind").Value(Names.Of(Kind)).Key("tiles").PosList(Tiles);
            WriteSource(o, "source", Source);
        }
    }

    public sealed class ChainArc : GameEvent
    {
        public Pos From;
        public Pos To;
        public SourceRef Source = null!;
        public ChainArc() : base(nameof(ChainArc)) { }

        protected override void WriteFields(JsonOut o)
        {
            o.Key("from").Pos(From).Key("to").Pos(To);
            WriteSource(o, "source", Source);
        }
    }

    public sealed class StatusApplied : GameEvent
    {
        public int Target;
        public StatusId Status;
        public int Value;
        public SourceRef Source = null!;
        public bool Refreshed;
        public bool Spread;
        public StatusApplied() : base(nameof(StatusApplied)) { }

        protected override void WriteFields(JsonOut o)
        {
            o.Key("target").Value(Target).Key("status").Value(Names.Of(Status)).Key("value").Value(Value);
            WriteSource(o, "source", Source);
            o.Key("refreshed").Value(Refreshed).Key("spread").Value(Spread);
        }
    }

    public sealed class StatusTick : GameEvent
    {
        public int Target;
        public StatusId Status;
        public int Remaining;
        public StatusTick() : base(nameof(StatusTick)) { }
        protected override void WriteFields(JsonOut o) => o.Key("target").Value(Target).Key("status").Value(Names.Of(Status)).Key("remaining").Value(Remaining);
    }

    public sealed class StatusExpired : GameEvent
    {
        public int Target;
        public StatusId Status;
        public StatusExpired() : base(nameof(StatusExpired)) { }
        protected override void WriteFields(JsonOut o) => o.Key("target").Value(Target).Key("status").Value(Names.Of(Status));
    }

    public sealed class StatusRemoved : GameEvent
    {
        public int Target;
        public StatusId Status;
        public string Reason = "";
        public StatusRemoved() : base(nameof(StatusRemoved)) { }
        protected override void WriteFields(JsonOut o) => o.Key("target").Value(Target).Key("status").Value(Names.Of(Status)).Key("reason").Value(Reason);
    }

    public sealed class EggHatched : GameEvent
    {
        public int Id;
        public Pos Pos;
        public string Host = "";
        public List<int> Spawned = new List<int>();
        public int Capped;
        public EggHatched() : base(nameof(EggHatched)) { }
        protected override void WriteFields(JsonOut o) => o.Key("id").Value(Id).Key("pos").Pos(Pos).Key("host").Value(Host).Key("spawned").Ints(Spawned).Key("capped").Value(Capped);
    }

    public sealed class DroneDocked : GameEvent
    {
        public int Id;
        public Pos Pos;
        public DroneDocked() : base(nameof(DroneDocked)) { }
        protected override void WriteFields(JsonOut o) => o.Key("id").Value(Id).Key("pos").Pos(Pos);
    }

    public sealed class DroneRedeployed : GameEvent
    {
        public int Id;
        public Pos Pos;
        public DroneRedeployed() : base(nameof(DroneRedeployed)) { }
        protected override void WriteFields(JsonOut o) => o.Key("id").Value(Id).Key("pos").Pos(Pos);
    }

    public sealed class Reloaded : GameEvent
    {
        public int Id;
        public int Ammo;
        public Reloaded() : base(nameof(Reloaded)) { }
        protected override void WriteFields(JsonOut o) => o.Key("id").Value(Id).Key("ammo").Value(Ammo);
    }

    public sealed class Sprinted : GameEvent
    {
        public int Movement;
        public Sprinted() : base(nameof(Sprinted)) { }
        protected override void WriteFields(JsonOut o) => o.Key("movement").Value(Movement);
    }

    public sealed class Purged : GameEvent
    {
        public int Id;
        public Purged() : base(nameof(Purged)) { }
        protected override void WriteFields(JsonOut o) => o.Key("id").Value(Id);
    }

    public sealed class Charged : GameEvent
    {
        public int Id;
        public Pos From;
        public Pos To;
        public Pos Dir;
        public EntityRef? Hit;
        public Charged() : base(nameof(Charged)) { }

        protected override void WriteFields(JsonOut o)
        {
            o.Key("id").Value(Id).Key("from").Pos(From).Key("to").Pos(To).Key("dir").Pos(Dir);
            WriteEntity(o, "hit", Hit);
        }
    }

    public sealed class HazardPrimed : GameEvent
    {
        public string Hazard = "";
        public int Step;
        public string Reason = "";
        public List<EntityRef> Victims = new List<EntityRef>();
        public HazardPrimed() : base(nameof(HazardPrimed)) { }
        protected override void WriteFields(JsonOut o) => o.Key("hazard").Value(Hazard).Key("step").Value(Step).Key("reason").Value(Reason).Key("victims").Entities(Victims);
    }

    public sealed class HazardHeld : GameEvent
    {
        public string Hazard = "";
        public int Step;
        public string Reason = "";
        public List<EntityRef> Victims = new List<EntityRef>();
        public HazardHeld() : base(nameof(HazardHeld)) { }
        protected override void WriteFields(JsonOut o) => o.Key("hazard").Value(Hazard).Key("step").Value(Step).Key("reason").Value(Reason).Key("victims").Entities(Victims);
    }

    public sealed class HazardFired : GameEvent
    {
        public string Hazard = "";
        public List<EntityRef> Victims = new List<EntityRef>();
        public List<Pos> Tiles = new List<Pos>();
        public HazardFired() : base(nameof(HazardFired)) { }
        protected override void WriteFields(JsonOut o) => o.Key("hazard").Value(Hazard).Key("victims").Entities(Victims).Key("tiles").PosList(Tiles);
    }

    public sealed class HazardCancelled : GameEvent
    {
        public string Hazard = "";
        public string Reason = "";
        public List<EntityRef> Victims = new List<EntityRef>();
        public HazardCancelled() : base(nameof(HazardCancelled)) { }
        protected override void WriteFields(JsonOut o) => o.Key("hazard").Value(Hazard).Key("reason").Value(Reason).Key("victims").Entities(Victims);
    }

    public sealed class IntercomLine : GameEvent
    {
        public string Line = "";
        public string Text = "";
        public IntercomLine() : base(nameof(IntercomLine)) { }
        protected override void WriteFields(JsonOut o) => o.Key("line").Value(Line).Key("text").Value(Text);
    }

    public sealed class ChainSuppressed : GameEvent
    {
        public string Effect = "";
        public string Source = "";
        public SuppressReason Reason;
        public ChainSuppressed() : base(nameof(ChainSuppressed)) { }
        protected override void WriteFields(JsonOut o) => o.Key("effect").Value(Effect).Key("source").Value(Source).Key("reason").Value(Names.Of(Reason));
    }

    public sealed class AiDecision : GameEvent
    {
        public int Id;
        public string Def = "";
        public string Summary = "";
        public List<AiOption>? Options;
        public AiDecision() : base("AIDecision") { }

        protected override void WriteFields(JsonOut o)
        {
            o.Key("id").Value(Id).Key("def").Value(Def).Key("summary").Value(Summary);
            if (Options == null) return;
            o.Key("options").BeginArray();
            foreach (var option in Options) option.Write(o);
            o.EndArray();
        }
    }

    public sealed class FightEnded : GameEvent
    {
        public FightResult Result;
        public string Cause = "";
        public int Round;
        public FightEnded() : base(nameof(FightEnded)) { }
        protected override void WriteFields(JsonOut o) => o.Key("result").Value(Names.Of(Result)).Key("cause").Value(Cause).Key("round").Value(Round);
    }

    /// <summary>Written as "Debug". Named DebugEvent so it cannot clash with UnityEngine.Debug in the client.</summary>
    public sealed class DebugEvent : GameEvent
    {
        public string Op = "";
        public string Detail = "";
        public DebugEvent() : base("Debug") { }
        protected override void WriteFields(JsonOut o) => o.Key("op").Value(Op).Key("detail").Value(Detail);
    }
}
