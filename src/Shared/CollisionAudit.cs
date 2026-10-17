using System;
using System.Linq;
using UnityEngine;

namespace YiGodhome;

// Read-only measurements. These are observations, not a terrain classification policy.
internal static class CollisionAudit
{
    public static void Dump(Action<string> log, string game, Component player)
    {
        string prefix = game + "_COLLISION";
        foreach (var collider in player.GetComponentsInChildren<Collider2D>(true))
            log(prefix + "_PLAYER " + Describe(collider));
        var all = UnityEngine.Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None).Where(c => c.enabled && c.gameObject.activeInHierarchy).ToArray();
        foreach (var group in all.GroupBy(c => new { c.gameObject.layer, Shape = c.GetType().Name, c.isTrigger }).OrderBy(g => g.Key.layer).ThenBy(g => g.Key.Shape))
            log(prefix + "_LAYER layer=" + group.Key.layer + " name=" + LayerMask.LayerToName(group.Key.layer)
                + " shape=" + group.Key.Shape + " trigger=" + group.Key.isTrigger + " count=" + group.Count());
        foreach (var collider in all.Where(c => !c.isTrigger && !c.transform.IsChildOf(player.transform))
            .OrderBy(c => c.bounds.SqrDistance(player.transform.position)).Take(12))
            log(prefix + "_NEAR " + Describe(collider) + " components="
                + string.Join(",", collider.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name)));
        log(prefix + "_SUMMARY active=" + all.Length + " queriesHitTriggers=" + Physics2D.queriesHitTriggers
            + " fixedDeltaTime=" + Time.fixedDeltaTime + " position=" + player.transform.position.ToString("F4"));
    }

    private static string Describe(Collider2D c)
    {
        string shape = c is BoxCollider2D box ? " size=" + box.size.ToString("F4") + " edgeRadius=" + box.edgeRadius
            : c is PolygonCollider2D polygon ? " paths=" + polygon.pathCount + " points=" + polygon.GetTotalPointCount()
            : c is EdgeCollider2D edge ? " points=" + edge.pointCount
            : c is CompositeCollider2D composite ? " paths=" + composite.pathCount + " points=" + composite.pointCount + " geometry=" + composite.geometryType
            : c is CircleCollider2D circle ? " radius=" + circle.radius
            : c is CapsuleCollider2D capsule ? " size=" + capsule.size.ToString("F4") + " direction=" + capsule.direction : "";
        return "name=" + c.name + " shape=" + c.GetType().Name + " enabled=" + c.enabled + " active=" + c.gameObject.activeInHierarchy
            + " layer=" + c.gameObject.layer + " layerName=" + LayerMask.LayerToName(c.gameObject.layer) + " trigger=" + c.isTrigger
            + " center=" + c.bounds.center.ToString("F4") + " boundsSize=" + c.bounds.size.ToString("F4")
            + " offset=" + c.offset.ToString("F4") + " scale=" + c.transform.lossyScale.ToString("F4") + shape
            + " body=" + (c.attachedRigidbody == null ? "none" : c.attachedRigidbody.bodyType.ToString());
    }
}
