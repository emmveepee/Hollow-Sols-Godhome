using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using YiGodhome.Geometry;

namespace YiGodhome;

internal static class GeometryExport
{
    internal static Snapshot? Latest { get; private set; }
    private static int armorPlatformScene=int.MinValue;
    private static bool armorPlatformExported;
    private static int noskPlatformScene=int.MinValue;
    private static readonly MemoryStream encodedScratch = new MemoryStream();
    private static readonly byte[] comparisonScratch = new byte[4096];
    private static readonly SnapshotPublication publication = new SnapshotPublication();
    // Grimm's authored arena side barriers are HeroBox (13), not Terrain (8).
    // Only the pinned static native wall objects join the terrain transfer.
    private static bool NativeArenaBarrier(Collider2D c, string scene) =>
        (scene == "GG_Grimm" || scene == "GG_Grimm_Nightmare") && c.gameObject.layer == 13
        && c.name == "Cutscene Wall" && c is BoxCollider2D && c.attachedRigidbody == null
        && c.GetComponentInParent<HealthManager>() == null;
    // Failed Champion's authored staggered-body platform is Soft Terrain,
    // unlike False Knight's otherwise equivalent Terrain-layer platform.
    private static bool NativeArmorPlatform(Collider2D c,string scene)
    {
        if((scene!="GG_Failed_Champion" && scene!="GG_False_Knight") || (c.gameObject.layer!=25 && c.gameObject.layer!=8)
            || c.name!="FK Terrain Block" || c is not BoxCollider2D) return false;
        // Use the exact reference already validated by the combat adapter.
        // Roll End's action value is not an identity/ownership test.
        if(!NativeArmorHead.IsStandablePlatform(c,scene))
            throw new InvalidOperationException("Active native armor platform lacks its validated owner.");
        return true;
    }
    private static bool RadianceAscentBox(Collider2D c,string scene) {
        var platform=c.transform.parent;
        return scene=="GG_Radiance" && c is BoxCollider2D && c.name=="Colliders" && platform!=null
            && (platform.name.StartsWith("Radiant Plat Thick",StringComparison.Ordinal)
                || platform.name.StartsWith("Radiant Plat Small",StringComparison.Ordinal)
                || platform.name.StartsWith("Radiant Plat Wide",StringComparison.Ordinal))
            && platform.parent?.name=="Ascend Set" && platform.parent.parent?.name=="Plat Sets"
            && platform.parent.parent.parent?.name=="Boss Control";
    }
    private static Point RestAnchor(Vector3 position)
    {
        var ground = Physics2D.RaycastAll(position + Vector3.up * 2, Vector2.down, 6, 1 << 8)
            .Where(h => h.collider != null && !h.collider.isTrigger && h.normal.y > 0.99f).OrderBy(h => h.distance).FirstOrDefault();
        if (ground.collider == null) throw new InvalidOperationException("No native floor below rest placement.");
        return new Point(position.x, ground.point.y);
    }
    private static bool TopEntry(string scene,HeroController hero)
        => BossSceneController.Instance==null && hero.sceneEntryGate!=null && hero.sceneEntryGate.gameObject.scene.name==scene
            && hero.sceneEntryGate.GetGatePosition()==GlobalEnums.GatePosition.top;
    internal static RaycastHit2D EntryFloor(string scene,HeroController hero) {
        float distance=20;
        if(TopEntry(scene,hero)) {
            var terrain=RoomColliderSource.Find(scene,null).Where(c=>c!=null && c.gameObject.scene.name==scene && c.gameObject.layer==8
                && c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger).ToArray();
            if(terrain.Length!=0)distance=Mathf.Clamp(hero.transform.position.y-terrain.Min(c=>c.bounds.min.y)+1,20,512);
        }
        return Physics2D.RaycastAll(hero.transform.position,Vector2.down,distance,1<<8)
            .Where(h=>h.collider!=null && h.collider.gameObject.scene.name==scene && !h.collider.isTrigger && h.normal.y>0.99f)
            .OrderBy(h=>h.distance).FirstOrDefault();
    }
    public static long Save(Action<string> log, string scene, HeroController hero, bool kinematic = false, Snapshot? anchor = null)
    {
        bool measured = anchor != null && HostFrameProbe.Enabled;
        long begin = measured ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var floor = anchor != null ? default(RaycastHit2D) : EntryFloor(scene,hero);
        if (anchor == null && floor.collider == null) throw new InvalidOperationException("No flat terrain floor under native spawn.");
        var shapes = new List<Shape>();
        int colliders = 0;
        Collider2D? armorPlatform=null;
        foreach (var candidate in (anchor == null ? UnityEngine.Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None) : Array.Empty<Collider2D>())
            .Where(c => c.gameObject.scene.name == scene && c.gameObject.layer == 8 && c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger && (c.usedByEffector || c.attachedRigidbody != null)))
        {
            var body = candidate.attachedRigidbody;
            log("HK_GEOMETRY_BODY name=" + candidate.name + " shape=" + candidate.GetType().Name + " effector=" + candidate.usedByEffector
                + " type=" + (body == null ? "none" : body.bodyType.ToString()) + " simulated=" + (body != null && body.simulated)
                + " constraints=" + (body == null ? "none" : body.constraints.ToString()) + " bounds=" + candidate.bounds
                + " components=" + string.Join(",", candidate.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name)));
        }
        foreach (var c in RoomColliderSource.Find(scene, HostFrameProbe.Enabled ? log : null)
            .Where(c => (c.gameObject.layer == 8 || NativeArenaBarrier(c, scene) || NativeArmorPlatform(c,scene)) && c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger
                && (c.attachedRigidbody == null || c.attachedRigidbody.simulated)))
        {
            bool standableArmor=NativeArmorPlatform(c,scene);
            if(standableArmor) armorPlatform=c;
            if (c.usedByEffector || c.attachedRigidbody != null && c.attachedRigidbody.bodyType != RigidbodyType2D.Static
                && !(kinematic && c.attachedRigidbody.bodyType == RigidbodyType2D.Kinematic))
                throw new InvalidOperationException("Unsupported terrain semantics: " + c.name);
            Point World(Vector2 p) { Vector3 v = c.transform.TransformPoint(p + c.offset); return new Point(v.x, v.y); }
            if (c is EdgeCollider2D edge)
            {
                var scale = c.transform.lossyScale;
                if (edge.edgeRadius != 0 && Math.Abs(Math.Abs(scale.x) - Math.Abs(scale.y)) > 0.0001f)
                    throw new InvalidOperationException("Nonuniform rounded edge unsupported.");
                shapes.Add(new Shape { Points = edge.points.Select(World).ToArray(), Radius = edge.edgeRadius * Math.Abs(scale.x),
                    AdjacentStart = edge.useAdjacentStartPoint, AdjacentEnd = edge.useAdjacentEndPoint,
                    Start = World(edge.adjacentStartPoint), End = World(edge.adjacentEndPoint) });
            }
            else if (c is BoxCollider2D box)
            {
                if (box.edgeRadius != 0) throw new InvalidOperationException("Rounded terrain box unsupported.");
                Vector2 h = box.size * 0.5f;
                if(standableArmor) {
                    // Native activation can enclose Yi. Do not import the shell
                    // into NS's embedded-solid hazard query; retain its exact top.
                    var b=box.bounds;
                    shapes.Add(new Shape {Closed=true,OneWay=true,Points=new[]{new Point(b.min.x,b.min.y),new Point(b.max.x,b.min.y),new Point(b.max.x,b.max.y),new Point(b.min.x,b.max.y)}});
                } else {
                    var points=new[] { World(new Vector2(-h.x, -h.y)), World(new Vector2(h.x, -h.y)), World(h), World(new Vector2(-h.x, h.y)) };
                    if(RadianceAscentBox(c,scene)) {
                        var outline=RadiancePlatforms.Outline(points);
                        if(!RadiancePlatforms.Matches(scene,outline))throw new InvalidOperationException("Native Radiance ascent box dimensions changed.");
                        shapes.Add(outline); // Four solid sides; never a one-way platform.
                    } else shapes.Add(new Shape {Closed=true,Points=points});
                }
            }
            else if (c is PolygonCollider2D polygon && polygon.pathCount == 1)
                shapes.Add(new Shape { Closed = true, Points = polygon.GetPath(0).Select(World).ToArray() });
            else throw new InvalidOperationException("Unsupported terrain shape: " + c.GetType().Name + " " + c.name);
            colliders++;
        }
        if(scene=="GG_Failed_Champion" || scene=="GG_False_Knight") {
            int handle=UnityEngine.SceneManagement.SceneManager.GetSceneByName(scene).handle;
            bool exported=armorPlatform!=null;
            if(armorPlatformScene!=handle || armorPlatformExported!=exported) {
                armorPlatformScene=handle;armorPlatformExported=exported;
                log("HK_NATIVE_ARMOR_PLATFORM exported="+exported+" scene="+scene+" frame="+Time.frameCount
                    +" nativeCollider="+(armorPlatform?.GetInstanceID()??0)+" nativeBounds="+(armorPlatform?.bounds.ToString()??"none")+" nativeLayer="+(armorPlatform?.gameObject.layer??-1)+" authoredOwner=True oneWay=True");
            }
        }
        long terrainEnd = measured ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        if (kinematic)
            foreach (var catcher in DreamVoidSource.Find(scene, HostFrameProbe.Enabled ? log : null))
            {
                var hazard = DreamVoidSource.Capture(catcher); shapes.Add(hazard);
                if (anchor == null) log("HK_DREAM_VOID_EXPORTED id=" + hazard.HazardId + " scene=" + scene);
            }
        long dreamEnd = measured ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var snapshot = new Snapshot { SourceSession = Guid.Parse(Environment.GetEnvironmentVariable("YIGODHOME_SESSION")), Scene = scene,
            Frame = anchor?.Frame ?? Time.frameCount, Floor = anchor?.Floor ?? new Point(floor.point.x, floor.point.y), PlayerWidth = anchor?.PlayerWidth ?? hero.GetComponent<Collider2D>().bounds.size.x, Shapes = NoskPlatform.Normalize(scene,shapes.ToArray()) };
        if(scene=="GG_Nosk" && snapshot.Shapes.Length!=shapes.Count && noskPlatformScene!=UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle) {
            noskPlatformScene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            log("HK_NOSK_PLATFORM_COLLISION rectangle=True leftFace=88 rightFace=104 top=6 surroundingTerrainPreserved=True frame="+Time.frameCount);
        }
        if (anchor != null) { snapshot.EntryStyle = anchor.EntryStyle; snapshot.EntryStartX = anchor.EntryStartX; snapshot.EntryStartY=anchor.EntryStartY; }
        else if(kinematic && TopEntry(scene,hero)) {
            var gate=hero.sceneEntryGate;
            snapshot.EntryStartY=gate.transform.position.y+gate.entryOffset.y-hero.GetComponent<Collider2D>().bounds.size.y*.5f;
            snapshot.EntryStartX=snapshot.Floor.X;
            snapshot.EntryStyle=5;
            log("HK_ROOM_ENTRY style=5 gate="+gate.name+" startY="+snapshot.EntryStartY+" landingY="+snapshot.Floor.Y+" nativeAirborne=True");
        }
        else if(kinematic && BossSceneController.Instance==null && hero.sceneEntryGate!=null
            && hero.sceneEntryGate.gameObject.scene.name==scene && hero.sceneEntryGate.GetGatePosition()==GlobalEnums.GatePosition.bottom) {
            var gate=hero.sceneEntryGate;
            snapshot.EntryStartX=gate.transform.position.x+gate.entryOffset.x;
            // Native HeroController.EnterScene's bottom branch adds3 before launch.
            snapshot.EntryStartY=gate.transform.position.y+gate.entryOffset.y+3-hero.GetComponent<Collider2D>().bounds.size.y*.5f;
            snapshot.EntryStyle=6;
            log("HK_ROOM_ENTRY style=6 gate="+gate.name+" start="+snapshot.EntryStartX+","+snapshot.EntryStartY+" landing="+snapshot.Floor.X+","+snapshot.Floor.Y);
        }
        else if (YiGodhome.Bridge.ReturnPreparation.IsHub(scene) && RoomEntryPresentation.Returning)
        { snapshot.EntryStyle = 4; snapshot.EntryStartX = snapshot.Floor.X; }
        else if (kinematic && BossSceneController.Instance == null && hero.sceneEntryGate != null
            && !hero.sceneEntryGate.name.StartsWith("door_dreamReturn", StringComparison.Ordinal))
        {
            var gate = hero.sceneEntryGate;
            int position = (int)gate.GetGatePosition();
            snapshot.EntryStyle = position == 4 ? (byte)1 : position == 2 ? (byte)2 : position == 1 ? (byte)3 : (byte)0;
            snapshot.EntryStartX = position == 2 ? gate.transform.position.x + gate.entryOffset.x - 1
                : position == 1 ? gate.transform.position.x + gate.entryOffset.x + 1 : snapshot.Floor.X;
            var entryFloor = Physics2D.RaycastAll(new Vector2(snapshot.EntryStartX, snapshot.Floor.Y + 2), Vector2.down, 3, 1 << 8)
                .Any(h => h.collider != null && !h.collider.isTrigger && h.normal.y > 0.99f && Math.Abs(h.point.y - snapshot.Floor.Y) < 0.05f);
            if (!entryFloor || Math.Abs(snapshot.EntryStartX - snapshot.Floor.X) > 10) snapshot.EntryStyle = 0;
            log("HK_ROOM_ENTRY style=" + snapshot.EntryStyle + " gate=" + gate.name + " start=" + snapshot.EntryStartX + " end=" + snapshot.Floor.X + " ground=" + entryFloor);
        }
        snapshot.RestPoints = anchor?.RestPoints ?? (scene.StartsWith("GG_", StringComparison.Ordinal) ? Resources.FindObjectsOfTypeAll<PlayMakerFSM>()
            .Where(f => f.gameObject.scene.name == scene && f.FsmName == "Bench Control" && f.gameObject.activeInHierarchy)
            .OrderBy(f => f.transform.position.x).Select(f => RestAnchor(f.transform.position)).ToArray() : Array.Empty<Point>());
        string path = Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!, "godhome-geometry.bin");
        // Main-thread-only encoding scratch. Codec.Write validates before any
        // publication; its point/shape ownership remains separate from bytes.
        encodedScratch.SetLength(0); encodedScratch.Position = 0;
        Codec.Write(encodedScratch, snapshot);
        byte[] encoded = encodedScratch.GetBuffer(); int encodedLength = checked((int)encodedScratch.Length);
        long encodeEnd = measured ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        string evidencePath = path;
        path = Codec.RuntimePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var exchange = new SnapshotFileLock(path)) {
            if (anchor != null && File.Exists(path)) {
                using var existing = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (existing.Length > Codec.MaxBytes) throw new InvalidDataException("Geometry file exceeds the size limit.");
                if (SnapshotContent.Matches(existing, encoded, encodedLength, comparisonScratch)) {
                    publication.CancelPending(log);
                    if (measured) GeometryRefreshCost.Record(log, scene, begin, terrainEnd, dreamEnd, encodeEnd, encodedLength, colliders, false);
                    Latest = snapshot; return snapshot.Frame;
                }
            }
            // A concurrently starting peer must never observe a partial snapshot.
            publication.Publish(path, encoded, encodedLength, log);
        }
        if (anchor == null && path != evidencePath) {
            using var file = new FileStream(evidencePath, FileMode.Create, FileAccess.Write, FileShare.None);
            file.Write(encoded, 0, encodedLength);
        }
        Latest = snapshot;
        if (measured) GeometryRefreshCost.Record(log, scene, begin, terrainEnd, dreamEnd, encodeEnd, encodedLength, colliders, true);
        log("HK_GEOMETRY_EXPORTED scene=" + scene + " colliders=" + colliders + " points=" + shapes.Sum(s => s.Points.Length)
            + " floor=" + floor.point.ToString("F4") + " playerWidth=" + snapshot.PlayerWidth);
        return snapshot.Frame;
    }
}
