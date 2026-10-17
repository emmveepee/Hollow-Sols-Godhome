using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using HollowSols.Foundation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;
using UnityEngine.UI;

namespace YiGodhome;

// Acquired before native new/load entry, independently of the later combat
// adapter. Native entry/cutscene clocks keep running behind hidden artwork.
internal sealed class BridgeLoadingPresentation : MonoBehaviour
{
    private static BridgeLoadingPresentation? current;
    private readonly Dictionary<Renderer,bool> hidden=new();
    private readonly Dictionary<Graphic,bool> hiddenLoadingGraphics=new();
    private readonly Dictionary<AudioSource,bool> mutedArrivalAudio=new();
    private GameObject? panel;
    private CanvasGroup? group;
    private Text? captionText;
    private float nextTrace;
    private int traceCount;
    private int capturedDots;
    private bool capturedGesture;
    private static readonly string[] captions={"Bridging Yi.<color=#FFFFFF00>..</color>","Bridging Yi..<color=#FFFFFF00>.</color>","Bridging Yi..."};
    private SessionToken token;
    private bool oldHud,revealing;
    private bool initialRevealCompleted;
    private bool initialEntryFinished;
    internal static bool Initializing=>current!=null && !current.initialEntryFinished;
    private int room=int.MinValue;
    private float alpha;
    private HeroController? cachedHero;
    private LoadingCanvas? nativeLoadingCanvas;
    private Graphic[] nativeLoadingGraphics=Array.Empty<Graphic>();
    internal static bool Blocking=>current!=null && current.alpha>0 && World(UnitySceneManager.GetActiveScene().name);
    internal static void Begin() {
        End();
        var obj=new GameObject("Hollow Sols scene-entry presentation");DontDestroyOnLoad(obj);
        current=obj.AddComponent<BridgeLoadingPresentation>();
        current.token=NativeSaveContext.Session!.Value;
        current.oldHud=HudGlobalHide.IsHidden;HudGlobalHide.IsHidden=true;
        current.Build();current.ObserveScene(UnitySceneManager.GetActiveScene());
    }
    internal static void End() {
        if(current==null)return;var owner=current;current=null;
        owner.Restore();Destroy(owner.gameObject);
    }
    internal static void SceneEntered(Scene scene) {if(current!=null)current.ObserveScene(scene);}
    internal static void SceneLoaded(Scene scene,LoadSceneMode mode) {
        // Fresh Godseeker entry uses a scene-owned sprite, outside HeroController.
        // Acquire it while still inactive, before its entry FSM activates it.
        if(current!=null)current.HideGodhomeArrival(scene);
    }
    private void HideGodhomeArrival(Scene scene) {
        if(scene.name!="GG_Atrium")return;
        var entry=scene.GetRootGameObjects().SingleOrDefault(o=>o.name=="Dream Entry");
        var arrival=entry?.transform.Find("Knight Dream Arrival");
        if(arrival==null)return;
        // Attack Pt is an inactive/animated descendant of the separate arrival
        // sprite. Hide the entire presentation hierarchy, not only its body.
        bool newlyBound=false;
        foreach(var renderer in arrival.GetComponentsInChildren<Renderer>(true)) {
            if(!hidden.ContainsKey(renderer)){hidden.Add(renderer,renderer.forceRenderingOff);newlyBound=true;}
            renderer.forceRenderingOff=true;
        }
        // Arrive Anim uses AudioPlaySimple on Dream Entry's own AudioSource.
        // Muting that exact owner also covers descendant animation audio while
        // leaving native animation-completion/control events and timing intact.
        foreach(var audio in entry!.GetComponentsInChildren<AudioSource>(true)) {
            if(!mutedArrivalAudio.ContainsKey(audio))mutedArrivalAudio.Add(audio,audio.mute);
            audio.mute=true;
        }
        if(newlyBound)HollowSolsMenuPlugin.Instance?.Log("HK_GODHOME_KNIGHT_ARRIVAL_HIDDEN bodyAndParticles=True arrivalAudioMuted=True nativeEntryCallbacks=True restoreOnTeardown=True");
    }
    internal static void HideNativeLoadingGraphic(Component __instance) {
        var owner=current;if(owner==null || !World(UnitySceneManager.GetActiveScene().name))return;
        var image=AccessTools.Field(__instance.GetType(),"image").GetValue(__instance) as Graphic;
        if(image==null)return;
        if(!owner.hiddenLoadingGraphics.ContainsKey(image))owner.hiddenLoadingGraphics.Add(image,image.enabled);
        image.enabled=false; // Leave the native spinner/loading callbacks running.
    }
    private static bool World(string name)=>name!="Menu_Title" && name!="Quit_To_Menu" && name!="Opening_Sequence"
        && name!="GG_Entrance_Cutscene" && name!="Knight_Pickup" && !name.Contains("Intro",StringComparison.OrdinalIgnoreCase)
        && !name.Contains("Cinematic",StringComparison.OrdinalIgnoreCase) && !name.Contains("Cutscene",StringComparison.OrdinalIgnoreCase);
    private void Build() {
        var ui=UIManager.instance??throw new InvalidOperationException("Native bridge loading UI unavailable.");
        var dimmer=(CanvasGroup)AccessTools.Field(typeof(UIManager),"modalDimmer").GetValue(ui);
        var pause=(MenuScreen)AccessTools.Field(typeof(UIManager),"pauseMenuScreen").GetValue(ui);
        var text=pause?.GetComponentsInChildren<Text>(true).FirstOrDefault(t=>t.font!=null && !string.IsNullOrEmpty(t.text));
        if(text==null) {
            var main=(CanvasGroup)AccessTools.Field(typeof(UIManager),"mainMenuScreen").GetValue(ui);
            text=main?.GetComponentsInChildren<Text>(true).FirstOrDefault(t=>t.font!=null && !string.IsNullOrEmpty(t.text));
        }
        if(dimmer==null || text==null)throw new InvalidOperationException("Native bridge dimmer/typography unavailable.");
        var canvas=dimmer.GetComponentInParent<Canvas>()??throw new InvalidOperationException("Native bridge UI canvas unavailable.");
        panel=new GameObject("Bridging Yi",typeof(RectTransform),typeof(CanvasGroup));panel.layer=dimmer.gameObject.layer;
        panel.SetActive(false);panel.transform.SetParent(dimmer.transform.parent,false);panel.transform.SetAsLastSibling();
        var rect=(RectTransform)panel.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
        group=panel.GetComponent<CanvasGroup>();group.interactable=false;group.blocksRaycasts=true;
        var dim=Instantiate(dimmer.gameObject,panel.transform,false);dim.name="Native pause dimmer";
        dim.SetActive(true);dim.GetComponent<CanvasGroup>().alpha=.8f;
        var caption=Instantiate(text.gameObject,panel.transform,false);caption.name="Bridging Yi caption";
        // The native label shares its object with selection-cursor children.
        // A status caption is not a selected menu action.
        foreach(Transform child in caption.transform)child.gameObject.SetActive(false);
        foreach(var behaviour in caption.GetComponents<MonoBehaviour>())if(behaviour is not Text)behaviour.enabled=false;
        foreach(var animator in caption.GetComponentsInChildren<Animator>(true))animator.enabled=false;
        foreach(var opacity in caption.GetComponentsInChildren<CanvasGroup>(true))opacity.alpha=1;
        var label=caption.GetComponent<Text>();captionText=label;label.text=captions[0];label.supportRichText=true;label.color=Color.white;label.alignment=TextAnchor.MiddleCenter;label.raycastTarget=false;
        label.canvasRenderer.SetAlpha(1);caption.SetActive(true);
        var captionRect=(RectTransform)caption.transform;captionRect.anchorMin=captionRect.anchorMax=captionRect.pivot=new Vector2(.5f,.5f);
        captionRect.anchoredPosition=Vector2.zero;captionRect.sizeDelta=new Vector2(1000,100);
        var sourceScale=text.transform.lossyScale;var targetScale=panel.transform.lossyScale;
        captionRect.localScale=new Vector3(sourceScale.x/targetScale.x,sourceScale.y/targetScale.y,1);
    }
    private void ObserveScene(Scene scene) {
        if(!World(scene.name)) {
            if(panel!=null)panel.SetActive(false);
            foreach(var entry in hidden)if(entry.Key!=null)entry.Key.forceRenderingOff=entry.Value;
            cachedHero=null;return;
        }
        if(room==scene.handle) {
            // Native entry temporarily activates the additive hero scene and
            // closes menu groups. Reassert our still-pending world mask when
            // the same gameplay scene becomes active again.
            if(alpha>0 && panel!=null && !panel.activeSelf)panel.SetActive(true);
            return;
        }
        HideGodhomeArrival(scene);
        room=scene.handle;alpha=initialRevealCompleted?0:1;revealing=false;cachedHero=null;capturedDots=0;capturedGesture=false;nativeLoadingCanvas=null;group!.alpha=alpha;panel!.SetActive(!initialRevealCompleted);
        if(scene.name=="GG_Atrium") {
            // The bench controller owns a different object from its flat seat
            // artwork. The retained seat looks like an early black Root base.
            // Installed scene424 identifies this standalone SpriteRenderer.
            var seat=scene.GetRootGameObjects().SingleOrDefault(o=>o.name=="GG_bench_0001_3")?.GetComponent<SpriteRenderer>();
            if(seat!=null) {
                if(!hidden.ContainsKey(seat))hidden.Add(seat,seat.forceRenderingOff);
                seat.forceRenderingOff=true;
                HollowSolsMenuPlugin.Instance?.Log("HK_NATIVE_BENCH_SEAT_HIDDEN replacement=RootNode rendererOnly=True restoreOnTeardown=True");
            }
        }
        foreach(var dead in hidden.Keys.Where(r=>r==null).ToArray())hidden.Remove(dead);
        SuppressKnight();
        if(!initialRevealCompleted)HollowSolsMenuPlugin.Instance?.FixtureLoadingPresentation("waiting",scene.handle);
    }
    internal static void SuppressKnight() {
        var owner=current;var hero=HeroController.instance;
        string scene=UnitySceneManager.GetActiveScene().name;
        if(owner==null || hero==null || !World(scene) && scene!="Knight_Pickup")return;
        if(owner.cachedHero!=hero)foreach(var renderer in hero.GetComponentsInChildren<Renderer>(true)) {
            if(!owner.hidden.ContainsKey(renderer))owner.hidden.Add(renderer,renderer.forceRenderingOff);
        }
        owner.cachedHero=hero;
        foreach(var entry in owner.hidden)if(entry.Key!=null)entry.Key.forceRenderingOff=true;
    }
    internal static bool PlayerReady(bool ready,bool imageReady) {
        var owner=current;if(owner==null)return ready;
        if(!NativeSaveContext.Session.HasValue || !owner.token.Equals(NativeSaveContext.Session.Value))return false;
        if(HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed==true && owner.traceCount<10 && Time.realtimeSinceStartup>=owner.nextTrace) {
            owner.nextTrace=Time.realtimeSinceStartup+1;owner.traceCount++;
            HollowSolsMenuPlugin.Instance.FixtureLoadingTrace("ready="+ready+" image="+imageReady+" panel="+(owner.panel!=null)+" active="+(owner.panel!=null && owner.panel.activeSelf)+" room="+owner.room+" current="+UnitySceneManager.GetActiveScene().handle);
        }
        if(ready && imageReady && owner.panel!=null && owner.panel.activeSelf && owner.room==UnitySceneManager.GetActiveScene().handle && !owner.revealing) {
            owner.revealing=true;HollowSolsMenuPlugin.Instance?.FixtureLoadingPresentation("reveal",owner.room);
        }
        // An interruption during the fade must not release input on a stale image.
        if(!ready || !imageReady)owner.revealing=false;
        return ready && !Blocking;
    }
    internal static void CaptureEntryGesture(bool active,bool imageReady) {
        var owner=current;
        if(owner!=null && owner.initialRevealCompleted && imageReady && !active)owner.initialEntryFinished=true;
        if(owner==null || owner.capturedGesture || !active || !imageReady)return;
        owner.capturedGesture=true;HollowSolsMenuPlugin.Instance?.FixtureLoadingPresentation("entry-gesture",owner.room);
    }
    private void Update() {
        if(current!=this)return;
        if(!NativeSaveContext.Session.HasValue || !NativeSaveContext.Session.Value.Equals(token)){End();return;}
        if(!HudGlobalHide.IsHidden)HudGlobalHide.IsHidden=true;
        if(panel==null){Build();room=int.MinValue;}
        ObserveScene(UnitySceneManager.GetActiveScene());SuppressKnight();
        int dots=(int)(Time.realtimeSinceStartup/.4f)%captions.Length;
        if(captionText!=null)captionText.text=captions[dots];
        if(panel!=null && panel.activeSelf && !revealing && GameManager.instance?.HasFinishedEnteringScene==true && (capturedDots&(1<<dots))==0) {
            capturedDots|=1<<dots;HollowSolsMenuPlugin.Instance?.FixtureLoadingPresentation("dots-"+(dots+1),room);
        }
        if(revealing && panel!=null && panel.activeSelf) {
            alpha=Mathf.Max(0,alpha-Time.unscaledDeltaTime/.25f);group!.alpha=alpha;
            if(alpha==0){initialRevealCompleted=true;panel.SetActive(false);HollowSolsMenuPlugin.Instance?.FixtureLoadingPresentation("ready",room);}
        }
    }
    private void Restore() {
        foreach(var entry in hidden)if(entry.Key!=null)entry.Key.forceRenderingOff=entry.Value;
        hidden.Clear();HudGlobalHide.IsHidden=oldHud;
        foreach(var entry in mutedArrivalAudio)if(entry.Key!=null)entry.Key.mute=entry.Value;
        mutedArrivalAudio.Clear();
        foreach(var entry in hiddenLoadingGraphics)if(entry.Key!=null)entry.Key.enabled=entry.Value;
        hiddenLoadingGraphics.Clear();
        if(panel!=null)Destroy(panel);panel=null;
    }
    private void LateUpdate() {
        if(current!=this)return;
        if(!World(UnitySceneManager.GetActiveScene().name))return;
        if(nativeLoadingCanvas==null) {
            nativeLoadingCanvas=UnityEngine.Object.FindFirstObjectByType<LoadingCanvas>(FindObjectsInactive.Include);
            nativeLoadingGraphics=nativeLoadingCanvas==null?Array.Empty<Graphic>():nativeLoadingCanvas.GetComponentsInChildren<Graphic>(true);
        }
        foreach(var graphic in nativeLoadingGraphics)if(graphic!=null && (panel==null || !graphic.transform.IsChildOf(panel.transform))) {
            if(!hiddenLoadingGraphics.ContainsKey(graphic))hiddenLoadingGraphics.Add(graphic,graphic.enabled);
            graphic.enabled=false;
        }
    }
}
