using System;
using UnityEngine;
using YiGodhome.Bridge;
using YiGodhome.Geometry;

namespace YiGodhome;

// Native states own movement and animation; the host only announces the fade boundary.
internal sealed class NativeRoomEntry : IDisposable
{
    private static NativeRoomEntry? current;
    private static readonly System.Reflection.MethodInfo stopRun=HarmonyLib.AccessTools.Method(typeof(Player),"StopRun");
    private static HollowSols.Foundation.SessionToken? inspectionSession;
    public static bool Active => current != null && (!current.done || current.statueDeparting);
    public static bool LyingReturn => current != null && current.style == 4 && !current.done && !current.gettingUp;
    internal static bool AllowHorizontalMove(Player __instance) {
        var owner=current;
        // PlayerNormalState still calls HorizontalMoveCheck with neutral input
        // during lockMoving; OffInputAirRunReduce erases the entrance drift.
        return owner==null || owner.disposed || owner.done || owner.style!=6 || owner.player!=__instance
            || __instance.CurrentStateType!=PlayerStateType.Normal;
    }
    public static bool ReadyToShow => current == null || Time.frameCount > current.createdFrame + 2;
    private readonly Player player;
    private readonly Action<string> log;
    private readonly byte style;
    private readonly Vector2 destination;
    private readonly Vector2 entrance;
    private readonly Vector2 launch;
    private readonly bool previousMovingLock;
    private static readonly System.Reflection.FieldInfo nativeStats=HarmonyLib.AccessTools.Field(typeof(Player),"statData");
    private bool waitingForEntrance;
    private float entranceWaitSeconds;
    private bool started, done, disposed;
    private bool departure;
    private bool gettingUp;
    private readonly bool gestures=HollowSols.Foundation.NativeSaveContext.Enabled;
    private readonly bool inspect;
    private NativeGesturePresentation? gesture;
    private bool pendingArm,armStarted,statueDeparting;
    private float gestureTime;
    private float startedAt;
    private float nextTrace;
    private float activeSeconds;
    private double returnReadyAt,firstRollAt=-1;
    private double firstRollWorstMs;
    private int firstRollGc;
    private bool firstRollReported;
    private readonly int createdFrame = Time.frameCount;
    public NativeRoomEntry(Player player, Snapshot snapshot, CoordinateMap map, Action<string> log)
    {
        if (current != null) throw new InvalidOperationException("Duplicate room entrance.");
        this.player = player; this.log = log; style = snapshot.EntryStyle;previousMovingLock=player.lockMoving;
        destination = player.transform.position;
        var token=HollowSols.Foundation.NativeSaveContext.Session;
        inspect=gestures && token.HasValue && !Nullable.Equals(inspectionSession,token);
        if(inspect)inspectionSession=token;
        done = style == 0 && !inspect;
        if (done) { current = this; return; }
        if(style==0){current=this;return;} // No authored door relocation for an in-place entry.
        var start = map.ToTarget(new Point(snapshot.EntryStartX, style>=5?snapshot.EntryStartY:snapshot.Floor.Y));
        entrance=new Vector2(start.X,start.Y);
        player.Velocity = Vector2.zero;
        player.SetPhysicPosition(new Vector2(start.X, start.Y));
        player.ResetNeedToCheckGroundFlag();
        if(style>=5) {
            player.SetOnGround=false;player.ChangeState(PlayerStateType.Normal);player.lockMoving=true;
            if(style==6) {
                var stats=(PlayerStatScriptableObject)nativeStats.GetValue(player);
                var impulse=AirborneEntryLaunch.CalculateComponents(entrance.x,entrance.y,destination.x,destination.y,stats.Gravity,player.JumpSpeedStat.Value);
                launch=new Vector2(impulse.X,impulse.Y);
            }
            current=this;
            log("NS_ROOM_ENTRY_WAIT style="+style+" geometry="+snapshot.Frame+" nativeFall=True start="+entrance+" landing="+destination);
            return;
        }
        if (!player.IsOnGroundCheck()) throw new InvalidOperationException("Native entrance start lacks ground.");
        player.SetOnGround = true;
        if (style == 4 && !gestures)
        {
            player.ChangeState(PlayerStateType.LieDown);
            player.PlayAnimation("LieDownLoop", true);
        }
        current = this;
        log("NS_ROOM_ENTRY_WAIT style=" + style + " geometry=" + snapshot.Frame + " nativeFrame=" + Time.frameCount + " state=" + player.CurrentStateType + " start=" + player.transform.position + " destination=" + destination);
    }
    public static void Metadata(Buttons flags)
    {
        var entry = current;
        if (entry == null || entry.disposed || NativeInputBridge.HostPaused) return;
        if (!entry.done && !entry.started && (flags & Buttons.EntryRevealed) != 0)
        {
            // Native SetPhysicPosition rounds/moves the body before Unity's
            // transform catches up. MoveTo reads that transform on entry and
            // would complete immediately if it still equals the destination.
            if((entry.style==2 || entry.style==3) && !DoorWalkAdmission.AtEntrance(entry.player.transform.position.x,entry.entrance.x)) {
                if(!entry.waitingForEntrance){entry.waitingForEntrance=true;entry.log("NS_ROOM_ENTRY_WAIT_POSITION expected="+entry.entrance+" actual="+entry.player.transform.position);}
                return;
            }
            entry.started = true; entry.startedAt = Time.realtimeSinceStartup;
            if(entry.inspect && entry.style==0)entry.pendingArm=true;
            else if (entry.style == 4) { /* Return pose stays visible through the host fade. */ }
            else if(entry.style>=5){entry.player.SetOnGround=false;entry.player.Velocity=entry.launch;}
            else if (entry.style == 1) entry.player.ChangeState(PlayerStateType.ZExit);
            else
            {
                var state = (PlayerMoveToState)entry.player.FindState((PlayerStateType)73);
                state.moveToDestination = entry.destination;
                state.ArriveFacing = (Facings)(entry.destination.x >= entry.player.transform.position.x ? 1 : -1);
                // RunningCheck otherwise calls StopRun after MoveTo sets its
                // walking multiplier, replacing0.25 with1 on a cold entry.
                // Keep a direction during StopRun so native braking isn't queued.
                entry.player.moveX=entry.destination.x>=entry.player.transform.position.x?1:-1;
                stopRun.Invoke(entry.player,Array.Empty<object>());
                state.IsWalk = true;
                state.MoveToCallBack = entry.Complete;
                entry.player.ChangeState((PlayerStateType)73);
            }
            entry.log("NS_ROOM_ENTRY_STARTED style=" + entry.style + " state=" + entry.player.CurrentStateType);
        }
        if(entry.gestures && entry.done && !entry.statueDeparting && (flags & Buttons.StatueEnter)!=0) {
            entry.statueDeparting=true;entry.gestureTime=0;
            entry.gesture=new NativeGesturePresentation(entry.player,NativeContentKind.StatueMeditation);entry.gesture.Sample(0);
            entry.log("NS_STATUE_MEDITATION_SIT nativeFadeUnchanged=True");
        }
        if (entry.done && !entry.departure && !entry.statueDeparting && (flags & Buttons.DoorEnter) != 0 && entry.player.IsOnGround)
        {
            entry.departure = true;
            entry.player.ChangeState(PlayerStateType.ZEnter);
            entry.log("NS_ROOM_DEPARTURE state=ZEnter");
        }
    }
    private void Complete()
    {
        if (disposed) return;
        if(inspect && !armStarted){gesture?.Dispose();gesture=null;pendingArm=true;return;}
        gesture?.Dispose();gesture=null;
        done = true;
        if(style>=5)player.lockMoving=previousMovingLock;
        if(style==4)returnReadyAt=Time.realtimeSinceStartupAsDouble;
        log("NS_ROOM_ENTRY_COMPLETE style=" + style + " position=" + player.transform.position + " elapsed=" + (Time.realtimeSinceStartup - startedAt));
    }
    public void Update()
    {
        if(disposed || NativeInputBridge.HostPaused)return;
        if(style>=5 && !started && !done) {
            player.SetPhysicPosition(entrance);player.Velocity=Vector2.zero;
            player.ResetNeedToCheckGroundFlag();player.SetOnGround=false;
        }
        if(style==4 && done && !firstRollReported) {
            double now=Time.realtimeSinceStartupAsDouble;
            if(firstRollAt<0 && player.CurrentStateType==PlayerStateType.Roll) {
                firstRollAt=now;firstRollGc=GC.CollectionCount(0);firstRollWorstMs=Time.unscaledDeltaTime*1000;
                log("NS_RETURN_FIRST_DODGE_BEGIN frame="+Time.frameCount+" afterReadyMs="+((now-returnReadyAt)*1000)+" frameMs="+firstRollWorstMs);
            }
            if(firstRollAt>=0) {
                firstRollWorstMs=Math.Max(firstRollWorstMs,Time.unscaledDeltaTime*1000);
                if(now-firstRollAt>=.75) {
                    firstRollReported=true;
                    log("NS_RETURN_FIRST_DODGE_END frame="+Time.frameCount+" maxFrameMs="+firstRollWorstMs+" collections="+(GC.CollectionCount(0)-firstRollGc));
                }
            }
        }
        if(gestures) {
            if(statueDeparting){gestureTime+=Time.deltaTime;gesture?.Sample(Mathf.Min(1.833334f,gestureTime));return;}
            if(style==4 && gesture==null && !done && NativeContentRepository.Current?.Ready==true) {
                gesture=new NativeGesturePresentation(player,NativeContentKind.StatueMeditation);gesture.Sample(1.833334f);
                log("NS_STATUE_RETURN_MEDITATING readyBeforeReveal=True");
            }
            // Initial native checkpoint serialization must finish before the
            // visible inspection. Content readiness alone precedes that save
            // and can start the gesture in the middle of its collection stall.
            if(pendingArm && !armStarted && NativeContentRepository.Current?.Ready==true
                && HollowSolsMenuPlugin.Instance?.Lifecycle?.State==HollowSols.Foundation.BridgeState.Active) {
                armStarted=true;gestureTime=0;gesture=new NativeGesturePresentation(player,NativeContentKind.ArmInspection);gesture.Sample(11.916667f);
                log("NS_ENTRY_ARM_INSPECTION_BEGIN nativeSegment=True");
            }
            if(armStarted && !done) {
                gestureTime+=Time.deltaTime;gesture!.Sample(Mathf.Min(14.666667f,11.916667f+gestureTime));
                if(gestureTime>=2.75f){log("NS_ENTRY_ARM_INSPECTION_COMPLETE");Complete();}return;
            }
        }
        if(waitingForEntrance && !started) {
            entranceWaitSeconds+=Time.deltaTime;
            if(entranceWaitSeconds>2)throw new InvalidOperationException("Native door entrance placement did not settle.");
        }
        if (done || !started || NativeInputBridge.HostPaused) return;
        activeSeconds += Time.deltaTime;
        if (style == 4)
        {
            if(gestures) {
                if(gesture==null)return;
                if(activeSeconds>=1) {
                    if(!gettingUp){gettingUp=true;log("NS_STATUE_RETURN_STAND_BEGIN");}
                    gesture.Sample(Mathf.Min(12.016666f,10.5f+activeSeconds-1));
                    if(activeSeconds>=2.516667f){Complete();return;}
                }
                return;
            }
            if (activeSeconds >= 1 && !gettingUp)
            {
                gettingUp = true;
                HarmonyLib.AccessTools.Method(typeof(PlayerLieDownState), "GetUp").Invoke(player.CurrentState, null);
                log("NS_RETURN_GET_UP nativeAnimation=True");
            }
            if (gettingUp && player.CurrentStateType != PlayerStateType.LieDown) { Complete(); return; }
        }
        if(style>=5 && activeSeconds>.05f && player.IsOnGround){Complete();return;}
        if (style == 1 && player.CurrentStateType != PlayerStateType.ZExit) { Complete(); return; }
        if (Time.realtimeSinceStartup >= nextTrace)
        {
            nextTrace = Time.realtimeSinceStartup + 0.1f;
            log("NS_ROOM_ENTRY_SAMPLE state=" + player.CurrentStateType + " position=" + player.transform.position + " animation=" + player.animator.GetCurrentAnimatorStateInfo(0).normalizedTime+" speed="+player.Velocity.x);
        }
        if (activeSeconds > 12) throw new InvalidOperationException("Native room entrance did not complete.");
    }
    public void Dispose()
    {
        disposed = true;
        if(style>=5 && player!=null)player.lockMoving=previousMovingLock;
        gesture?.Dispose();gesture=null;
        if (current == this) current = null;
        if (started && !done && player != null && player.CurrentState is PlayerMoveToState state)
        { state.MoveToCallBack = null; player.ChangeState(PlayerStateType.Normal); }
    }
}
