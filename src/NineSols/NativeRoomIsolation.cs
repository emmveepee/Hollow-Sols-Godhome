using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace YiGodhome;

internal sealed class NativeRoomIsolation : IDisposable
{
    private const string Scene = "A2_S5_BossHorseman_Final";
    private static bool ServiceScene(string scene) => scene == Scene || scene == NativeCanvas.ServicesName;
    private static NativeRoomIsolation? current;
    private readonly Harmony harmony = new Harmony("local.yigodhome.native-room-isolation");
    private readonly Action<string> log;
    private readonly HashSet<string> sounds = new HashSet<string>();
    private readonly Dictionary<Collider2D, bool> triggers = new Dictionary<Collider2D, bool>();
    private bool surveyed;
    private int controlsUiSounds;
    private int controlsUiSoundSamples;
    internal static int ControlsUiSounds=>current?.controlsUiSounds??0;
    public NativeRoomIsolation(Action<string> log)
    {
        this.log = log;
        if (current != null) throw new InvalidOperationException("Duplicate native room isolation.");
        current = this;
        harmony.Patch(AccessTools.Method(typeof(SoundManager), "PlaySound"), prefix: new HarmonyMethod(typeof(NativeRoomIsolation), nameof(BeforeSound)),
            postfix:new HarmonyMethod(typeof(NativeRoomIsolation),nameof(AfterSound)));
        harmony.Patch(AccessTools.Method(typeof(ICutScene), "PlayCutscene"), prefix: new HarmonyMethod(typeof(NativeRoomIsolation), nameof(BeforeCutscene)));
        harmony.Patch(AccessTools.Method(typeof(SimpleCutsceneManager), "PlayAnimation"), prefix: new HarmonyMethod(typeof(NativeRoomIsolation), nameof(BeforeCutscene)));
        harmony.Patch(AccessTools.Method(typeof(GameCore), "ResetLevel", new[] { typeof(bool) }), postfix: new HarmonyMethod(typeof(NativeRoomIsolation), nameof(AfterReset)));
    }
    private static void AfterSound(string __0,uint __result) {
        if(current==null || !NativeInputBridge.HostPaused || !NativeMenu.ControlsOpen || !__0.StartsWith("UISFX_",StringComparison.Ordinal))return;
        if(__result!=0)current.controlsUiSounds++;
        if(current.controlsUiSoundSamples++<8)current.log("NS_CONTROLS_UI_SOUND event="+__0+" nativePlayingId="+__result+" paused=True");
    }
    internal static string PathOf(Transform? t) => t == null ? "null" : (t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name);
    private static bool BeforeSound(string __0, GameObject __1, ref uint __result)
    {
        var owner = current; if (owner == null) return true;
        bool blocked = __1 != null && ServiceScene(__1.scene.name) && __0.StartsWith("EnvirSFX_", StringComparison.Ordinal)
            && __1.GetComponentInParent<Player>() == null && __1.GetComponentInParent<MonsterBase>() == null;
        if(NativeInputBridge.HostPaused && NativeMenu.ControlsOpen && !__0.StartsWith("UISFX_",StringComparison.Ordinal))blocked=true;
        string key = __0 + " object=" + (__1 == null ? "null" : PathOf(__1.transform));
        if (owner.sounds.Add(key)) owner.log((blocked ? "NS_ROOM_SOUND_BLOCKED event=" : "NS_ROOM_SOUND_ALLOWED event=") + key);
        if (blocked) __result = 0;
        return !blocked;
    }
    private static bool BeforeCutscene(Component __instance)
    {
        if (current == null || !ServiceScene(__instance.gameObject.scene.name)) return true;
        current.log("NS_ROOM_CUTSCENE_BLOCKED path=" + PathOf(__instance.transform));
        return false;
    }
    private static void AfterReset() { current?.Reapply(true); }
    private void Reapply(bool reset)
    {
        foreach (var entry in triggers) if (entry.Key != null && entry.Key.enabled) entry.Key.enabled = false;
        if (reset && surveyed) log("NS_ROOM_QUARANTINE_RESET triggers=" + triggers.Count);
    }
    public void Survey(Player player, MonsterBase combatOwner)
    {
        if (surveyed) { Reapply(false); return; }
        surveyed = true;
        foreach (var c in Resources.FindObjectsOfTypeAll<Collider2D>().Where(c => ServiceScene(c.gameObject.scene.name) && c.isTrigger
            && !c.transform.IsChildOf(player.transform) && !c.transform.IsChildOf(combatOwner.transform)
            && !c.transform.root.name.StartsWith("YiGodhome", StringComparison.Ordinal)))
        {
            triggers.Add(c, c.enabled); c.enabled = false;
            log("NS_ROOM_TRIGGER_ISOLATED path=" + PathOf(c.transform));
        }
        log("NS_ROOM_QUARANTINE_READY triggers=" + triggers.Count + " combatOwner=" + combatOwner.GetInstanceID());
        bool scripted = YiGodhome.Bridge.SessionConfiguration.Current.RunFixtures;
        if (scripted)
        {
            foreach (var point in Resources.FindObjectsOfTypeAll<SavePoint>().Where(p => ServiceScene(p.gameObject.scene.name)))
            {
                log("NS_REST_AUDIT path=" + PathOf(point.transform));
                foreach (var interaction in point.GetComponentsInChildren<AbstractInteraction>(true))
                    log("NS_REST_INTERACTION path=" + PathOf(interaction.transform) + " action=" + interaction.IntructionalUISetting?.FirstActionData?.Name);
                foreach (var c in point.GetComponentsInChildren<Component>(true))
                    if (c != null)
                    {
                        log("NS_REST_COMPONENT path=" + PathOf(c.transform) + " type=" + c.GetType().FullName);
                        foreach (var field in c.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                            if (typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(field.FieldType) && field.GetValue(c) is UnityEngine.Events.UnityEventBase e)
                                for (int i = 0; i < e.GetPersistentEventCount(); i++)
                                    log("NS_REST_CALLBACK type=" + c.GetType().Name + " field=" + field.Name + " target=" + e.GetPersistentTarget(i) + " method=" + e.GetPersistentMethodName(i));
                    }
            }
            var kuafu = Resources.FindObjectsOfTypeAll<SimpleCutsceneManager>().Single(c => ServiceScene(c.gameObject.scene.name) && PathOf(c.transform).Contains("夸伏演出"));
            var before = GameCore.Instance.currentCutScene; bool actions = player.actions.Enabled;
            kuafu.Play();
            if (GameCore.Instance.currentCutScene != before || player.actions.Enabled != actions || triggers.Keys.Any(c => c != null && c.enabled))
                throw new InvalidOperationException("Native room quarantine failed its entry check.");
            log("NS_ROOM_CUTSCENE_CHECK nativeEntry=True controlUnchanged=True triggersDisabled=True");
        }
    }
    public void Dispose()
    {
        harmony.UnpatchSelf(); if (current == this) current = null;
        foreach (var entry in triggers) if (entry.Key != null) entry.Key.enabled = entry.Value;
        triggers.Clear();
    }
}
