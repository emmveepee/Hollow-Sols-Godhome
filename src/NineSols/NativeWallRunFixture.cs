using System;
using System.IO;
using UnityEngine;
using YiGodhome.Bridge;
using YiGodhome.Geometry;

namespace YiGodhome;

internal sealed class NativeWallRunFixture : IDisposable
{
    private static NativeWallRunFixture? active;
    public static string? Failure;
    public static bool Running => active != null && active.index < active.strips.Length;
    private readonly Player player;
    private readonly CoordinateMap map;
    private readonly Action<string> log;
    private Vector3 original;
    private static readonly System.Collections.Generic.HashSet<string> completed=new();
    private readonly string scene;
    private readonly WallRunStrip[] strips;
    private int index;
    private float started;
    private bool placed, observed;
    private float initialY, peakY, elapsed;
    private bool ceilingBlocked, reachedCeiling;
    private bool waitingLedge;
    private bool physicalEndSample;
    private float ceilingGain;
    private readonly HarmonyLib.Harmony audit = new HarmonyLib.Harmony("local.yigodhome.wall-probe-audit");
    private int auditSamples;
    private int jumpAuditSamples;
    private float nextJumpAudit;
    private bool nativeJumped;
    private float jumpStartX, jumpAway;
    private string? jumpExitState;
    private static void AfterWallJump(bool __result)
    {
        var a=active;
        if(a==null || !a.ceilingBlocked || a.elapsed<3 || !__result || a.nativeJumped) return;
        a.nativeJumped=true;a.jumpStartX=a.player.transform.position.x;
        a.log("NS_WALL_NATIVE_JUMP index="+a.index+" nativeResult=True state="+a.player.CurrentStateType+" scene="+a.scene);
    }
    private static void BeforeWallJump()
    {
        var a=active;
        if(a==null || !a.ceilingBlocked || a.elapsed<3 || a.elapsed<a.nextJumpAudit || a.jumpAuditSamples>=32) return;
        a.jumpAuditSamples++;a.nextJumpAudit=a.elapsed+.025f;
        object? Read(string name)=>HarmonyLib.AccessTools.Property(typeof(Player),name).GetValue(a.player);
        var condition=HarmonyLib.AccessTools.Field(typeof(Player),"onGroundCondition").GetValue(a.player);
        var offGround=HarmonyLib.AccessTools.Property(condition.GetType(),"FalseTimer").GetValue(condition);
        a.log("NS_WALL_JUMP_CONTRACT index="+a.index+" state="+a.player.CurrentStateType+" elapsed="+a.elapsed+" moveX="+a.player.moveX
            +" buffered="+HarmonyLib.AccessTools.Field(typeof(Player),"allowBufferedWallJump").GetValue(a.player)
            +" ability="+Read("CanWallSlideAndWallJump")+" canJump="+Read("CanJump")+" offGround="+offGround+" bufferSeconds="+Read("JumpBufferTime")
            +" tempDisable="+a.player.TempDisableJump+" slowWalk="+Read("IsSlowWalk")+" weak="+Read("IsWeak")
            +" inputBuffered="+HarmonyLib.AccessTools.Property(typeof(ConditionTimer.Condition),"InputBuffered").GetValue(a.player.jumpWasPressedCondition));
    }
    private static void BeforeLedge(Player __instance)
    {
        var a = active;
        if (a == null || a.player != __instance || a.auditSamples++ >= 12) return;
        var b = (Bounds)HarmonyLib.AccessTools.Property(typeof(Actor), "cachedBounds").GetValue(__instance);
        var offset = __instance.BackDir * 20;
        int mask = __instance.solid_layer.value | __instance.shield_layer.value;
        var hit = Physics2D.OverlapArea(new Vector2(b.min.x, b.max.y) + offset, new Vector2(b.max.x, b.max.y) + offset, mask);
        a.log("NS_WALL_LEDGE_AUDIT facing=" + __instance.Facing + " back=" + __instance.BackDir + " bounds=" + b
            + " source=" + a.map.ToSource(new Point(b.center.x, b.min.y)).X + " scale=" + a.map.Scale
            + " probe=" + (new Vector2(b.center.x, b.max.y) + offset) + " hit=" + (hit == null ? "none" : hit.name)
            + " inputX=" + __instance.moveX);
    }
    public static NativeWallRunFixture? Create(Player player, CoordinateMap map, Action<string> log,string scene="GG_Workshop")
    {
        string run = Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
        return (scene=="GG_Workshop" || scene=="GG_Atrium" || scene=="GG_Flukemarm" || scene=="GG_Uumuu" || scene=="GG_Uumuu_V") && !completed.Contains(scene) && YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures && YiGodhome.Bridge.SessionConfiguration.Current.Has("wall-probe") ? new NativeWallRunFixture(player, map, log,scene) : null;
    }
    private NativeWallRunFixture(Player player, CoordinateMap map, Action<string> log,string scene)
    {
        this.player = player; this.map = map; this.log = log;this.scene=scene;
        var authoredStrips=RoomFeatures.ClimbingWalls(scene);
        // Leave the native center-locked intro through its actual right-side
        // gameplay camera region, then verify the left wall in ordinary framing.
        strips=scene=="GG_Uumuu" || scene=="GG_Uumuu_V" ? new[]{authoredStrips[1],authoredStrips[0]} : authoredStrips;
        original = player.transform.position; started = Time.realtimeSinceStartup + 1; active = this;
        audit.Patch(HarmonyLib.AccessTools.Method(typeof(Player), "RunUpLedgeCheck"), prefix: new HarmonyLib.HarmonyMethod(typeof(NativeWallRunFixture), nameof(BeforeLedge)));
        audit.Patch(HarmonyLib.AccessTools.Method(typeof(PlayerWallClimbBaseState),"WallJumpCheck"),prefix:new HarmonyLib.HarmonyMethod(typeof(NativeWallRunFixture),nameof(BeforeWallJump)),postfix:new HarmonyLib.HarmonyMethod(typeof(NativeWallRunFixture),nameof(AfterWallJump)));
    }
    public static Controls Apply(Controls incoming)
    {
        var a = active;
        if (a == null || a.index >= a.strips.Length || NativeInputBridge.HostPaused || NativeRoomEntry.Active) return incoming;
        if (Time.realtimeSinceStartup < a.started) return new Controls(0, 0, incoming.Buttons & Buttons.Keyboard);
        var strip = a.strips[a.index];
        if (!a.placed)
        {
            if (a.index == 0) a.original = a.player.transform.position;
            // Both Uumuu arenas extend below their safe playable base. Start
            // above the measured103.4827 pool floor or the native alternate
            // spike polygon's102.6447 top, never inside those native hazards.
            float minimumStart = a.scene == "GG_Uumuu" || a.scene == "GG_Uumuu_V" ? 104.5f : 12;
            var point = a.map.ToTarget(new Point(strip.ContactX + strip.Facing * 0.28f, Math.Max(strip.Bottom + 1, minimumStart)));
            a.player.ChangeState(PlayerStateType.Normal, false);
            a.player.SetPosition(new Vector3(point.X, point.Y, a.original.z), true);
            a.player.ResetJumpAndDash(true, true); a.player.SetOnGround = false; a.player.ResetNeedToCheckGroundFlag();
            a.player.Velocity = new Vector2(0, 200);
            a.initialY = a.peakY = a.player.transform.position.y;
            a.ceilingBlocked=a.scene=="GG_Flukemarm" || a.scene=="GG_Atrium" && a.index<2;
            a.reachedCeiling=false;
            a.waitingLedge=false;
            a.physicalEndSample=false;
            a.jumpAuditSamples=0;a.nextJumpAudit=0;a.nativeJumped=false;a.jumpAway=0;a.jumpExitState=null;
            if(a.ceilingBlocked) {
                Physics2D.SyncTransforms();
                var ceiling=Physics2D.Raycast(a.player.transform.position+Vector3.up,Vector2.up,(strip.Top-strip.Bottom+4)*a.map.Scale,a.player.solid_layer.value);
                if(ceiling.collider==null || Math.Abs(a.map.ToSource(new Point(0,ceiling.point.y)).Y-strip.Top)>.1f)
                    throw new InvalidOperationException("Hall tunnel fixture lacks its pinned native ceiling.");
                a.ceilingGain=(ceiling.point.y-a.player.myCollider.bounds.size.y-a.initialY)/a.map.Scale;
                a.log("NS_WALL_NATIVE_CEILING index="+a.index+" sourceTop="+strip.Top+" requiredGain="+a.ceilingGain+" collider="+ceiling.collider.name);
            }
            a.placed = true; a.started = Time.realtimeSinceStartup; a.elapsed = 0;
            a.log("NS_WALL_FIXTURE_BEGIN scene="+a.scene+" index=" + a.index + " area=" + a.player.WallRunEnableAreaCheck()+" contactX="+strip.ContactX+" facing="+strip.Facing);
        }
        a.observed |= a.player.CurrentStateType == PlayerStateType.WallRun;
        a.peakY = Math.Max(a.peakY, a.player.transform.position.y);
        if(a.nativeJumped) a.jumpAway=Math.Max(a.jumpAway,(a.player.transform.position.x-a.jumpStartX)*strip.Facing/a.map.Scale);
        if(a.nativeJumped && a.jumpAway>=.25f && (a.player.CurrentStateType==PlayerStateType.Normal || a.player.CurrentStateType==PlayerStateType.WallSlide))
            a.jumpExitState=a.player.CurrentStateType.ToString();
        a.elapsed += RCGTime.deltaTime;
        if (!a.physicalEndSample && a.elapsed > 3.8f && (a.scene == "GG_Uumuu" || a.scene == "GG_Uumuu_V")) {
            a.physicalEndSample=true;
            var bounds=a.player.myCollider.bounds;
            var rootSource=a.map.ToSource(new Point(a.player.transform.position.x,a.player.transform.position.y));
            var feet=a.map.ToSource(new Point(bounds.center.x,bounds.min.y));
            var head=a.map.ToSource(new Point(bounds.center.x,bounds.max.y));
            var roof=Physics2D.Raycast(new Vector2(bounds.center.x,bounds.max.y+.01f),Vector2.up,4*a.map.Scale,a.player.solid_layer.value);
            a.log("NS_UUMUU_WALL_PHYSICAL_END index="+a.index+" scene="+a.scene+" state="+a.player.CurrentStateType+" elapsed="+a.elapsed
                +" rootX="+rootSource.X+" rootY="+rootSource.Y+" feetY="+feet.Y+" headY="+head.Y
                +" roof="+(roof.collider==null?"none":roof.collider.name)+" roofY="+(roof.collider==null?0:a.map.ToSource(new Point(roof.point.x,roof.point.y)).Y)
                +" stripTop="+strip.Top+" nativeBounds=True stateForced=False");
        }
        // The actual native ledge animation exits through its animation event.
        // Allow at most2s for an already reached ledge; never force its state.
        if (a.elapsed > 4 && a.elapsed < 6 && !a.ceilingBlocked && a.observed && a.player.CurrentStateType == PlayerStateType.LedgeClimb) {
            if (!a.waitingLedge) {
                a.waitingLedge = true;
                a.log("NS_WALL_NATIVE_LEDGE_WAIT index=" + a.index + " scene=" + a.scene + " elapsed=" + a.elapsed + " nativeAnimationEvent=True stateForced=False");
            }
            return new Controls(0, 0, incoming.Buttons & Buttons.Keyboard);
        }
        if (a.elapsed > 4)
        {
            float gain = (a.peakY - a.initialY) / a.map.Scale;
            float requiredGain = strip.Top - a.map.ToSource(new Point(0, a.initialY)).Y - 1;
            if(a.ceilingBlocked) requiredGain=a.ceilingGain-.1f;
            // A successful native jump can reach the opposite tunnel wall and
            // enter WallSlide before this check. Require actual jump/away motion,
            // rather than forcing the free-ledge final state inside a shaft.
            bool exited=a.ceilingBlocked ? a.nativeJumped && a.jumpAway>=.25f && a.jumpExitState!=null
                : a.player.CurrentStateType==PlayerStateType.Normal;
            if (!a.observed || gain < requiredGain || !exited || a.ceilingBlocked && !a.reachedCeiling)
            {
                Failure = "Native wall run failed: index=" + a.index + " observed=" + a.observed + " gain=" + gain+" finalState="+a.player.CurrentStateType+" ceilingReached="+a.reachedCeiling;
                a.log("NS_WALL_FIXTURE_FAILED " + Failure); active = null; return default;
            }
            a.log("NS_WALL_FIXTURE_PASS index=" + a.index + " nativeWallRun=True gain=" + gain + " finalState=" + a.player.CurrentStateType
                + " requiredGain=" + requiredGain + " bottom=" + strip.Bottom + " top=" + strip.Top+" ceilingBlocked="+a.ceilingBlocked+" nativeCeilingReached="+a.reachedCeiling
                +" nativeJumped="+a.nativeJumped+" jumpAway="+a.jumpAway+" scene="+a.scene+" jumpExitState="+(a.jumpExitState??"none"));
            a.index++; a.placed = a.observed = false;
            if (a.index == a.strips.Length)
            {
                a.player.ChangeState(PlayerStateType.Normal, false); a.player.SetPosition(a.original, true);
                a.player.Velocity = Vector2.zero; a.player.ResetNeedToCheckGroundFlag();
                completed.Add(a.scene);
                a.log("NS_WALL_FIXTURE_COMPLETE surfaces="+a.strips.Length+" originalPositionRestored=True scene="+a.scene);
                return default;
            }
        }
        if(a.ceilingBlocked && a.elapsed>3) {
            if(!a.reachedCeiling) a.reachedCeiling=a.observed && (a.peakY-a.initialY)/a.map.Scale>=a.ceilingGain-.1f;
            // The native tunnel has a roof, so exit with real wall-jump input.
            return new Controls(strip.Facing,0,(incoming.Buttons & Buttons.Keyboard) | (a.elapsed<3.06f ? Buttons.Jump : Buttons.None));
        }
        return new Controls(-strip.Facing, 1, incoming.Buttons & Buttons.Keyboard);
    }
    public void Dispose() { audit.UnpatchSelf(); if (active == this) active = null; }
}
