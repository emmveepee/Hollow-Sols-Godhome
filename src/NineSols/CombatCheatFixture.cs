using System;
using System.IO;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;
using YiGodhome.Geometry;
namespace YiGodhome;

// Explicit opt-in diagnostic only. These grants/placements are never normal play.
internal sealed class CombatCheatFixture : IDisposable
{
    private readonly bool enabled;
    private readonly bool invulnerable;
    private float radianceLandingUntil;
    private GameObject? immunityOwner;
    private Health? immunityHealth;
    private readonly bool kickProbe;
    private readonly float kickCutoff;
    private long kicked;
    private int nkgKickPlacements;
    private float nkgKickStarted = -1;
    private readonly System.Collections.Generic.HashSet<string> flameTrials = new();
    private readonly System.Collections.Generic.HashSet<long> descentTrials = new();
    private readonly System.Collections.Generic.HashSet<long> sheoBlueTrials = new();
    private float nextGrant, nextPlacement;
    private int nailmasterLandingSamples;
    private readonly Action<string> log;
    private readonly System.Collections.Generic.HashSet<string> projectileTrials = new System.Collections.Generic.HashSet<string>();
    private readonly System.Collections.Generic.Dictionary<long,int> soulOrbitTrials=new();
    private float soulProjectileDeadline=-1;
    public bool PrepareSoulProjectiles(ContactFrame frame,CoordinateMap map,Point floor) {
        if(!enabled || invulnerable || !SessionConfiguration.Current.Has("trace-probe")
            || SessionConfiguration.Current.Has("crimson-probe")
            || EncounterProfile.Selected.Family!=EncounterProfile.SoulMaster && EncounterProfile.Selected.Family!=EncounterProfile.SoulTyrant) return false;
        if(soulProjectileDeadline<0) {
            soulProjectileDeadline=Time.realtimeSinceStartup+90;
            log("NS_SOUL_PROJECTILE_PHASE seconds=90 nativeAttackSelection=True addedImmunity=False frame="+Time.frameCount);
        }
        if(Time.realtimeSinceStartup>=soulProjectileDeadline)return false;
        Update(frame,map,floor);
        return true;
    }
    private readonly System.Collections.Generic.HashSet<string> hiveMeleeTrials=new();
    private bool hiveCoreTrial;
    private readonly System.Collections.Generic.HashSet<string> minionParryTrials=new();
    private readonly System.Collections.Generic.HashSet<long> hornetRetirementTrials=new();
    private float projectileHoldUntil;
    private float sheoLastMelee;
    private string shieldChargeState = "";
    private int shieldChargeSamples;
    private readonly bool pureProbe;
    private readonly bool reflectionFinish;
    private bool finishing;
    private static Guid nkgDiagnosticEncounter;
    private static bool nkgDiagnosticRight;
    private Guid nkgPlacementEncounter;
    private bool nkgRightPlacement;
    private long placedTendril;
    private readonly System.Collections.Generic.HashSet<string> comboTrials = new System.Collections.Generic.HashSet<string>();
    private readonly System.Collections.Generic.HashSet<string> summonTrials = new System.Collections.Generic.HashSet<string>();
    private bool armorMovementHeld;
    private Guid uumuuGuideEncounter;
    private int uumuuGuideCore, uumuuGuideTrials;
    private float uumuuGuideStarted, uumuuGuidePlacement;
    private float uumuuGuideSide;
    public bool PrepareUumuu(ContactFrame frame, CoordinateMap map)
    {
        if (!enabled || !SessionConfiguration.Current.Has("trace-probe") || EncounterProfile.Selected.Family != EncounterProfile.Uumuu) return false;
        var boss = frame.Summons.FirstOrDefault(t => t.BossBar && t.Active && t.Health > 0 && t.PassiveRepel);
        if (boss == null) return false;
        var core = frame.Summons.FirstOrDefault(t => !t.BossBar && t.Active && t.Health > 0 && t.HideHealthBar);
        if (core == null) return false;
        if (uumuuGuideEncounter != frame.Encounter) { uumuuGuideEncounter=frame.Encounter; uumuuGuideCore=uumuuGuideTrials=0; }
        if (uumuuGuideCore != core.Entity) {
            if (uumuuGuideTrials >= 8) return false;
            uumuuGuideTrials++; uumuuGuideCore=core.Entity; uumuuGuideStarted=Time.time; uumuuGuidePlacement=0;
            uumuuGuideSide=core.X >= boss.X ? -1 : 1;
            log("NS_UUMUU_CORE_GUIDE_BEGIN encounter="+frame.Encounter+" core="+core.Entity+" boss="+boss.Entity+" side="+uumuuGuideSide+" nativeHoming=True bossStateForced=False healthWritten=False frame="+Time.frameCount);
        }
        if (Time.time-uumuuGuideStarted>6) return false;
        var player=Player.i;
        if (player==null || player.health.IsDead || !player.actions.Enabled || NativeMenu.Open || NativeInputBridge.HostPaused || player.freeze) return true;
        RenewGrant(player);
        if (player.CurrentStateType!=PlayerStateType.Normal || Time.time<uumuuGuidePlacement) return true;
        uumuuGuidePlacement=Time.time+.15f;
        // Move the diagnostic Yi to the opposite side of the live shell. The
        // authored core must fly into the native boss and break its own shield.
        var point=map.ToTarget(new Point(boss.X+uumuuGuideSide*(boss.Width*.5f+.55f),boss.Y-1.63f));
        var position=new Vector3(point.X,point.Y,player.transform.position.z);
        var ground=Physics2D.Raycast(position+Vector3.up*(4*map.Scale),Vector2.down,10*map.Scale,player.solid_layer.value);
        if(ground.collider!=null) position.y=Math.Max(position.y,ground.point.y-(player.myCollider.bounds.min.y-player.transform.position.y)+.05f*map.Scale);
        player.SetPosition(position,true);player.Velocity=Vector2.zero;
        return true;
    }
    private readonly System.Collections.Generic.HashSet<(Guid,long)> ringTrials=new();
    private float ringHoldUntil;
    private float ringTrialDeadline=-1;
    private readonly System.Collections.Generic.HashSet<(Guid,int,long)> swordTrials=new();
    private float swordHoldUntil;
    public bool PrepareXero(ContactFrame frame,CoordinateMap map,Point floor)
    {
        if(!enabled || invulnerable || EncounterProfile.Selected.Family!=EncounterProfile.Xero || !SessionConfiguration.Current.Has("trace-probe")) return false;
        var player=Player.i;
        if(player==null || player.health.IsDead || !player.actions.Enabled || NativeMenu.Open || player.freeze
            || TimePauseManager.GamePlayTimeScaleModifier.finalTimeScale==0) return true;
        RenewGrant(player);
        if(Time.time<swordHoldUntil) return true;
        if(swordTrials.Count(t=>t.Item1==frame.Encounter)>=8) return false;
        var volume=frame.Volumes.FirstOrDefault(v=>v.Active && v.Category==AttackCategory.Projectile
            && (v.Traits&AttackTraits.Reflectable)!=0 && !swordTrials.Contains((frame.Encounter,v.Id,v.Activation)));
        if(volume==null) {
            var waiting=map.ToTarget(new Point(frame.HurtX,floor.Y));
            player.SetPosition(new Vector3(waiting.X,waiting.Y,player.transform.position.z),true);
            player.Velocity=Vector2.zero;player.SetOnGround=true;player.ResetNeedToCheckGroundFlag();
            return true;
        }
        float x=0,y=0;for(int i=0;i<volume.Points.Length;i+=2) {x+=volume.Points[i];y+=volume.Points[i+1];}
        var centre=map.ToTarget(new Point(x/(volume.Points.Length/2),y/(volume.Points.Length/2)));
        player.SetPosition(new Vector3(centre.X,centre.Y-(player.myCollider.bounds.center.y-player.transform.position.y),player.transform.position.z),true);
        player.Velocity=Vector2.zero;player.SetOnGround=false;player.ResetNeedToCheckGroundFlag();
        player.ChangeState(PlayerStateType.Parry,false);player.inputCommandQueue.ForceWasPressAction(player.actions.Parry);
        swordTrials.Add((frame.Encounter,volume.Id,volume.Activation));swordHoldUntil=Time.time+.6f;
        log("NS_XERO_PARRY_FIXTURE encounter="+frame.Encounter+" collider="+volume.Id+" activation="+volume.Activation+" nativeParry=True frame="+Time.frameCount);
        return true;
    }
    public bool PrepareRings(ContactFrame frame,CoordinateMap map,Point floor)
    {
        if(!enabled || EncounterProfile.Selected!=EncounterProfile.ElderHu || !SessionConfiguration.Current.Has("trace-probe")) return false;
        var player=Player.i;
        if(player==null || player.health.IsDead || !player.actions.Enabled || NativeMenu.Open || player.freeze
            || TimePauseManager.GamePlayTimeScaleModifier.finalTimeScale==0) return true;
        RenewGrant(player);
        if(Time.time<ringHoldUntil) return true;
        if(ringTrials.Count>=8) return false;
        if(ringTrialDeadline<0) ringTrialDeadline=Time.time+12;
        bool airborne=ringTrials.Count%2==0;
        foreach(var volume in frame.Volumes) {
            if(!volume.Active || volume.Category!=AttackCategory.Melee || volume.ActionId<=0 || ringTrials.Contains((frame.Encounter,volume.ActionId))) continue;
            float low=float.PositiveInfinity,high=float.NegativeInfinity,x=0;
            for(int i=0;i<volume.Points.Length;i+=2) {x+=volume.Points[i];low=Math.Min(low,volume.Points[i+1]);high=Math.Max(high,volume.Points[i+1]);}
            if(volume.Points.Length<6 || airborne && low<floor.Y+2 || !airborne && (low>floor.Y+1.5f || high<floor.Y)) continue;
            x/=volume.Points.Length/2;
            var centre=map.ToTarget(new Point(x,(low+high)*.5f));
            var feet=airborne?new Vector3(centre.X,centre.Y-(player.myCollider.bounds.center.y-player.transform.position.y),player.transform.position.z)
                :new Vector3(centre.X,map.ToTarget(new Point(x,floor.Y)).Y,player.transform.position.z);
            player.SetPosition(feet,true);player.Velocity=Vector2.zero;player.SetOnGround=!airborne;player.ResetNeedToCheckGroundFlag();
            player.ChangeState(PlayerStateType.Parry,false);player.inputCommandQueue.ForceWasPressAction(player.actions.Parry);
            ringTrials.Add((frame.Encounter,volume.ActionId));ringHoldUntil=Time.time+.3f;ringTrialDeadline=Time.time+12;
            log("NS_RING_PARRY_FIXTURE encounter="+frame.Encounter+" action="+volume.ActionId+" collider="+volume.Id+" requestedAirborne="+airborne+" nativeState="+player.CurrentStateType+" nativeParry=True frame="+Time.frameCount);
            return true;
        }
        if(Time.time>ringTrialDeadline) throw new InvalidOperationException("Native ring parry fixture lacks the requested grounded/aerial shape.");
        return true;
    }
    public CombatCheatFixture(Action<string> log)
    {
        this.log = log;
        string root = Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
        enabled = YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures && YiGodhome.Bridge.SessionConfiguration.Current.Has("cheat-probe");
        invulnerable = enabled && SessionConfiguration.Current.Has("invulnerable-probe");
        kickProbe = enabled && YiGodhome.Bridge.SessionConfiguration.Current.Has("kick-probe");
        // The paired HK fixture expires twenty seconds before NS. Reserve an
        // additional twenty-five seconds for startup skew and native recovery.
        kickCutoff = int.TryParse(Environment.GetEnvironmentVariable("YIGODHOME_SMOKE_SECONDS"), out int seconds) && seconds > 45 ? seconds - 45 : float.PositiveInfinity;
        reflectionFinish = enabled && SessionConfiguration.Current.Has("reflection-finish-probe");
        pureProbe = enabled && SessionConfiguration.Current.Has("pv-tuning-probe");
    }
    public void MaintainInvulnerability(ContactFrame frame)
    {
        if (!invulnerable) return;
        if (frame.Difficulty != 2) throw new InvalidOperationException("Invulnerable fixture requires Radiant.");
        var player = Player.i;
        if (player == null || player.health.IsDead || immunityHealth == player.health) return;
        Dispose();
        immunityOwner = new GameObject("YiGodhomeRadiantVictoryFixture");
        immunityHealth = player.health;
        immunityHealth.BecomeInvincible(immunityOwner);
        log("NS_INVULNERABLE_VICTORY_BEGIN nativeVote=True tier=2 owner=" + immunityOwner.GetInstanceID());
    }
    public void Dispose()
    {
        if (immunityHealth != null && immunityOwner != null) {
            immunityHealth.RemoveInvincible(immunityOwner);
            log("NS_INVULNERABLE_VICTORY_END nativeVoteRemoved=True owner=" + immunityOwner.GetInstanceID());
        }
        immunityHealth = null;
        if (immunityOwner != null) UnityEngine.Object.Destroy(immunityOwner);
        immunityOwner = null;
    }
    public bool PreserveWallMovement()
    {
        if (!enabled || !NativeWallRunFixture.Running) return false;
        var player = Player.i;
        if (player != null && player.actions.Enabled && !player.health.IsDead && !player.freeze && !NativeMenu.Open
            && TimePauseManager.GamePlayTimeScaleModifier.finalTimeScale != 0) RenewGrant(player);
        return true;
    }
    public bool PreserveArmorMovement(ContactFrame frame)
    {
        bool hold = enabled && SessionConfiguration.Current.Has("stagger-probe")
            && EncounterProfile.Selected.ArmorAndHead && !frame.SecondaryTarget
            && frame.Summons.Any(body => body.BossBar && body.Health == 0)
            && frame.Summons.Any(body => !body.BossBar && body.Active && body.Health > 0);
        if (hold != armorMovementHeld) {
            armorMovementHeld = hold;
            log("NS_ARMOR_NATIVE_MOVEMENT_LEASE active=" + hold + " encounter=" + frame.Encounter
                + " placement=False addedImmunity=False frame=" + Time.frameCount);
        }
        if (!hold) return false;
        var player = Player.i;
        if (player != null && player.actions.Enabled && !player.health.IsDead && !player.freeze && !NativeMenu.Open
            && TimePauseManager.GamePlayTimeScaleModifier.finalTimeScale != 0) RenewGrant(player);
        return true;
    }
    public void Update(ContactFrame frame, CoordinateMap map, Point? floor = null)
    {
        var player = Player.i;
        // Leave time for the final native hold/recovery to finish before the
        // bounded process deadline; this changes only diagnostic placements.
        if (kickProbe && Time.realtimeSinceStartup >= kickCutoff) return;
        if (enabled && (frame.Difficulty == 2 && !kickProbe && !invulnerable || RadiantRevivalProbe.BlocksCheat)) return;
        // Refill is a bounded diagnostic lease, independent of target exposure.
        // A shielded boss or empty summon interval must not silently stop it.
        if (enabled && !NativeMenu.Open && player != null && !player.health.IsDead && player.actions.Enabled
            && !player.freeze && !NativeInputBridge.HostPaused && TimePauseManager.GamePlayTimeScaleModifier.finalTimeScale != 0)
            RenewGrant(player);
        if (!enabled || !frame.HurtActive && !pureProbe && !(kickProbe && frame.Kickable) || NativeMenu.Open || player == null || player.health.IsDead || !player.actions.Enabled) return;
        // Native hitstop evidence must not be contaminated by fixture teleports.
        if (player.freeze || TimePauseManager.GamePlayTimeScaleModifier.finalTimeScale == 0) return;
        if(EncounterProfile.Selected.Family==EncounterProfile.Sheo && SessionConfiguration.Current.Has("crimson-probe") && sheoBlueTrials.Count<4) {
            var blue=frame.Volumes.FirstOrDefault(v=>v.Active && v.NotifyParry && !sheoBlueTrials.Contains(v.ActionId));
            if(blue!=null && (player.CurrentStateType==PlayerStateType.Normal || player.CurrentStateType==PlayerStateType.Attack)) {
                sheoBlueTrials.Add(blue.ActionId);
                float bx=0,by=0;for(int i=0;i<blue.Points.Length;i+=2){bx+=blue.Points[i];by+=blue.Points[i+1];}
                var bp=map.ToTarget(new Point(bx/(blue.Points.Length/2),by/(blue.Points.Length/2)-.7f));
                player.SetPosition(new Vector3(bp.X,bp.Y,player.transform.position.z),true);player.Velocity=Vector2.zero;
                player.ChangeState(PlayerStateType.Parry,false);player.inputCommandQueue.ForceWasPressAction(player.actions.Parry);
                log("NS_SHEO_BLUE_PARRY_FIXTURE action="+blue.ActionId+" collider="+blue.Id+" nativeDefense=True verdictInjected=False frame="+Time.frameCount);
                nextPlacement=Time.realtimeSinceStartup+.3f;return;
            }
        }
        if (SessionConfiguration.Current.Has("trace-probe") && (EncounterProfile.Selected == EncounterProfile.NightmareGrimm || EncounterProfile.Selected == EncounterProfile.Grimm) && nkgPlacementEncounter != frame.Encounter) {
            if (nkgDiagnosticEncounter != frame.Encounter) { nkgDiagnosticEncounter = frame.Encounter; nkgDiagnosticRight = !nkgDiagnosticRight; }
            nkgPlacementEncounter = frame.Encounter; nkgRightPlacement = nkgDiagnosticRight;
            log("NS_NKG_FACING_FIXTURE encounter=" + frame.Encounter + " side=" + (nkgRightPlacement ? "right" : "left") + " nativeFlight=True facingOverride=False");
        }
        // Regular Grimm keeps a hittable balloon body. Do not teleport the
        // diagnostic player into its projectile origin while testing bats.
        if(EncounterProfile.Selected == EncounterProfile.Grimm && frame.Crimson) return;
        if (SessionConfiguration.Current.Has("homing-probe") && arrowTrial < 0 && player.CurrentStateType == PlayerStateType.Normal) {
            var weapon = player.weaponDataCollection.currentAcquiredList.OfType<PlayerWeaponData>().Single(w => w.weaponPrefab.name.StartsWith("Chasing Arrow Shooter", StringComparison.Ordinal));
            player.weaponDataCollection.SetCurrent(weapon); arrowTrial = 0;
            log("NS_HOMING_FIXTURE weapon=" + weapon.name + " prefab=" + weapon.weaponPrefab.name + " nativeEquip=True");
        }
        if (reflectionFinish && frame.Health <= 100 && !finishing) {
            finishing = true; projectileTrials.Clear();
            log("NS_REFLECTION_FINISH_BEGIN entity=" + frame.Entity + " health=" + frame.Health + " encounter=" + frame.Encounter);
        }
        if (Time.realtimeSinceStartup < projectileHoldUntil) return;
        if (pureProbe && frame.Kickable && frame.AttackId != kicked) {
            kicked = frame.AttackId;
            var kick = map.ToTarget(new Point(frame.HurtX, frame.HurtY + frame.HurtHeight * 0.5f + 0.2f));
            player.SetPosition(new Vector3(kick.X, kick.Y, player.transform.position.z), true);
            player.SetOnGround = false; player.ResetNeedToCheckGroundFlag();
            player.ChangeState(kicked % 2 == 1 ? PlayerStateType.JumpKick : PlayerStateType.Parry, false);
            if (kicked % 2 == 0) player.inputCommandQueue.ForceWasPressAction(player.actions.Parry);
            player.Velocity = new Vector2(0, -100);
            projectileHoldUntil = Time.realtimeSinceStartup + 0.5f;
            log("NS_PV_KICK_FIXTURE action=" + kicked + " route=" + (kicked % 2 == 1 ? "kick" : "aerial-parry")); return;
        }
        if (pureProbe && frame.Crimson && frame.AttackId != placedTendril) {
            var tendril = frame.Volumes.FirstOrDefault(v => v.Active && (v.Traits & AttackTraits.Crimson) != 0);
            if (tendril != null) {
                placedTendril = frame.AttackId;
                float x = 0, y = 0; for (int i = 0; i < tendril.Points.Length; i += 2) { x += tendril.Points[i]; y += tendril.Points[i+1]; }
                var p = map.ToTarget(new Point(x / (tendril.Points.Length / 2), y / (tendril.Points.Length / 2) - 0.8f));
                player.SetPosition(new Vector3(p.X, p.Y, player.transform.position.z), true); player.Velocity = Vector2.zero;
                projectileHoldUntil = Time.realtimeSinceStartup + 0.5f;
                log("NS_PV_TENDRIL_FIXTURE action=" + frame.AttackId + " nativeState=" + player.CurrentStateType); return;
            }
        }
        if (pureProbe) {
            var slash = frame.Volumes.FirstOrDefault(v => v.Active && (v.Traits & AttackTraits.LargeParryPush) != 0 && !comboTrials.Contains(v.Id + ":" + v.Activation));
            if (slash != null && comboTrials.Count < 3) {
                comboTrials.Add(slash.Id + ":" + slash.Activation);
                if (comboTrials.Count == 1) {
                    float x = 0, y = 0; for (int i = 0; i < slash.Points.Length; i += 2) { x += slash.Points[i]; y += slash.Points[i+1]; }
                    var p = map.ToTarget(new Point(x / (slash.Points.Length / 2), y / (slash.Points.Length / 2) - 0.8f));
                    player.SetPosition(new Vector3(p.X, p.Y, player.transform.position.z), true); player.Velocity = Vector2.zero;
                }
                player.ChangeState(PlayerStateType.Parry, false); player.inputCommandQueue.ForceWasPressAction(player.actions.Parry);
                projectileHoldUntil = Time.realtimeSinceStartup + 0.5f;
                log("NS_PV_COMBO_FIXTURE ordinal=" + comboTrials.Count + " id=" + slash.Id + " activation=" + slash.Activation + " relocated=" + (comboTrials.Count == 1)); return;
            }
            if (frame.Crimson || frame.Kickable) return;
        }
        if (frame.SecondaryTarget && !EncounterProfile.Selected.Roster && summonTrials.Count < 2 && summonTrials.Add(frame.Entity + ":" + frame.TargetGeneration))
        {
            var position = map.ToTarget(new Point(frame.HurtX, frame.HurtY - 0.7f));
            player.SetPosition(new Vector3(position.X, position.Y, player.transform.position.z), true);
            player.SetOnGround = false; player.ResetNeedToCheckGroundFlag(); player.Velocity = Vector2.zero;
            player.ChangeState(summonTrials.Count == 1 ? PlayerStateType.Parry : PlayerStateType.Normal, false);
            if (summonTrials.Count == 1) player.inputCommandQueue.ForceWasPressAction(player.actions.Parry);
            projectileHoldUntil = Time.realtimeSinceStartup + 0.6f;
            log("NS_SUMMON_DEFENSE_FIXTURE entity=" + frame.Entity + " route=" + (summonTrials.Count == 1 ? "parry" : "damage"));
            return;
        }
        bool orangeMinionProbe=SessionConfiguration.Current.Has("trace-probe") && (EncounterProfile.Selected.Family==EncounterProfile.BrokenVessel
            || EncounterProfile.Selected.Family==EncounterProfile.LostKin || EncounterProfile.Selected.Family==EncounterProfile.WingedNosk);
        bool collectorJarProbe=SessionConfiguration.Current.Has("trace-probe") && EncounterProfile.Selected.Family==EncounterProfile.Collector;
        if(!invulnerable && (orangeMinionProbe || collectorJarProbe) && minionParryTrials.Count<4) {
            var minion=frame.Volumes.FirstOrDefault(v=>v.Active && (orangeMinionProbe && v.Category==AttackCategory.Contact || collectorJarProbe && v.Category==AttackCategory.Melee) && v.NotifyParry
                && !minionParryTrials.Contains(v.Id+":"+v.Activation));
            if(minion!=null) {
                minionParryTrials.Add(minion.Id+":"+minion.Activation);
                float x=0,y=0;
                for(int i=0;i<minion.Points.Length;i+=2){x+=minion.Points[i];y+=minion.Points[i+1];}
                var minionPoint=map.ToTarget(new Point(x/(minion.Points.Length/2),y/(minion.Points.Length/2)-.8f));
                player.SetPosition(new Vector3(minionPoint.X,minionPoint.Y,player.transform.position.z),true);
                player.SetOnGround=false;player.ResetNeedToCheckGroundFlag();player.Velocity=Vector2.zero;
                player.ChangeState(PlayerStateType.Parry,false);player.inputCommandQueue.ForceWasPressAction(player.actions.Parry);
                projectileHoldUntil=Time.realtimeSinceStartup+.6f;
                log((collectorJarProbe?"NS_COLLECTOR_JAR_PARRY_FIXTURE":"NS_ORANGE_MINION_PARRY_FIXTURE")+" id="+minion.Id+" activation="+minion.Activation+" action="+minion.ActionId+" nativeParryInput=True frame="+Time.frameCount);
                return;
            }
        }
        bool hiveProjectileProbe=EncounterProfile.Selected.Family==EncounterProfile.HiveKnight && SessionConfiguration.Current.Has("trace-probe");
        bool sheoProjectileProbe=EncounterProfile.Selected.Family==EncounterProfile.Sheo && SessionConfiguration.Current.Has("trace-probe") && !SessionConfiguration.Current.Has("crimson-probe");
        bool soulProjectileProbe=(EncounterProfile.Selected.Family==EncounterProfile.SoulMaster || EncounterProfile.Selected.Family==EncounterProfile.SoulTyrant)
            && SessionConfiguration.Current.Has("trace-probe");
        if(sheoProjectileProbe && frame.Volumes.Any(v=>v.Active && v.Category==AttackCategory.Melee))sheoLastMelee=Time.realtimeSinceStartup;
        if(!kickProbe && EncounterProfile.Selected.Family==EncounterProfile.HornetSentinel && SessionConfiguration.Current.Has("trace-probe") && hornetRetirementTrials.Count<8) {
            var counter=frame.Volumes.FirstOrDefault(v=>v.Active && v.Category==AttackCategory.Melee && v.ActionId>0
                && (v.Traits&AttackTraits.RetireOnParry)!=0 && !hornetRetirementTrials.Contains(v.ActionId));
            if(counter!=null) {
                hornetRetirementTrials.Add(counter.ActionId);
                float counterX=0,counterY=0;
                for(int i=0;i<counter.Points.Length;i+=2){counterX+=counter.Points[i];counterY+=counter.Points[i+1];}
                var counterPoint=map.ToTarget(new Point(counterX/(counter.Points.Length/2),counterY/(counter.Points.Length/2)-.8f));
                player.SetPosition(new Vector3(counterPoint.X,counterPoint.Y,player.transform.position.z),true);
                player.SetOnGround=false;player.ResetNeedToCheckGroundFlag();player.Velocity=Vector2.zero;
                player.ChangeState(PlayerStateType.Parry,false);player.inputCommandQueue.ForceWasPressAction(player.actions.Parry);
                projectileHoldUntil=Time.realtimeSinceStartup+.6f;
                log("NS_HORNET_RETIRED_STRIKE_FIXTURE id="+counter.Id+" activation="+counter.Activation+" action="+counter.ActionId+" nativeParryInput=True frame="+Time.frameCount);
                return;
            }
        }
        if(hiveProjectileProbe && !kickProbe && !hiveCoreTrial) {
            var core=frame.Volumes.FirstOrDefault(v=>v.Active && v.Category==AttackCategory.Melee && v.ActionId==0 && v.DamageGroupId>0);
            if(core!=null) {
                hiveCoreTrial=true;
                float coreX=0,coreY=0;
                for(int i=0;i<core.Points.Length;i+=2){coreX+=core.Points[i];coreY+=core.Points[i+1];}
                var corePoint=map.ToTarget(new Point(coreX/(core.Points.Length/2),coreY/(core.Points.Length/2)-.8f));
                player.ChangeState(PlayerStateType.Normal,false);
                player.SetPosition(new Vector3(corePoint.X,corePoint.Y,player.transform.position.z),true);
                player.SetOnGround=false;player.ResetNeedToCheckGroundFlag();player.Velocity=Vector2.zero;
                projectileHoldUntil=Time.realtimeSinceStartup+1.2f;
                log("NS_HIVE_CORE_FIXTURE id="+core.Id+" activation="+core.Activation+" damageGroup="+core.DamageGroupId+" nativeCollision=True frame="+Time.frameCount);
                return;
            }
        }
        int projectileLimit=finishing?32:(EncounterProfile.Selected.ArmorAndHead && SessionConfiguration.Current.Has("trace-probe") || hiveProjectileProbe || soulProjectileProbe)?8:
            (EncounterProfile.Selected==EncounterProfile.NightmareGrimm || EncounterProfile.Selected==EncounterProfile.Grimm)?8:4;
        if (!invulnerable && !SessionConfiguration.Current.Has("pantheon-probe") && projectileTrials.Count < projectileLimit
            && (!sheoProjectileProbe || Time.realtimeSinceStartup-sheoLastMelee>=.2f && player.CurrentStateType==PlayerStateType.Normal))
        {
            var shot = frame.Volumes.FirstOrDefault(v => v.Active && (v.ConsumeOnContact || v.ParryCooldown > 0 && v.Category == AttackCategory.Projectile || EncounterProfile.Selected == EncounterProfile.PureVessel && v.Category == AttackCategory.Projectile)
                && (!sheoProjectileProbe || NativeHostContact.DiagnosticVolumeActive(v))
                // Installed Hive bees export circles; stingers export boxes.
                // Reserve four bounded trials for the later native bee swarm.
                && (!hiveProjectileProbe || (projectileTrials.Count<4 ? v.CircleRadius==0 && v.Points.Length<=8 : v.CircleRadius>0 || v.Points.Length>8))
                && (!soulProjectileProbe || (projectileTrials.Count<2 ? v.DamageGroupId==0 : v.DamageGroupId>0 && (!soulOrbitTrials.TryGetValue(v.DamageGroupId,out var tried) || tried<2)))
                && !projectileTrials.Contains(v.Id + ":" + v.Activation));
            if (shot != null)
            {
                projectileTrials.Add(shot.Id + ":" + shot.Activation);
                if(soulProjectileProbe && shot.DamageGroupId>0) {
                    soulOrbitTrials.TryGetValue(shot.DamageGroupId,out int tried);soulOrbitTrials[shot.DamageGroupId]=tried+1;
                    log("NS_SOUL_ORBIT_PARRY_FIXTURE group="+shot.DamageGroupId+" collider="+shot.Id+" activation="+shot.Activation+" nativeInput=True frame="+Time.frameCount);
                }
                float x = 0, y = 0;
                for (int i = 0; i < shot.Points.Length; i += 2) { x += shot.Points[i]; y += shot.Points[i + 1]; }
                float count = shot.Points.Length / 2;
                var shotPoint = map.ToTarget(new Point(x / count, y / count - 0.8f));
                player.SetPosition(new Vector3(shotPoint.X, shotPoint.Y, player.transform.position.z), true);
                player.SetOnGround = false; player.ResetNeedToCheckGroundFlag(); player.Velocity = Vector2.zero;
                bool parryProjectile=finishing || soulProjectileProbe && shot.DamageGroupId>0 || (hiveProjectileProbe ? projectileTrials.Count<=2 || projectileTrials.Count==5 || projectileTrials.Count==6
                    : EncounterProfile.Selected == EncounterProfile.Grimm ? projectileTrials.Count % 2 != 0
                    : projectileTrials.Count <= (EncounterProfile.Selected.ArmorAndHead && SessionConfiguration.Current.Has("trace-probe")?6:2));
                player.ChangeState(parryProjectile ? PlayerStateType.Parry : PlayerStateType.Normal, false);
                // Native parry validation checks its input command queue as well
                // as animation state. Exercise that input instead of a verdict.
                if (parryProjectile) player.inputCommandQueue.ForceWasPressAction(player.actions.Parry);
                projectileHoldUntil = Time.realtimeSinceStartup + 0.6f;
                log("NS_PROJECTILE_FIXTURE id=" + shot.Id + " activation=" + shot.Activation + " nativeState=" + player.CurrentStateType);
                return;
            }
        }
        if(hiveProjectileProbe && projectileTrials.Count>=projectileLimit && hiveMeleeTrials.Count<4) {
            var bee=frame.Volumes.FirstOrDefault(v=>v.Active && v.Category==AttackCategory.Projectile && v.ConsumeOnContact
                && (v.CircleRadius>0 || v.Points.Length>8) && !hiveMeleeTrials.Contains(v.Id+":"+v.Activation));
            if(bee!=null) {
                hiveMeleeTrials.Add(bee.Id+":"+bee.Activation);
                float x=bee.CircleX,y=bee.CircleY;
                if(bee.CircleRadius==0) {x=y=0;for(int i=0;i<bee.Points.Length;i+=2){x+=bee.Points[i];y+=bee.Points[i+1];}x/=bee.Points.Length/2;y/=bee.Points.Length/2;}
                float side=frame.HurtX>=x?-1:1;
                var hivePoint=map.ToTarget(new Point(x+side*1.2f,y-1.6f));
                player.SetPosition(new Vector3(hivePoint.X,hivePoint.Y,player.transform.position.z),true);
                player.SetOnGround=false;player.ResetNeedToCheckGroundFlag();player.Velocity=Vector2.zero;
                player.ChangeState(PlayerStateType.Normal,false);
                if(!NativeInputBridge.RequestHiveMeleePress(player)) throw new InvalidOperationException("Bounded Hive attack input lease unavailable.");
                projectileHoldUntil=Time.realtimeSinceStartup+.6f;
                log("NS_HIVE_MELEE_FIXTURE id="+bee.Id+" activation="+bee.Activation+" actualAttackInput=True side="+side+" frame="+Time.frameCount);
                return;
            }
        }
        if (finishing) return;
        if (EncounterProfile.Selected == EncounterProfile.NightmareGrimm && flameTrials.Count < 2 && !kickProbe) {
            var flame = frame.Volumes.FirstOrDefault(v => v.Active && ((v.Traits == AttackTraits.Crimson && (v.Traits & AttackTraits.LargeParryPush) == 0 && !flameTrials.Contains("pillar"))
                || v.Category == AttackCategory.Burn && !flameTrials.Contains("trail")));
            if (flame != null) {
                string kind = flame.Traits == AttackTraits.Crimson ? "pillar" : "trail"; flameTrials.Add(kind);
                float x=0,y=0;for(int i=0;i<flame.Points.Length;i+=2){x+=flame.Points[i];y+=flame.Points[i+1];}
                var p=map.ToTarget(new Point(x/(flame.Points.Length/2),y/(flame.Points.Length/2)-.7f));
                player.ChangeState(PlayerStateType.Normal,false);player.SetPosition(new Vector3(p.X,p.Y,player.transform.position.z),true);player.Velocity=Vector2.zero;
                projectileHoldUntil=Time.realtimeSinceStartup+.6f;
                log("NS_NKG_FLAME_FIXTURE kind="+kind+" collider="+flame.Id+" activation="+flame.Activation+" nativeCollision=True frame="+Time.frameCount);return;
            }
        }
        if (kickProbe && frame.BossTitle!=BossTitleVariant.NailmasterOro
            && (EncounterProfile.Selected != EncounterProfile.NightmareGrimm || nkgKickStarted < 0 || Time.realtimeSinceStartup - nkgKickStarted < 45))
        {
            if (frame.Kickable && frame.AttackId != kicked)
            {
                kicked = frame.AttackId;
                if (EncounterProfile.Selected == EncounterProfile.NightmareGrimm && nkgKickStarted < 0) nkgKickStarted = Time.realtimeSinceStartup;
                var kick = map.ToTarget(new Point(frame.HurtX, frame.HurtY + frame.HurtHeight * 0.5f + 0.2f));
                player.SetPosition(new Vector3(kick.X, kick.Y, player.transform.position.z), true);
                player.SetOnGround = false; player.ResetNeedToCheckGroundFlag();
                bool skull = EncounterProfile.Selected == EncounterProfile.NightmareGrimm ? nkgKickPlacements++ % 2 != 0 : kicked % 2 == 0;
                player.ChangeState(skull ? PlayerStateType.JumpKick : PlayerStateType.Parry, false);
                if (!skull) player.inputCommandQueue.ForceWasPressAction(player.actions.Parry);
                player.Velocity = new Vector2(0, -100);
                log("NS_KICK_FIXTURE attack=" + kicked + " nativeState=True requested=" + (skull ? "SkullKick" : "AerialParry"));
            }
            return;
        }
        // Narrow Sisters descent oracle through the same ordinary body volume.
        // Only bounded cheat-probe can place Yi; it never creates damage/verdicts.
        var descent = EncounterProfile.Selected == EncounterProfile.Sisters && descentTrials.Count < 3
            ? frame.Volumes.FirstOrDefault(v => v.Active && v.ActionId > 0 && v.Category == AttackCategory.Melee
                && v.Traits == AttackTraits.None && frame.HurtCollider == v.Id) : null;
        if (descent != null && descentTrials.Add(descent.ActionId)) {
            float x = 0, y = 0; for (int i = 0; i < descent.Points.Length; i += 2) { x += descent.Points[i]; y += descent.Points[i + 1]; }
            var center = map.ToTarget(new Point(x / (descent.Points.Length / 2), floor?.Y ?? y / (descent.Points.Length / 2) - .75f));
            player.ChangeState(PlayerStateType.Normal, false);
            player.SetPosition(new Vector3(center.X, center.Y, player.transform.position.z), true); player.Velocity = Vector2.zero;
            if(floor.HasValue) {player.SetOnGround=true;player.ResetNeedToCheckGroundFlag();}
            projectileHoldUntil = Time.realtimeSinceStartup + .4f;
            log("NS_SISTERS_DESCENT_FIXTURE encounter=" + frame.Encounter + " collider=" + descent.Id + " activation=" + descent.Activation + " action=" + descent.ActionId + " nativeCollision=True frame=" + Time.frameCount+" floorPlacement="+floor.HasValue+" hostFloor="+floor?.Y);
            return;
        }
        if (!frame.HurtActive) return;
        // Dedicated paint diagnostics start from the native arena entry point;
        // point-blank boss placement would retire a volley before it is tested.
        if(sheoProjectileProbe && projectileTrials.Count<2)return;
        if(soulProjectileProbe && soulProjectileDeadline>Time.realtimeSinceStartup)return;
        if (Time.realtimeSinceStartup < nextPlacement || (player.CurrentStateType != PlayerStateType.Normal
            && player.CurrentStateType != PlayerStateType.Attack && player.CurrentStateType != PlayerStateType.ShootArrow)) return;
        nextPlacement = Time.realtimeSinceStartup + 0.05f;
        // The bounded pause fixture needs an in-flight arrow, rather than a
        // projectile already consumed by a point-blank receiver.
        float gap = NativeInputBridge.DiagnosticBow ? 8 : 0.7f;
        var point = map.ToTarget(new Point(frame.HurtX + (nkgRightPlacement ? 1 : -1) * (frame.HurtWidth * 0.5f + gap), frame.HurtY - 0.7f));
        bool talismanTraversal = SessionConfiguration.Current.Has("talisman-traversal-probe");
        bool nailmasterVictory = invulnerable && EncounterProfile.Selected.Family == EncounterProfile.OroMato;
        if ((talismanTraversal || nailmasterVictory) && floor.HasValue) {
            var nativeFloor = map.ToTarget(floor.Value);
            point = new Point(point.X, nativeFloor.Y - (player.myCollider.bounds.min.y - player.transform.position.y));
        }
        if(invulnerable && EncounterProfile.Selected.Family==EncounterProfile.AbsoluteRadiance && Time.realtimeSinceStartup<radianceLandingUntil)return;
        if(invulnerable && EncounterProfile.Selected.Family==EncounterProfile.AbsoluteRadiance && !player.canAirAttack
            && player.CurrentStateType==PlayerStateType.Normal && NativeRecoveryAnchor.TryRadianceDiagnosticLanding(player,out var landing)) {
            player.SetPosition(new Vector3(landing.x,landing.y,player.transform.position.z),true);
            player.Velocity=Vector2.zero;player.ResetNeedToCheckGroundFlag();
            radianceLandingUntil=Time.realtimeSinceStartup+.75f;
            log("NS_RADIANCE_LANDING_FIXTURE nativeGround=True admissionWrite=False settleSeconds=.75 position="+landing+" frame="+Time.frameCount);
            return;
        }
        player.SetPosition(new Vector3(point.X, point.Y, player.transform.position.z), true);
        if (nailmasterVictory && floor.HasValue) {
            // Permit actual floor collision to restore native aerial attack admission.
            // Do not force grounded state or write canAirAttack/reset counters.
            if (player.CurrentStateType == PlayerStateType.Normal) player.Velocity = Vector2.zero;
            player.ResetNeedToCheckGroundFlag();
            if (nailmasterLandingSamples++ < 24)
                log("NS_NAILMASTER_LANDING_FIXTURE frame=" + Time.frameCount + " grounded=" + player.IsOnGroundCheck()
                    + " canAirAttack=" + player.canAirAttack + " state=" + player.CurrentStateType
                    + " velocity=" + player.Velocity.ToString("F4") + " floor=" + floor.Value.Y
                    + " forcedGround=False admissionWrite=False");
        }
        if (talismanTraversal) {
            player.Velocity = Vector2.zero; player.ResetNeedToCheckGroundFlag();
            log("NS_TALISMAN_GROUNDED_FIXTURE frame=" + Time.frameCount + " target=" + frame.Entity
                + " grounded=" + player.IsOnGroundCheck() + " position=" + player.transform.position.ToString("F4")
                + " midDashPlacement=False");
        }
        log("NS_CHEAT_PLACEMENT target=" + frame.Entity + " nativePosition=True");
    }
    private float shieldFixtureAt = -1;
    private bool shieldGrounded;
    private readonly System.Collections.Generic.HashSet<long> nkgCounterTrials = new();
    public bool PrepareCrimson(ContactFrame frame, CoordinateMap map, Point floor) {
        bool nkg=EncounterProfile.Selected==EncounterProfile.NightmareGrimm;
        bool defender=EncounterProfile.Selected==EncounterProfile.DungDefender || EncounterProfile.Selected==EncounterProfile.WhiteDefender || EncounterProfile.Selected==EncounterProfile.Sly || EncounterProfile.Selected==EncounterProfile.Galien || EncounterProfile.Selected==EncounterProfile.Marmu || EncounterProfile.Selected.Family==EncounterProfile.OroMato || EncounterProfile.Selected.Family==EncounterProfile.Sheo;
        if (!enabled || !nkg && !defender || !SessionConfiguration.Current.Has("crimson-probe")) return false;
        var player = Player.i;
        if (player == null || player.health.IsDead || player.freeze || NativeInputBridge.HostPaused) return true;
        RenewGrant(player);
        if (!frame.Crimson) return false;
        if (player.CurrentStateType != PlayerStateType.ParryCounterDefense && player.CurrentStateType != PlayerStateType.ParryCounterAttack) return true;
        var strike = frame.Volumes.FirstOrDefault(v => v.Active && (v.Traits & AttackTraits.Crimson) != 0 && !nkgCounterTrials.Contains(v.ActionId));
        if (strike == null) return true;
        nkgCounterTrials.Add(strike.ActionId);
        float x = 0, y = 0; for (int i = 0; i < strike.Points.Length; i += 2) { x += strike.Points[i]; y += strike.Points[i + 1]; }
        var p = map.ToTarget(new Point(x / (strike.Points.Length / 2), y / (strike.Points.Length / 2) - .7f));
        player.SetPosition(new Vector3(p.X, p.Y, player.transform.position.z), true); player.Velocity = Vector2.zero;
        log((nkg?"NS_NKG_COUNTER_FIXTURE":"NS_AUTHORED_COUNTER_FIXTURE")+" action=" + strike.ActionId + " collider=" + strike.Id + " nativeDefense=True balloon=" + ((strike.Traits & AttackTraits.LargeParryPush) != 0) + " frame=" + Time.frameCount);
        return true;
    }
    public bool PrepareSoul(ContactFrame frame, CoordinateMap map, Point floor)
    {
        if (!enabled || EncounterProfile.Selected != EncounterProfile.SoulMaster && EncounterProfile.Selected != EncounterProfile.SoulTyrant) return false;
        // Resume ordinary diagnostic combat between crimson attacks. A latched
        // first sighting must not suppress normal targeting for the whole fight.
        if (!frame.Crimson) { shieldGrounded=false; return false; }
        var player = Player.i;
        if (player == null || player.health.IsDead || NativeInputBridge.HostPaused || player.freeze) return true;
        RenewGrant(player);
        if (!shieldGrounded) {
            var ground = map.ToTarget(floor);
            float foot = player.myCollider.bounds.min.y - player.transform.position.y;
            player.SetPosition(new Vector3(ground.X, ground.Y - foot, player.transform.position.z), true);
            player.Velocity = Vector2.zero; player.ResetNeedToCheckGroundFlag(); shieldGrounded = true;
            log("NS_SOUL_COUNTER_FIXTURE grounded=True nativeInput=True");
        }
        if (frame.Crimson && !frame.Windup && (player.CurrentStateType == PlayerStateType.ParryCounterDefense || player.CurrentStateType == PlayerStateType.ParryCounterAttack)) {
            var strike = frame.Volumes.FirstOrDefault(v => v.Active && (v.Traits & AttackTraits.Crimson) != 0);
            if (strike != null) {
                float x = 0, y = 0;
                for (int i = 0; i < strike.Points.Length; i += 2) { x += strike.Points[i]; y += strike.Points[i + 1]; }
                var center = map.ToTarget(new Point(x / (strike.Points.Length / 2), y / (strike.Points.Length / 2) - .7f));
                player.SetPosition(new Vector3(center.X, center.Y, player.transform.position.z), true); player.Velocity = Vector2.zero;
            }
        }
        return true;
    }
    public bool PrepareShield(ContactFrame frame, CoordinateMap map, Point floor) {
        if (!enabled || !SessionConfiguration.Current.Has("markoth-shield-probe") && !SessionConfiguration.Current.Has("markoth-projectile-probe")) return false;
        var player=Player.i;if(player==null||player.health.IsDead||!player.actions.Enabled||player.freeze||NativeInputBridge.HostPaused)return true;
        RenewGrant(player);if(shieldFixtureAt<0)shieldFixtureAt=Time.time;
        if(SessionConfiguration.Current.Has("markoth-projectile-probe")) SelectArrow(player);
        var shield=frame.Volumes.FirstOrDefault(v=>v.BlocksProjectiles && (!frame.Crimson || (v.Traits & AttackTraits.Crimson)!=0));
        if(shield==null)return true;
        float x=0,y=0;for(int i=0;i<shield.Points.Length;i+=2){x+=shield.Points[i];y+=shield.Points[i+1];}x/=shield.Points.Length/2;y/=shield.Points.Length/2;
        bool counter=!SessionConfiguration.Current.Has("markoth-projectile-probe") && Time.time-shieldFixtureAt>=7;
        if (counter && shieldChargeSamples < 80) {
            var prepare = player.FindState(PlayerStateType.ParryCounter);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            bool prepared = (bool)typeof(PlayerCounterPrepareState).GetField("isPrepared", flags)!.GetValue(prepare);
            bool locked = (bool)typeof(PlayerCounterPrepareState).GetField("postLock", flags)!.GetValue(prepare);
            string sample = player.CurrentStateType + ":" + prepared + ":" + locked + ":" + frame.Crimson + ":" + frame.Windup;
            if (sample != shieldChargeState) {
                shieldChargeState = sample; shieldChargeSamples++;
                log("NS_SHIELD_COUNTER_INPUT state=" + player.CurrentStateType + " prepared=" + prepared + " postLock=" + locked
                    + " nativeParryPressed=" + player.actions.Parry.IsPressed + " crimson=" + frame.Crimson + " windup=" + frame.Windup + " frame=" + Time.frameCount);
            }
        }
        if(counter && !shieldGrounded) {
            var ground=map.ToTarget(floor);float foot=player.myCollider.bounds.min.y-player.transform.position.y;
            player.SetPosition(new Vector3(ground.X,ground.Y-foot,player.transform.position.z),true);player.Velocity=Vector2.zero;player.ResetNeedToCheckGroundFlag();shieldGrounded=true;
            log("NS_SHIELD_FIXTURE_GROUNDED nativeFloor=True frame="+Time.frameCount);
        }
        if(counter && (!frame.Crimson || frame.Windup || player.CurrentStateType!=PlayerStateType.ParryCounterDefense && player.CurrentStateType!=PlayerStateType.ParryCounterAttack)) return true;
        var p=map.ToTarget(new Point(x-(counter?0:2),y-.7f));
        var nozzle=NativeProjectileBlockers.DiagnosticOffset(player);
        if(!counter&&nozzle.HasValue){var center=map.ToTarget(new Point(x-2,y));p=new Point(center.X,center.Y-nozzle.Value);}
        player.SetPosition(new Vector3(p.X,p.Y,player.transform.position.z),true);player.Velocity=Vector2.zero;
        return true;
    }
    private int arrowTrial = -1;
    private PlayerWeaponData[]? arrowChoices;
    private void SelectArrow(Player player) {
        // Same native choice/equip contract as inventory; bounded fixture only,
        // under the early no-save-I/O guard. Never substitute projectile prefabs.
        if(arrowChoices==null) {
            arrowChoices=player.weaponDataCollection.currentAcquiredList.OfType<PlayerWeaponData>().ToArray();
            if(arrowChoices.Length==0||arrowChoices.Length>16)throw new InvalidOperationException("Native arrow trial roster out of bounds.");
            log("NS_ARROW_ROSTER count="+arrowChoices.Length+" names="+string.Join("|",arrowChoices.Select(w=>w.name)));
        }
        int trial=(int)((Time.time-shieldFixtureAt)/10);
        if(trial==arrowTrial||player.CurrentStateType!=PlayerStateType.Normal)return;
        var weapon=arrowChoices[(trial+1)%arrowChoices.Length];
        player.weaponDataCollection.SetCurrent(weapon);
        if(player.weaponDataCollection.currentWeapon!=weapon)throw new InvalidOperationException("Native arrow selection refused.");
        arrowTrial=trial;
        log("NS_ARROW_TRIAL ordinal="+trial+" weapon="+weapon.name+" prefab="+weapon.weaponPrefab.name+" nativeEquip=True nativeFrame="+Time.frameCount);
    }
    public bool PrepareMace(ContactFrame? frame, CoordinateMap map)
    {
        if (!enabled || !EncounterProfile.Selected.ArmorAndHead || frame?.Crimson != true) return false;
        var player = Player.i;
        if (player != null && player.actions.Enabled && !player.health.IsDead && !player.freeze && !NativeMenu.Open
            && TimePauseManager.GamePlayTimeScaleModifier.finalTimeScale != 0) RenewGrant(player);
        if (frame.Windup && frame.AttackId != placedTendril && player != null && !player.health.IsDead && !player.freeze) {
            placedTendril = frame.AttackId;
            var point = map.ToTarget(new Point(frame.X, frame.Y - 0.8f));
            player.SetPosition(new Vector3(point.X, point.Y, player.transform.position.z), true);
            log("NS_MACE_PLACEMENT_FIXTURE encounter=" + frame.Encounter + " action=" + frame.AttackId + " frame=" + Time.frameCount);
        }
        return true;
    }
    private void RenewGrant(Player player)
    {
        if (Time.realtimeSinceStartup < nextGrant) return;
        nextGrant = Time.realtimeSinceStartup + (SessionConfiguration.Current.Has("pantheon-probe") || EncounterProfile.Selected == EncounterProfile.Grimm ? .25f : 2);
        // Healing diagnostics must exercise injury windows on every boss.
        // RestoreEverything also grants native immunity and masks collisions.
        player.health.GainFull(); player.potion.GainFull();
        if (EncounterProfile.Selected == EncounterProfile.NightmareGrimm || EncounterProfile.Selected == EncounterProfile.Grimm) {
            log((EncounterProfile.Selected == EncounterProfile.Grimm ? "NS_GRIMM_GRANT" : "NS_NKG_GRANT") + " nativeHealth=True addedImmunity=False");
        }
        player.chiContainer.GainFull();
        log("NS_CHEAT_GRANT explicitDiagnostic=True health=" + player.health.currentValue + " qi=" + player.chiContainer.Value+" addedImmunity=False");
    }
}
