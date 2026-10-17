using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

internal sealed class NativeVisualCapture : IDisposable
{
    private readonly Player player;
    private readonly NativeRevivalDrops? revivalDrops;
    private readonly Action<string> log;
    private readonly Camera camera;
    private RenderTexture target = null!;
    private RenderTexture resolved = null!;
    private Texture2D pixels = null!;
    private readonly Dictionary<int, (RenderTexture Target, RenderTexture Resolved, Texture2D Pixels)> captureTargets = new Dictionary<int, (RenderTexture, RenderTexture, Texture2D)>();
    private readonly Dictionary<int,long> captureTargetUses=new();
    private readonly Dictionary<int,RenderTexture> effectMattes=new();
    private bool thunderMatteLogged,revivalMatteLogged;
    private int revivalGeneration,revivalAllocations;
    private long revivalBytes,revivalEvicted;
    private float revivalPressureUntil;
    private void RevivalPressure()
    {
        if(!arrowLayer)return;
        int generation=NativeRevivalDrops.SpawnGeneration(player);
        bool start=generation!=revivalGeneration;
        if(start) {
            revivalGeneration=generation;revivalAllocations=endpoint.PixelAllocations;
            revivalBytes=endpoint.AllocatedPixelBytes;revivalEvicted=endpoint.EvictedPixelBytes;
            revivalPressureUntil=Time.realtimeSinceStartup+2;
        }
        if(!start && (revivalPressureUntil==0 || Time.realtimeSinceStartup<revivalPressureUntil))return;
        log("NS_REVIVAL_WORLD_PRESSURE stage="+(start?"start":"settled")+" generation="+generation
            +" newBuffers="+(endpoint.PixelAllocations-revivalAllocations)+" newBytes="+(endpoint.AllocatedPixelBytes-revivalBytes)
            +" evictedBytes="+(endpoint.EvictedPixelBytes-revivalEvicted)+" retainedBytes="+endpoint.RetainedPixelBytes
            +" outstanding="+endpoint.OutstandingPixels+" frame="+Time.frameCount);
        if(!start)revivalPressureUntil=0;
    }
    private readonly Dictionary<int,int> captureTargetHits=new();
    private long targetUse;
    private int targetAllocations,asyncQueueSkips;
    private readonly ExpandedCaptureHold expandedHold=new();
    private bool expandedPrepared;
    private bool HomingSelected=>player.weaponDataCollection.currentWeapon?.weaponPrefab.name.StartsWith("Chasing Arrow Shooter",StringComparison.Ordinal)==true;
    private int stressFrames,stressWarmAllocations,stressWarmGc;
    private double stressMs,stressMax;
    private bool stressStarted;
    private readonly NativeHudReadback[] actorReadback;
    private long lastPublishedActorFrame=-1;
    private bool AdmitActorFrame(long frame) {if(frame<=lastPublishedActorFrame)return false;lastPublishedActorFrame=frame;return true;}
    private readonly string directory;
    private readonly VisualEndpoint endpoint;
    private readonly Guid session;
    private readonly bool separateArrows, arrowLayer;
    private bool arrowPartitionActive;
    private int arrowPartitionSamples;
    private float nextCapture;
    private int thunderAuditSamples;
    private float nextThunderAudit;
    private int thunderSnapshots;
    private float nextThunderSnapshot;
    private readonly HashSet<Renderer> thunderAuditSeen=new();
    private int captures;
    private int attackSnapshots;
    private bool disposed;
    private readonly bool effects;
    private readonly bool saveSnapshots;
    private int side, captureHeight;
    private int densitySamples;
    internal static int ExpandedBowSamples;
    private readonly HashSet<string> effectSamples = new HashSet<string>();
    private readonly Renderer[] attached;
    private readonly FxPlayer[] effectSources;
    private readonly FooAttachable[] targetAttachments;
    private int lastDeposit, depositFrame, depositSnapshots;
    private bool projectileSnapshot;
    private int flightSnapshots;
    private float nextFlightSnapshot;
    private int warningSnapshots;
    private bool lastRecovery, recoveryCovered;
    private float recoveryTraceUntil, nextRecoveryTrace;
    private bool recoveryTraceKnown, tracedRecovery;
    private PlayerStateType tracedRecoveryState;
    private int recoverySnapshots;
    private long returnSnapshot = -1;
    private readonly HashSet<string> gestureSnapshots=new();
    private readonly List<Renderer> ownedArrowRenderers = new List<Renderer>();
    private readonly HashSet<string> excludedEffects = new HashSet<string>();
    private readonly List<Material> materialScratch = new List<Material>();
    private readonly Dictionary<Shader,bool> shaderEligibility = new Dictionary<Shader,bool>();
    private readonly Dictionary<GameObject,int> captureLayers = new Dictionary<GameObject,int>();
    private readonly List<Renderer> spawnedScratch = new List<Renderer>(), projectileScratch = new List<Renderer>(), warningScratch = new List<Renderer>(), selectedScratch = new List<Renderer>(), childRenderers = new List<Renderer>();
    private readonly List<FxPlayer> fxFirst = new List<FxPlayer>(), fxSecond = new List<FxPlayer>(), childFx = new List<FxPlayer>();
    private readonly HashSet<PoolObject> poolSeen = new HashSet<PoolObject>();
    private readonly HashSet<FxPlayer> fxSeen = new HashSet<FxPlayer>();
    private readonly HashSet<Renderer> candidateSeen = new HashSet<Renderer>(), selectedSeen = new HashSet<Renderer>();
    private long selectorGeometry = -1;
    private int selectorChecks, selectorKinds;
    private double captureTotalMs, captureMaxMs;
    private double readbackTotalMs;
    private int captureWindowCount;
    private const int CaptureLayer = 30;
    private readonly NativePauseAudit? pauseAudit;
    private readonly NativeEffectPause effectPause = new NativeEffectPause();
    public long CapturedGeometry { get; private set; } = -1;
    private long entryTraceGeometry = -1;
    private float entryTraceUntil, entryTraceNext;
    private PlayerStateType damagePreviousState;
    private float damageTraceUntil, damageTraceNext;
    private int damageTraceSamples;
    private int bodyCropSamples;
    private long sourceEntryGeometry=-1;
    private float sourceEntryUntil;
    private int sourceEntryMask=-1,sourceEntrySamples;
    private bool? initialOpaque;
    private int initialVisibilityChanges;
    public Func<Guid>? DetailedCaptureId;
    private Guid damageCaptureId;
    public Func<IEnumerable<Renderer>>? AdditionalRenderers;
    // Explicit native boss-warning ownership, separate from Yi/gesture art.
    public Func<IEnumerable<Renderer>>? WorldRenderers;
    private readonly HashSet<Renderer> worldWarnings=new();
    public Func<long>? AdditionalContentSequence, AdditionalContentFrame;
    public NativeVisualCapture(Player player, VisualEndpoint endpoint, Guid session, Action<string> log, MonsterBase? targetOwner = null, bool separateArrows = false, bool arrowLayer = false)
    {
        if (arrowLayer && !separateArrows) throw new ArgumentException("Arrow layer requires separate ownership.");
        this.separateArrows = separateArrows; this.arrowLayer = arrowLayer;
        actorReadback=new[]{new NativeHudReadback(endpoint,log,"actor",AdmitActorFrame),new NativeHudReadback(endpoint,log,"actor",AdmitActorFrame)};
        this.player = player; this.log = log; this.endpoint = endpoint; this.session = session;
        if(!arrowLayer)revivalDrops=new NativeRevivalDrops(player,log);
        ExpandedBowSamples=0;
        if (!arrowLayer && SessionConfiguration.Current.Has("pause-probe")) pauseAudit = new NativePauseAudit(log);
        directory = Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
        effects = YiGodhome.Bridge.SessionConfiguration.Current.Has("effects-probe"); side = effects ? 512 : 256;
        saveSnapshots = YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures;
        attached = player.GetComponentsInChildren<Renderer>(true);
        if (SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe")
            && EncounterProfile.Selected.Family == EncounterProfile.NightmareGrimm) ShaderHeapControl();
        targetAttachments = targetOwner == null ? Array.Empty<FooAttachable>() : targetOwner.GetComponentsInChildren<FooAttachable>(true);
        var targetEffects = targetAttachments.Select(f => f.fooFXPlayer).Where(f => f != null).ToArray();
        effectSources = player.GetComponentsInChildren<FxPlayer>(true).Concat(targetEffects).Distinct().ToArray();
        if (!arrowLayer && UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Any(r => r.gameObject.layer == CaptureLayer))
            throw new InvalidOperationException("Diagnostic capture layer is already occupied.");
        var obj = new GameObject("YiGodhome Native Capture Camera");
        camera = obj.AddComponent<Camera>(); camera.enabled = false;
        camera.orthographic = true; camera.orthographicSize = 80; camera.aspect = 1;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        camera.nearClipPlane = 0.1f; camera.farClipPlane = 500; camera.cullingMask = 1 << CaptureLayer;
        camera.allowHDR = false; camera.allowMSAA = false; camera.useOcclusionCulling = false;
        SelectResolution(side);
        if(effects && separateArrows && !arrowLayer && HollowSols.Foundation.NativeSaveContext.Enabled) {
            int originalSide=side,originalHeight=captureHeight;
            foreach(int size in new[]{512,768,1024}) {
                SelectResolution(size,size);endpoint.PreparePixels(size*size*4);
            }
            SelectResolution(originalSide,originalHeight);
            log("NS_ACTOR_ENTRY_TARGETS_PREPARED sizes=512,768,1024 noImagePublished=True nativeTimingUnchanged=True frame="+Time.frameCount);
        }
        if((!separateArrows || arrowLayer) && (HomingSelected || HollowSolsMenuPlugin.Instance?.FixtureRadianceHoming==true))PrepareExpandedCapture("native-entry");
        log("NS_VISUAL_PRIMARY name=" + player.PlayerSprite.name + " active=" + player.PlayerSprite.gameObject.activeInHierarchy
            + " sprite=" + (player.PlayerSprite.sprite == null ? "none" : player.PlayerSprite.sprite.name)
            + " bounds=" + player.PlayerSprite.bounds + " position=" + player.PlayerSprite.transform.position);
        foreach (var renderer in player.GetComponentsInChildren<Renderer>(true))
            log("NS_VISUAL_RENDERER name=" + renderer.name + " type=" + renderer.GetType().Name + " enabled=" + renderer.enabled
                + " layer=" + renderer.gameObject.layer + " bounds=" + renderer.bounds.ToString()
                + " shaders=" + string.Join(",", renderer.sharedMaterials.Where(m => m != null).Select(m => m.shader.name)));
        log("NS_VISUAL_READY width=" + side + " height=" + side + " nativeMaterials=True supersample=2 effects=" + effects + " targetFps=" + (effects ? 144 : 30));
    }

