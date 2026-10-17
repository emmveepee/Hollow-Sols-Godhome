using System;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;

namespace YiGodhome;

internal sealed class NativeSpellIsolation : IDisposable
{
    private static NativeSpellIsolation? current;
    private readonly Harmony harmony = new Harmony("local.yigodhome.native-spell-isolation");
    private readonly Action<string> log;
    private readonly Func<bool> ownsScene;
    private int blocked;
    private bool soulVerified;
    private readonly Dictionary<AudioSource,bool> mutedFootsteps = new();
    private HeroController? footstepsHero;
    private static readonly System.Reflection.FieldInfo runSteps = AccessTools.Field(typeof(HeroController),"footStepsRunAudioSource");
    private static readonly System.Reflection.FieldInfo walkSteps = AccessTools.Field(typeof(HeroController),"footStepsWalkAudioSource");
    private static bool YiOwns => current != null && current.ownsScene() && GameManager.instance?.profileID == 4;
    private static readonly System.Reflection.FieldInfo hardLand = AccessTools.Field(typeof(HeroController),"hardLandingEffectPrefab");
    private static readonly System.Reflection.FieldInfo softLand = AccessTools.Field(typeof(HeroController),"softLandingEffectPrefab");
    public NativeSpellIsolation(Action<string> log, Func<bool> ownsScene)
    {
        this.log = log; this.ownsScene = ownsScene;
        if (current != null) throw new InvalidOperationException("Duplicate spell isolation.");
        current = this;
        harmony.Patch(AccessTools.Method(typeof(HeroAudioController), "PlaySound"), prefix: new HarmonyMethod(typeof(NativeSpellIsolation), nameof(NoKnightSound)));
        harmony.Patch(AccessTools.Method(typeof(ObjectPoolExtensions), "Spawn", new[] {typeof(GameObject),typeof(Vector3)}), prefix: new HarmonyMethod(typeof(NativeSpellIsolation), nameof(NoKnightLand)));
        harmony.Patch(AccessTools.Method(typeof(HeroController), "CanCast"), prefix: new HarmonyMethod(typeof(NativeSpellIsolation), nameof(BeforeCast)));
        harmony.Patch(AccessTools.Method(typeof(HeroController), "AddMPCharge"), prefix: new HarmonyMethod(typeof(NativeSpellIsolation), nameof(NoSoul)));
        harmony.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.AudioPlay), "OnEnter"), prefix: new HarmonyMethod(typeof(NativeSpellIsolation), nameof(BeforeChallengeAudio)));
    }
    private static bool NoKnightSound() => !YiOwns;
    private static bool NoKnightLand(GameObject __0, ref GameObject __result) {
        var hero=HeroController.instance;
        if(!YiOwns || hero == null || __0 == null || (!ReferenceEquals(__0,hardLand.GetValue(hero)) && !ReferenceEquals(__0,softLand.GetValue(hero)))) return true;
        __result=null!; return false; // Audited landing callers discard the result.
    }
    private void RestoreFootsteps() {
        foreach(var pair in mutedFootsteps) if(pair.Key != null) pair.Key.mute=pair.Value;
        mutedFootsteps.Clear();
        footstepsHero=null;
    }
    private static bool BeforeChallengeAudio(HutongGames.PlayMaker.Actions.AudioPlay __instance)
    {
        var owner = current;
        if (owner == null || !owner.ownsScene() || GameManager.instance?.profileID != 4
            || __instance.Fsm.Name != "GG Boss UI" || __instance.State.Name != "Challenge Audio"
            || __instance.oneShotClip.Value?.name != "hero_unsheath") return true;
        if (__instance.finishedEvent != null) __instance.Fsm.Event(__instance.finishedEvent);
        __instance.Finish();
        owner.log("HK_KNIGHT_CHALLENGE_SOUND_SUPPRESSED clip=hero_unsheath nativeStatueFlow=True");
        return false;
    }
    private static bool BeforeCast(ref bool __result)
    {
        var owner = current;
        if (owner == null || !owner.ownsScene()) return true;
        __result = false;
        if (++owner.blocked <= 5) owner.log("HK_NATIVE_SPELL_BLOCKED permission=CanCast");
        return false;
    }
    private static bool NoSoul() => current == null || !current.ownsScene() || GameManager.instance?.profileID != 4;
    public void Update()
    {
        if (!YiOwns) { RestoreFootsteps(); return; }
        var hero=HeroController.instance;
        if(hero != footstepsHero) {
            RestoreFootsteps(); footstepsHero=hero;
            if(hero != null) { MuteSource(runSteps,hero); MuteSource(walkSteps,hero); }
        }
        foreach(var pair in mutedFootsteps) if(pair.Key != null) pair.Key.mute=true;
        if (ownsScene() && GameManager.instance?.profileID == 4) {
            GameManager.instance.playerData.MPCharge = 0;
            GameManager.instance.playerData.MPReserve = 0;
            if (!soulVerified) {
                soulVerified = true;
                log("HK_NATIVE_SOUL_ZERO charge=" + GameManager.instance.playerData.MPCharge + " reserve=" + GameManager.instance.playerData.MPReserve + " saveIO=disabled");
            }
        }
    }
    private void MuteSource(System.Reflection.FieldInfo field, HeroController hero) {
        if(field.GetValue(hero) is not AudioSource audio || audio == null || mutedFootsteps.ContainsKey(audio)) return;
        mutedFootsteps.Add(audio,audio.mute); audio.mute=true;
        log("HK_KNIGHT_FOOTSTEPS_SILENCED source="+field.Name+" nativeGameplayPreserved=True");
    }
    public void Dispose() { RestoreFootsteps(); harmony.UnpatchSelf(); if (current == this) current = null; }
}
