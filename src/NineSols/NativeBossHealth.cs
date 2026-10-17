using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using YiGodhome.Bridge;
namespace YiGodhome;

// Substitute presentation reads only. Combat still uses the native posture system.
internal sealed class NativeBossHealth : IDisposable
{
    private static readonly List<NativeBossHealth> instances = new List<NativeBossHealth>();
    private readonly Harmony harmony = new Harmony("local.yigodhome.boss.health.presentation");
    private readonly MonsterBase owner;
    private readonly Action<string> log;
    private int depth, entity;
    private ContactFrame? host;
    private float nextLog;
    private int previousHealth = -1;
    private Vector3 basePosition, baseScale;
    private Component? nameText;
    private Vector3 baseNameScale;
    private Vector3 baseNamePosition;
    private bool layoutBound;
    private bool baseNameEnabled;
    private System.Reflection.PropertyInfo? nameProperty;
    private string lastName="";
    private int lastLayoutCount=-1;
    private BossTitleVariant lastLayoutTitle;
    private static readonly List<NativeBossHealth> visibleLayout = new List<NativeBossHealth>();
    private static bool singleLayoutBound;
    private static Vector3 singlePosition, singleScale;
    private static UIMonsterHPManager? singleLayoutOwner;
    private readonly NativeHealthNotchView notches = new NativeHealthNotchView();
    public UIBossHP? View { get; private set; }
    public static UIBossHP[] Views => instances.Select(i => i.View).Where(v => v != null).Cast<UIBossHP>().ToArray();
    public NativeBossHealth(MonsterBase owner, Action<string> log)
    {
        this.owner = owner; this.log = log; instances.Add(this);
        if (instances.Count > 1) return;
        try
        {
            foreach (string method in new[] { "Update", "UpdateHpFillAmount", "DecreasingTween" })
                harmony.Patch(AccessTools.Method(typeof(UIBossHP), method), prefix: new HarmonyMethod(typeof(NativeBossHealth), nameof(Enter)),
                    finalizer: new HarmonyMethod(typeof(NativeBossHealth), nameof(Leave)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(PostureSystem), "posturePercentage"), postfix: new HarmonyMethod(typeof(NativeBossHealth), nameof(Solid)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(PostureSystem), "RemainTotalPercentage"), postfix: new HarmonyMethod(typeof(NativeBossHealth), nameof(Total)));
        }
        catch { Dispose(); throw; }
    }
    private static void Enter(UIBossHP __instance, out NativeBossHealth? __state)
    {
        __state = instances.FirstOrDefault(i => i.host != null && __instance == i.View);
        if (__state != null) __state.depth++;
    }
    private static void Leave(NativeBossHealth? __state) { if (__state != null) __state.depth--; }
    internal Func<float>? PresentationInjury;
    private BossHealthProjection Values => new BossHealthProjection(host!.BarHealth>=0?host.BarHealth:host.Health, host.BarMaxHealth>0?host.BarMaxHealth:host.MaxHealth, PresentationInjury?.Invoke() ?? owner.postureSystem.InternalInjury);
    private readonly BossBarEntry entry=new();
    private bool depletedLogged;
    private static void Solid(PostureSystem __instance, ref float __result)
    { var context = instances.FirstOrDefault(i => i.depth > 0 && __instance == i.owner.postureSystem); if (context != null) __result = context.Values.Solid; }
    private static void Total(PostureSystem __instance, ref float __result)
    { var context = instances.FirstOrDefault(i => i.depth > 0 && __instance == i.owner.postureSystem); if (context != null) __result = context.Values.Total; }
    public void Update(ContactFrame? frame, string scene,bool hidden=false)
    {
        host = frame;
        if (frame == null) { entry.Update(false);if (View != null) View.gameObject.SetActive(false); return; }
        if (entity != 0 && entity != frame.Entity) throw new InvalidOperationException("Boss health target changed without teardown.");
        entity = frame.Entity;
        if(!frame.BossBarsReady) {
            entry.Update(false);previousHealth=frame.Health;
            if(View!=null)View.gameObject.SetActive(false);
            return; // Do not borrow/play native Enter behind the host title gate.
        }
        bool created=View==null;
        if (View == null)
        {
            string name = EncounterProfile.ForScene(scene)?.DisplayName ?? "";
            GameCore.Instance.monsterHpUI.BorrowBossHealth(owner.postureSystem, name);
            var views = (List<UIBossHP>)AccessTools.Field(typeof(UIMonsterHPManager), "bossHPList").GetValue(GameCore.Instance.monsterHpUI);
            View = views.Single(v => v.bindingPosture == owner.postureSystem);
            if (View == null || View.bindingPosture != owner.postureSystem) throw new InvalidOperationException("Native boss health widget binding failed.");
            View.BindPostureSystem(owner.postureSystem, name);
            basePosition = View.transform.localPosition; baseScale = View.transform.localScale;
            if(!singleLayoutBound || singleLayoutOwner!=GameCore.Instance.monsterHpUI) {
                singlePosition=basePosition; singleScale=baseScale; singleLayoutBound=true; singleLayoutOwner=GameCore.Instance.monsterHpUI;
            }
            nameText = AccessTools.Field(typeof(UIBossHP), "bossNameText").GetValue(View) as Component;
            if(nameText != null) nameProperty=AccessTools.Property(nameText.GetType(),"text");
            if(nameText != null) baseNameScale=nameText.transform.localScale;
            if(nameText != null) baseNamePosition=nameText.transform.localPosition;
            if(nameText is Behaviour nameBehaviour) baseNameEnabled=nameBehaviour.enabled;
            layoutBound = true;
            log("NS_BOSS_HUD_BOUND native=True entity=" + entity + " path=" + NativeRoomIsolation.PathOf(View.transform));
        }
        bool show=!hidden && (frame.Health > 0 || EncounterProfile.ForScene(scene)?.ArmorAndHead == true);
        bool entering=entry.Update(show);
        View.gameObject.SetActive(show);
        // BorrowBossHealth already plays Enter for a newly acquired widget.
        // Retained Oro and other phase widgets need the same native animation
        // restarted at authorization, not one that elapsed while invisible.
        if(entering && !created)View.PlayShowHPAnimation();
        if(entering)log("NS_BOSS_BAR_ENTRY entity="+entity+" title="+frame.BossTitle+" nativeEnter=True newWidget="+created+" frame="+Time.frameCount);
        Relayout(scene);
        notches.Update(View, frame, log);
        if (!depletedLogged && frame.Health == 0 && EncounterProfile.ForScene(scene)?.ArmorAndHead == true) {
            depletedLogged = true;
            log("NS_ARMOR_HUD_DEPLETED encounter=" + frame.Encounter + " target=" + frame.Entity + " activation=" + frame.TargetGeneration
                + " geometry=" + frame.GeometryFrame + " hp=" + frame.Health + " max=" + frame.MaxHealth + " active=" + View.gameObject.activeInHierarchy + " total=" + Values.Total + " frame=" + Time.frameCount);
        }
        if (previousHealth >= 0 && frame.Health < previousHealth)
            AccessTools.Method(typeof(UIBossHP), "DecreaseFeedback").Invoke(View, null);
        previousHealth = frame.Health;
        if (Time.realtimeSinceStartup >= nextLog)
        {
            nextLog = Time.realtimeSinceStartup + 1;
            var values = Values;
            log("NS_BOSS_HEALTH entity=" + entity + " hostHp=" + frame.Health + " max=" + frame.MaxHealth + " nativeInjury=" + owner.postureSystem.InternalInjury
                + " hostInjury=" + values.Injury + " solid=" + values.Solid + " total=" + values.Total + " reserve=" + owner.postureSystem.RemainTotal);
        }
    }
    private static void Relayout(string scene) {
        visibleLayout.Clear();
        foreach(var i in instances) if(i.View != null && i.View.gameObject.activeSelf && i.layoutBound) visibleLayout.Add(i);
        bool compact=visibleLayout.Count>1;
        var profile=EncounterProfile.ForScene(scene);
        string name=profile?.DisplayName??"";
        if(profile?.Family==EncounterProfile.OroMato) {
            var title=visibleLayout.FirstOrDefault()?.host?.BossTitle??BossTitleVariant.Native;
            name=title==BossTitleVariant.NailmasterOro?"Nailmaster Oro":"Brothers Oro & Mato";
        }
        if(profile?.Family==EncounterProfile.Sisters)
            name=BossBarNames.Sisters(visibleLayout.FirstOrDefault()?.host?.BossTitle??BossTitleVariant.MantisLords);
        for(int index=0;index<visibleLayout.Count;index++) {
            var context=visibleLayout[index]; var widget=context.View!;
            // Scaling around the native widget pivot lowers its below-bar title
            // past the canvas edge. Lift the entire compact stack clear of it.
            widget.transform.localPosition=singlePosition+Vector3.up*(compact?48+index*28:0);
            widget.transform.localScale=Vector3.Scale(singleScale,compact?new Vector3(.76f,.50f,1):Vector3.one);
            if(context.nameText != null) {
                context.nameText.transform.localScale=Vector3.Scale(context.baseNameScale,compact?new Vector3(1,1.52f,1):Vector3.one);
                context.nameText.transform.localPosition=context.baseNamePosition+(compact?Vector3.down*32:Vector3.zero);
                // Keep the title object alive so the native Enter/Exit animator
                // can reset its alpha even while this is the unnamed second bar.
                // Inactive children miss those keys when pooled for another fight.
                context.nameText.gameObject.SetActive(true);
                if(context.nameText is Behaviour title) title.enabled=index==0;
                if(context.lastName != name) { context.nameProperty?.SetValue(context.nameText,name); context.lastName=name; }
            }
            var titleVariant=context.host?.BossTitle??BossTitleVariant.Native;
            if(context.lastLayoutCount!=visibleLayout.Count || context.lastLayoutTitle!=titleVariant) {
                context.lastLayoutCount=visibleLayout.Count; context.lastLayoutTitle=titleVariant;
                context.log("NS_BOSS_LAYOUT encounter="+context.host?.Encounter+" entity="+context.entity+" count="+visibleLayout.Count+" index="+index
                    +" compact="+compact+" title="+titleVariant+" name="+name.Replace(' ','_')+" position="+widget.transform.localPosition
                    +" scale="+widget.transform.localScale+" nativeWidget=True frame="+Time.frameCount);
            }
        }
    }
    public void Dispose()
    {
        if (!instances.Contains(this)) return;
        notches.Dispose();
        // Native DestroyBossHealth returns the widget to its pool. Restore every
        // transform we changed before the next encounter can borrow that widget.
        if(View!=null && layoutBound) {
            View.transform.localPosition=basePosition; View.transform.localScale=baseScale;
            if(nameText!=null) {nameText.transform.localPosition=baseNamePosition;nameText.transform.localScale=baseNameScale;nameText.gameObject.SetActive(true);if(nameText is Behaviour title) title.enabled=baseNameEnabled;}
        }
        if (View != null && GameCore.Instance != null) GameCore.Instance.monsterHpUI.DestroyBossHealth(owner.postureSystem);
        View = null; host = null; instances.Remove(this); if (instances.Count == 0) harmony.UnpatchSelf();
    }
}