    private void PrepareExpandedCapture(string source) {
        if(expandedPrepared || !HollowSols.Foundation.NativeSaveContext.Enabled)return;
        long tick=System.Diagnostics.Stopwatch.GetTimestamp();int oldSide=side,oldHeight=captureHeight;
        SelectResolution(VisualCodec.MaxSide,VisualCodec.MaxSide);
        endpoint.PreparePixels(VisualCodec.MaxSide*VisualCodec.MaxSide*4);
        SelectResolution(oldSide,oldHeight);expandedPrepared=true;
        log(FormattableString.Invariant($"NS_EXPANDED_CAPTURE_PREPARED source={source} ms={(System.Diagnostics.Stopwatch.GetTimestamp()-tick)*1000d/System.Diagnostics.Stopwatch.Frequency:F3} pixelAllocations={endpoint.PixelAllocations} frame={Time.frameCount} entryHeld={NativeRoomEntry.Active} hostPaused={NativeInputBridge.HostPaused} nativeUi={NativeMenu.Open} noImagePublished=True"));
    }
    private void SelectResolution(int requested, int height=0)
    {
        if(height==0)height=requested;
        int key=requested*4096+height;
        if (!captureTargets.TryGetValue(key, out var cached))
        {
            // Resolve a 2x capture before CPU readback. The wire image dimensions
            // remain unchanged while each output pixel averages four samples.
            var render = new RenderTexture(requested * 2, height * 2, 24, RenderTextureFormat.ARGB32);
            render.filterMode = FilterMode.Bilinear;
            render.Create();
            var resolve = new RenderTexture(requested, height, 0, RenderTextureFormat.ARGB32);
            resolve.Create();
            cached = (render, resolve, new Texture2D(requested, height, TextureFormat.RGBA32, false));
            // Keep the original three square targets and at most three expanded shapes.
            if(captureTargets.Count>=6) {
                // Keep recurring resolutions instead of allowing uncommon crop
                // transitions to evict the hot idle/bow targets. Capacity and
                // pending GPU-source protection remain unchanged.
                int retiredKey = -1, fewestHits = int.MaxValue; long oldestUse = long.MaxValue;
                foreach (var entry in captureTargets) {
                    bool pending = false;
                    effectMattes.TryGetValue(entry.Key, out var entryMatte);
                    foreach (var readback in actorReadback) if (readback.Source == entry.Value.Resolved
                        || entryMatte != null && readback.MatteSource == entryMatte) { pending = true; break; }
                    if (pending) continue;
                    int hits = captureTargetHits[entry.Key]; long used = captureTargetUses[entry.Key];
                    if (hits < fewestHits || hits == fewestHits && used < oldestUse) {
                        retiredKey = entry.Key; fewestHits = hits; oldestUse = used;
                    }
                }
                if (retiredKey < 0) throw new InvalidOperationException("All native capture targets are owned by pending readbacks.");
                var retired = captureTargets[retiredKey]; captureTargets.Remove(retiredKey);
                captureTargetUses.Remove(retiredKey); captureTargetHits.Remove(retiredKey);
                retired.Target.Release(); retired.Resolved.Release();
                UnityEngine.Object.Destroy(retired.Target); UnityEngine.Object.Destroy(retired.Resolved); UnityEngine.Object.Destroy(retired.Pixels);
                if (effectMattes.TryGetValue(retiredKey, out var matte)) { effectMattes.Remove(retiredKey); matte.Release(); UnityEngine.Object.Destroy(matte); }
            }
            captureTargets.Add(key, cached); captureTargetHits.Add(key, 0);
            targetAllocations++;
        }
        captureTargetUses[key]=++targetUse;
        if (captureTargetHits[key] < int.MaxValue) captureTargetHits[key]++;
        side = requested; captureHeight=height; target = cached.Target; resolved = cached.Resolved; pixels = cached.Pixels; camera.targetTexture = target;
    }

    private readonly NativeDepositArtwork depositArtwork=new();

