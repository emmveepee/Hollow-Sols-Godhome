using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using YiGodhome.Geometry;

namespace YiGodhome;

internal sealed class GeometryLandingProbe : IDisposable
{
    private static readonly System.Reflection.FieldInfo CounterPrepared = typeof(PlayerCounterPrepareState).GetField("isPrepared", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
    private static readonly System.Reflection.FieldInfo CounterLocked = typeof(PlayerCounterPrepareState).GetField("postLock", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
    private readonly Action<string> log;
    private readonly Player player;
    private GameObject root;
    private readonly Vector2 expectedFloor;
    private readonly float started;
    private readonly Snapshot snapshot;
    public string SourceScene => snapshot.Scene;
    public Guid SourceSession => snapshot.SourceSession;
    public Point SourceFloor => snapshot.Floor;
    private Shape[]? envelopeShapes;
    private CombatEnvelope combatEnvelope;
    private bool envelopeValid;
    internal bool TryCombatEnvelope(out CombatEnvelope envelope) {
        if(envelopeShapes!=snapshot.Shapes){envelopeShapes=snapshot.Shapes;envelopeValid=CombatEnvelope.TryFromPlatform(snapshot,out combatEnvelope);}
        envelope=combatEnvelope;return envelopeValid;
    }
    private readonly CoordinateMap map;
    private readonly Dictionary<GameObject, int> originalLayers = new Dictionary<GameObject, int>();
    private readonly Dictionary<Collider2D, bool> originalColliders = new Dictionary<Collider2D, bool>();
    private bool disposed;
    private NativeDreamVoids? dreamVoids;
    private NativeRecoveryAnchor? recoveryAnchor;
    private NativeWallRunFixture? wallFixture;
    private NativeRoomEntry? roomEntry;
    private NativeTalismanTerrain? talismanTerrain;
    private readonly NativeMeleeWindows meleeWindows;
    private NativeParryContactDisplay? parryContactDisplay;
    public CoordinateMap Map => map;
    public long SourceFrame => snapshot.Frame;
    public Point[] RestPoints => snapshot.RestPoints;
    internal void CheckNativeBirthMelee(Collider2D[] receivers,int entity)=>meleeWindows.CheckNewReceiver(receivers,log,entity);
    // Manual detailed capture only: inspect the actual native ceiling query at
    // the reported Nosk face, rather than inventing a replacement jump rule.
    internal void TraceNoskCorner(Action<string> detail) {
        if(disposed || snapshot.Scene!="GG_Nosk")return;
        var b=(Bounds)HarmonyLib.AccessTools.Property(typeof(Actor),"cachedBounds").GetValue(player);
        var face=map.ToSource(new Point(b.max.x,b.min.y));
        if(Math.Abs(face.X-88)>.6f || face.Y<2.5f || face.Y>6.5f)return;
        var a=new Vector2(b.min.x+.0625f,b.max.y+.5f-.0625f);
        var z=new Vector2(b.max.x-.0625f,a.y);
        var hit=Physics2D.OverlapArea(a,z,player.solid_layer.value);
        var rayOrigin=new Vector2(b.center.x,b.max.y);
        var rays=RaycastHelper.RayCast2DWithThickness(rayOrigin,Vector2.up,1,player.solid_layer.value,b.size.x-.125f,2);
        var nativeHits=string.Join(";",rays.Take(4).Select(h=>h.collider.name+":"+h.collider.GetType().Name+":point="+h.point+":normal="+h.normal+":bounds="+h.collider.bounds));
        detail("DETAIL_NOSK_CORNER sourceRight="+face.X+" sourceFeet="+face.Y+" nativeBounds="+b+" liveBounds="+player.myCollider.bounds
            +" ceilingProbeA="+a+" ceilingProbeB="+z+" hit="+(hit==null?"none":hit.name)
            +" ownedTerrain="+(hit!=null && hit.transform.IsChildOf(root.transform))+" grounded="+player.IsOnGround+" velocity="+player.Velocity+" nativeUpRays="+nativeHits);
    }
    public void RefreshShapes(Snapshot next)
    {
        if (disposed) throw new ObjectDisposedException(nameof(GeometryLandingProbe));
        Codec.ValidateRefresh(snapshot, next);
        var replacement = new GameObject("YiGodhome Imported Terrain"); replacement.SetActive(false);
        try { Populate(replacement, next, map, LayerMask.NameToLayer("Solid")); }
        catch { UnityEngine.Object.Destroy(replacement); throw; }
        var previous = root; previous.SetActive(false); root = replacement; root.SetActive(true);
        UnityEngine.Object.Destroy(previous); Physics2D.SyncTransforms(); player.ResetNeedToCheckGroundFlag();
        snapshot.Shapes = next.Shapes;
        dreamVoids?.Refresh(next.Shapes);
        log("NS_GEOMETRY_REFRESH frame=" + next.Frame + " shapes=" + next.Shapes.Length + " playerRelocated=False");
    }

    private void Populate(GameObject target, Snapshot source, CoordinateMap coordinates, int layer)
    {
        var climbing=RoomFeatures.ClimbingWalls(source.Scene);
        var wallOffsets=climbing.Length==0?Array.Empty<float>():new float[climbing.Length];
        var wallFound=climbing.Length==0?Array.Empty<bool>():new bool[climbing.Length];
        Vector2 Mapped(Point p) {
            var v = coordinates.ToTarget(p);
            // Native positive-X contact rounds to an eighth unit. A fractional
            // left face can therefore put the ceiling overlap probe in the wall.
            if(source.Scene=="GG_Nosk" && Math.Abs(p.X-88)<.001f && p.Y>=3 && p.Y<=6)
                v=new Point(NoskPlatform.AlignNativeLeftFace(v.X),v.Y);
            return new Vector2(v.X, v.Y);
        }
        for (int i = 0; i < source.Shapes.Length; i++)
        {
            var shape = source.Shapes[i];
            if (shape.HazardId != 0) continue;
            int shapeLayer=layer;
            if(shape.OneWay) {
                int mask=player.oneway_layer.value;
                if(mask<=0 || (mask&(mask-1))!=0 || (mask&player.solid_layer.value)!=0)
                    throw new InvalidOperationException("Native one-way platform layer is ambiguous.");
                shapeLayer=0;while((1<<shapeLayer)!=mask)shapeLayer++;
            }
            var obj = new GameObject("YiGodhome Terrain " + i) { layer = shapeLayer, tag = "VRSafeGround" };
            obj.transform.SetParent(target.transform, false);
            float wallOffset=0;
            for(int wall=0;wall<climbing.Length;wall++)if(climbing[wall].Matches(shape)) {
                wallOffset=climbing[wall].NativeContactOffset(shape,coordinates);wallOffsets[wall]=wallOffset;wallFound[wall]=true;break;
            }
            Vector2[] points = RadiancePlatforms.Matches(source.Scene,shape)
                ? RadiancePlatforms.MapOutline(shape,coordinates).Select(p=>new Vector2(p.X,p.Y)).ToArray()
                : shape.Points.Select(Mapped).ToArray();
            if(wallOffset!=0)for(int point=0;point<points.Length;point++)points[point].x+=wallOffset;
            if (shape.Closed) obj.AddComponent<PolygonCollider2D>().points = points;
            else
            {
                var edge = obj.AddComponent<EdgeCollider2D>(); edge.points = points; edge.edgeRadius = shape.Radius * coordinates.Scale;
                edge.adjacentStartPoint = Mapped(shape.Start); edge.adjacentEndPoint = Mapped(shape.End);
                edge.useAdjacentStartPoint = shape.AdjacentStart; edge.useAdjacentEndPoint = shape.AdjacentEnd;
            }
        }
        if (climbing.Length > 0)
        {
            int mask = player.wallJumpDisabled_layer.value;
            if (mask <= 0 || (mask & (mask - 1)) != 0) throw new InvalidOperationException("Native wall-run layer is ambiguous.");
            int wallLayer = 0; while ((1 << wallLayer) != mask) wallLayer++;
            for(int wall=0;wall<climbing.Length;wall++)
            {
                var strip=climbing[wall];
                if (!wallFound[wall]) throw new InvalidOperationException("Climbing strip lacks matching host geometry.");
                var zone = new GameObject("YiGodhome Native Wall Run Area") { layer = wallLayer };
                zone.transform.SetParent(target.transform, false);
                zone.transform.position = Mapped(new Point(strip.ContactX + strip.Facing * 0.3f, (strip.Bottom + strip.Top) * 0.5f))+Vector2.right*wallOffsets[wall];
                var trigger = zone.AddComponent<BoxCollider2D>(); trigger.isTrigger = true;
                trigger.size = new Vector2(0.7f, strip.Top - strip.Bottom) * coordinates.Scale;
                log("NS_WALL_RUN_CONTACT scene="+source.Scene+" index="+wall+" facing="+strip.Facing+" nativeOffset="+wallOffsets[wall]+" triggerAligned=True eighthUnitContact=True");
            }
            log("NS_WALL_RUN_AREAS nativeLayer=" + wallLayer + " surfaces=" + climbing.Length + " nativeMovement=True");
        }
    }
    private bool airborne;
    private bool verified;
    private int phase;
    private int phaseFrame;
    private Vector2 wallStart;
    private float wallX;
    private float wallDirection;
    private float wallStartX;
    private int diagnosticJumps;
    private float nextJump;
    private float nextTrace;

    public GeometryLandingProbe(Action<string> log, Player player, string path, bool collisionDiagnostic = true)
    {
        this.log = log; this.player = player;
        meleeWindows = new NativeMeleeWindows(player, log);
        using (var file = new MemoryStream(Codec.ReadFileBytes(path))) snapshot = Codec.Read(file);
        if ((Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "pose-ns" || Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "input-ns")
            && snapshot.SourceSession != Guid.Parse(Environment.GetEnvironmentVariable("YIGODHOME_SESSION")))
            throw new InvalidOperationException("Geometry belongs to a different bridge session.");
        expectedFloor = player.transform.position;
        float scale = player.myCollider.bounds.size.x / snapshot.PlayerWidth;
        map = new CoordinateMap(snapshot.Floor, new Point(expectedFloor.x, expectedFloor.y), scale);
        // Validate every transformed coordinate before changing the scene.
        foreach (var s in snapshot.Shapes)
        {
            foreach (var p in s.Points) map.ToTarget(p);
            map.ToTarget(s.Start); map.ToTarget(s.End);
        }
        int solid = LayerMask.NameToLayer("Solid");
        if (solid < 0 || (player.solid_layer.value & (1 << solid)) == 0) throw new InvalidOperationException("Native Solid collision layer unavailable.");
        root = new GameObject("YiGodhome Imported Terrain");
        root.SetActive(false);
        try
        {
        Vector2 Mapped(Point p) { var v = map.ToTarget(p); return new Vector2(v.X, v.Y); }
        Populate(root, snapshot, map, solid);
        int mask = player.solid_layer.value | player.oneway_layer.value | player.onewaySlope_layer.value;
        int isolatedLayer = LayerMask.NameToLayer("Ignore Raycast");
        if (isolatedLayer < 0 || ((mask | Actor.globalSolidLayer.value) & (1 << isolatedLayer)) != 0)
            throw new InvalidOperationException("No audited excluded layer for original scene terrain.");
        int disabled = 0;
        var original = UnityEngine.Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None)
            .Where(c => c.enabled && (mask & (1 << c.gameObject.layer)) != 0 && !c.transform.IsChildOf(player.transform)).ToArray();
        // Capture shared GameObject layers before changing any collider on that object.
        foreach (var c in original)
        {
            originalLayers[c.gameObject] = c.gameObject.layer;
            originalColliders[c] = c.enabled;
        }
        foreach (var c in original)
        {
            // Native scene code can restore Collider2D.enabled after startup. Isolate its
            // collision layer as well, so reactivation cannot put original terrain back
            // into the native Actor's terrain masks. Materialize before changing shared GOs.
            log("NS_GEOMETRY_DISABLE name=" + c.name + " id=" + c.GetInstanceID() + " type=" + c.GetType().Name + " isolatedLayer=" + isolatedLayer);
            c.gameObject.layer = isolatedLayer; c.enabled = false; disabled++;
        }
        root.SetActive(true);
        Physics2D.SyncTransforms();
        talismanTerrain = new NativeTalismanTerrain(player, () => root.transform, log);
        recoveryAnchor = new NativeRecoveryAnchor(player, expectedFloor, () => root.transform, log, snapshot.Scene, map.Scale);
        if (!collisionDiagnostic)
        {
            dreamVoids = new NativeDreamVoids(player, map, log);
            dreamVoids.Refresh(snapshot.Shapes);
            foreach (var kind in new[] { PlayerStateType.ZEnter, PlayerStateType.ZExit })
            {
                var state = player.FindState(kind);
                log("NS_DOOR_STATE_AUDIT state=" + kind + " type=" + (state == null ? "none" : state.GetType().FullName));
                if (state != null)
                    foreach (var field in state.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Where(f => f.FieldType == typeof(string)))
                        log("NS_DOOR_STATE_FIELD state=" + kind + " field=" + field.Name + " value=" + field.GetValue(state));
            }
            player.Velocity = Vector2.zero;
            player.SetPhysicPosition(expectedFloor);
            player.ResetNeedToCheckGroundFlag();
            bool grounded = player.IsOnGroundCheck();
            var ground = (Collider2D?)typeof(Actor).GetField("currentGround", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.GetValue(player);
            if (!grounded || ground == null || !ground.transform.IsChildOf(root.transform))
                throw new InvalidOperationException("Room entry is not on imported native ground: scene=" + snapshot.Scene
                    + " state=" + player.CurrentStateType + " target=" + expectedFloor + " position=" + player.transform.position
                    + " canMove=" + player.CanMove + " grounded=" + grounded + " ground=" + (ground == null ? "none" : ground.name)
                    + " bounds=" + player.myCollider.bounds);
            player.SetOnGround = true;
            player.SafeGroundRecorder.ForceSave(expectedFloor);
            verified = true;
            log("NS_GEOMETRY_IMPORTED source=" + snapshot.SourceSession + " scene=" + snapshot.Scene + " frame=" + snapshot.Frame
                + " shapes=" + snapshot.Shapes.Length + " scale=" + scale + " disabledNativeColliders=" + disabled
                + " targetFloor=" + expectedFloor.ToString("F4") + " saveIO=disabled");
            log("NS_GEOMETRY_ENTRY_VERIFIED frame=" + snapshot.Frame + " nativeGround=True diagnosticRelocation=False");
            wallFixture = NativeWallRunFixture.Create(player, map, log,snapshot.Scene);
            roomEntry = new NativeRoomEntry(player, snapshot, map, log);
            return;
        }
        // Find a real transferred vertical surface with enough clear space for the native body.
        // This only chooses a test location; Actor.MoveHExact resolves the collision itself.
        var body = player.myCollider.bounds.size;
        bool wallFound = false;
        foreach (var shape in snapshot.Shapes)
        {
            int segments = shape.Closed ? shape.Points.Length : shape.Points.Length - 1;
            for (int i = 0; i < segments && !wallFound; i++)
            {
                Vector2 a = Mapped(shape.Points[i]), b = Mapped(shape.Points[(i + 1) % shape.Points.Length]);
                if (Math.Abs(a.x - b.x) > 0.001f || Math.Abs(a.y - b.y) < body.y * 3) continue;
                foreach (float side in new[] { -1f, 1f })
                {
                    var center = new Vector2(a.x + side * (body.x * 0.5f + 20), (a.y + b.y) * 0.5f);
                    if (Physics2D.OverlapBoxAll(center, new Vector2(body.x, body.y) * 1.1f, 0, mask).Any(c => !c.isTrigger)) continue;
                    wallStart = center - (Vector2)(player.myCollider.bounds.center - player.transform.position);
                    wallX = a.x; wallDirection = -side; wallFound = true; break;
                }
            }
            if (wallFound) break;
        }
        if (!wallFound) throw new InvalidOperationException("No clear transferred wall suitable for native collision test.");
        player.Velocity = Vector2.zero;
        player.SetPhysicPosition(expectedFloor + Vector2.up * 40);
        player.SetOnGround = false;
        player.ResetNeedToCheckGroundFlag();
        started = Time.realtimeSinceStartup;
        log("NS_GEOMETRY_IMPORTED source=" + snapshot.SourceSession + " scene=" + snapshot.Scene + " frame=" + snapshot.Frame
            + " shapes=" + snapshot.Shapes.Length + " scale=" + scale + " disabledNativeColliders=" + disabled
            + " targetFloor=" + expectedFloor.ToString("F4") + " saveIO=disabled");
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        dreamVoids?.Dispose();
        recoveryAnchor?.Dispose();
        wallFixture?.Dispose();
        roomEntry?.Dispose();
        talismanTerrain?.Dispose();
        if (root != null) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
        foreach (var entry in originalLayers) if (entry.Key != null) entry.Key.layer = entry.Value;
        foreach (var entry in originalColliders) if (entry.Key != null) entry.Key.enabled = entry.Value;
        Physics2D.SyncTransforms();
        if (player != null) player.ResetNeedToCheckGroundFlag();
        log("NS_GEOMETRY_RELEASED restoredLayers=" + originalLayers.Count + " restoredColliders=" + originalColliders.Count);
        originalLayers.Clear(); originalColliders.Clear();
    }

    public Bridge.Pose? Sample()
    {
        if (!verified || disposed || !NativeRoomEntry.ReadyToShow) return null;
        dreamVoids?.Update();
        var bounds = NativeRestPoint.PresentationBounds(player.myCollider);
        var foot = map.ToSource(new Point(bounds.center.x, bounds.min.y));
        var velocity = player.Velocity;
        var charging = player.CurrentStateType == PlayerStateType.ParryCounter;
        var state = charging ? player.FindState(PlayerStateType.ParryCounter) : null;
        bool counterReady = state != null && (bool)CounterPrepared.GetValue(state) && !(bool)CounterLocked.GetValue(state);
        return new Bridge.Pose(snapshot.SourceSession, snapshot.Frame, foot.X, foot.Y, velocity.x / map.Scale, velocity.y / map.Scale,
            bounds.size.x / map.Scale, bounds.size.y / map.Scale, player.IsOnGround,
            new Bridge.PlayerVitals(player.health.currentValue, player.health.maxHealth.Value, player.health.CurrentInternalInjury,
                player.chiContainer.Value, player.ammo.Value, player.potion.DrinkTimesLeft, player.health.IsDead,
                player.CurrentStateType == PlayerStateType.FooAttack || player.CurrentStateType == PlayerStateType.FooExplode, charging, counterReady,
                NativeRoomEntry.Active || NativeContentRepository.Current?.Ready==false), meleeWindows.Sample(map),
                (Bridge.EncounterProfile.ForScene(snapshot.Scene)?.Family == Bridge.EncounterProfile.NightmareGrimm || Bridge.EncounterProfile.ForScene(snapshot.Scene)?.Family == Bridge.EncounterProfile.Grimm) ? NativeBalloonReach.Height(player, map.Scale) : 0,
                NativeInputBridge.ParryDebugRequested ? (parryContactDisplay ??= new NativeParryContactDisplay(player, log)).Sample(map) : null);
    }

    public void Update()
    {
        if (disposed) return;
        roomEntry?.Update();
        if (verified)
        {
            if (Environment.GetEnvironmentVariable("YIGODHOME_MODE") == "pose-ns" && diagnosticJumps < 3
                && Time.realtimeSinceStartup >= nextJump && player.IsOnGround)
            {
                player.GroundJump(); diagnosticJumps++; nextJump = Time.realtimeSinceStartup + 1.5f;
                log("NS_POSE_NATIVE_JUMP count=" + diagnosticJumps + " frame=" + Time.frameCount);
            }
            return;
        }
        float elapsed = Time.realtimeSinceStartup - started;
        if (phase == 0 && elapsed >= nextTrace)
        {
            nextTrace = elapsed + 0.2f;
            log("NS_GEOMETRY_FALL elapsed=" + elapsed + " grounded=" + player.IsOnGround + " ground=" + (player.LastGround == null ? "none" : player.LastGround.name)
                + " position=" + player.transform.position.ToString("F4") + " colliderMin=" + player.myCollider.bounds.min.y + " velocity=" + player.Velocity.ToString("F4")
                + " focused=" + Application.isFocused + " timeScale=" + Time.timeScale);
        }
        if (elapsed > 8) throw new InvalidOperationException("Imported terrain test timed out: phase=" + phase + " position=" + player.transform.position.ToString("F4")
            + " airborneSeen=" + airborne + " grounded=" + player.IsOnGround + " ground=" + (player.LastGround == null ? "none" : player.LastGround.name)
            + " colliderMin=" + player.myCollider.bounds.min.y + " expectedFloor=" + expectedFloor.y + " focused=" + Application.isFocused + " timeScale=" + Time.timeScale);
        if (phase == 1 && Time.frameCount >= phaseFrame + 3)
        {
            wallStartX = player.transform.position.x;
            bool result = player.MoveHExact(wallDirection * 50);
            phase = 2; phaseFrame = Time.frameCount;
            log("NS_GEOMETRY_WALL_MOVE from=" + wallStartX + " wall=" + wallX + " direction=" + wallDirection + " nativeResult=" + result);
            return;
        }
        if (phase == 2 && Time.frameCount >= phaseFrame + 3)
        {
            var bounds = player.myCollider.bounds;
            float edge = wallDirection > 0 ? bounds.max.x : bounds.min.x;
            float wallError = Math.Abs(edge - wallX);
            float traveled = (player.transform.position.x - wallStartX) * wallDirection;
            if (wallError > 1.1f || traveled < 5 || traveled > 30)
                throw new InvalidOperationException("Native wall collision failed: edge=" + edge + " wall=" + wallX + " error=" + wallError + " travel=" + traveled);
            log("NS_GEOMETRY_VERIFIED airborneToGrounded=True wallBlocked=True wallError=" + wallError + " travel=" + traveled + " elapsed=" + elapsed);
            verified = true;
            player.SafeGroundRecorder.ForceSave(expectedFloor);
            nextJump = Time.realtimeSinceStartup + 1;
            player.Velocity = Vector2.zero;
            player.SetPhysicPosition(expectedFloor + Vector2.up * 40);
            player.SetOnGround = false; player.ResetNeedToCheckGroundFlag();
            return;
        }
        if (phase != 0) return;
        if (!player.IsOnGround) airborne = true;
        float error = Math.Abs(player.myCollider.bounds.min.y - expectedFloor.y);
        if (airborne && elapsed > 0.15f && player.IsOnGround && error < 1.1f)
        {
            // MoveVExact can mark a landing without refreshing LastGround. Invalidate the
            // native ground-query cache after replacing geometry, then ask the native query.
            player.ResetNeedToCheckGroundFlag();
            bool freshGrounded = player.IsOnGroundCheck();
            var ground = (Collider2D?)typeof(Actor).GetField("currentGround", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.GetValue(player);
            log("NS_GEOMETRY_GROUND_QUERY fresh=" + freshGrounded + " ground=" + (ground == null ? "none" : ground.name)
                + " id=" + (ground == null ? 0 : ground.GetInstanceID()) + " enabled=" + (ground != null && ground.enabled)
                + " active=" + (ground != null && ground.gameObject.activeInHierarchy) + " actionsEnabled=" + player.actions.Enabled);
            foreach (var nearby in Physics2D.OverlapBoxAll(new Vector2(player.myCollider.bounds.center.x, player.myCollider.bounds.min.y), new Vector2(20, 4), 0, player.solid_layer.value))
                log("NS_GEOMETRY_FOOT_OVERLAP name=" + nearby.name + " id=" + nearby.GetInstanceID() + " enabled=" + nearby.enabled + " imported=" + nearby.transform.IsChildOf(root.transform));
            if (!freshGrounded || ground == null || !ground.transform.IsChildOf(root.transform))
                throw new InvalidOperationException("Native fresh ground query did not hit imported terrain: " + (ground == null ? "none" : ground.name));
            log("NS_GEOMETRY_LANDING_VERIFIED airborneToGrounded=True ground=" + ground.name + " floorError=" + error
                + " elapsed=" + elapsed + " position=" + player.transform.position.ToString("F4") + " nativeState=" + player.CurrentStateType);
            player.Velocity = Vector2.zero;
            player.SetPhysicPosition(wallStart);
            player.SetOnGround = false; player.ResetNeedToCheckGroundFlag();
            phase = 1; phaseFrame = Time.frameCount;
        }
    }
}
