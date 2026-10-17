using System;
using System.IO;
using System.Linq;
using System.Globalization;
using UnityEngine;
using YiGodhome.Bridge;
using HollowSols.Foundation;

namespace YiGodhome;

public sealed partial class HollowSolsMenuPlugin
{
    private bool CombatFixture=>fixtureScenario.StartsWith("combat-",StringComparison.Ordinal);
    private static bool ValidCombatFixture(string scenario)=>scenario.StartsWith("combat-Endurance-",StringComparison.Ordinal) ? scenario=="combat-Endurance-Sweep" || scenario=="combat-Endurance-VengeflyKingAscended" || EncounterProfile.All.Any(p=>p.Key==scenario.Substring(17)) : new[]{"combat-SoulWarrior","combat-SoulMaster","combat-SoulTyrant","combat-AbsoluteRadiance","combat-RadianceFinish","combat-RadianceRainPerf","combat-RadianceArrowPerf","combat-RadianceIdlePerf","combat-RadianceRainVisual","combat-Collector","combat-Nosk","combat-GruzMother"}.Contains(scenario);
#if HOLLOW_KNIGHT
    private int combatTestStage,combatReflections,combatImpacts,combatJarBreaks,combatJarSpawns;
    private double combatTestAt,combatPulseUntil,combatNextPulse;
    private PlayMakerFSM? combatInspect;
    private BossStatue? combatStatue;
    private readonly System.Collections.Generic.HashSet<int> combatSpawnedEnemies=new();
    private readonly System.Collections.Generic.Dictionary<long,long> combatSwingFrames=new();
    private bool combatSameSwing;
    private long combatJarSwing;
    private bool radianceFinishArmed,radianceFinishEvent,radianceNativeVictory;
    private HealthManager? radianceFixtureBoss;
    private int radiancePerfStage,radiancePerfBowHits,radiancePerfBladeFrames,radianceVisualFrame;
    private double radiancePerfAt;
    private bool radiancePerfPlaced;
    private EncounterProfile? combatFixtureProfile;
    private int radianceIdleHp;
    private float radianceIdleX,radianceIdleY;
    private int gruzTalismanHits;
    private int noskJumpStage;
    private float noskJumpStart,noskJumpMaximum;
    internal void FixtureObserveGameplay(string message)
    {
        if(fixtureId==Guid.Empty || !CombatFixture)return;
        if(FixtureRosterEndurance)return;
        string Value(string key)=>message.Split(' ').FirstOrDefault(s=>s.StartsWith(key+"=",StringComparison.Ordinal))?.Substring(key.Length+1)??"";
        if(fixtureScenario=="combat-GruzMother" && message.StartsWith("HK_NATIVE_HIT ",StringComparison.Ordinal)
            && Value("kind")=="Talisman" && int.TryParse(Value("before"),out var oldHp) && int.TryParse(Value("after"),out var newHp) && oldHp>newHp)gruzTalismanHits++;
        if(message.StartsWith("HK_PROJECTILE_REFLECTED ",StringComparison.Ordinal)
            && (fixtureScenario!="combat-AbsoluteRadiance" || Value("kind")=="RadianceOrb"))combatReflections++;
        if(message.StartsWith("HK_REFLECTED_HIT ",StringComparison.Ordinal))combatImpacts++;
        if(FixtureRadiancePerformance && message.StartsWith("HK_NATIVE_ARROW_ROUTE ",StringComparison.Ordinal) && int.TryParse(Value("before"),out var bowBefore) && int.TryParse(Value("after"),out var bowAfter) && bowBefore>bowAfter)radiancePerfBowHits++;
        if(fixtureScenario=="combat-RadianceFinish") {
            if(message.StartsWith("HK_RADIANCE_FINAL_NAIL ",StringComparison.Ordinal))radianceFinishEvent=true;
            if(message.StartsWith("HK_HUB_COMBAT_ENDED victory=True",StringComparison.Ordinal))radianceNativeVictory=true;
        }
        if(message.StartsWith("HK_JAR_MELEE_BREAK ",StringComparison.Ordinal)) {combatJarBreaks++;long.TryParse(Value("swing"),out combatJarSwing);}
        if(message.StartsWith("HK_JAR_SPAWNED_ENEMY ",StringComparison.Ordinal) && int.TryParse(Value("entity"),out var entity)) {combatJarSpawns++;combatSpawnedEnemies.Add(entity);}
        if(message.StartsWith("HK_SUMMON_HIT ",StringComparison.Ordinal) && Value("kind")=="Melee" && int.TryParse(Value("entity"),out entity)
            && combatSpawnedEnemies.Contains(entity) && long.TryParse(Value("nativeFrame"),out var frame)
            && combatSwingFrames.TryGetValue(frame,out var swing) && swing==combatJarSwing
            && int.TryParse(Value("before"),out var before) && int.TryParse(Value("after"),out var after) && before>after && after>=0) {
            combatSameSwing=true;Log("COLLECTOR_SAME_SWING_NATIVE_DAMAGE entity="+entity+" swing="+swing+" nativeFrame="+frame);
        }
    }
    private void UpdateCombatFixture()
    {
        if(!FixtureCommandsAllowed || gameplay==null)throw new InvalidOperationException("Combat fixture lost its disposable native context.");
        if(ContinuousEndurance && combatTestStage==6){AdvanceEndurancePlan();return;}
        bool finish=fixtureScenario=="combat-RadianceFinish";
        var profile=combatFixtureProfile??=finish || FixtureRadiancePerformance?EncounterProfile.AbsoluteRadiance:FixtureRosterEndurance?EnduranceProfile:EncounterProfile.All.Single(p=>p.Key==fixtureScenario.Substring(7));
        var peer=gameplay.FixturePairedPeer;var pose=peer?.Pose;var nativeFrame=peer?.Frame??-1;
        if(pose!=null && fixtureScenario=="combat-Collector") {
            foreach(var window in pose.Melee)combatSwingFrames[nativeFrame]=window.Swing;
            foreach(var frame in combatSwingFrames.Keys.Where(f=>nativeFrame-f>180).ToArray())combatSwingFrames.Remove(frame);
        }
        if(combatTestStage==0) {if(!gameplay.FixturePairedWorldReady)return;gameplay.FixturePairedControl(default(Controls));gameplay.FixtureTravelToHall();combatTestAt=Now;combatTestStage=1;return;}
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if(finish && combatTestStage==7) {
            if(scene=="GG_Workshop" && gameplay.FixturePairedWorldReady && gameplay.PairOutcomesDrained) {
                if(!radianceFinishEvent || !radianceNativeVictory)throw new InvalidOperationException("Reflected finale returned without native event/victory.");
                Log("RADIANCE_REFLECTED_FINALE_PASS nativeFinalHit=True nativeVictory=True nativeReturn=True actualProjectileCollision=True forcedTerminalWindowOnly=True");combatTestStage=6;
            } else if(Now-combatTestAt>35)throw new InvalidOperationException("Radiance reflected finale did not complete its native return.");
            return;
        }
        if(combatTestStage is >=1 and <=4 && Now-combatTestAt>35)throw new InvalidOperationException("Combat fixture native setup stalled at stage "+combatTestStage+" in "+scene);
        if(combatTestStage==1 && scene=="GG_Workshop" && gameplay.FixturePairedWorldReady) {
            var statue=Resources.FindObjectsOfTypeAll<BossStatue>().Single(s=>s.gameObject.scene.name==scene && (s.statueStatePD==profile.Statue || s.dreamStatueStatePD==profile.Statue));
            combatStatue=statue;
            // Reproduce selecting the native regular/dream lever, rather than
            // inheriting the copied save's last statue variant.
            bool dream=statue.dreamStatueStatePD==profile.Statue;
            if(statue.UsingDreamVersion!=dream)statue.SetDreamVersion(dream,true,false);
            combatInspect=statue.GetComponentsInChildren<PlayMakerFSM>().Single(f=>f.name=="Inspect" && f.FsmName=="npc_control");
            var position=combatInspect.transform.position;
            var ground=Physics2D.RaycastAll(position+Vector3.up*2,Vector2.down,10,1<<8).Where(h=>h.collider!=null && !h.collider.isTrigger && h.normal.y>.99f).OrderBy(h=>h.distance).First();
            string placement=Path.Combine(fixtureFolder,ContinuousEndurance?"combat-place-"+enduranceIndex+".request":"combat-place.request");
            File.WriteAllText(placement+".tmp",fixtureId+"\nGG_Workshop\n"+position.x.ToString("R",CultureInfo.InvariantCulture)+"\n"+ground.point.y.ToString("R",CultureInfo.InvariantCulture)+(ContinuousEndurance?"\n"+enduranceIndex:""));
            File.Move(placement+".tmp",placement); // Immutable per-case publication; readers never block a later case.
            combatTestStage=2;combatTestAt=Now;return;
        }
        if(combatTestStage==2 && Now-combatTestAt>1 && combatInspect!=null && combatInspect.ActiveStateName.StartsWith("In Range",StringComparison.Ordinal) && HeroController.instance.CanInteract()) {
            InteractionBinding.ScriptedPressUntil=Time.realtimeSinceStartup+.15f;combatTestStage=3;combatTestAt=Now;return;
        }
        if(combatTestStage==3) {
            var ui=UnityEngine.Object.FindFirstObjectByType<BossChallengeUI>();
            if(ui!=null && combatStatue?.bossUIControlFSM.ActiveStateName=="Open UI") {int tier=FixtureEnduranceAscended?1:0;ui.LoadBoss(tier);combatTestStage=4;combatTestAt=Now;Log("COMBAT_FIXTURE_NATIVE_CHALLENGE boss="+profile.Key+" tier="+tier);}
            return;
        }
        if(combatTestStage==4 && gameplay.FixtureCombatActive) {
            if(scene!=profile.Scene)throw new InvalidOperationException("Native statue launched "+scene+" instead of "+profile.Scene);
            HealthManager? finishBoss=null;
            if(finish || FixtureRadiancePerformance) {
                var target=gameplay.FixtureContact;
                var receiver=target?.Summons.SingleOrDefault(t=>t.BossBar && t.Active && t.Health>0);
                if(receiver==null)return;
                finishBoss=Resources.FindObjectsOfTypeAll<HealthManager>().SingleOrDefault(h=>h.GetInstanceID()==receiver.Entity && h.gameObject.scene.name==scene);
                if(finishBoss==null || !finishBoss.gameObject.activeInHierarchy)return;
                radianceFixtureBoss=finishBoss;
            }
            combatTestStage=5;combatTestAt=Now;
            if(FixtureRosterEndurance)BeginRosterEndurance(profile);
            if(FixtureRadiancePerformance) {
                radiancePerfAt=Now;
                var control=finishBoss!.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Control");
                control.SetState(fixtureScenario=="combat-RadianceArrowPerf"?"Stun1 Start":"Rage1 Tele");
                Log("RADIANCE_PERF_SETUP workload="+fixtureScenario+" nativeState="+control.ActiveStateName.Replace(' ','_')+" healthNotWritten=True resourceOverrides=False forcedAttackWindowOnly=True frame="+Time.frameCount);
            }
            if(finish) {
                finishBoss!.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Attack Choices").SetState("Orb Wait");
                Log("RADIANCE_FINALE_FIXTURE_ORB_REQUEST nativeChoice=True bounded=True healthNotWritten=True");
            }
        }
        if(combatTestStage!=5 || pose==null)return;
        if(pose.Vitals?.Dead==true)throw new InvalidOperationException("Native Yi died before the requested mechanical evidence; no resource override.");
        if(FixtureRosterEndurance) {UpdateRosterEndurance(pose,profile);return;}
        if(FixtureRadiancePerformance) {UpdateRadiancePerformance(pose);return;}
        if(profile==EncounterProfile.Nosk) {
            if(noskJumpStage==0) {
                File.WriteAllText(Path.Combine(fixtureFolder,"nosk-wall.request"),fixtureId+"\nGG_Nosk\n87.6\n3");
                noskJumpStage=1;combatTestAt=Now;return;
            }
            if(noskJumpStage==1 && gameplay.FixturePairedInputReady && pose.Vitals?.EntryActive!=true && pose.Grounded && pose.X>86.5f && pose.X<88) {
                noskJumpStart=pose.Y;noskJumpMaximum=pose.Y;noskJumpStage=2;combatTestAt=Now;
            }
            if(noskJumpStage==2) {
                noskJumpMaximum=Math.Max(noskJumpMaximum,pose.Y);
                // Hold into the repaired left face while using Yi's native jump.
                gameplay.FixturePairedControl(new Controls(1,0,Now-combatTestAt<.2?Buttons.Jump:Buttons.None));
                if(noskJumpMaximum-noskJumpStart>1.5f) {
                    Log("NOSK_LEFT_WALL_NATIVE_JUMP rise="+(noskJumpMaximum-noskJumpStart)+" x="+pose.X+" groundedStart=True nativePhysics=True");
                    gameplay.FixturePairedControl(default(Controls));gameplay.FixtureTravelToHall();combatTestStage=6;
                } else if(Now-combatTestAt>3)throw new InvalidOperationException("Nosk left-wall native jump failed; rise="+(noskJumpMaximum-noskJumpStart));
            } else if(Now-combatTestAt>12)throw new InvalidOperationException("Nosk wall placement did not settle on native safe ground.");
            return;
        }
        var frameData=gameplay.FixtureContact;
        if(frameData==null)return;
        if(finish && !radianceFinishArmed && combatReflections>0) {
            var boss=radianceFixtureBoss;
            if(boss==null || !frameData.Summons.Any(t=>t.Entity==boss.GetInstanceID()) || boss.isDead)throw new InvalidOperationException("Finale fixture lost its exact native receiver.");
            var fsm=boss.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Control");
            fsm.SetState("Death Ready");radianceFinishArmed=true;combatTestAt=Now;
            Log("RADIANCE_FINALE_FIXTURE_ARMED nativeWindow=Death_Ready hp="+boss.hp+" healthNotWritten=True actualReflectedFlight=True");
        }
        if(finish && radianceFinishEvent){gameplay.FixturePairedControl(default(Controls));combatTestStage=7;combatTestAt=Now;return;}
        if(profile.Key=="GruzMother") {
            if(gruzTalismanHits>=2) {
                Log("GRUZ_WATER_FIXTURE_COMPLETE talismanHits="+gruzTalismanHits+" nativeDamage=True victoryClaimed=False");
                gameplay.FixturePairedControl(default(Controls));gameplay.FixtureTravelToHall();combatTestStage=6;return;
            }
            if(Now-combatTestAt>45)throw new InvalidOperationException("Gruz water fixture exceeded bounded attempts: talismanHits="+gruzTalismanHits);
            float dx=frameData.HurtX-pose.X;
            float cycle=(float)((Now-combatTestAt)%3.2);
            bool aligned=Math.Abs(dx)<2.5f && Math.Abs(frameData.HurtY-(pose.Y+pose.Height*.5f))<2;
            float approach=Math.Abs(dx)>1.2f?Math.Sign(dx):0;
            Buttons action=Buttons.None;
            if(gruzTalismanHits<2) {
                if(aligned && cycle<.18f && pose.Vitals?.Chi>=1)action=Buttons.Talisman;
                else if(cycle>2.1f && cycle<2.23f)action=Buttons.Parry;
                if(frameData.HurtY>pose.Y+2 && pose.Grounded && cycle>2.5f)action|=Buttons.Jump;
            }
            gameplay.FixturePairedControl(new Controls(approach,0,action));return;
        }
        bool collector=profile==EncounterProfile.Collector;
        var shot=frameData.Volumes.Where(v=>v.Active && (collector?v.Category==AttackCategory.Melee && v.NotifyParry:(v.Traits&AttackTraits.Reflectable)!=0))
            .OrderBy(v=>Math.Abs((v.Points.Where((_,i)=>i%2==0).Min()+v.Points.Where((_,i)=>i%2==0).Max())*.5f-pose.X)).FirstOrDefault();
        float move=0;Buttons buttons=Buttons.None;
        if(shot!=null) {
            float x=(shot.Points.Where((_,i)=>i%2==0).Min()+shot.Points.Where((_,i)=>i%2==0).Max())*.5f;
            float bottom=shot.Points.Where((_,i)=>i%2==1).Min(),top=shot.Points.Where((_,i)=>i%2==1).Max();
            float delta=x-pose.X;
            if(collector && Math.Abs(delta)>1)move=Math.Sign(delta);
            bool near=Math.Abs(delta)<(collector?2:1.4f) && bottom<pose.Y+pose.Height+.7f && top>pose.Y-.5f;
            if(near && Now>=combatNextPulse) {combatPulseUntil=Now+(collector ? .12 : .08);combatNextPulse=Now+(collector ? .6 : .4);Log("COMBAT_FIXTURE_INPUT_CUE collider="+shot.Id+" activation="+shot.Activation+" gapX="+Math.Abs(delta)+" kind="+(collector?"Melee":"LateParry"));}
            if(!collector && Now>=combatPulseUntil && Math.Abs(delta)>1.4f && Math.Abs(delta)<3)move=Math.Sign(delta);
        }
        if(Now<combatPulseUntil)buttons=collector?Buttons.Attack:Buttons.Parry;
        gameplay.FixturePairedControl(new Controls(move,0,buttons));
        bool done=!finish && (collector?combatSameSwing:combatReflections>=3 && combatImpacts>=1);
        if(done) {
            Log("COMBAT_FIXTURE_MECHANICS_COMPLETE boss="+profile.Key+" reflected="+combatReflections+" impacts="+combatImpacts+" jars="+combatJarBreaks+" spawns="+combatJarSpawns+" sameSwing="+combatSameSwing+" victoryClaimed=False");
            gameplay.FixturePairedControl(default(Controls));gameplay.FixtureTravelToHall();combatTestStage=6;
        }
    }
    private void UpdateRadiancePerformance(YiGodhome.Bridge.Pose pose) {
        // ScreenCapture renders at end of frame. Keep native attack/aura owners
        // alive through that render, instead of cancelling them with the request.
        if(radiancePerfStage==3) {
            gameplay!.FixturePairedControl(default(Controls));
            if(Time.frameCount>=radianceVisualFrame) {gameplay.FixtureTravelToHall();combatTestStage=6;}
            return;
        }
        var boss=radianceFixtureBoss??throw new InvalidOperationException("Radiance performance lost its native receiver.");
        var control=boss.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Control");
        var contact=gameplay!.FixtureContact;
        if(contact==null)return;
        if(FixtureRadianceIdle) {UpdateRadianceIdle(pose,boss,control,contact);return;}
        bool rain=fixtureScenario=="combat-RadianceRainPerf";
        if(rain) {
            int blades=contact.Volumes.Count(v=>v.Active && (v.Traits&AttackTraits.Crimson)!=0 && (v.Traits&AttackTraits.Reflectable)!=0);
            if(blades>0)radiancePerfBladeFrames++;
            if(radiancePerfStage==0 && control.ActiveStateName=="Rage Comb" && blades>0) {
                radiancePerfStage=1;radiancePerfAt=Now;
                Log("RADIANCE_PERF_WINDOW_BEGIN workload=rain nativeBlades="+blades+" frame="+Time.frameCount);
            }
            if(radiancePerfStage==0 && Now-radiancePerfAt>12)throw new InvalidOperationException("Native crimson rain workload did not appear.");
            // Dodge toward a gap in the admitted falling blades. Native input,
            // collision, resources and damage remain authoritative.
            float safest=pose.X,best=-1;
            for(float x=50;x<=72;x+=.5f) {
                float distance=8;
                foreach(var v in contact.Volumes.Where(v=>v.Active && (v.Traits&AttackTraits.Reflectable)!=0)) {
                    float lo=float.MaxValue,hi=float.MinValue,bottom=float.MaxValue;
                    for(int i=0;i<v.Points.Length;i+=2){lo=Math.Min(lo,v.Points[i]);hi=Math.Max(hi,v.Points[i]);bottom=Math.Min(bottom,v.Points[i+1]);}
                    if(bottom>pose.Y+8 || bottom<pose.Y-2)continue;
                    distance=Math.Min(distance,Math.Max(0,Math.Max(lo-x,x-hi)));
                }
                float score=distance-Math.Abs(x-pose.X)*.2f;if(score>best){best=score;safest=x;}
            }
            double cycle=(Now-radiancePerfAt)%.5;
            gameplay.FixturePairedControl(new Controls(Math.Abs(safest-pose.X)>.4f?Math.Sign(safest-pose.X):0,0,cycle<.08?Buttons.Parry:Buttons.None));
            if(radiancePerfStage==1 && Now-radiancePerfAt>=14) {
                if(radiancePerfBladeFrames<100)throw new InvalidOperationException("Insufficient actual crimson rain frames.");
                Log("RADIANCE_PERF_WINDOW_END workload=rain bladeFrames="+radiancePerfBladeFrames+" elapsed="+(Now-radiancePerfAt).ToString("F3",CultureInfo.InvariantCulture)+" frame="+Time.frameCount);
                menu!.FixturePixels(Path.Combine(fixtureFolder,"radiance-rain-workload.png"));
                gameplay.FixturePairedControl(default(Controls));radiancePerfStage=3;radianceVisualFrame=Time.frameCount+2;
            }
            return;
        }
        if(radiancePerfStage==0) {
            gameplay.FixturePairedControl(default(Controls));
            if(control.ActiveStateName=="Abyss Up" && !radiancePerfPlaced) {
                // Use the authored platform position reached by the native
                // climb transition; this is placement, never HP/phase writes.
                var top=control.FsmStates.Single(s=>s.Name=="Climb Plats1").Actions.OfType<HutongGames.PlayMaker.Actions.SendEventByName>()
                    .Select(a=>a.eventTarget.gameObject.GameObject.Value).Where(g=>g!=null)
                    .SelectMany(g=>g.GetComponentsInChildren<Collider2D>()).Where(c=>c.enabled && !c.isTrigger)
                    .OrderByDescending(c=>c.bounds.max.y).First();
                var p=top.bounds;
                File.WriteAllText(Path.Combine(fixtureFolder,"radiance-platform.request"),fixtureId+"\nGG_Radiance\n"+p.center.x.ToString("R",CultureInfo.InvariantCulture)+"\n"+p.max.y.ToString("R",CultureInfo.InvariantCulture));
                radiancePerfPlaced=true;
            }
            if(radiancePerfPlaced && pose.Y>30 && (control.ActiveStateName.StartsWith("A2",StringComparison.Ordinal) || control.ActiveStateName=="Arena 2 Idle")) {
                radiancePerfStage=1;radiancePerfAt=Now;Log("RADIANCE_PERF_WINDOW_BEGIN workload=platform-no-arrow y="+pose.Y+" control="+control.ActiveStateName.Replace(' ','_')+" frame="+Time.frameCount);
            } else if(Now-radiancePerfAt>25)throw new InvalidOperationException("Native platform phase did not become ready.");
            return;
        }
        double elapsed=Now-radiancePerfAt;
        if(radiancePerfStage==1 && elapsed>=8) {
            Log("RADIANCE_PERF_WINDOW_END workload=platform-no-arrow frame="+Time.frameCount);
            radiancePerfStage=2;radiancePerfAt=Now;elapsed=0;
            Log("RADIANCE_PERF_WINDOW_BEGIN workload=platform-homing-arrow frame="+Time.frameCount);
        }
        Buttons buttons=radiancePerfStage==2 && elapsed%2.4<.12?Buttons.Bow:Buttons.None;
        gameplay.FixturePairedControl(new Controls(0,0,buttons));
        if(radiancePerfStage==2 && elapsed>=14) {
            Log("RADIANCE_PERF_WINDOW_END workload=platform-homing-arrow nativeBowHits="+radiancePerfBowHits+" frame="+Time.frameCount);
            if(radiancePerfBowHits<1)throw new InvalidOperationException("Homing performance did not establish actual native arrow damage.");
            menu!.FixturePixels(Path.Combine(fixtureFolder,"radiance-platform-workload.png"));
            gameplay.FixturePairedControl(default(Controls));radiancePerfStage=3;radianceVisualFrame=Time.frameCount+2;
        }
    }
    private void UpdateRadianceIdle(YiGodhome.Bridge.Pose pose,HealthManager boss,PlayMakerFSM control,ContactFrame contact) {
        gameplay!.FixturePairedControl(default(Controls));
        if(radiancePerfStage==0 && control.ActiveStateName=="Rage Comb") {
            var blades=contact.Volumes.Where(v=>v.Active && (v.Traits&AttackTraits.Crimson)!=0 && (v.Traits&AttackTraits.Reflectable)!=0).ToArray();
            if(blades.Length<16)return;
            // Center comes from the native authored abyss position, not a safe
            // gap. The user explicitly authorized fixture-only invulnerability.
            radianceIdleX=control.FsmStates.Single(s=>s.Name=="Rage1 Start").Actions.OfType<HutongGames.PlayMaker.Actions.iTweenMoveTo>().Single().vectorPosition.Value.x;
            var ground=Physics2D.RaycastAll(new Vector2(radianceIdleX,pose.Y+3),Vector2.down,8,1<<8)
                .Where(h=>h.collider!=null && !h.collider.isTrigger && h.normal.y>.99f).OrderBy(h=>h.distance).FirstOrDefault();
            if(ground.collider==null)throw new InvalidOperationException("Native center has no valid ground for the idle fixture.");
            radianceIdleY=ground.point.y;
            File.WriteAllText(Path.Combine(fixtureFolder,"radiance-idle.request"),fixtureId+"\nGG_Radiance\n"+radianceIdleX.ToString("R",CultureInfo.InvariantCulture)+"\n"+radianceIdleY.ToString("R",CultureInfo.InvariantCulture));
            radiancePerfStage=1;radiancePerfAt=Now;return;
        }
        if(radiancePerfStage==1) {
            if(pose.Grounded && Math.Abs(pose.X-radianceIdleX)<.25f && Math.Abs(pose.Y-radianceIdleY)<.4f) {
                radiancePerfStage=2;radiancePerfAt=Now;radianceIdleHp=boss.hp;
                Log("RADIANCE_PERF_WINDOW_BEGIN workload=rain-idle-extended x="+pose.X+" y="+pose.Y+" controls=Neutral noAttacks=True fixtureInvulnerability=True frame="+Time.frameCount);
            } else if(Now-radiancePerfAt>8)throw new InvalidOperationException("Native center idle placement did not settle.");
            return;
        }
        if(radiancePerfStage==0 && Now-radiancePerfAt>12)throw new InvalidOperationException("Idle crimson rain workload did not begin.");
        if(radiancePerfStage!=2)return;
        if(contact.Volumes.Any(v=>v.Active && (v.Traits&AttackTraits.Crimson)!=0 && (v.Traits&AttackTraits.Reflectable)!=0))radiancePerfBladeFrames++;
        if(boss.hp!=radianceIdleHp || combatReflections!=0 || radiancePerfBowHits!=0)throw new InvalidOperationException("Idle rain sample was contaminated by outgoing damage.");
        if(Now-radiancePerfAt>=(fixtureScenario=="combat-RadianceRainVisual"?8:120)) {
            Log("RADIANCE_PERF_WINDOW_END workload=rain-idle-extended elapsed="+(Now-radiancePerfAt).ToString("F3",CultureInfo.InvariantCulture)+" bladeFrames="+radiancePerfBladeFrames+" bossHealthUnchanged=True controls=Neutral frame="+Time.frameCount);
            menu!.FixturePixels(Path.Combine(fixtureFolder,"radiance-idle-extended.png"));
            radiancePerfStage=3;radianceVisualFrame=Time.frameCount+2;
        }
    }
    private bool CombatFixtureDrained=>(!ContinuousEndurance || enduranceIndex+1==endurancePlan.Length) && CombatReturnDrained;
    private bool CombatReturnDrained=>combatTestStage==6 && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="GG_Workshop" && gameplay?.FixturePairedWorldReady==true && gameplay.PairOutcomesDrained;
#else
    private bool combatPlaced;
    private double endurancePlacementReadAt;
    private bool noskPlaced;
    private int noskCornerSamples;
    private double noskCornerAt;
    private bool gruzWaterSelected,radianceHomingSelected,radiancePlatformPlaced,radianceIdlePlaced;
    private RadianceIdleImmunityLease? radianceIdleImmunity;
    public sealed class RadianceIdleImmunityLease : MonoBehaviour {
        internal Health? Health;internal float Deadline;internal Guid Fixture;
        private void Update(){var host=Instance;if(Time.realtimeSinceStartup>=Deadline || host==null || host.fixtureId!=Fixture || !host.FixtureRadianceIdle || host.gameplay?.FixtureRoomReady("GG_Radiance")!=true)Release();}
        internal void Release(){if(Health!=null){Health.RemoveInvincible(gameObject);Health=null;Instance?.Log("NS_RADIANCE_IDLE_IMMUNITY_END nativeVoteRemoved=True");}Destroy(gameObject);}
        private void OnDestroy(){if(Health!=null){Health.RemoveInvincible(gameObject);Health=null;}}
    }
    private void UpdateCombatFixture()
    {
        if(FixtureRosterEndurance)UpdateRosterEnduranceImmunity();
        if(FixtureRadiancePerformance && fixtureScenario=="combat-RadianceArrowPerf" && !radianceHomingSelected && gameplay?.FixturePairedPlayer is Player arrowYi && NativeSaveContext.Enabled) {
            var weapon=arrowYi.weaponDataCollection.currentAcquiredList.OfType<PlayerWeaponData>().Single(w=>w.weaponPrefab.name.StartsWith("Chasing Arrow Shooter",StringComparison.Ordinal));
            arrowYi.weaponDataCollection.SetCurrent(weapon);radianceHomingSelected=true;
            Log("RADIANCE_PERF_NATIVE_HOMING_SELECTED prefab="+weapon.weaponPrefab.name+" resourceOverrides=False");
        }
        if(FixtureRadianceIdle && !radianceIdlePlaced && gameplay?.FixtureRoomReady("GG_Radiance")==true) {
            string idle=Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"HollowKnight","radiance-idle.request");
            if(File.Exists(idle)) {
                var d=File.ReadAllLines(idle);if(d.Length!=4 || d[0]!=fixtureId.ToString() || d[1]!="GG_Radiance")throw new InvalidDataException("Foreign idle placement request.");
                var idleYi=gameplay.FixturePairedPlayer??throw new InvalidOperationException("Idle immunity has no live Yi.");
                var owner=new GameObject("HollowSolsRadianceIdleFixture");radianceIdleImmunity=owner.AddComponent<RadianceIdleImmunityLease>();
                radianceIdleImmunity.Health=idleYi.health;radianceIdleImmunity.Deadline=Time.realtimeSinceStartup+135;radianceIdleImmunity.Fixture=fixtureId;
                idleYi.health.BecomeInvincible(owner);
                Log("NS_RADIANCE_IDLE_IMMUNITY_BEGIN explicitUserAuthorization=True nativeVote=True finiteSeconds=135 disposablePair=True");
                gameplay.FixturePlaceInRoom(d[1],float.Parse(d[2],CultureInfo.InvariantCulture),float.Parse(d[3],CultureInfo.InvariantCulture));radianceIdlePlaced=true;
            }
        }
        if(FixtureRadiancePerformance && !radiancePlatformPlaced && gameplay?.FixtureRoomReady("GG_Radiance")==true) {
            string place=Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"HollowKnight","radiance-platform.request");
            if(File.Exists(place)) {
                var d=File.ReadAllLines(place);if(d.Length!=4 || d[0]!=fixtureId.ToString() || d[1]!="GG_Radiance")throw new InvalidDataException("Foreign Radiance placement request.");
                gameplay.FixturePlaceInRoom(d[1],float.Parse(d[2],CultureInfo.InvariantCulture),float.Parse(d[3],CultureInfo.InvariantCulture));radiancePlatformPlaced=true;
            }
        }
        if(fixtureScenario=="combat-GruzMother" && !gruzWaterSelected && gameplay?.FixturePairedPlayer is Player yi && NativeSaveContext.Enabled) {
            var water=yi.mainAbilities.FooExplodeAutoStyle.AbilityData;
            if(!water.IsAcquired || water.IsTestForceEquip)throw new InvalidOperationException("Water fixture requires acquired native talisman without force equipment.");
            var flags=(System.Collections.Generic.Dictionary<string,GameFlagBase>)HarmonyLib.AccessTools.Field(typeof(GameFlagCollection),"flagDict").GetValue(SaveManager.Instance.allFlags);
            var members=HarmonyLib.AccessTools.Field(typeof(GameFlagBaseCollection<PlayerAbilityData>),"gameFlagDataList");
            var collection=flags.Values.OfType<PlayerAbilitySingleChoiceCollection>().Single(c=>((System.Collections.Generic.List<PlayerAbilityData>)members.GetValue(c)).Contains(water));
            collection.SetCurrent(water);gruzWaterSelected=true;
            Log("GRUZ_WATER_FIXTURE_NATIVE_SELECTION acquired=True resourceOverrides=False disposablePair=True");
        }
        if(fixtureScenario=="combat-Nosk" && !noskPlaced && gameplay?.FixtureRoomReady("GG_Nosk")==true) {
            string wall=Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"HollowKnight","nosk-wall.request");
            if(File.Exists(wall)) {
                var data=File.ReadAllLines(wall);
                if(data.Length!=4 || data[0]!=fixtureId.ToString() || data[1]!="GG_Nosk")throw new InvalidDataException("Foreign Nosk placement request.");
                gameplay.FixturePlaceInRoom("GG_Nosk",float.Parse(data[2],CultureInfo.InvariantCulture),float.Parse(data[3],CultureInfo.InvariantCulture));noskPlaced=true;noskCornerAt=Now+1.8;
            }
        }
        if(noskPlaced && noskCornerSamples<32 && Now>=noskCornerAt) {noskCornerSamples++;noskCornerAt=Now+.1;gameplay?.FixtureNoskCorner(Log);}
        if((combatPlaced && !ContinuousEndurance) || gameplay?.FixtureHallReady!=true)return;
        if(ContinuousEndurance){if(Now<endurancePlacementReadAt)return;endurancePlacementReadAt=Now+.25;}
        string request=Path.Combine(Path.GetDirectoryName(fixtureFolder)!,"HollowKnight",ContinuousEndurance?"combat-place-"+(combatPlaced?enduranceIndex+1:enduranceIndex)+".request":"combat-place.request");
        if(!File.Exists(request))return;
        var fields=File.ReadAllLines(request);
        if(fields.Length!=(ContinuousEndurance?5:4) || fields[0]!=fixtureId.ToString() || fields[1]!="GG_Workshop")throw new InvalidDataException("Foreign combat placement request.");
        if(ContinuousEndurance) {
            if(!int.TryParse(fields[4],out int next) || next<0 || next>=endurancePlan.Length || next<enduranceIndex || next>enduranceIndex+1)throw new InvalidDataException("Foreign endurance placement generation.");
            if(combatPlaced && next==enduranceIndex)return;
            enduranceIndex=next;cachedEnduranceProfile=null;
        }
        gameplay.FixturePlaceInHall(float.Parse(fields[2],CultureInfo.InvariantCulture),float.Parse(fields[3],CultureInfo.InvariantCulture));combatPlaced=true;
    }
#endif
}
