using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using YiGodhome.Bridge;

static class Program
{
    static void Assert(bool b, string message) { if (!b) throw new Exception(message); }
    static void BossDisplay()
    {
        foreach (var profile in EncounterProfile.All) {
            int expected = profile == EncounterProfile.SoulMaster || profile == EncounterProfile.SoulTyrant || profile == EncounterProfile.Xero || profile == EncounterProfile.AbsoluteRadiance ? 42 : 21;
            Assert(profile.ReflectedProjectileDamage == expected, "Outgoing reflection damage changed for " + profile.Key);
            Assert(EncounterProfile.ForScene(profile.Scene)?.ReflectedProjectileDamage == expected, "Scene selection lost fixed reflection damage");
            var alternate = EncounterProfile.ForScene(profile.Scene + "_V");
            if (alternate != null) Assert(alternate.ReflectedProjectileDamage == expected, "Alternate arena changed reflection damage");
        }
        Assert(!EncounterProfile.TraitorLord.UsesHealthNotches, "Traitor Lord retained retired notch mechanic");
        Assert(!EncounterProfile.NoEyes.UsesHealthNotches, "Accepted No Eyes notch policy regressed");
        var sly=EncounterProfile.ForScene("GG_Sly");
        Assert(!EncounterProfile.Sly.UsesHealthNotches && sly!=null && !sly.UsesHealthNotches,
            "Sly retained notch mechanic through scene selection");
        var rewardSession = Guid.NewGuid(); var reward = new KillRewardSettlement();
        var lethal = Event(rewardSession, Guid.NewGuid()); lethal.Kind = HitKind.Talisman;
        reward.Track(1, lethal);
        var lethalAck = new HitAck { Session = rewardSession, Sequence = 1, Disposition = HitDisposition.Applied, Health = 0, TargetKilled = true };
        using var rewardWire = new MemoryStream(); HitCodec.WriteAck(rewardWire, lethalAck); rewardWire.Position = 0;
        Assert(HitCodec.ReadAck(rewardWire).TargetKilled, "Native death proof lost on wire");
        Assert(reward.Resolve(lethalAck)?.Entity == lethal.Entity && reward.Resolve(lethalAck) == null, "Confirmed kill rewarded twice or lost retired identity");
        reward.Track(2, lethal);
        Assert(reward.Resolve(new HitAck { Session = rewardSession, Sequence = 2, Disposition = HitDisposition.Applied, Health = 0 }) == null, "Nonfatal zero HP rewarded");
        reward.Track(3, lethal);
        Assert(reward.Resolve(new HitAck { Session = rewardSession, Sequence = 3, Disposition = HitDisposition.Refused, Health = 0 }) == null, "Refused kill rewarded");
        reward.Track(4, lethal);
        Assert(reward.Resolve(new HitAck { Session = Guid.NewGuid(), Sequence = 4, Disposition = HitDisposition.Applied, Health = 0, TargetKilled = true }) == null, "Foreign session rewarded");
        Reject(() => HitCodec.ValidateAck(new HitAck { Session = rewardSession, Sequence = 1, Disposition = HitDisposition.Refused, Health = 0, TargetKilled = true }));
        Reject(() => HitCodec.ValidateAck(new HitAck { Session = rewardSession, Sequence = 1, Disposition = HitDisposition.Applied, Health = 1, TargetKilled = true }));
        var session = Guid.NewGuid(); var encounter = Guid.NewGuid(); int calls = 0;
        var blocked = Event(session, encounter);
        var ledger = new HitLedger(session, encounter, 25, 12);
        HitAck Pause(HitOutcome h) { calls++; return new HitAck { Session = session, Sequence = h.Sequence, Disposition = HitDisposition.Refused, Health = 1600, ParryPause = true }; }
        var first = ledger.Process(blocked, 1000, 1000, 1600, Pause);
        using var encoded = new MemoryStream(); HitCodec.WriteAck(encoded, first); encoded.Position = 0;
        Assert(HitCodec.ReadAck(encoded).ParryPause, "Native parry feedback lost on wire");
        first.ParryPause = false;
        Assert(ledger.Process(blocked, 1000, 1000, 1600, Pause).ParryPause && calls == 1, "Cached block feedback mutated or native block replayed");
        var invalidPause = new HitAck { Session = session, Sequence = 1, Disposition = HitDisposition.Applied, Health = 1600, ParryPause = true };
        Reject(() => HitCodec.ValidateAck(invalidPause));
        var reflected = Event(session, encounter, 2); reflected.Kind = HitKind.ProjectileReflected; reflected.Damage = 0; reflected.AttackId = 7;
        HitAck Reflection(HitOutcome h) { calls++; return new HitAck { Session = session, Sequence = h.Sequence, Disposition = HitDisposition.Applied, Health = 1600 }; }
        ledger.Process(reflected,1000,1000,1600,Reflection); ledger.Process(reflected,1000,1000,1600,Reflection);
        Assert(calls == 2, "Reflected projectile replayed its native redirection");
        var parried=Event(session,encounter,3);parried.Kind=HitKind.StrikeParried;parried.Damage=0;parried.AttackId=11;
        using(var wire=new MemoryStream()) {HitCodec.Write(wire,parried);wire.Position=0;Assert(HitCodec.Read(wire).Kind==HitKind.StrikeParried,"Ordinary native defense receipt lost its kind");}
        ledger.Process(parried,1000,1000,1600,Reflection);ledger.Process(parried,1000,1000,1600,Reflection);
        Assert(calls==3,"Ordinary native defense receipt replayed its boss reaction");
        parried.Damage=1;Reject(()=>HitCodec.Validate(parried));parried.Damage=0;
        parried.AttackId=0;Reject(()=>HitCodec.Validate(parried));parried.AttackId=11;
        var settlement = new InjurySettlement();
        settlement.Track(10, 264); settlement.Track(11, 64);
        Assert(settlement.Resolve(10, HitDisposition.Refused) == 264, "Refused talisman lost injury");
        Assert(settlement.Resolve(10, HitDisposition.Refused) == 0, "Duplicate acknowledgement duplicated injury");
        Assert(settlement.Resolve(11, HitDisposition.Applied) == 0, "Applied talisman restored consumed injury");
        settlement.Track(12, 100);
        Assert(settlement.Resolve(12, HitDisposition.Stale) == 100, "Stale talisman lost injury");
        Assert(new InjurySettlement().Resolve(12, HitDisposition.Refused) == 0, "Old target injury crossed generation");
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity }) Reject(() => settlement.Track(13, invalid));
        var full = new BossHealthProjection(450, 450, 0);
        Assert(full.Solid == 1 && full.Total == 1, "Full host health lost");
        var injured = new BossHealthProjection(450, 450, 100);
        Assert(injured.Total == 1 && Math.Abs(injured.Injury - 52.5f) < 0.001f && injured.Solid < 1, "Injury must not subtract realized HP");
        var realized = new BossHealthProjection(397, 450, 0);
        Assert(realized.Solid == realized.Total && realized.Total < injured.Total, "Realization must consume the native injury layer");
        var capped = new BossHealthProjection(10, 450, 100);
        Assert(capped.Injury == 10 && capped.Solid == 0, "Injury exceeds remaining host HP");
        var dead = new BossHealthProjection(0, 450, 100);
        Assert(dead.Total == 0 && dead.Solid == 0 && dead.Injury == 0, "Dead target retains health");
        foreach (float invalid in new[] { -1f, float.NaN, float.PositiveInfinity })
        {
            bool rejected = false;
            try { _ = new BossHealthProjection(450, 450, invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Assert(rejected, "Invalid native injury accepted");
        }
    }
    static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; } catch (EndOfStreamException) { return; } catch (InvalidOperationException) { return; }
        throw new Exception("Invalid hit operation accepted");
    }
    static HitOutcome Event(Guid session, Guid encounter, long sequence = 1) => new HitOutcome { Session = session, Encounter = encounter, Sequence = sequence, GeometryFrame = 25, HostTick = 1000, NativeFrame = 40, Entity = 12, Dealer = -23, Kind = HitKind.Melee, Damage = 40, Direction = 180 };
    static void Summons()
    {
        var session=Guid.NewGuid(); var encounter=Guid.NewGuid(); int applications=0; long liveGeneration=2;
        var ledger=new HitLedger(session,encounter,25,12,h=>h.Entity==80 && h.TargetActivation>0 && h.TargetActivation<=liveGeneration);
        HitAck Apply(HitOutcome h) { if(h.TargetActivation==liveGeneration) applications++; return new HitAck {Session=session,Sequence=h.Sequence,Disposition=h.TargetActivation==liveGeneration?HitDisposition.Applied:HitDisposition.Refused,Health=8}; }
        var old=Event(session,encounter); old.Entity=80; old.TargetActivation=1;
        Assert(ledger.Process(old,1000,1000,8,Apply).Disposition==HitDisposition.Refused && applications==0,"Recycled minion took stale damage");
        var live=Event(session,encounter,2); live.Entity=80; live.TargetActivation=2;
        ledger.Process(live,1000,1000,8,Apply); ledger.Process(live,1000,1000,8,Apply);
        Assert(applications==1,"Duplicate minion hit applied twice");
        var future=Event(session,encounter,3); future.Entity=80; future.TargetActivation=3; Reject(()=>ledger.Process(future,1000,1000,8,Apply));
        var boss=Event(session,encounter,3); ledger.Process(boss,1000,1000,650,h=>new HitAck {Session=session,Sequence=h.Sequence,Disposition=HitDisposition.Applied,Health=600});
        Console.WriteLine("PASS: mixed boss/minion ordered ledger, stale generations, duplicate outcomes and unannounced targets.");
    }
    static void DestroyedReceiverRace()
    {
        var session=Guid.NewGuid();var encounter=Guid.NewGuid();double now=1;bool live=true;
        var receipts=new RetiredTargetReceipts();int damage=0,counters=0;long activation=1;
        var ledger=new HitLedger(session,encounter,25,12,h=>h.Entity==80 && (live && h.TargetActivation==activation || receipts.Recognizes(h.Entity,h.TargetActivation,now)));
        HitAck Apply(HitOutcome h) {
            bool current=live && h.Entity==80 && h.TargetActivation==activation;
            bool applied=false;
            if(current && h.Kind==HitKind.Talisman){damage++;live=false;receipts.Retire(80,activation,now);applied=true;}
            else if(current && h.Kind==HitKind.TaiChiKick)counters++;
            return new HitAck {Session=session,Sequence=h.Sequence,Health=0,TargetKilled=applied,Disposition=applied?HitDisposition.Applied:HitDisposition.Refused};
        }
        var explosion=Event(session,encounter);explosion.Entity=80;explosion.TargetActivation=1;explosion.Kind=HitKind.Talisman;explosion.Damage=160;
        var kick=Event(session,encounter,2);kick.Entity=80;kick.TargetActivation=1;kick.Kind=HitKind.TaiChiKick;kick.Damage=0;kick.AttackId=56;
        var melee=Event(session,encounter,3);melee.Entity=80;melee.TargetActivation=1;
        Assert(ledger.Process(explosion,1000,1000,21,Apply).TargetKilled,"Talisman killing blow not recorded");
        now+=.014;
        Assert(ledger.Process(kick,1014,1000,0,Apply).Disposition==HitDisposition.Refused,"Kick after native destruction failed to settle");
        Assert(ledger.Process(melee,1014,1000,0,Apply).Disposition==HitDisposition.Refused,"Companion melee after native destruction failed to settle");
        live=true;activation=2;
        var oldKick=Event(session,encounter,4);oldKick.Entity=80;oldKick.TargetActivation=1;oldKick.Kind=HitKind.TaiChiKick;oldKick.Damage=0;oldKick.AttackId=56;
        Assert(ledger.Process(oldKick,1014,1000,0,Apply).Disposition==HitDisposition.Refused && counters==0,"Old generation counter touched reused body");
        now=2.1;
        Assert(!receipts.Recognizes(80,1,now),"Retired receipt did not expire");
        Assert(ledger.Process(explosion,2100,1000,0,Apply).TargetKilled && damage==1,"Cached lethal acknowledgement reapplied or was lost after receipt expiry");
        Assert(ledger.Process(kick,2100,1000,0,Apply).Disposition==HitDisposition.Refused && counters==0,"Cached refusal failed after receipt expiry");
        var foreign=Event(Guid.NewGuid(),encounter);foreign.Entity=80;foreign.Kind=HitKind.Talisman;Reject(()=>ledger.Process(foreign,2100,1000,0,Apply));
        var unknown=Event(session,encounter,5);unknown.Entity=81;Reject(()=>ledger.Process(unknown,2100,1000,0,Apply));
        var future=Event(session,encounter,5);future.Entity=80;future.TargetActivation=3;Reject(()=>ledger.Process(future,2100,1000,0,Apply));
        Assert(!new RetiredTargetReceipts().Recognizes(80,1,1.01),"New encounter inherited a retired receipt");
        for(int i=0;i<256;i++)receipts.Retire(100+i,1,3);
        bool bounded=false;try{receipts.Retire(500,1,3);}catch(InvalidOperationException){bounded=true;}
        Assert(bounded,"Receipt retention exceeded its fixed budget");
        receipts.Retire(500,1,4.1);Assert(receipts.Recognizes(500,1,4.2),"Expired receipt slots could not be reused");
        Assert(damage==1 && counters==0,"Lethal race replayed gameplay");
        Console.WriteLine("PASS: recorded talisman→destroyed receiver→late kick/melee race settles once; generation reuse, cached acks past expiry, foreign targets/encounters and bounded receipt reuse stay safe.");
    }
    static void RetiredDefense()
    {
        var session = Guid.NewGuid(); var encounter = Guid.NewGuid(); bool retired = false; int damageCalls = 0, counterCalls = 0;
        var ledger = new HitLedger(session,encounter,25,12,h => h.Entity == 80 && h.TargetActivation == 1);
        HitAck Apply(HitOutcome h) {
            if (h.Kind == HitKind.Melee) { damageCalls++; retired = true; }
            else if (!retired) counterCalls++;
            return new HitAck { Session=session, Sequence=h.Sequence, Health=0,
                Disposition=h.Kind == HitKind.Melee ? HitDisposition.Applied : HitDisposition.Refused };
        }
        var lethal = Event(session,encounter); lethal.Entity=80; lethal.TargetActivation=1;
        var kick = Event(session,encounter,2); kick.Entity=80; kick.TargetActivation=1; kick.Kind=HitKind.TaiChiKick; kick.Damage=0; kick.AttackId=5;
        Assert(ledger.Process(lethal,1000,1000,2,Apply).Disposition==HitDisposition.Applied,"Lethal damage was lost");
        Assert(ledger.Process(kick,1000,1000,0,Apply).Disposition==HitDisposition.Refused,"Retired-body defense did not settle safely");
        Assert(ledger.Process(kick,1000,1000,0,Apply).Disposition==HitDisposition.Refused && damageCalls==1 && counterCalls==0,"Retired-body replay repeated damage/counter");
        var future=Event(session,encounter,3); future.Entity=80; future.TargetActivation=2; Reject(()=>ledger.Process(future,1000,1000,0,Apply));
        var foreign=Event(session,Guid.NewGuid(),3); foreign.Entity=80; foreign.TargetActivation=1; Reject(()=>ledger.Process(foreign,1000,1000,0,Apply));
        var next=Event(session,encounter,3); ledger.Process(next,1000,1000,500,h=>new HitAck { Session=session,Sequence=h.Sequence,Health=500,Disposition=HitDisposition.Refused });
        Assert(damageCalls==1 && counterCalls==0,"Retired defense altered native phase ownership");
        Console.WriteLine("PASS: lethal damage followed by same-frame retired-body kick, immutable refusal replay, strict future/foreign identity and continued ordered ledger.");
    }
    static void Codec()
    {
        var h = Event(Guid.NewGuid(), Guid.NewGuid()); h.TargetActivation = 42; using var s = new MemoryStream(); HitCodec.Write(s, h);
        Assert(s.Length == HitCodec.OutcomeSize, "Hit wire size drift"); s.Position = 0; Assert(h.Same(HitCodec.Read(s)), "Hit round trip changed data");
        var bytes = s.ToArray(); for (int i = 0; i < bytes.Length; i++) { int length = i; Reject(() => HitCodec.Read(new MemoryStream(bytes, 0, length))); }
        var oldVersion = (byte[])bytes.Clone(); oldVersion[4] = 3; Reject(() => HitCodec.Read(new MemoryStream(oldVersion)));
        var wrongActivation = Event(h.Session, h.Encounter); wrongActivation.TargetActivation = -1; Reject(() => HitCodec.Validate(wrongActivation));
        foreach (Action<HitOutcome> corrupt in new Action<HitOutcome>[] { e => e.Session = Guid.Empty, e => e.Sequence = 0, e => e.GeometryFrame = -1, e => e.HostTick = 0, e => e.Entity = 0, e => e.Dealer = 0, e => e.Kind = (HitKind)0, e => e.Damage = float.NaN, e => e.Damage = float.PositiveInfinity, e => e.Damage = 0, e => e.Direction = 360 })
        { var bad = Event(h.Session, h.Encounter); corrupt(bad); Reject(() => HitCodec.Write(new MemoryStream(), bad)); }
        var counter = Event(h.Session, h.Encounter); counter.Kind = HitKind.UnboundedCounter; counter.Damage = 0; counter.AttackId = 9;
        using var c = new MemoryStream(); HitCodec.Write(c, counter); c.Position = 0;
        Assert(counter.Same(HitCodec.Read(c)), "Counter identity lost");
        counter.Kind = HitKind.TaiChiKick;
        using var kick = new MemoryStream(); HitCodec.Write(kick, counter); kick.Position = 0;
        Assert(counter.Same(HitCodec.Read(kick)), "Native kick identity lost");
        counter.Kind = HitKind.ProjectileResolved;
        using var impact = new MemoryStream(); HitCodec.Write(impact, counter); impact.Position = 0;
        Assert(counter.Same(HitCodec.Read(impact)), "Projectile collider/activation identity lost");
        counter.Kind=HitKind.StrikeDeflected;
        using var trap=new MemoryStream();HitCodec.Write(trap,counter);trap.Position=0;
        var deflected=HitCodec.Read(trap);
        Assert(deflected.Kind==HitKind.StrikeDeflected && deflected.Kind!=HitKind.StrikeParried,"Trap reflection authorization collapsed into ordinary parry");
        var legacy=trap.ToArray();legacy[4]=5;Reject(()=>HitCodec.Read(new MemoryStream(legacy)));
        deflected.Damage=1;Reject(()=>HitCodec.Validate(deflected));deflected.Damage=0;
        deflected.AttackId=0;Reject(()=>HitCodec.Validate(deflected));
        counter.AttackId = 0; Reject(() => HitCodec.Write(new MemoryStream(), counter));
        counter.AttackId = 9; counter.Damage = 1; Reject(() => HitCodec.Write(new MemoryStream(), counter));
        counter.Damage = 0;
        var counters = new HitLedger(h.Session, h.Encounter, 25, 12); int interruptions = 0;
        HitAck Interrupt(HitOutcome e) { interruptions++; return new HitAck { Session = e.Session, Sequence = e.Sequence, Disposition = HitDisposition.Applied, Health = 650 }; }
        Assert(counters.Process(counter, 1001, 1000, 650, Interrupt).Health == 650, "Counter changed host health");
        counters.Process(counter, 9000, 1000, 650, Interrupt);
        Assert(interruptions == 1, "Counter replay interrupted twice");
        counter.AttackId = 10; Reject(() => counters.Process(counter, 1001, 1000, 650, Interrupt));
        counter.Sequence = 2;
        Assert(counters.Process(counter, 1251, 1000, 650, Interrupt).Disposition == HitDisposition.Stale && interruptions == 1, "Stale counter interrupted new attack");
        var ack = new HitAck { Session = h.Session, Sequence = 1, Disposition = HitDisposition.Applied, Health = 629 };
        using var a = new MemoryStream(); HitCodec.WriteAck(a, ack); Assert(a.Length == HitCodec.AckSize, "Ack wire size drift"); a.Position = 0;
        Assert(HitCodec.ReadAck(a).Health == 629, "Ack changed health");
        bytes = a.ToArray(); for (int i = 0; i < bytes.Length; i++) { int length = i; Reject(() => HitCodec.ReadAck(new MemoryStream(bytes, 0, length))); }
    }
    static void Ledger()
    {
        Guid session = Guid.NewGuid(), encounter = Guid.NewGuid(); int calls = 0, health = 650;
        HitAck Apply(HitOutcome e) { calls++; health -= 21; return new HitAck { Session = e.Session, Sequence = e.Sequence, Disposition = HitDisposition.Applied, Health = health }; }
        var ledger = new HitLedger(session, encounter, 25, 12); var first = Event(session, encounter);
        Assert(ledger.Process(first, 1001, 1000, health, Apply).Health == 629, "Native callback not applied");
        var duplicate = ledger.Process(first, 9000, 1000, health, Apply); duplicate.Health = 1;
        Assert(calls == 1 && ledger.Process(first, 9000, 1000, health, Apply).Health == 629, "Duplicate replay or mutable acknowledgement");
        var changed = Event(session, encounter); changed.Damage = 80; Reject(() => ledger.Process(changed, 1001, 1000, health, Apply));
        changed = Event(session, encounter); changed.AttackId = 2; Reject(() => ledger.Process(changed, 1001, 1000, health, Apply));
        Reject(() => ledger.Process(Event(session, encounter, 3), 1001, 1000, health, Apply));
        Reject(() => ledger.Process(Event(Guid.NewGuid(), encounter, 2), 1001, 1000, health, Apply));
        var stale = ledger.Process(Event(session, encounter, 2), 1251, 1000, health, Apply);
        Assert(stale.Disposition == HitDisposition.Stale && calls == 1, "Stale event applied");
        var refused = ledger.Process(Event(session, encounter, 3), 1001, 1000, health, e => new HitAck { Session = session, Sequence = e.Sequence, Disposition = HitDisposition.Refused, Health = health });
        Assert(refused.Health == 629, "Refused event changed health");
        Reject(() => ledger.Process(Event(session, encounter, 4), 999, 1000, health, Apply));
        Reject(() => ledger.Process(Event(session, encounter, 4), 1001, 1000, health, e => { calls++; throw new InvalidOperationException("Uncertain native effect"); }));
        Reject(() => ledger.Process(Event(session, encounter, 4), 1001, 1000, health, Apply));
        Assert(calls == 2, "Uncertain native effect retried");
    }
    static int Main()
    {
        try { NativeDelta(); BossDisplay(); Codec(); Summons(); RetiredDefense(); DestroyedReceiverRace(); Ledger(); Exchange(); Generations(); LostAck(); WrongAck(); Overflow(); Console.WriteLine("PASS: native damage/float transfer noise, hit/ack bounded codecs, all truncations, target identity, duplicate/reordered/stale/refused outcomes, immutable replay, uncertain callback, real ordered pipe, main-thread dispatch, isolated encounter pipes, lost ack after apply, wrong ack, overflow and disconnect."); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static void NativeDelta()
    {
        Assert(ResolvedDamage.Loss(3113.151f, 41.07798f, 3049.151f, 105.078f) == 0, "Native FooHit transfer rounding became damage");
        Assert(ResolvedDamage.Loss(3500, 0, 3420, 0) == 80, "Third combo damage changed");
        Assert(ResolvedDamage.Loss(3000, 192, 3000, 0) == 192, "Native talisman explosion lost injury conversion");
        Assert(ResolvedDamage.Loss(3000, 0, 3000.5f, 0) == -0.5f, "Native restoration became damage");
        Assert(ResolvedDamage.Loss(3500, 0, 3499.99f, 0) > 0.009f, "Small real damage was suppressed");
    }
    static bool Until(Func<bool> condition) => SpinWait.SpinUntil(condition, 5000);
    static void Exchange()
    {
        Guid id = Guid.NewGuid(), encounter = Guid.NewGuid();
        using var ns = new HitEndpoint(id, Role.NineSols); using var hk = new HitEndpoint(id, Role.HollowKnight);
        var ledger = new HitLedger(id, encounter, 25, 12); int health = 650, calls = 0, received = 0;
        int mainThread = Thread.CurrentThread.ManagedThreadId;
        HitAck Apply(HitOutcome e)
        {
            Assert(Thread.CurrentThread.ManagedThreadId == mainThread, "Native callback on IO thread"); calls++; health -= 21;
            return new HitAck { Session = id, Sequence = e.Sequence, Disposition = HitDisposition.Applied, Health = health };
        }
        for (int i = 0; i < 3; i++) Assert(ns.Submit(Event(id, encounter)) == i + 1, "Publisher sequence incorrect");
        Thread.Sleep(100); Assert(calls == 0, "Event applied before main-thread dispatch");
        Assert(Until(() => {
            hk.ProcessPending(e => ledger.Process(e, 1001, 1000, health, Apply));
            while (ns.TryGetAck(out var ack)) { received++; Assert(ack.Sequence == received && ack.Health == 650 - received * 21, "Ack order or health changed"); }
            return received == 3;
        }), "Ordered hits not acknowledged");
        Assert(calls == 3 && health == 587, "Native calls repeated or lost");
        ns.Dispose(); Assert(Until(() => hk.Stopped), "Disconnect did not stop consumer");
    }
    static void Generations()
    {
        Guid id = Guid.NewGuid(), oldId = Guid.NewGuid(), nextId = Guid.NewGuid();
        using var oldNs = new HitEndpoint(id, Role.NineSols, oldId); using var oldHk = new HitEndpoint(id, Role.HollowKnight, oldId);
        using var nextNs = new HitEndpoint(id, Role.NineSols, nextId); using var nextHk = new HitEndpoint(id, Role.HollowKnight, nextId);
        oldNs.Submit(Event(id, oldId)); nextNs.Submit(Event(id, nextId));
        bool oldAck = false, nextAck = false;
        HitAck Reply(HitOutcome h, Guid expected, int hp) { Assert(h.Encounter == expected, "Old encounter crossed into new pipe"); return new HitAck { Session = id, Sequence = h.Sequence, Disposition = HitDisposition.Applied, Health = hp }; }
        Assert(Until(() => {
            oldHk.ProcessPending(h => Reply(h, oldId, 1)); nextHk.ProcessPending(h => Reply(h, nextId, 629));
            if (oldNs.TryGetAck(out var a)) { Assert(a.Health == 1, "Old generation ack mixed"); oldAck = true; }
            if (nextNs.TryGetAck(out var b)) { Assert(b.Health == 629, "New generation ack mixed"); nextAck = true; }
            return oldAck && nextAck;
        }), "Separate encounter pipes did not exchange");
    }
    static void LostAck()
    {
        Guid id = Guid.NewGuid(), encounter = Guid.NewGuid();
        using var ns = new HitEndpoint(id, Role.NineSols); using var hk = new HitEndpoint(id, Role.HollowKnight);
        var ledger = new HitLedger(id, encounter, 25, 12); int calls = 0;
        ns.Submit(Event(id, encounter));
        Assert(Until(() => {
            hk.ProcessPending(e => ledger.Process(e, 1001, 1000, 650, request => {
                calls++; hk.Dispose(); // Native side effect occurred, but the pipe cannot send its ack.
                return new HitAck { Session = id, Sequence = request.Sequence, Disposition = HitDisposition.Applied, Health = 629 };
            }));
            return calls == 1;
        }), "Lost-ack fixture did not apply");
        Assert(Until(() => ns.Stopped) && !ns.TryGetAck(out _), "Disconnected publisher invented an acknowledgement");
        foreach (var phase in new[] { EncounterPhase.Victory, EncounterPhase.Defeated, EncounterPhase.Restoring }) {
            var lifetime = new EncounterLifetime();
            lifetime.Observe(new EncounterStatus(encounter, 25, EncounterPhase.Active), 0);
            lifetime.Observe(new EncounterStatus(encounter, 25, phase), 1);
            Assert(lifetime.Check(2, ns.Stopped, 1, true) != null, "Real lost acknowledgement hung terminal return");
        }
        Reject(() => ns.Submit(Event(id, encounter)));
        Assert(ledger.Process(Event(id, encounter), 1001, 1000, 629, _ => { calls++; throw new Exception("Replayed native hit"); }).Health == 629 && calls == 1, "Lost acknowledgement replayed damage");
    }
    static void WrongAck()
    {
        Guid id = Guid.NewGuid(); using var ns = new HitEndpoint(id, Role.NineSols);
        using var peer = new NamedPipeClientStream(".", "YiGodhome." + id.ToString("N") + ".hits", PipeDirection.InOut);
        peer.Connect(5000); ns.Submit(Event(id, Guid.NewGuid())); var request = HitCodec.Read(peer);
        HitCodec.WriteAck(peer, new HitAck { Session = id, Sequence = request.Sequence + 1, Disposition = HitDisposition.Applied, Health = 1 });
        Assert(Until(() => ns.Stopped) && !ns.TryGetAck(out _), "Unsent acknowledgement accepted");
    }
    static void Overflow()
    {
        Guid id = Guid.NewGuid(); using var ns = new HitEndpoint(id, Role.NineSols);
        for (int i = 0; i < 64; i++) ns.Submit(Event(id, id));
        Reject(() => ns.Submit(Event(id, id))); Assert(ns.Stopped, "Overflow did not fail closed");
    }
}
