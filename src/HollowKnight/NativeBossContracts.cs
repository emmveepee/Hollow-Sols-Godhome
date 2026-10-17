using System;
using System.Linq;
using YiGodhome.Bridge;

namespace YiGodhome;

// Bounded, explicit trace-fixture evidence for an already-admitted boss only.
// No scene inventory, dependency discovery, save access or normal-session work.
internal static class NativeBossContracts
{
    private static readonly System.Collections.Generic.HashSet<int> weaponOwners=new();
    private static readonly System.Collections.Generic.Dictionary<int,HealthManager> bossOwners=new();
    private static readonly System.Collections.Generic.HashSet<int> pendingQueries=new();
    private static readonly System.Collections.Generic.List<int> completedQueries=new();
    private static readonly System.Collections.Generic.HashSet<int> xeroSources=new();
    private static readonly System.Collections.Generic.HashSet<string> globExamples=new();
    private static HutongGames.PlayMaker.Actions.FindChild[] huChildren=Array.Empty<HutongGames.PlayMaker.Actions.FindChild>();
    private static float huDeadline;
    private static int huFrame=-1;
    internal static void RefreshQueries(Action<string>? log) {
        if(!SessionConfiguration.Current.RunFixtures || !SessionConfiguration.Current.Has("trace-probe") || pendingQueries.Count==0)return;
        completedQueries.Clear();
        foreach(var id in pendingQueries) {
            if(!bossOwners.TryGetValue(id,out var health) || health==null){completedQueries.Add(id);continue;}
            if(!health.gameObject.activeInHierarchy)continue;
            var fsms=health.GetComponents<PlayMakerFSM>();
            if(fsms.Any(f=>f.enabled && string.IsNullOrEmpty(f.ActiveStateName)))continue;
            if(fsms.Where(f=>f.enabled).SelectMany(f=>f.FsmStates).SelectMany(s=>s.Actions).OfType<GetHero>()
                .Any(a=>!a.storeResult.IsNone && a.storeResult.Value==null))continue;
            log?.Invoke("HK_NATIVE_QUERY_REFRESH entity="+id+" owner="+health.name.Replace(' ','_')+" active=True initialized=True frame="+UnityEngine.Time.frameCount);
            foreach(var fsm in fsms) {
                log?.Invoke("HK_BOSS_FSM_CONTRACT entity="+id+" owner="+health.name.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" enabled="+fsm.enabled+" active=True state="+fsm.ActiveStateName.Replace(' ','_'));
                foreach(var state in fsm.FsmStates) {PositionContract(health.name,fsm,state,log);RadianceComparison(health,fsm,state,log);}
            }
            ChildQueries(health,log);
            completedQueries.Add(id);
        }
        foreach(var id in completedQueries)pendingQueries.Remove(id);
    }
    private static void ChildQueries(HealthManager health,Action<string>? log) {
        // Only descendants of the admitted receiver, excluding other health owners.
        // Query typed actions; never enumerate a scene or discover donor assets.
        var fsms=health.GetComponentsInChildren<PlayMakerFSM>(true)
            .Where(f=>f.gameObject!=health.gameObject && f.GetComponentInParent<HealthManager>()==health
                && f.FsmStates.Any(s=>s.Actions.Any(a=>a is HutongGames.PlayMaker.Actions.GetPosition
                    || a is HutongGames.PlayMaker.Actions.GetPosition2D || a is HutongGames.PlayMaker.Actions.GetDistance
                    || a is HutongGames.PlayMaker.Actions.CheckTargetDirection || a is HutongGames.PlayMaker.Actions.GetAngleToTarget2D
                    || a is HutongGames.PlayMaker.Actions.ChaseObjectV2)))
            .Take(33).ToArray();
        if(fsms.Length>32)throw new InvalidOperationException("Scoped native child query budget changed.");
        log?.Invoke("HK_NATIVE_CHILD_QUERY_SUMMARY entity="+health.GetInstanceID()+" owner="+health.name.Replace(' ','_')+" queryFsms="+fsms.Length+" receiverOwnedOnly=True");
        foreach(var fsm in fsms) {
            if(fsm.FsmStates.Length>64 || fsm.FsmStates.Any(s=>s.Actions.Length>32))
                throw new InvalidOperationException("Scoped native child query state/action budget changed.");
            log?.Invoke("HK_NATIVE_CHILD_QUERY_OWNER entity="+health.GetInstanceID()+" owner="+fsm.name.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')
                +" enabled="+fsm.enabled+" active="+fsm.gameObject.activeInHierarchy+" state="+fsm.ActiveStateName.Replace(' ','_'));
            foreach(var state in fsm.FsmStates)PositionContract(fsm.name,fsm,state,log);
        }
    }
    private static void PositionContract(string owner,PlayMakerFSM fsm,HutongGames.PlayMaker.FsmState state,Action<string>? log) {
        foreach(var hero in state.Actions.OfType<GetHero>())
            log?.Invoke("HK_NATIVE_HERO_REFERENCE owner="+owner.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" state="+state.Name.Replace(' ','_')+" variable="+hero.storeResult.Name.Replace(' ','_'));
        foreach(var query in state.Actions.OfType<HutongGames.PlayMaker.Actions.GetPosition>()) {
            var target=FSMUtility.GetSafe(query.gameObject,query);
            log?.Invoke("HK_NATIVE_POSITION_QUERY owner="+owner.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" state="+state.Name.Replace(' ','_')
                +" target="+(target?.name.Replace(' ','_')??"none")+" hero="+(target!=null && target==HeroController.instance?.gameObject)
                +" targetVariable="+(query.gameObject.GameObject?.Name??"").Replace(' ','_')+" ownerMode="+query.gameObject.OwnerOption
                +" space="+query.space+" everyFrame="+query.everyFrame+" vector="+(query.vector.IsNone?"none":query.vector.Name.Replace(' ','_'))
                +" x="+(query.x.IsNone?"none":query.x.Name.Replace(' ','_'))+" y="+(query.y.IsNone?"none":query.y.Name.Replace(' ','_')));
        }
        foreach(var query in state.Actions.OfType<HutongGames.PlayMaker.Actions.GetPosition2D>()) {
            var target=FSMUtility.GetSafe(query.gameObject,query);
            log?.Invoke("HK_NATIVE_POSITION_QUERY owner="+owner.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" state="+state.Name.Replace(' ','_')
                +" target="+(target?.name.Replace(' ','_')??"none")+" hero="+(target!=null && target==HeroController.instance?.gameObject)
                +" targetVariable="+(query.gameObject.GameObject?.Name??"").Replace(' ','_')+" ownerMode="+query.gameObject.OwnerOption
                +" space="+query.space+" everyFrame="+query.everyFrame+" vector="+(query.vector.IsNone?"none":query.vector.Name.Replace(' ','_'))
                +" x="+(query.x.IsNone?"none":query.x.Name.Replace(' ','_'))+" y="+(query.y.IsNone?"none":query.y.Name.Replace(' ','_'))+" dimensions=2");
        }
        foreach(var aim in state.Actions.OfType<HutongGames.PlayMaker.Actions.GetAngleToTarget2D>())
            log?.Invoke("HK_NATIVE_AIM_QUERY owner="+owner.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" state="+state.Name.Replace(' ','_')
                +" source="+(FSMUtility.GetSafe(aim.gameObject,aim)?.name.Replace(' ','_')??"none")+" sourceVariable="+(aim.gameObject.GameObject?.Name??"").Replace(' ','_')+" sourceMode="+aim.gameObject.OwnerOption
                +" target="+(aim.target.Value?.name.Replace(' ','_')??"none")+" targetVariable="+aim.target.Name.Replace(' ','_')
                +" hero="+(aim.target.Value!=null && aim.target.Value==HeroController.instance?.gameObject)+" offsetX="+aim.offsetX.Value+" offsetY="+aim.offsetY.Value
                +" angle="+aim.storeAngle.Name.Replace(' ','_')+" everyFrame="+aim.everyFrame);
        foreach(var subtraction in state.Actions.OfType<HutongGames.PlayMaker.Actions.Vector3Subtract>())
            log?.Invoke("HK_NATIVE_QUERY_VECTOR_SUBTRACT owner="+owner.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" state="+state.Name.Replace(' ','_')
                +" result="+subtraction.vector3Variable.Name.Replace(' ','_')+" subtract="+subtraction.subtractVector.Name.Replace(' ','_')+" everyFrame="+subtraction.everyFrame);
        foreach(var ray in state.Actions.OfType<HutongGames.PlayMaker.Actions.RayCast2d>())
            log?.Invoke("HK_NATIVE_QUERY_RAYCAST owner="+owner.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" state="+state.Name.Replace(' ','_')
                +" kind=2d fromObject="+(FSMUtility.GetSafe(ray.fromGameObject,ray)?.name.Replace(' ','_')??"none")+" fromVector="+ray.fromPosition.Name.Replace(' ','_')
                +" directionVector="+ray.direction.Name.Replace(' ','_')+" distanceVariable="+ray.distance.Name.Replace(' ','_')+" distance="+ray.distance.Value
                +" space="+ray.space+" hit="+(ray.hitEvent?.Name??"none")+" layers="+string.Join(",",ray.layerMask.Select(m=>m.Value)));
        foreach(var ray in state.Actions.OfType<HutongGames.PlayMaker.Actions.RayCast2dV2>())
            log?.Invoke("HK_NATIVE_QUERY_RAYCAST owner="+owner.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" state="+state.Name.Replace(' ','_')
                +" kind=2dV2 fromObject="+(FSMUtility.GetSafe(ray.fromGameObject,ray)?.name.Replace(' ','_')??"none")+" fromVector="+ray.fromPosition.Name.Replace(' ','_')
                +" directionVector="+ray.direction.Name.Replace(' ','_')+" distanceVariable="+ray.distance.Name.Replace(' ','_')+" distance="+ray.distance.Value
                +" space="+ray.space+" hit="+(ray.hitEvent?.Name??"none")+" layers="+string.Join(",",ray.layerMask.Select(m=>m.Value)));
        foreach(var ray in state.Actions.OfType<HutongGames.PlayMaker.Actions.Raycast>())
            log?.Invoke("HK_NATIVE_QUERY_RAYCAST owner="+owner.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" state="+state.Name.Replace(' ','_')
                +" kind=3d fromObject="+(FSMUtility.GetSafe(ray.fromGameObject,ray)?.name.Replace(' ','_')??"none")+" fromVector="+ray.fromPosition.Name.Replace(' ','_')
                +" directionVector="+ray.direction.Name.Replace(' ','_')+" distanceVariable="+ray.distance.Name.Replace(' ','_')+" distance="+ray.distance.Value
                +" space="+ray.space+" hit="+(ray.hitEvent?.Name??"none")+" layers="+string.Join(",",ray.layerMask.Select(m=>m.Value)));
        foreach(var direction in state.Actions.OfType<HutongGames.PlayMaker.Actions.CheckTargetDirection>())
            log?.Invoke("HK_NATIVE_DIRECTION_QUERY owner="+owner.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" state="+state.Name.Replace(' ','_')
                +" target="+(direction.target.Value?.name.Replace(' ','_')??"none")+" targetVariable="+direction.target.Name.Replace(' ','_')
                +" aboveEvent="+(direction.aboveEvent?.Name??"none")+" belowEvent="+(direction.belowEvent?.Name??"none")
                +" aboveVariable="+(direction.aboveBool.IsNone?"none":direction.aboveBool.Name.Replace(' ','_'))+" belowVariable="+(direction.belowBool.IsNone?"none":direction.belowBool.Name.Replace(' ','_')));
        foreach(var velocity in state.Actions.OfType<HutongGames.PlayMaker.Actions.SetVelocityAsAngle>())
            log?.Invoke("HK_NATIVE_ANGLED_VELOCITY owner="+owner.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" state="+state.Name.Replace(' ','_')
                +" target="+(FSMUtility.GetSafe(velocity.gameObject,velocity)?.name.Replace(' ','_')??"none")+" targetVariable="+(velocity.gameObject.GameObject?.Name??"").Replace(' ','_')
                +" angleVariable="+velocity.angle.Name.Replace(' ','_')+" speed="+velocity.speed.Value);
        foreach(var chase in state.Actions.OfType<HutongGames.PlayMaker.Actions.ChaseObjectV2>())
            log?.Invoke("HK_NATIVE_CHASE_QUERY owner="+owner.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" state="+state.Name.Replace(' ','_')
                +" target="+(chase.target.Value?.name.Replace(' ','_')??"none")+" targetVariable="+chase.target.Name.Replace(' ','_')+" hero="+(chase.target.Value!=null && chase.target.Value==HeroController.instance?.gameObject)
                +" offsetX="+chase.offsetX.Value+" offsetY="+chase.offsetY.Value+" speed="+chase.speedMax.Value);
        foreach(var distance in state.Actions.OfType<HutongGames.PlayMaker.Actions.GetDistance>())
            log?.Invoke("HK_NATIVE_DISTANCE_QUERY owner="+owner.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" state="+state.Name.Replace(' ','_')
                +" target="+(distance.target.Value?.name.Replace(' ','_')??"none")+" targetVariable="+distance.target.Name.Replace(' ','_')
                +" result="+distance.storeResult.Name.Replace(' ','_')+" axis=XYZ");
        foreach(var action in state.Actions.OfType<HutongGames.PlayMaker.Actions.FloatAdd>())
            log?.Invoke("HK_NATIVE_POSITION_OFFSET owner="+owner.Replace(' ','_')+" state="+state.Name.Replace(' ','_')+" variable="+action.floatVariable.Name.Replace(' ','_')+" operation=add operand="+action.add.Name.Replace(' ','_')+" value="+action.add.Value);
        foreach(var action in state.Actions.OfType<HutongGames.PlayMaker.Actions.FloatSubtract>())
            log?.Invoke("HK_NATIVE_POSITION_OFFSET owner="+owner.Replace(' ','_')+" state="+state.Name.Replace(' ','_')+" variable="+action.floatVariable.Name.Replace(' ','_')+" operation=subtract operand="+action.subtract.Name.Replace(' ','_')+" value="+action.subtract.Value);
        foreach(var comparison in state.Actions.OfType<HutongGames.PlayMaker.Actions.FloatCompare>())
            log?.Invoke("HK_NATIVE_POSITION_COMPARE owner="+owner.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" state="+state.Name.Replace(' ','_')
                +" left="+comparison.float1.Name.Replace(' ','_')+" right="+comparison.float2.Name.Replace(' ','_')+" value="+comparison.float2.Value+" everyFrame="+comparison.everyFrame
                +" below="+(comparison.lessThan?.Name??"none")+" above="+(comparison.greaterThan?.Name??"none")+" equal="+(comparison.equal?.Name??"none"));
    }
    internal static void CaptureWeapon(DamageHero damage,EncounterProfile profile,Action<string>? log)
    {
        if(!SessionConfiguration.Current.RunFixtures || !SessionConfiguration.Current.Has("trace-probe")) return;
        if(profile.Family==EncounterProfile.GreyPrinceZote && damage.gameObject.scene.name==profile.Scene) {
            var node=damage.transform;
            for(int depth=0;node!=null && depth<2;depth++,node=node.parent)
                if(node.name=="Zote Balloon" || node.name=="Zote Balloon (1)" || node.name=="Zote Balloon(Clone)" || node.name=="Zote Balloon (1)(Clone)") {
                    CaptureWeaponOwner(node.gameObject,"admitted_native_zote_spawn_exclusion",log);return;
                }
        }
        if((profile.Family==EncounterProfile.SoulMaster || profile.Family==EncounterProfile.SoulTyrant) && HostReflectedProjectile.SoulOrb(damage) is PlayMakerFSM orb) {
            SoulOrbImpactEvidence.Bind(orb,log);
            CaptureWeaponOwner(orb.gameObject,"admitted_native_soul_orb",log);return;
        }
        if(profile.Family==EncounterProfile.Gorb && damage.name.StartsWith("Shot Slug Spear",StringComparison.Ordinal)) {
            CaptureWeaponOwner(damage.gameObject,"admitted_native_gorb_spear",log);return;
        }
        if((profile.Family==EncounterProfile.CrystalGuardian || profile.Family==EncounterProfile.EnragedGuardian) && damage.name=="Beam" && damage.gameObject.scene.name==profile.Scene) {
            if(weaponOwners.Contains(damage.gameObject.GetInstanceID()) || weaponOwners.Count>=64)return;
            CaptureWeaponOwner(damage.gameObject,"admitted_native_beam_collider",log);
            var parent=damage.transform.parent;
            log?.Invoke("HK_GUARDIAN_COLLIDER_REFERENCE source="+damage.GetInstanceID()+" owner="+damage.gameObject.GetInstanceID()+" parent="+(parent?.name.Replace(' ','_')??"none")+" parentId="+(parent?.gameObject.GetInstanceID()??0));
            for(int depth=0;parent!=null && depth<3;depth++,parent=parent.parent) {
                if(parent.GetComponent<HealthManager>()!=null)break;
                if(parent.GetComponents<PlayMakerFSM>().Length==0)continue;
                CaptureWeaponOwner(parent.gameObject,"admitted_native_beam_parent",log);break;
            }
            return;
        }
        if(profile.Family==EncounterProfile.SoulWarrior && damage.gameObject.scene.name==profile.Scene
            && damage.name.StartsWith("Mage Balloon Spawner",StringComparison.Ordinal)) {
            CaptureWeaponOwner(damage.gameObject,damage.name,log);return;
        }
        if(profile.Family==EncounterProfile.Sheo && damage.name.StartsWith("Paint Shot ",StringComparison.Ordinal)
            && damage.gameObject.activeInHierarchy && (damage.gameObject.scene.name==profile.Scene || damage.gameObject.scene.name=="DontDestroyOnLoad")) {
            CaptureWeaponOwner(damage.gameObject,damage.name,log);return;
        }
        if(profile.Family==EncounterProfile.HiveKnight) {
            // Only already admitted, native scene-owned collision sources;
            // never discover additional scene or prefab objects.
            if(!damage.gameObject.activeInHierarchy || damage.gameObject.scene.name!=profile.Scene
                || damage.GetComponentInParent<HealthManager>()!=null) return;
            var owner=damage.transform;
            for(int depth=0;owner!=null && depth<3;depth++,owner=owner.parent) {
                if(owner.GetComponent<HealthManager>()!=null) return;
                if(owner.GetComponents<PlayMakerFSM>().Length==0) continue;
                CaptureWeaponOwner(owner.gameObject,damage.name,log);return;
            }
            return;
        }
        if(profile.Family==EncounterProfile.FalseKnight || profile.Family==EncounterProfile.FailedChampion) {
            if(damage.hazardType!=1 || !damage.gameObject.activeInHierarchy
                || !damage.name.StartsWith("Falling Barrel",StringComparison.Ordinal)) return;
            var control=damage.GetComponents<PlayMakerFSM>().SingleOrDefault(f=>f.FsmName=="Fall Barrel Control");
            if(control!=null) CaptureWeaponOwner(damage.gameObject,damage.name,log);
            return;
        }
        if(profile.Family==EncounterProfile.Xero) {
            if(damage.gameObject.scene.name!=profile.Scene || damage.hazardType!=1 || !damage.gameObject.activeInHierarchy
                || damage.GetComponent<HealthManager>()!=null || xeroSources.Count>=32 || xeroSources.Contains(damage.GetInstanceID())) return;
            var owner=damage.transform;
            for(int depth=0;owner!=null && depth<4;depth++,owner=owner.parent) {
                if(owner.GetComponent<HealthManager>()!=null) return;
                if(owner.GetComponents<PlayMakerFSM>().Length==0) continue;
                xeroSources.Add(damage.GetInstanceID());CaptureWeaponOwner(owner.gameObject,damage.name,log);return;
            }
            return;
        }
        if(profile.Family==EncounterProfile.ElderHu) {
            if(huChildren.Length==0 || huFrame==UnityEngine.Time.frameCount) return;
            huFrame=UnityEngine.Time.frameCount;
            if(huChildren.Any(f=>f.storeResult.Value==null)) {
                if(UnityEngine.Time.realtimeSinceStartup>huDeadline) throw new InvalidOperationException("Native Hu ring references did not initialize within diagnostic deadline.");
                return;
            }
            foreach(var find in huChildren) {
                var child=find.storeResult.Value;
                log?.Invoke("HK_HU_RING_REFERENCE index="+find.childName.Value+" ringId="+child.GetInstanceID()+" nativeResolved=True");
                CaptureWeaponOwner(child,"authored_FindChild",log);
            }
            huChildren=Array.Empty<HutongGames.PlayMaker.Actions.FindChild>();return;
        }
        if((profile.Family==EncounterProfile.Nosk || profile.Family==EncounterProfile.WingedNosk || profile.Family==EncounterProfile.GodTamer)
            && (damage.name=="Vomit Glob Nosk(Clone)" || damage.name=="Vomit Glob(Clone)")) {
            if(globExamples.Add(damage.name))CaptureWeaponOwner(damage.gameObject,"admitted_native_vomit_glob",log);
            return;
        }
        if(profile.Family!=EncounterProfile.Galien) return;
        var root=damage.transform;
        while(root!=null && root.name!="Galien Hammer" && root.name!="Galien Mini Hammer(Clone)") root=root.parent;
        if(root==null || root.gameObject.scene.name!=profile.Scene) return;
        CaptureWeaponOwner(root.gameObject,damage.name,log);
    }
    private static void CaptureWeaponOwner(UnityEngine.GameObject root,string source,Action<string>? log)
    {
        if(weaponOwners.Contains(root.GetInstanceID()) || weaponOwners.Count>=64) return;
        weaponOwners.Add(root.GetInstanceID());
        var fsms=root.GetComponents<PlayMakerFSM>();
        if(fsms.Length>4) throw new InvalidOperationException("Scoped native weapon FSM capacity exceeded.");
        log?.Invoke("HK_WEAPON_OWNER_CONTRACT owner="+root.name.Replace(' ','_')+" id="+root.GetInstanceID()+" source="+source.Replace(' ','_')+" frame="+UnityEngine.Time.frameCount);
        if(EncounterProfile.Selected.Family==EncounterProfile.Sheo)
            log?.Invoke("HK_SHEO_PROJECTILE_CONTRACT owner="+root.name.Replace(' ','_')+" bullet="+(root.GetComponent<EnemyBullet>()!=null)
                +" body="+(root.GetComponent<UnityEngine.Rigidbody2D>()!=null)+" recycle="+(root.GetComponent<AutoRecycleSelf>()!=null)
                +" gravity="+(root.GetComponent<UnityEngine.Rigidbody2D>()?.gravityScale??0));
        var damageSources=root.GetComponentsInChildren<DamageHero>(true);
        // Installed Hive glob: one native core and exactly eight stingers.
        bool hiveGlob=EncounterProfile.Selected.Family==EncounterProfile.HiveKnight
            && (root.name=="Hive Knight Glob" || root.name=="Hive Knight Glob (1)" || root.name=="Hive Knight Glob (2)");
        int damageBudget=hiveGlob?9:8;
        if(damageSources.Length>damageBudget) throw new InvalidOperationException("Scoped native weapon collider capacity exceeded.");
        foreach(var damage in damageSources) {
            var shapes=damage.GetComponents<UnityEngine.Collider2D>();
            if(shapes.Length>4) throw new InvalidOperationException("Scoped native weapon shape capacity exceeded.");
            foreach(var shape in shapes) {
                log?.Invoke("HK_WEAPON_SHAPE_CONTRACT owner="+root.name.Replace(' ','_')+" source="+damage.name.Replace(' ','_')
                    +" id="+shape.GetInstanceID()+" type="+shape.GetType().Name+" enabled="+shape.enabled+" active="+shape.gameObject.activeInHierarchy
                    +" width="+shape.bounds.size.x+" height="+shape.bounds.size.y+" localX="+shape.transform.localPosition.x+" localY="+shape.transform.localPosition.y);
                if(shape is UnityEngine.PolygonCollider2D polygon && polygon.pathCount==1 && polygon.GetTotalPointCount()<=64) {
                    var points=polygon.points;
                    log?.Invoke("HK_WEAPON_LOCAL_SHAPE owner="+root.name.Replace(' ','_')+" id="+shape.GetInstanceID()+" vertices="+points.Length+" offset="+polygon.offset
                        +" minX="+points.Min(p=>p.x)+" maxX="+points.Max(p=>p.x)+" minY="+points.Min(p=>p.y)+" maxY="+points.Max(p=>p.y)+" scale="+shape.transform.lossyScale);
                }
            }
        }
        foreach(var fsm in fsms) {
            log?.Invoke("HK_WEAPON_FSM_CONTRACT owner="+root.name.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" enabled="+fsm.enabled+" active="+fsm.gameObject.activeInHierarchy+" state="+fsm.ActiveStateName.Replace(' ','_'));
            if(fsm.FsmStates.Length>64) throw new InvalidOperationException("Scoped native weapon state capacity exceeded.");
            foreach(var state in fsm.FsmStates) {
                bool tyrantSpinner=EncounterProfile.Selected.Family==EncounterProfile.SoulTyrant && source=="native_spinner_event_reference" && fsm.FsmName=="Summon Orbs" && state.Name=="Spawn";
                int actionBudget=tyrantSpinner?36:32;
                if(state.Actions.Length>actionBudget || state.Transitions.Length>16) throw new InvalidOperationException("Scoped native weapon action capacity exceeded: "+root.name+"/"+fsm.FsmName+"/"+state.Name+" actions="+state.Actions.Length);
                if(tyrantSpinner)log?.Invoke("HK_SOUL_SPINNER_CAPACITY nativeActions="+state.Actions.Length+" poolEmissions="+state.Actions.OfType<HutongGames.PlayMaker.Actions.SpawnObjectFromGlobalPool>().Count()+" actionBudget="+actionBudget+" exactEventOwner=True");
                PositionContract(root.name,fsm,state,log);
                log?.Invoke("HK_WEAPON_STATE_CONTRACT owner="+root.name.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" state="+state.Name.Replace(' ','_')
                    +" transitions="+string.Join(";",state.Transitions.Select(t=>t.EventName.Replace(' ','_')+">"+t.ToState.Replace(' ','_')))
                    +" actions="+string.Join(";",state.Actions.Select(a=>a.GetType().Name)));
                foreach(var action in state.Actions) {
                    string type=action.GetType().Name;
                    if(type!="ActivateGameObject" && type!="ActivateGameObject0" && type!="SetCollider") continue;
                    var objectField=HarmonyLib.AccessTools.Field(action.GetType(),"gameObject");
                    var valueField=HarmonyLib.AccessTools.Field(action.GetType(),type=="SetCollider"?"active":"activate");
                    var authored=objectField?.GetValue(action) as HutongGames.PlayMaker.FsmOwnerDefault;
                    var target=authored==null?null:FSMUtility.GetSafe(authored,action);
                    var value=valueField?.GetValue(action) as HutongGames.PlayMaker.FsmBool;
                    log?.Invoke("HK_WEAPON_OBJECT_CONTRACT owner="+root.name.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" state="+state.Name.Replace(' ','_')
                        +" action="+type+" target="+(target?.name.Replace(' ','_')??"none")+" active="+(value?.Value.ToString()??"unknown"));
                }
            }
        }
    }
    private static void RadianceComparison(HealthManager health,PlayMakerFSM fsm,HutongGames.PlayMaker.FsmState state,Action<string>? log) {
        if(EncounterProfile.Selected.Family!=EncounterProfile.AbsoluteRadiance)return;
        foreach(var compare in state.Actions.OfType<HutongGames.PlayMaker.Actions.IntCompare>())
            log?.Invoke("HK_RADIANCE_HP_CONTRACT fsm="+fsm.FsmName.Replace(' ','_')+" state="+state.Name.Replace(' ','_')
                +" left="+compare.integer1.Name+" leftValue="+compare.integer1.Value+" right="+compare.integer2.Name+" rightValue="+compare.integer2.Value
                +" below="+compare.lessThan?.Name+" equal="+compare.equal?.Name+" above="+compare.greaterThan?.Name);
        if(state.Name=="Death Ready")foreach(var trigger in state.Actions.OfType<HutongGames.PlayMaker.Actions.Trigger2dEvent>())
            log?.Invoke("HK_RADIANCE_FINAL_CONTRACT event="+trigger.sendEvent?.Name+" tag="+trigger.collideTag.Value+" layer="+trigger.collideLayer.Value);
    }
    internal static void Capture(HealthManager health, Action<string>? log) {
        if(!SessionConfiguration.Current.RunFixtures || !SessionConfiguration.Current.Has("trace-probe")) return;
        int id=health.GetInstanceID();
        if(bossOwners.TryGetValue(id,out var previous) && previous==health) return;
        foreach(var old in bossOwners.Where(p=>p.Value==null).Select(p=>p.Key).ToArray()) {bossOwners.Remove(old);pendingQueries.Remove(old);}
        if(bossOwners.Count>=32) throw new InvalidOperationException("Scoped boss owner evidence capacity exceeded.");
        bossOwners[id]=health;
        var followers=health.GetComponentsInChildren<PlayMakerFSM>(true).Where(f=>f.FsmName=="follow_hero" && f.gameObject!=health.gameObject && f.GetComponentInParent<HealthManager>()==health).ToArray();
        if(followers.Length>16)throw new InvalidOperationException("Scoped native hero-follow range budget changed.");
        foreach(var follower in followers)CaptureWeaponOwner(follower.gameObject,"native_hero_follow_query_only",log);
        if(EncounterProfile.Selected.Family==EncounterProfile.SoulWarrior && health.name=="Mage Knight") {
            foreach(var name in new[]{"Dash Range L","Dash Range R","Stomp Range","No Shoot Range","Evade Range","Evade Check","Alert Range"}) {
                var range=health.transform.Find(name);
                if(range==null)throw new InvalidOperationException("Pinned warrior range owner missing: "+name);
                CaptureWeaponOwner(range.gameObject,"native_AI_range_only",log);
                foreach(var shape in range.GetComponents<UnityEngine.Collider2D>()) log?.Invoke("HK_WARRIOR_RANGE_CONTRACT name="+name.Replace(' ','_')+" shape="+shape.GetType().Name+" trigger="+shape.isTrigger+" bounds="+shape.bounds+" nativeAIOnly=True");
            }
        }
        var fsms=health.GetComponents<PlayMakerFSM>();
        ChildQueries(health,log);
        if(fsms.Length>12) throw new InvalidOperationException("Scoped boss FSM evidence capacity exceeded.");
        if(!health.gameObject.activeInHierarchy || fsms.Any(f=>f.enabled && string.IsNullOrEmpty(f.ActiveStateName))
            || fsms.Where(f=>f.enabled).SelectMany(f=>f.FsmStates).SelectMany(s=>s.Actions).OfType<GetHero>()
                .Any(a=>!a.storeResult.IsNone && a.storeResult.Value==null))pendingQueries.Add(id);
        foreach(var fsm in fsms) {
            log?.Invoke("HK_BOSS_FSM_CONTRACT entity="+id+" owner="+health.name.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" enabled="+fsm.enabled+" active="+fsm.gameObject.activeInHierarchy+" state="+fsm.ActiveStateName.Replace(' ','_'));
            if(health.name=="Ghost Warrior Hu" && fsm.FsmName=="Attacking") {
                var init=fsm.FsmStates.Single(s=>s.Name=="Init");
                var children=init.Actions.OfType<HutongGames.PlayMaker.Actions.FindChild>().ToArray();
                if(children.Length!=17) throw new InvalidOperationException("Native Hu authored ring reference contract changed.");
                foreach(var find in children) {
                    if(!int.TryParse(find.childName.Value,out int index) || index<1 || index>17) throw new InvalidOperationException("Unauthored Hu ring reference.");
                }
                huChildren=children;huDeadline=UnityEngine.Time.realtimeSinceStartup+15;huFrame=-1;
                log?.Invoke("HK_HU_RING_REFERENCE_PENDING entity="+health.GetInstanceID()+" references=17 nativeInit=True boundedSeconds=15");
            }

            if(fsm.FsmStates.Length>160) throw new InvalidOperationException("Scoped boss state evidence capacity exceeded.");
            foreach(var state in fsm.FsmStates) {
                int actionBudget=EncounterProfile.Selected.Family==EncounterProfile.AbsoluteRadiance?256:64;
                if(state.Actions.Length>actionBudget || state.Transitions.Length>32) throw new InvalidOperationException("Scoped boss action evidence capacity exceeded: "+fsm.FsmName+"/"+state.Name+" actions="+state.Actions.Length);
                PositionContract(health.name,fsm,state,log);
                if((EncounterProfile.Selected.Family==EncounterProfile.HornetProtector || EncounterProfile.Selected.Family==EncounterProfile.HornetSentinel) && state.Name=="Fire")
                    foreach(var activation in state.Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>()) {
                        var target=FSMUtility.GetSafe(activation.gameObject,activation);
                        if(activation.activate.Value && target!=null && target.name=="Hit ADash" && target.GetComponentInParent<HealthManager>()==health)
                            CaptureWeaponOwner(target,"native_hornet_air_dash_reference",log);
                    }
                if((EncounterProfile.Selected.Family==EncounterProfile.SoulMaster || EncounterProfile.Selected.Family==EncounterProfile.SoulTyrant) && state.Name=="HS Orb") {
                    foreach(var emission in state.Actions.OfType<HutongGames.PlayMaker.Actions.SendEventByName>().Where(a=>a.sendEvent.Value=="SPINNER SUMMON")) {
                        var target=FSMUtility.GetSafe(emission.eventTarget.gameObject,emission);
                        log?.Invoke("HK_SOUL_SPINNER_REFERENCE entity="+id+" target="+(target?.name.Replace(' ','_')??"none")+" targetId="+(target?.GetInstanceID()??0)
                            +" targetVariable="+(emission.eventTarget.gameObject.GameObject?.Name??"").Replace(' ','_')+" sendToChildren="+emission.eventTarget.sendToChildren.Value);
                        if(target!=null && target!=health.gameObject)CaptureWeaponOwner(target,"native_spinner_event_reference",log);
                    }
                }
                if((EncounterProfile.Selected.Family==EncounterProfile.SoulMaster || EncounterProfile.Selected.Family==EncounterProfile.SoulTyrant)
                    && health.name.EndsWith("Phase2",StringComparison.Ordinal) && (state.Name=="Shoot?" || state.Name=="Shot Antic")) {
                    foreach(var addition in state.Actions.OfType<HutongGames.PlayMaker.Actions.IntAdd>())
                        log?.Invoke("HK_SOUL_ATTACK_COUNT_CONTRACT entity="+id+" state="+state.Name.Replace(' ','_')+" operation=add variable="+addition.intVariable.Name.Replace(' ','_')+" value="+addition.add.Value);
                    foreach(var reset in state.Actions.OfType<HutongGames.PlayMaker.Actions.SetIntValue>())
                        log?.Invoke("HK_SOUL_ATTACK_COUNT_CONTRACT entity="+id+" state="+state.Name.Replace(' ','_')+" operation=set variable="+reset.intVariable.Name.Replace(' ','_')+" value="+reset.intValue.Value);
                }
                if((EncounterProfile.Selected.Family==EncounterProfile.CrystalGuardian || EncounterProfile.Selected.Family==EncounterProfile.EnragedGuardian) && fsm.FsmName=="Beam Miner") {
                    foreach(var emission in state.Actions.OfType<HutongGames.PlayMaker.Actions.SendEventByName>().Where(a=>a.sendEvent.Value=="UP" || a.sendEvent.Value=="DOWN")) {
                        var target=FSMUtility.GetSafe(emission.eventTarget.gameObject,emission);
                        log?.Invoke("HK_GUARDIAN_BEAM_REFERENCE entity="+id+" state="+state.Name.Replace(' ','_')+" event="+emission.sendEvent.Value
                            +" variable="+(emission.eventTarget.gameObject.GameObject?.Name??"").Replace(' ','_')+" target="+(target?.name.Replace(' ','_')??"none")+" targetId="+(target?.GetInstanceID()??0));
                        if(target!=null && target!=health.gameObject)CaptureWeaponOwner(target,"native_beam_event_reference",log);
                    }
                    foreach(var activation in state.Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>()) {
                        var target=FSMUtility.GetSafe(activation.gameObject,activation);
                        log?.Invoke("HK_GUARDIAN_OBJECT_REFERENCE entity="+id+" state="+state.Name.Replace(' ','_')+" active="+activation.activate.Value
                            +" variable="+(activation.gameObject.GameObject?.Name??"").Replace(' ','_')+" target="+(target?.name.Replace(' ','_')??"none"));
                        if(target!=null && activation.gameObject.GameObject?.Name=="Beam")CaptureWeaponOwner(target,"native_beam_activation_reference",log);
                    }
                }
                log?.Invoke("HK_BOSS_STATE_CONTRACT boss="+health.name.Replace(' ','_')+" fsm="+fsm.FsmName.Replace(' ','_')+" state="+state.Name.Replace(' ','_')
                    +" transitions="+string.Join(";",state.Transitions.Select(t=>t.EventName.Replace(' ','_')+">"+t.ToState.Replace(' ','_')))
                    +" actions="+string.Join(";",state.Actions.Select(a=>a.GetType().Name)));
                if((EncounterProfile.Selected.Family==EncounterProfile.SoulMaster || EncounterProfile.Selected.Family==EncounterProfile.SoulTyrant)
                    && (state.Name=="Quake Land" || state.Name=="Quake Waves"))
                    foreach(var action in state.Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>()) {
                        var obj=FSMUtility.GetSafe(action.gameObject,action);
                        log?.Invoke("HK_SOUL_LANDING_OBJECT_CONTRACT entity="+id+" state="+state.Name.Replace(' ','_')+" variable="+(action.gameObject.GameObject?.Name??"").Replace(' ','_')
                            +" owner="+(obj?.name.Replace(' ','_')??"none")+" ownerId="+(obj?.GetInstanceID()??0)+" parent="+(obj?.transform.parent?.name.Replace(' ','_')??"none")+" active="+action.activate.Value
                            +" dealers="+(obj==null?"none":string.Join(";",obj.GetComponentsInChildren<DamageHero>(true).Select(d=>d.name.Replace(' ','_')+":"+d.GetInstanceID()))));
                    }
                if(health.name=="Sheo Boss" && fsm.FsmName=="nailmaster_sheo") {
                    foreach(var action in state.Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>()) {
                        var obj=FSMUtility.GetSafe(action.gameObject,action);
                        log?.Invoke("HK_SHEO_OBJECT_CONTRACT state="+state.Name.Replace(' ','_')+" variable="+(action.gameObject.GameObject?.Name??"").Replace(' ','_')
                            +" owner="+(obj?.name.Replace(' ','_')??"none")+" ownerId="+(obj?.GetInstanceID()??0)+" active="+action.activate.Value);
                    }
                    foreach(var action in state.Actions.OfType<HutongGames.PlayMaker.Actions.SpawnObjectFromGlobalPool>())
                        log?.Invoke("HK_SHEO_POOL_CONTRACT state="+state.Name.Replace(' ','_')+" type=Spawn prefab="+action.gameObject.Value?.name.Replace(' ','_'));
                    foreach(var action in state.Actions.OfType<HutongGames.PlayMaker.Actions.FlingObjectsFromGlobalPool>())
                        log?.Invoke("HK_SHEO_POOL_CONTRACT state="+state.Name.Replace(' ','_')+" type=Fling prefab="+action.gameObject.Value?.name.Replace(' ','_'));
                }
                if((health.name=="Oro" || health.name=="Mato") && fsm.FsmName=="nailmaster"
                    && new[]{"Init","P2 HP Adjust","Entry Wait","Activate","Death Type","Call Mato","Choice","Entry Roar","Roar End 2",
                        "2 Slash","2 Stomp","Do Combo","Do Combo 2","Do Stomp","Do Stomp 2","Cross Combo","Cross Combo 2","Cross Stomp","Cross Stomp 2",
                        "Stomp Slash","NA Charge","Dash Forward","Cyclone End","D Slash End","Combo Slash1","Combo Slash2","Combo Slash3","Combo Slash4","Dstab Land"}.Contains(state.Name)) {
                    string prefix="HK_NAILMASTER_NATIVE_CONTRACT boss="+health.name+" state="+state.Name.Replace(' ','_');
                    foreach(var action in state.Actions.OfType<HutongGames.PlayMaker.Actions.SetBoolValue>())
                        log?.Invoke(prefix+" type=SetBool variable="+action.boolVariable.Name.Replace(' ','_')+" value="+action.boolValue.Value);
                    foreach(var action in state.Actions.OfType<HutongGames.PlayMaker.Actions.BoolTest>())
                        log?.Invoke(prefix+" type=BoolTest variable="+action.boolVariable.Name.Replace(' ','_')+" value="+action.boolVariable.Value);
                    foreach(var action in state.Actions.OfType<HutongGames.PlayMaker.Actions.FindChild>())
                        log?.Invoke(prefix+" type=FindChild child="+action.childName.Value.Replace(' ','_')+" variable="+action.storeResult.Name.Replace(' ','_'));
                    foreach(var action in state.Actions.OfType<HutongGames.PlayMaker.Actions.SendEventByName>()) {
                        var target=action.eventTarget;
                        var obj=target.gameObject==null?null:FSMUtility.GetSafe(target.gameObject,action);
                        log?.Invoke(prefix+" type=SendEvent event="+action.sendEvent.Value.Replace(' ','_')+" targetKind="+target.target
                            +" variable="+(target.gameObject?.GameObject?.Name??"")+" owner="+(obj?.name.Replace(' ','_')??"none")
                            +" ownerId="+(obj?.GetInstanceID()??0)+" fsm="+(target.fsmName?.Value??""));
                    }
                    foreach(var action in state.Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>()) {
                        var obj=FSMUtility.GetSafe(action.gameObject,action);
                        log?.Invoke(prefix+" type=Activate variable="+(action.gameObject.GameObject?.Name??"")+" owner="+(obj?.name.Replace(' ','_')??"none")
                            +" ownerId="+(obj?.GetInstanceID()??0)+" active="+action.activate.Value);
                    }
                }
                if(health.name=="Sly Boss" && (state.Name=="Stun Reset" || state.Name=="Stun Recover" || state.Name=="Begin Rage" || state.Name=="Death Type")) {
                    foreach(var action in state.Actions.OfType<HutongGames.PlayMaker.Actions.SetBoolValue>())
                        log?.Invoke("HK_SLY_NATIVE_BOOL_CONTRACT state="+state.Name.Replace(' ','_')+" variable="+action.boolVariable.Name+" value="+action.boolValue.Value);
                    foreach(var action in state.Actions.OfType<HutongGames.PlayMaker.Actions.BoolTest>())
                        log?.Invoke("HK_SLY_NATIVE_BOOL_CONTRACT state="+state.Name.Replace(' ','_')+" variable="+action.boolVariable.Name+" readOnly=True");
                }
                if(health.name=="White Defender" && fsm.FsmName=="Dung Defender"
                    && (state.Name=="G Slam" || state.Name=="Air Dive" || state.Name=="AD In" || state.Name=="Pillar")) {
                    foreach(var action in state.Actions.OfType<HutongGames.PlayMaker.Actions.SendEventByName>()) {
                        var target=action.eventTarget;
                        var owner=target.gameObject==null?null:FSMUtility.GetSafe(target.gameObject,action);
                        log?.Invoke("HK_WHITE_NATIVE_EVENT_CONTRACT state="+state.Name.Replace(' ','_')+" event="+action.sendEvent.Value.Replace(' ','_')
                            +" targetKind="+target.target+" owner="+(owner?.name.Replace(' ','_')??"none")+" ownerId="+(owner?.GetInstanceID()??0)
                            +" fsm="+(target.fsmName?.Value??"")+" children="+(target.sendToChildren?.Value??false));
                    }
                    foreach(var action in state.Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>()) {
                        var owner=FSMUtility.GetSafe(action.gameObject,action);
                        log?.Invoke("HK_WHITE_NATIVE_ACTIVATION_CONTRACT state="+state.Name.Replace(' ','_')+" owner="+(owner?.name.Replace(' ','_')??"none")
                            +" ownerId="+(owner?.GetInstanceID()??0)+" active="+action.activate.Value);
                    }
                }
                if((health.name=="False Knight New" || health.name=="False Knight Dream") && fsm.FsmName=="FalseyControl"
                    && (state.Name=="Stun Start" || state.Name=="Roll End" || state.Name=="Opened" || state.Name=="Opened 2" || state.Name=="Hit" || state.Name=="Hit 2" || state.Name=="S Attack" || state.Name=="Slam")) {
                    foreach(var action in state.Actions.OfType<HutongGames.PlayMaker.Actions.ActivateGameObject>()) {
                        var obj=FSMUtility.GetSafe(action.gameObject,action);
                        log?.Invoke("HK_ARMOR_NATIVE_OBJECT_CONTRACT state="+state.Name.Replace(' ','_')+" type=Activate owner="+(obj?.name.Replace(' ','_')??"none")+" ownerId="+(obj?.GetInstanceID()??0)+" active="+action.activate.Value);
                    }
                    foreach(var action in state.Actions.OfType<HutongGames.PlayMaker.Actions.SetCollider>()) {
                        var obj=FSMUtility.GetSafe(action.gameObject,action);
                        log?.Invoke("HK_ARMOR_NATIVE_OBJECT_CONTRACT state="+state.Name.Replace(' ','_')+" type=Collider owner="+(obj?.name.Replace(' ','_')??"none")+" ownerId="+(obj?.GetInstanceID()??0)+" active="+action.active.Value);
                    }
                }
            }
        }
    }
}
