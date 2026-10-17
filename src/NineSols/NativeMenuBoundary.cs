using System;
using HarmonyLib;
using DG.Tweening;

namespace YiGodhome;

// The borrowed native inventory may edit Yi, but must never launch native travel.
internal sealed class NativeMenuBoundary : IDisposable
{
    private NativeInventoryProgression? progression;
    private static readonly System.Collections.Generic.List<PlayerSitAtSavePointCondition> rootConditions = new();
    private static readonly System.Reflection.MethodInfo RootValid = AccessTools.PropertyGetter(typeof(PlayerSitAtSavePointCondition), "isValid");
    private static readonly System.Reflection.MethodInfo FinalValid = AccessTools.PropertyGetter(typeof(AbstractConditionComp), "FinalResult");
    private static readonly System.Reflection.FieldInfo FailConditions = AccessTools.Field(typeof(UISubmitFailReasonProvider), "conditions");
    private static Func<Guid>? captureId;
    private static Action<string>? capture;
    private static void BlockedReason(UISubmitFailReasonProvider __instance) {
        if(captureId?.Invoke()==Guid.Empty || captureId==null || capture==null || NativeGameplayUI.Current?.menuUI==null
            || !__instance.transform.IsChildOf(NativeGameplayUI.Current.menuUI.transform))return;
        var conditions=FailConditions.GetValue(__instance) as AbstractConditionComp[];
        if(conditions==null || conditions.Length>16)return;
        capture("DETAIL_LOADOUT_BLOCKED provider="+NativeRoomIsolation.PathOf(__instance.transform)+" nativeRoot="+(Player.i?.IsSitAtSavePoint??false)
            +" bridgeRoot="+NativeRestPoint.Active+" conditions="+conditions.Length);
        for(int i=0;i<conditions.Length;i++) {
            var condition=conditions[i];
            if(condition!=null)capture("DETAIL_LOADOUT_FAILED_CONDITION index="+i+" type="+condition.GetType().Name
                +" name="+condition.name+" finalValid="+FinalValid.Invoke(condition,null));
        }
    }
    private static readonly System.Reflection.FieldInfo WeaponLock = AccessTools.Field(typeof(Player), "lockForChangeWeapon");
    internal static bool WeaponSwitchLocked(Player player) => (bool)WeaponLock.GetValue(player);
    internal static void TraceConfiguration(Action<string> log) {
        var panel=NativeGameplayUI.Current?.menuUI;
        if(panel==null || !NativeMenu.InventoryOpen)return;
        rootConditions.Clear();
        panel.GetComponentsInChildren(true,rootConditions);
        if(rootConditions.Count>16) {rootConditions.Clear();throw new InvalidOperationException("Owned loadout root-condition budget changed.");}
        try {
            foreach(var condition in rootConditions)log("DETAIL_LOADOUT_ROOT_CONDITION path="+NativeRoomIsolation.PathOf(condition.transform)
                +" valid="+RootValid.Invoke(condition,null)+" finalValid="+FinalValid.Invoke(condition,null)+" active="+condition.gameObject.activeInHierarchy+" bridgeRoot="+NativeRestPoint.Active
                +" nativeRoot="+(Player.i?.IsSitAtSavePoint??false)+" nativeToMenu="+(GameCore.Instance?.savePanelUiController?.ToMenu??false));
        } finally {rootConditions.Clear();}
    }
    private readonly Harmony harmony = new Harmony("local.yigodhome.menu-boundary");
    private static void UiClock(RCGUIPanel __instance,Tween __result) {
        if(__result!=null && NativeMenu.OwnsPanel(__instance))__result.SetUpdate(true);
    }
    private static void BindingSubmit(ControllerMappingButton __instance){if(report!=null)NativeMenu.BindingSubmitted(__instance,report);}
    private static void NativeSelection(UIControlButton __instance) {
        if(!NativeMenu.Open)return;
        var panel=__instance.GetComponentInParent<RCGUIPanel>();
        if(panel!=null && NativeMenu.OwnsPanel(panel))
            (AccessTools.Field(typeof(UIControlButton),"_autoDisableAnimator").GetValue(__instance) as RCGMaker.Runtime.AutoDisableAnimator)?.SetDirty();
    }
    private static bool BindingPrepare(ControllerMappingButton __instance)=>report==null || NativeMenu.PrepareBinding(__instance,report);
    private static Action<string>? report;
    internal static PlayerInfoPanelType LastRootPage;
    internal static int RootPageRequests,RootRestoreRequests;
    private static void RootRestore(SavePanel __instance) {if(NativeRestPoint.Active && __instance==GameCore.Instance.savePanelUiController)RootRestoreRequests++;}
    private static bool RootBackAction(PlayerInputActionData __instance, ref InControl.PlayerAction __result) {
        if(!NativeRestPoint.Active || NativeMenu.ControlsOpen || __instance.actionSetType!=ActionSetType.UI || __instance.Name!="Cancel")return true;
        __result=ApplicationCore.Instance.playerInput.gameplayActions.Hack;return false;
    }
    private static bool RootMenuOwner(UIManager __instance,PlayerInfoPanelType __0) {
        if(NativeMenu.ControlsOpen || NativeMenu.BindingActive)return false;
        if(NativeRestPoint.Active){LastRootPage=__0;RootPageRequests++;}
        var retained=NativeGameplayUI.Current;
        if(!HollowSols.Foundation.NativeSaveContext.Enabled || !NativeRestPoint.Active || retained==null
            || retained==__instance || __instance.menuUI!=null || retained.menuUI==null)return true;
        report?.Invoke("NS_ROOT_MENU_OWNER_ROUTED nativePage="+__0+" retainedGameplayUI=True nativeRootCallbacksPreserved=True");
        retained.ShowMenu(__0);return false;
    }
    public NativeMenuBoundary(Action<string> log, Func<Guid>? detailedId=null, Action<string>? detail=null)
    {
        report = log;
        captureId=detailedId;capture=detail;
        try
        {
            progression=new NativeInventoryProgression(log);
            NativeBindingInput.Install(harmony);
            harmony.Patch(AccessTools.PropertyGetter(typeof(PlayerInputActionData),"Action"),prefix:new HarmonyMethod(typeof(NativeMenuBoundary),nameof(RootBackAction)));
            harmony.Patch(AccessTools.Method(typeof(SavePanel),"RestoreClicked",new[]{typeof(Action)}),prefix:new HarmonyMethod(typeof(NativeMenuBoundary),nameof(RootRestore)));
            harmony.Patch(AccessTools.Method(typeof(UIManager),"ShowMenu",new[]{typeof(PlayerInfoPanelType)}),prefix:new HarmonyMethod(typeof(NativeMenuBoundary),nameof(RootMenuOwner)));
            foreach(string method in new[]{"ShowUI","HideUI"})harmony.Patch(AccessTools.Method(typeof(RCGUIPanel),method,new[]{typeof(float),typeof(Action)}),
                postfix:new HarmonyMethod(typeof(NativeMenuBoundary),nameof(UiClock)));
            harmony.Patch(AccessTools.Method(typeof(ControllerMappingButton),"Submit"),prefix:new HarmonyMethod(typeof(NativeMenuBoundary),nameof(BindingPrepare)),postfix:new HarmonyMethod(typeof(NativeMenuBoundary),nameof(BindingSubmit)));
            harmony.Patch(AccessTools.Method(typeof(UIControlButton),"OnSelect"),prefix:new HarmonyMethod(typeof(NativeMenuBoundary),nameof(NativeSelection)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(UITabsItem), "IsAllValid"),
                prefix: new HarmonyMethod(typeof(NativeMenuBoundary), nameof(FilterTab)));
            harmony.Patch(AccessTools.Method(typeof(TabsUI), "GoToTab", new[] { typeof(PlayerInfoPanelType) }),
                prefix: new HarmonyMethod(typeof(NativeMenuBoundary), nameof(AllowTab)));
            harmony.Patch(AccessTools.Method(typeof(ChallengeUIButton), "Submit"),
                prefix: new HarmonyMethod(typeof(NativeMenuBoundary), nameof(BlockReplay)));
            harmony.Patch(AccessTools.Method(typeof(PauseUIPanel),"ResetChallenge"),prefix:new HarmonyMethod(typeof(NativeMenuBoundary),nameof(BlockReplay)));
            harmony.Patch(AccessTools.Method(typeof(PauseUIPanel),"ExitChallenge"),prefix:new HarmonyMethod(typeof(NativeMenuBoundary),nameof(BlockReplay)));
            harmony.Patch(AccessTools.Method(typeof(UISubmitFailReasonProvider),"OnSubmitBlocked"),prefix:new HarmonyMethod(typeof(NativeMenuBoundary),nameof(BlockedReason)));
            log("NS_MENU_BOUNDARY installed=True replaySubmit=False travelTabs=False");
        }
        catch { Dispose(); throw; }
    }
    private static bool Forbidden(PlayerInfoPanelType type) => type == PlayerInfoPanelType.MemoryChallenge
        || type == PlayerInfoPanelType.TeleportPanel;
    private static bool FilterTab(UITabsItem __instance, ref bool __result)
    {
        // String-keyed child tabs may leave PanelType at its default (TeleportPanel).
        // Reject travel BEFORE native prerequisites consult absent campaign
        // services. A postfix cannot prevent that evaluation from throwing.
        if (__instance.OverridePanelType == null && Forbidden(__instance.PanelType)) {__result = false;return false;}
        return true;
    }
    private static bool AllowTab(PlayerInfoPanelType __0)
    {
        if (!Forbidden(__0)) return true;
        report?.Invoke("NS_MENU_ROUTE_BLOCKED panel=" + __0);
        return false;
    }
    private static bool BlockReplay()
    {
        report?.Invoke("NS_MENU_REPLAY_BLOCKED beforeCallbacks=True");
        return false;
    }
    public void Dispose() { progression?.Dispose();progression=null;harmony.UnpatchSelf(); report = null; captureId=null;capture=null; }
}
