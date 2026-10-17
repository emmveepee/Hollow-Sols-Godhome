using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace YiGodhome;

// Own only the selector and its panel; native Options and native pause remain owners.
internal sealed class HollowSolsConfiguration : IDisposable
{
    private readonly Action<string> log;
    private readonly HollowSolsNativePause nativePause;
    private MenuScreen? screen;
    private MenuButtonList? list;
    private Array? originalEntries;
    private GameObject? selector, panel;
    private HollowSolsChoice? selectorChoice, shared, contact, trueContact;
    private readonly List<(RectTransform Rect, Vector2 Position)> positions=new();
    private readonly List<(Selectable Selectable, Navigation Navigation)> navigation=new();
    private Font? font;
    private RectTransform? nativeChoiceTemplate;
    private readonly List<GameObject> ownedRows=new();
    private bool open;
    private MenuScreen? panelScreen;
    private Coroutine? transition;
    private CanvasGroup? checkbox;
    private GameObject? checkmark;
    private float screenAlpha;
    private bool screenInput,screenRays;
    private int polishStage,polishCapture;
    private float polishAt;
    internal bool FixturePolish(string folder) {
        if(HollowSolsMenuPlugin.Instance?.FixtureCommandsAllowed!=true)throw new InvalidOperationException("UI fixture requires the disposable paired session.");
        float now=Time.realtimeSinceStartup;
        if(polishStage==0){polishAt=now;polishStage=1;}
        if(polishStage==1) {
            if(polishCapture<4 && now-polishAt>=new[]{.04f,.16f,.30f,.60f}[polishCapture]) {
                nativePause.FixtureVisibility();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder,"pause-reveal-"+polishCapture+".png"));polishCapture++;
            }
            if(now-polishAt>1){UIManager.instance.UIGoToOptionsMenu();polishStage=2;polishAt=now;}
        } else if(polishStage==2 && now-polishAt>1.2f) {
            selectorChoice!.Select();
            polishStage=21;polishAt=now;
        } else if(polishStage==21 && now-polishAt>.35f) {
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder,"native-options-selector.png"));polishStage=22;polishAt=now;
        } else if(polishStage==22 && now-polishAt>.2f) {
            savedShared=HollowSolsSettings.SharedHealth;savedContact=HollowSolsSettings.Contact;savedTrue=HollowSolsSettings.StoredTrueContact;fixtureSettingsCaptured=true;
            Open();polishStage=3;polishAt=now;
        } else if(polishStage==3 && now-polishAt>1.2f) {
            if(!open || EventSystem.current.currentSelectedGameObject!=shared!.gameObject)throw new InvalidOperationException("Native configuration transition/focus failed.");
            HollowSolsSettings.Contact=true;HollowSolsSettings.TrueContact=true;Refresh();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder,"native-configuration-checked.png"));polishStage=4;polishAt=now;
        } else if(polishStage==4 && now-polishAt>.4f) {
            HollowSolsSettings.TrueContact=false;Refresh();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder,"native-configuration-unchecked.png"));polishStage=5;polishAt=now;
        } else if(polishStage==5 && now-polishAt>.4f) {
            HollowSolsSettings.SharedHealth=savedShared;HollowSolsSettings.Contact=savedContact;HollowSolsSettings.TrueContact=savedTrue;fixtureSettingsCaptured=false;
            Close(true);polishStage=6;polishAt=now;
        } else if(polishStage==6 && transition==null && now-polishAt>1) {
            if(EventSystem.current.currentSelectedGameObject!=selector)throw new InvalidOperationException("Native configuration return focus failed.");
            UIManager.instance.UILeaveOptionsMenu();polishStage=7;polishAt=now;
        } else if(polishStage==7 && now-polishAt>1.2f){polishStage=8;log("HK_UI_POLISH_FIXTURE_COMPLETE nativeTransition=True nativeOptionsClone=True rightCheckbox=True settingsRestored=True");}
        return polishStage==8;
    }
    private bool unavailable;
    private static HollowSolsConfiguration? current;
    private readonly Harmony fadeScope=new Harmony("local.hollowsols.hk.configuration-fades");
    private int fixtureStage;
    private float fixtureAt;
    private bool savedShared, savedContact, savedTrue;
    private bool fixtureSettingsCaptured;
    internal bool FixtureComplete => fixtureStage==12;
    internal void Fixture(bool ready) {
        if(!YiGodhome.Bridge.SessionConfiguration.Current.Has("configuration-probe")) return;
        if(!YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures) throw new InvalidOperationException("Unauthorized configuration fixture.");
        if(unavailable) throw new InvalidOperationException("Configuration fixture cannot bind native Options.");
        var gm=GameManager.instance; var ui=UIManager.instance;
        if(screen==null || gm==null || ui==null || FixtureComplete) return;
        float now=Time.realtimeSinceStartup;
        string path=System.IO.Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
        if(fixtureStage>0 && fixtureStage!=12 && now-fixtureAt>12)throw new InvalidOperationException("Configuration fixture stage "+fixtureStage+" exceeded its finite UI deadline.");
        if(fixtureStage==0 && ready) { gm.StartCoroutine(gm.PauseGameToggle()); fixtureStage=1; fixtureAt=now; }
        else if(fixtureStage==1 && gm.IsGamePaused() && now-fixtureAt>1) { ui.UIGoToOptionsMenu(); fixtureStage=2; fixtureAt=now; }
        else if(fixtureStage==2 && now-fixtureAt>2 && screen.screenCanvasGroup.alpha>.99f) {
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(path,"configuration-options.png")); fixtureStage=3; fixtureAt=now;
        } else if(fixtureStage==3 && now-fixtureAt>1) {
            savedShared=HollowSolsSettings.SharedHealth; savedContact=HollowSolsSettings.Contact; savedTrue=HollowSolsSettings.StoredTrueContact;
            fixtureSettingsCaptured=true;
            ExecuteEvents.Execute(selector!,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);
            if(!open) throw new InvalidOperationException("Configuration submit failed.");
            fixtureStage=4; fixtureAt=now;
        } else if(fixtureStage==4 && now-fixtureAt>1) {
            if(transition!=null || EventSystem.current.currentSelectedGameObject!=shared!.gameObject)throw new InvalidOperationException("Configuration native transition/default selection failed.");
            log("HK_CONFIGURATION_CONTACT_ADMISSION active="+contact!.isActiveAndEnabled+" interactable="+contact.interactable+" admitted="+contact.IsInteractable()
                +" groups="+string.Join(";",contact.GetComponentsInParent<CanvasGroup>(true).Take(8).Select(g=>g.name+":"+g.interactable+":"+g.ignoreParentGroups+":"+g.alpha)));
            ExecuteEvents.Execute(contact!.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);
            if(HollowSolsSettings.Contact==savedContact || trueContact!.IsInteractable()!=HollowSolsSettings.Contact) throw new InvalidOperationException("True contact dependency/toggle failed.");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(path,"configuration-panel.png")); fixtureStage=5; fixtureAt=now;
        } else if(fixtureStage==5 && now-fixtureAt>1) {
            HollowSolsSettings.SharedHealth=savedShared; HollowSolsSettings.Contact=savedContact; HollowSolsSettings.TrueContact=savedTrue;
            fixtureSettingsCaptured=false;
            ExecuteEvents.Execute(shared!.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.cancelHandler);
            if(open)throw new InvalidOperationException("Configuration cancel failed.");
            fixtureStage=55;fixtureAt=now;
        } else if(fixtureStage==55 && transition==null && now-fixtureAt>.1f) {
            if(EventSystem.current.currentSelectedGameObject!=selector)throw new InvalidOperationException("Configuration native Options restoration failed.");
            ui.UILeaveOptionsMenu();fixtureStage=6;fixtureAt=now;
        } else if(fixtureStage==6 && now-fixtureAt>2) {
            if(gm.IsGamePaused()) gm.StartCoroutine(gm.PauseGameToggle()); fixtureStage=7; fixtureAt=now;
        } else if(fixtureStage==7 && !gm.IsGamePaused() && now-fixtureAt>1) {
            gm.StartCoroutine(gm.PauseGameToggle());fixtureStage=8;fixtureAt=now;
        } else if(fixtureStage==8 && gm.IsGamePaused() && now-fixtureAt>1) {
            nativePause.FixtureOpen();fixtureStage=9;fixtureAt=now;
        } else if(fixtureStage==9 && HollowSolsNativePause.PeerVisible && now-fixtureAt>.8f) {
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(path,"nine-sols-native-pause.png"));fixtureStage=10;fixtureAt=now;
        } else if(fixtureStage==10 && !HollowSolsNativePause.Active && now-fixtureAt>.5f) {
            if(gm.IsGamePaused())gm.StartCoroutine(gm.PauseGameToggle());fixtureStage=11;fixtureAt=now;
        } else if(fixtureStage==11 && !gm.IsGamePaused() && now-fixtureAt>1) {
            fixtureStage=12;
            log("HK_CONFIGURATION_FIXTURE_COMPLETE nativeSubmit=True nativeCancel=True nativePauseRoundTrip=True contactDependency=True settingsRestored=True saveData=False");
        }
    }
    private float contentAlpha, controlsAlpha;
    private bool contentInteractable, contentRays, controlsInteractable, controlsRays;
    internal HollowSolsConfiguration(Action<string> log) { this.log=log;nativePause=new HollowSolsNativePause(log); }
    internal void FixtureNativeControls(bool cancel=false) {if(cancel)nativePause.FixtureHiddenCancel();else nativePause.FixtureOpen();}
    internal void Update() {
        nativePause.Update();
        if(unavailable) return;
        var ui=UIManager.instance;
        if(ui == null || GameManager.instance?.profileID != 4) return;
        var options=AccessTools.Field(typeof(UIManager),"optionsMenuScreen").GetValue(ui) as MenuScreen;
        if(options == null) return;
        if(screen != options) { try { Dispose(); Bind(options); } catch(Exception error) { Dispose(); unavailable=true; log("HK_CONFIGURATION_UNAVAILABLE reason="+error); return; } }
        if(open && !screen!.gameObject.activeInHierarchy) Close(false);
    }
    private void Bind(MenuScreen options) {
        current=this;
        foreach(var name in new[]{"FadeInCanvasGroup","FadeOutCanvasGroup"})
            fadeScope.Patch(AccessTools.Method(typeof(UIManager),name),postfix:new HarmonyMethod(typeof(HollowSolsConfiguration),nameof(ScopeOwnedFade)));
        screen=options;
        list=options.GetComponentInChildren<MenuButtonList>(true);
        var entriesField=AccessTools.Field(typeof(MenuButtonList),"entries");
        var native=new List<Selectable>();
        if(list != null) {
            originalEntries=(Array)entriesField.GetValue(list);
            var field=AccessTools.Field(originalEntries.GetType().GetElementType(),"selectable");
            foreach(var entry in originalEntries) if(field.GetValue(entry) is Selectable s && s != null) native.Add(s);
        } else native.AddRange(options.content.GetComponentsInChildren<MenuSelectable>(true));
        if(native.Count<2 || native.Count>16) throw new InvalidOperationException("Native Options row contract differs.");
        var rows=native.Select(button=>(Button:button,Rect:(RectTransform)(button.transform.parent==options.content.transform?button.transform:button.transform.parent))).ToList();
        if(rows.Select(row=>row.Rect).Distinct().Count()!=rows.Count) throw new InvalidOperationException("Native Options rows must have independent authored owners.");
        rows=rows.OrderByDescending(row=>options.content.transform.InverseTransformPoint(row.Rect.position).y).ToList();
        native=rows.Select(row=>row.Button).ToList();
        nativeChoiceTemplate=rows.First(row=>row.Button.GetType().Name=="MenuButton").Rect;
        log("HK_CONFIGURATION_NATIVE_ROWS "+string.Join(";",rows.Select(row=>row.Button.name+":"+row.Rect.name+":"+options.content.transform.InverseTransformPoint(row.Rect.position))));
        font=options.GetComponentsInChildren<Text>(true).FirstOrDefault(t=>t.font!=null)?.font ?? throw new InvalidOperationException("Native Options font missing.");
        foreach(var row in rows) positions.Add((row.Rect,row.Rect.anchoredPosition));
        var first=options.content.transform.InverseTransformPoint(rows[0].Rect.position);
        float gap=Math.Abs(first.y-options.content.transform.InverseTransformPoint(rows[1].Rect.position).y);
        if(gap<20 || gap>250) throw new InvalidOperationException("Native Options row spacing differs.");
        bool includesBack=native[native.Count-1].name=="ApplyButton" || native[native.Count-1].name.IndexOf("back",StringComparison.OrdinalIgnoreCase)>=0;
        int ownIndex=includesBack?native.Count-1:native.Count;
        float compactGap=gap*native.Count/(native.Count+1f);
        for(int i=0;i<rows.Count;i++) {
            var point=options.content.transform.InverseTransformPoint(rows[i].Rect.position);
            point.y=first.y-compactGap*(includesBack && i==rows.Count-1?i+1:i);
            rows[i].Rect.position=options.content.transform.TransformPoint(point);
        }
        selectorChoice=NativeChoice(nativeChoiceTemplate!,nativeChoiceTemplate!.parent,"Hollow Sols Configuration",Open,uiBack,out var selectorRow,true);
        ownedRows.Add(selectorRow);
        selector=selectorChoice.gameObject;
        ownedRows[0].transform.position=options.content.transform.TransformPoint(new Vector3(first.x,first.y-compactGap*ownIndex,first.z));
        if(list != null && originalEntries != null) {
        var selectableField=AccessTools.Field(originalEntries.GetType().GetElementType(),"selectable");
        var entries=Array.CreateInstance(originalEntries.GetType().GetElementType()!,originalEntries.Length+1);
        var own=Activator.CreateInstance(originalEntries.GetType().GetElementType()!)!;
        selectableField.SetValue(own,selectorChoice);
        int destination=0; bool inserted=false;
        foreach(var entry in originalEntries) {
            if(includesBack && !inserted && selectableField.GetValue(entry) is Selectable s && s==native[native.Count-1]) { entries.SetValue(own,destination++); inserted=true; }
            entries.SetValue(entry,destination++);
        }
        if(!inserted) entries.SetValue(own,destination);
        entriesField.SetValue(list,entries);
        AccessTools.Method(typeof(MenuButtonList),"SetupActive").Invoke(list,null);
        } else {
            native.Insert(ownIndex,selectorChoice);
            foreach(var s in options.controls.GetComponentsInChildren<MenuSelectable>(true)) if(!native.Contains(s)) native.Add(s);
            for(int i=0;i<native.Count;i++) {
                var s=native[i]; navigation.Add((s,s.navigation));
                s.navigation=new Navigation {mode=Navigation.Mode.Explicit,selectOnUp=native[(i+native.Count-1)%native.Count],selectOnDown=native[(i+1)%native.Count]};
            }
        }
        // The native list rebuilds navigation on activation from its registered entries.
        BuildNativePanel(options);
        Label(panelScreen!.content.transform,"Changes apply to the next fight",first.y,30);
        shared=Choice(panelScreen.content.transform,"",first.y-gap,()=>{HollowSolsSettings.SharedHealth=!HollowSolsSettings.SharedHealth;Refresh();},()=>Close(true));
        contact=Choice(panelScreen.content.transform,"",first.y-gap*2,()=>{HollowSolsSettings.Contact=!HollowSolsSettings.Contact;Refresh();},()=>Close(true));
        trueContact=Choice(panelScreen.content.transform,"True Global Contact Damage",first.y-gap*3,()=>{HollowSolsSettings.TrueContact=!HollowSolsSettings.TrueContact;Refresh();},()=>Close(true));
        BuildCheckbox(trueContact);
        var nativeBack=rows[rows.Count-1].Rect;
        var back=NativeChoice(nativeBack,panel!.transform,"Back",()=>Close(true),()=>Close(true),out var backRow);
        ownedRows.Add(backRow);
        backRow.transform.position=nativeBack.position;
        var controlPath=new Stack<string>();
        if(!options.controls.transform.IsChildOf(nativeBack) && options.controls.transform!=nativeBack)throw new InvalidOperationException("Native back fade owner is outside its row.");
        for(var node=options.controls.transform;node!=nativeBack;node=node.parent)controlPath.Push(node.name);
        panelScreen.controls=(controlPath.Count==0?backRow.transform:backRow.transform.Find(string.Join("/",controlPath))).GetComponent<CanvasGroup>();
        var choices=new[]{shared,contact,trueContact,back};
        for(int i=0;i<choices.Length;i++) choices[i].navigation=new Navigation {mode=Navigation.Mode.Explicit,selectOnUp=choices[(i+choices.Length-1)%choices.Length],selectOnDown=choices[(i+1)%choices.Length]};
        panelScreen.defaultHighlight=shared;
        panel!.SetActive(false);
        log("HK_CONFIGURATION_BOUND nativeOptions=True rows="+native.Count+" saveData=False");
    }
    private static void ScopeOwnedFade(CanvasGroup __0,ref System.Collections.IEnumerator __result) {
        var owner=current;var expected=owner?.panel;
        if(owner==null || expected==null || __0==null || !__0.transform.IsChildOf(expected.transform))return;
        __result=owner.OwnedFade(__result,expected);
    }
    private System.Collections.IEnumerator OwnedFade(System.Collections.IEnumerator fade,GameObject expected) {
        while(panel==expected && expected!=null && expected.activeSelf && fade.MoveNext())yield return fade.Current;
    }
    private void BuildNativePanel(MenuScreen donor) {
        var staging=new GameObject("Hollow Sols inactive native options");staging.SetActive(false);
        panel=UnityEngine.Object.Instantiate(donor.gameObject,staging.transform,false);panel.SetActive(false);
        panel.name="Hollow Sols Configuration";panel.transform.SetParent(donor.transform.parent,false);UnityEngine.Object.Destroy(staging);
        panelScreen=panel.GetComponent<MenuScreen>();
        // ShowMenu invokes SetupActive directly even on a disabled list. Remove
        // the owned donor list so it cannot resurrect its original rows.
        foreach(var donorList in panel.GetComponentsInChildren<MenuButtonList>(true))UnityEngine.Object.DestroyImmediate(donorList);
        foreach(var behaviour in panel.GetComponentsInChildren<MonoBehaviour>(true)) {
            if(behaviour is MenuButtonList || behaviour is PreselectOption || behaviour is GameMenuOptions || behaviour.GetType().Name.Contains("Localiz",StringComparison.OrdinalIgnoreCase)) behaviour.enabled=false;
        }
        foreach(var trigger in panel.GetComponentsInChildren<EventTrigger>(true))UnityEngine.Object.DestroyImmediate(trigger);
        foreach(var native in panel.GetComponentsInChildren<MenuSelectable>(true)) {
            native.enabled=false;
            // Hide only the cloned donor row, keeping its native controls/content groups.
            var root=native.transform.parent;
            if(root!=panelScreen.content.transform && root!=panelScreen.controls.transform)root.gameObject.SetActive(false);
            else native.gameObject.SetActive(false);
        }
        foreach(var group in new[]{panelScreen.title,panelScreen.content,panelScreen.controls})
            if(group==null || !group.transform.IsChildOf(panel.transform))throw new InvalidOperationException("Native Options fade groups must belong to their screen.");
        var title=panelScreen.title.GetComponentsInChildren<Text>(true).First(t=>!string.IsNullOrEmpty(t.text));
        title.text="Hollow Sols Configuration";
        title.resizeTextForBestFit=true;title.resizeTextMinSize=title.fontSize*3/4;title.resizeTextMaxSize=title.fontSize;
    }
    private void BuildCheckbox(HollowSolsChoice choice) {
        var box=new GameObject("True contact indicator",typeof(RectTransform),typeof(CanvasGroup));box.layer=choice.gameObject.layer;
        box.transform.SetParent(choice.Label.transform.parent,false);checkbox=box.GetComponent<CanvasGroup>();checkbox.blocksRaycasts=false;
        var rect=(RectTransform)box.transform;rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
        rect.localPosition=choice.Label.transform.localPosition+Vector3.right*(choice.Label.preferredWidth*.5f+35);rect.sizeDelta=new Vector2(26,26);
        void Stroke(Transform parent,string name,Vector2 position,Vector2 size,float angle=0) {
            var line=new GameObject(name,typeof(RectTransform),typeof(Image));line.layer=box.layer;line.transform.SetParent(parent,false);
            var r=(RectTransform)line.transform;r.sizeDelta=size;r.anchoredPosition=position;r.localRotation=Quaternion.Euler(0,0,angle);
            var image=line.GetComponent<Image>();image.color=Color.white;image.raycastTarget=false;
        }
        Stroke(rect,"Top",new Vector2(0,13),new Vector2(26,1.5f));Stroke(rect,"Bottom",new Vector2(0,-13),new Vector2(26,1.5f));
        Stroke(rect,"Left",new Vector2(-13,0),new Vector2(1.5f,26));Stroke(rect,"Right",new Vector2(13,0),new Vector2(1.5f,26));
        checkmark=new GameObject("Checkmark",typeof(RectTransform));checkmark.layer=box.layer;checkmark.transform.SetParent(rect,false);
        Stroke(checkmark.transform,"Short",new Vector2(-5,-1),new Vector2(10,2),-45);
        Stroke(checkmark.transform,"Long",new Vector2(3,2),new Vector2(18,2),45);
    }
    private void uiBack() { UIManager.instance.UILeaveOptionsMenu(); }
    private Text Label(Transform parent,string value,float y,int size) {
        var obj=new GameObject(value,typeof(RectTransform),typeof(Text)); obj.layer=parent.gameObject.layer; obj.transform.SetParent(parent,false);
        var r=(RectTransform)obj.transform; r.sizeDelta=new Vector2(1200,55); r.localPosition=new Vector3(0,y,0);
        var text=obj.GetComponent<Text>(); text.font=font; text.fontSize=size; text.alignment=TextAnchor.MiddleCenter; text.text=value; text.color=Color.white; text.raycastTarget=false;
        return text;
    }
    private HollowSolsChoice Choice(Transform parent,string label,float y,Action act,Action back) {
        var choice=NativeChoice(nativeChoiceTemplate!,parent,label,act,back,out var root);
        ownedRows.Add(root);var rect=(RectTransform)root.transform;
        rect.localPosition=new Vector3(0,y,0);
        return choice;
    }
    internal static HollowSolsChoice NativeChoice(RectTransform template,Transform parent,string label,Action act,Action back,out GameObject root,bool followNativeVisibility=false) {
        if(template.GetComponentsInChildren<MenuSelectable>(true).Length!=1)throw new InvalidOperationException("Native menu row must contain one selectable.");
        var staging=new GameObject("YiGodhome inactive menu clone");staging.SetActive(false);
        root=UnityEngine.Object.Instantiate(template.gameObject,staging.transform,false);root.SetActive(false);
        root.name="Hollow Sols native menu row";root.transform.SetParent(parent,false);UnityEngine.Object.Destroy(staging);
        var native=root.GetComponentsInChildren<MenuSelectable>(true).Single();native.enabled=false;
        var button=native.gameObject;var colors=native.colors;var transition=native.transition;var graphic=native.targetGraphic;
        var fields=new[]{"leftCursor","rightCursor","selectHighlight","descriptionText","uiAudioPlayer"}.Select(name=>AccessTools.Field(typeof(MenuSelectable),name)).ToArray();
        var values=fields.Select(info=>info.GetValue(native)).ToArray();
        // Cloned native row event bindings still open Game Options on submit.
        // The owned replacement supplies submit/cancel itself; retain its artwork
        // and MenuSelectable highlighting, but remove the donor event callbacks.
        foreach(var trigger in root.GetComponentsInChildren<EventTrigger>(true))UnityEngine.Object.DestroyImmediate(trigger);
        foreach(var receiver in root.GetComponentsInChildren<MonoBehaviour>(true))
            if(receiver != null && receiver is not MenuSelectable && (receiver is ISubmitHandler || receiver is ICancelHandler || receiver is IPointerClickHandler)) UnityEngine.Object.DestroyImmediate(receiver);
        // Selectable forbids a second component even when its first is disabled.
        // Remove only the inactive owned clone's behavior before rebinding it.
        UnityEngine.Object.DestroyImmediate(native);
        var choice=button.AddComponent<HollowSolsChoice>();
        choice.colors=colors;choice.transition=transition;choice.targetGraphic=graphic;
        for(int i=0;i<fields.Length;i++)fields[i].SetValue(choice,values[i]);
        if (AccessTools.Field(typeof(MenuSelectable), "uiAudioPlayer").GetValue(choice) == null)
            AccessTools.Field(typeof(MenuSelectable), "uiAudioPlayer").SetValue(choice, UnityEngine.Object.FindFirstObjectByType<MenuAudioController>());
        choice.Label=root.GetComponentsInChildren<Text>(true).First(t=>!string.IsNullOrEmpty(t.text));
        choice.Label.text=label;choice.Label.resizeTextForBestFit=true;choice.Label.resizeTextMinSize=20;choice.Label.resizeTextMaxSize=choice.Label.fontSize;
        choice.SetLabel(label);
        if(followNativeVisibility)root.AddComponent<HollowSolsRowVisibility>().Bind(template,choice);
        else foreach(var group in root.GetComponentsInChildren<CanvasGroup>(true)){group.interactable=group.blocksRaycasts=true;group.alpha=1;}
        choice.Act=act;choice.Back=back;choice.navigation=new Navigation {mode=Navigation.Mode.Explicit};
        root.SetActive(true);return choice;
    }
    private void Open() {
        if(open || transition!=null || screen==null || panel==null) return;
        contentAlpha=screen.content.alpha; contentInteractable=screen.content.interactable; contentRays=screen.content.blocksRaycasts;
        controlsAlpha=screen.controls.alpha; controlsInteractable=screen.controls.interactable; controlsRays=screen.controls.blocksRaycasts;
        screenAlpha=screen.screenCanvasGroup.alpha;screenInput=screen.screenCanvasGroup.interactable;screenRays=screen.screenCanvasGroup.blocksRaycasts;
        screen.content.alpha=screen.controls.alpha=0; screen.content.interactable=screen.controls.interactable=false; screen.content.blocksRaycasts=screen.controls.blocksRaycasts=false;
        screen.screenCanvasGroup.alpha=0;screen.screenCanvasGroup.interactable=screen.screenCanvasGroup.blocksRaycasts=false;
        open=true; panel.SetActive(true); Refresh();
        transition=UIManager.instance.StartCoroutine(ShowPanel());
        log("HK_CONFIGURATION_OPEN nativePausePreserved=True");
    }
    private System.Collections.IEnumerator ShowPanel() {
        var show=(System.Collections.IEnumerator)AccessTools.Method(typeof(UIManager),"ShowMenu").Invoke(UIManager.instance,new object[]{panelScreen!});
        while(open && show.MoveNext())yield return show.Current;
        if(open)shared!.Select();transition=null;
    }
    private void Refresh() {
        shared!.SetLabel("Sisters of Battle/Mantis Lords Shared HP: "+(HollowSolsSettings.SharedHealth?"ON":"OFF"));
        contact!.SetLabel("Global Contact Damage: "+(HollowSolsSettings.Contact?"ON":"OFF"));
        trueContact!.SetLabel("True Global Contact Damage");
        checkmark!.SetActive(HollowSolsSettings.TrueContact);checkbox!.alpha=HollowSolsSettings.Contact?1:.5f;
        trueContact.interactable=HollowSolsSettings.Contact; trueContact.Label.color=HollowSolsSettings.Contact?Color.white:Color.gray;
        log("HK_CONFIGURATION_VALUES shared="+HollowSolsSettings.SharedHealth+" contact="+HollowSolsSettings.Contact+" trueContact="+HollowSolsSettings.TrueContact+" apply=nextEncounter");
    }
    private void Close(bool select) {
        if(!open) return; open=false;
        if(transition!=null){UIManager.instance.StopCoroutine(transition);transition=null;}
        if(select && panelScreen!=null && screen!=null && panel!.activeInHierarchy) {
            transition=UIManager.instance.StartCoroutine(HidePanel());return;
        }
        RestoreOptions(select);
    }
    private System.Collections.IEnumerator HidePanel() {
        var hide=(System.Collections.IEnumerator)AccessTools.Method(typeof(UIManager),"HideMenu").Invoke(UIManager.instance,new object[]{panelScreen!});
        while(panel!=null && hide.MoveNext())yield return hide.Current;
        RestoreOptions(false);
        if(screen!=null) {
            var show=(System.Collections.IEnumerator)AccessTools.Method(typeof(UIManager),"ShowMenu").Invoke(UIManager.instance,new object[]{screen});
            while(screen!=null && show.MoveNext())yield return show.Current;
        }
        selectorChoice?.Select();transition=null;
    }
    private void RestoreOptions(bool select) {
        panel?.SetActive(false);
        if(screen!=null) { screen.content.alpha=contentAlpha; screen.content.interactable=contentInteractable; screen.content.blocksRaycasts=contentRays;
            screen.controls.alpha=controlsAlpha; screen.controls.interactable=controlsInteractable; screen.controls.blocksRaycasts=controlsRays;
            screen.screenCanvasGroup.alpha=screenAlpha;screen.screenCanvasGroup.interactable=screenInput;screen.screenCanvasGroup.blocksRaycasts=screenRays; }
        if(select && selectorChoice!=null) selectorChoice.Select();
    }
    public void Dispose() {
        fadeScope.UnpatchSelf();if(current==this)current=null;
        if(fixtureSettingsCaptured) {HollowSolsSettings.SharedHealth=savedShared;HollowSolsSettings.Contact=savedContact;HollowSolsSettings.TrueContact=savedTrue;fixtureSettingsCaptured=false;}
        nativePause.Dispose();
        if(transition!=null && UIManager.instance!=null){UIManager.instance.StopCoroutine(transition);transition=null;RestoreOptions(false);}
        Close(false);
        if(list!=null && originalEntries!=null) AccessTools.Field(typeof(MenuButtonList),"entries").SetValue(list,originalEntries);
        foreach(var position in positions) if(position.Rect!=null) position.Rect.anchoredPosition=position.Position;
        foreach(var saved in navigation) if(saved.Selectable!=null) saved.Selectable.navigation=saved.Navigation;
        navigation.Clear();
        positions.Clear(); if(selector!=null) UnityEngine.Object.Destroy(selector); if(panel!=null) UnityEngine.Object.Destroy(panel);
        foreach(var row in ownedRows)if(row!=null)UnityEngine.Object.Destroy(row);ownedRows.Clear();nativeChoiceTemplate=null;
        selector=panel=null; screen=null; list=null; originalEntries=null;
    }
}

