using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace YiGodhome;

// Opens the real peer menu while retaining the host's paused encounter.
internal sealed class HollowSolsNativePause : IDisposable
{
    internal static bool Requested,Active,PeerVisible,PeerBinding;
    internal static float FixtureHoldUntil;
    private static float closeRequestedAt=-1;
    internal static bool ForwardClose=>Active && closeRequestedAt>=0 && (Time.realtimeSinceStartup-closeRequestedAt)%.4f<.12f;
    internal static void RequestClose(){if(Active && !PeerBinding && closeRequestedAt<0)closeRequestedAt=Time.realtimeSinceStartup;}
    private MenuScreen? screen;
    private CanvasGroup? content;
    private GameObject? row;
    private HollowSolsChoice? choice;
    private MenuButtonList? list;
    private Array? entries;
    private readonly Action<string> log;
    private readonly System.Collections.Generic.List<(RectTransform Rect,Vector2 Position)> positions=new();
    private float contentAlpha,controlsAlpha,requestedAt;
    private bool seen,contentInteractable,controlsInteractable,contentRays,controlsRays;
    private bool unavailable;
    internal HollowSolsNativePause(Action<string> log)=>this.log=log;
    internal void Update() {
        if(unavailable)return;
        var ui=UIManager.instance;
        if(ui==null || GameManager.instance?.profileID!=4)return;
        var pause=AccessTools.Field(typeof(UIManager),"pauseMenuScreen").GetValue(ui) as MenuScreen;
        if(pause!=null && screen!=pause) {
            try {Dispose();Bind(pause);}catch(Exception error){Dispose();unavailable=true;log("HK_NATIVE_PAUSE_UNAVAILABLE reason="+error);}
        }
        if(!Active)return;
        if(PeerBinding)closeRequestedAt=-1;
        if(PeerVisible) {seen=true;Requested=false;}
        if(seen&&!PeerVisible || !seen&&Time.realtimeSinceStartup-requestedAt>5) Close();
    }
    private void Bind(MenuScreen pause) {
        content=pause.content??pause.screenCanvasGroup;
        if(content==null)return;
        list=pause.GetComponentInChildren<MenuButtonList>(true);
        if(list==null)throw new InvalidOperationException("Native pause button list missing.");
        var field=AccessTools.Field(typeof(MenuButtonList),"entries");entries=(Array)field.GetValue(list);
        var selectable=AccessTools.Field(entries.GetType().GetElementType(),"selectable");
        var buttons=entries.Cast<object>().Select(e=>(Selectable)selectable.GetValue(e)).ToArray();
        if(buttons.Length<3 || buttons.Length>8)throw new InvalidOperationException("Native pause row count changed.");
        RectTransform Rect(Selectable s)=>(RectTransform)(s.transform.parent!=null
            && s.transform.parent.GetComponentsInChildren<MenuSelectable>(true).Length==1?s.transform.parent:s.transform);
        var rows=buttons.Select(Rect).ToArray();
        foreach(var r in rows)positions.Add((r,r.anchoredPosition));
        var ordered=rows.OrderByDescending(r=>content.transform.InverseTransformPoint(r.position).y).ToArray();
        var first=content.transform.InverseTransformPoint(ordered[0].position);
        float gap=first.y-content.transform.InverseTransformPoint(ordered[1].position).y;
        float compact=gap*buttons.Length/(buttons.Length+1f);
        for(int i=0;i<ordered.Length;i++) {
            var p=content.transform.InverseTransformPoint(ordered[i].position);p.y=first.y-compact*i;
            ordered[i].position=content.transform.TransformPoint(p);
        }
        // Pause content is not necessarily the row's authored fade parent.
        // Retain that hierarchy so the new text follows the vanilla reveal.
        choice=HollowSolsConfiguration.NativeChoice(ordered[0],ordered[0].parent,"Open Nine Sols Controls",Open,()=>uiBack(),out row,true);
        row.transform.position=content.transform.TransformPoint(new Vector3(first.x,first.y-compact*buttons.Length,first.z));
        var updated=Array.CreateInstance(entries.GetType().GetElementType()!,entries.Length+1);
        Array.Copy(entries,updated,entries.Length);var own=Activator.CreateInstance(entries.GetType().GetElementType()!)!;
        selectable.SetValue(own,choice);updated.SetValue(own,entries.Length);field.SetValue(list,updated);
        AccessTools.Method(typeof(MenuButtonList),"SetupActive").Invoke(list,null);
        screen=pause;
        log("HK_NATIVE_PAUSE_SELECTOR_BOUND nativeArtwork=True nativeNavigation=True position=afterQuit");
    }
    private void uiBack() {if(Active)return;if(GameManager.instance?.IsGamePaused()==true)GameManager.instance.StartCoroutine(GameManager.instance.PauseGameToggle());}
    private void Open() {
        if(Active || screen==null || GameManager.instance?.IsGamePaused()!=true) {
            log("HK_NATIVE_PAUSE_OPEN_REJECTED active="+Active+" peer="+PeerVisible+" binding="+PeerBinding+" screen="+(screen!=null)+" state="+GameManager.instance?.gameState+" paused="+GameManager.instance?.IsGamePaused());return;
        }
        contentAlpha=content!.alpha;controlsAlpha=screen.controls?.alpha??0;
        contentInteractable=content!.interactable;controlsInteractable=screen.controls?.interactable??false;
        contentRays=content!.blocksRaycasts;controlsRays=screen.controls?.blocksRaycasts??false;
        content!.alpha=0;content!.interactable=false;
        if(screen.controls!=null){screen.controls.alpha=0;screen.controls.interactable=false;screen.controls.blocksRaycasts=false;}
        content!.blocksRaycasts=false;
        Requested=Active=true;seen=false;requestedAt=Time.realtimeSinceStartup;closeRequestedAt=-1;
        if(YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures && YiGodhome.Bridge.SessionConfiguration.Current.Has("configuration-probe"))FixtureHoldUntil=Time.realtimeSinceStartup+1.5f;
        log("HK_NATIVE_PAUSE_REQUEST hostPausePreserved=True authenticatedInput=True");
    }
    internal void FixtureOpen() {
        if(HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed != true && (!YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures || !YiGodhome.Bridge.SessionConfiguration.Current.Has("configuration-probe")))throw new InvalidOperationException("Unauthorized pause menu fixture.");
        Open();
    }
    internal void FixtureHiddenCancel() {
        if(HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed!=true || !Active || choice==null)
            throw new InvalidOperationException("Hidden pause cancel fixture is not authorized.");
        choice.OnCancel(new UnityEngine.EventSystems.BaseEventData(UnityEngine.EventSystems.EventSystem.current));
        if(ForwardClose)throw new InvalidOperationException("Hidden HK Cancel closed the native binding listener.");
        var audio=AccessTools.Field(typeof(MenuSelectable),"uiAudioPlayer").GetValue(choice) as MenuAudioController;
        if(audio==null)throw new InvalidOperationException("Native menu audio fixture owner missing.");
        long muted=NativePairedGame.MutedHostMenuSounds;
        foreach(var method in new[]{"PlaySelect","PlaySubmit","PlayCancel","PlaySlider"})AccessTools.Method(typeof(MenuAudioController),method).Invoke(audio,null);
        if(NativePairedGame.MutedHostMenuSounds-muted!=4)throw new InvalidOperationException("HK menu sound leaked into NS Controls.");
        log("HK_PEER_MENU_SOUND_PASS nativeMethodsSuppressed=4 counterpartSoundUnchanged=True");
        log("HK_PEER_MENU_CANCEL_IGNORED nativeCancelOwnedByNineSols=True");
    }
    internal void FixtureVisibility() {
        if(HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed!=true || choice==null || entries==null)throw new InvalidOperationException("Pause visibility fixture is not ready.");
        var field=AccessTools.Field(entries.GetType().GetElementType(),"selectable");
        var donor=(Selectable)field.GetValue(entries.GetValue(0));
        var text=donor.GetComponentsInChildren<Text>(true).First(t=>!string.IsNullOrEmpty(t.text));
        float Alpha(Text label)=>label.color.a*label.canvasRenderer.GetAlpha()*label.GetComponentsInParent<CanvasGroup>(true).Aggregate(1f,(value,group)=>value*group.alpha);
        float native=Alpha(text),own=Alpha(choice.Label);
        if(Mathf.Abs(native-own)>.01f)throw new InvalidOperationException("Pause row faded out of sync: native="+native+" own="+own);
        log("HK_PAUSE_FADE_MATCH native="+native+" own="+own+" nativeParent="+text.transform.parent.name+" ownedParent="+choice.Label.transform.parent.name);
    }
    private void Close() {
        Requested=Active=false;seen=false;closeRequestedAt=-1;
        if(screen!=null) {
            content!.alpha=contentAlpha;if(screen.controls!=null)screen.controls.alpha=controlsAlpha;
            content!.interactable=contentInteractable;if(screen.controls!=null)screen.controls.interactable=controlsInteractable;
            content!.blocksRaycasts=contentRays;if(screen.controls!=null)screen.controls.blocksRaycasts=controlsRays;
            choice?.Select();
        }
    }
    public void Dispose() {
        if(Active)Close();Requested=Active=PeerVisible=PeerBinding=false;
        if(list!=null && entries!=null)AccessTools.Field(typeof(MenuButtonList),"entries").SetValue(list,entries);
        foreach(var p in positions)if(p.Rect!=null)p.Rect.anchoredPosition=p.Position;positions.Clear();
        if(row!=null)UnityEngine.Object.Destroy(row);row=null;choice=null;list=null;entries=null;screen=null;content=null;
    }
}
