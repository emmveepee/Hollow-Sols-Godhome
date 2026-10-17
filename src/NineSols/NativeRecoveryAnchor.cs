using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace YiGodhome;

// Keep the game's fade/get-up/invulnerability sequence, but supply a spawn in
// the imported room instead of selecting one from the borrowed native scene.
internal sealed class NativeRecoveryAnchor : IDisposable
{
    private static NativeRecoveryAnchor? active;
    private readonly Harmony harmony = new Harmony("local.yigodhome.recovery-anchor");
    private readonly Player player;
    private readonly Vector2 entry;
    private readonly Func<Transform> terrain;
    private readonly Action<string> log;
    private readonly bool arena, liveArena;
    private readonly float scale, footOffset;
    private Vector2? hostGround;
    internal static void ObserveHost(Player player,YiGodhome.Bridge.ContactFrame frame,YiGodhome.Geometry.CoordinateMap map,long geometry)
    {
        var a=active;
        if(a==null || a.player!=player || !a.liveArena || frame.GeometryFrame!=geometry || !frame.HasHazardCheckpoint)return;
        var ground=map.ToTarget(new YiGodhome.Geometry.Point(frame.HazardCheckpointX,frame.HazardCheckpointY));
        var point=new Vector2(ground.X,ground.Y-a.footOffset);
        if(a.hostGround!=point)a.log("NS_RADIANCE_NATIVE_CHECKPOINT source="+frame.HazardCheckpointX+","+frame.HazardCheckpointY+" position="+point+" geometry="+geometry);
        a.hostGround=point;
    }
    public NativeRecoveryAnchor(Player player, Vector2 entry, Func<Transform> terrain, Action<string> log, string scene, float scale)
    {
        if (active != null) throw new InvalidOperationException("Duplicate imported recovery anchor.");
        this.player = player; this.entry = entry; this.terrain = terrain; this.log = log;
        footOffset = player.myCollider.bounds.min.y - player.transform.position.y;
        liveArena=scene=="GG_Radiance";
        this.scale = scale; arena = scene != "GG_Workshop" && scene != "GG_Atrium";
        active = this;
        try { harmony.Patch(AccessTools.Method(typeof(GameLevel), "GetNearestSpawnPoint"), prefix: new HarmonyMethod(typeof(NativeRecoveryAnchor), nameof(Resolve))); }
        catch { Dispose(); throw; }
    }
    private bool Supported(Vector2 position)
    {
        // A nearby wall is not safe ground. Require an upward-facing imported
        // surface immediately below the player's feet at the proposed position.
        return Physics2D.RaycastAll(new Vector2(position.x, position.y + footOffset + scale * 0.1f),
            Vector2.down, scale * 0.3f, player.solid_layer.value | player.oneway_layer.value)
            .Any(h => h.collider != null && !h.collider.isTrigger && h.normal.y > 0.99f && h.collider.transform.IsChildOf(terrain()));
    }
    private Vector2 NearestSupportedGround(bool belowOnly=false) {
        var owner=terrain();var shapes=owner.GetComponentsInChildren<Collider2D>();
        if(shapes.Length>4096)throw new InvalidOperationException("Imported recovery geometry capacity exceeded.");
        float best=float.PositiveInfinity;Vector2 result=entry;
        foreach(var shape in shapes) {
            if(shape==null || !shape.enabled || shape.isTrigger)continue;
            var b=shape.bounds;
            var hits=Physics2D.RaycastAll(new Vector2(b.center.x,b.max.y+scale*.1f),Vector2.down,b.size.y+scale*.3f,player.solid_layer);
            foreach(var hit in hits) {
                if(hit.collider!=shape || hit.normal.y<.99f)continue;
                var candidate=new Vector2(hit.point.x,hit.point.y-footOffset+scale*.01f);
                if(belowOnly && candidate.y+footOffset>player.myCollider.bounds.min.y+scale*.1f)continue;
                float distance=(candidate-(Vector2)player.transform.position).sqrMagnitude;
                if(distance>=best || !Supported(candidate))continue;
                best=distance;result=candidate;
            }
        }
        if(float.IsPositiveInfinity(best))throw new InvalidOperationException("No active platform supports Radiance recovery.");
        log("NS_RADIANCE_RECOVERY activePlatform=True nativeLastGroundUnavailable=True position="+result);
        return result;
    }
    internal static bool TryRadianceDiagnosticLanding(Player player,out Vector2 point) {
        point=default;
        if(active==null || active.player!=player || !active.liveArena || !YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures
            || !YiGodhome.Bridge.SessionConfiguration.Current.Has("invulnerable-probe"))return false;
        point=active.NearestSupportedGround(true);return true;
    }
    private static bool Resolve(ref (PlayerSpawnPointManager.SpawnType, Vector3) __result)
    {
        var a = active;
        // Installed build's sole caller is PlayerRespawnState.OnStateEnter;
        // the state enum can still report the outgoing state during that call.
        if (a == null || GameCore.Instance.player != a.player) return true;
        Vector2 point = a.player.lastSafeGroundPosition;
        // All imported rooms use the latest native grounded record. Validate
        // against this room's current terrain so donor/stale/offscreen records
        // cannot escape the bridge. Entry is only the no-valid-record fallback.
        bool recorded = a.Supported(point);
        // This anchor is room/session scoped. Keep the native checkpoint over
        // hitstop/respawn fades; a transient empty frame is not a new checkpoint.
        bool nativeCheckpoint=a.liveArena && a.hostGround.HasValue;
        if(nativeCheckpoint)point=a.hostGround!.Value;
        else {
            if (!recorded) point = a.entry;
            if(a.liveArena && !a.Supported(point))point=a.NearestSupportedGround();
        }
        if (!a.Supported(point)) throw new InvalidOperationException("No verified imported ground for native recovery.");
        a.player.SafeGroundRecorder.ForceSave(point);
        __result = ((PlayerSpawnPointManager.SpawnType)3, point);
        a.log("NS_RECOVERY_ANCHOR nativeSequence=True hostCheckpoint="+nativeCheckpoint+" recorded=" + recorded + " arena=" + a.arena + " position=" + point);
        return false;
    }
    public void Dispose() { if (active == this) active = null; harmony.UnpatchSelf(); }
}
