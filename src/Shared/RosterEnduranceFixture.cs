using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using YiGodhome.Bridge;
using HollowSols.Foundation;
#if HOLLOW_KNIGHT
using HarmonyLib;
#endif

namespace YiGodhome;

public sealed partial class HollowSolsMenuPlugin
{
    private EncounterProfile? cachedEnduranceProfile;
    private string[] endurancePlan=Array.Empty<string>();
    private int enduranceIndex;
    private bool ContinuousEndurance=>fixtureScenario=="combat-Endurance-Sweep";
    private string EnduranceWorkload=>ContinuousEndurance?endurancePlan[enduranceIndex]:fixtureScenario.Substring(17);
    private void InitializeEndurancePlan() {
        if(!ContinuousEndurance)return;
        var lines=System.IO.File.ReadAllLines(System.IO.Path.Combine(fixtureFolder,"endurance-plan.request"));
        if(lines.Length<2 || lines.Length>46 || lines[0]!=fixtureId.ToString())throw new InvalidOperationException("Invalid continuous endurance plan identity/count.");
        var seen=new HashSet<string>();
        for(int i=1;i<lines.Length;i++) {
            if(!seen.Add(lines[i]) || !ValidCombatFixture("combat-Endurance-"+lines[i]) || lines[i]=="Sweep")throw new InvalidOperationException("Invalid/duplicate endurance workload.");
        }
        endurancePlan=new string[lines.Length-1];Array.Copy(lines,1,endurancePlan,0,endurancePlan.Length);
        if(fixtureSeconds<endurancePlan.Length*180+120 || fixtureSeconds>endurancePlan.Length*240+180)throw new InvalidOperationException("Continuous endurance lost its finite plan budget.");
    }
    private EncounterProfile EnduranceProfile {
        get {
            if(cachedEnduranceProfile!=null)return cachedEnduranceProfile;
            if(FixtureEnduranceAscended)return cachedEnduranceProfile=EncounterProfile.ForScene(EncounterProfile.Vengefly.Scene+"_V")
                ??throw new InvalidOperationException("Ascended Vengefly's native arena profile is missing.");
            string key=EnduranceWorkload;
            foreach(var profile in EncounterProfile.All)if(profile.Key==key)return cachedEnduranceProfile=profile;
            throw new InvalidOperationException("Unknown endurance statue.");
        }
    }
#if HOLLOW_KNIGHT
    private Harmony? enduranceHooks;
    private SessionToken? enduranceSession;
    private double enduranceAt;
    private int enduranceDamageHeld,enduranceCollections,enduranceCombatFrames,enduranceBodiesPeak;
    private bool enduranceStarted,enduranceReceiptWritten;
    private EncounterProfile? enduranceProfile;
    private readonly Dictionary<int,int> enduranceMaximum=new();
    private void BeginRosterEndurance(EncounterProfile profile) {
        enduranceStarted=false;enduranceDamageHeld=0;enduranceCollections=0;enduranceCombatFrames=0;enduranceBodiesPeak=0;enduranceMaximum.Clear();
        if(enduranceSession==null)enduranceSession=activeToken;
        if(enduranceSession==null || !Nullable.Equals(enduranceSession,activeToken))throw new InvalidOperationException("Endurance save identity changed.");
        enduranceProfile=profile;
        if(enduranceHooks!=null)return;
        enduranceHooks=new Harmony("local.hollowsols.fixture.endurance");
        // SubtractHealth also covers extra damage and the native shared Sisters
        // accumulator. First priority prevents another health adapter subtracting.
        enduranceHooks.Patch(AccessTools.Method(typeof(HealthManager),"SubtractHealth",new[]{typeof(int)}),
            prefix:new HarmonyMethod(typeof(HollowSolsMenuPlugin),nameof(EnduranceSubtract)){priority=Priority.First});
        enduranceHooks.Patch(AccessTools.Method(typeof(GCManager),"ForceCollect"),
            prefix:new HarmonyMethod(typeof(HollowSolsMenuPlugin),nameof(EnduranceCollection)));
    }
    private void AdvanceEndurancePlan() {
        if(!ContinuousEndurance || combatTestStage!=6)return;
        if(Now-combatTestAt>45)throw new InvalidOperationException("Continuous endurance Hall drain stalled.");
        if(!CombatReturnDrained)return;
        if(!enduranceReceiptWritten){System.IO.File.WriteAllText(System.IO.Path.Combine(fixtureFolder,"endurance-"+enduranceIndex+".complete"),fixtureId+"\n"+EnduranceWorkload);enduranceReceiptWritten=true;}
        if(enduranceIndex+1==endurancePlan.Length)return;
        var token=activeToken??throw new InvalidOperationException("Continuous endurance lost its save token.");
        if(!Nullable.Equals(enduranceSession,activeToken))throw new InvalidOperationException("Continuous endurance changed its loaded save session.");
        Log("ROSTER_ENDURANCE_HANDOFF completed="+EnduranceWorkload+" index="+enduranceIndex+" sameSave=True slot="+token.Slot.Number+" connection="+token.Connection);
        enduranceIndex++;enduranceReceiptWritten=false;cachedEnduranceProfile=null;combatFixtureProfile=null;combatStatue=null;combatInspect=null;
        enduranceProfile=null;enduranceStarted=false;enduranceMaximum.Clear();
        combatTestStage=1;combatTestAt=Now;
    }
    private static bool EnduranceSubtract(HealthManager __instance,int __0) {
        var owner=Instance;
        if(owner==null || !owner.FixtureRosterEndurance || owner.combatTestStage!=5
            || !NativeSaveContext.Enabled || owner.enduranceProfile==null
            || __instance.gameObject.scene.name!=owner.enduranceProfile.Scene)return true;
        var profile=owner.enduranceProfile;
        if(!profile.IsBossName(__instance.name) && !(profile.ArmorAndHead && __instance.name=="Head"))return true;
        int id=__instance.GetInstanceID();
        if(!owner.enduranceMaximum.TryGetValue(id,out var maximum))owner.enduranceMaximum.Add(id,maximum=Math.Max(1,__instance.hp));
        // Do not restore HP or invent phase thresholds. The conservative half-HP
        // hold is a diagnostic safety budget, not a native phase interpretation.
        if(!owner.enduranceStarted || owner.Now-owner.enduranceAt>=10 || __0>=__instance.hp || __instance.hp-__0<=maximum/2) {
            owner.enduranceDamageHeld++;
            return false;
        }
        return true;
    }
    private static void EnduranceCollection() {
        var owner=Instance;
        if(owner==null || !owner.FixtureRosterEndurance)return;
        bool timed=owner.enduranceStarted && owner.combatTestStage==5;
        if(timed)owner.enduranceCollections++;
        owner.Log("HK_ENDURANCE_NATIVE_COLLECTION boss="+owner.EnduranceWorkload+" timedCombat="+timed
            +" elapsed="+(timed?owner.Now-owner.enduranceAt:0).ToString("F3",CultureInfo.InvariantCulture)
            +" heapMB="+(GC.GetTotalMemory(false)/1048576.0).ToString("F2",CultureInfo.InvariantCulture)+" frame="+Time.frameCount+" policyUnchanged=True");
    }
    private void UpdateRosterEndurance(YiGodhome.Bridge.Pose pose,EncounterProfile profile) {
        if(!gameplay!.FixtureCombatActive)throw new InvalidOperationException("Endurance encounter ended before its timed completion.");
        if(!enduranceStarted) {
            if(!gameplay.FixturePairedInputReady || pose.Vitals?.EntryActive==true)return;
            enduranceAt=Now;enduranceStarted=true;
            Log("ROSTER_PERF_WINDOW_BEGIN workload="+EnduranceWorkload+" seconds=180 tier="+(FixtureEnduranceAscended?1:0)+" nativeAttacks=True fixtureInvulnerability=True outgoingHoldAfterSeconds=10 frame="+Time.frameCount);
        }
        enduranceCombatFrames++;
        double elapsed=Now-enduranceAt;
        if(elapsed>=180) {
            if(FixtureEnduranceAscended && enduranceBodiesPeak<2)throw new InvalidOperationException("Ascended Vengefly did not expose both live native boss bodies.");
            Log("ROSTER_PERF_WINDOW_END workload="+EnduranceWorkload+" elapsed="+elapsed.ToString("F3",CultureInfo.InvariantCulture)
                +" frames="+enduranceCombatFrames+" heldDamage="+enduranceDamageHeld+" nativeCollections="+enduranceCollections+" nativeBossBodiesPeak="+enduranceBodiesPeak+" healthNotReset=True nativeAttacksNotForced=True frame="+Time.frameCount);
            if(!ContinuousEndurance){enduranceHooks?.UnpatchSelf();enduranceHooks=null;}
            gameplay.FixturePairedControl(default(Controls));gameplay.FixtureTravelToHall();combatTestStage=6;combatTestAt=Now;return;
        }
        var contact=gameplay.FixtureContact;
        if(contact==null)return;
        int bossBodies=contact.HurtActive && contact.Health>0?1:0;
        foreach(var t in contact.Summons)if(t.BossBar && t.Active && t.Health>0 && t.Entity!=contact.Entity)bossBodies++;
        enduranceBodiesPeak=Math.Max(enduranceBodiesPeak,bossBodies);
        float x=contact.HurtX,y=contact.HurtY,height=contact.HurtHeight;
        bool target=contact.HurtActive;
        float closest=float.MaxValue;
        foreach(var t in contact.Summons) {
            if(!t.Active || t.Health<=0)continue;
            float distance=Math.Abs(t.X-pose.X);
            if(distance>=closest)continue;
            closest=distance;x=t.X;y=t.Y;height=t.Height;target=true;
        }
        double cycle=elapsed%1.5;
        Buttons action=Buttons.None;
        float move=target && Math.Abs(x-pose.X)>1.2f?Math.Sign(x-pose.X):0;
        if(pose.Vitals?.TalismanActive==true)action=Buttons.Talisman;
        else if(cycle>.2 && cycle<.35)action=Buttons.Parry;
        else if(cycle>.55 && cycle<.72)action=(int)(elapsed/6)%3==0?Buttons.Bow:(int)(elapsed/6)%3==1?Buttons.Attack:Buttons.Talisman;
        if(target && y>pose.Y+pose.Height+height*.5f && cycle<.18)action|=Buttons.Jump;
        gameplay.FixturePairedControl(new Controls(move,0,action));
    }
#else
    private EnduranceImmunityLease? enduranceImmunity;
    public sealed class EnduranceImmunityLease : MonoBehaviour {
        internal Health? Health;internal Guid Fixture;internal float Deadline;internal string Room="";
        private void Update() {
            var owner=Instance;
            if(Time.realtimeSinceStartup>=Deadline || owner==null || owner.fixtureId!=Fixture
                || !owner.FixtureRosterEndurance || owner.gameplay?.FixtureRoomReady(Room)!=true)Release();
        }
        private void Release(){if(Health!=null){Health.RemoveInvincible(gameObject);Health=null;Instance?.Log("NS_ENDURANCE_IMMUNITY_END nativeVoteRemoved=True");}Destroy(gameObject);}
        private void OnDestroy(){if(Health!=null){Health.RemoveInvincible(gameObject);Health=null;}}
    }
    private void UpdateRosterEnduranceImmunity() {
        if(enduranceImmunity!=null || !NativeSaveContext.Enabled || gameplay?.FixtureRoomReady(EnduranceProfile.Scene)!=true)return;
        var yi=gameplay.FixturePairedPlayer;
        if(yi==null)return;
        var vote=new GameObject("HollowSolsEnduranceFixture");enduranceImmunity=vote.AddComponent<EnduranceImmunityLease>();
        enduranceImmunity.Health=yi.health;enduranceImmunity.Fixture=fixtureId;
        enduranceImmunity.Room=EnduranceProfile.Scene;enduranceImmunity.Deadline=Time.realtimeSinceStartup+210;
        yi.health.BecomeInvincible(vote);
        Log("NS_ENDURANCE_IMMUNITY_BEGIN finiteSeconds=210 nativeVote=True disposablePair=True boss="+EnduranceWorkload);
    }
#endif
}
