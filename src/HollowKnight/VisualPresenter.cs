using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// Composite native pixels in the real scene so existing foreground depth still applies.
internal sealed class VisualPresenter : IDisposable
{
    private Texture2D? texture;
    private Sprite? sprite;
    private SpriteRenderer? renderer;
    private Material? material;
    private long sequence;
    private sealed class Image {internal Texture2D Texture=null!;internal Sprite Sprite=null!;internal long Used;}
    private readonly Dictionary<int,Image> actorImages=new();
    private long imageUse,retainedImageBytes;
    private int imageCreates;
    private void BindImage(int width,int height) {
        if(!actor && !worldEffects) {
            if(sprite!=null)UnityEngine.Object.Destroy(sprite);if(texture!=null)UnityEngine.Object.Destroy(texture);
            texture=new Texture2D(width,height,TextureFormat.RGBA32,false);
            texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;
            sprite=Sprite.Create(texture,new Rect(0,0,width,height),Vector2.zero,1,0,SpriteMeshType.FullRect);return;
        }
        int key=width*4096+height;
        if(!actorImages.TryGetValue(key,out var image)) {
            long bytes=(long)width*height*4;
            while(actorImages.Count>=6 || retainedImageBytes+bytes>64L*1024*1024) {
                var retired=actorImages.OrderBy(p=>p.Value.Used).First();actorImages.Remove(retired.Key);
                retainedImageBytes-=(long)retired.Value.Texture.width*retired.Value.Texture.height*4;
                UnityEngine.Object.Destroy(retired.Value.Sprite);UnityEngine.Object.Destroy(retired.Value.Texture);
            }
            image=new Image {Texture=new Texture2D(width,height,TextureFormat.RGBA32,false)};
            image.Texture.wrapMode=TextureWrapMode.Clamp;image.Texture.filterMode=FilterMode.Bilinear;
            image.Sprite=Sprite.Create(image.Texture,new Rect(0,0,width,height),Vector2.zero,1,0,SpriteMeshType.FullRect);
            actorImages.Add(key,image);retainedImageBytes+=bytes;
            log((worldEffects ? "HK_ARROW_TEXTURE_CACHE created=" : "HK_ACTOR_TEXTURE_CACHE created=")+(++imageCreates)+" width="+width+" height="+height+" retainedBytes="+retainedImageBytes+" maximumBytes=67108864");
        }
        image.Used=++imageUse;texture=image.Texture;sprite=image.Sprite;
    }
    private string waterScene="";
    private Renderer[] waterForeground=Array.Empty<Renderer>();
    private readonly Action<string> log;
    private readonly bool victoryHold;
    private readonly bool actor;
    private readonly bool aura;
    private readonly bool behindActor;
    private readonly bool worldEffects;
    private readonly Dictionary<HealthManager, Renderer[]> bodies = new();
    private readonly List<HealthManager> liveBodies = new(32), removedBodies = new(32);
    private readonly HashSet<HealthManager> liveBodySet = new();
    private readonly Dictionary<Renderer, (int Layer, int Order)> bodyBands = new();
    internal static SpriteRenderer? ActorRenderer { get; private set; }
    internal static SpriteRenderer? RootRenderer { get; private set; }
    private string? lastOrder;
    private bool actorOrderKnown;
    private float lastActorDepth;
    private int lastActorLayer, lastActorOrder, lastActorBodies;
    internal void RefreshOrder()
    {
        if (actor && visible && renderer != null && HeroController.instance != null) {
            var p = renderer.transform.position; OrderActor(p.x,p.y);
        }
    }
    internal void BindBodies(IReadOnlyList<HealthManager>? known)
    {
        if (!actor && !aura) return;
        liveBodies.Clear();liveBodySet.Clear();removedBodies.Clear();
        for (int i = 0; known != null && i < known.Count; i++) {
            var body = known[i];
            if(body==null)continue;
            if(liveBodies.Count==32)break;
            liveBodies.Add(body);liveBodySet.Add(body);
        }
        foreach(var body in bodies.Keys)if(body==null || !liveBodySet.Contains(body))removedBodies.Add(body!);
        foreach (var old in removedBodies) {
            foreach (var part in bodies[old]) RestoreBand(part);
            bodies.Remove(old);
        }
        foreach (var body in liveBodies) {
            bool knownBody=bodies.TryGetValue(body,out var parts), invalid=false;
            if(parts!=null)foreach(var part in parts)if(part==null){invalid=true;break;}
            // Dormant pooled health owners frequently have artwork on children
            // and no eligible root renderer. Probe both admitted native types
            // without allocating a component array/filter every rendered frame.
            // A renderer added during native initialization is seen immediately.
            bool emptyChanged=knownBody && parts!.Length==0
                && (body.GetComponent<MeshRenderer>()!=null || body.GetComponent<SpriteRenderer>()!=null);
            if (!knownBody || invalid || emptyChanged) {
                bodies[body] = body.GetComponents<Renderer>()
                    .Where(r => (r is MeshRenderer || r is SpriteRenderer) && !r.name.StartsWith("YiGodhome", StringComparison.Ordinal)
                        && r.GetComponentInParent<HealthManager>() == body).Take(32).ToArray();
                if (parts == null && bodies[body].Length == 0) log("HK_ACTOR_ORDER_PENDING entity=" + body.GetInstanceID() + " renderers=" + body.GetComponentsInChildren<Renderer>(true).Length + " nativeInitializationPending=True");
                foreach (var part in bodies[body]) {
                    // Markoth's dream sprite uses an elevated sorting band. Copying
                    // it onto Yi bypasses foreground occlusion. Keep both combat
                    // bodies in the native hero band and order them by depth.
                    var heroBand = HeroController.instance?.GetComponent<Renderer>();
                    if (actor && (EncounterProfile.Selected.Family == EncounterProfile.Markoth || EncounterProfile.Selected.Family==EncounterProfile.Marmu || EncounterProfile.Selected.Family==EncounterProfile.Xero) && heroBand != null && !bodyBands.ContainsKey(part)) {
                        bodyBands.Add(part, (part.sortingLayerID, part.sortingOrder));
                        part.sortingLayerID = heroBand.sortingLayerID; part.sortingOrder = heroBand.sortingOrder;
                        log("HK_MARKOTH_COMBAT_SORT_BAND renderer=" + part.GetInstanceID() + " layer=" + part.sortingLayerID + " order=" + part.sortingOrder + " nativeForegroundPreserved=True");
                    }
                    log("HK_ACTOR_ORDER_BIND entity=" + body.GetInstanceID() + " renderer=" + part.GetInstanceID()
                    + " depth=" + part.bounds.center.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    + " layer=" + part.sortingLayerID + " order=" + part.sortingOrder + " queue=" + part.sharedMaterial?.renderQueue);
                }
            }
        }
    }
    private void OrderActor(float x, float y)
    {
        var hero = HeroController.instance;
        if (!actor) {
            if(behindActor && ActorRenderer!=null) {
                renderer!.sortingLayerID=ActorRenderer.sortingLayerID;renderer.sortingOrder=ActorRenderer.sortingOrder;
                renderer.transform.position=new Vector3(x,y,ActorRenderer.transform.position.z+.05f);
                if(lastOrder==null){lastOrder="behind-actor";log("HK_REST_DRAW_ORDER behindYi=True separateDepth=True nativeForegroundPreserved=True");}
                return;
            }
            renderer!.transform.position = new Vector3(x,y,hero.transform.position.z + (aura ? .15f : 0));
            if(aura) { var band=hero.GetComponent<Renderer>(); if(band!=null) { renderer.sortingLayerID=band.sortingLayerID; renderer.sortingOrder=band.sortingOrder; } }
            return;
        }
        float depth = hero.transform.position.z;
        var native = hero.GetComponent<Renderer>();
        int layer = native == null ? 0 : native.sortingLayerID, order = native == null ? 0 : native.sortingOrder;
        int count = 0;
        if (actor) foreach (var parts in bodies.Values) foreach (var part in parts) {
            if (part == null || !part.enabled || !part.gameObject.activeInHierarchy) continue;
            count++; depth = Math.Min(depth, part.bounds.center.z);
            int value = SortingLayer.GetLayerValueFromID(part.sortingLayerID), current = SortingLayer.GetLayerValueFromID(layer);
            if (value > current || value == current && part.sortingOrder > order) { layer = part.sortingLayerID; order = part.sortingOrder; }
        }
        // Same native sorting band; distance puts Yi ahead of bodies, while
        // authored world foreground can still occlude him.
        if (actor && count > 0) depth -= .02f;
        string scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if(scene!=waterScene) {
            waterScene=scene;waterForeground=Array.Empty<Renderer>();
            if(scene=="GG_Uumuu" || scene=="GG_Flukemarm") {
                var surface=Resources.FindObjectsOfTypeAll<Transform>().Single(t=>t.gameObject.scene.name==scene && t.name=="acid_water_top");
                waterForeground=surface.GetComponentsInChildren<Renderer>(true);
                if(waterForeground.Length==0 || waterForeground.Length>16)throw new InvalidOperationException("Native shallow water artwork scope changed.");
                log("HK_SHALLOW_WATER_FOREGROUND scene="+scene+" nativeRenderers="+waterForeground.Length+" collisionFloorUnchanged=True");
            }
        }
        if(waterForeground.Length>0) {
            // The native water remains a foreground surface for Yi at every
            // height. Collision depth and the accepted shallow floor are separate.
            Renderer? water = null; int waterLayer = 0; float maximumDepth = float.NegativeInfinity;
            foreach (var part in waterForeground) {
                if (part == null) continue;
                int value = SortingLayer.GetLayerValueFromID(part.sortingLayerID);
                if (water == null || value < waterLayer || value == waterLayer && part.sortingOrder < water.sortingOrder) {
                    water = part; waterLayer = value;
                }
                maximumDepth = Math.Max(maximumDepth, part.bounds.max.z);
            }
            if (water != null) { layer = water.sortingLayerID; order = water.sortingOrder; depth = Math.Max(depth, maximumDepth + .02f); }
        }
        renderer!.sortingLayerID = layer; renderer.sortingOrder = order;
        renderer.transform.position = new Vector3(x,y,depth);
        if (actor) {
            ActorRenderer = renderer;
            if (!actorOrderKnown || depth != lastActorDepth || layer != lastActorLayer || order != lastActorOrder || count != lastActorBodies) {
                actorOrderKnown = true; lastActorDepth = depth; lastActorLayer = layer; lastActorOrder = order; lastActorBodies = count;
                log("HK_ACTOR_DRAW_ORDER depth=" + depth.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    + " layer=" + layer + " order=" + order + " bodies=" + count + " aheadOfKnownBodies=True");
            }
        }
    }
    private bool visible;
    private HollowSols.Foundation.SessionToken? displayedSession;
    private int displayedRoom;
    private bool entryHeld,entryGraceLogged,cachedInitialImage;
    private readonly ActorEntryPixels entryPixels=new();
    private readonly EntryImageHold roomEntryImage=new();
    private static VisualPresenter? heldEntryActor;
    internal static bool HoldsEntryImage(Guid session,long geometry) => heldEntryActor!=null
        && heldEntryActor.displayedWireSession==session && heldEntryActor.lastGeometry==geometry
        && heldEntryActor.roomEntryImage.Holds(session,geometry,UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle,Time.realtimeSinceStartupAsDouble);
    private long entryPixelWait=-1;
    private Guid displayedWireSession;
    private bool fixtureGapStarted,fixtureGapComplete;
    private float fixtureGapUntil;
    private int auraSnapshots;
    private float nextAuraSnapshot;
    private void RestoreBand(Renderer part) {
        if (!bodyBands.TryGetValue(part, out var band)) return;
        if (part != null) { part.sortingLayerID = band.Layer; part.sortingOrder = band.Order; }
        bodyBands.Remove(part!);
    }
    private float screenFade;
    private bool recovering;
    private float nextRecoveryTrace;
    private long overlayDrawn;
    private double cadenceStarted = -1, lastImageAt;
    private int cadenceImages, cadenceDraws;
    private double maxImageGap, totalAge, maxAge;
    private float maxAnchorGap, maxFallingGap;
    private int fallingSamples;
    private float maxCorrectedFallingGap;
    private int visibleFallingSamples;
    private string? returnHoldScene;
    private readonly RadianceEndVisibility nativeEndVisibility=new();
    public void HideForNativeEnd() {
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(!nativeEndVisibility.Request(scene.name,scene.handle))return;
        returnHoldScene=null; visible=false; if(renderer!=null)renderer.enabled=false;
    }
    private float returnHoldUntil;
    private bool returnHoldVerified, lastOpaque;
    private long lastGeometry, lastNativeFrame;
    public void HoldForNativeReturn()
    {
        if (!victoryHold || !visible || renderer == null) return;
        returnHoldScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        returnHoldUntil = Time.realtimeSinceStartup + 30;
        returnHoldVerified = false;
        log("HK_VICTORY_IMAGE_HOLD scene=" + returnHoldScene + " sequence=" + sequence + " nativeFadeUnchanged=True");
    }
    public VisualPresenter(Action<string> log, bool victoryHold = false, bool actor = false, bool aura = false, bool behindActor = false, bool worldEffects = false) { this.log = log; this.victoryHold = victoryHold; this.actor = actor; this.aura=aura; this.behindActor=behindActor; this.worldEffects=worldEffects; }
    public bool ReadyForControl => !cachedInitialImage && visible && renderer != null && renderer.enabled && screenFade <= 0 && !recovering;
    public bool ReadyForReveal => !cachedInitialImage && visible && renderer != null && renderer.enabled && screenFade <= 0;
    private bool HoldInitialImage(string reason,VisualFrame? frame,Bridge.Pose? pose) {
        // Pixels are presentation, never a transport heartbeat or a control ack.
        // Teardown/lifecycle and room identities bound this lease, not image age.
        var state=HollowSolsMenuPlugin.Instance?.Lifecycle?.State;
        if(!(actor || behindActor) || !visible || !entryHeld || !BridgeLoadingPresentation.Initializing
            || state!=HollowSols.Foundation.BridgeState.Active || !displayedSession.HasValue
            || !Nullable.Equals(displayedSession,HollowSols.Foundation.NativeSaveContext.Session)
            || displayedRoom!=UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle
            || renderer==null || !renderer.enabled || sprite==null || Camera.main==null
            || behindActor && BridgeLoadingPresentation.Blocking
            || frame!=null && (frame.Session!=displayedWireSession || frame.GeometryFrame!=lastGeometry)
            || pose!=null && (pose.GeometrySession!=displayedWireSession || pose.GeometryFrame!=lastGeometry))return false;
        cachedInitialImage=true;
        if(!entryGraceLogged){entryGraceLogged=true;log("HK_INITIAL_IMAGE_HOLD reason="+reason+" lifetime=initialization controlsRemainGated=True sameSessionAndRoom=True");}
        return true;
    }
    public void Update(VisualFrame? frame, Bridge.Pose? pose)
    {
        if(actor && heldEntryActor==this)heldEntryActor=null;
        if(nativeEndVisibility.Hidden(UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle)) {
            visible=false;if(renderer!=null)renderer.enabled=false;return;
        }
        if (returnHoldScene != null && Time.realtimeSinceStartup < returnHoldUntil
            && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == returnHoldScene && renderer != null) {
            if (!returnHoldVerified && Time.realtimeSinceStartup >= returnHoldUntil - 29.9f && Camera.main != null) {
                var point = Camera.main.WorldToViewportPoint(renderer.bounds.center);
                bool onScreen = point.z > 0 && point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1;
                log("HK_VICTORY_IMAGE_VISIBLE scene=" + returnHoldScene + " geometry=" + lastGeometry
                    + " nativeFrame=" + lastNativeFrame + " enabled=" + renderer.enabled + " active=" + renderer.gameObject.activeInHierarchy
                    + " sprite=" + (renderer.sprite != null) + " opaque=" + lastOpaque + " onScreen=" + onScreen + " sequence=" + sequence);
                returnHoldVerified = true;
            }
            return;
        }
        returnHoldScene = null;
        // Fixture-only loss exercises the same retention path for longer than
        // the former 750ms limit, without stopping either process or saving.
        if(actor && HollowSolsMenuPlugin.Instance?.FixtureInitialImageGap==true
            && visible && entryHeld && BridgeLoadingPresentation.Initializing) {
            if(!fixtureGapStarted) {
                fixtureGapStarted=true;fixtureGapUntil=Time.realtimeSinceStartup+.9f;
                var matched=frame!;
                frame=new VisualFrame {Session=matched.Session,GeometryFrame=matched.GeometryFrame,Rgba=Array.Empty<byte>()};
                // Exercise the real transparent-packet rejection below.
                if(HoldInitialImage("foreign-fixture",new VisualFrame {Session=Guid.NewGuid(),GeometryFrame=matched.GeometryFrame},pose))
                    throw new InvalidOperationException("Foreign image borrowed initialization retention.");
            }
            else if(!fixtureGapComplete && Time.realtimeSinceStartup<fixtureGapUntil) {
                if(!HoldInitialImage("missing-fixture",null,null) || ReadyForReveal || ReadyForControl || renderer?.enabled!=true)
                    throw new InvalidOperationException("Initialization image loss flickered or released readiness.");
                return;
            }
            else if(!fixtureGapComplete){fixtureGapComplete=true;log("HK_INITIAL_IMAGE_RETENTION_PASS missingSeconds=0.9 transparentReplacementRejected=True foreignIdentityRejected=True readinessHeld=True");}
        }
        if (frame == null || pose == null || frame.Session != pose.GeometrySession
            || frame.GeometryFrame != pose.GeometryFrame || Camera.main == null)
        {
            if(HoldInitialImage("missing-or-mismatched",frame,pose))return;
            // A first-entry asset/reclaim stall is not a new actor absence.
            // Retain only matching opaque artwork, briefly, with input still
            // governed by the unchanged fresh-pose/native-readiness checks.
            if(actor && visible && renderer!=null && renderer.enabled && Camera.main!=null
                && HollowSolsMenuPlugin.Instance?.Lifecycle?.State==HollowSols.Foundation.BridgeState.Active
                && Nullable.Equals(displayedSession,HollowSols.Foundation.NativeSaveContext.Session)
                && (frame==null || frame.Session==displayedWireSession && frame.GeometryFrame==lastGeometry)
                && (pose==null || pose.GeometrySession==displayedWireSession && pose.GeometryFrame==lastGeometry && pose.Vitals?.EntryActive==true)
                && roomEntryImage.Holds(displayedWireSession,lastGeometry,UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle,Time.realtimeSinceStartupAsDouble)) {
                heldEntryActor=this;
                if(!entryGraceLogged){entryGraceLogged=true;log("HK_ROOM_ENTRY_IMAGE_HOLD maximumSeconds=0.5 controlsRemainGated=True geometry="+lastGeometry);}
                return;
            }
            if (visible) log("HK_VISUAL_HIDDEN reason=stale-or-mismatched-frame frameMissing="+(frame==null)+" poseMissing="+(pose==null)
                +" sessionMismatch="+(frame!=null && pose!=null && frame.Session!=pose.GeometrySession)
                +" geometryMismatch="+(frame!=null && pose!=null && frame.GeometryFrame!=pose.GeometryFrame)+" cameraMissing="+(Camera.main==null));
            if (renderer != null) renderer.enabled = false;
            visible = false; screenFade = 0; recovering = false; return;
        }
        if(actor && !entryPixels.Observe(frame,UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle)) {
            // A successful upload of transparent pixels is not a ready actor.
            // Never expose an old room's image or replay the save-load overlay.
            if(entryPixelWait!=frame.GeometryFrame) {
                entryPixelWait=frame.GeometryFrame;
                log("HK_ACTOR_ENTRY_PIXELS_WAIT geometry="+frame.GeometryFrame+" nativeFrame="+frame.NativeFrame+" opaque=False firstImageOnly=True");
            }
            cachedInitialImage=true;if(renderer!=null)renderer.enabled=false;visible=false;return;
        }
        if(actor && BridgeLoadingPresentation.Initializing && !ActorPixels.Visible(frame.Rgba)) {
            if(HoldInitialImage("transparent",frame,pose))return;
            cachedInitialImage=true;
            if(renderer!=null)renderer.enabled=false;
            visible=false;return;
        }
        cachedInitialImage=false;
        if (renderer == null)
        {
            var hero = HeroController.instance;
            if (hero == null || (Camera.main.cullingMask & (1 << hero.gameObject.layer)) == 0)
                throw new InvalidOperationException("Native hero layer is not visible to the scene camera.");
            var shader = Shader.Find("Sprites/Default");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Native sprite compositor shader unavailable.");
            var obj = new GameObject("YiGodhome Native Sprite") { layer = hero.gameObject.layer };
            renderer = obj.AddComponent<SpriteRenderer>();
            // Scene unload destroys the renderer object, but these runtime assets
            // survive. Rebind even when the incoming image dimensions are unchanged.
            renderer.sprite = sprite;
            if (material != null) UnityEngine.Object.Destroy(material);
            material = new Material(shader); renderer.sharedMaterial = material;
            log("HK_VISUAL_SCENE_READY layer=" + obj.layer + " depth=" + hero.transform.position.z + " shader=" + shader.name);
        }
        returnHoldScene = null;
        if (texture == null || texture.width != frame!.Width || texture.height != frame.Height)
        {
            BindImage(frame!.Width,frame.Height);
            renderer.sprite = sprite;
            sequence = 0;
        }
        if (sequence != frame!.Sequence)
        {
            entryGraceLogged=false;displayedWireSession=frame.Session;
            displayedSession=HollowSols.Foundation.NativeSaveContext.Session;
            displayedRoom=UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;entryHeld=pose.Vitals?.EntryActive==true;
            double now = Time.realtimeSinceStartupAsDouble;
            if (cadenceStarted < 0) { cadenceStarted = now; lastImageAt = now; }
            maxImageGap = Math.Max(maxImageGap, (now - lastImageAt) * 1000); lastImageAt = now;
            cadenceImages++;
            double age = (frame.SentTick - frame.CaptureTick) * 1000.0 / frame.Frequency + frame.PreviousRoundTripMs;
            totalAge += age; maxAge = Math.Max(maxAge, age);
            if ((frame.Recovering || recovering) && (frame.Recovering != recovering || Time.realtimeSinceStartup >= nextRecoveryTrace)) {
                nextRecoveryTrace = Time.realtimeSinceStartup + .1f;
                log("HK_RECOVERY_PRESENTED sequence=" + frame.Sequence + " frame=" + frame.NativeFrame + " geometry=" + frame.GeometryFrame
                    + " recovering=" + frame.Recovering + " fade=" + frame.ScreenFade.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            var recoveryCamera = Camera.main;
            if (SessionConfiguration.Current.Has("void-probe") && recovering && !frame.Recovering && recoveryCamera != null) {
                var point = recoveryCamera.WorldToViewportPoint(new Vector3(pose.X, pose.Y + pose.Height / 2, renderer!.transform.position.z));
                bool onScreen = point.z > 0 && point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1;
                log("HK_VOID_PRESENTED geometry=" + frame.GeometryFrame + " nativeFrame=" + frame.NativeFrame + " onScreen=" + onScreen + " opaque=" + ActorPixels.Visible(frame.Rgba) + " x=" + pose.X + " y=" + pose.Y);
            }
            screenFade = frame.ScreenFade; recovering = frame.Recovering;
            using (HostFrameProbe.Measure(14)) { texture!.LoadRawTextureData(frame.Rgba); texture.Apply(false, false); } sequence = frame.Sequence;
            lastGeometry = frame.GeometryFrame; lastNativeFrame = frame.NativeFrame;
            if (victoryHold) lastOpaque = ActorPixels.Visible(frame.Rgba);
            if(actor)roomEntryImage.Observe(frame.Session,frame.GeometryFrame,displayedRoom,Time.realtimeSinceStartupAsDouble,
                pose.Vitals?.EntryActive==true,victoryHold?lastOpaque:ActorPixels.Visible(frame.Rgba));
            if (sequence == 1 || sequence % 30 == 0) log("HK_VISUAL_PRESENTED sequence=" + sequence + " frame=" + frame.NativeFrame + " hash=" + frame.Hash);
        }
        VisualPlacement.Resolve(frame, pose, out float drawX, out float drawY);
        OrderActor(drawX, drawY);
        float anchorGap = Vector2.Distance(new Vector2(frame.AnchorX, frame.AnchorY), new Vector2(pose.X, pose.Y));
        maxAnchorGap = Math.Max(maxAnchorGap, anchorGap);
        if (pose.VelocityY < -12 && !frame.Recovering)
        {
            fallingSamples++; maxFallingGap = Math.Max(maxFallingGap, anchorGap);
            var view = Camera.main.WorldToViewportPoint(new Vector3(pose.X, pose.Y + pose.Height * 0.5f, renderer.transform.position.z));
            if (renderer.isVisible && view.z > 0 && view.x >= 0 && view.x <= 1 && view.y >= 0 && view.y <= 1) visibleFallingSamples++;
            maxCorrectedFallingGap = Math.Max(maxCorrectedFallingGap, Vector2.Distance(new Vector2(drawX - frame.X + frame.AnchorX, drawY - frame.Y + frame.AnchorY), new Vector2(pose.X, pose.Y)));
        }
        renderer.transform.localScale = new Vector3(frame.WorldWidth / frame.Width, frame.WorldHeight / frame.Height, 1);
        // Preparation may already publish valid pixels while paired native
        // snapshots are still being restored/captured. The initial dimmer owns
        // this stage; reveal Yi and his Root only after both sides initialize.
        renderer.enabled = !(behindActor && BridgeLoadingPresentation.Blocking)
            && !((actor || behindActor) && BridgeLoadingPresentation.Initializing
            && HollowSols.Foundation.NativeSaveContext.Enabled
            && HollowSolsMenuPlugin.Instance?.Lifecycle?.State==HollowSols.Foundation.BridgeState.Loading);
        if(behindActor)RootRenderer=renderer;
        if (renderer.sprite != sprite || renderer.sprite == null || renderer.sharedMaterial != material)
            throw new InvalidOperationException("Native image renderer lost its sprite/material binding.");
        visible = renderer.enabled;
        if (aura && SessionConfiguration.Current.RunFixtures && auraSnapshots < 4 && Time.realtimeSinceStartup >= nextAuraSnapshot && ActorPixels.Visible(frame.Rgba)) {
            nextAuraSnapshot = Time.realtimeSinceStartup + .35f;
            log("HK_NATIVE_FLAME_BOUNDS image=" + renderer.bounds + " position=" + renderer.transform.position
                + " layer=" + renderer.sortingLayerID + " order=" + renderer.sortingOrder
                + " bodies=" + string.Join(";", bodies.Values.SelectMany(p=>p).Where(p=>p!=null && p.enabled).Select(p=>p.name + ":" + p.bounds + ":layer=" + p.sortingLayerID + ":order=" + p.sortingOrder)));
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!, "host-native-flame-" + auraSnapshots++ + ".png"));
        }
        cadenceDraws++;
        if (cadenceStarted >= 0 && Time.realtimeSinceStartupAsDouble - cadenceStarted >= 2)
        {
            double elapsed = Time.realtimeSinceStartupAsDouble - cadenceStarted;
            log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "HK_VISUAL_CADENCE images={0} draws={1} seconds={2:F3} imageFps={3:F2} maxImageGapMs={4:F2} meanTransferAgeMs={5:F2} maxTransferAgeMs={6:F2}",
                cadenceImages, cadenceDraws, elapsed, cadenceImages / elapsed, maxImageGap, totalAge / Math.Max(1, cadenceImages), maxAge));
            log("HK_VISUAL_BINDING scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                + " geometry=" + frame.GeometryFrame + " sprite=True material=True active=" + renderer.gameObject.activeInHierarchy
                + " enabled=" + renderer.enabled + " cameraVisible=" + renderer.isVisible);
            log("HK_VISUAL_ALIGNMENT maxAnchorGap=" + maxAnchorGap + " fallSamples=" + fallingSamples + " visibleFallSamples=" + visibleFallingSamples + " maxFallingGap=" + maxFallingGap + " maxCorrectedFallingGap=" + maxCorrectedFallingGap);
            maxAnchorGap = maxFallingGap = maxCorrectedFallingGap = 0; fallingSamples = visibleFallingSamples = 0;
            cadenceStarted = Time.realtimeSinceStartupAsDouble; cadenceImages = cadenceDraws = 0; maxImageGap = totalAge = maxAge = 0;
        }
    }
    public void DrawRecovery()
    {
        if (!visible || screenFade <= 0) return;
        var oldColor = GUI.color; int oldDepth = GUI.depth;
        try { GUI.depth = -1000; GUI.color = new Color(0, 0, 0, screenFade); GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); }
        finally { GUI.color = oldColor; GUI.depth = oldDepth; }
        if (Event.current.type == EventType.Repaint && overlayDrawn != sequence)
        { overlayDrawn = sequence; log("HK_RECOVERY_DRAWN sequence=" + sequence + " fade=" + screenFade.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); }
    }
    public void Dispose()
    {
        if(heldEntryActor==this)heldEntryActor=null;
        if (actor && ActorRenderer == renderer) ActorRenderer = null;
        if (behindActor && RootRenderer == renderer) RootRenderer = null;
        foreach (var part in bodyBands.Keys.ToArray()) RestoreBand(part);
        bodies.Clear();
        liveBodies.Clear();liveBodySet.Clear();removedBodies.Clear();
        visible = false; screenFade = 0; recovering = false;
        if (renderer != null) UnityEngine.Object.Destroy(renderer.gameObject);
        if(actorImages.Count>0) {
            foreach(var image in actorImages.Values){UnityEngine.Object.Destroy(image.Sprite);UnityEngine.Object.Destroy(image.Texture);}
            actorImages.Clear();retainedImageBytes=0;sprite=null;texture=null;
        }
        if (sprite != null) UnityEngine.Object.Destroy(sprite);
        if (material != null) UnityEngine.Object.Destroy(material);
        if (texture != null) UnityEngine.Object.Destroy(texture);
        renderer = null; sprite = null; material = null; texture = null;
    }
}
