using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace YiGodhome;

internal sealed class HeroProxy : IDisposable
{
    private static HeroProxy? rangeOwner;
    private float nativePivotFromFeet;
    private float nativeRecoveryDistance;
    internal static bool TryRadianceHazardGround(long geometry,out Vector2 point)
    {
        point=default;var owner=rangeOwner;
        if(owner==null || !owner.following || !owner.arena || owner.hero==null
            || UnitySceneManager.GetActiveScene().name!="GG_Radiance" || owner.AppliedPose==null || owner.geometryFrame!=geometry)return false;
        // HazardRespawn uses the latest native marker then FindGroundPoint's
        // extended downward Terrain ray. Send feet, not either game's pivot.
        var marker=GameManager.instance.playerData.hazardRespawnLocation;
        var hit=Physics2D.Raycast(marker,Vector2.down,owner.nativeRecoveryDistance,1<<8);
        if(hit.collider==null || hit.collider.isTrigger || hit.normal.y<.99f)return false;
        point=hit.point+Vector2.up*.01f;return true;
    }
    internal static bool TryRangePivotCorrection(string scene,out float correction) {
        var owner=rangeOwner;correction=0;
        if(owner==null || !owner.following || !owner.arena || owner.AppliedPose==null || owner.geometryFrame!=owner.AppliedPose.GeometryFrame
            || (owner.hubScene??Bridge.EncounterProfile.Selected.Scene)!=scene)return false;
        correction=owner.nativePivotFromFeet-owner.AppliedPose.Height*.5f;
        if(float.IsNaN(correction) || Math.Abs(correction)>4)throw new InvalidOperationException("Native range pivot correction left its bound.");
        return true;
    }
    private readonly Action<string> log;
    private PlayMakerFSM? bench;
    private HeroController? hero;
    private Rigidbody2D? body;
    private BoxCollider2D? targetCollider;
    private Vector2 originalSize, originalOffset;
    private MeshRenderer? heroRenderer;
    private bool originalRendererEnabled;
    private RigidbodyType2D originalBodyType;
    private RigidbodyInterpolation2D originalInterpolation;
    private float originalGravity;
    private bool originalEnabled;
    private bool requested, sawGetOff, dismounted, following, stale;
    private float requestedAt, nextTrace;
    private string lastState = "";
    private long geometryFrame = -1;
    private readonly bool arena;
    private readonly string? hubScene;
    private readonly string? benchScene;
    private GlobalEnums.DamageMode originalDamageMode;
    public bool Ready => following;
    public Bridge.Pose? AppliedPose { get; private set; }
    public bool Dismounted => dismounted;
    public HeroProxy(Action<string> log, bool arena = false, string? hubScene = null, string? benchScene = null) {
        if (benchScene != null && (arena || hubScene != null || benchScene != "GG_Atrium" && benchScene != "GG_Workshop"))
            throw new ArgumentException("Bench exit requires an audited Godhome rest room.");
        this.log = log; this.arena = arena; this.hubScene = hubScene; this.benchScene = benchScene;
    }
    public void Update(Bridge.Pose? pose)
    {
        if (UnitySceneManager.GetActiveScene().name != (benchScene ?? hubScene ?? (arena ? YiGodhome.Bridge.EncounterProfile.Selected.Scene : "GG_Atrium")) || GameManager.instance.profileID != 4)
            throw new InvalidOperationException("Proxy diagnostic left its validated scene/profile.");
        if (hero == null) hero = HeroController.instance;
        if (hero == null) return;
        if (hubScene != null && !requested)
        {
            if (!GameManager.instance.HasFinishedEnteringScene || hero.controlReqlinquished || GameManager.instance.playerData.atBench) return;
            requested = dismounted = true;
        }
        if (arena && !requested)
        {
            if (BossSceneController.Instance == null || !BossSceneController.Instance.HasTransitionedIn
                || hero.controlReqlinquished || GameManager.instance.playerData.atBench) return;
            requested = dismounted = true;
            log("HK_PROXY_ARENA_ENTRY_VERIFIED scene=" + YiGodhome.Bridge.EncounterProfile.Selected.Scene);
        }
        if (!requested)
        {
            string room = benchScene ?? "GG_Atrium";
            string marker = benchScene == null ? "RestBench" : GameManager.instance.playerData.respawnMarkerName;
            bench = Resources.FindObjectsOfTypeAll<PlayMakerFSM>().Single(f => f.gameObject.scene.name == room
                && f.name == marker && f.FsmName == "Bench Control" && f.gameObject.activeInHierarchy);
            if (bench.ActiveStateName != "Resting") return;
            var resting = bench.FsmStates.Single(s => s.Name == "Resting");
            if (!resting.Transitions.Any(t => t.EventName == "GET UP" && t.ToState == "Get Up")
                || !bench.FsmStates.Any(s => s.Name == "Get Off") || !bench.FsmStates.Any(s => s.Name == "Regain Control")
                || !GameManager.instance.playerData.atBench) throw new InvalidOperationException("Unexpected native bench contract.");
            requested = true; requestedAt = Time.realtimeSinceStartup;
            log("HK_PROXY_BENCH_REQUEST event=GET UP from=Resting");
            bench.SendEvent("GET UP");
        }
        if (!dismounted)
        {
            string state = bench!.ActiveStateName;
            if (state != lastState) { lastState = state; log("HK_PROXY_BENCH_STATE state=" + state + " atBench=" + GameManager.instance.playerData.atBench + " relinquished=" + hero.controlReqlinquished); }
            if (state == "Get Off") sawGetOff = true;
            if (sawGetOff && !GameManager.instance.playerData.atBench && !hero.controlReqlinquished && (state == "Idle" || state == "In Range"))
            { dismounted = true; log("HK_PROXY_DISMOUNT_VERIFIED state=" + state + " elapsed=" + (Time.realtimeSinceStartup - requestedAt)); }
            else if (Time.realtimeSinceStartup - requestedAt > 6) throw new InvalidOperationException("Native bench exit timed out.");
            else return;
        }
        if (!following && pose != null)
        {
            body = hero.GetComponent<Rigidbody2D>(); if (body == null) throw new InvalidOperationException("Missing native hero body.");
            // Native SetupGameRefs uses this exact root renderer. Descendant renderers
            // include camera blackout masks and must not be hidden with the Knight.
            heroRenderer = hero.GetComponent<MeshRenderer>(); if (heroRenderer == null) throw new InvalidOperationException("Missing native hero renderer.");
            originalBodyType = body.bodyType; originalGravity = body.gravityScale; originalEnabled = hero.enabled;
            originalInterpolation = body.interpolation;
            log("HK_PROXY_BODY_CAPTURE interpolation=" + originalInterpolation + " body=" + body.position + " transform=" + hero.transform.position + " bounds=" + hero.GetComponent<Collider2D>().bounds);
            targetCollider = hero.GetComponent<BoxCollider2D>();
            if (targetCollider == null) throw new InvalidOperationException("Native target requires audited box collider.");
            originalSize = targetCollider.size; originalOffset = targetCollider.offset;
            Physics2D.SyncTransforms();
            nativePivotFromFeet=body.position.y-targetCollider.bounds.min.y;
            if(arena && UnitySceneManager.GetActiveScene().name=="GG_Radiance") {
                nativeRecoveryDistance=(float)HarmonyLib.AccessTools.Field(typeof(HeroController),"FIND_GROUND_POINT_DISTANCE_EXT").GetValue(hero);
                if(nativeRecoveryDistance<=0 || float.IsNaN(nativeRecoveryDistance) || float.IsInfinity(nativeRecoveryDistance))
                    throw new InvalidOperationException("Native hazard ground search distance changed.");
            }
            if(rangeOwner!=null && rangeOwner!=this)throw new InvalidOperationException("Competing native range proxy owner.");
            rangeOwner=this;
            originalRendererEnabled = heroRenderer.enabled;
            originalDamageMode = hero.damageMode;
            geometryFrame = pose.GeometryFrame; following = true;
            if (hubScene == null) hero.RelinquishControl();
            else hero.AcceptInput(); // Native interaction FSMs remain active; locomotion component does not.
            hero.enabled = false;
            if (arena) hero.damageMode = GlobalEnums.DamageMode.NO_DAMAGE;
            body.bodyType = RigidbodyType2D.Kinematic; body.gravityScale = 0; body.linearVelocity = Vector2.zero;
            body.interpolation = RigidbodyInterpolation2D.None;
            heroRenderer.enabled = false;
            log("HK_PROXY_READY nativeHeroRetained=True nativeMovement=False geometryFrame=" + geometryFrame);
        }
        if (!following) return;
        body!.linearVelocity = Vector2.zero;
        if (arena && hero.damageMode != GlobalEnums.DamageMode.NO_DAMAGE) throw new InvalidOperationException("Native proxy damage protection changed.");
        if (pose == null)
        {
            if (!stale) log("HK_PROXY_FROZEN reason=stale-pose");
            stale = true; return;
        }
        if (pose.GeometryFrame != geometryFrame) throw new InvalidOperationException("Proxy geometry identity changed.");
        stale = false;
        // Aimed attacks read the target Transform, not its feet. Remove the
        // Knight artwork's pivot offset and expose Yi's actual body dimensions.
        var scale = targetCollider!.transform.lossyScale;
        targetCollider.offset = Vector2.zero;
        targetCollider.size = new Vector2(pose.Width / Math.Abs(scale.x), pose.Height / Math.Abs(scale.y));
        // Use the live native collider offset; the Knight's transform is not its feet.
        Physics2D.SyncTransforms();
        var bounds = hero.GetComponent<Collider2D>().bounds;
        var delta = new Vector3(pose.X - bounds.center.x, pose.Y - bounds.min.y, 0);
        // Collider bounds are physics coordinates. During a native falling entry,
        // the interpolated rendered Transform can lag Rigidbody2D.position.
        var target = body.position + (Vector2)delta;
        body.position = target;
        hero.transform.position = new Vector3(target.x, target.y, hero.transform.position.z);
        AppliedPose = pose;
        hero.cState.onGround = pose.Grounded; hero.cState.falling = pose.VelocityY < -0.01f;
        hero.cState.jumping = pose.VelocityY > 0.01f;
        if(hubScene!=null && !hero.enabled && !hero.controlReqlinquished) {
            // Knight combat timers do not run while Yi owns locomotion. A lever
            // or native tink must not leave their stale flags blocking statues.
            hero.cState.attacking=false;hero.cState.dashing=false;hero.cState.backDashing=false;
            hero.cState.recoiling=false;hero.cState.recoilFrozen=false;
            if(!hero.cState.transitioning && !hero.cState.hazardDeath && !hero.cState.hazardRespawning && GameManager.instance?.IsGamePaused()!=true)
                hero.AcceptInput();
        }
        if (Math.Abs(pose.VelocityX) > 0.01f)
        {
            if (hubScene != null) { if (pose.VelocityX > 0) hero.FaceRight(); else hero.FaceLeft(); }
            else hero.cState.facingRight = pose.VelocityX > 0;
        }
        Physics2D.SyncTransforms();
        if (Time.realtimeSinceStartup >= nextTrace)
        {
            nextTrace = Time.realtimeSinceStartup + 1;
            bounds = hero.GetComponent<Collider2D>().bounds;
            float error = Vector2.Distance(new Vector2(bounds.center.x, bounds.min.y), new Vector2(pose.X, pose.Y));
            if (error > 0.01f) throw new InvalidOperationException("Proxy foot mapping drifted: error=" + error + " pose=" + pose.X + "," + pose.Y + " bounds=" + bounds + " position=" + hero.transform.position + " collider=" + hero.GetComponent<Collider2D>().enabled + " recoiling=" + hero.cState.recoiling);
            string F(float x) => x.ToString("R", CultureInfo.InvariantCulture);
            log("HK_PROXY_SAMPLE x=" + F(pose.X) + " y=" + F(pose.Y) + " error=" + F(error)
                + " cameraX=" + F(Camera.main.transform.position.x) + " cameraY=" + F(Camera.main.transform.position.y) + " nativeDisabled=" + !hero.enabled);
        }
    }
    public void Dispose() => Release(true);
    public void Release(bool regainControl)
    {
        if(rangeOwner==this)rangeOwner=null;
        if (!following) return;
        following = false;
        log("HK_PROXY_RELEASE_BEGIN position=" + (hero == null ? Vector3.zero : hero.transform.position)
            + " body=" + (body == null ? Vector2.zero : body.position) + " velocity=" + (body == null ? Vector2.zero : body.linearVelocity));
        // An interaction may already have issued its one-shot native walk velocity.
        // Preserve it when handing control back instead of waiting for an FSM timeout.
        var handoffVelocity = !regainControl && body != null ? body.linearVelocity : Vector2.zero;
        if (heroRenderer != null) heroRenderer.enabled = originalRendererEnabled;
        if (targetCollider != null)
        {
            Physics2D.SyncTransforms();
            float foot = targetCollider.bounds.min.y;
            targetCollider.size = originalSize; targetCollider.offset = originalOffset;
            Physics2D.SyncTransforms();
            if (body != null) { var p = body.position; p.y += foot - targetCollider.bounds.min.y; body.position = p; if (hero != null) hero.transform.position = new Vector3(p.x, p.y, hero.transform.position.z); }
        }
        if (body != null) { body.bodyType = originalBodyType; body.gravityScale = originalGravity; body.interpolation = originalInterpolation; body.linearVelocity = handoffVelocity; }
        if (hero != null) { hero.damageMode = originalDamageMode; hero.enabled = originalEnabled; if (regainControl) { hero.RegainControl(); hero.IgnoreInput(); } }
        log("HK_PROXY_RELEASED");
    }
}
