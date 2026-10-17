using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using YiGodhome.Bridge;

static class Program
{
    static void RadiancePresentationContracts()
    {
        var ending=new YiGodhome.RadianceEndVisibility();
        Assert(!ending.Request("GG_Workshop",20) && !ending.Hidden(20),"Late victory hid Hall before an arena lease");
        Assert(ending.Request("GG_Radiance",21) && ending.Hidden(21),"Radiance ending did not hide Yi");
        Assert(ending.Request("GG_Radiance",21) && ending.Hidden(21),"Repeated ending changed lease");
        Assert(!ending.Request("GG_Workshop",22) && !ending.Hidden(22),"Retained ending source hid return room");
        Assert(!ending.Request("GG_Workshop",22) && !ending.Hidden(22),"Repeated stale source rearmed Hall hide");
        Assert(!ending.Hidden(21),"Retired arena lease revived after return");
        Assert(ending.Request("GG_Radiance",23) && ending.Hidden(23),"Next Radiance attempt could not hide");
        Assert(!ending.Hidden(24),"Scene change retained hide lease");

        Console.WriteLine("PASS: Radiance ending hides only its arena; late/repeated Hall requests cannot block statue return, scene changes expire and subsequent fights rearm.");
        Assert(EncounterProfile.AbsoluteRadiance.UsesHealthNotches,"Radiance native phase notches still disabled");
        var gates=new[]{2600,2150,1850,1100,720};HealthNotches.Validate(gates,3000);
        foreach(int gate in gates) {
            Assert(Math.Abs(HealthNotches.BarFraction(gate,3000,2282)-(gate-718)/2282f)<.00001f,"Radiance notch disagrees with visible HP reserve removal");
            Assert(HealthNotches.InjuryLimit(gate,gates)==0,"Radiance internal damage crossed an unopened native phase dam");
        }
        Assert(HealthNotches.BarFraction(500,1000,1000)==.5f,"Ordinary boss notch moved");
        Console.WriteLine("PASS: Radiance phase notches enabled, native injury dams preserved and display markers follow the corrected HP bar.");
        var full=RadiancePresentation.Health(3000,3000,720,false);
        Assert(full.Health==full.Maximum && full.Maximum==2282,"Radiance full bar includes scripted reserve");
        Assert(RadiancePresentation.Health(719,3000,720,false).Health==2,"Final readiness is inferred from HP");
        Assert(RadiancePresentation.Health(719,3000,720,true).Health==1,"Native final-hit window is not one unit");
        Assert(RadiancePresentation.Health(1000,3000,720,true).Health==1,"Nine-hit final gate leaves phantom HP");
        Assert(RadiancePresentation.Health(0,3000,720,true).Health==0,"Dead body has nonzero display HP");
        Assert(BossBarNames.Sisters(BossBarNames.SistersTitle("MANTIS_LORDS_MAIN"))=="Mantis Lords","Solo sister phase prematurely renamed");
        Assert(BossBarNames.Sisters(BossBarNames.SistersTitle("SISTERS_MAIN"))=="Sister's of Battle","Native trio title did not rename bar");
        var f=Frame(Guid.NewGuid());f.BarHealth=1;f.BarMaxHealth=2282;f.BossBarsReady=true;f.BossTitle=BossTitleVariant.SistersOfBattle;
        f.Summons=new[]{new SummonedTarget{Entity=123,Collider=456,Generation=1,Health=719,MaxHealth=3000,BossBar=true,BarHealth=1,BarMaxHealth=2282,Width=2,Height=2}};
        using var stream=new MemoryStream();ContactCodec.Write(stream,f);stream.Position=0;var copy=ContactCodec.Read(stream);
        var projected=copy.Summons[0].Project(copy);
        Assert(projected.Health==719 && projected.MaxHealth==3000 && projected.BarHealth==1 && projected.BarMaxHealth==2282,"Bar projection altered combat HP or lost display values");
        Assert(copy.BossTitle==BossTitleVariant.SistersOfBattle && projected.BossTitle==BossTitleVariant.SistersOfBattle && copy.BossBarsReady && projected.BossBarsReady,"Title authorization lost on paired wire or secondary boss projection");
        var invalidBars=stream.ToArray();invalidBars[invalidBars.Length-1]=2;
        Reject(()=>ContactCodec.Read(new MemoryStream(invalidBars)));
        f.BossBarsReady=false;using(var pending=new MemoryStream()) {
            ContactCodec.Write(pending,f);pending.Position=0;
            Assert(!ContactCodec.Read(pending).BossBarsReady,"Closed title gate became authorized during transport");
        }
        f.BarMaxHealth=-1;Reject(()=>ContactCodec.Validate(f));f.BarMaxHealth=2282;
        f.Summons[0].BossBar=false;Reject(()=>ContactCodec.Validate(f));f.Summons[0].BossBar=true;
        var old=stream.ToArray();old[4]=37;Reject(()=>ContactCodec.Read(new MemoryStream(old)));
        var rain=new AttackVolume{Id=1,Activation=1,Active=true,Damage=1,Category=AttackCategory.Projectile,ConsumeOnContact=true,
            ActionId=1,Traits=AttackTraits.Crimson|AttackTraits.Reflectable,RadianceRainCounter=true,Points=new float[]{0,0,1,0,1,2,0,2}};
        f.Volumes=new[]{rain};using var wire=new MemoryStream();ContactCodec.Write(wire,f);wire.Position=0;
        var blade=ContactCodec.Read(wire).Volumes[0];Assert(blade.RadianceRainCounter && blade.Points[0]==0 && blade.Points[5]==2,"Counter margin changed damaging contour");
        rain.Traits=AttackTraits.Reflectable;Reject(()=>AttackVolumes.Validate(f.Volumes));
        foreach(float scale in new[]{1f,30f}) {
            Assert(RadiancePresentation.RainCounterNear(true,true,.24f*scale,scale),"Rain counter margin missing");
            Assert(!RadiancePresentation.RainCounterNear(true,true,.26f*scale,scale),"Rain counter margin too large");
            Assert(!RadiancePresentation.RainCounterNear(false,true,.1f,scale),"Other move gained a counter margin");
            Assert(!RadiancePresentation.RainCounterNear(true,false,.1f,scale),"Ordinary parry gained a counter margin");
            Assert(!RadiancePresentation.RainCounterNear(true,true,float.NaN,scale),"Nonfinite counter proximity admitted");
        }
        Console.WriteLine("PASS: display-only Radiance reserve/final-hit HP, strict v38 roundtrip and rain-only native-counter proximity with unchanged damage contours.");
    }
    static void LedgeFacingAfterParry()
    {
        foreach(int wall in new[]{-1,1}) {
            Assert(AttachedParryFacing.Restore(wall,-wall,true,true,true,true),"Accepted ledge parry retained recoil-facing away from wall");
            Assert(!AttachedParryFacing.Restore(wall,wall,true,true,true,true),"Unchanged ledge facing was rewritten");
            Assert(!AttachedParryFacing.Restore(wall,-wall,false,true,true,true),"Rejected parry changed facing");
            Assert(!AttachedParryFacing.Restore(wall,-wall,true,false,true,true),"Released ledge still owned facing");
            Assert(!AttachedParryFacing.Restore(wall,-wall,true,true,false,true),"Detached wall still owned facing");
            Assert(!AttachedParryFacing.Restore(wall,-wall,true,true,true,false),"Detached actor still owned facing");
        }
        Assert(!AttachedParryFacing.Restore(0,-1,true,true,true,true),"Parry without an attachment snapshot changed facing");
        Assert(!ProjectileDefense.NativeEligible(false,false) && ProjectileDefense.NativeEligible(false,true),"Ledge correction changed imperfect/Ricochet rules");
        Console.WriteLine("PASS: accepted ledge parries preserve either wall-facing direction only while still attached; rejection, release, unchanged facing and imperfect/Ricochet rules stay native.");
    }
    static void RadianceHazardCheckpoints()
    {
        var frame=Frame(Guid.NewGuid());frame.HasHazardCheckpoint=true;
        foreach(float height in new float[]{18,47,83,128,141}) {
            frame.HazardCheckpointX=32;frame.HazardCheckpointY=height;
            using var wire=new MemoryStream();ContactCodec.Write(wire,frame);wire.Position=0;
            var read=ContactCodec.Read(wire);
            Assert(read.HasHazardCheckpoint && read.HazardCheckpointX==32 && read.HazardCheckpointY==height
                && read.GeometryFrame==frame.GeometryFrame && read.Encounter==frame.Encounter,"Native ascent checkpoint lost ground/room identity");
            var data=wire.ToArray();data[data.Length-18]=2;Reject(()=>ContactCodec.Read(new MemoryStream(data)));
            data=wire.ToArray();data[4]=34;Reject(()=>ContactCodec.Read(new MemoryStream(data)));
            Reject(()=>ContactCodec.Read(new MemoryStream(wire.ToArray(),0,(int)wire.Length-1)));
        }
        frame.HasHazardCheckpoint=false;Reject(()=>ContactCodec.Validate(frame));
        frame.HasHazardCheckpoint=true;
        foreach(float bad in new[]{float.NaN,float.PositiveInfinity,100001f}) {
            frame.HazardCheckpointY=bad;Reject(()=>ContactCodec.Validate(frame));
        }
        frame.HasHazardCheckpoint=false;frame.HazardCheckpointX=frame.HazardCheckpointY=0;ContactCodec.Validate(frame);
        foreach(float dx in new[]{-10000f,-25f,-.01f,0f,.01f,25f,10000f})
        foreach(float dy in new[]{-10000f,-25f,-.01f,0f,.01f,25f,10000f}) {
            var v=ProjectileReflection.Velocity(dx,dy);
            Assert(Math.Abs(Math.Sqrt((double)v.X*v.X+(double)v.Y*v.Y)-40)<.00001,"Reflected speed changed with native speed/direction");
            if(dx!=0 || dy!=0)Assert(v.X*dx+v.Y*dy>0 && Math.Abs(v.X*dy-v.Y*dx)<.1,"Fixed-speed reflection did not aim at receiver");
        }
        Assert(ProjectileReflection.Speed==40,"Global reflection no longer matches Radiance radial/orb baseline");
        Console.WriteLine("PASS: ascent checkpoint identities and fixed global40-unit reflection velocity across stationary/tiny/large/cardinal/diagonal directions.");
    }
    static void GrimmIndependentBats()
    {
        var frame=Frame(Guid.NewGuid());
        frame.Volumes=new AttackVolume[4];
        for(int i=0;i<4;i++) {
            var volume=new AttackVolume{Id=51+i,Activation=1,Category=AttackCategory.Projectile,Active=true,ConsumeOnContact=true,
                DamageGroupId=99,CounterGroupId=99,InitialDamageGroupHold=1,RepeatDamageGroupHold=true,
                ParryCooldown=1,CooldownOnDamage=true,Points=new float[]{0,0,1,0,1,1}};
            YiGodhome.GrimmFirebatStrike.Apply(volume,i<2?701:702);
            frame.Volumes[i]=volume;
        }
        using var wire=new MemoryStream();ContactCodec.Write(wire,frame);wire.Position=0;
        var read=ContactCodec.Read(wire).Volumes;
        foreach(long resolved in new long[]{701,702}) {
            var retired=new ActionRetirement();retired.Retire(resolved);retired.Retain(0,read);
            foreach(var volume in read) {
                Assert(retired.Contains(volume.ActionId)==(volume.ActionId==resolved),"Resolving a firebat retired a different bat");
                Assert(volume.DamageGroupId==0 && volume.CounterGroupId==0 && volume.InitialDamageGroupHold==0
                    && !volume.RepeatDamageGroupHold && volume.ParryCooldown==0 && !volume.CooldownOnDamage,
                    "Firebat inherited volley retirement");
            }
            var reused=new AttackVolume();YiGodhome.GrimmFirebatStrike.Apply(reused,703);
            Assert(!retired.Contains(reused.ActionId),"Reborrowed bat inherited a retired strike");
        }
        Console.WriteLine("PASS: production Grimm/NKG firebat policy keeps each bat independent through wire/retirement, combines its flash/flight and rejects inherited volley grouping.");
    }
    static void NailmasterRecoilWire()
    {
        foreach(bool doubleSwing in new[]{false,true}) foreach(bool heavy in new[]{false,true})
        foreach(var facing in new[]{AttackTraits.PushLeft,AttackTraits.PushRight}) {
            var traits=NailmasterRecoil.Traits(doubleSwing,heavy)|facing;
            var frame=Frame(Guid.NewGuid());
            frame.Volumes=new[]{new AttackVolume {Id=51,Activation=1,ActionId=7,Category=AttackCategory.Melee,Active=true,
                Traits=traits,Points=new float[]{0,0,1,0,1,1,0,1}}};
            using var wire=new MemoryStream();ContactCodec.Write(wire,frame);wire.Position=0;
            var read=ContactCodec.Read(wire).Volumes[0];
            Assert(read.Traits==traits,"Nailmaster recoil/facing policy changed in transport");
            Assert(NailmasterRecoil.Tumbles((read.Traits&AttackTraits.LargeParryPush)!=0,(read.Traits&AttackTraits.InitialParryPush)!=0)==(heavy&&!doubleSwing),
                "Double-swing increased push acquired tumble or heavy strike lost it");
        }
        var invalid=Frame(Guid.NewGuid());
        invalid.Volumes=new[]{new AttackVolume {Id=51,Activation=1,ActionId=7,Category=AttackCategory.Melee,Active=true,
            Traits=AttackTraits.InitialParryPush|AttackTraits.PushLeft,Points=new float[]{0,0,1,0,1,1,0,1}}};
        Reject(()=>ContactCodec.Validate(invalid));
        Console.WriteLine("PASS: production nailmaster double-swing/heavy/ordinary recoil survives strict wire validation in both facings; double swings never tumble and the reported invalid flag remains rejected.");
    }
    static void GorbPersistentSpears()
    {
        var frame=Frame(Guid.NewGuid());
        frame.Volumes=new[]{new AttackVolume{Id=51,Activation=1,ActionId=0,DamageGroupId=52,Category=AttackCategory.Projectile,
            Traits=AttackTraits.Reflectable,ConsumeOnContact=true,ParryCooldown=1,CooldownOnDamage=true,Points=new float[]{0,0,1,0,1,1}}};
        using var wire=new MemoryStream();ContactCodec.Write(wire,frame);wire.Position=0;
        var spear=ContactCodec.Read(wire).Volumes[0];
        Assert(spear.DamageGroupId==52 && spear.ParryCooldown==1 && spear.CooldownOnDamage,"Persistent Gorb spear lost its native injury hold/volley identity");
        Assert(!ProjectileDefense.Reflects(spear.Traits,true,false,ProjectileDefense.NativeEligible(false,false)),"Imperfect unjaded Gorb parry reflected a spear");
        Assert(ProjectileDefense.Reflects(spear.Traits,true,false,ProjectileDefense.NativeEligible(true,false))
            && ProjectileDefense.Reflects(spear.Traits,true,false,ProjectileDefense.NativeEligible(false,true)),"Perfect/Ricochet Gorb reflection regressed");
        Console.WriteLine("PASS: Gorb persistent-flight injury policy/volley survives transport; imperfect unjaded parry never reflects, perfect/Ricochet retains native eligibility.");
    }
    static void DefenderEruptions()
    {
        var sequence=new YiGodhome.DefenderEruptionSequence();long identity=0;
        Func<long> allocate=()=>++identity;
        sequence.Enter("Erupt Out",allocate);
        Assert(!sequence.HarmlessLanding("Erupt Out",22,22,42),"Rising eruption was disabled");
        Assert(!sequence.HarmlessLanding("Erupt Fall",32.001f,22,42),"Ordinary fall retired above its midpoint");
        Assert(sequence.HarmlessLanding("Erupt Fall",32,22,42),"Ordinary fall remained harmful at its midpoint");
        Assert(sequence.HarmlessLanding("Erupt Fall",33,22,42),"Collision correction rearmed a retired descent");
        Assert(sequence.HarmlessLanding("Erupt Land",22,22,42),"Ordinary landing rearmed body damage");
        sequence.Enter("Erupt Out First",allocate);
        Assert(!sequence.HarmlessLanding("Erupt Fall",33,22,42),"New ordinary launch inherited old landing retirement");
        sequence.Enter("Rage Roar",allocate);long warning=sequence.Sequence;
        Assert(!sequence.HarmlessLanding("Erupt Fall",22,22,42) && !sequence.HarmlessLanding("Erupt Land",22,22,42),"HP-triggered eruption was changed by ordinary fall cutoff");
        sequence.Enter("Set Rage",allocate);
        Assert(warning>0 && warning==sequence.Sequence,"Set Rage replayed the warning identity");
        long previous=0;
        for(int launch=0;launch<6;launch++) {
            foreach(var state in new[]{"Dive In 1","Dive In 2","Underground","Erupt Tunneling","Tunneling R","Set Max X","Tunneling L","Erupt Antic","Erupt Antic R","First?"})
                sequence.Enter(state,allocate);
            Assert(sequence.Sequence==warning,"Burrowing replaced the sequence warning");
            Assert(sequence.Enter(launch==0?"Erupt Out First":"Erupt Out",allocate),"Launch did not receive a strike");
            long strike=sequence.Strike;
            Assert(strike>previous && strike!=warning,"Repeated eruption reused a retired strike");
            foreach(var state in new[]{"Erupt Fall","Erupt Land","Rage In"})sequence.Enter(state,allocate);
            Assert(sequence.Sequence==warning && sequence.Strike==strike,"Fall/landing rearmed the same launch");
            previous=strike;
        }
        sequence.Enter("Roar?",allocate);Assert(sequence.Sequence==0,"Completed eruption left a stuck crimson sequence");
        sequence.Enter("Erupt Out",allocate);Assert(sequence.Sequence==0 && sequence.Strike>previous,"Ordinary eruption became rage or reused its strike");
        sequence.Enter("Rage Roar",allocate);Assert(sequence.Sequence>warning,"Later rage reused its old warning");
        sequence.Enter("Stun Set",allocate);Assert(sequence.Sequence==0,"Native interruption retained rage");
        var rolling=Frame(Guid.NewGuid());
        rolling.Volumes=new[]{new AttackVolume{Id=8,Activation=1,ActionId=17,Category=AttackCategory.Melee,Traits=AttackTraits.Crimson,
            Active=true,ParryCooldown=1,CooldownOnDamage=true,Points=new float[]{0,0,1,0,1,1}}};
        using(var stream=new MemoryStream()) {
            ContactCodec.Write(stream,rolling);stream.Position=0;var roundtrip=ContactCodec.Read(stream).Volumes[0];
            Assert(roundtrip.ParryCooldown==1 && roundtrip.CooldownOnDamage,"Rolling one-second injury/parry policy was lost in transport");
        }
        var cooldown=new TimedActionRetirement();cooldown.Hold(17,10,1);
        Assert(cooldown.Held(17,10.999f) && !cooldown.Held(17,11),"Rolling attack did not rearm at exactly one gameplay second");
        cooldown.Hold(17,11,1);Assert(cooldown.Held(17,11.999f) && !cooldown.Held(17,12),"Rolling attack could not repeat its injury cooldown");
        Console.WriteLine("PASS: ordinary Defender falls retire halfway down with a latched landing, rage is unchanged, and rolling injury/parry cooldown survives transport and rearms repeatedly at one second.");
        Console.WriteLine("PASS: Defender rage has one warning identity across six independent eruptions; fall/landing retain each hit, ordinary launch and native interruption remain separate.");
        var dung=new AttackVolume[9];
        for(int i=0;i<dung.Length;i++)dung[i]=new AttackVolume{Id=100+i,Activation=1,ActionId=200+i,Category=AttackCategory.Melee,
            Active=true,DamageGroupId=300,InitialDamageGroupHold=1,RepeatDamageGroupHold=true,Traits=AttackTraits.RetireOnParry,
            Points=new float[]{0,0,1,0,1,1}};
        rolling.Volumes=dung;
        using(var stream=new MemoryStream()) {
            ContactCodec.Write(stream,rolling);stream.Position=0;dung=ContactCodec.Read(stream).Volumes;
        }
        var damageGrace=new DamageGroupRetirement();var defended=new ActionRetirement();defended.Retire(dung[0].ActionId);
        Assert(!damageGrace.Resolve(300,false,20,1,true) && !defended.Contains(dung[1].ActionId),"One dung parry retired siblings or opened shared injury grace");
        int injuries=0;
        foreach(var ball in dung)if(!damageGrace.Contains(ball.DamageGroupId,20) && damageGrace.Resolve(ball.DamageGroupId,true,20,ball.InitialDamageGroupHold,ball.RepeatDamageGroupHold))injuries++;
        Assert(injuries==1 && !defended.Contains(17),"Nine simultaneous dung balls escaped injury grace or retired boss action");
        dung[8].ActionId=999;dung[8].Activation=2;damageGrace.Retain(dung,20.5f);
        Assert(damageGrace.Contains(300,20.999f) && !damageGrace.Resolve(300,true,20.5f,1,true),"New dung ball escaped current injury grace");
        Assert(!damageGrace.Contains(300,21) && damageGrace.Resolve(300,true,21,1,true),"Dung grace did not rearm after one second");
        Assert(damageGrace.Contains(300,21.999f) && !damageGrace.Contains(300,22),"Repeated dung damage did not renew one-second grace");
        Console.WriteLine("PASS: nine independent Defender balls admit one injury per second; parry retirement stays individual, new balls inherit live damage grace and boss action stays independent.");
    }
    static void ImmutableContourOwnership()
    {
        var cache = new ImmutableContourSnapshot<(float X, float Y, float Size, float Offset)>();
        var firstKey = (1f, 2f, 3f, 4f);
        Assert(!cache.TryGet(firstKey, out _), "Uninitialized contour cache returned points");
        var old = new float[] { 1, 2, 3, 4 };
        cache.Publish(firstKey, old);
        Assert(cache.TryGet(firstKey, out var retained) && ReferenceEquals(old, retained), "Unchanged contour was copied");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 8192; i++) cache.TryGet(firstKey, out _);
        Assert(GC.GetAllocatedBytesForCurrentThread() == before, "Warm contour lookup allocated");
        foreach (var changed in new[] { (1.000001f, 2f, 3f, 4f), (1f, 2.000001f, 3f, 4f), (1f, 2f, 3.000001f, 4f), (1f, 2f, 3f, 4.000001f) })
            Assert(!cache.TryGet(changed, out _), "Contour lookup tolerated a transform/shape change");
        var replacement = new float[] { 5, 6, 7, 8 };
        cache.Publish((5f, 2f, 3f, 4f), replacement);
        Assert(old[0] == 1 && retained[3] == 4 && cache.TryGet((5f, 2f, 3f, 4f), out var latest)
            && ReferenceEquals(latest, replacement), "Replacing contour modified an escaping prior frame");
        var separate = new ImmutableContourSnapshot<(float, float, float, float)>();
        Assert(!separate.TryGet(firstKey, out _), "Contour escaped its collider owner");
        Console.WriteLine("PASS: immutable contour snapshots reuse exact keys without allocation; changes replace rather than mutate queued frames and owners remain isolated.");
    }
    static void ValidationScratchSafety()
    {
        var volumes=new AttackVolume[AttackVolumes.MaxCount];
        for(int i=0;i<volumes.Length;i++)volumes[i]=new AttackVolume {Id=i+1,Category=AttackCategory.Melee,Points=new float[]{0,0,1,0,1,1,0,1}};
        var targets=new SummonedTarget[SummonedTargets.Maximum];
        for(int i=0;i<targets.Length;i++)targets[i]=new SummonedTarget{Entity=i+1,Collider=i+1,Generation=1,MaxHealth=100,Width=1,Height=1};
        AttackVolumes.Validate(volumes);SummonedTargets.Validate(targets);
        long start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<4096;i++){AttackVolumes.Validate(volumes);SummonedTargets.Validate(targets);}
        Assert(GC.GetAllocatedBytesForCurrentThread()==start,"Warmed maximum-size validation allocated scratch");
        int id=volumes[1].Id;volumes[1].Id=volumes[0].Id;Reject(()=>AttackVolumes.Validate(volumes));volumes[1].Id=id;
        id=targets[1].Entity;targets[1].Entity=targets[0].Entity;Reject(()=>SummonedTargets.Validate(targets));targets[1].Entity=id;
        AttackVolumes.Validate(volumes);SummonedTargets.Validate(targets);
        Exception? failure=null;var threads=new Thread[4];using var ready=new ManualResetEventSlim(false);
        for(int i=0;i<threads.Length;i++) {threads[i]=new Thread(()=>{try{ready.Wait();for(int j=0;j<1024;j++){AttackVolumes.Validate(volumes);SummonedTargets.Validate(targets);}}catch(Exception e){Interlocked.CompareExchange(ref failure,e,null);}});threads[i].Start();}
        ready.Set();foreach(var thread in threads)thread.Join();if(failure!=null)throw failure;
        Assert(volumes[1].Id==2 && targets[1].Entity==2 && volumes[0].Points[2]==1,"Validation mutated immutable input");
        Console.WriteLine("PASS: maximum-size validation allocates zero warmed scratch, rejects duplicates after reuse/failure and isolates concurrent caller threads without mutating frames.");
    }
    static void RadianceWindows()
    {
        var life=new SummonedTarget {Entity=901,Generation=7,Health=1,MaxHealth=1};
        var lives=new[]{life};
        Assert(ContactOwnerLease.Live(lives,901,7),"Live contact owner retired prematurely");
        Assert(!ContactOwnerLease.Live(lives,902,7) && !ContactOwnerLease.Live(lives,901,6),"Contact owner identity/generation alias");
        life.Health=0;Assert(!ContactOwnerLease.Live(lives,901,7),"Dead owner retained delayed contact");
        life.Generation=8;life.Health=1;
        Assert(!ContactOwnerLease.Live(lives,901,7) && ContactOwnerLease.Live(lives,901,8),"Pooled fresh life inherited dead contact identity");
        Assert(!ContactOwnerLease.Live(Array.Empty<SummonedTarget>(),901,8),"Removed owner remained live");
        var owned=new AttackVolume {Id=981,Activation=1,ActionId=121,Category=AttackCategory.Contact,ContactOwner=901,ContactOwnerGeneration=8,
            Points=new float[]{0,0,1,0,1,1,0,1},Active=true};
        using(var ownerWire=new MemoryStream()){using(var w=new BinaryWriter(ownerWire,System.Text.Encoding.UTF8,true))AttackVolumes.Write(w,new[]{owned});ownerWire.Position=0;
            using var r=new BinaryReader(ownerWire);var copy=AttackVolumes.Read(r)[0];Assert(copy.ContactOwner==901 && copy.ContactOwnerGeneration==8,"Contact owner metadata lost");}
        owned.ContactOwnerGeneration=0;Reject(()=>AttackVolumes.Validate(new[]{owned}));owned.ContactOwnerGeneration=8;
        owned.Category=AttackCategory.Projectile;Reject(()=>AttackVolumes.Validate(new[]{owned}));
        Assert(YiGodhome.RadianceAttackWindows.RainSequence("Rage1 Loop") && !YiGodhome.RadianceAttackWindows.RainSequence("Nail Top Sweep"),"Stationary rain escaped its authored rage sequence");
        Assert(YiGodhome.RadianceAttackWindows.ReflectedBladeDamage(true)==2*YiGodhome.RadianceAttackWindows.ReflectedBladeDamage(false),"Crimson rain damage is not twice fixed ordinary blade damage");
        Assert(YiGodhome.RadianceAttackWindows.ReflectedBladeDamage(false)==42 && YiGodhome.RadianceAttackWindows.ReflectedBladeDamage(true)==84,"Radiance reflection tuning lost fixed 42/84 damage");
        foreach(var choice in new[]{"Beam Sweep L","Beam Sweep R","Beam Sweep L 2","Beam Sweep R 2","Eye Beam Recover","Return"})
            Assert(!YiGodhome.RadianceAttackWindows.BodyWarning(choice,"Casting"),"Sweep/recovery leaked a Radiance body warning");
        Assert(YiGodhome.RadianceAttackWindows.BodyWarning("Eye Beam Wait","A1 Cast Antic") && YiGodhome.RadianceAttackWindows.BodyWarning("Eye Beam","Casting"),"Authored eye-beam warning missing");
        Assert(!YiGodhome.RadianceAttackWindows.BodyWarning("Eye Beam","Final Idle") && !YiGodhome.RadianceAttackWindows.BodyWarning("Eye Beam","Death Ready"),"Stale choice kept Radiance crimson at the finale");
        Assert(YiGodhome.RadianceAttackWindows.Ending("Knight Break Antic") && !YiGodhome.RadianceAttackWindows.Ending("Death Ready") && !YiGodhome.RadianceAttackWindows.Ending("Scream"),"Radiance terminal presentation confused final-hit readiness or ascent with completion");
        long beamId=100;Func<long> nextBeam=()=>++beamId;
        long stationary=YiGodhome.RadianceAttackWindows.BeamAction(false,50,nextBeam);
        Assert(stationary==50 && YiGodhome.RadianceAttackWindows.BeamAction(false,50,nextBeam)==50,"Rays within one stationary burst lost shared strike identity");
        var burstRetirement=new ActionRetirement();long firstBurst=0;
        foreach(var command in new[]{"EB 1","EB 2","EB 3"}) {
            Assert(YiGodhome.RadianceAttackWindows.BeamBurstState(command),"Stationary beam burst did not start its own strike");
            long strike=YiGodhome.RadianceAttackWindows.BeamAction(false,nextBeam(),nextBeam);
            Assert(!burstRetirement.Contains(strike),"One stationary burst retired a later burst");
            burstRetirement.Retire(strike);if(firstBurst==0)firstBurst=strike;
        }
        Assert(!YiGodhome.RadianceAttackWindows.BeamBurstState("Eb Extra Wait"),"Anticipation duplicated a stationary beam strike");
        long climbFirst=YiGodhome.RadianceAttackWindows.BeamAction(true,50,nextBeam);
        long climbSecond=YiGodhome.RadianceAttackWindows.BeamAction(true,50,nextBeam);
        var beamRetirement=new ActionRetirement();beamRetirement.Retire(climbFirst);
        Assert(climbFirst!=climbSecond && !beamRetirement.Contains(climbSecond) && !beamRetirement.Contains(stationary),"One ascent beam retired subsequent beams or stationary burst");
        var traits=AttackTraits.Crimson|AttackTraits.Reflectable;
        foreach(bool precise in new[]{false,true})foreach(bool ricochet in new[]{false,true}) {
            bool eligible=ProjectileDefense.NativeEligible(precise,ricochet);
            Assert(eligible==(precise||ricochet),"Imperfect parry reflected without Ricochet or perfect parry failed");
            Assert(ProjectileDefense.Reflects(AttackTraits.Reflectable,true,false,eligible)==eligible,"Ordinary projectile reflection bypassed native timing/jade gate");
            Assert(!ProjectileDefense.Reflects(AttackTraits.None,true,false,eligible),"Nonreflectable attack acquired deflection");
        }
        Assert(ProjectileDefense.Reflects(traits,false,true,true),"Native successful unbound counter did not deflect crimson blade");
        Assert(!ProjectileDefense.Reflects(traits,false,false,true) && !ProjectileDefense.Reflects(traits,false,true,false),"Damage/rejected counter created reflection");
        Assert(ProjectileDefense.Reflects(AttackTraits.Reflectable,true,false,true),"Ordinary native reflection changed");
        Assert(!ProjectileDefense.Reflects(AttackTraits.Crimson,false,true,true),"Nonreflectable crimson attack created reflection");
        var blades=new[]{new AttackVolume {Id=821,Activation=1,ActionId=1021,Category=AttackCategory.Projectile,Traits=traits,ConsumeOnContact=true,DamageGroupId=201,InitialDamageGroupHold=1,Points=new float[]{0,0,1,0,1,2,0,2}},
            new AttackVolume {Id=822,Activation=1,ActionId=1022,Category=AttackCategory.Projectile,Traits=traits,ConsumeOnContact=true,DamageGroupId=201,InitialDamageGroupHold=1,Points=new float[]{2,0,3,0,3,2,2,2}}};
        using(var bytes=new MemoryStream()) {
            AttackVolumes.Write(new BinaryWriter(bytes),blades);bytes.Position=0;
            var read=AttackVolumes.Read(new BinaryReader(bytes));
            Assert(read[0].CounterInjuryEntity==0 && read[1].CounterInjuryEntity==0,"Detached rain acquired internal injury owner");
            var retired=new ActionRetirement();retired.Retire(read[0].ActionId);
            Assert(!retired.Contains(read[1].ActionId),"Countering a blade retired its sibling rain");
        }
        blades[0].CounterInjuryEntity=10;Reject(()=>AttackVolumes.Validate(blades));blades[0].CounterInjuryEntity=0;
        blades[0].ActionId=0;Reject(()=>AttackVolumes.Validate(blades));blades[0].ActionId=1021;
        blades[0].ConsumeOnContact=false;Reject(()=>AttackVolumes.Validate(blades));blades[0].ConsumeOnContact=true;
        Console.WriteLine("PASS: detached crimson rain counter reflection, fixed double damage, no injury owner and independent blade retirement round-trip; malformed contracts rejected.");
        Assert(YiGodhome.RadianceAttackWindows.NailFlight(false,"Tween"),"Native moving sword comb lost its zero-Rigidbody-velocity hitbox");
        foreach(var state in new[]{"Init","Antic","Reset","Recycle"})
            Assert(!YiGodhome.RadianceAttackWindows.NailFlight(false,state),"Dormant/anticipating comb acquired a strike");
        Assert(YiGodhome.RadianceAttackWindows.NailFlight(true,null),"Independent Radiance sword flight lost its strike");
        Assert(YiGodhome.RadianceAttackWindows.OrbFlight("Chase Hero 2",true),"Final Radiance orb flight was excluded");
        Assert(!YiGodhome.RadianceAttackWindows.OrbFlight("Chase Hero 2",false),"Radiance-specific final flight leaked to other orb families");
        foreach(var state in new[]{"Impact","Impact pause","Dissipate","Recycle","Init"})
            Assert(!YiGodhome.RadianceAttackWindows.OrbFlight(state,true),"Radiance orb cleanup acquired a strike");
        Console.WriteLine("PASS: inherited sword-comb motion, independent sword flight and final Radiance orb admission exclude anticipation/cleanup.");
    }
    static void WireFloats()
    {
        using var reference = new MemoryStream(); using var candidate = new MemoryStream();
        using var standard = new BinaryWriter(reference); using var exact = new WireWriter(candidate);
        // Include signed zero, subnormals, infinities, NaN payloads and arbitrary bits.
        int[] boundaries = { 0, int.MinValue, 1, -1, 0x7f800000, unchecked((int)0xff800000), 0x7fc01234 };
        foreach (int bits in boundaries) { float value = BitConverter.Int32BitsToSingle(bits); standard.Write(value); exact.Write(value); }
        var random = new Random(9017);
        for (int i = 0; i < 10000; i++) {
            float value = BitConverter.Int32BitsToSingle(random.Next(int.MinValue, int.MaxValue));
            double wide = BitConverter.Int64BitsToDouble(random.NextInt64(long.MinValue, long.MaxValue));
            standard.Write(value); exact.Write(value); standard.Write(wide); exact.Write(wide);
        }
        standard.Flush(); exact.Flush();
        Assert(reference.Length == candidate.Length, "Floating wire length changed");
        byte[] before = reference.ToArray(), after = candidate.ToArray();
        for (int i = 0; i < before.Length; i++) Assert(before[i] == after[i], "Floating wire bit changed");
        Console.WriteLine("PASS: allocation-free floating writer matches standard wire bytes for signed zero, special values and 10000 arbitrary float/double pairs.");
    }
    static void NativeCircle()
    {
        var points = new float[128];
        for(int i=0;i<64;i++) { points[2*i]=80+(float)Math.Cos(i*Math.PI/32)*.515f; points[2*i+1]=7+(float)Math.Sin(i*Math.PI/32)*.515f; }
        var v = new AttackVolume { Id=801, Activation=1, Active=true, Category=AttackCategory.Projectile, CircleX=80, CircleY=7, CircleRadius=.515f, Points=points };
        using var bytes=new MemoryStream(); AttackVolumes.Write(new BinaryWriter(bytes),new[]{v}); bytes.Position=0;
        var read=AttackVolumes.Read(new BinaryReader(bytes))[0];
        Assert(read.CircleX==80 && read.CircleY==7 && read.CircleRadius==.515f && read.Points.Length==128,"Native circle lost its primitive or debug contour");
        v.CircleRadius=float.NaN; Reject(()=>AttackVolumes.Validate(new[]{v})); v.CircleRadius=.515f;
        v.CircleX+=.01f; Reject(()=>AttackVolumes.Validate(new[]{v})); v.CircleX=80;
        v.CircleRadius=0; Reject(()=>AttackVolumes.Validate(new[]{v})); v.CircleRadius=.515f;
        float saved=v.Points[2]; v.Points[2]=v.Points[0]; Reject(()=>AttackVolumes.Validate(new[]{v})); v.Points[2]=saved;
        Array.Reverse(v.Points); Reject(()=>AttackVolumes.Validate(new[]{v}));
        Console.WriteLine("PASS: explicit native circles retain contours and reject nonfinite, mismatched and malformed primitive metadata.");
    }
    static void Assert(bool b, string message) { if (!b) throw new Exception(message); }
    static void NativeCircleAdmission()
    {
        // Equal perpendicular 3D transform axes need not project to a circle
        // in the exported XY plane. This satisfied the old host admission test.
        var matrix=System.Numerics.Matrix4x4.CreateRotationY((float)(Math.PI/3));
        var axisX=System.Numerics.Vector3.TransformNormal(System.Numerics.Vector3.UnitX,matrix);
        var axisY=System.Numerics.Vector3.TransformNormal(System.Numerics.Vector3.UnitY,matrix);
        Assert(Math.Abs(axisX.Length()-axisY.Length())<.00001f && Math.Abs(System.Numerics.Vector3.Dot(axisX,axisY))<.00001f,"Counterexample did not satisfy legacy transform test");
        var points=new float[128];
        for(int i=0;i<64;i++) {
            var native=System.Numerics.Vector3.Transform(new System.Numerics.Vector3((float)Math.Cos(i*Math.PI/32),(float)Math.Sin(i*Math.PI/32),0),matrix);
            points[i*2]=80+native.X; points[i*2+1]=7+native.Y;
        }
        var v=new AttackVolume {Id=902,Activation=1,Active=true,Category=AttackCategory.Melee,Points=points,CircleX=80,CircleY=7,CircleRadius=1};
        Reject(()=>AttackVolumes.Validate(new[]{v}));
        Assert(!AttackVolumes.TryUseNativeCircle(v,80,7,1),"Projected ellipse was promoted to a circle");
        Assert(ReferenceEquals(points,v.Points) && v.CircleX==0 && v.CircleY==0 && v.CircleRadius==0,"Fallback changed the native contour or retained circle metadata");
        using var bytes=new MemoryStream(); AttackVolumes.Write(new BinaryWriter(bytes),new[]{v}); bytes.Position=0;
        var received=AttackVolumes.Read(new BinaryReader(bytes))[0];
        for(int i=0;i<points.Length;i++)Assert(received.Points[i]==points[i],"Fallback changed a transported native vertex");
        Assert(received.CircleRadius==0,"Fallback receiver still requested a circle collider");
        for(int i=0;i<64;i++){points[i*2]=80+(float)Math.Cos(i*Math.PI/32)*.515f;points[i*2+1]=7+(float)Math.Sin(i*Math.PI/32)*.515f;}
        Assert(AttackVolumes.TryUseNativeCircle(v,80,7,.515f),"Valid native circle lost its optimized collider");
        AttackVolumes.Validate(new[]{v});
        AttackVolumes.TryUseNativeCircle(v,80,7,.515f);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<8192;i++)if(!AttackVolumes.TryUseNativeCircle(v,80,7,.515f))throw new Exception("Warm native circle eligibility changed");
        Assert(GC.GetAllocatedBytesForCurrentThread()==before,"Circle eligibility allocated per-frame scratch");
        points[0]=float.NaN;
        Assert(!AttackVolumes.TryUseNativeCircle(v,80,7,.515f),"Malformed contour admitted circle metadata");
        Reject(()=>AttackVolumes.Validate(new[]{v}));
        Console.WriteLine("PASS: projected native ellipse uses unchanged polygon fallback; valid circles remain optimized, malformed wire geometry stays rejected, and warm admission allocates no scratch.");
    }
    static bool Until(Func<bool> condition) => SpinWait.SpinUntil(condition, 5000);
    static void CounterStreak()
    {
        var streak = new ConsecutiveCounters();
        streak.Begin(1); Assert(streak.Accept(1) && streak.Count == 1, "First counter lost");
        Assert(!streak.Accept(1) && !streak.Accept(2) && streak.Count == 1, "Duplicate/foreign counter advanced streak");
        streak.Begin(2); streak.Begin(3);
        Assert(streak.Count == 0 && !streak.Accept(2), "Missed dive/stale receipt preserved streak");
        Assert(streak.Accept(3), "Current counter refused");
        streak.Begin(4); Assert(streak.Accept(4) && streak.Count == 2, "Consecutive counter lost");
        streak.Begin(5); Assert(streak.Accept(5) && streak.Count == 3, "Third consecutive counter lost");
        streak.Reset(); Assert(streak.Count == 0 && !streak.Accept(5), "Pattern reset retained old counter");
        Console.WriteLine("PASS: consecutive counters reset on missed strikes and reject stale/duplicate acknowledgements.");
    }
    static void BurnPolicy()
    {
        var burn = new AttackVolume { Id=71, Activation=1, ActionId=3, Active=true, Damage=0, Category=AttackCategory.Burn, Points=new float[]{0,0,2,0,2,1,0,1} };
        using var wire=new MemoryStream(); AttackVolumes.Write(new BinaryWriter(wire),new[]{burn}); wire.Position=0;
        var decoded=AttackVolumes.Read(new BinaryReader(wire))[0];
        Assert(decoded.Category==AttackCategory.Burn && decoded.Damage==0 && decoded.Traits==AttackTraits.None,"Burn became direct damage during transport");
        burn.Damage=1; Reject(()=>AttackVolumes.Validate(new[]{burn})); burn.Damage=0;
        burn.Traits=AttackTraits.RetireOnParry; Reject(()=>AttackVolumes.Validate(new[]{burn})); burn.Traits=AttackTraits.None;
        burn.ParryCooldown=.25f; Reject(()=>AttackVolumes.Validate(new[]{burn}));
        Console.WriteLine("PASS: native burn transport rejects direct damage, strike retirement and adapter cooldowns.");
    }
    static ContactFrame Frame(Guid id, long seq = 1)
    {
        long tick = Stopwatch.GetTimestamp();
        return new ContactFrame { Session = id, Encounter = id, GeometryFrame = 10, NativeFrame = seq, Sequence = seq, CaptureTick = tick, SentTick = tick, Entity = 23, Collider = -54, Damage = 1, Health = 650, MaxHealth = 650, Active = true, X = 50, Y = 20, Width = 2.5f, Height = 1.9f, HurtCollider = 24, HurtActive = true, HurtX = 50, HurtY = 19.8f, HurtWidth = 3, HurtHeight = 2.9f };
    }
    static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; } catch (EndOfStreamException) { return; }
        throw new Exception("Malformed contact accepted");
    }
    static void Notches() {
        var gates = new[] {1200, 600};
        Assert(Math.Abs(HealthNotches.InjuryLimit(1800,gates)*BossHealthProjection.NativeToHost-600)<.001, "Injury crosses first native threshold");
        Assert(HealthNotches.InjuryLimit(1200,gates)==0, "Equality opened a dam");
        Assert(Math.Abs(HealthNotches.InjuryLimit(1199,gates)*BossHealthProjection.NativeToHost-599)<.001, "Real damage crossing did not open next segment");
        Assert(Math.Abs(HealthNotches.InjuryLimit(500,gates)*BossHealthProjection.NativeToHost-500)<.001, "Final segment cannot fill");
        var frame=Frame(Guid.NewGuid()); frame.Notches=new[]{500,250}; frame.Summons=new[]{new SummonedTarget{Entity=8,Collider=9,Generation=2,Health=200,MaxHealth=400,Width=1,Height=1,Active=true,Kickable=true,AttackId=7,Notches=new[]{100}}};
        using var stream=new MemoryStream(); ContactCodec.Write(stream,frame);stream.Position=0;var copy=ContactCodec.Read(stream);
        Assert(copy.Notches[0]==500 && copy.Summons[0].Notches[0]==100 && copy.Summons[0].Kickable && copy.Summons[0].AttackId==7,"Target notch/kick identity lost in codec");
        frame.Notches=new[]{250,500};Reject(()=>ContactCodec.Validate(frame));
        Console.WriteLine("PASS: native HP notch limits, strict crossing, final segment, target identity and malformed policy");
    }
    static void Cooldown() {
        var hold = new DefenseCooldown(); hold.Begin(3, 10, 1);
        Assert(hold.Held(3, 10.999) && !hold.Held(3, 11), "Parry hold deadline changed");
        Assert(!hold.Held(4, 10.2) && !hold.Held(2, 10.2), "Another activation inherited suppression");
        Assert(hold.Held(3, 10.2) && hold.Held(3, 10.2), "Frozen gameplay clock must retain suppression");
        var frame = Frame(Guid.NewGuid()); frame.Volumes = new[] { new AttackVolume { Id=9, Activation=3, Active=true, Category=AttackCategory.Projectile, ParryCooldown=1, Points=new float[]{0,0,1,0,1,1} } };
        using var stream = new MemoryStream(); ContactCodec.Write(stream, frame); stream.Position=0;
        Assert(ContactCodec.Read(stream).Volumes[0].ParryCooldown == 1, "Parry cooldown lost in codec");
        frame.Volumes[0].CooldownOnDamage=true;
        using var policyWire = new MemoryStream(); AttackVolumes.Write(new BinaryWriter(policyWire), frame.Volumes);
        var bytes = policyWire.ToArray(); bytes[53] = 2;
        Reject(()=>AttackVolumes.Read(new BinaryReader(new MemoryStream(bytes))));
        frame.Volumes[0].ParryCooldown=0; Reject(()=>ContactCodec.Validate(frame));
        frame.Volumes[0].ParryCooldown=1; frame.Volumes[0].ConsumeOnContact=true; Reject(()=>ContactCodec.Validate(frame));
        // A native persistent nail holds damage while a real projectile parry
        // still resolves its reflection/impact. Other consuming cooldowns remain invalid.
        frame.Volumes[0].Traits=AttackTraits.Reflectable;
        using var nailWire=new MemoryStream(); ContactCodec.Write(nailWire,frame); nailWire.Position=0;
        var nail=ContactCodec.Read(nailWire).Volumes[0];
        Assert(nail.ConsumeOnContact && nail.CooldownOnDamage && nail.ParryCooldown==1 && nail.Traits==AttackTraits.Reflectable,"Persistent reflected nail policy lost");
        frame.Volumes[0].CooldownOnDamage=false; Reject(()=>ContactCodec.Validate(frame));
        bool invalidClock = false; try { hold.Begin(3,10,double.NaN); } catch (ArgumentOutOfRangeException) { invalidClock = true; } Assert(invalidClock, "Nonfinite cooldown accepted");
        Console.WriteLine("PASS: temporary parry hold, paused clock, activation isolation and wire policy");
    }
    static void Codec()
    {
        var f = Frame(Guid.NewGuid()); f.TargetGeneration = 42; using var stream = new MemoryStream(); ContactCodec.Write(stream, f);
        Assert(stream.Length == ContactCodec.Size, "Wire size drift"); stream.Position = 0; var copy = ContactCodec.Read(stream);
        Assert(copy.TargetGeneration == 42, "Primary activation lost");
        foreach(var title in new[]{BossTitleVariant.Native,BossTitleVariant.NailmasterOro,BossTitleVariant.BrothersOroMato}) {
            var named=Frame(Guid.NewGuid()); named.BossTitle=title;
            using var payload=new MemoryStream(); ContactCodec.Write(payload,named); payload.Position=0;
            var decoded=ContactCodec.Read(payload); Assert(decoded.BossTitle==title,"Native phase title lost");
            var child=new SummonedTarget {Entity=4,Collider=5,Generation=1,Health=1,MaxHealth=1,Width=1,Height=1};
            Assert(child.Project(decoded).BossTitle==title,"Child HUD lost authored phase title");
            var malformed=payload.ToArray(); malformed[malformed.Length-1]=3;
            Reject(()=>ContactCodec.Read(new MemoryStream(malformed)));
            named.BossTitle=(BossTitleVariant)255; Reject(()=>ContactCodec.Validate(named));
        }
        Assert(copy.Session == f.Session && copy.Collider == f.Collider && copy.X == f.X && copy.Health == 650 && copy.MaxHealth == 650 && copy.Active && copy.HurtCollider == 24 && copy.HurtWidth == 3, "Round trip changed contact");
        for (int tier = 0; tier <= 2; tier++) {
            var difficulty = Frame(Guid.NewGuid()); difficulty.Difficulty = tier;
            difficulty.Volumes = new[] { new AttackVolume { Id = 1, Damage = 2, Activation = 1, Active = true, Category = AttackCategory.Melee, Points = new float[] { 0,0, 1,0, 1,1 } } };
            using var payload = new MemoryStream(); ContactCodec.Write(payload, difficulty); payload.Position = 0;
            var decoded = ContactCodec.Read(payload);
            Assert(decoded.Difficulty == tier && decoded.Volumes[0].Damage == 2, "Tier or per-volume damage lost");
            difficulty.Difficulty = 3; Reject(() => ContactCodec.Validate(difficulty));
            difficulty.Difficulty = tier; difficulty.Volumes[0].Damage = 101; Reject(() => ContactCodec.Validate(difficulty));
        }
        byte[] bytes = stream.ToArray();
        var previous = (byte[])bytes.Clone(); previous[4] = 15; Reject(() => ContactCodec.Read(new MemoryStream(previous)));
        for (int i = 0; i < bytes.Length; i++) { int length = i; Reject(() => ContactCodec.Read(new MemoryStream(bytes, 0, length))); }
        foreach (int offset in new[] { 0, 4, 110, 135, 136, 141 }) { var bad = (byte[])bytes.Clone(); bad[offset] = 255; Reject(() => ContactCodec.Read(new MemoryStream(bad))); }
        var invalidQuick = Frame(Guid.NewGuid()); invalidQuick.QuickWarning = true; Reject(() => ContactCodec.Validate(invalidQuick));
        var invalidFlag = (byte[])bytes.Clone(); invalidFlag[invalidFlag.Length - 2] = 2; Reject(() => ContactCodec.Read(new MemoryStream(invalidFlag)));
        f.Crimson = true; f.Windup = true; f.QuickWarning = true; f.Active = false; f.AttackId = 7;
        using var crimson = new MemoryStream(); ContactCodec.Write(crimson, f); crimson.Position = 0;
        var warning = ContactCodec.Read(crimson);
        Assert(warning.QuickWarning && warning.Crimson && warning.Windup && !warning.Active && warning.AttackId == 7, "Crimson warning identity lost");
        f.QuickWarning = false; f.CrimsonSequence = 9; f.CrimsonCharge = .5f; f.CrimsonRight = true;
        using var prolonged = new MemoryStream(); ContactCodec.Write(prolonged, f); prolonged.Position = 0;
        var held = ContactCodec.Read(prolonged);
        Assert(held.CrimsonSequence == 9 && held.CrimsonCharge == .5f && held.CrimsonRight && held.AttackId == 7, "Visual and strike identity conflated");
        f.StationaryFlame=true;
        using var flameWire=new MemoryStream(); ContactCodec.Write(flameWire,f); flameWire.Position=0;
        Assert(ContactCodec.Read(flameWire).StationaryFlame,"Stationary native flame role lost");
        f.Crimson=false; Reject(()=>ContactCodec.Validate(f)); f.Crimson=true;
        var badFlame=flameWire.ToArray(); badFlame[badFlame.Length-2]=2;
        Reject(()=>ContactCodec.Read(new MemoryStream(badFlame)));
        f.StationaryFlame=false;
        f.CrimsonCharge = float.NaN; Reject(() => ContactCodec.Validate(f)); f.CrimsonCharge = 1;
        f.QuickWarning = true; ContactCodec.Validate(f); f.QuickWarning = false;
        f.CrimsonSequence = 0; Reject(() => ContactCodec.Validate(f));
        var dashCue = Frame(Guid.NewGuid()); dashCue.Active = false; dashCue.KickWindup = true; dashCue.AttackId = 9;
        using var cueWire = new MemoryStream(); ContactCodec.Write(cueWire, dashCue); cueWire.Position = 0;
        var cue = ContactCodec.Read(cueWire);
        Assert(cue.KickWindup && !cue.Kickable && !cue.Active && !cue.Crimson, "Windup granted premature kick/damage eligibility");
        // Native swimming states retain one warning across a collider gap.
        dashCue.CrimsonSequence = 9; dashCue.CrimsonCharge = 1;
        ContactCodec.Validate(dashCue);
        dashCue.KickWindup = false; dashCue.Kickable = true;
        Reject(() => ContactCodec.Validate(dashCue));
        dashCue.Kickable = false; dashCue.KickWindup = true;
        ContactCodec.Validate(dashCue);
        dashCue.Active = true; Reject(() => ContactCodec.Validate(dashCue)); dashCue.Active = false;
        dashCue.Crimson = true; Reject(() => ContactCodec.Validate(dashCue)); dashCue.Crimson = false;
        dashCue.Kickable = true; Reject(() => ContactCodec.Validate(dashCue)); dashCue.Kickable = false;
        dashCue.AttackId = 0; Reject(() => ContactCodec.Validate(dashCue));
        f.AttackId = 0; Reject(() => ContactCodec.Validate(f));
        f.AttackId = 7; f.Active = true; Reject(() => ContactCodec.Validate(f));
        foreach (Action<ContactFrame> corrupt in new Action<ContactFrame>[] { x => x.Session = Guid.Empty, x => x.Encounter = Guid.Empty, x => x.Frequency = 0, x => x.Sequence = 0, x => x.Entity = 0, x => x.Width = -1, x => x.HurtCollider = 0, x => x.HurtHeight = float.NaN, x => x.X = float.NaN, x => x.Damage = 101, x => x.MaxHealth = 0, x => x.MaxHealth = 649, x => x.MaxHealth = 1000001, x => x.RoundTripMs = double.PositiveInfinity, x => x.CaptureTick = x.SentTick + 1 })
        { var bad = Frame(Guid.NewGuid()); corrupt(bad); Reject(() => ContactCodec.Validate(bad)); }
    }
    static void Summons()
    {
        var f = Frame(Guid.NewGuid());
        f.Summons = new SummonedTarget[32];
        for (int i = 0; i < 32; i++) f.Summons[i] = new SummonedTarget { Entity = 100+i, Collider = 200+i, Generation = i+1, Health = 8, MaxHealth = 8, Damage = 1, Active = true, BossBar = i % 2 == 0, X = i, Y = 3, Width = 1, Height = 1 };
        f.Summons[31].SharedHealth=true;
        f.Summons[29].HideHealthBar=true;
        using var bytes = new MemoryStream(); ContactCodec.Write(bytes, f); bytes.Position = 0;
        var copy = ContactCodec.Read(bytes); Assert(copy.Summons[31].Generation == 32 && copy.Summons[31].Health == 8 && !copy.Summons[31].BossBar && copy.Summons[31].SharedHealth && copy.Summons[30].BossBar, "Summon/shared boss identity lost");
        Assert(copy.Summons[29].HideHealthBar && !copy.Summons[30].HideHealthBar,"Hidden projectile health presentation lost");
        f.Summons[30].HideHealthBar=true; Reject(()=>ContactCodec.Validate(f)); f.Summons[30].HideHealthBar=false;
        var finale=f.Summons[30];finale.Active=false;finale.Damage=0;finale.Health=672;finale.MaxHealth=3000;finale.HideHealthBar=true;
        using(var terminalWire=new MemoryStream()) {
            ContactCodec.Write(terminalWire,f);terminalWire.Position=0;
            var terminal=ContactCodec.Read(terminalWire).Summons[30];
            Assert(terminal.BossBar && terminal.HideHealthBar && !terminal.Active && terminal.Health==672
                && terminal.Entity==finale.Entity && terminal.Generation==finale.Generation,
                "Radiance terminal bar hide changed its receiver, health or activity");
        }
        finale.Active=true;Reject(()=>ContactCodec.Validate(f));finale.Active=false;
        finale.Damage=1;Reject(()=>ContactCodec.Validate(f));finale.Damage=0;
        finale.Kickable=true;finale.AttackId=7;Reject(()=>ContactCodec.Validate(f));finale.Kickable=false;finale.AttackId=0;
        finale.SharedHealth=true;Reject(()=>ContactCodec.Validate(f));finale.SharedHealth=false;
        finale.HideHealthBar=false;finale.Active=true;finale.Damage=1;finale.Health=finale.MaxHealth=8;
        Assert(f.Summons[1].Attack().TalismanContactImmunity && !f.Summons[0].Attack().TalismanContactImmunity,"Boss and summon talisman contact roles were conflated");
        var contact=f.Summons[1].Attack(); f.Volumes=new[]{contact};
        using(var contactWire=new MemoryStream()) { ContactCodec.Write(contactWire,f); contactWire.Position=0; var received=ContactCodec.Read(contactWire).Volumes[0];
            Assert(received.Category==AttackCategory.Contact && received.TalismanContactImmunity && received.CooldownOnDamage && received.ParryCooldown==1,"Contact-only talisman protection/retirement lost in transport"); }
        contact.Category=AttackCategory.Projectile; Reject(()=>ContactCodec.Validate(f)); contact.Category=AttackCategory.Contact;
        contact.PassiveContact=true; Reject(()=>ContactCodec.Validate(f)); contact.PassiveContact=false;
        f.Volumes=Array.Empty<AttackVolume>();
        var wire = bytes.ToArray(); for (int i = ContactCodec.Size; i < wire.Length; i++) { int length=i; Reject(() => ContactCodec.Read(new MemoryStream(wire,0,length))); }
        using var targetsWire = new MemoryStream();
        using (var writer = new BinaryWriter(targetsWire, System.Text.Encoding.UTF8, true)) SummonedTargets.Write(writer, f.Summons);
        var badRole = targetsWire.ToArray(); badRole[33] = 2;
        Reject(() => SummonedTargets.Read(new BinaryReader(new MemoryStream(badRole))));
        var badHidden = targetsWire.ToArray(); badHidden[36] = 2;
        Reject(() => SummonedTargets.Read(new BinaryReader(new MemoryStream(badHidden))));
        using var ns = new ContactEndpoint(f.Session, Role.NineSols); using var hk = new ContactEndpoint(f.Session, Role.HollowKnight);
        hk.Publish(f); Assert(Until(() => ns.FreshFrame?.Summons.Length == 32 && ns.FreshFrame.Summons[30].BossBar && !ns.FreshFrame.Summons[31].BossBar), "Summons did not cross actual pipe");
        finale.Active=false;finale.Damage=0;finale.Health=672;finale.MaxHealth=3000;finale.HideHealthBar=true;
        hk.Publish(f);
        Assert(Until(()=>ns.FreshFrame?.Summons[30].HideHealthBar==true && ns.FreshFrame.Summons[30].BossBar
            && ns.FreshFrame.Summons[30].Health==672 && ns.FreshFrame.Summons[30].Generation==finale.Generation),
            "Native terminal boss visibility transition disconnected or replaced the receiver over the actual pipe");
        f.Summons[1].Entity = f.Summons[0].Entity; Reject(() => ContactCodec.Validate(f)); f.Summons[1].Entity = 101;
        f.Summons[1].Entity = f.Entity; Reject(() => ContactCodec.Validate(f)); f.Summons[1].Entity = 101;
        f.Summons[0].Generation=0; Reject(() => ContactCodec.Validate(f)); f.Summons[0].Generation=1;
        f.Summons[0].Width=float.NaN; Reject(() => ContactCodec.Validate(f)); f.Summons[0].Width=1;
        Array.Resize(ref f.Summons,33); Reject(() => ContactCodec.Validate(f));
        Console.WriteLine("PASS: bounded summon generations, actual-pipe delivery, truncation, alias and malformed target rejection.");
    }
    static void Volumes()
    {
        var f = Frame(Guid.NewGuid());
        var vertices = new float[] { -2, 0, 0, -3, 2, 0, 0, 3 };
        f.Volumes = new[] { new AttackVolume { Id = 1, Activation = 3, Active = true, Category = AttackCategory.Projectile, Points = vertices },
            new AttackVolume { Id = 2, Activation = 7, Active = false, Category = AttackCategory.Hazard, Points = vertices } };
        f.Volumes[0].ConsumeOnContact = true; f.Volumes[0].Traits = AttackTraits.Reflectable;
        using var stream = new MemoryStream(); ContactCodec.Write(stream, f); stream.Position = 0;
        var copy = ContactCodec.Read(stream);
        Assert(copy.Volumes.Length == 2 && copy.Volumes[0].Activation == 3 && copy.Volumes[1].Id == 2 && !copy.Volumes[1].Active && copy.Volumes[0].Points[3] == -3, "Independent rotated attack identities lost");
        Assert(copy.Volumes[0].Traits == AttackTraits.Reflectable && copy.Volumes[0].ConsumeOnContact, "Parry-only reflection trait lost");
        f.Volumes[0].Traits = (AttackTraits)64; Reject(() => ContactCodec.Validate(f));
        f.Volumes[0].Traits = AttackTraits.Kickable; Reject(() => ContactCodec.Validate(f));
        f.Volumes[0].ActionId = 9; f.Volumes[0].Traits = AttackTraits.Crimson | AttackTraits.Kickable; Reject(() => ContactCodec.Validate(f));
        f.Volumes[0].Traits = AttackTraits.Reflectable; f.Volumes[0].ConsumeOnContact = false; Reject(() => ContactCodec.Validate(f)); f.Volumes[0].ConsumeOnContact = true;
        byte[] bytes = stream.ToArray();
        for (int i = ContactCodec.Size; i < bytes.Length; i++) { int length = i; Reject(() => ContactCodec.Read(new MemoryStream(bytes, 0, length))); }
        var badCount = (byte[])bytes.Clone(); badCount[158] = 255; Reject(() => ContactCodec.Read(new MemoryStream(badCount)));
        f.Volumes[1].Id = 1; Reject(() => ContactCodec.Validate(f)); f.Volumes[1].Id = 2;
        f.Volumes[0].Activation = 0; Reject(() => ContactCodec.Validate(f)); f.Volumes[0].Activation = 3;
        f.Volumes[0].Points = new float[] { 0,0,1,1,2,2 }; Reject(() => ContactCodec.Validate(f));
        f.Volumes[0].Points = new float[] { 0,0,float.PositiveInfinity,1,2,2 }; Reject(() => ContactCodec.Validate(f));
        var a = new ContactVerdict(); var b = new ContactVerdict(); a.Begin(); b.Begin();
        Assert(a.Accept(a.Generation, true) && b.CanResolve, "Resolving one sword consumed another");
        long pending = b.Generation; b.Suspend(); b.Resume();
        Assert(!b.Accept(pending, true) && b.CanResolve, "Stale callback survived recovery");
        a.Suspend(); a.Resume(); Assert(!a.CanResolve, "Freshness recovery rearmed consumed sword");
        a.Begin(); Assert(a.CanResolve, "Next native throw did not rearm");
    }
    static void InitialDamageGroupHolds()
    {
        var points=new float[]{0,0,1,0,0,1};
        var body=new AttackVolume{Id=90,Activation=1,ActionId=700,DamageGroupId=700,InitialDamageGroupHold=1,Category=AttackCategory.Melee,Points=points};
        var shots=new AttackVolume[6];shots[0]=body;
        for(int i=1;i<6;i++)shots[i]=new AttackVolume{Id=90+i,Activation=1,ActionId=700+i,DamageGroupId=700,InitialDamageGroupHold=1,Category=AttackCategory.Projectile,ParryCooldown=1,CooldownOnDamage=true,Points=points};
        using var stream=new MemoryStream();AttackVolumes.Write(new BinaryWriter(stream),shots);stream.Position=0;var copy=AttackVolumes.Read(new BinaryReader(stream));
        Assert(copy[5].InitialDamageGroupHold==1 && copy[5].ActionId==705 && !copy[5].ConsumeOnContact,"Dive shot lost independent action or timed initial group");
        var groups=new DamageGroupRetirement();
        Assert(!groups.Resolve(700,false,20,1),"Parry incorrectly started the injury hold");
        Assert(groups.Resolve(700,true,20,1) && groups.Contains(700,20),"First dive injury did not hold the group immediately");
        for(int i=1;i<6;i++)Assert(groups.Contains(copy[i].DamageGroupId,20) && !groups.Resolve(700,true,20,1),"Same-frame landing shot bypassed the initial hold");
        groups.Retain(Array.Empty<AttackVolume>(),20.5f);Assert(groups.Contains(700,20.5f),"Collider gap erased pending hold");
        groups.Retain(copy,20.999f);Assert(groups.Contains(700,20.999f),"Initial hold ended early");
        groups.Retain(copy,20.999f);long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++)groups.Retain(copy,20.999f);
        Assert(GC.GetAllocatedBytesForCurrentThread()==before,"Warm injury-group retention allocated during combat");
        Assert(!groups.Contains(700,21) && !groups.Resolve(700,true,21,1),"Rearmed shot restarted shared immunity instead of staying independent");
        var strikes=new TimedActionRetirement();strikes.Hold(701,21,1);
        Assert(strikes.Held(701,21.5f) && !strikes.Held(702,21.5f),"One surviving projectile suppressed another independent shot");
        Assert(!strikes.Held(701,22),"Individual projectile failed to rearm");
        groups.Retain(Array.Empty<AttackVolume>(),22);Assert(groups.Resolve(700,true,30,1),"Ended native pool generation retained previous injury hold");
        body.InitialDamageGroupHold=float.NaN;Reject(()=>AttackVolumes.Validate(new[]{body}));body.InitialDamageGroupHold=1;
        body.DamageGroupId=0;Reject(()=>AttackVolumes.Validate(new[]{body}));body.DamageGroupId=700;
        copy[1].ConsumeOnContact=true;Reject(()=>AttackVolumes.Validate(copy));
        Console.WriteLine("PASS: dive body/five shots share one initial injury, pause-time hold survives collider gaps, shots rearm independently at one second and pool generations release.");
    }
    static void DamageGroups()
    {
        var recurring=new DamageGroupRetirement();
        var bodyHold=new DamageGroupRetirement();
        var contact=new AttackVolume {Id=879,Activation=1,Category=AttackCategory.Melee,PassiveContact=true,DamageGroupId=777,
            InitialDamageGroupHold=1,RepeatDamageGroupHold=true,ParryCooldown=1,CooldownOnDamage=true,Points=new float[]{0,0,1,0,1,1,0,1}};
        AttackVolumes.Validate(new[]{contact});
        Assert(bodyHold.Resolve(777,true,10,1,true),"Initial Nosk body hit failed to hold injury");
        contact.PassiveContact=false;contact.ActionId=75;contact.Activation=2;contact.Traits=AttackTraits.Kickable;contact.ParryCooldown=0;contact.CooldownOnDamage=false;
        AttackVolumes.Validate(new[]{contact});bodyHold.Retain(new[]{contact},10.1f);
        Assert(bodyHold.Contains(777,10.999f) && !bodyHold.Resolve(777,true,10.1f,1,true),"Landing/new attack escaped one-second body hold");
        Assert(!bodyHold.Contains(777,11) && bodyHold.Resolve(777,true,11,1,true),"Body hold did not rearm after exactly one second");
        Assert(bodyHold.Resolve(778,true,10.1f,1,true),"Nosk body hold affected an unrelated projectile group");
        var floor=new AttackVolume{Id=880,Activation=1,ActionId=880,DamageGroupId=881,InitialDamageGroupHold=1,RepeatDamageGroupHold=true,
            Category=AttackCategory.Melee,ParryCooldown=1,CooldownOnDamage=true,Points=new float[]{0,0,1,0,0,1}};
        var flight=new AttackVolume{Id=882,Activation=1,ActionId=882,DamageGroupId=881,InitialDamageGroupHold=1,RepeatDamageGroupHold=true,
            Category=AttackCategory.Projectile,NotifyParry=true,Traits=AttackTraits.RetireOnParry,ParryCooldown=1,CooldownOnDamage=true,Points=floor.Points};
        floor.NotifyParry=true;
        using(var goopWire=new MemoryStream()) {
            AttackVolumes.Write(new BinaryWriter(goopWire),new[]{floor,flight});goopWire.Position=0;
            var decoded=AttackVolumes.Read(new BinaryReader(goopWire));
            Assert(decoded[0].RepeatDamageGroupHold && decoded[0].NotifyParry && decoded[1].NotifyParry && decoded[1].Traits==AttackTraits.RetireOnParry,"Flight/landed goop injury/parry contract lost in transport");
        }
        flight.RepeatDamageGroupHold=false;Reject(()=>AttackVolumes.Validate(new[]{flight}));flight.RepeatDamageGroupHold=true;
        flight.NotifyParry=false;Reject(()=>AttackVolumes.Validate(new[]{flight}));flight.NotifyParry=true;
        Assert(!recurring.Resolve(881,false,10,1,true),"Goop parry started group injury immunity");
        Assert(recurring.Resolve(881,true,10,1,true) && !recurring.Resolve(881,true,10,1,true),"Overlapping goop failed same-frame injury hold");
        recurring.Retain(Array.Empty<AttackVolume>(),10.5f);
        Assert(recurring.Contains(881,10.999f) && !recurring.Contains(881,11),"Goop hold lost across collider gap or exceeded one second");
        Assert(recurring.Resolve(881,true,11,1,true) && recurring.Contains(881,11.999f),"Later goop injury did not restart shared safety window");
        bool changedMode=false;try{recurring.Resolve(881,true,12,1,false);}catch(InvalidOperationException){changedMode=true;}
        Assert(changedMode,"Live group silently changed initial/recurring hold mode");
        floor.InitialDamageGroupHold=0;Reject(()=>AttackVolumes.Validate(new[]{floor}));floor.InitialDamageGroupHold=1;
        flight.ConsumeOnContact=true;Reject(()=>AttackVolumes.Validate(new[]{flight}));flight.ConsumeOnContact=false;
        flight.Traits=AttackTraits.Reflectable;Reject(()=>AttackVolumes.Validate(new[]{flight}));
        var orange=new AttackVolume{Id=883,Activation=1,ActionId=883,Category=AttackCategory.Contact,NotifyParry=true,
            TalismanContactImmunity=true,ParryCooldown=1,CooldownOnDamage=true,Points=floor.Points};
        using(var contactWire=new MemoryStream()){
            AttackVolumes.Write(new BinaryWriter(contactWire),new[]{orange});contactWire.Position=0;
            Assert(AttackVolumes.Read(new BinaryReader(contactWire))[0].NotifyParry,"Native orange-minion defense notification lost in transport");
        }
        orange.ParryCooldown=.0001f;AttackVolumes.Validate(new[]{orange});
        orange.TalismanContactImmunity=false;Reject(()=>AttackVolumes.Validate(new[]{orange}));orange.TalismanContactImmunity=true;
        orange.DamageGroupId=881;Reject(()=>AttackVolumes.Validate(new[]{orange}));orange.DamageGroupId=0;
        orange.Traits=AttackTraits.Kickable;Reject(()=>AttackVolumes.Validate(new[]{orange}));
        Console.WriteLine("PASS: flight/landed goop parry notifications round-trip; Nosk body hold survives landing/new actions for one second, independent of projectile groups.");
        var f=Frame(Guid.NewGuid());
        var points=new float[]{-1,0,1,0,0,2};
        f.Volumes=new[]{
            new AttackVolume{Id=1,Activation=1,Category=AttackCategory.Projectile,ConsumeOnContact=true,DamageGroupId=17,Traits=AttackTraits.Reflectable,Points=points},
            new AttackVolume{Id=2,Activation=3,Category=AttackCategory.Projectile,ConsumeOnContact=true,DamageGroupId=17,Traits=AttackTraits.Reflectable,Points=points},
            new AttackVolume{Id=3,Activation=4,Category=AttackCategory.Projectile,ConsumeOnContact=true,DamageGroupId=18,Points=points},
            new AttackVolume{Id=4,Activation=2,Category=AttackCategory.Melee,DamageGroupId=17,Points=points}};
        using var stream=new MemoryStream();ContactCodec.Write(stream,f);stream.Position=0;var copy=ContactCodec.Read(stream);
        Assert(copy.Volumes[0].DamageGroupId==17 && copy.Volumes[1].DamageGroupId==17 && copy.Volumes[1].Activation==3,"Volley lineage or independent activation lost");
        var groups=new DamageGroupRetirement();
        Assert(!groups.Resolve(17,false) && !groups.Contains(17),"Parry suppressed sibling projectiles");
        Assert(groups.Resolve(17,true) && groups.Contains(17) && !groups.Contains(18),"Injury failed to retire only its volley");
        Assert(!groups.Resolve(17,true),"Duplicate injury retirement accepted");
        groups.Retain(copy.Volumes);Assert(groups.Contains(17),"Live volley retirement lost");
        groups.Retain(new[]{copy.Volumes[3]});Assert(groups.Contains(17),"Armed core lost injury retirement before its burst");
        groups.Retain(new[]{copy.Volumes[2]});Assert(!groups.Contains(17),"Ended pool lease retained");
        f.Volumes[0].ActionId=2;Reject(()=>ContactCodec.Validate(f));f.Volumes[0].ActionId=0;
        f.Volumes[0].DamageGroupId=-1;Reject(()=>ContactCodec.Validate(f));f.Volumes[0].DamageGroupId=17;
        f.Volumes[0].Category=AttackCategory.Melee;Reject(()=>ContactCodec.Validate(f));
        f.Volumes[0].Category=AttackCategory.Projectile;
        f.Volumes[3].Traits=AttackTraits.RetireOnParry;Reject(()=>ContactCodec.Validate(f));f.Volumes[3].Traits=AttackTraits.None;
        f.Volumes[3].BlocksProjectiles=true;Reject(()=>ContactCodec.Validate(f));f.Volumes[3].BlocksProjectiles=false;
        f.Volumes[3].ActionId=2;f.Volumes[3].Traits=AttackTraits.Crimson;ContactCodec.Validate(f);
        using(var parentWire=new MemoryStream()) {ContactCodec.Write(parentWire,f);parentWire.Position=0;var parent=ContactCodec.Read(parentWire).Volumes[3];Assert(parent.ActionId==2 && parent.DamageGroupId==17 && parent.Traits==AttackTraits.Crimson,"Parent strike lost distinct defense and shared emitted injury identities");}
        f.Volumes[3].CounterGroupId=2;Reject(()=>ContactCodec.Validate(f));f.Volumes[3].CounterGroupId=0;
        f.Volumes[3].ParryCooldown=1;Reject(()=>ContactCodec.Validate(f));f.Volumes[3].ParryCooldown=0;
        f.Volumes[3].ActionId=0;f.Volumes[3].Traits=AttackTraits.None;
        var old=stream.ToArray();old[4]=25;old[5]=0;Reject(()=>ContactCodec.Read(new MemoryStream(old)));
        Console.WriteLine("PASS: independent projectile defenses, damage-only volley retirement, pooled lease cleanup and contact-version rejection.");
    }
    static void Kicks()
    {
        var f = Frame(Guid.NewGuid()); f.Kickable = true; f.AttackId = 17;
        using var wire = new MemoryStream(); ContactCodec.Write(wire, f); wire.Position = 0;
        Assert(ContactCodec.Read(wire).Kickable, "Native kick window lost");
        f.Active = false; Reject(() => ContactCodec.Validate(f)); f.Active = true;
        f.Active = false;
        f.Volumes = new[] { new AttackVolume { Id = 1, Activation = 1, ActionId = 17, Traits = AttackTraits.Kickable, Active = true, Category = AttackCategory.Melee, Points = new float[] { 0,0,1,0,1,1,0,1 } } };
        ContactCodec.Validate(f);
        using var grouped = new MemoryStream(); ContactCodec.Write(grouped, f); grouped.Position = 0;
        Assert(ContactCodec.Read(grouped).Volumes[0].ActionId == 17, "Grouped counter identity lost");
        f.Volumes[0].ActionId = 18; Reject(() => ContactCodec.Validate(f)); f.Volumes = Array.Empty<AttackVolume>(); f.Active = true;
        f.Crimson = true; Reject(() => ContactCodec.Validate(f)); f.Crimson = false;
        f.AttackId = 0; Reject(() => ContactCodec.Validate(f)); f.AttackId = 17;
    }
    static void Exchange(Guid generation = default)
    {
        var id = Guid.NewGuid(); using var ns = new ContactEndpoint(id, Role.NineSols, generation); using var hk = new ContactEndpoint(id, Role.HollowKnight, generation);
        hk.Publish(Frame(id)); Assert(Until(() => ns.FreshFrame != null), "No contact received"); Assert(ns.FreshFrame!.Collider == -54, "Contact changed");
        Thread.Sleep(280); Assert(ns.FreshFrame == null, "Stale contact remains active");
        hk.Publish(Frame(id, 2)); Assert(Until(() => ns.FreshFrame?.NativeFrame == 2), "Fresh recovery failed");
        hk.Dispose(); Assert(Until(() => ns.FreshFrame == null), "Disconnect retained contact");
    }
    static void InvalidPeer(bool wrongSession, bool gap, double age)
    {
        var id = Guid.NewGuid(); using var ns = new ContactEndpoint(id, Role.NineSols);
        using var peer = new NamedPipeClientStream(".", "YiGodhome." + id.ToString("N") + ".contact", PipeDirection.InOut);
        peer.Connect(5000); var f = Frame(wrongSession ? Guid.NewGuid() : id, gap ? 2 : 1); ContactCodec.Write(peer, f);
        if (!wrongSession && !gap)
        {
            using var reader = new BinaryReader(peer); using var writer = new BinaryWriter(peer);
            Assert(reader.ReadInt64() == 1 && reader.ReadInt64() == f.SentTick, "Bad contact acknowledgement"); writer.Write(age); writer.Flush();
            Assert(Until(() => { while (ns.TryGetEvent(out var s)) if (s.Contains("CONTACT_RECEIVED")) return true; return false; }), "No receive evidence");
        }
        else Assert(Until(() => { while (ns.TryGetEvent(out var s)) if (s.Contains("CONTACT_STOP")) return true; return false; }), "Invalid peer accepted");
        Assert(ns.FreshFrame == null, "Invalid or slow frame published");
    }
    static int Main()
    {
        RadiancePresentationContracts();
        LedgeFacingAfterParry();
        RadianceHazardCheckpoints();
        GrimmIndependentBats();
        NailmasterRecoilWire();
        DefenderEruptions();
        GorbPersistentSpears();
        Assert(!TalismanShieldPolicy.Allows(true,true,false),"Shielded boss accepted talisman placement");
        Assert(!TalismanShieldPolicy.Allows(true,false,true),"Delayed explosion bypassed restored shield");
        Assert(TalismanShieldPolicy.Allows(false,true,false) && TalismanShieldPolicy.Allows(false,false,true),"Vulnerable talisman behavior regressed");
        Assert(TalismanShieldPolicy.Allows(true,false,false),"Shield policy changed ordinary native hit admission");
        // Reproduce a direct interface event with NO preliminary before-hit
        // callback, plus a shield change between the preliminary check/event.
        int deposits=0;
        void NativeAttachmentEvent(bool live,bool available,bool shielded) {
            if(TalismanShieldPolicy.AllowsEvent(live,available,shielded,true,false))deposits++;
        }
        NativeAttachmentEvent(true,true,true);
        Assert(deposits==0,"Direct native event bypassed shield and created a deposit");
        Assert(TalismanShieldPolicy.Allows(false,true,false),"Vulnerable preliminary attachment rejected");
        NativeAttachmentEvent(true,true,true);
        Assert(deposits==0,"Shield restored before event but native attachment still ran");
        NativeAttachmentEvent(true,true,false);NativeAttachmentEvent(false,true,false);NativeAttachmentEvent(true,false,false);
        Assert(deposits==1,"Vulnerable/live attachment or inactive/dead rejection regressed");
        Assert(TalismanShieldPolicy.AllowsEvent(true,false,false,false,true)
            && !TalismanShieldPolicy.AllowsEvent(true,false,true,false,true),"Hidden delayed explosion or restored shield policy regressed");
        Console.WriteLine("PASS: direct talisman event without before-hit callback and restored-shield race reject before deposit; live vulnerable placement and unshielded hidden explosion remain allowed.");
        Console.WriteLine("PASS: explicit shield rejects attachment and delayed explosion without changing vulnerable or ordinary-hit policy.");
        Assert(Math.Abs(NativeMeleeDirection.Angle(0,-1,0)-270)<.001f,"Native downward melee became horizontal");
        Assert(NativeMeleeDirection.Angle(-1,0,0)==180 && NativeMeleeDirection.Angle(1,0,180)==0 && NativeMeleeDirection.Angle(0,1,0)==90,"Native cardinal strike direction changed");
        Assert(NativeMeleeDirection.Angle(0,0,180)==180 && NativeMeleeDirection.Angle(float.NaN,0,180)==180,"Invalid native direction lost bounded fallback");
        Console.WriteLine("PASS: native downward/upward/side melee direction survives host angle conversion; invalid vectors retain prior facing fallback.");
        // Native collapse: 8 frames at12fps. Reverse begins from the identical
        // final fallen image; the whole hold must never cycle into standing.
        var collapse=YiGodhome.CounterStunTimeline.Sample(0,3,12,8);
        Assert(collapse.Index==0 && !collapse.Rising && !collapse.Complete,"Collapse did not start naturally");
        for(int n=100;n<432;n++) {
            var fallen=YiGodhome.CounterStunTimeline.Sample(n/144f,3,12,8);
            Assert(fallen.Index==7 && !fallen.Rising && !fallen.Complete,"Stunned Tamer left the fallen pose");
        }
        var reverse=YiGodhome.CounterStunTimeline.Sample(3,3,12,8);
        Assert(reverse.Rising && reverse.Index==0 && !reverse.Complete,"Reverse did not begin from fallen endpoint");
        var last=YiGodhome.CounterStunTimeline.Sample(3.65f,3,12,8);
        Assert(last.Rising && last.Index==7 && !last.Complete,"Get-up ended before its final native frame");
        Assert(YiGodhome.CounterStunTimeline.Sample(3.67f,3,12,8).Complete,"Native get-up never released AI");
        Console.WriteLine("PASS: native collapse frames hold fallen pose for full stun and reverse endpoint continuously before AI release.");
        var nailmaster=YiGodhome.CounterStunTimeline.Sample(2.99f,3,10,1,20,12);
        Assert(nailmaster.Index==0 && !nailmaster.Rising && !nailmaster.Complete,"Single-frame fallen pose did not hold for three seconds");
        nailmaster=YiGodhome.CounterStunTimeline.Sample(3.25f,3,10,1,20,12);
        Assert(nailmaster.Rising && nailmaster.Index==5 && !nailmaster.Complete,"Distinct native get-up clip lost its authored rate/length");
        Assert(!YiGodhome.CounterStunTimeline.Sample(3.55f,3,10,1,20,12).Complete
            && YiGodhome.CounterStunTimeline.Sample(3.61f,3,10,1,20,12).Complete,"AI released before native get-up finished");
        Console.WriteLine("PASS: separate native fallen/get-up clips retain three-second hold and independent frame rates before AI release.");
        // Preserved player capture: Lancer jump130 becomes landing slash132
        // immediately before its confirmed peer counter reaches the host.
        Assert(YiGodhome.CounterReceiptWindow.Matches(130,132,130,.014f,true),"Confirmed Lancer counter lost across landing");
        Assert(!YiGodhome.CounterReceiptWindow.Matches(131,132,130,.014f,true),"Beast action admitted as Lancer counter");
        Assert(!YiGodhome.CounterReceiptWindow.Matches(130,132,130,.251f,true),"Expired jump counter admitted");
        Assert(!YiGodhome.CounterReceiptWindow.Matches(130,132,130,-.001f,true),"Future receipt admitted");
        Assert(!YiGodhome.CounterReceiptWindow.Matches(130,132,130,.014f,false),"Unrelated adapter changed move-transition policy");
        Assert(YiGodhome.CounterReceiptWindow.Matches(130,130,130,.25f,false),"Existing exact-action late receipt regressed");
        Assert(!YiGodhome.CounterReceiptWindow.Matches(0,0,0,0,true),"Empty strike admitted");
        Console.WriteLine("PASS: captured Tamer jump counter survives immediate landing, rejects Beast/foreign/expired actions and preserves other adapters' exact-action policy.");

        ImmutableContourOwnership();ValidationScratchSafety();RadianceWindows();
        try { WireFloats(); InitialDamageGroupHolds(); DamageGroups(); NativeCircle(); NativeCircleAdmission(); BurnPolicy(); CounterStreak(); StrikeRetirement(); Verdict(); Notches(); Cooldown(); Codec(); Summons(); Volumes(); DenseVolley(); Kicks(); Exchange(); Exchange(Guid.NewGuid()); InvalidPeer(true, false, 0); InvalidPeer(false, true, 0); InvalidPeer(false, false, 300); Console.WriteLine("PASS: contact codec bounds/truncations, real session/encounter pipes, freshness/recovery/disconnect, wrong session/sequence, slow first transfer and exactly-once native verdict identity."); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static void StrikeRetirement()
    {
        var notified=new AttackVolume {Id=900,Activation=1,ActionId=30,Category=AttackCategory.Melee,NotifyParry=true,Points=new float[]{0,0,1,0,0,1}};
        using(var parryWire=new MemoryStream()) {
            using(var writer=new BinaryWriter(parryWire,System.Text.Encoding.UTF8,true))AttackVolumes.Write(writer,new[]{notified});
            parryWire.Position=0;Assert(AttackVolumes.Read(new BinaryReader(parryWire))[0].NotifyParry,"Ordinary native parry notification lost on wire");
            var malformed=parryWire.ToArray();malformed[malformed.Length-1]=2;Reject(()=>AttackVolumes.Read(new BinaryReader(new MemoryStream(malformed))));
        }
        notified.ActionId=0;Reject(()=>AttackVolumes.Validate(new[]{notified}));notified.ActionId=30;
        notified.Category=AttackCategory.Projectile;Reject(()=>AttackVolumes.Validate(new[]{notified}));notified.Category=AttackCategory.Melee;
        notified.Traits=AttackTraits.Crimson;Reject(()=>AttackVolumes.Validate(new[]{notified}));notified.Traits=AttackTraits.None;
        notified.ParryCooldown=1;Reject(()=>AttackVolumes.Validate(new[]{notified}));notified.ParryCooldown=0;
        // Native Hornet barbs survive ordinary injury but notify a successful
        // projectile defense, without consuming/reflection wire authority.
        notified.Category=AttackCategory.Projectile;notified.ParryCooldown=1;notified.CooldownOnDamage=true;
        using(var barbWire=new MemoryStream()) {
            AttackVolumes.Write(new BinaryWriter(barbWire),new[]{notified});barbWire.Position=0;
            var decoded=AttackVolumes.Read(new BinaryReader(barbWire))[0];
            Assert(decoded.NotifyParry && !decoded.ConsumeOnContact && decoded.ActionId==30 && decoded.ParryCooldown==1,
                "Native barb parry notification lost its independent action contract");
        }
        notified.CooldownOnDamage=false;Reject(()=>AttackVolumes.Validate(new[]{notified}));
        notified.Category=AttackCategory.Melee;notified.ParryCooldown=0;
        var grouped=new AttackVolume {Id=901,Activation=1,ActionId=31,CounterGroupId=40,Category=AttackCategory.Melee,Traits=AttackTraits.Crimson,Points=new float[]{0,0,1,0,0,1}};
        AttackVolumes.Validate(new[]{grouped});
        grouped.CounterInjuryEntity=-143;
        using(var stream=new MemoryStream()) {
            AttackVolumes.Write(new BinaryWriter(stream),new[]{grouped});stream.Position=0;
            var decoded=AttackVolumes.Read(new BinaryReader(stream))[0];
            Assert(decoded.CounterGroupId==40 && decoded.ActionId==31,"Counter group lost independent strike identity on wire");
            Assert(decoded.CounterInjuryEntity==-143,"Attached counter injury owner lost its signed native identity");
        }
        grouped.CounterGroupId=-1;Reject(()=>AttackVolumes.Validate(new[]{grouped}));grouped.CounterGroupId=40;
        grouped.Traits=AttackTraits.Kickable;Reject(()=>AttackVolumes.Validate(new[]{grouped}));grouped.Traits=AttackTraits.Crimson;
        grouped.ActionId=0;Reject(()=>AttackVolumes.Validate(new[]{grouped}));grouped.ActionId=31;
        grouped.DamageGroupId=40;Reject(()=>AttackVolumes.Validate(new[]{grouped}));grouped.DamageGroupId=0;
        var groups=new ActionRetirement();var strikes=new ActionRetirement();
        var other=new AttackVolume {Id=902,ActionId=32,CounterGroupId=40};
        strikes.Retire(31);strikes.Retain(0,new[]{grouped,other});
        Assert(strikes.Contains(31) && !strikes.Contains(32),"Shared counter group retired another live body's injury strike");
        strikes.Retire(31);groups.Retire(40);grouped.ActionId=32;
        groups.RetainCounterGroups(40,new[]{grouped});strikes.Retain(32,new[]{grouped});
        Assert(groups.Contains(40) && !strikes.Contains(32),"Counter group changed later swing injury retirement");
        groups.RetainCounterGroups(40,Array.Empty<AttackVolume>());
        Assert(groups.Contains(40),"Collider gap rearmed a countered visual sequence");
        var retained=new[]{grouped};
        for(int i=0;i<1024;i++)groups.RetainCounterGroups(40,retained);
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<8192;i++)groups.RetainCounterGroups(40,retained);
        Assert(GC.GetAllocatedBytesForCurrentThread()==allocated,"Warmed counter-group retirement allocated per-frame scratch");
        groups.RetainCounterGroups(41,Array.Empty<AttackVolume>());
        Assert(!groups.Contains(40) && !groups.Contains(41),"Completed counter group leaked to fresh sequence");
        Console.WriteLine("PASS: strict shared-counter wire identity, separate injury swings, collider-gap retirement, fresh sequence reuse and zero-allocation warmed retention.");
        var actions = new ActionRetirement();
        Assert(actions.Retire(9) && !actions.Contains(4) && actions.Retire(4) && !actions.Retire(9), "Out-of-order bodies lost independent retirement");
        actions.Retain(4, new[] { new AttackVolume { ActionId = 9 }, new AttackVolume { ActionId = 12 } });
        Assert(actions.Contains(9) && actions.Contains(4) && !actions.Contains(12), "Live body snapshots rearmed or pre-retired another action");
        actions.Retain(12, Array.Empty<AttackVolume>());
        Assert(!actions.Contains(9) && !actions.Contains(4) && actions.Retire(12), "Ended body actions leaked into the next activation");
        // A hop/dive can replace its aerial weapon with a landed body collider,
        // with no enabled shapes during the intervening animation state.
        foreach(bool parried in new[]{false,true}) {
            var dive=new ActionRetirement();long action=parried?141:140;
            var airborne=new AttackVolume {Id=501,Activation=1,ActionId=action,Active=true};
            dive.Retire(airborne.ActionId);
            var owner=new SummonedTarget {Entity=55,Generation=1,AttackId=action};
            dive.Retain(0,Array.Empty<AttackVolume>(),new[]{owner});
            var landed=new AttackVolume {Id=502,Activation=8,ActionId=action,Active=true};
            dive.Retain(0,new[]{landed},new[]{owner});
            Assert(dive.Contains(landed.ActionId),"Aerial "+(parried?"parry":"injury")+" rearmed after collider gap/landing replacement");
            owner.AttackId=action+10;landed.ActionId=owner.AttackId;
            dive.Retain(0,new[]{landed},new[]{owner});
            Assert(!dive.Contains(action) && !dive.Contains(landed.ActionId),"Retired dive leaked into the next native attack");
        }
        Console.WriteLine("PASS: aerial injury/parry retirement survives shape-free anticipation and landed collider replacement; next hop rearms independently.");
        var waves = new ActionRetirement();
        Assert(waves.Retire(77), "Shared paired wave could not retire");
        waves.Retain(0,new[]{new AttackVolume {Id=101,ActionId=77,Active=false},new AttackVolume {Id=102,ActionId=77,Active=false}});
        Assert(waves.Contains(77), "Inactive paired controllers lost retirement between shape emissions");
        waves.Retain(0,new[]{new AttackVolume {Id=102,ActionId=77,Active=false}});
        Assert(waves.Contains(77), "Ending one wave rearmed its surviving paired controller");
        waves.Retain(0,new[]{new AttackVolume {Id=102,ActionId=78,Active=false}});
        Assert(!waves.Contains(77) && !waves.Contains(78) && waves.Retire(78), "Native controller reuse inherited retired wave action");
        var pillars=new ActionRetirement();
        var baseFlame=new AttackVolume {Id=401,Activation=1,ActionId=90,Active=true,Category=AttackCategory.Melee,Traits=AttackTraits.Crimson,Points=new float[]{0,0,1,0,0,1}};
        var column=new AttackVolume {Id=402,Activation=0,ActionId=90,Active=false,Category=AttackCategory.Melee,Traits=AttackTraits.Crimson,Points=baseFlame.Points};
        AttackVolumes.Validate(new[]{baseFlame,column});
        pillars.Retire(baseFlame.ActionId);baseFlame.Active=false;
        pillars.Retain(0,new[]{baseFlame,column});
        column.Active=true;column.Activation=1;
        Assert(pillars.Contains(column.ActionId),"NKG column rearmed after its base's collider-free gap");
        var otherPillar=new AttackVolume {ActionId=91};
        Assert(!pillars.Contains(otherPillar.ActionId),"One NKG pillar retired a different pillar");
        pillars.Retain(0,new[]{otherPillar});
        Assert(!pillars.Contains(90),"Ended NKG pooled lifetime retained its retired action");
        var gaps=new ActionRetirement();gaps.Retire(80);gaps.Retire(81);
        gaps.Retain(0,Array.Empty<AttackVolume>(),new[]{new SummonedTarget{Entity=10,AttackId=80,Active=false,Kickable=false},new SummonedTarget{Entity=11,AttackId=81,Active=false,Kickable=false}});
        Assert(gaps.Contains(80) && gaps.Contains(81),"Concurrent authored sequences rearmed during collider-free landing gap");
        gaps.Retain(0,new[]{new AttackVolume{ActionId=80}},new[]{new SummonedTarget{Entity=11,AttackId=82}});
        Assert(gaps.Contains(80) && !gaps.Contains(81) && !gaps.Contains(82),"New independent sequence inherited retirement or cleared surviving wave");
        gaps.Retain(0,Array.Empty<AttackVolume>(),Array.Empty<SummonedTarget>());
        Assert(!gaps.Contains(80),"Ended authored sequence retained stale immunity");
        var first = new AttackRetirement(); var second = new AttackRetirement(); var third = new AttackRetirement();
        Assert(!first.Retire(0) && !first.Contains(1), "Invalid retirement consumed first activation");
        Assert(first.Retire(1) && first.Contains(1) && !first.Retire(1), "Parry retirement not exactly once");
        Assert(!second.Contains(1) && !third.Contains(1), "First swing retirement suppressed later swings");
        Assert(second.Retire(1) && third.Retire(1), "Later strikes could not retire independently");
        Assert(!first.Contains(2) && first.Retire(2) && first.Contains(1) && first.Contains(2), "Reuse or stale snapshot rearmed consumed strike");
        // Spin/idle shapes can overlap in one physics frame, then a later shape
        // can arrive while that same persistent weapon is temporarily retired.
        var timed=new TimedActionRetirement();
        timed.Hold(20,10,1);
        Assert(timed.Held(20,10) && timed.Held(20,10.99f) && !timed.Held(3,10),"Timed retirement failed sibling/independent action isolation");
        timed.Hold(3,10.25f,1);
        Assert(timed.Held(20,10.99f) && timed.Held(3,10.99f),"Out-of-order action invalidated another weapon's cooldown");
        Assert(timed.Held(20,10.99f) && timed.Held(20,10.99f),"Paused gameplay advanced retirement");
        timed.Prune(11);
        Assert(!timed.Held(20,11) && timed.Held(3,11),"Persistent weapon was permanently retired or another owner released early");
        timed.Prune(11.25f);
        Assert(!timed.Held(3,11.25f),"Second weapon could not rearm at its own deadline");
        Console.WriteLine("PASS: same-frame/later sibling cooldowns, paused time, independent out-of-order actions and persistent-weapon rearming.");
        var preview=new AttackGeneration();
        Assert(preview.Preview(true,7)==1 && preview.Preview(true,7)==1 && preview.Observe(false,7)==0,"Preview mutated native activation state");
        var firstShot=new AttackVolume {Id=903,Activation=preview.Preview(true,7),Active=true,Damage=2,
            Category=AttackCategory.Projectile,ConsumeOnContact=true,Traits=AttackTraits.Reflectable,DamageGroupId=44,InitialDamageGroupHold=1,
            Points=new float[]{0,0,1,0,1,1,0,1}};
        AttackVolumes.Validate(new[]{firstShot});
        Assert(preview.Observe(true,7)==firstShot.Activation && preview.Preview(true,7)==firstShot.Activation,"First-shot policy lease differed from emitted activation");
        preview.Observe(false,7);Assert(preview.Preview(true,7)==2 && preview.Observe(true,7)==2,"Reused window preview could not rearm");
        Assert(preview.Preview(true,8)==3 && preview.Observe(true,8)==3,"Pool reuse preview lost native generation");
        var native = new AttackGeneration();
        Assert(native.Observe(false, 7) == 0 && native.Observe(true, 7) == 1 && native.Observe(true, 7) == 1, "Continuous window was regenerated");
        Assert(native.Observe(false, 7) == 1 && native.Observe(true, 7) == 2, "Interrupted native child could not rearm next authored window");
        Assert(native.Observe(true, 8) == 3, "Native pool reuse lost its activation");
        var minionLife=new AttackGeneration();var retiredLives=new AttackRetirement();
        long deadLife=minionLife.Observe(true,10);retiredLives.Retire(deadLife);
        long borrowedLife=minionLife.Observe(true,12); // Recycle/re-enable occurred between host samples.
        Assert(borrowedLife>deadLife && !retiredLives.Contains(borrowedLife) && minionLife.Observe(true,12)==borrowedLife,
            "Missed dormant sample retained a retired minion identity or regenerated a continuous native life");
        // Replay the reported enabled-child timeline through the production
        // adapter window and generation policy, including a second native cast.
        var tendril = new AttackGeneration(); var consumed = new AttackRetirement();
        long cast = tendril.Observe(YiGodhome.PureVesselAttackWindows.Tendril("Tendril Hit"), 5);
        Assert(consumed.Retire(cast), "Native tendril counter did not consume cast");
        foreach (string state in new[] { "Stun Start", "Stun Fall", "Stun", "Idle Stance", "Tendril Distance Check", "Tendril Antic", "Tendril Burst" })
            Assert(!YiGodhome.PureVesselAttackWindows.Tendril(state) && tendril.Observe(false, 5) == cast, "Lingering tendril admitted in " + state);
        long nextCast = tendril.Observe(YiGodhome.PureVesselAttackWindows.Tendril("Tendril Hit"), 5);
        Assert(nextCast > cast && !consumed.Contains(nextCast), "Old native child/counter state suppressed next tendril cast");
        foreach (string state in new[] { "Dstab Jump", "Dstab Air", "Stomp Antic", "Stomp Land", "Plume Gen", "Idle Stance" })
            Assert(!YiGodhome.PureVesselAttackWindows.Descending(state), "Idle/anticipation/land became body attack");
        Assert(YiGodhome.PureVesselAttackWindows.Descending("Stomp Down") && YiGodhome.PureVesselAttackWindows.Dash("Dash")
            && !YiGodhome.PureVesselAttackWindows.Dash("Dash Recover"), "Authored dash/descent window unavailable");
        var f = Frame(Guid.NewGuid());
        f.Volumes = new[] { new AttackVolume { Id = 123, Activation = 2, Active = true, Category = AttackCategory.Melee,
            Traits = AttackTraits.RetireOnParry | AttackTraits.LargeParryPush | AttackTraits.InitialParryPush | AttackTraits.PushRight, Points = new float[] { 0,0,1,0,1,1,0,1 } } };
        using var wire = new MemoryStream(); ContactCodec.Write(wire, f); wire.Position = 0;
        Assert(ContactCodec.Read(wire).Volumes[0].Traits == f.Volumes[0].Traits, "Strike retirement/push policy lost in transport");
        f.Volumes[0].Traits |= AttackTraits.PushLeft; Reject(() => ContactCodec.Validate(f));
        f.Volumes[0].Traits = AttackTraits.LargeParryPush | AttackTraits.PushLeft;
        using var left = new MemoryStream(); ContactCodec.Write(left, f); left.Position = 0;
        Assert(ContactCodec.Read(left).Volumes[0].Traits == f.Volumes[0].Traits, "Left facing lost in transport");
        f.Volumes[0].Traits = AttackTraits.PushRight;
        using var directed = new MemoryStream(); ContactCodec.Write(directed, f); directed.Position = 0;
        Assert(ContactCodec.Read(directed).Volumes[0].Traits == AttackTraits.PushRight, "Ordinary melee direction was conflated with stronger knockback");
        f.Volumes[0].Category = AttackCategory.Projectile; Reject(() => ContactCodec.Validate(f));
        f.Volumes[0].Category = AttackCategory.Melee;
        f.Volumes[0].Traits = AttackTraits.RetireOnParry | AttackTraits.LargeParryPush;
        f.Volumes[0].Category = AttackCategory.Projectile; Reject(() => ContactCodec.Validate(f));
        f.Volumes[0].Category = AttackCategory.Melee; f.Volumes[0].Traits = AttackTraits.InitialParryPush; Reject(() => ContactCodec.Validate(f));
        Assert(YiGodhome.PureVesselAttackWindows.DashWindup("Dash Antic") && !YiGodhome.PureVesselAttackWindows.Dash("Dash Antic")
            && YiGodhome.PureVesselAttackWindows.FocusWindup("Ball Up") && !YiGodhome.PureVesselAttackWindows.FocusWindup("Focus Burst")
            && YiGodhome.PureVesselAttackWindows.Focus("Focus Wait") && !YiGodhome.PureVesselAttackWindows.Focus("Idle Stance"), "Cue and attack windows overlap incorrectly");
        Console.WriteLine("PASS: independent strike retirement, stale/duplicate suppression, pool reuse and v16 facing/cue/presentation policy roundtrip.");
    }
    static void DenseVolley()
    {
        var blocker = new AttackVolume { Id = 701, Activation = 1, Category = AttackCategory.Melee, Active = false, BlocksProjectiles = true, Points = new float[] {0,0,1,0,1,1,0,1} };
        using (var bytes = new MemoryStream()) { using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true)) AttackVolumes.Write(writer, new[] { blocker }); bytes.Position=0; using var reader=new BinaryReader(bytes); var read=AttackVolumes.Read(reader)[0]; Assert(read.BlocksProjectiles && !read.Active, "Shield blocker was coupled to damage retirement"); var malformed=bytes.ToArray(); malformed[54]=2; Reject(() => AttackVolumes.Read(new BinaryReader(new MemoryStream(malformed)))); }
        blocker.Activation=0; Reject(() => AttackVolumes.Validate(new[] {blocker})); blocker.Activation=1;
        blocker.Category=AttackCategory.Projectile; Reject(() => AttackVolumes.Validate(new[] {blocker})); blocker.Category=AttackCategory.Melee;

        var id = Guid.NewGuid(); var f = Frame(id);
        f.Volumes = new AttackVolume[AttackVolumes.MaxCount];
        for (int i = 0; i < f.Volumes.Length; i++)
        {
            var points = new float[AttackVolumes.MaxVertices * 2];
            for (int j = 0; j < AttackVolumes.MaxVertices; j++)
            { points[j * 2] = i + (float)Math.Cos(j * Math.PI * 2 / AttackVolumes.MaxVertices); points[j * 2 + 1] = (float)Math.Sin(j * Math.PI * 2 / AttackVolumes.MaxVertices); }
            f.Volumes[i] = new AttackVolume { Id = i + 1, Activation = i + 1, Active = true, ConsumeOnContact = i > 0, Category = i == 0 ? AttackCategory.Melee : AttackCategory.Projectile, Points = points };
        }
        using var wire = new MemoryStream(); ContactCodec.Write(wire, f); wire.Position = 0;
        Assert(ContactCodec.Read(wire).Volumes.Length == 128, "Dense volley was truncated");
        using var ns = new ContactEndpoint(id, Role.NineSols); using var hk = new ContactEndpoint(id, Role.HollowKnight);
        hk.Publish(f); Assert(Until(() => ns.FreshFrame?.Volumes.Length == 128), "Maximum volley did not cross actual pipe");
        var last = ns.FreshFrame!.Volumes[127];
        Assert(last.Id == 128 && last.Activation == 128 && last.Points.Length == 128 && last.ConsumeOnContact, "Dense volley identities/vertices/impact semantics changed");
        var invalid = wire.ToArray(); invalid[158] = 129; Reject(() => ContactCodec.Read(new MemoryStream(invalid)));
        invalid = wire.ToArray(); invalid[4] = 8; Reject(() => ContactCodec.Read(new MemoryStream(invalid)));
        f.Volumes[0].ConsumeOnContact = true; Reject(() => ContactCodec.Validate(f)); f.Volumes[0].ConsumeOnContact = false;
        Array.Resize(ref f.Volumes, 129); Reject(() => ContactCodec.Validate(f));
        Console.WriteLine("PASS: 128 independent 64-vertex volumes over actual pipe; over-capacity and old protocol rejected.");
    }
    static void Verdict()
    {
        var v = new ContactVerdict(); Assert(!v.CanResolve, "Unstarted contact admitted");
        v.Begin(); long first = v.Generation;
        Assert(!v.Accept(first, false) && v.CanResolve, "Native refusal consumed contact");
        Assert(v.Accept(first, true) && !v.Accept(first, true), "Duplicate native verdict accepted");
        v.Begin(); Assert(!v.Accept(first, true) && v.CanResolve, "Prior generation affected new contact");
        v.Suspend(); Assert(!v.Accept(v.Generation, true), "Stale native verdict accepted");
        v.Begin(); Assert(v.Accept(v.Generation, true), "New independent contact rejected");
        v.Begin(); long pending=v.Generation;
        v.Drain(10,.09f); v.Tick(10);
        Assert(v.Draining && v.CanResolve && v.Generation==pending, "Contact close lost pending native generation");
        v.Tick(10.05f); Assert(v.Accept(pending,true) && !v.Accept(pending,true), "Pending hit did not settle exactly once");
        v.Begin(); v.Drain(20,.09f); v.Tick(20.09f);
        Assert(!v.CanResolve && !v.Draining, "Expired delayed hit admitted");
        v.Begin(); pending=v.Generation; v.Drain(30,.09f); v.Begin();
        Assert(!v.Draining && !v.Accept(pending,true), "Old pending generation entered new strike");
        v.Drain(40,.09f); v.Suspend(); Assert(!v.CanResolve && !v.Draining,"Parry/teardown did not revoke delayed hit");
        v.Begin(); pending=v.Generation; v.Drain(50,.09f); v.Resume();
        Assert(v.Generation==pending && v.Draining,"Resume replaced a pending contact generation");
        v.Suspend(); v.Begin(); bool rejected=false;
        try {v.Drain(60,float.NaN);} catch(ArgumentException){rejected=true;}
        Assert(rejected && !v.Draining,"Nonfinite drain created an unbounded lease");
    }
}
