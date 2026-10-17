#if !HOLLOWSOLS_PUBLIC_RELEASE
using System;
using UnityEngine;
using UnityEngine.UI;
using YiGodhome.Bridge;
using Pose = YiGodhome.Bridge.Pose;

namespace YiGodhome;

// One native UI mesh for all exact contour edges, rebuilt once per painted frame.
internal sealed class CollisionContourGraphic : MaskableGraphic
{
    private ContactFrame? frame;
    private Pose? pose;
    private Camera? camera;
    private Matrix4x4 worldToClip, worldToView;
    private Rect viewport;
    private Vector2 screenOrigin;
    private bool projectionVerified;
    private bool showCollision,showParry;
    internal Action<string>? Log;
    internal int Edges { get; private set; }
    internal void Show(ContactFrame? nextFrame, Pose? nextPose, Camera nextCamera, bool collision, bool parry)
    {
        frame = nextFrame; pose = nextPose; camera = nextCamera;
        showCollision=collision;showParry=parry;
        SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        mesh.Clear(); Edges = 0;
        if (camera == null) return;
        worldToView = camera.worldToCameraMatrix;
        worldToClip = camera.projectionMatrix * worldToView;
        viewport = camera.pixelRect;
        screenOrigin = new Vector2(Screen.width * .5f, Screen.height * .5f);
        if (showCollision && pose != null) {
            Box(mesh, pose.X, pose.Y + pose.Height * .5f, pose.Width, pose.Height, Color.green);
            foreach (var w in pose.Melee) Box(mesh, w.X, w.Y, w.Width, w.Height, Color.cyan);
        }
        if (showCollision && frame != null) {
            if (frame.HurtActive) Box(mesh, frame.HurtX, frame.HurtY, frame.HurtWidth, frame.HurtHeight, Color.green);
            foreach (var target in frame.Summons) if (target.Active) Box(mesh, target.X, target.Y, target.Width, target.Height, Color.green);
            foreach (var volume in frame.Volumes) if (volume.Active) {
                var first = Project(new Vector2(volume.Points[0], volume.Points[1]));
                var previous = first;
                for (int i = 2; i < volume.Points.Length; i += 2) {
                    var next = Project(new Vector2(volume.Points[i], volume.Points[i + 1]));
                    Line(mesh, previous, next, Color.red); previous = next;
                }
                Line(mesh, previous, first, Color.red);
            }
        }
        if(showParry && pose!=null)foreach(var points in pose.ParryContacts) {
            var first=Project(new Vector2(points[0],points[1]));var previous=first;
            for(int i=2;i<points.Length;i+=2){var next=Project(new Vector2(points[i],points[i+1]));Line(mesh,previous,next,Color.magenta);previous=next;}
            Line(mesh,previous,first,Color.magenta);
        }
        if (Log != null) HostWorkCost.Contour.Record(start, Edges, "contour_mesh", Log);
    }
    private void Box(VertexHelper mesh, float x, float y, float width, float height, Color color)
    {
        var a = Project(new Vector2(x - width * .5f, y - height * .5f));
        var b = Project(new Vector2(x + width * .5f, y - height * .5f));
        var c = Project(new Vector2(x + width * .5f, y + height * .5f));
        var d = Project(new Vector2(x - width * .5f, y + height * .5f));
        Line(mesh, a, b, color); Line(mesh, b, c, color); Line(mesh, c, d, color); Line(mesh, d, a, color);
    }
    private Vector3 Project(Vector2 point)
    {
        var clip = worldToClip * new Vector4(point.x, point.y, 0, 1);
        var screen = new Vector3(viewport.x + (clip.x / clip.w + 1) * .5f * viewport.width,
            viewport.y + (clip.y / clip.w + 1) * .5f * viewport.height, -worldToView.MultiplyPoint3x4(point).z);
        if (!projectionVerified && YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures) {
            var expected = camera!.WorldToScreenPoint(point);
            float error = Vector3.Distance(screen, expected);
            if (float.IsNaN(error) || error > .05f) throw new InvalidOperationException("Contour projection differs from native camera.");
            projectionVerified = true;
            Log?.Invoke("HK_CONTOUR_PROJECTION_VERIFIED maxPixelError=" + error + " frame=" + Time.frameCount);
        }
        return screen;
    }
    private void Line(VertexHelper mesh, Vector3 first, Vector3 last, Color color)
    {
        if (first.z <= 0 || last.z <= 0) return;
        var p = new Vector2(first.x, first.y); var q = new Vector2(last.x, last.y);
        // Reject only segments wholly outside the screen. Partially visible
        // contours and their exact collision geometry are retained.
        if (Math.Max(p.x, q.x) < -2 || Math.Min(p.x, q.x) > screenOrigin.x * 2 + 2
            || Math.Max(p.y, q.y) < -2 || Math.Min(p.y, q.y) > screenOrigin.y * 2 + 2) return;
        var delta = q - p;
        if (delta.sqrMagnitude < .0001f) return;
        // Default screen-space overlay has its origin at the canvas center.
        p -= screenOrigin; q -= screenOrigin;
        var normal = new Vector2(-delta.y, delta.x).normalized;
        int index = mesh.currentVertCount;
        mesh.AddVert(p - normal, color, Vector2.zero); mesh.AddVert(p + normal, color, Vector2.zero);
        mesh.AddVert(q + normal, color, Vector2.zero); mesh.AddVert(q - normal, color, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2); mesh.AddTriangle(index, index + 2, index + 3); Edges++;
    }
}

#endif
