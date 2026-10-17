using System;
using System.Linq;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// A native damage accumulator feeding Adjust HP, not independent boss health.
internal sealed class GrimmStaggerBody
{
    private readonly HealthManager boss;
    private readonly PlayMakerFSM control;
    private readonly GetHP batReference;
    private readonly IntClamp clamp;
    private readonly Collider2D body;
    private readonly Action<string>? log;
    private HealthManager? bat;
    private Collider2D? batShape;
    private Renderer? batRenderer;
    private readonly ActivateGameObject? bowReference;
    private Collider2D? bowShape;
    private Renderer? bowRenderer;
    private string observed = "";
    private bool forced;
    private bool forcedTerminal;
    private readonly NativeAccumulatorDeathRelay transferDeath;
    internal GrimmStaggerBody(HealthManager boss, Action<string>? log)
    {
        this.boss = boss; this.log = log; body = boss.GetComponent<BoxCollider2D>();
        control = boss.GetComponents<PlayMakerFSM>().Single(f => f.FsmName == "Control");
        var death = boss.GetComponent<NativeGrimmDeathPresentation>() ?? boss.gameObject.AddComponent<NativeGrimmDeathPresentation>();
        death.Bind(boss, control, log);
        bowReference = control.FsmStates.SingleOrDefault(s => s.Name == "GG Bow")?.Actions.OfType<ActivateGameObject>().Single(a => a.gameObject.GameObject?.Name == "GG Bow");
        var adjust = control.FsmStates.Single(s => s.Name == "Adjust HP");
        batReference = adjust.Actions.OfType<GetHP>().Single(a => a.target.GameObject?.Name == "Real Bat Obj");
        clamp = adjust.Actions.OfType<IntClamp>().Single();
        if (clamp.minValue.Value != -50 || clamp.maxValue.Value != 0)
            throw new InvalidOperationException("Native Grimm bat damage cap changed.");
        transferDeath=boss.GetComponent<NativeAccumulatorDeathRelay>() ?? boss.gameObject.AddComponent<NativeAccumulatorDeathRelay>();
        transferDeath.Bind(adjust.Actions.OfType<SetHP>().Single(),boss,log);
    }
    private void Bind()
    {
        if (bowShape == null && bowReference != null) {
            var bow = FSMUtility.GetSafe(bowReference.gameObject, bowReference);
            if (bow != null) {
                if (bow.name != "Grimm Bow" || bow.scene != boss.gameObject.scene) throw new InvalidOperationException("Native Grimm bow ownership changed.");
                bowShape = bow.GetComponent<BoxCollider2D>(); bowRenderer = bow.GetComponent<Renderer>();
                if (bowShape == null || bowRenderer == null) throw new InvalidOperationException("Native Grimm bow receiver missing.");
                log?.Invoke("HK_GRIMM_BOW_BOUND boss=" + boss.GetInstanceID() + " collider=" + bowShape.GetInstanceID() + " nativeReceiver=True");
            }
        }
        if (bat != null) return;
        var native = FSMUtility.GetSafe(batReference.target, batReference);
        if (native == null) return; // native Init populates the authored reference
        bat = native.GetComponent<HealthManager>(); batShape = native.GetComponent<BoxCollider2D>(); batRenderer = native.GetComponent<Renderer>();
        if (bat == null || batShape == null || batRenderer == null || native.name != "Real Bat" || native.scene != boss.gameObject.scene)
            throw new InvalidOperationException("Native Grimm bat receiver ownership changed.");
        transferDeath.BindAccumulator(bat,()=>Accumulating);
        log?.Invoke("HK_GRIMM_BAT_BOUND boss=" + boss.GetInstanceID() + " receiver=" + bat.GetInstanceID() + " collider=" + batShape.GetInstanceID() + " cap=50 nativeAdjustment=True");
    }
    // Adjustment occurs after Reformed. Keep the pending native debit visible
    // throughout the return sequence, without applying it a second time.
    private bool Accumulating => control.ActiveStateName == "Exploded" || control.ActiveStateName == "Call Bats"
        || control.ActiveStateName == "Reforming" || control.ActiveStateName == "Reformed";
    private bool Exposed => bat != null && Accumulating && batShape!.enabled && batShape.gameObject.activeInHierarchy && batRenderer!.enabled;
    internal bool ExposesBat => Exposed;
    internal bool ExposesBow => bowShape != null && (control.ActiveStateName == "GG Bow" || control.ActiveStateName == "GG Bow Wait" || control.ActiveStateName == "GG Bow Return") && bowShape.enabled && bowShape.gameObject.activeInHierarchy && bowRenderer!.enabled;
    internal Collider2D Shape { get { Bind(); return Exposed ? batShape! : ExposesBow ? bowShape! : body; } }
    internal HealthManager Receiver { get { Bind(); return Exposed ? bat! : boss; } }
    internal HealthManager TalismanReceiver { get { Bind(); return bat != null && Accumulating ? bat : boss; } }
    internal bool Owns(HealthManager health) { Bind(); return health == boss || health == bat; }
    internal int Health { get { Bind(); return Math.Max(0, boss.hp + (bat != null && Accumulating ? Math.Max(-50, Math.Min(0, bat.hp)) : 0)); } }
    internal void Observe(Guid encounter, long activation)
    {
        Bind();
        if (bat == null || !SessionConfiguration.Current.RunFixtures) return;
        if (SessionConfiguration.Current.Has("stagger-probe") && !forced && control.ActiveStateName == "Slash Recover") {
            forced = true; control.SendEvent("STUN");
            log?.Invoke("HK_STAGGER_FIXTURE encounter=" + encounter + " entity=" + boss.GetInstanceID() + " event=STUN forced=True frame=" + Time.frameCount);
        }
        if(SessionConfiguration.Current.Has("stagger-probe") && !forcedTerminal && boss.hp > 0 && boss.hp <= 50 && !Accumulating) {
            forcedTerminal=true; control.SendEvent("STUN");
            log?.Invoke("HK_STAGGER_TERMINAL_FIXTURE entity="+boss.GetInstanceID()+" health="+boss.hp+" event=STUN nativeHealthUnchanged=True frame="+Time.frameCount);
        }
        string state = control.ActiveStateName + ":" + bat.hp + ":" + batShape!.enabled + ":" + batRenderer!.enabled;
        if (state == observed) return; observed = state;
        log?.Invoke("HK_GRIMM_BAT_STATE encounter=" + encounter + " entity=" + boss.GetInstanceID() + " activation=" + activation + " state=" + control.ActiveStateName.Replace(' ', '_')
            + " receiver=" + bat.GetInstanceID() + " rawHealth=" + bat.hp + " bossHealth=" + boss.hp + " presented=" + Health + " collider=" + batShape.enabled + " visible=" + batRenderer.enabled + " exposed=" + Exposed + " frame=" + Time.frameCount);
    }
}

