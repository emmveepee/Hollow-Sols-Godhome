using System;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// The box owns native vulnerability windows; the polygon owns the body outline.
// Neither collider is changed. HealthManager.Hit remains the damage authority.
internal sealed class MossBodyGeometry
{
    private readonly PolygonCollider2D polygon;
    private readonly Vector2[] points;
    private readonly BoxCollider2D activation;
    private readonly PlayMakerFSM control;
    public MossBodyGeometry(HealthManager health)
    {
        polygon = health.GetComponent<PolygonCollider2D>();
        if (polygon == null || polygon.pathCount != 1 || health.GetComponent<BoxCollider2D>() == null)
            throw new InvalidOperationException("Moss native body/activation geometry changed.");
        points = polygon.GetPath(0);
        if (points.Length != 7) throw new InvalidOperationException("Moss authored body outline changed.");
        activation = health.GetComponent<BoxCollider2D>();
        control = health.GetComponents<PlayMakerFSM>().Single(f => f.FsmName == "Mossy Control");
    }
    public Bounds Bounds()
    {
        if (polygon == null) throw new InvalidOperationException("Moss body geometry owner disappeared.");
        var bounds = new Bounds(polygon.transform.TransformPoint(points[0] + polygon.offset), Vector3.zero);
        for (int i = 1; i < points.Length; i++) bounds.Encapsulate(polygon.transform.TransformPoint(points[i] + polygon.offset));
        return bounds;
    }
    public void Decorate(AttackVolume volume)
    {
        volume.Active &= volume.Id == activation.GetInstanceID() && (control.ActiveStateName == "Charge" || control.ActiveStateName == "In Air");
        if (!volume.Active) return;
        var outline = new float[points.Length * 2];
        for (int i = 0; i < points.Length; i++) {
            var point = polygon.transform.TransformPoint(points[i] + polygon.offset);
            outline[i * 2] = point.x; outline[i * 2 + 1] = point.y;
        }
        volume.Points = outline;
    }
}
