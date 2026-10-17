using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using YiGodhome.Bridge;
using Pose = YiGodhome.Bridge.Pose;

namespace YiGodhome;

internal sealed class NativeHudCapture : IDisposable
{
    internal Camera SourceCamera => source;
    private readonly UIManager uiOwner;
    private readonly VisualEndpoint endpoint;
    private readonly bool effectsOnly;
    private readonly Guid session;
    private readonly Action<string> log;
    private readonly Transform[] roots;
    private readonly Camera source, camera;
    private readonly RenderTexture target;
    private readonly Texture2D pixels;
    private readonly RenderTexture fireTarget;
    private readonly Texture2D firePixels;
    private readonly NativeHurtPostProcess hurtEffects;
    private readonly bool hudAfterHurt;
    private bool hurtOrderVerified;
    private readonly RenderTexture hurtBlack,hurtWhite;
    private readonly Texture2D hurtWhitePixels;
    private readonly int fireWidth, fireHeight;
    private readonly List<Transform> hierarchyScratch=new();
    private readonly List<GameObject> captureObjects=new();
    private readonly List<int> captureLayers=new();
    private readonly HashSet<GameObject> captureSeen=new();
    private float nextPressure;
    private int queueSkipped;
    private bool fireVerified;
    private int fireSamples;
    private bool fireProbe;
    private float fireProbeAt=-1, hurtProbeAt=-1;
    private bool hurtSaved;
    private readonly PlayerHealth health;
    private readonly UnityEngine.Events.UnityAction hurtAction;
    private readonly Animator? hurtAnimator;
    private readonly SpriteRenderer[] hurtSprites;
    private float hurtUntil=-1;
    private float actualHurtAt=-1;
    private bool actualHurtReference;
    private bool actualBurnReference;
    private float actualBurnAt=-1;
    private void Hurt(string origin)
    {
        if(hurtAnimator==null) return;
        hurtAnimator.SetTrigger("Hurt"); hurtUntil=Time.time+.6f;
        // Do not wait for the next ordinary 20Hz HUD slot and miss the native
        // short attack-hit flash. Busy readbacks still retain their own lease.
        next=0;
        if(origin=="Health.OnTakeDamageEvent") actualHurtAt=Time.time;
        log("NS_NATIVE_HURT_HUD source="+origin+" nativeAnimator="+hurtAnimator.GetInstanceID()+" nativeTrigger=Hurt frame="+Time.frameCount);
    }
    private long geometry;
    private float next;
    private int count;
    private readonly int layer;
    private readonly int width, bandHeight;
    private readonly bool snapshots;
    private readonly NativeHudReadback readback;
    public NativeHudCapture(VisualEndpoint endpoint, Guid session, Action<string> log,bool effectsOnly=false)
    {
        this.endpoint = endpoint; this.session = session; this.log = log;this.effectsOnly=effectsOnly;
        health=Player.i.health;
        var hurtOwners=effectsOnly?UnityEngine.Object.FindObjectsOfType<CameraHUDEffect>(true)
            .Select(o=>HarmonyLib.AccessTools.Field(typeof(CameraHUDEffect),"effectAnimator").GetValue(o) as Animator)
            .Where(a=>a!=null && a.parameters.Any(p=>p.name=="Hurt" && p.type==AnimatorControllerParameterType.Trigger)).Distinct().ToArray():Array.Empty<Animator>();
        if(hurtOwners.Length>1) throw new InvalidOperationException("Native hurt HUD owner ambiguous.");
        hurtAnimator=hurtOwners.SingleOrDefault();
        hurtSprites=hurtAnimator==null?Array.Empty<SpriteRenderer>():hurtAnimator.GetComponentsInChildren<SpriteRenderer>(true);
        log("NS_NATIVE_HURT_HUD_BOUND nativeAnimator="+(hurtAnimator==null?0:hurtAnimator.GetInstanceID())+" exactOwner=CameraHUDEffect available="+(hurtAnimator!=null));
        hurtAction=()=>Hurt("Health.OnTakeDamageEvent");
        if(effectsOnly)health.OnTakeDamageEvent.AddListener(hurtAction);
        readback = new NativeHudReadback(endpoint, log, effectsOnly?"hud_effects":"player_hud");
        var ui = UnityEngine.Object.FindObjectOfType<UIManager>(true);
        if (ui == null || ui.PlayerUI == null) throw new InvalidOperationException("Native player HUD unavailable.");
        uiOwner=ui;
        roots = new[] { ui.PlayerUI.transform.Find("LeftTop"), ui.PlayerUI.transform.Find("LeftDown") };
        if (roots.Any(r => r == null)) throw new InvalidOperationException("Native combat HUD layout changed.");
        if(HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed==true) {
            var coreUI=GameCore.Instance.GetComponentsInChildren<UIManager>(true);
            log("NS_FIXTURE_HUD_OWNER capture="+ui.GetInstanceID()+" path="+NativeRoomIsolation.PathOf(ui.transform)
                +" playerRootActive="+ui.PlayerUI.activeInHierarchy+" roots="+string.Join(";",roots.Select(r=>r.name+":"+r.gameObject.activeSelf+":"+r.gameObject.activeInHierarchy))
                +" coreOwners="+string.Join(";",coreUI.Take(4).Select(u=>u.GetInstanceID()+":"+NativeRoomIsolation.PathOf(u.transform))));
        }
        source = ui.PlayerUI.GetComponentInParent<Camera>();
        if (source == null || !source.orthographic) throw new InvalidOperationException("Native HUD camera unavailable.");
        var hurtCamera=hurtAnimator?.GetComponent<Camera>();
        hudAfterHurt=!effectsOnly && hurtCamera!=null && hurtCamera!=source && source.depth>hurtCamera.depth;
        log("NS_NATIVE_HURT_CAPTURE_SCOPE hudCamera="+source.name+" hudCameraId="+source.GetInstanceID()+" hudDepth="+source.depth
            +" hurtOwner="+(hurtAnimator?.name??"none")+" hurtCamera="+(hurtCamera?.name??"none")+" hurtCameraId="+(hurtCamera?.GetInstanceID()??0)
            +" hurtDepth="+(hurtCamera?.depth??0)+" sameCamera="+(hurtCamera==source)+" nativeHudSize="+source.pixelWidth+"x"+source.pixelHeight);
        var occupied = UnityEngine.Object.FindObjectsOfType<Renderer>(true).Select(r => r.gameObject.layer).ToArray();
        layer = Enumerable.Range(8, 24).Reverse().FirstOrDefault(l => l != 30 && !occupied.Contains(l));
        if (layer == 0) throw new InvalidOperationException("No isolated HUD capture layer available.");
        // Reference PNG encoding/readback can exceed peer freshness. Ordinary
        // trace and manual captures must keep the same asynchronous HUD path.
        snapshots = YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("hud-reference-probe");
        float canvasHeight = Mathf.Clamp(source.pixelHeight, 1080, 2160);
        canvasHeight = Mathf.Min(canvasHeight, Mathf.Sqrt(VisualCodec.MaxHudPixels / (source.aspect * 0.65f * (2f / 3))));
        canvasHeight *= Mathf.Min(1, VisualCodec.MaxHudSide / (canvasHeight * source.aspect * 0.65f));
        width = Mathf.Clamp(Mathf.RoundToInt(canvasHeight * source.aspect * 0.65f), 1, VisualCodec.MaxHudSide);
        bandHeight = Mathf.Max(1, Mathf.FloorToInt(canvasHeight / 3));
        if ((long)width * bandHeight * 2 > VisualCodec.MaxHudPixels) throw new InvalidOperationException("Native HUD crop exceeds transport bounds.");
        camera = new GameObject("YiGodhome Native HUD Capture").AddComponent<Camera>(); camera.enabled = false;
        target = new RenderTexture(effectsOnly?1:width, effectsOnly?1:bandHeight * 3, 24, RenderTextureFormat.ARGB32); target.Create();
        pixels = new Texture2D(effectsOnly?1:width, effectsOnly?1:bandHeight * 2, TextureFormat.RGBA32, false);
        // The ordinary atlas covers65% of the screen width. Full-screen hurt
        // effects need the matching pixel density rather than stretching that
        // narrower atlas raster over the entire screen.
        fireWidth = effectsOnly?Mathf.Min(VisualCodec.MaxHudSide, Mathf.RoundToInt(width / .65f)):1;
        fireWidth = Mathf.Min(fireWidth, Mathf.FloorToInt(Mathf.Sqrt(VisualCodec.MaxHudPixels * source.aspect)));
        fireHeight = effectsOnly?Mathf.Max(1, Mathf.RoundToInt(fireWidth / source.aspect)):1;
        if ((long)fireWidth * fireHeight > VisualCodec.MaxHudPixels) throw new InvalidOperationException("Native fire HUD exceeds transport bounds.");
        fireTarget = new RenderTexture(fireWidth, fireHeight, 24, RenderTextureFormat.ARGB32); fireTarget.Create();
        firePixels = new Texture2D(fireWidth, fireHeight, TextureFormat.RGBA32, false);
        hurtEffects=new NativeHurtPostProcess(hurtAnimator,fireWidth,fireHeight,log);
        hurtBlack=new RenderTexture(fireWidth,fireHeight,24,RenderTextureFormat.ARGB32); hurtBlack.Create();
        hurtWhite=new RenderTexture(fireWidth,fireHeight,24,RenderTextureFormat.ARGB32); hurtWhite.Create();
        hurtWhitePixels=new Texture2D(fireWidth,fireHeight,TextureFormat.RGBA32,false);
        ActorPixels.Prepare();
        // Prepare the full-screen hurt/revival path while entry is concealed.
        // Keep native pixel density and both matte samples unchanged.
        endpoint.PreparePixels(fireWidth*fireHeight*4,6);
        if(!effectsOnly)endpoint.PreparePixels(width*bandHeight*2*4);
        NativeGameplayUI.Bind(uiOwner);
        // The isolated simulation has no native campaign controller to request
        // its combat HUD. Own an ordinary native display vote for this lease.
        uiOwner.CheckInit();uiOwner.VotePlayerUIShow(true,camera);
        log("NS_HUD_READY native=True roots=LeftTop,LeftDown width=" + width + " height=" + bandHeight * 2 + " atlas=True sourceHeight=" + source.pixelHeight);
    }
    public void Capture(Pose pose)
    {
        if (geometry != pose.GeometryFrame) { if(geometry != 0) NativeFireHud.Reset(); geometry=pose.GeometryFrame; }
        readback.Observe(pose.GeometryFrame);
        if(Time.realtimeSinceStartup>=nextPressure) {
            nextPressure=Time.realtimeSinceStartup+2;
            log("NS_HUD_PRESSURE channel="+(effectsOnly?"effects":"widgets")+" pixelAllocations="+endpoint.PixelAllocations+" allocatedPixelBytes="+endpoint.AllocatedPixelBytes
                +" retainedPixelBytes="+endpoint.RetainedPixelBytes+" outstandingPixels="+endpoint.OutstandingPixels
                +" queueSkipped="+queueSkipped+" readbackBusy="+readback.Busy+" frame="+Time.frameCount);
        }
        if (readback.Busy || Time.realtimeSinceStartup < next || !effectsOnly && roots.Any(r => !r.gameObject.activeInHierarchy)) return;
        // Do not render/read back two more full-screen mattes just to replace
        // a frame still waiting for the slower native-effect conversion worker.
        if(!endpoint.CanQueuePixels){queueSkipped++;return;}
        next = Time.realtimeSinceStartup + 1f / 20;
        long tick = System.Diagnostics.Stopwatch.GetTimestamp();
        NativeFireHud.Observe(log);
        if(effectsOnly && !fireProbe && SessionConfiguration.Current.RunFixtures && SessionConfiguration.Current.Has("stagger-probe")
            && (EncounterProfile.Selected.Family==EncounterProfile.Grimm || EncounterProfile.Selected.Family==EncounterProfile.NightmareGrimm) && NativeFireHud.Ready) {
            fireProbe=true; fireProbeAt=Time.time;
        }
        if(fireProbeAt>=0 && hurtProbeAt<0 && Time.time-fireProbeAt>=1.2f) {
            hurtProbeAt=Time.time; Hurt("bounded-visual-fixture");
        }
        bool fireEffect=effectsOnly && NativeFireHud.EffectsVisible && NativeFireHud.Root!=null;
        if(fireEffect && actualBurnAt<0)actualBurnAt=Time.time;
        bool hurtEffect=hurtAnimator!=null && (Time.time<hurtUntil || hurtSprites.Any(s=>s!=null && s.enabled && s.gameObject.activeInHierarchy && s.color.a>0));
        bool postprocess=effectsOnly && hurtEffects.Active;
        // Native fire sprites can write alpha squared by their blend factors.
        // Recover actual coverage through the same dark mattes as the border.
        bool matte=postprocess || fireEffect;
        bool fire = fireEffect || hurtEffect || matte;
        if(effectsOnly && !fire) {
            var empty=endpoint.RentFrame(4);Array.Clear(empty.Rgba,0,4);
            empty.Session=session;empty.GeometryFrame=pose.GeometryFrame;empty.NativeFrame=Time.frameCount;empty.CaptureTick=tick;
            empty.Width=empty.Height=1;empty.WorldWidth=empty.WorldHeight=1;endpoint.PublishPixels(empty);return;
        }
        bool hurtEvidence=snapshots && !hurtSaved && hurtProbeAt>=0 && Time.time-hurtProbeAt>=.1f;
        var renderTarget = fire ? fireTarget : target;
        var renderPixels = fire ? firePixels : pixels;
        captureObjects.Clear();captureLayers.Clear();captureSeen.Clear();
        if(!effectsOnly)foreach(var root in roots)CollectCaptureRoot(root);
        if(fireEffect)CollectCaptureRoot(NativeFireHud.Root!);
        if(hurtEffect)CollectCaptureRoot(hurtAnimator!.transform);
        var objects=captureObjects;var layers=captureLayers;
        var old = RenderTexture.active;
        bool asynchronous = readback.Supported;
        bool compare = asynchronous && NativeWorkCost.Enabled && (!readback.Verified || fire && !fireVerified);
        bool hurtReference=snapshots && YiGodhome.Bridge.SessionConfiguration.Current.Has("trace-probe") && !actualHurtReference && actualHurtAt>=0 && Time.time-actualHurtAt>=.08f && Time.time-actualHurtAt<.4f && matte;
        bool evidence = hurtReference || hurtEvidence || snapshots && (fire && (!fireVerified || fireSamples == 2) || count + 1 == 20 || count + 1 == 100 || count + 1 == 400 || count + 1 == 1000);
        try
        {
            foreach (var obj in objects) obj.layer = layer;
            if(postprocess && hudAfterHurt) SetLateHudLayers(objects,layers,false);
            camera.CopyFrom(source); camera.enabled = false; camera.cullingMask = 1 << layer;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = matte?Color.black:Color.clear;
            camera.allowHDR = false; camera.allowMSAA = false; camera.targetTexture = matte && !postprocess ? hurtBlack : renderTarget;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.aspect = source.aspect * (fire ? 1 : 0.65f);
            camera.transform.SetPositionAndRotation(source.transform.position
                - source.transform.right * (source.orthographicSize * source.aspect * (fire ? 0 : 0.35f)), source.transform.rotation);
            camera.ResetProjectionMatrix(); camera.Render(); RenderTexture.active = renderTarget;
            if(matte) {
                if(postprocess)hurtEffects.Render(renderTarget,hurtBlack);
                camera.backgroundColor=new Color(64f/255,64f/255,64f/255,1);
                camera.targetTexture=postprocess?renderTarget:hurtWhite;camera.Render();
                if(postprocess)hurtEffects.Render(renderTarget,hurtWhite);
                if(postprocess && hudAfterHurt) RenderLateHud(objects,layers,renderTarget);
                RenderTexture.active=hurtBlack;
            }
            // Keep native vertical projection for screen-space fill/mask shaders.
            // Transfer only the occupied bands, with bottom then top in the atlas.
            if (!asynchronous || compare || evidence) {
                if (fire) renderPixels.ReadPixels(new Rect(0, 0, fireWidth, fireHeight), 0, 0, false);
                else {
                    renderPixels.ReadPixels(new Rect(0, 0, width, bandHeight), 0, 0, false);
                    renderPixels.ReadPixels(new Rect(0, bandHeight * 2, width, bandHeight), 0, bandHeight, false);
                }
                NativeReadbackVerification.Verify(renderPixels, "player_hud", log);
                if(matte && (!asynchronous || hurtEvidence || hurtReference)) {
                    RenderTexture.active=hurtWhite; hurtWhitePixels.ReadPixels(new Rect(0,0,fireWidth,fireHeight),0,0,false);
                    if(hurtEvidence) File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!,"native-hurt-hud-white.png"),hurtWhitePixels.EncodeToPNG());
                }
                if(hurtReference) {
                    actualHurtReference=true;
                    string directory=Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
                    File.WriteAllBytes(Path.Combine(directory,"native-damage-black.png"),renderPixels.EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(directory,"native-damage-matte-64.png"),hurtWhitePixels.EncodeToPNG());
                    var reference=new RenderTexture(fireWidth,fireHeight,0,RenderTextureFormat.ARGB32);reference.Create();
                    try {
                        camera.backgroundColor=new Color(.5f,.5f,.5f,1);camera.Render();
                        RenderTexture.active=renderTarget;renderPixels.ReadPixels(new Rect(0,0,fireWidth,fireHeight),0,0,false);
                        File.WriteAllBytes(Path.Combine(directory,"native-damage-grey-input.png"),renderPixels.EncodeToPNG());
                        hurtEffects.Render(renderTarget,reference);RenderTexture.active=reference;
                        renderPixels.ReadPixels(new Rect(0,0,fireWidth,fireHeight),0,0,false);
                        File.WriteAllBytes(Path.Combine(directory,"native-damage-grey-reference.png"),renderPixels.EncodeToPNG());
                        camera.backgroundColor=new Color(64f/255,64f/255,64f/255,1);camera.Render();
                        RenderTexture.active=renderTarget;renderPixels.ReadPixels(new Rect(0,0,fireWidth,fireHeight),0,0,false);
                        File.WriteAllBytes(Path.Combine(directory,"native-damage-dark-input.png"),renderPixels.EncodeToPNG());
                        hurtEffects.Render(renderTarget,reference);RenderTexture.active=reference;
                        renderPixels.ReadPixels(new Rect(0,0,fireWidth,fireHeight),0,0,false);
                        File.WriteAllBytes(Path.Combine(directory,"native-damage-dark-reference.png"),renderPixels.EncodeToPNG());
                        log("NS_NATIVE_DAMAGE_REFERENCE blackDarkGrey=True referenceLevel=64 colorSpace="+QualitySettings.activeColorSpace+" inputSrgb="+renderTarget.sRGB+" outputSrgb="+reference.sRGB+" frame="+Time.frameCount);
                    } finally {reference.Release();UnityEngine.Object.Destroy(reference);RenderTexture.active=hurtBlack;}
                    // Diagnostic reference must not replace the captured matte.
                    renderPixels.ReadPixels(new Rect(0,0,fireWidth,fireHeight),0,0,false);
                }
            }
        }
        finally { RenderTexture.active = old; for (int i = 0; i < objects.Count; i++) if (objects[i] != null) objects[i].layer = layers[i]; }
        var captured = endpoint.RentFrame((fire ? fireWidth * fireHeight : width * bandHeight * 2) * 4);
        if (!asynchronous) renderPixels.GetRawTextureData<byte>().CopyTo(captured.Rgba);
        captured.Session = session; captured.GeometryFrame = pose.GeometryFrame; captured.NativeFrame = Time.frameCount; captured.CaptureTick = tick;
        captured.MatteBackground=64;
        captured.Width = fire ? fireWidth : width; captured.Height = fire ? fireHeight : bandHeight * 2; captured.WorldWidth = fire ? 1 : .65f; captured.WorldHeight = fire ? 1 : 1f / 3;
        if (asynchronous && matte) readback.RequestMattes(hurtBlack,hurtWhite,captured,compare?renderPixels.GetRawTextureData<byte>().ToArray():null);
        else if (asynchronous) readback.Request(renderTarget, captured, 0, fire ? fireHeight : bandHeight, !fire, compare ? renderPixels.GetRawTextureData<byte>().ToArray() : null);
        else if(matte) {
            var white=endpoint.RentFrame(captured.Rgba.Length); hurtWhitePixels.GetRawTextureData<byte>().CopyTo(white.Rgba);
            endpoint.PublishMattePixels(captured,white);
        } else endpoint.PublishPremultipliedPixels(captured);
        NativeWorkCost.Hud.Record(tick, "player_hud", log);
        if (fire && !fireVerified) {
            fireVerified=true;
            log("NS_FIRE_HUD_CAPTURE fullScreen=True width="+fireWidth+" height="+fireHeight+" nativeAnimator=True bytes="+captured.Rgba.Length+" frame="+Time.frameCount);
            if(snapshots) File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!,"native-fire-hud.png"),renderPixels.EncodeToPNG());
        }
        if (fire && snapshots && ++fireSamples == 3)
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!,"native-fire-hud-settled.png"),renderPixels.EncodeToPNG());
        if(hurtEvidence) {
            hurtSaved=true;
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!,"native-hurt-hud.png"),renderPixels.EncodeToPNG());
            log("NS_NATIVE_HURT_HUD_CAPTURE fullScreen=True nativeTrigger=True healthUnchanged=True frame="+Time.frameCount);
        }
        count++;
        if(snapshots && !actualBurnReference && fireEffect && actualBurnAt>=0 && Time.time-actualBurnAt>=.12f
            && YiGodhome.Bridge.SessionConfiguration.Current.Has("hud-reference-probe"))CaptureBurnReference();
        if (evidence)
        {
            int visible = renderPixels.GetPixels32().Count(p => p.a != 0);
            log("NS_HUD_CAPTURE count=" + count + " frame=" + Time.frameCount + " visiblePixels=" + visible + " health=" + pose.Vitals?.Health + " chi=" + pose.Vitals?.Chi + " ammo=" + pose.Vitals?.Ammo
                + " captureMs=" + ((System.Diagnostics.Stopwatch.GetTimestamp() - tick) * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("F3"));
            if (snapshots) File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!, "hud-" + count + ".png"), renderPixels.EncodeToPNG());
        }
    }
    private void CollectCaptureRoot(Transform root) {
        hierarchyScratch.Clear();root.GetComponentsInChildren(true,hierarchyScratch);
        foreach(var child in hierarchyScratch)if(captureSeen.Add(child.gameObject)) {
            captureObjects.Add(child.gameObject);captureLayers.Add(child.gameObject.layer);
        }
    }
    private void CaptureBurnReference()
    {
        var root=NativeFireHud.Root;if(root==null)return;
        actualBurnReference=true;
        var objects=root.GetComponentsInChildren<Transform>(true).Select(t=>t.gameObject).ToArray();
        if(objects.Length>32)throw new InvalidOperationException("Native burning HUD reference scope changed.");
        var original=objects.Select(o=>o.layer).ToArray();
        var previous=RenderTexture.active;
        var reference=new RenderTexture(fireWidth,fireHeight,24,RenderTextureFormat.ARGB32);reference.Create();
        try {
            foreach(var obj in objects)obj.layer=layer;
            camera.CopyFrom(source);camera.enabled=false;camera.cullingMask=1<<layer;camera.clearFlags=CameraClearFlags.SolidColor;
            camera.allowHDR=false;camera.allowMSAA=false;camera.targetTexture=reference;camera.rect=new Rect(0,0,1,1);
            camera.aspect=source.aspect;camera.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);camera.ResetProjectionMatrix();
            string directory=Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
            void Save(string name,Color background) {
                camera.backgroundColor=background;camera.Render();RenderTexture.active=reference;
                firePixels.ReadPixels(new Rect(0,0,fireWidth,fireHeight),0,0,false);
                File.WriteAllBytes(Path.Combine(directory,name),firePixels.EncodeToPNG());
            }
            Save("native-burning-grey-reference.png",new Color(.5f,.5f,.5f,1));
            Save("capture-burning-grey-reference.png",new Color(.5f,.5f,.5f,1));
            Save("capture-burning-transparent.png",Color.clear);
            Save("capture-burning-dark-reference.png",new Color(64f/255,64f/255,64f/255,1));
            log("NS_NATIVE_BURN_REFERENCE actualNativeFireState=True isolatedAuthoredRoot=True nativeAndCaptureGrey=True transparentAndDark=True size="+fireWidth+"x"+fireHeight
                +" colorSpace="+QualitySettings.activeColorSpace+" srgb="+reference.sRGB+" frame="+Time.frameCount);
        } finally {
            RenderTexture.active=previous;
            for(int i=0;i<objects.Length;i++)if(objects[i]!=null)objects[i].layer=original[i];
            camera.targetTexture=null;reference.Release();UnityEngine.Object.Destroy(reference);
        }
    }
    private bool IsHurtObject(GameObject obj)=>hurtAnimator!=null && obj.transform.IsChildOf(hurtAnimator.transform);
    private void SetLateHudLayers(IReadOnlyList<GameObject> objects,IReadOnlyList<int> original,bool capture)
    {
        for(int i=0;i<objects.Count;i++) if(!IsHurtObject(objects[i])) objects[i].layer=capture?layer:original[i];
    }
    private void RenderLateHud(IReadOnlyList<GameObject> objects,IReadOnlyList<int> original,RenderTexture effectInput)
    {
        // Native gameplay UI renders after the lower-depth hurt camera. Preserve
        // its sprite/mask draw without running the earlier image effect over it.
        for(int i=0;i<objects.Count;i++) if(IsHurtObject(objects[i])) objects[i].layer=original[i];
        SetLateHudLayers(objects,original,true);
        camera.clearFlags=CameraClearFlags.Depth;
        try {
            camera.targetTexture=hurtBlack;camera.Render();
            camera.targetTexture=hurtWhite;camera.Render();
            if(!hurtOrderVerified) {
                hurtOrderVerified=true;
                log("NS_NATIVE_HURT_HUD_ORDER nativeDepthOrder=True hudAfterPostprocess=True widgetShaderUnchanged=True depthStencil=True capture="+fireWidth+"x"+fireHeight+" frame="+Time.frameCount);
            }
        } finally {
            SetLateHudLayers(objects,original,false);
            for(int i=0;i<objects.Count;i++) if(IsHurtObject(objects[i])) objects[i].layer=layer;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.targetTexture=effectInput;
        }
    }
    public void Dispose() { if(uiOwner!=null)uiOwner.RevokeVotePlayerUIShow(camera); NativeGameplayUI.Release(uiOwner); if(health!=null) health.OnTakeDamageEvent.RemoveListener(hurtAction); readback.Dispose(); hurtEffects.Dispose(); hurtBlack.Release(); hurtWhite.Release(); UnityEngine.Object.Destroy(hurtBlack); UnityEngine.Object.Destroy(hurtWhite); UnityEngine.Object.Destroy(hurtWhitePixels); NativeFireHud.Reset(); fireTarget.Release(); UnityEngine.Object.Destroy(fireTarget); UnityEngine.Object.Destroy(firePixels); UnityEngine.Object.Destroy(camera.gameObject); target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(pixels); }
}