// Native death presentation outlives contact ownership and its victory receipt.
// Grimm can enter this authored death state without HealthManager.isDead.
internal sealed class NativeGrimmDeathPresentation : MonoBehaviour
{
    private HealthManager boss = null!;
    private PlayMakerFSM control = null!;
    private Renderer body = null!;
    private Action<string>? log;
    private bool shown;
    private GetHP staggerReceiver=null!;
    private AudioStop[] staggerAudioStops=Array.Empty<AudioStop>();
    private static readonly System.Reflection.FieldInfo batOwners=HarmonyLib.AccessTools.Field(typeof(FakeBat),"fakeBats");
    private static readonly System.Reflection.FieldInfo batBoss=HarmonyLib.AccessTools.Field(typeof(FakeBat),"grimm");
    internal void Bind(HealthManager health, PlayMakerFSM fsm, Action<string>? logger)
    {
        if (!fsm.FsmStates.Any(s=>s.Name=="Death Start")) throw new InvalidOperationException("Native Grimm death state changed.");
        boss=health; control=fsm; log=logger;
        body=health.GetComponent<Renderer>() ?? throw new InvalidOperationException("Native Grimm death renderer missing.");
        staggerReceiver=fsm.FsmStates.Single(s=>s.Name=="Adjust HP").Actions.OfType<GetHP>().Single(a=>a.target.GameObject?.Name=="Real Bat Obj");
        staggerAudioStops=fsm.FsmStates.Single(s=>s.Name=="Call Bats").Actions.OfType<AudioStop>().ToArray();
        shown=false;
    }
    private void LateUpdate()
    {
        if (control == null || body == null || control.ActiveStateName != "Death Start") return;
        bool hidden=!body.enabled; body.enabled=true;
        if (!shown) {
            shown=true;
            int retired=0;
            var nativeBats=batOwners.GetValue(null) as System.Collections.Generic.List<FakeBat>;
            if(nativeBats!=null) for(int i=nativeBats.Count-1;i>=0;i--) {
                var bat=nativeBats[i];
                if(bat!=null && bat.gameObject.scene==boss.gameObject.scene && batBoss.GetValue(bat) as Transform==boss.transform) {
                    bat.gameObject.SetActive(false); retired++;
                }
            }
            var realBat=FSMUtility.GetSafe(staggerReceiver.target,staggerReceiver);
            if(realBat!=null && realBat.scene==boss.gameObject.scene) realBat.SetActive(false);
            int stopped=0;
            foreach(var action in staggerAudioStops) {
                var audioOwner=FSMUtility.GetSafe(action.gameObject,action);
                var audio=audioOwner==null?null:audioOwner.GetComponent<AudioSource>();
                if(audio!=null && audioOwner!.scene==boss.gameObject.scene) { audio.Stop(); stopped++; }
            }
            log?.Invoke("HK_GRIMM_DEATH_STAGGER_AUDIO_STOPPED entity="+boss.GetInstanceID()+" nativeOwners="+stopped+" exactCallBatsActions=True frame="+Time.frameCount);
            log?.Invoke("HK_GRIMM_DEATH_BATS_RETIRED entity="+boss.GetInstanceID()+" fakeBats="+retired+" realBatActive="+(realBat!=null && realBat.activeInHierarchy)+" exactNativeOwners=True frame="+Time.frameCount);
            log?.Invoke("HK_GRIMM_DEATH_REVEALED entity="+boss.GetInstanceID()+" previouslyHidden="+hidden+" visible="+body.enabled+" nativeDeathState=True state=Death_Start frame="+Time.frameCount);
        }
    }
}