internal sealed class HollowSolsChoice : MenuSelectable, ISubmitHandler, IPointerClickHandler, ICancelHandler, ISelectHandler, IDeselectHandler
{
    internal Text Label=null!;
    private string? fixedLabel;
    internal void SetLabel(string value){fixedLabel=value;if(Label!=null)Label.text=value;}
    private void LateUpdate(){if(Label!=null && fixedLabel!=null && Label.text!=fixedLabel)Label.text=fixedLabel;}
    internal Action? Act, Back;
    internal Animator? CardHighlight;
    private int lastSubmitFrame = -1;
    private static readonly System.Reflection.MethodInfo validateDeselect=AccessTools.Method(typeof(MenuSelectable),"ValidateDeselect");
    public void OnSubmit(BaseEventData data) { if(!IsInteractable() || lastSubmitFrame == Time.frameCount) return; lastSubmitFrame = Time.frameCount; PlaySubmitSound(); Act?.Invoke(); }
    public void OnPointerClick(PointerEventData data) { if(data.button==PointerEventData.InputButton.Left) OnSubmit(data); }
    public new void OnCancel(BaseEventData data) { PlayCancelSound(); Back?.Invoke(); }
    public new void OnSelect(BaseEventData data) { base.OnSelect(data); if(CardHighlight!=null){CardHighlight.ResetTrigger("hide");CardHighlight.SetTrigger("show");} }
    public new void OnDeselect(BaseEventData data) {
        var native=(System.Collections.IEnumerator)validateDeselect.Invoke(this,new object[]{data});
        StartCoroutine(OwnedMenuDeselect.Guard(native,()=>EventSystem.current!=null && EventSystem.current.currentSelectedGameObject==gameObject));
        if(CardHighlight!=null){CardHighlight.ResetTrigger("show");CardHighlight.SetTrigger("hide");}
    }
}