    public void Capture(Bridge.Pose pose)
    {
        RevivalPressure();
        foreach(var readback in actorReadback)readback.Observe(pose.GeometryFrame);
        if(!arrowLayer) {
            if(sourceEntryGeometry!=pose.GeometryFrame) {sourceEntryGeometry=pose.GeometryFrame;sourceEntryUntil=Time.realtimeSinceStartup+2;sourceEntryMask=-1;sourceEntrySamples=0;}
            if(Time.realtimeSinceStartup<sourceEntryUntil && sourceEntrySamples<12) {
                var nativeSprite=player.PlayerSprite;
                int mask=(nativeSprite.enabled?1:0)|(nativeSprite.gameObject.activeInHierarchy?2:0)
                    |(nativeSprite.forceRenderingOff?4:0)|(nativeSprite.color.a>.001f?8:0)|(nativeSprite.sprite!=null?16:0);
                if(mask!=sourceEntryMask) {
                    sourceEntryMask=mask;sourceEntrySamples++;
                    log("NS_ACTOR_ENTRY_SOURCE geometry="+pose.GeometryFrame+" nativeFrame="+Time.frameCount+" state="+player.CurrentStateType
                        +" enabled="+nativeSprite.enabled+" active="+nativeSprite.gameObject.activeInHierarchy+" forceOff="+nativeSprite.forceRenderingOff
                        +" alpha="+nativeSprite.color.a+" flasher="+player.spriteFlasher.fade+" sprite="+(nativeSprite.sprite?.name??"none")
                        +" gesture="+NativeGesturePresentation.FrameKey+" scopedSeconds=2");
                }
            }
        }
        if((!separateArrows || arrowLayer) && !expandedPrepared && HomingSelected && (NativeMenu.Open || NativeInputBridge.HostPaused || player.IsSitAtSavePoint))PrepareExpandedCapture("native-ui");
        Guid detailId=DetailedCaptureId?.Invoke()??Guid.Empty;
        if(detailId!=damageCaptureId) {damageCaptureId=detailId;damageTraceSamples=bodyCropSamples=0;damageTraceUntil=0;damagePreviousState=default;}
        var captureState=player.CurrentStateType;
        if(detailId!=Guid.Empty && captureState==PlayerStateType.Hurt && damagePreviousState!=captureState) damageTraceUntil=Time.time+1;
        damagePreviousState=captureState;
        bool inspectDamagePixels=detailId!=Guid.Empty && damageTraceSamples<24 && Time.time<damageTraceUntil;
        bool canAsync=!saveSnapshots && !NativeRoomEntry.Active && NativeGesturePresentation.FrameKey.Length==0
            && !player.TouchTraped && player.CurrentStateType!=PlayerStateType.Respawn && !lastRecovery
            && Time.realtimeSinceStartup>=recoveryTraceUntil
            && (HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed!=true || HollowSolsMenuPlugin.Instance.FixtureActorAsync)
            && !inspectDamagePixels;
        // Keep the channel's existing latest-image/drop policy, but avoid
        // rendering frames that would only replace an already queued image.
        // Completed GPU requests are drained above; native simulation is not gated.
        if(canAsync && !endpoint.CanQueuePixels){asyncQueueSkips++;return;}
        NativeHudReadback? asyncReadback=null;
        if(canAsync) {
            foreach(var readback in actorReadback)if(readback.Supported && !readback.Busy){asyncReadback=readback;break;}
            if(asyncReadback==null && actorReadback.Any(r=>r.Supported))return;
        }
        else if(actorReadback.Any(r=>r.Busy))return;
        if (Time.realtimeSinceStartup < nextCapture) return;
        float interval = 1f / (effects ? 144 : 30);
        nextCapture += interval;
        if (nextCapture < Time.realtimeSinceStartup) nextCapture = Time.realtimeSinceStartup + interval;
        long captureTick = System.Diagnostics.Stopwatch.GetTimestamp();
        var body = NativeRestPoint.PresentationBounds(player.myCollider);
        float scale = body.size.x / pose.Width;
        if (scale <= 0 || float.IsNaN(scale)) throw new InvalidOperationException("Invalid native visual scale.");
        var previousTarget = RenderTexture.active;
        var layers = captureLayers; layers.Clear();
        ownedArrowRenderers.Clear();
        var spawned = spawnedScratch; var projectiles = projectileScratch; var warnings = warningScratch; var selected = selectedScratch;
        spawned.Clear(); projectiles.Clear(); warnings.Clear(); selected.Clear(); candidateSeen.Clear(); selectedSeen.Clear();worldWarnings.Clear();
        if(effects) {
            CollectSpawnedRenderers();
            foreach(var renderer in ownedArrowRenderers)if(Eligible(renderer))projectiles.Add(renderer);
            foreach(var renderer in AdditionalRenderers?.Invoke() ?? Array.Empty<Renderer>())if(Eligible(renderer,true))warnings.Add(renderer);
            foreach(var renderer in WorldRenderers?.Invoke() ?? Array.Empty<Renderer>())if(Eligible(renderer,true) && worldWarnings.Add(renderer))warnings.Add(renderer);
            SelectRenderers(attached); SelectRenderers(spawned); SelectRenderers(projectiles);
            foreach(var attachment in targetAttachments) {
                var attachedDeposit=attachment==null?null:attachment.bindingFoo;
                if(attachedDeposit!=null && attachedDeposit.HasFoo()) SelectRenderers(depositArtwork.Get(attachedDeposit));
            }
            foreach(var renderer in warnings)if(selectedSeen.Add(renderer))selected.Add(renderer);
        } else selected.Add(player.PlayerSprite);
        if (separateArrows)
        {
            int worldCount = 0;
            for (int i = selected.Count - 1; i >= 0; i--)
            {
                bool world = WorldArrow(selected[i]);
                if (world) worldCount++;
                if (world != arrowLayer) selected.RemoveAt(i);
            }
            if ((worldCount > 0) != arrowPartitionActive && arrowPartitionSamples++ < 20)
                log("NS_NATIVE_ARROW_PARTITION frame=" + Time.frameCount + " geometry=" + pose.GeometryFrame
                    + " worldLayer=" + arrowLayer + " selected=" + selected.Count + " detachedArrowRenderers=" + worldCount
                    + " bossWarningRenderers="+worldWarnings.Count+" actorMotion=" + !arrowLayer);
            arrowPartitionActive = worldCount > 0;
            if (arrowLayer && selected.Count == 0)
            {
                var empty = endpoint.RentFrame(4); Array.Clear(empty.Rgba, 0, 4);
                empty.Session = session; empty.GeometryFrame = pose.GeometryFrame; empty.NativeFrame = Time.frameCount;
                empty.CaptureTick = captureTick; empty.Width = empty.Height = 1; empty.WorldWidth = empty.WorldHeight = 1;
                if (AdmitActorFrame(empty.NativeFrame)) endpoint.PublishPixels(empty); else empty.ReleasePixels();
                CapturedGeometry = pose.GeometryFrame;
                return;
            }
            // Actor's sizing hold must not be driven by excluded arrows.
            if (!arrowLayer) projectiles.Clear();
        }
        int selectorKind = warnings.Count>0 ? 8 : projectiles.Count>0 ? 4 : spawned.Count>0 ? 2 : 1;
        if(selectorGeometry!=pose.GeometryFrame) { selectorGeometry=pose.GeometryFrame; selectorKinds=0; }
        if(effects && SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("trace-probe")
            && EncounterProfile.Selected.Family==EncounterProfile.NightmareGrimm && selectorChecks<16 && (selectorKinds&selectorKind)==0) {
            selectorKinds|=selectorKind; selectorChecks++;
            var oldSpawned=SpawnedRenderers().ToArray();
            var oldProjectiles=ownedArrowRenderers.Where(r=>Eligible(r)).ToArray();
            var oldWarnings=(AdditionalRenderers?.Invoke() ?? Array.Empty<Renderer>()).Concat(WorldRenderers?.Invoke()??Array.Empty<Renderer>()).Where(r=>Eligible(r,true)).Distinct().ToArray();
            var deposits=targetAttachments.Where(f=>f!=null && f.bindingFoo!=null && f.bindingFoo.HasFoo()).SelectMany(f=>f.bindingFoo.GetComponentsInChildren<Renderer>(true));
            var oldSelected=attached.Concat(oldSpawned).Concat(oldProjectiles).Concat(deposits).Distinct().Where(r=>Eligible(r)).Concat(oldWarnings).Distinct().Where(r=>!separateArrows || WorldArrow(r)==arrowLayer).ToArray();
            // The control rediscovers arrow leases; compare distinct identities
            // because both paths intentionally retain repeated renderers there.
            if(!oldSelected.SequenceEqual(selected) || !oldWarnings.SequenceEqual(warnings)
                || (!separateArrows || arrowLayer) && !oldProjectiles.Distinct().SequenceEqual(projectiles.Distinct()))
                throw new InvalidOperationException("Capture scratch changed native renderer selection/order.");
            log("NS_VISUAL_SELECTION_VERIFIED geometry="+pose.GeometryFrame+" kind="+selectorKind+" selected="+selected.Count+" warnings="+warnings.Count+" arrows="+projectiles.Count+" sameNativeOrder=True boundedClosure=True");
        }
        effectPause.Apply(selected);
        var spriteBounds = player.PlayerSprite.bounds;
        var captureBounds = arrowLayer ? selected[0].bounds : spriteBounds;
        foreach (var renderer in selected) if (renderer != player.PlayerSprite) captureBounds.Encapsulate(renderer.bounds);
        float halfExtent = Mathf.Clamp(Mathf.Max(captureBounds.extents.x, captureBounds.extents.y) + 4, 16, effects ? 256 : 80);
        float sourceWorldSize = halfExtent * 2 / scale;
        float desiredPixels = sourceWorldSize * 96;
        int requestedSide = effects ? (desiredPixels > 768 ? 1024 : desiredPixels > 512 ? 768 : 512) : 256;
        int requestedHeight=requestedSide;
        float halfWidth=halfExtent;
        // Preserve the existing capture below its original limit. The later
        // 480-unit arrow reach needs more pixels, not a wider 1024px square.
        if(effects && desiredPixels>1024) {
            float x=Mathf.Clamp(captureBounds.extents.x+4,16,256), y=Mathf.Clamp(captureBounds.extents.y+4,16,256);
            int Tier(float n)=>n>2048?2560:n>1536?2048:n>1024?1536:n>768?1024:n>512?768:512;
            requestedSide=Tier(x*2/scale*96);requestedHeight=Tier(y*2/scale*96);
            float aspect=(float)requestedSide/requestedHeight;
            halfExtent=Math.Max(y,x/aspect);halfWidth=halfExtent*aspect;
        }
        // Arrow travel repeatedly crosses exact crop tiers. Retain peak sizes
        // briefly instead of allocating/destroying huge images at every crossing.
        // Expand the view with the retained pixels, keeping native density.
        if((arrowLayer || HollowSolsMenuPlugin.Instance?.FixtureCaptureStressRequested!=true) && !stressStarted) {
            bool homing=HomingSelected;
            var held=expandedHold.Choose(pose.GeometryFrame,Time.time,projectiles.Count>0,requestedSide,requestedHeight,halfWidth,halfExtent,homing && (!separateArrows || arrowLayer)?VisualCodec.MaxSide:0);
            requestedSide=held.Width;requestedHeight=held.Height;halfWidth=held.HalfWidth;halfExtent=held.HalfHeight;
        }
        bool stress=false;
        if(!arrowLayer && (stressStarted || HollowSolsMenuPlugin.Instance?.FixtureCaptureStressRequested==true) && stressFrames<150) {
            stress=true;stressStarted=true;
            var widths=new[]{768,1024,1536,1536,2048,2048,1536,2560,2560,2560};
            var heights=new[]{768,1024,768,1024,2048,1536,1536,2048,1536,2560};
            int index=stressFrames<10?stressFrames:8+(stressFrames%2);
            requestedSide=widths[index];requestedHeight=heights[index];
            float aspect=(float)requestedSide/requestedHeight;halfExtent=Math.Max(halfExtent,halfWidth/aspect);halfWidth=halfExtent*aspect;
        }
        if (side != requestedSide || captureHeight!=requestedHeight) SelectResolution(requestedSide,requestedHeight);
        else {
            int key = side*4096+captureHeight; captureTargetUses[key]=++targetUse;
            if (captureTargetHits[key] < int.MaxValue) captureTargetHits[key]++;
        }
        bool effectSnapshot = effects && selected.Count > 1 && effectSamples.Count < 12 && effectSamples.Add(player.CurrentStateType.ToString());
        bool thunderMatte=false,revivalMatte=false;
        if(arrowLayer)foreach(var part in selected) {
            if(NativeRevivalDrops.Owns(player,part))revivalMatte=true;
            else if(NativePlayerArrow.IsThunder(part,player))thunderMatte=true;
        }
        // RGB-only/additive glow does not carry useful transparent-target alpha.
        // Reuse native black/white renders rather than changing orb opacity or
        // applying straight-alpha division to unrelated material output alpha.
        bool nativeEffectMatte=thunderMatte || revivalMatte;
        RenderTexture? whiteTarget = null;
        if (nativeEffectMatte)
        {
            int key = side * 4096 + captureHeight;
            if (!effectMattes.TryGetValue(key, out whiteTarget))
            {
                whiteTarget = new RenderTexture(side, captureHeight, 0, RenderTextureFormat.ARGB32); whiteTarget.Create();
                effectMattes.Add(key, whiteTarget);
            }
            if(thunderMatte && !thunderMatteLogged) { thunderMatteLogged=true;log("NS_THUNDER_NATIVE_MATTES unchangedMaterials=True recoverRgbCoverage=True nativeWorldPlacement=True async="+(asyncReadback!=null)); }
            if(revivalMatte && !revivalMatteLogged) { revivalMatteLogged=true;log("NS_REVIVAL_NATIVE_MATTES unchangedMaterials=True recoverRgbCoverage=True nativeWorldPlacement=True async="+(asyncReadback!=null)); }
        }
        VisualFrame? matteBlack = null, matteWhite = null;
        var captureCenter = captureBounds.center;
        if (!arrowLayer) {
            captureCenter.x = VisualCrop.KeepOwner(captureCenter.x, halfWidth, spriteBounds.min.x, spriteBounds.max.x);
            captureCenter.y = VisualCrop.KeepOwner(captureCenter.y, halfExtent, spriteBounds.min.y, spriteBounds.max.y);
        }
        if (captureCenter != captureBounds.center && bodyCropSamples < 16)
        {
            bodyCropSamples++;
            log("NS_NATIVE_BODY_CROP_CORRECTED frame=" + Time.frameCount + " geometry=" + pose.GeometryFrame
                + " state=" + player.CurrentStateType + " body=" + spriteBounds + " requestedCenter=" + captureBounds.center
                + " actualCenter=" + captureCenter + " halfWidth=" + halfWidth + " halfHeight=" + halfExtent
                + " selected=" + selected.Count + " arrows=" + projectiles.Count + " async=" + (asyncReadback != null));
        }
        try
        {
            foreach (var renderer in selected)
                if (renderer.enabled && renderer.gameObject.activeInHierarchy && !layers.ContainsKey(renderer.gameObject))
                { layers.Add(renderer.gameObject, renderer.gameObject.layer); renderer.gameObject.layer = CaptureLayer; }
            camera.aspect=(float)side/captureHeight;
            camera.orthographicSize = halfExtent;
            camera.transform.position = new Vector3(captureCenter.x, captureCenter.y, -100);
            camera.backgroundColor = nativeEffectMatte ? Color.black : Color.clear;
            camera.Render(); Graphics.Blit(target, resolved); RenderTexture.active = resolved;
            if(asyncReadback==null || stress && !asyncReadback.VerifiedSize(side,captureHeight)) {
                long readbackTick = System.Diagnostics.Stopwatch.GetTimestamp();
                pixels.ReadPixels(new Rect(0, 0, side, captureHeight), 0, 0, false);
                NativeReadbackVerification.Verify(pixels, "actor", log);
                readbackTotalMs += (System.Diagnostics.Stopwatch.GetTimestamp() - readbackTick) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            }
            if (nativeEffectMatte)
            {
                if (asyncReadback == null)
                {
                    matteBlack = endpoint.RentFrame(side * captureHeight * 4);
                    pixels.GetRawTextureData<byte>().CopyTo(matteBlack.Rgba);
                }
                camera.backgroundColor = Color.white;
                camera.Render(); Graphics.Blit(target, whiteTarget!);
                if (asyncReadback == null)
                {
                    matteWhite = endpoint.RentFrame(side * captureHeight * 4);
                    RenderTexture.active = whiteTarget;
                    pixels.ReadPixels(new Rect(0, 0, side, captureHeight), 0, 0, false);
                    pixels.GetRawTextureData<byte>().CopyTo(matteWhite.Rgba);
                }
            }
        }
        catch { matteBlack?.ReleasePixels(); matteWhite?.ReleasePixels(); throw; }
        finally { camera.backgroundColor = Color.clear; RenderTexture.active = previousTarget; foreach (var pair in layers) if (pair.Key != null) pair.Key.layer = pair.Value; layers.Clear(); }
        if (nativeEffectMatte)
        {
            var black = matteBlack ?? endpoint.RentFrame(side * captureHeight * 4);
            Describe(black,pose,captureTick,scale,body,halfWidth,false,0);
            if (asyncReadback != null) asyncReadback.RequestMattes(resolved,whiteTarget!,black,null);
            else if (AdmitActorFrame(black.NativeFrame)) endpoint.PublishMattePixels(black,matteWhite!);
            else { black.ReleasePixels(); matteWhite!.ReleasePixels(); }
            CapturedGeometry = pose.GeometryFrame; captures++;
            double matteMs = (System.Diagnostics.Stopwatch.GetTimestamp()-captureTick)*1000d/System.Diagnostics.Stopwatch.Frequency;
            captureTotalMs += matteMs; captureMaxMs = Math.Max(captureMaxMs,matteMs); captureWindowCount++;
            ReportCosts(camera.orthographicSize*2/scale);
            return;
        }
        var captured = endpoint.RentFrame(side * captureHeight * 4);
        if(asyncReadback==null)pixels.GetRawTextureData<byte>().CopyTo(captured.Rgba);
        else {
            Describe(captured,pose,captureTick,scale,body,halfWidth,false,0);
            byte[]? reference=stress && !asyncReadback.VerifiedSize(side,captureHeight)?pixels.GetRawTextureData<byte>().ToArray():null;
            asyncReadback.Request(resolved,captured,0,captureHeight,false,reference);
            CapturedGeometry=pose.GeometryFrame;captures++;
            double asyncMs=(System.Diagnostics.Stopwatch.GetTimestamp()-captureTick)*1000d/System.Diagnostics.Stopwatch.Frequency;
            RecordStress(stress,asyncMs,true);
            captureTotalMs+=asyncMs;captureMaxMs=Math.Max(captureMaxMs,asyncMs);captureWindowCount++;
            ReportCosts(camera.orthographicSize*2/scale);
            return;
        }
        if(NativeRoomEntry.Active && NativeGesturePresentation.FrameKey.StartsWith("ArmInspection",StringComparison.Ordinal)) {
            bool opaque=ActorPixels.Visible(captured.Rgba);
            if(initialOpaque!=opaque && initialVisibilityChanges++<16) {
                initialOpaque=opaque;
                log("NS_INITIAL_ACTOR_PIXELS opaque="+opaque+" frame="+Time.frameCount+" gesture="+NativeGesturePresentation.SpriteName
                    +" selected="+selected.Count+" nativeSpriteHidden="+player.PlayerSprite.forceRenderingOff);
            }
            if(!opaque && HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed==true)
                throw new InvalidOperationException("Native arm inspection produced transparent actor pixels.");
        }
        if(SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("markoth-projectile-probe")
            && thunderAuditSamples<32 && Time.time>=nextThunderAudit) {
            nextThunderAudit=Time.time+.1f;
            thunderAuditSeen.Clear();
            foreach(var arrowRenderer in ownedArrowRenderers) {
                if(arrowRenderer==null || !arrowRenderer.enabled || !arrowRenderer.gameObject.activeInHierarchy || !thunderAuditSeen.Add(arrowRenderer)
                    || !NativePlayerArrow.IsThunder(arrowRenderer,player)) continue;
                if(thunderAuditSamples++>=32) break;
                var b=arrowRenderer.bounds;
                bool inside=b.min.x>=captureCenter.x-halfWidth && b.max.x<=captureCenter.x+halfWidth
                    && b.min.y>=captureCenter.y-halfExtent && b.max.y<=captureCenter.y+halfExtent;
                log("NS_THUNDER_RENDERER frame="+Time.frameCount+" renderer="+arrowRenderer.name+" enabled="+arrowRenderer.enabled
                    +" active="+arrowRenderer.gameObject.activeInHierarchy+" selected="+selected.Contains(arrowRenderer)+" insideCapture="+inside
                    +" nativeBounds="+b+" shader="+arrowRenderer.sharedMaterial?.shader?.name+" captureSide="+side+" nativeWorldSize="+(halfExtent*2));
            }
        }
        if(saveSnapshots && SessionConfiguration.Current.Has("markoth-projectile-probe") && thunderSnapshots<4 && Time.time>=nextThunderSnapshot
            && projectiles.Any(r=>r!=null && r.sharedMaterial?.shader?.name=="BlendModes/SpriteDefault/Grab" && NativePlayerArrow.IsThunder(r,player))) {
            nextThunderSnapshot=Time.time+.12f;
            string path=Path.Combine(directory,"yi-thunder-grab-"+thunderSnapshots++ +".png");
            File.WriteAllBytes(path,pixels.EncodeToPNG());
            log("NS_THUNDER_NATIVE_CAPTURE frame="+Time.frameCount+" nativeShader=True cornerAlpha="+captured.Rgba[3]+","+captured.Rgba[side*4-1]
                +","+captured.Rgba[(captureHeight-1)*side*4+3]+","+captured.Rgba[side*captureHeight*4-1]+" path="+path);
        }

        if(SessionConfiguration.Current.RunFixtures || detailId!=Guid.Empty) {
            bool bodyInside=spriteBounds.min.x>=captureCenter.x-halfWidth && spriteBounds.max.x<=captureCenter.x+halfWidth
                && spriteBounds.min.y>=captureCenter.y-halfExtent && spriteBounds.max.y<=captureCenter.y+halfExtent;
            if(!arrowLayer && !bodyInside && bodyCropSamples<16) {
                bodyCropSamples++;
                // Inspect only the already-owned selected closure. A far effect
                // can move the bounded camera crop without changing sprite alpha.
                log("NS_NATIVE_BODY_CROP frame="+Time.frameCount+" geometry="+pose.GeometryFrame+" state="+player.CurrentStateType
                    +" sourceEnabled="+player.PlayerSprite.enabled+" sourceActive="+player.PlayerSprite.gameObject.activeInHierarchy
                    +" body="+spriteBounds+" cropCenter="+captureBounds.center+" halfExtent="+halfExtent+" selected="+selected.Count
                    +" contributors="+string.Join(";",selected.Where(r=>r!=player.PlayerSprite).Take(4).Select(r=>r.GetInstanceID()+":"+r.name+":"+r.bounds))
                    +" detailCapture="+detailId+" bodyInside=False additionalReadback=False");
            }
            var state=player.CurrentStateType;

            if(damageTraceSamples<24 && Time.time<damageTraceUntil && Time.time>=damageTraceNext) {
                damageTraceSamples++;damageTraceNext=Time.time+.1f;
                var damageSprite=player.PlayerSprite;
                // Whole-frame opacity can be supplied by hurt particles even
                // when Yi's body is absent. Inspect its native projected bounds
                // in the existing readback; never add another render/readback.
                int x0=Mathf.Clamp(Mathf.FloorToInt((spriteBounds.min.x-camera.transform.position.x+halfWidth)*side/(halfWidth*2)),0,side);
                int x1=Mathf.Clamp(Mathf.CeilToInt((spriteBounds.max.x-camera.transform.position.x+halfWidth)*side/(halfWidth*2)),0,side);
                int y0=Mathf.Clamp(Mathf.FloorToInt((spriteBounds.min.y-camera.transform.position.y+halfExtent)*captureHeight/(halfExtent*2)),0,captureHeight);
                int y1=Mathf.Clamp(Mathf.CeilToInt((spriteBounds.max.y-camera.transform.position.y+halfExtent)*captureHeight/(halfExtent*2)),0,captureHeight);
                int bodyRegionOpaque=0;
                for(int y=y0;y<y1;y++) for(int x=x0;x<x1;x++)
                    if(captured.Rgba[(y*side+x)*4+3]>=128) bodyRegionOpaque++;
                log("NS_DAMAGE_VISIBILITY frame="+Time.frameCount+" state="+state+" sourceEnabled="+damageSprite.enabled
                    +" sourceActive="+damageSprite.gameObject.activeInHierarchy+" sourceColor="+damageSprite.color+" sprite="+(damageSprite.sprite?.name??"none")
                    +" capturedOpaque="+ActorPixels.Visible(captured.Rgba)+" nativeFlasherFade="+player.spriteFlasher.fade
                    +" captureSide="+side+" nativeWorldSize="+(halfExtent*2)+" bodyPixelsX="+(spriteBounds.size.x*side/(halfWidth*2))
                    +" bodyPixelsY="+(spriteBounds.size.y*captureHeight/(halfExtent*2))+" bodyRegionOpaquePixels="+bodyRegionOpaque
                    +" selectedRenderers="+selected.Count+" detailCapture="+detailId);
            }
        }
        if (saveSnapshots)
        {
            if (entryTraceGeometry != pose.GeometryFrame)
            { entryTraceGeometry = pose.GeometryFrame; entryTraceUntil = Time.realtimeSinceStartup + 3; entryTraceNext = 0; }
            if (Time.realtimeSinceStartup <= entryTraceUntil && Time.realtimeSinceStartup >= entryTraceNext)
            {
                entryTraceNext = Time.realtimeSinceStartup + .1f;
                var bodySprite = player.PlayerSprite;
                log("NS_ENTRY_PIXELS geometry=" + pose.GeometryFrame + " nativeFrame=" + Time.frameCount
                    + " state=" + player.CurrentStateType + " renderer=" + bodySprite.enabled
                    + " active=" + bodySprite.gameObject.activeInHierarchy + " color=" + bodySprite.color
                    + " sprite=" + (bodySprite.sprite == null ? "none" : bodySprite.sprite.name)
                    + " opaque=" + ActorPixels.Visible(captured.Rgba));
            }
        }
        pauseAudit?.Observe(player, selected, projectiles.Count, pose, captured.Rgba);
        double costMs = (System.Diagnostics.Stopwatch.GetTimestamp() - captureTick) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        RecordStress(stress,costMs,false);
        captureTotalMs += costMs; captureMaxMs = Math.Max(captureMaxMs, costMs);
        captureWindowCount++;
        float size = camera.orthographicSize * 2 / scale;
        float worldWidth=halfWidth*2/scale;
        if(projectiles.Count>0 && (side>1024 || captureHeight>1024) && HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed==true) {
            float density=captureHeight/size;
            if(density<95.9f)throw new InvalidOperationException("Bow capture lost the original 96px/world-unit density: "+density);
            if(++ExpandedBowSamples<=4) {
                log("NS_BOW_DENSITY_PASS width="+side+" height="+captureHeight+" worldWidth="+worldWidth+" worldHeight="+size+" pixelsPerUnit="+density+" nativeArrows="+projectiles.Count);
                File.WriteAllBytes(Path.Combine(directory,"bow-density-"+ExpandedBowSamples+".png"),pixels.EncodeToPNG());
            }
            if(ExpandedBowSamples==3)HollowSolsMenuPlugin.Instance!.FixtureBowDensitySatisfied();
        }
        if (player.CurrentStateType == PlayerStateType.ShootArrow && densitySamples++ < 24)
            log("NS_VISUAL_DENSITY state=" + player.CurrentStateType + " frame=" + Time.frameCount + " side=" + side
                + " worldSize=" + size.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + " pixelsPerUnit=" + (captureHeight / size).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        bool recovering = player.TouchTraped || player.CurrentStateType == PlayerStateType.Respawn;
        float fade = 0;
        if (recovering)
        {
            var cover = GameCore.Instance.blackCover;
            if (cover == null) throw new InvalidOperationException("Native trap recovery cover unavailable.");
            if (cover.isActiveAndEnabled) fade = cover.color.a;
        }
        if (recovering || lastRecovery) recoveryTraceUntil = Time.realtimeSinceStartup + 1;
        bool recoveryTraceActive = recovering || Time.realtimeSinceStartup < recoveryTraceUntil;
        if (recoveryTraceActive && (!recoveryTraceKnown || tracedRecovery != recovering || tracedRecoveryState != player.CurrentStateType
            || Time.realtimeSinceStartup >= nextRecoveryTrace))
        {
            recoveryTraceKnown = true; tracedRecovery = recovering; tracedRecoveryState = player.CurrentStateType;
            nextRecoveryTrace = Time.realtimeSinceStartup + .1f;
            log("NS_RECOVERY_FRAME frame=" + Time.frameCount + " geometry=" + pose.GeometryFrame + " recovering=" + recovering
                + " fade=" + fade.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " state=" + player.CurrentStateType
                + " sprite=" + (player.PlayerSprite.sprite == null ? "none" : player.PlayerSprite.sprite.name)
                + " x=" + pose.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " y=" + pose.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            var animation = player.animator.GetCurrentAnimatorStateInfo(0);
            log("NS_RECOVERY_ANIMATION frame=" + Time.frameCount + " requestedRespawnState=" + animation.IsName("RespawnGetUp")
                + " normalizedTime=" + animation.normalizedTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + " clips=" + string.Join(",", player.animator.GetCurrentAnimatorClipInfo(0).Select(c => c.clip.name)));
        }
        if (!recoveryTraceActive) recoveryTraceKnown = false;
        if (recovering && !lastRecovery) recoveryCovered = false;
        recoveryCovered |= fade >= 0.99f;
        if (saveSnapshots && recoveryCovered && player.CurrentStateType == PlayerStateType.Respawn && fade < 0.5f && recoverySnapshots < 2)
        { File.WriteAllBytes(Path.Combine(directory, "yi-recovery-" + recoverySnapshots++ + ".png"), pixels.EncodeToPNG()); }
        lastRecovery = recovering;
        if((saveSnapshots || HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed==true) && NativeGesturePresentation.FrameKey.Length>0 && gestureSnapshots.Count<24 && gestureSnapshots.Add(NativeGesturePresentation.FrameKey)) {
            log("NS_NATIVE_GESTURE_CAPTURE key="+NativeGesturePresentation.FrameKey+" sprite="+NativeGesturePresentation.SpriteName+" geometry="+pose.GeometryFrame+" controlHeld="+NativeRoomEntry.Active);
            if(saveSnapshots || HollowSolsMenuPlugin.Instance?.FixtureGestureScreenshots==true)
                File.WriteAllBytes(Path.Combine(directory,"yi-gesture-"+NativeGesturePresentation.FrameKey+".png"),pixels.EncodeToPNG());
        }
        if (NativeRoomEntry.LyingReturn && returnSnapshot != pose.GeometryFrame && ActorPixels.Visible(captured.Rgba)) {
            returnSnapshot = pose.GeometryFrame;
            log("NS_RETURN_POSE_CAPTURED geometry=" + pose.GeometryFrame + " nativeFrame=" + Time.frameCount + " state=" + player.CurrentStateType
                + " sprite=" + (player.PlayerSprite.sprite == null ? "none" : player.PlayerSprite.sprite.name) + " gesture="+NativeGesturePresentation.SpriteName+" renderer=" + player.PlayerSprite.enabled + " fade=" + fade + " opaquePixels=True");
            if (saveSnapshots) File.WriteAllBytes(Path.Combine(directory, "yi-return-" + pose.GeometryFrame + ".png"), pixels.EncodeToPNG());
        }
        Describe(captured,pose,captureTick,scale,body,halfWidth,recovering,fade);
        if(AdmitActorFrame(captured.NativeFrame))endpoint.PublishPremultipliedPixels(captured);else captured.ReleasePixels();
        CapturedGeometry = pose.GeometryFrame;
        captures++;
        if (saveSnapshots && captures == 90) VerifyResolve();
        if (captures % 30 == 0)
        {
            var art = player.PlayerSprite.sprite;
            var artSize = player.PlayerSprite.bounds.size;
            log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "NS_RASTER_SAMPLE frame={0} sprite={1} side={2} worldSize={3:R} nativeRect={4} nativePpu={5} filter={6} capturePerNativeX={7:R} capturePerNativeY={8:R} originX={9:R} originY={10:R} vx={11:R} vy={12:R} renderers={13}",
                Time.frameCount, art == null ? "none" : art.name, side, size, art == null ? Rect.zero : art.rect,
                art == null ? 0 : art.pixelsPerUnit, art == null ? "none" : art.texture.filterMode.ToString(),
                art == null ? 0 : artSize.x * side / (halfWidth * 2 * art.rect.width),
                art == null ? 0 : artSize.y * captureHeight / (halfExtent * 2 * art.rect.height),
                (spriteBounds.min.x - camera.transform.position.x + halfWidth) * side / (halfWidth * 2),
                (spriteBounds.min.y - camera.transform.position.y + halfExtent) * captureHeight / (halfExtent * 2), pose.VelocityX, pose.VelocityY, selected.Count));
        }
        if (warnings.Count > 0 && warningSnapshots < 4 && captures % 6 == 0)
        {
            if (saveSnapshots) File.WriteAllBytes(Path.Combine(directory, "yi-crimson-" + warningSnapshots + ".png"), pixels.EncodeToPNG());
            log("NS_VISUAL_CRIMSON sample=" + warningSnapshots++ + " frame=" + Time.frameCount + " renderers=" + string.Join(",", warnings.Select(r => r.name)));
        }
        var deposit = targetAttachments.Where(f => f != null && f.bindingFoo != null && f.bindingFoo.HasFoo()).Select(f => f.bindingFoo).FirstOrDefault();
        if (deposit == null) { lastDeposit = 0; depositSnapshots = 0; }
        else {
            if (lastDeposit != deposit.GetInstanceID()) { lastDeposit = deposit.GetInstanceID(); depositFrame = Time.frameCount; depositSnapshots = 0; }
            if (Time.frameCount - depositFrame >= 6 + depositSnapshots * 20 && depositSnapshots < 3) {
                var owned = deposit.GetComponentsInChildren<Renderer>(true);
                log("NS_VISUAL_TALISMAN attached=True position=" + deposit.transform.position + " selected=" + string.Join(",", owned.Where(r => selected.Contains(r)).Select(r => r.name)));
                if (saveSnapshots) File.WriteAllBytes(Path.Combine(directory, "yi-talisman-" + lastDeposit + "-" + depositSnapshots + ".png"), pixels.EncodeToPNG());
                depositSnapshots++;
            }
        }
        if (saveSnapshots && effects && player.CurrentStateType == PlayerStateType.Attack && attackSnapshots < 8)
        {
            File.WriteAllBytes(Path.Combine(directory, "yi-attack-" + attackSnapshots + ".png"), pixels.EncodeToPNG());
            log("NS_VISUAL_ATTACK sample=" + attackSnapshots++ + " frame=" + Time.frameCount + " renderers=" + string.Join(",", selected.Select(r => r.name)));
        }
        if (!projectileSnapshot && projectiles.Count > 0)
        {
            projectileSnapshot = true;
            string path = Path.Combine(directory, "yi-projectile-" + pose.GeometryFrame + ".png");
            if (saveSnapshots) File.WriteAllBytes(path, pixels.EncodeToPNG());
            log("NS_VISUAL_PROJECTILE ownedByYi=True renderers=" + string.Join(",", projectiles.Select(r => r.name)) + " path=" + (saveSnapshots ? path : "disabled-for-physical-input"));
        }
        // A muzzle capture alone cannot establish that a homing flight remains
        // visible after its shooter's pool borrow has ended. Inspect only the
        // already-proven active arrow leases in the bounded homing fixture.
        if (saveSnapshots && SessionConfiguration.Current.Has("homing-probe")
            && flightSnapshots < 8 && Time.realtimeSinceStartup >= nextFlightSnapshot) {
            var flights = NativePlayerArrow.Owned(player).Where(a =>
                a.GetComponentsInChildren<Renderer>(true).Any(r => projectiles.Contains(r))
                && Vector2.Distance(a.transform.position, player.transform.position) > 100).ToArray();
            if (flights.Length > 0) {
                string path = Path.Combine(directory, "yi-flight-" + pose.GeometryFrame + "-" + flightSnapshots++ + ".png");
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                foreach (var a in flights) log("NS_VISUAL_ARROW_FLIGHT owner=" + a.GetInstanceID()
                    + " sourceActive=" + (a.lastPlayerComponent != null && a.lastPlayerComponent.gameObject.activeInHierarchy)
                    + " position=" + a.transform.position + " player=" + player.transform.position
                    + " renderers=" + string.Join(",", a.GetComponentsInChildren<Renderer>(true).Where(r => projectiles.Contains(r)).Select(r => r.name))
                    + " geometry=" + pose.GeometryFrame + " path=" + path);
                nextFlightSnapshot = Time.realtimeSinceStartup + .12f;
            }
        }
        if (effectSnapshot)
        {
            string path = Path.Combine(directory, "yi-effects-" + pose.GeometryFrame + "-" + player.CurrentStateType + ".png");
            if (saveSnapshots) File.WriteAllBytes(path, pixels.EncodeToPNG());
            log("NS_VISUAL_EFFECTS state=" + player.CurrentStateType + " renderers=" + string.Join(",", selected.Select(r => r.name)) + " worldSize=" + size + " path=" + (saveSnapshots ? path : "disabled-for-physical-input"));
        }
        ReportCosts(size);
        if (captures == 1 || captures == 90 || captures == 210 || captures == 240)
        {
            var colors = pixels.GetPixels32(); int visible = 0, clear = 0; long rgb = 0;
            foreach (var c in colors) { if (c.a > 0) visible++; else clear++; rgb += c.r + c.g + c.b; }
            if (saveSnapshots) File.WriteAllBytes(Path.Combine(directory, "yi-capture-" + captures + ".png"), pixels.EncodeToPNG());
            log("NS_VISUAL_CAPTURE count=" + captures + " frame=" + Time.frameCount + " opaquePixels=" + visible + " clearPixels=" + clear + " rgb=" + rgb + " state=" + player.CurrentStateType);
        }
    }

