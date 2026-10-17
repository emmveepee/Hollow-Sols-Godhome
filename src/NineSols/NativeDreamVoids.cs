using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Geometry;

namespace YiGodhome;

// Harmless host dream falls invoke the native recovery entry independently
// of damage. Nine Sols owns the fade, safe-ground relocation and respawn state.
internal sealed class NativeDreamVoids : IDisposable
{
    private readonly Dictionary<int, BoxCollider2D> regions = new Dictionary<int, BoxCollider2D>();
    private readonly Player player;
    private readonly Action<string> log;
    private readonly CoordinateMap map;
    private float health, nextTrace;
    private readonly bool scripted = YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures;
    private readonly float started = Time.realtimeSinceStartup;
    private int fixtureDrops, recoveries;
    private float lastDrop;
    public NativeDreamVoids(Player player, CoordinateMap map, Action<string> log)
    { this.player = player; this.map = map; this.log = log; health = player.health.currentValue; }

    public void Refresh(Shape[] shapes)
    {
        var hazards = shapes.Where(s => s.HazardId != 0).ToArray();
        foreach (var id in regions.Keys.Where(id => !hazards.Any(s => s.HazardId == id)).ToArray())
        { UnityEngine.Object.Destroy(regions[id].gameObject); regions.Remove(id); }
        foreach (var s in hazards)
        {
            var p = s.Points.Select(map.ToTarget).ToArray();
            if (!regions.TryGetValue(s.HazardId, out var box))
            {
                // Geometry query only: excluded layer and no damage components.
                var obj = new GameObject("YiGodhome Harmless Dream Void " + s.HazardId) { layer = LayerMask.NameToLayer("Ignore Raycast") };
                box = obj.AddComponent<BoxCollider2D>(); box.isTrigger = true;
                regions.Add(s.HazardId, box);
                log("NS_DREAM_VOID_BOUND id=" + s.HazardId + " damage=0 nativeRecovery=True");
            }
            box.transform.position = new Vector2((p[0].X + p[2].X) * 0.5f, (p[0].Y + p[2].Y) * 0.5f);
            box.size = new Vector2(Math.Abs(p[2].X - p[0].X), Math.Abs(p[2].Y - p[0].Y));
        }
        Physics2D.SyncTransforms();
    }
    public void Update()
    {
        if (regions.Count == 0) return;
        if (scripted && fixtureDrops < 2 && Time.realtimeSinceStartup - started > 25
            && player.IsOnGround && !player.TouchTraped && player.CurrentStateType == PlayerStateType.Normal
            && (fixtureDrops == 0 || Time.realtimeSinceStartup - lastDrop > 8 && recoveries >= fixtureDrops))
        {
            // Test placement only; retain the native safe-ground record.
            var box = regions.Values.First().bounds;
            player.SetPhysicPosition(new Vector2(box.center.x, box.max.y + map.Scale * 8));
            player.Velocity = Vector2.zero; player.SetOnGround = false; player.ResetNeedToCheckGroundFlag();
            lastDrop = Time.realtimeSinceStartup; fixtureDrops++;
            log("NS_DREAM_VOID_FIXTURE drop=" + fixtureDrops + " safeGroundUnchanged=True");
        }
        if (!player.health.IsDead && !player.TouchTraped && player.CurrentStateType != PlayerStateType.Respawn)
            foreach (var region in regions)
                if (Physics2D.Distance(region.Value, player.myCollider).isOverlapped)
                {
                    // The audited native method does not modify health/resources.
                    player.TouchTrapResetSpawnPoint(1);
                    recoveries++;
                    log("NS_DREAM_VOID_RECOVERY count=" + recoveries + " id=" + region.Key + " health=" + player.health.currentValue + " damage=0");
                    break;
                }
        if (health != player.health.currentValue || Time.realtimeSinceStartup >= nextTrace)
        {
            nextTrace = Time.realtimeSinceStartup + 0.5f;
            log("NS_DREAM_VOID_STATE health=" + player.health.currentValue + " previousHealth=" + health
                + " position=" + player.transform.position + " state=" + player.CurrentStateType + " grounded=" + player.IsOnGround);
            health = player.health.currentValue;
        }
    }
    public void Dispose()
    {
        foreach (var box in regions.Values) if (box != null) { box.gameObject.SetActive(false); UnityEngine.Object.Destroy(box.gameObject); }
        regions.Clear();
    }
}