// Only owned row visibility follows the donor. Selection cursors/highlights
// remain independent; no timer guesses or writes to native fade owners.
internal sealed class HollowSolsRowVisibility : MonoBehaviour
{
    private Text? source,label;
    private readonly List<(CanvasGroup Source,CanvasGroup Owned)> groups=new();
    internal void Bind(RectTransform donor,HollowSolsChoice choice) {
        label=choice.Label;
        source=donor.GetComponentsInChildren<Text>(true).First(t=>!string.IsNullOrEmpty(t.text));
        foreach(var group in source.GetComponentsInParent<CanvasGroup>(true)) {
            if(!group.transform.IsChildOf(donor) && group.transform!=donor)continue;
            var path=new Stack<string>();for(var node=group.transform;node!=donor;node=node.parent)path.Push(node.name);
            var target=path.Count==0?transform:transform.Find(string.Join("/",path));
            if(target!=null && target.GetComponent<CanvasGroup>() is CanvasGroup owned)groups.Add((group,owned));
        }
        Apply();
    }
    private void LateUpdate()=>Apply();
    private void Apply() {
        if(source==null || label==null)return;
        foreach(var pair in groups)if(pair.Source!=null && pair.Owned!=null) {
            pair.Owned.alpha=pair.Source.alpha;
            pair.Owned.interactable=pair.Source.interactable;pair.Owned.blocksRaycasts=pair.Source.blocksRaycasts;
        }
        var color=label.color;color.a=source.color.a;label.color=color;
        label.canvasRenderer.SetAlpha(source.canvasRenderer.GetAlpha());
    }
}