    private void Describe(VisualFrame captured,Bridge.Pose pose,long captureTick,float scale,Bounds body,float halfWidth,bool recovering,float fade) {
        float worldWidth=halfWidth*2/scale;
        float size=camera.orthographicSize*2/scale;
        captured.Session = session; captured.NativeFrame = Time.frameCount; captured.CaptureTick = captureTick;
        captured.ContentSequence = AdditionalContentSequence?.Invoke() ?? 0;
        captured.ContentFrame = captured.ContentSequence > 0 ? AdditionalContentFrame?.Invoke() ?? 0 : 0;
        captured.GeometryFrame = pose.GeometryFrame; captured.Width = side; captured.Height = captureHeight;
        captured.Recovering = recovering; captured.ScreenFade = fade;
        captured.X = pose.X + (camera.transform.position.x - body.center.x) / scale - worldWidth / 2;
        captured.Y = pose.Y + (camera.transform.position.y - body.min.y) / scale - size / 2;
        captured.WorldWidth = worldWidth;captured.WorldHeight = size;
        captured.AnchorX = pose.X; captured.AnchorY = pose.Y;
        captured.Motion = arrowLayer ? null : pose;
    }
    private bool WorldArrow(Renderer renderer) => renderer != player.PlayerSprite
        && (NativeRevivalDrops.Owns(player,renderer) || worldWarnings.Contains(renderer) || !renderer.transform.IsChildOf(player.transform) && NativePlayerArrow.Owner(renderer, player) != null);
    private void ReportCosts(float size) {
        if (captures % 90 == 0) {
            log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "NS_VISUAL_COST samples={0} meanMs={1:F3} maxMs={2:F3} worldSize={3:R} readbackMeanMs={4:F3} frame={5} pixelAllocations={6} retainedPixelBytes={7} targetAllocations={8} queueSkipped={9} allocatedPixelBytes={10} evictedPixelBytes={11}", captureWindowCount, captureTotalMs / captureWindowCount, captureMaxMs, size, readbackTotalMs / captureWindowCount, Time.frameCount,endpoint.PixelAllocations,endpoint.RetainedPixelBytes,targetAllocations,asyncQueueSkips,endpoint.AllocatedPixelBytes,endpoint.EvictedPixelBytes));
            captureWindowCount = 0; captureTotalMs = captureMaxMs = readbackTotalMs = 0;
        }
    }
    private void RecordStress(bool stress,double costMs,bool asynchronous) {
        if(!stress)return;
            stressFrames++;
            if(stressFrames==50){stressWarmAllocations=endpoint.PixelAllocations;stressWarmGc=GC.CollectionCount(2);}
            if(stressFrames>50){stressMs+=costMs;stressMax=Math.Max(stressMax,costMs);}
            if(stressFrames==150) {
                int allocations=endpoint.PixelAllocations-stressWarmAllocations;
                if(allocations>2 || endpoint.RetainedPixelBytes>128L*1024*1024)throw new InvalidOperationException("Expanded actor pool still churns after warmup: "+allocations);
                log("NS_CAPTURE_STRESS_PASS frames=100 dimensions=2560x1536,2560x2560 newPixelBuffers="+allocations+" gc2="+(GC.CollectionCount(2)-stressWarmGc)
                    +" meanCaptureMs="+(stressMs/100).ToString("F3")+" maxCaptureMs="+stressMax.ToString("F3")+" retainedPixelBytes="+endpoint.RetainedPixelBytes+" nativePixels=True densityUnchanged=True asynchronous="+asynchronous+" exactAsyncPixels="+actorReadback.All(r=>r.Verified));
                HollowSolsMenuPlugin.Instance!.FixtureCaptureStressComplete();
            }
    }

    private bool Eligible(Renderer renderer, bool warning = false)
    {
        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) return false;
        if (renderer == player.PlayerSprite) return true;
        if (renderer.name.IndexOf("LIGHTMASK", StringComparison.OrdinalIgnoreCase) >= 0 || renderer.bounds.size.sqrMagnitude < 0.001f
            || Vector2.Distance(renderer.bounds.center, player.PlayerSprite.bounds.center) > (warning || ownedArrowRenderers.Contains(renderer) ? 480 : 192)
            || renderer.bounds.extents.x > 128 || renderer.bounds.extents.y > 128) return false;
        materialScratch.Clear(); renderer.GetSharedMaterials(materialScratch);
        bool eligible = materialScratch.Count > 0;
        foreach (var material in materialScratch) {
            var shader = material == null ? null : material.shader;
            if (shader == null || !AllowedShader(shader)) { eligible=false; break; }
        }
        if (!eligible && excludedEffects.Count < 48 && excludedEffects.Add(renderer.name)) log("NS_VISUAL_EFFECT_EXCLUDED name=" + renderer.name
            + " shaders=" + string.Join(",", materialScratch.Where(m => m != null).Select(m => m.shader.name)));
        materialScratch.Clear();
        return eligible;
    }

    private void SelectRenderers(System.Collections.Generic.IReadOnlyList<Renderer> candidates)
    {
        for(int i=0;i<candidates.Count;i++) {
            var renderer=candidates[i];
            if(candidateSeen.Add(renderer) && Eligible(renderer) && selectedSeen.Add(renderer))selectedScratch.Add(renderer);
        }
    }
    private void CollectSpawnedRenderers()
    {
        poolSeen.Clear(); fxFirst.Clear(); fxSecond.Clear();
        try {
            NativeRevivalDrops.Collect(player,spawnedScratch);
            foreach(var source in effectSources)if(source!=null)fxFirst.Add(source);
            // Live arrows can outlast their pooled shooter. Their secondary FX
            // are independent pool objects, rather than transform descendants.
            var arrows=NativePlayerArrow.Owned(player);
            foreach(var arrow in arrows) {
                childFx.Clear();arrow.GetComponentsInChildren(true,childFx);
                foreach(var fx in childFx)if(fx!=null && !fxFirst.Contains(fx))fxFirst.Add(fx);
            }
            var current=fxFirst; var next=fxSecond;
            for(int depth=0;depth<8 && current.Count!=0;depth++) {
                next.Clear(); fxSeen.Clear();
                foreach(var source in current)foreach(var spawned in source.AllObjects) {
                    if(spawned==null || !spawned.isOnScene || !spawned.gameObject.activeInHierarchy
                        || spawned.lastPlayerComponent!=source || !poolSeen.Add(spawned))continue;
                    if(poolSeen.Count>128)return;
                    childRenderers.Clear(); spawned.GetComponentsInChildren(true,childRenderers);
                    if(NativePlayerArrow.Owner(spawned,player)!=null)ownedArrowRenderers.AddRange(childRenderers);
                    spawnedScratch.AddRange(childRenderers);
                    childFx.Clear(); spawned.GetComponentsInChildren(true,childFx);
                    foreach(var child in childFx)if(fxSeen.Add(child))next.Add(child);
                }
                var swap=current; current=next; next=swap;
            }
            foreach(var arrow in arrows) {
                childRenderers.Clear(); arrow.GetComponentsInChildren(true,childRenderers);
                ownedArrowRenderers.AddRange(childRenderers);
                if(poolSeen.Add(arrow))spawnedScratch.AddRange(childRenderers);
            }
        } finally { poolSeen.Clear(); fxSeen.Clear(); fxFirst.Clear(); fxSecond.Clear(); childRenderers.Clear(); childFx.Clear(); }
    }

    private bool AllowedShader(Shader shader)
    {
        if(shaderEligibility.TryGetValue(shader,out bool allowed))return allowed;
        string name=shader.name;
        allowed=name=="Sprites/Default" || name=="Particles/Standard Unlit" || name=="Mobile/Particles/Alpha Blended" || name=="Mobile/Particles/Additive"
            || name=="Custom/SpriteWithHighLight" || name=="2DxFX/Standard/ColorRGB"
            || name=="AllIn1SpriteShader/AllIn1SpriteShader" || name=="2DxFX/AL/DestroyedFX" || name=="BlendModes/SpriteDefault/Grab";
        if(shaderEligibility.Count>=128)shaderEligibility.Clear();
        shaderEligibility.Add(shader,allowed); return allowed;
    }
    private static bool LegacyShader(Shader shader) => shader.name=="Sprites/Default" || shader.name=="Particles/Standard Unlit"
        || shader.name=="Mobile/Particles/Alpha Blended" || shader.name=="Mobile/Particles/Additive"
        || shader.name=="Custom/SpriteWithHighLight" || shader.name=="2DxFX/Standard/ColorRGB"
        || shader.name=="AllIn1SpriteShader/AllIn1SpriteShader" || shader.name=="2DxFX/AL/DestroyedFX" || shader.name=="BlendModes/SpriteDefault/Grab";
    private void ShaderHeapControl()
    {
        var shader=player.PlayerSprite.sharedMaterial?.shader; if(shader==null)return;
        const int count=8192; bool expected=LegacyShader(shader);
        if(AllowedShader(shader)!=expected)throw new InvalidOperationException("Shader whitelist changed.");
        int collections=GC.CollectionCount(0); long before=GC.GetTotalMemory(false);
        for(int i=0;i<count;i++)if(LegacyShader(shader)!=expected)throw new InvalidOperationException("Legacy shader identity changed.");
        long middle=GC.GetTotalMemory(false);
        for(int i=0;i<count;i++)if(AllowedShader(shader)!=expected)throw new InvalidOperationException("Cached shader identity changed.");
        long after=GC.GetTotalMemory(false);
        log("NS_SHADER_HEAP_CONTROL calls="+count+" legacyProcessHeapDeltaBytes="+(middle-before)+" currentProcessHeapDeltaBytes="+(after-middle)
            +" gc="+(GC.CollectionCount(0)-collections)+" equalDecisions=True nativeAsset=True startupOnly=True");
    }

    private IEnumerable<Renderer> SpawnedRenderers()
    {
        var revival=new List<Renderer>();NativeRevivalDrops.Collect(player,revival);
        foreach(var renderer in revival)yield return renderer;
        var seen = new HashSet<PoolObject>();
        var sources = effectSources.Where(f => f != null).ToArray();
        for (int depth = 0; depth < 3 && sources.Length != 0; depth++)
        {
            var next = new List<FxPlayer>();
            foreach (var source in sources)
                foreach (var spawned in source.AllObjects)
                {
                    if (spawned == null || !spawned.isOnScene || !spawned.gameObject.activeInHierarchy
                        || spawned.lastPlayerComponent != source || !seen.Add(spawned)) continue;
                    if (seen.Count > 128) yield break;
                    var renderers = spawned.GetComponentsInChildren<Renderer>(true);
                    // Resolve each flight borrow while its native shooter is
                    // still alive. The exact pool lease survives that shooter.
                    if (NativePlayerArrow.Owner(spawned, player) != null) ownedArrowRenderers.AddRange(renderers);
                    foreach (var renderer in renderers) yield return renderer;
                    next.AddRange(spawned.GetComponentsInChildren<FxPlayer>(true));
                }
            sources = next.Distinct().ToArray();
        }
        foreach (var arrow in NativePlayerArrow.Owned(player)) {
            var renderers = arrow.GetComponentsInChildren<Renderer>(true);
            ownedArrowRenderers.AddRange(renderers);
            if (seen.Add(arrow)) foreach (var renderer in renderers) yield return renderer;
        }
    }

    public void RetireCombatEffects()
    {
        revivalDrops?.Clear();
        // Same bounded native ownership closure as capture; never sweep the
        // service scene or reset Yi's resources to dispose a projectile.
        var owned = new HashSet<PoolObject>();
        var sources = effectSources.Where(f => f != null).ToArray();
        for (int depth = 0; depth < 3 && sources.Length != 0; depth++) {
            var next = new List<FxPlayer>();
            foreach (var source in sources)
                foreach (var obj in source.AllObjects.ToArray()) {
                    if (obj == null || !obj.isOnScene || obj.lastPlayerComponent != source || !owned.Add(obj)) continue;
                    if (owned.Count > 128) throw new InvalidOperationException("Sequence effect retirement exceeded capture ownership budget.");
                    if (obj.GetComponent<MonsterBase>() != null) throw new InvalidOperationException("Player effect retirement encountered a native actor.");
                    next.AddRange(obj.GetComponentsInChildren<FxPlayer>(true));
                }
            sources = next.Distinct().ToArray();
        }
        foreach (var arrow in NativePlayerArrow.Owned(player)) owned.Add(arrow);
        if (owned.Count > 128) throw new InvalidOperationException("Arrow retirement exceeded native ownership budget.");
        foreach (var obj in owned.Reverse()) if (obj != null && obj.isOnScene) obj.ReturnToPool();
        if (owned.Any(obj => obj != null && obj.isOnScene)) throw new InvalidOperationException("Native sequence effect retirement failed.");
        log("NS_SEQUENCE_EFFECTS_RETIRED owners=" + owned.Count + " nativePool=True playerReset=False");
    }

    private void VerifyResolve()
    {
        var previous = RenderTexture.active;
        var high = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        try
        {
            RenderTexture.active = target;
            high.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); high.Apply(false, false);
            var source = high.GetRawTextureData<Color32>(); var output = pixels.GetRawTextureData<Color32>();
            int edges = 0; float maximum = 0;
            for (int y = 0; y < captureHeight; y++) for (int x = 0; x < side; x++)
            {
                int first = y * 2 * target.width + x * 2;
                float expected = (source[first].a + source[first + 1].a + source[first + target.width].a + source[first + target.width + 1].a) * 0.25f;
                maximum = Mathf.Max(maximum, Mathf.Abs(output[y * side + x].a - expected));
                if (expected > 0 && expected < 255) edges++;
            }
            if (edges == 0 || maximum > 2) throw new InvalidOperationException("Supersample alpha resolve mismatch: max=" + maximum + " edges=" + edges);
            log("NS_VISUAL_RESOLVE_VERIFIED sourceSide=" + target.width + " outputSide=" + side + " fractionalAlpha=" + edges + " maxAlphaError=" + maximum);
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(high); }
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        revivalDrops?.Dispose();
        foreach(var readback in actorReadback)readback.Dispose();
        effectPause.Dispose(); depositArtwork.Clear();
        if (camera != null) { camera.targetTexture = null; UnityEngine.Object.Destroy(camera.gameObject); }
        foreach (var cached in captureTargets.Values)
        { cached.Target.Release(); cached.Resolved.Release(); UnityEngine.Object.Destroy(cached.Target); UnityEngine.Object.Destroy(cached.Resolved); UnityEngine.Object.Destroy(cached.Pixels); }
        captureTargets.Clear();captureTargetUses.Clear();
        foreach (var matte in effectMattes.Values) { matte.Release(); UnityEngine.Object.Destroy(matte); }
        effectMattes.Clear();
        materialScratch.Clear(); shaderEligibility.Clear();
        captureLayers.Clear(); spawnedScratch.Clear(); projectileScratch.Clear(); warningScratch.Clear(); selectedScratch.Clear(); ownedArrowRenderers.Clear();
        childRenderers.Clear(); childFx.Clear(); fxFirst.Clear(); fxSecond.Clear(); poolSeen.Clear(); fxSeen.Clear(); candidateSeen.Clear(); selectedSeen.Clear();
        log("NS_VISUAL_RELEASED nativeCapture=True");
    }
}
