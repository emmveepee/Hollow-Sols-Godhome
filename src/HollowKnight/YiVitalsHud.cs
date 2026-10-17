using System;
using UnityEngine;
using UnityEngine.UI;
using HarmonyLib;
using YiGodhome.Bridge;
using Pose = YiGodhome.Bridge.Pose;
namespace YiGodhome;
// Displays the native combat HUD; no resource rules or artwork are recreated.
internal sealed class YiVitalsHud : IDisposable
{
    private readonly bool originalHidden;
    private readonly Action<string> log;
    private Texture2D? image;
    private long sequence;
    private Guid imageSession;
    private long imageGeometry;
    private Texture2D? atlasImage, fireImage;
    private bool fullScreen;
    private bool disposed;
    private float previousHealth=-1;
    private bool hurtSnapshotPending;
    private int hurtSnapshots;
    private GameObject? canvasRoot;
    private RawImage? whole,top,bottom;
    private Canvas? pauseCanvas;
    private BossSummaryUI? summary;
    private int summaryScene = int.MinValue;
    private float nextSummaryLookup;
    private readonly NativeHudGate nativeHud=new();
    private readonly bool effectsOnly;
    internal void PlaceBehind(YiVitalsHud widgets) {
        if(canvasRoot!=null && widgets.canvasRoot!=null && canvasRoot.transform.parent==widgets.canvasRoot.transform.parent
            && canvasRoot.transform.GetSiblingIndex()>widgets.canvasRoot.transform.GetSiblingIndex())
            canvasRoot.transform.SetSiblingIndex(widgets.canvasRoot.transform.GetSiblingIndex());
    }
    private string previousSlide="";
    private bool nativeShown;
    private bool NativeVisible()
    {
        if(effectsOnly) {if(canvasRoot!=null)canvasRoot.transform.localScale=Vector3.one;return true;}
        bool shown=nativeHud.Read(out var scale);
        if(canvasRoot!=null)canvasRoot.transform.localScale=nativeHud.Owner!=null && canvasRoot.transform.IsChildOf(nativeHud.Owner.transform)?Vector3.one:new Vector3(scale.x,scale.y,1);
        if(previousSlide!=nativeHud.State || nativeShown!=shown) {
            previousSlide=nativeHud.State;nativeShown=shown;
            log("HK_YI_HUD_NATIVE_VISIBILITY state="+previousSlide+" shown="+shown+" scale="+scale+" frame="+Time.frameCount);
        }
        return shown;
    }
    private void OrderBelowSummary()
    {
        if (canvasRoot == null || pauseCanvas == null) return;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (summaryScene != scene.handle) { summaryScene = scene.handle; summary = null; nextSummaryLookup = 0; }
        if (scene.name == "GG_Workshop" && summary == null && Time.realtimeSinceStartup >= nextSummaryLookup) {
            nextSummaryLookup = Time.realtimeSinceStartup + .5f;
            summary = UnityEngine.Object.FindFirstObjectByType<BossSummaryUI>(FindObjectsInactive.Include);
        }
        var target = summary != null && summary.isActiveAndEnabled ? summary.GetComponentInParent<Canvas>() : pauseCanvas;
        if (target == null) target = pauseCanvas;
        if (canvasRoot.transform.parent != target.transform) {
            canvasRoot.transform.SetParent(target.transform, false);
            canvasRoot.transform.SetAsFirstSibling();
            canvasRoot.layer = target.gameObject.layer;
            foreach (var widget in new[] { whole, top, bottom }) if (widget != null) widget.gameObject.layer = target.gameObject.layer;
            log("HK_HUD_SUMMARY_ORDER summaryActive=" + (summary != null && summary.isActiveAndEnabled) + " nativeCanvas=" + target.name + " firstSibling=True");
        }
    }
    private bool BindCanvas() {
        if(canvasRoot!=null) { OrderBelowSummary(); return true; }
        var ui=UIManager.instance;
        var pause=ui==null?null:AccessTools.Field(typeof(UIManager),"pauseMenuScreen").GetValue(ui) as MenuScreen;
        var canvas=pause?.GetComponentInParent<Canvas>()?.rootCanvas;
        if(canvas==null)return false;
        pauseCanvas = canvas;
        canvasRoot=new GameObject("YiGodhome HUD beneath native menus",typeof(RectTransform),typeof(LayoutElement));
        canvasRoot.GetComponent<LayoutElement>().ignoreLayout = true;
        canvasRoot.layer=canvas.gameObject.layer;canvasRoot.transform.SetParent(canvas.transform,false);canvasRoot.transform.SetAsFirstSibling();
        var rect=(RectTransform)canvasRoot.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
        RawImage Image(string name,Vector2 min,Vector2 max,Rect uv) {
            var obj=new GameObject(name,typeof(RectTransform),typeof(RawImage));obj.layer=canvas.gameObject.layer;obj.transform.SetParent(canvasRoot.transform,false);
            var r=(RectTransform)obj.transform;r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;
            var raw=obj.GetComponent<RawImage>();raw.raycastTarget=false;raw.uvRect=uv;return raw;
        }
        whole=Image("Native full HUD",Vector2.zero,Vector2.one,new Rect(0,0,1,1));
        top=Image("Native top HUD",new Vector2(0,2f/3),new Vector2(.65f,1),new Rect(0,.5f,1,.5f));
        bottom=Image("Native bottom HUD",Vector2.zero,new Vector2(.65f,1f/3),new Rect(0,0,1,.5f));
        log("HK_HUD_NATIVE_MENU_ORDER sharedCanvas=True firstSibling=True nativeMenusAbove=True");return true;
    }
    private void RefreshCanvas() {
        if(!BindCanvas())return;
        bool shown=!disposed && sequence!=0 && image!=null && NativeVisible();
        whole!.enabled=shown&&fullScreen;top!.enabled=bottom!.enabled=shown&&!fullScreen;
        whole.texture=top.texture=bottom.texture=image;
    }
    public YiVitalsHud(Action<string> log,bool effectsOnly=false)
    {
        this.log = log;this.effectsOnly=effectsOnly;
        if (!effectsOnly && UnityEngine.Object.FindObjectsByType<HudGlobalHide>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length == 0) throw new InvalidOperationException("Native HUD visibility unavailable.");
        originalHidden = HudGlobalHide.IsHidden; if(!effectsOnly)HudGlobalHide.IsHidden = true;
        log("HK_YI_HUD_READY nativeHudHidden=True authority=NineSols screen=" + Screen.width + "x" + Screen.height);
    }
    public void Update(VisualFrame? frame, Pose? pose)
    {
        using var cost = HostFrameProbe.Measure(15);
        OrderBelowSummary();
        if (frame == null || pose == null || frame.GeometryFrame != pose.GeometryFrame || frame.Session!=pose.GeometrySession) {
            if(sequence!=0 && VisualPresenter.HoldsEntryImage(imageSession,imageGeometry)){RefreshCanvas();return;}
            sequence = 0; RefreshCanvas(); return;
        }
        imageSession=frame.Session;imageGeometry=frame.GeometryFrame;
        bool fire = frame.WorldWidth == 1 && frame.WorldHeight == 1;
        if (!fire && (frame.WorldWidth != 0.65f || frame.WorldHeight != 1f / 3 || frame.Height % 2 != 0)) throw new InvalidOperationException("Unexpected native HUD atlas layout.");
        if (frame.Sequence == sequence) {RefreshCanvas();return;}
        if(effectsOnly && frame.Width==1 && frame.Height==1) {
            // Clear the effect without throwing away its warmed full-resolution
            // texture on every injury→idle→injury cycle.
            sequence=frame.Sequence;image=null;RefreshCanvas();return;
        }
        if(SessionConfiguration.Current.RunFixtures && pose.Vitals!=null) {
            if(previousHealth>=0 && pose.Vitals.Health<previousHealth && hurtSnapshots<2) hurtSnapshotPending=true;
            previousHealth=pose.Vitals.Health;
        }
        fullScreen=fire; image=fire ? fireImage : atlasImage;
        if (image == null || image.width != frame.Width || image.height != frame.Height)
        { if (image != null) UnityEngine.Object.Destroy(image); image = new Texture2D(frame.Width, frame.Height, TextureFormat.RGBA32, false); }
        if(fire) fireImage=image; else atlasImage=image;
        image.LoadRawTextureData(frame.Rgba); image.Apply(false, false); sequence = frame.Sequence;
        RefreshCanvas();
        if (sequence == 1 || sequence % 30 == 0) log("HK_HUD_PRESENTED sequence=" + sequence + " frame=" + frame.NativeFrame + " hash=" + frame.Hash + " geometry=" + frame.GeometryFrame);
    }
    internal bool FixtureVisible
    {
        get {
            if (sequence == 0 || image == null || canvasRoot == null || !canvasRoot.activeInHierarchy
                || !(whole?.enabled == true || top?.enabled == true && bottom?.enabled == true)) return false;
            var pixels = image.GetRawTextureData();
            for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) return true;
            return false;
        }
    }
    public void Draw(PlayerVitals? vitals)
    {
        if (disposed || image == null || sequence == 0 || vitals == null) return;
        // Native FSMs may change after the transport Update. Disable the
        // actual widgets too, rather than only skipping the IMGUI fallback.
        RefreshCanvas();
        if(!NativeVisible())return;
        if(canvasRoot!=null) {
            if(hurtSnapshotPending && Event.current.type==EventType.Repaint) {
                hurtSnapshotPending=false;hurtSnapshots++;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!,"native-damage-host-"+hurtSnapshots+".png"));
                log("HK_NATIVE_DAMAGE_HUD_SCREENSHOT sequence="+sequence+" file=native-damage-host-"+hurtSnapshots+".png fullResolution=True nativeCanvas=True");
            }
            return;
        }
        if(fullScreen) {
            GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),image,ScaleMode.StretchToFill,true);
            if(hurtSnapshotPending && Event.current.type==EventType.Repaint) {
                hurtSnapshotPending=false;hurtSnapshots++;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!,"native-damage-host-"+hurtSnapshots+".png"));
                log("HK_NATIVE_DAMAGE_HUD_SCREENSHOT sequence="+sequence+" file=native-damage-host-"+hurtSnapshots+".png fullResolution=True");
            }
            return;
        }
        GUI.DrawTextureWithTexCoords(new Rect(0, 0, Screen.width * 0.65f, Screen.height / 3f), image, new Rect(0, 0.5f, 1, 0.5f), true);
        GUI.DrawTextureWithTexCoords(new Rect(0, Screen.height * (2f / 3), Screen.width * 0.65f, Screen.height / 3f), image, new Rect(0, 0, 1, 0.5f), true);
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if(canvasRoot!=null)UnityEngine.Object.Destroy(canvasRoot);
        if (atlasImage != null) UnityEngine.Object.Destroy(atlasImage);
        if (fireImage != null) UnityEngine.Object.Destroy(fireImage);
        if(!effectsOnly)HudGlobalHide.IsHidden = originalHidden; log("HK_YI_HUD_RELEASED nativeHudRestored=True");
    }
}
