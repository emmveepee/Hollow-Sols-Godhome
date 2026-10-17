using System;
using System.Linq;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// Native ball presentation and independent sword strikes have separate leases.
internal sealed class WatcherSequence
{
    private readonly HealthManager health;
    private readonly PlayMakerFSM control;
    private readonly Func<long> allocate;
    private readonly Action<string>? log;
    private readonly DamageHero[] swords;
    private readonly long[] swordActions=new long[2];
    private readonly GameObject sparks;
    private readonly AudioSource rollAudio, jumpAudio;
    private string previous="", mode="";
    private bool ball, countered;
    private float readyAt;
    private int frame=-1, samples;
    internal long Action {get;private set;}
    internal bool Present {get;private set;}
    internal bool Kick {get;private set;}
    internal WatcherSequence(HealthManager health,PlayMakerFSM control,Func<long> allocate,Action<string>? log)
    {
        this.health=health;this.control=control;this.allocate=allocate;this.log=log;
        foreach(var name in new[]{"Charge Start","Set Dir","Charge Antic","Charge","Hit Wall","Scale Sparks","Charge End","Charge Recover",
            "Jump Start","Aim","Jump Antic","Antic Air","Flip Jump Dir","Jump Launch","Jump Air","Bounce","Bounce Air","Land",
            "Roll Start","Set Dir 2","Roll","Roll End","Start Run","Slash1","Slash1 Recover","Slash2","Slash2 Recover"})
            if(!control.FsmStates.Any(s=>s.Name==name))throw new InvalidOperationException("Native Watcher sequence state changed: "+name);
        swords=new DamageHero[2];
        for(int i=0;i<2;i++) {
            var on=control.FsmStates.Single(s=>s.Name=="Slash"+(i+1)).Actions.OfType<ActivateGameObject>().Single();
            var part=FSMUtility.GetSafe(on.gameObject,on);
            if(!on.activate.Value || part==null || part.name!="Slash"+(i+1)+" Hit" || part.GetComponentInParent<HealthManager>(true)!=health
                || part.GetComponent<PolygonCollider2D>()==null || part.GetComponent<DamageHero>()==null)
                throw new InvalidOperationException("Native Watcher sword ownership changed.");
            swords[i]=part.GetComponent<DamageHero>();
        }
        var off=control.FsmStates.Single(s=>s.Name=="Charge End").Actions.OfType<ActivateGameObject>().Single();
        sparks=FSMUtility.GetSafe(off.gameObject,off);
        if(off.activate.Value || sparks==null || sparks.name!="Sparks" || !sparks.transform.IsChildOf(health.transform))
            throw new InvalidOperationException("Native Watcher spark cleanup ownership changed.");
        rollAudio=Audio("Charge End","Roll Audio");jumpAudio=Audio("Land","Spinjump Audio");
        log?.Invoke("HK_WATCHER_SEQUENCE_BOUND entity="+health.GetInstanceID()+" swords=2 nativeCleanup=True independentBody=True");
    }
    private AudioSource Audio(string state,string name) {
        var stop=control.FsmStates.Single(s=>s.Name==state).Actions.OfType<AudioStop>().Single();
        var target=FSMUtility.GetSafe(stop.gameObject,stop);
        if(target==null || target.name!=name || !target.transform.IsChildOf(health.transform) || target.GetComponent<AudioSource>()==null)
            throw new InvalidOperationException("Native Watcher audio cleanup ownership changed.");
        return target.GetComponent<AudioSource>();
    }
    private static bool Ground(string s)=>s=="Charge Start" || s=="Set Dir" || s=="Charge Antic" || s=="Charge" || s=="Hit Wall" || s=="Scale Sparks" || s=="Charge End";
    private static bool Air(string s)=>s=="Jump Start" || s=="Aim" || s=="Jump Antic" || s=="Antic Air" || s=="Flip Jump Dir" || s=="Jump Launch" || s=="Jump Air" || s=="Bounce" || s=="Bounce Air" || s=="Land";
    private static bool Back(string s)=>s=="Roll Start" || s=="Set Dir 2" || s=="Roll" || s=="Roll End";
    internal void Update() {
        if(frame==Time.frameCount || health==null)return;frame=Time.frameCount;
        string state=control.ActiveStateName;
        if(Time.time<readyAt && (state=="Charge Start" || state=="Jump Start" || state=="Roll Start")) {
            control.SetState("Start Run");
            if(samples++<64)log?.Invoke("HK_WATCHER_COOLDOWN_CHOICE entity="+health.GetInstanceID()+" skipped="+state.Replace(' ','_')+" nativeExit=Start_Run gameplayTime="+Time.time+" until="+readyAt+" frame="+Time.frameCount);
            state=control.ActiveStateName;
        }
        bool sequence=Ground(state)||Air(state)||Back(state);
        if(sequence&&!ball) {
            Action=allocate();countered=false;mode=Ground(state)?"ground":Air(state)?"air":"back";
            log?.Invoke("HK_WATCHER_BALL_BEGIN entity="+health.GetInstanceID()+" action="+Action+" mode="+mode+" gameplayTime="+Time.time+" frame="+Time.frameCount);
        }
        if(ball&&!sequence)log?.Invoke("HK_WATCHER_BALL_END entity="+health.GetInstanceID()+" action="+Action+" nativeState="+state.Replace(' ','_')+" frame="+Time.frameCount);
        ball=sequence;Present=ball&&!countered;
        Kick=Present&&(state=="Charge" || state=="Hit Wall" || state=="Scale Sparks" || state=="Jump Launch" || state=="Jump Air" || state=="Bounce" || state=="Bounce Air" || state=="Roll");
        for(int i=0;i<2;i++)if(state=="Slash"+(i+1)&&previous!=state) {
            swordActions[i]=allocate();
            log?.Invoke("HK_WATCHER_SWORD_BEGIN entity="+health.GetInstanceID()+" action="+swordActions[i]+" swing="+(i+1)+" push="+(health.transform.lossyScale.x<0?1:-1)+" frame="+Time.frameCount);
        }
        if(state!=previous && SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe") && samples++<64)
            log?.Invoke("HK_WATCHER_SEQUENCE_STATE entity="+health.GetInstanceID()+" action="+Action+" state="+state.Replace(' ','_')+" present="+Present+" kick="+Kick+" frame="+Time.frameCount);
        previous=state;
    }
    internal void Decorate(AttackVolume volume,DamageHero damage) {
        int index=Array.IndexOf(swords,damage);
        if(index>=0) {
            volume.ActionId=swordActions[index];volume.Active&=swordActions[index]>0;
            volume.Traits|=health.transform.lossyScale.x<0?AttackTraits.PushRight:AttackTraits.PushLeft;
        } else if(damage.gameObject==health.gameObject) {
            volume.ActionId=Action;volume.Active&=Kick;
            if(Present)volume.Traits|=AttackTraits.Kickable;
        }
    }
    internal bool Counter(long action,HitKind kind,int entity) {
        if(health==null || action<=0 || action!=Action || !Kick || entity!=0&&entity!=health.GetInstanceID()
            || kind!=HitKind.TaiChiKick&&kind!=HitKind.UnboundedCounter)return false;
        countered=true;Kick=Present=false;readyAt=Time.time+2;
        var body=health.GetComponent<Rigidbody2D>();if(body!=null)body.linearVelocity=Vector2.zero;
        string exit=mode=="air"?"Land":mode=="back"?"Roll End":"Charge End";
        control.SetState(exit);
        log?.Invoke("HK_WATCHER_COUNTER entity="+health.GetInstanceID()+" action="+action+" mode="+mode+" nativeExit="+exit.Replace(' ','_')+" sparks="+sparks.activeSelf
            +" rollAudio="+rollAudio.isPlaying+" jumpAudio="+jumpAudio.isPlaying+" gameplayTime="+Time.time+" readyAt="+readyAt+" nativeCleanup=True frame="+Time.frameCount);
        return true;
    }
}
