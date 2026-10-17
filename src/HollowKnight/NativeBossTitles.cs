using System;
using System.Linq;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using YiGodhome.Bridge;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;
namespace YiGodhome;

// Retain native title animation/localization; select its authored right-hand layout.
internal sealed class NativeBossTitles : IDisposable
{
    private static NativeBossTitles? current;
    private readonly Harmony harmony = new Harmony("local.yigodhome.boss-titles");
    private readonly Action<string> log;
    private readonly YiGodhome.Bridge.BossTitleGate bossBarGate=new();
    private readonly NativeHudGate bossHud=new();
    private FsmBool? nailmasterPhase;
    private int nailmasterRoom,sistersRoom;
    private BossTitleVariant sistersTitle=BossTitleVariant.MantisLords;
    private BossTitleVariant CurrentPhase => UnitySceneManager.GetActiveScene().name=="GG_Nailmasters"
        ? nailmasterRoom==UnitySceneManager.GetActiveScene().handle && nailmasterPhase?.Value==true
            ? YiGodhome.Bridge.BossTitleVariant.BrothersOroMato : YiGodhome.Bridge.BossTitleVariant.NailmasterOro
        : UnitySceneManager.GetActiveScene().name=="GG_Mantis_Lords_V"
            ? sistersRoom==UnitySceneManager.GetActiveScene().handle?sistersTitle:BossTitleVariant.MantisLords
            : YiGodhome.Bridge.BossTitleVariant.Native;
    internal static BossTitleVariant CurrentBossTitle=>current?.CurrentPhase??BossTitleVariant.Native;
    internal static void BindNailmasterPhase(FsmBool phase) {
        if(current==null)return;
        current.nailmasterPhase=phase;current.nailmasterRoom=UnitySceneManager.GetActiveScene().handle;
    }
    internal static bool ReadyFor(YiGodhome.Bridge.BossTitleVariant title) {
        var adapter=current;if(adapter==null)return false;
        var scene=UnitySceneManager.GetActiveScene();
        if(scene.name=="GG_Radiance")adapter.bossBarGate.Reveal(scene.handle,title);
        return adapter.bossBarGate.Ready(scene.handle,title,adapter.bossHud.Read(out _));
    }
    internal static bool BossBarReady => current!=null && ReadyFor(current.CurrentPhase);
    private FsmBool? last;
    private Transform? smallTitle;
    private const float SmallRaise=.75f;
    public NativeBossTitles(Action<string> log)
    {
        if (current != null) throw new InvalidOperationException("Duplicate title adapter.");
        this.log = log; current = this;
        harmony.Patch(AccessTools.Method(typeof(FadeGroup),"FadeUp"),postfix:new HarmonyMethod(typeof(NativeBossTitles),nameof(TitleRevealed)));
        harmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.SetMeshRenderer),"OnEnter"),postfix:new HarmonyMethod(typeof(NativeBossTitles),nameof(ZoteTitleRevealed)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(FsmBool), "Value"), postfix: new HarmonyMethod(typeof(NativeBossTitles), nameof(ReadRight)));
        harmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.SetPosition),"DoSetPosition"),postfix:new HarmonyMethod(typeof(NativeBossTitles),nameof(RaiseNativePosition)));
    }
    private static void TitleRevealed(FadeGroup __instance) {
        var adapter=current;
        if(adapter==null || !OwnsTitle)return;
        var root=AreaTitle.instance.transform;
        if(__instance.transform!=root.Find("Title Small") && __instance.transform!=root.Find("Title Large"))return;
        int handle=UnitySceneManager.GetActiveScene().handle;
        if(UnitySceneManager.GetActiveScene().name=="GG_Mantis_Lords_V") {
            var control=AreaTitle.instance.GetComponents<PlayMakerFSM>().Single(f=>f.FsmName=="Area Title Control");
            var titleKeys=control.FsmStates.Single(s=>s.Name=="Other").Actions.OfType<GetLanguageStringProcessed>();
            // Read the title's resolved native key, not HP/remaining enemy count.
            var main=titleKeys.Single(a=>a.convName.Value.EndsWith("_MAIN",StringComparison.Ordinal));
            adapter.sistersRoom=handle;adapter.sistersTitle=BossBarNames.SistersTitle(main.convName.Value);
            adapter.log("HK_SISTERS_TITLE_PHASE nativeKey="+main.convName.Value+" title="+adapter.sistersTitle+" frame="+Time.frameCount);
        }
        var phase=adapter.CurrentPhase;
        if(!adapter.bossBarGate.Ready(handle,phase,false))adapter.log("HK_BOSS_BAR_TITLE_REVEAL scene="+UnitySceneManager.GetActiveScene().name+" nativeFrame="+Time.frameCount);
        adapter.bossBarGate.Reveal(handle,phase);
    }
    // Zote owns a separate scene-root title, NOT a child of AreaTitle.instance.
    // Observe only that exact main renderer's native fade-in, excluding extras.
    private static void ZoteTitleRevealed(HutongGames.PlayMaker.Actions.SetMeshRenderer __instance) {
        var adapter=current;
        var scene=UnitySceneManager.GetActiveScene();
        if(adapter==null || GameManager.instance?.profileID!=4 || (scene.name!="GG_Grey_Prince_Zote" && scene.name!="GG_Hollow_Knight")
            || __instance.Fsm.Name!="color_fader" || __instance.State.Name!="Up" || !__instance.active.Value)return;
        var owner=__instance.Owner;
        if(owner==null || owner.scene!=scene || !owner.activeInHierarchy
            || FSMUtility.GetSafe(__instance.gameObject,__instance)!=owner)return;
        var parent=owner.transform.parent;
        var root=parent!=null?parent.parent:null;
        bool zote=GreyPrinceTitleSource.Matches(scene.name,root?.name,parent?.name,owner.name,root!=null && root.parent==null);
        bool vessel=PureVesselTitleSource.Matches(scene.name,parent?.name,owner.name,parent!=null && parent.parent==null);
        if(!zote && !vessel)return;
        adapter.bossBarGate.Reveal(scene.handle);
        adapter.log("HK_BOSS_BAR_TITLE_REVEAL scene="+scene.name+" source="+(vessel?"Battle_Scene/Title":"Grey_Prince_Title/Title_Main/Title_Small_Main")+" nativeFrame="+Time.frameCount);
    }
    private static bool OwnsTitle => GameManager.instance?.profileID==4 && HubPresence.Allowed(UnitySceneManager.GetActiveScene().name) && AreaTitle.instance!=null;
    private void BindSmall(bool raise) {
        var title=AreaTitle.instance.transform.Find("Title Small");
        if(smallTitle==title) return;
        RestoreSmall();smallTitle=title;
        if(smallTitle==null) throw new InvalidOperationException("Native small boss title folder missing.");
        if(raise) smallTitle.localPosition+=Vector3.up*SmallRaise;
        log("HK_BOSS_SMALL_TITLE_LAYOUT raised="+SmallRaise+" nativeFolder=True frame="+Time.frameCount);
    }
    private void RestoreSmall() {
        if(smallTitle!=null) smallTitle.localPosition-=Vector3.up*SmallRaise;
        smallTitle=null;
    }
    private static void RaiseNativePosition(HutongGames.PlayMaker.Actions.SetPosition __instance) {
        var adapter=current;
        if(adapter==null || __instance.Fsm.Name!="Area Title Control" || !OwnsTitle || __instance.Owner!=AreaTitle.instance.gameObject) return;
        var target=FSMUtility.GetSafe(__instance.gameObject,__instance);
        if(target==null || target.transform!=AreaTitle.instance.transform.Find("Title Small")) return;
        bool writesY=!__instance.y.IsNone || !__instance.vector.IsNone;
        adapter.BindSmall(!writesY);
        if(writesY) adapter.smallTitle!.localPosition+=Vector3.up*SmallRaise;
    }
    private static void ReadRight(FsmBool __instance, ref bool __result)
    {
        var adapter = current;
        if (adapter == null || __instance.Name != "Display Right" || AreaTitle.instance == null
            || GameManager.instance?.profileID != 4 || !HubPresence.Allowed(UnitySceneManager.GetActiveScene().name)) return;
        bool titleVariable = AreaTitle.instance.GetComponents<PlayMakerFSM>().Any(f => f.FsmVariables.BoolVariables.Any(v => ReferenceEquals(v, __instance)));
        if (!titleVariable) return;
        __result = true;
        if (adapter.last != __instance) { adapter.last = __instance; adapter.log("HK_BOSS_TITLE_LAYOUT native=True displayRight=True"); }
    }
    public void Update()
    {
        if(OwnsTitle) BindSmall(true); else RestoreSmall();
    }
    public void Dispose()
    {
        RestoreSmall();
        harmony.UnpatchSelf(); if (current == this) current = null;
    }
}
