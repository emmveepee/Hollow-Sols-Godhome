using System;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// Observe authored phases; never set HP, immunity, states or completion.
internal sealed class NativeArmorHead : IDisposable
{
    private static NativeArmorHead? bound;
    internal static bool IsStandablePlatform(Collider2D collider,string scene) => bound!=null && bound.Armor!=null
        && bound.Armor.gameObject.scene.name==scene && collider==bound.platform;
    internal readonly HealthManager Armor, Head;
    private readonly PlayMakerFSM control;
    private readonly Collider2D headShape;
    private readonly Action<string>? log;
    private readonly Guid encounter;
    private readonly ArmorHeadPhase phase = new ArmorHeadPhase();
    private readonly RageStrike rage = new RageStrike();
    private readonly Func<long> allocate;
    private long strikeAction;
    private readonly DamageHero mace;
    private readonly BoxCollider2D maceShape;
    internal MeshRenderer[] RageParts => mace.GetComponents<MeshRenderer>();
    private readonly BoxCollider2D? armorTinger;
    private readonly BoxCollider2D platform;
    private int platformPoseSamples;
    private readonly BoxCollider2D armorShape;
    private readonly System.Collections.Generic.HashSet<long> clinkSwings=new();
    private long clinkExposure;
    private int clinkSamples;
    internal long ArmorLease => phase.ArmorLease;
    internal long HeadLease => phase.HeadLease;
    internal bool Broken => phase.Broken;
    internal bool Exposed => phase.Exposed;
    internal bool Raging=>rage.InSequence;
    private string state = "";
    private float chargeBegan;
    private readonly float chargeSeconds;
    private long releasedSequence;
    private float nextMaceTrace;
    private int maceSamples;
    private int geometrySamples;
    internal NativeArmorHead(EncounterProfile profile, Guid encounter, Action<string>? log, Func<long> allocate)
    {
        this.encounter = encounter; this.log = log; this.allocate=allocate;
        var health = Resources.FindObjectsOfTypeAll<HealthManager>().Where(h => h.gameObject.scene.name == profile.Scene).ToArray();
        Armor = health.Single(h => h.name == profile.Boss);
        Head = health.Single(h => h.name == "Head" && h.transform.IsChildOf(Armor.transform));
        control = Armor.GetComponents<PlayMakerFSM>().Single(f => f.FsmName == "FalseyControl");
        var nativePlatform=control.FsmStates.Single(s=>s.Name=="Roll End").Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>()
            .Select(a=>FSMUtility.GetSafe(a.gameObject,a)).Single(o=>o!=null && o.name=="FK Terrain Block");
        platform=nativePlatform.GetComponent<BoxCollider2D>()??throw new InvalidOperationException("Native staggered armor platform missing.");
        armorShape=Armor.GetComponent<BoxCollider2D>()??throw new InvalidOperationException("Native armor box missing.");
        headShape = Head.GetComponent<BoxCollider2D>() ?? throw new InvalidOperationException("Native head collider missing.");
        mace = Armor.GetComponentsInChildren<DamageHero>(true).Single(d => d.name == "Hitter");
        maceShape = mace.GetComponent<BoxCollider2D>() ?? throw new InvalidOperationException("Native mace collider missing.");
        if(SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe")) {
            var tinger=control.FsmStates.Single(s=>s.Name=="Opened 2").Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>()
                .Select(a=>FSMUtility.GetSafe(a.gameObject,a)).SingleOrDefault(o=>o!=null && o.name=="Tinger");
            armorTinger=tinger?.GetComponent<BoxCollider2D>();
        }
        chargeSeconds = new[] { "R Attack Antic" }.Sum(name => control.FsmStates.Single(s => s.Name == name)
            .Actions.OfType<HutongGames.PlayMaker.Actions.Wait>().Single().time.Value);
        if (chargeSeconds <= 0 || chargeSeconds > 5) throw new InvalidOperationException("Native rage anticipation duration changed.");
        foreach (string required in new[] { "Stun Start", "Opened", "Opened 2", "Hit", "Hit 2", "Idle Pause", "Stun Fail", "Rage", "Rage Slam", "Turn", "Particle Pause" })
            if (!control.FsmStates.Any(s => s.Name == required)) throw new InvalidOperationException("Native armor phase missing: " + required);
        if(bound!=null) throw new InvalidOperationException("Overlapping native armor platform owners.");
        bound=this;
        log?.Invoke("HK_NATIVE_ARMOR_PLATFORM_BOUND collider="+platform.GetInstanceID()+" owner="+Armor.GetInstanceID()+" scene="+profile.Scene+" authoredReference=True layer="+platform.gameObject.layer);
        log?.Invoke("HK_ARMOR_BOUND encounter=" + encounter + " armor=" + Armor.GetInstanceID() + " head=" + Head.GetInstanceID() + " armorHP=" + Armor.hp + " headHP=" + Head.hp + " native=True");
    }
    public void Dispose() {if(bound==this)bound=null;}
    internal bool Owns(HealthManager h) => h == Armor || h == Head;
    internal bool Damageable(HealthManager h) => h == Head ? phase.HeadDamageState && Head.gameObject.activeInHierarchy && headShape.enabled : h == Armor && !Broken;
    internal int PresentedHealth(HealthManager h) => h == Armor ? phase.ArmorHealth(h.hp) : Math.Max(0, h.hp);
    internal void Melee(YiGodhome.Bridge.Pose pose,HostHitReceiver receiver)
    {
        // Native body taps and exposed-head damage are independent receivers;
        // one real swing can overlap both, just like the vanilla nail.
        if((control.ActiveStateName!="Opened" && control.ActiveStateName!="Opened 2") || !Exposed || !Armor.IsInvincible
            || !armorShape.enabled || !Armor.gameObject.activeInHierarchy) return;
        if(clinkExposure!=HeadLease) {clinkExposure=HeadLease;clinkSwings.Clear();}
        foreach(var window in pose.Melee) {
            if(window.Swing<=0 || clinkSwings.Contains(window.Swing)) continue;
            var bounds=new Bounds(new Vector3(window.X,window.Y,armorShape.bounds.center.z),new Vector3(window.Width,window.Height,1));
            if(!armorShape.bounds.Intersects(bounds)) continue;
            if(clinkSwings.Count>=64) throw new InvalidOperationException("Native armor clink swing budget exceeded.");
            clinkSwings.Add(window.Swing); // Uncertain native outcomes are never replayed.
            int before=Armor.hp;
            receiver.NativeArmorClink(Armor,window.X<armorShape.bounds.center.x?0:180);
            if(clinkSamples++<64) log?.Invoke("HK_NATIVE_ARMOR_CLINK encounter="+encounter+" swing="+window.Swing+" collider="+window.Collider
                +" activation="+window.Activation+" before="+before+" after="+Armor.hp+" nativeState="+control.ActiveStateName.Replace(' ','_')+" frame="+Time.frameCount);
        }
    }
    internal void ObservePose(YiGodhome.Bridge.Pose? pose)
    {
        if(!SessionConfiguration.Current.RunFixtures || pose==null || !pose.Grounded || !platform.enabled
            || !platform.gameObject.activeInHierarchy || platformPoseSamples>=16) return;
        var bounds=platform.bounds;
        if(pose.X<bounds.min.x || pose.X>bounds.max.x || Math.Abs(pose.Y-bounds.max.y)>.1f) return;
        platformPoseSamples++;
        log?.Invoke("HK_NATIVE_ARMOR_STANDING encounter="+encounter+" collider="+platform.GetInstanceID()+" grounded=True nativeTop="+bounds.max.y
            +" yiX="+pose.X+" yiY="+pose.Y+" nativeState="+control.ActiveStateName.Replace(' ','_')+" frame="+Time.frameCount);
    }
    internal void Decorate(AttackVolume volume, DamageHero source)
    {
        if (source != mace || !rage.InSequence) return;
        volume.ActionId = strikeAction; volume.Traits |= AttackTraits.Crimson;
        // The mace is an attached native melee strike, not emitted debris.
        // AccurateParry must accumulate its internal injury on this armor owner.
        volume.CounterInjuryEntity = Armor.GetInstanceID();
        // Installed native Rage already plays attack0005..7 with animated mace
        // collision. Its Wait is animation duration, not anticipation. Turn and
        // Particle Pause restore body-shaped geometry and must not damage Yi.
        volume.Active &= rage.Striking;
    }
    internal void Warning(ContactFrame frame)
    {
        if (!rage.InSequence) return;
        var center = maceShape.transform.TransformPoint(maceShape.offset);
        frame.X = center.x; frame.Y = center.y; frame.AttackId = strikeAction;
        var scale = maceShape.transform.lossyScale;
        frame.Width = maceShape.size.x * Math.Abs(scale.x);
        frame.Height = maceShape.size.y * Math.Abs(scale.y);
        frame.Crimson = true; frame.Windup = rage.Windup; frame.QuickWarning = false;
        frame.CrimsonSequence = rage.Sequence;
        frame.CrimsonCharge = rage.InitialCharge ? Mathf.Clamp01((Time.time - chargeBegan) / chargeSeconds) : 1;
        frame.CrimsonRight = Armor.transform.localScale.x >= 0;
    }
    internal bool Counter(long action)
    {
        Observe();
        if (action!=strikeAction || !rage.Counter(rage.Action)) return false;
        log?.Invoke("HK_MACE_COUNTER encounter=" + encounter + " action=" + action + " state=" + state.Replace(' ', '_') + " interrupted=False frame=" + Time.frameCount);
        return true;
    }
    internal void Observe()
    {
        string next = control.ActiveStateName;
        bool wasBroken = Broken, wasExposed = Exposed;
        phase.Observe(next, Head.gameObject.activeInHierarchy);
        long priorAction = rage.Action;
        long priorSequence = rage.Sequence;
        bool wasRaging = rage.InSequence;
        rage.Observe(next);
        if(rage.Action!=priorAction) strikeAction=allocate();
        if (rage.InSequence && SessionConfiguration.Current.RunFixtures && maceSamples < 240 && Time.realtimeSinceStartup >= nextMaceTrace) {
            nextMaceTrace = Time.realtimeSinceStartup + .1f; maceSamples++;
            var sprite = mace.GetComponent<tk2dSprite>();
            var center = maceShape.transform.TransformPoint(maceShape.offset);
            log?.Invoke("HK_MACE_GEOMETRY state=" + next.Replace(' ', '_') + " action=" + rage.Action + " frame=" + Time.frameCount
                + " collider=" + maceShape.enabled + " object=" + mace.gameObject.activeInHierarchy + " offset=" + maceShape.offset + " size=" + maceShape.size
                + " scale=" + maceShape.transform.lossyScale + " center=" + center + " sprite=" + sprite.CurrentSprite.name + " bounds=" + sprite.GetComponent<Renderer>().bounds);
        }
        if (rage.Sequence != priorSequence) {
            chargeBegan = Time.time;
            log?.Invoke("HK_RAGE_VISUAL_BEGIN encounter=" + encounter + " sequence=" + rage.Sequence + " chargeSeconds=" + chargeSeconds + " frame=" + Time.frameCount);
        }
        if (rage.InSequence && !rage.InitialCharge && releasedSequence != rage.Sequence) {
            releasedSequence = rage.Sequence;
            log?.Invoke("HK_RAGE_VISUAL_RELEASE encounter=" + encounter + " sequence=" + rage.Sequence + " frame=" + Time.frameCount);
        }
        if (wasRaging && !rage.InSequence)
            log?.Invoke("HK_RAGE_VISUAL_END encounter=" + encounter + " sequence=" + rage.Sequence + " state=" + next.Replace(' ', '_') + " frame=" + Time.frameCount);
        if (rage.Action != priorAction) {
            var center = maceShape.transform.TransformPoint(maceShape.offset);
            log?.Invoke("HK_MACE_WINDUP encounter=" + encounter + " action=" + rage.Action + " collider=" + maceShape.GetInstanceID()
                + " x=" + center.x + " y=" + center.y + " frame=" + Time.frameCount);
        }
        if (next != state || wasBroken != Broken || wasExposed != Exposed) {
            state = next;
            log?.Invoke("HK_ARMOR_PHASE encounter=" + encounter + " frame=" + Time.frameCount + " state=" + next.Replace(' ', '_')
                + " broken=" + Broken + " exposed=" + Exposed + " armor=" + Armor.GetInstanceID() + " armorLease=" + ArmorLease
                + " armorHP=" + Armor.hp + " presentedArmor=" + phase.ArmorHealth(Armor.hp)
                + " head=" + Head.GetInstanceID() + " headLease=" + HeadLease + " headHP=" + Head.hp);
            if(SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe") && geometrySamples++<80) {
                var shapes=new[]{Armor.GetComponent<BoxCollider2D>(),headShape,maceShape,armorTinger};
                foreach(var shape in shapes) {
                    if(shape==null) continue;
                    var receiver=shape.GetComponent<HealthManager>();
                    log?.Invoke("HK_ARMOR_NATIVE_GEOMETRY state="+next.Replace(' ','_')+" collider="+shape.GetInstanceID()+" name="+shape.name.Replace(' ','_')
                        +" enabled="+shape.enabled+" active="+shape.gameObject.activeInHierarchy+" layer="+shape.gameObject.layer+" trigger="+shape.isTrigger+" bounds="+shape.bounds
                        +" nativeBody="+(shape.attachedRigidbody?.bodyType.ToString()??"none")+" health="+(receiver?.hp??-1)+" invincible="+(receiver?.IsInvincible??false));
                }
            }
        }
    }
}
