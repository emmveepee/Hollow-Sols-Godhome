using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace YiGodhome;

internal static class NativeCombatAudit
{
    private static bool completed;
    public static void Dump(Action<string> log, Player player)
    {
        // Exhaustive scene inventory is a standalone bounded diagnostic, not
        // part of normal combat or receiver reacquisition after every return.
        if (completed || !YiGodhome.Bridge.SessionConfiguration.Current.Has("combat-audit")) return;
        completed = true;
        var palette = GameConfig.Instance.CurrentColorSet;
        log("NS_ATTACK_PALETTE outline=" + palette.TaiChiKickOutlineHintColor + " afterimage=" + palette.TaiChiKickAfterimageHintColor
            + " danger=" + palette.particleColorsDangerHint_P.Evaluate(0.5f));
        foreach (var effect in Resources.FindObjectsOfTypeAll<DelaySpriteEffect>())
            foreach (var field in effect.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                if (field.FieldType == typeof(float) || field.FieldType == typeof(Color) || field.FieldType == typeof(SpriteRenderer))
                    log("NS_DELAY_EFFECT_AUDIT name=" + effect.name + " field=" + field.Name + " value=" + field.GetValue(effect));
        foreach (var clip in player.GetComponentsInChildren<Animator>(true).SelectMany(a => a.runtimeAnimatorController == null ? Array.Empty<AnimationClip>() : a.runtimeAnimatorController.animationClips).Distinct())
            log("NS_NATIVE_PLAYER_CLIP name=" + clip.name + " duration=" + clip.length);
        foreach (var state in new[] { PlayerStateType.ZEnter, PlayerStateType.ZExit })
        {
            var nativeState = player.FindState(state);
            foreach (var field in nativeState.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (field.FieldType.IsEnum || field.FieldType.IsPrimitive || field.FieldType == typeof(string))
                    log("NS_NATIVE_DOOR_STATE state=" + state + " field=" + field.Name + " value=" + field.GetValue(nativeState));
        }
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>().Where(s => s.name.IndexOf("wallrun", StringComparison.OrdinalIgnoreCase) >= 0 || s.name.IndexOf("climbwall", StringComparison.OrdinalIgnoreCase) >= 0))
            log("NS_WALL_ART name=" + sprite.name + " size=" + sprite.rect.size);
        foreach (var monster in Resources.FindObjectsOfTypeAll<MonsterBase>())
            log("NS_TARGET_TEMPLATE name=" + monster.name + " path=" + Path(monster.transform) + " scene=" + monster.gameObject.scene.name
                + " initialized=" + monster.Inited + " components=" + monster.GetComponentsInChildren<MonoBehaviour>(true).Length);
        foreach (var challenge in Resources.FindObjectsOfTypeAll<ChallengeData>())
            log("NS_COMBAT_CHALLENGE name=" + challenge.name + " scenes=" + string.Join(",", challenge.AllChallengeScenes.Select(s => s.SceneName)) + " revealed=" + challenge.IsRevealed);
        foreach (var connection in Resources.FindObjectsOfTypeAll<SceneConnectionPoint>().Where(c => c.gameObject.scene.IsValid()))
            log("NS_COMBAT_CONNECTION path=" + Path(connection.transform) + " target=" + connection.scene?.SceneName + " id=" + connection.connectionID + " mode=" + connection.changeSceneMode + " active=" + connection.isActiveAndEnabled);
        foreach (var point in Resources.FindObjectsOfTypeAll<TeleportPointData>())
            log("NS_COMBAT_TELEPORT name=" + point.name + " scene=" + point.SceneName + " acquired=" + point.IsAcquired + " disabled=" + point.IsDisabled);
        foreach (var button in Resources.FindObjectsOfTypeAll<TeleportPointButton>())
            log("NS_COMBAT_TELEPORT_BUTTON path=" + Path(button.transform) + " point=" + button.teleportPoint?.name);
        string[] names = { "DamageDealer", "DamageReceiver", "EffectDealer", "EffectReceiver", "ParriableAttackEffect", "PlayerHealth", "WeaknessActivator", "DownSlashParameter" };
        var components = Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(c => c.gameObject.scene.IsValid() && names.Contains(c.GetType().Name)).ToArray();
        foreach (var component in components)
        {
            log("NS_COMBAT_COMPONENT id=" + component.GetInstanceID() + " type=" + component.GetType().Name + " path=" + Path(component.transform)
                + " active=" + component.isActiveAndEnabled + " layer=" + component.gameObject.layer);
            foreach (var field in component.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object? value = field.GetValue(component);
                if (value is UnityEventBase ev)
                {
                    for (int i = 0; i < ev.GetPersistentEventCount(); i++)
                        log("NS_COMBAT_CALLBACK component=" + component.GetInstanceID() + " event=" + field.Name + " target=" + ev.GetPersistentTarget(i) + " method=" + ev.GetPersistentMethodName(i));
                }
                else if (value == null || value is UnityEngine.Object || field.FieldType.IsPrimitive || field.FieldType.IsEnum || value is string)
                    log("NS_COMBAT_FIELD component=" + component.GetInstanceID() + " field=" + field.Name + " value=" + Describe(value));
            }
            foreach (var collider in component.GetComponents<Collider2D>())
                log("NS_COMBAT_COLLIDER component=" + component.GetInstanceID() + " type=" + collider.GetType().Name + " enabled=" + collider.enabled + " trigger=" + collider.isTrigger + " bounds=" + collider.bounds);
        }
        log("NS_COMBAT_AUDIT components=" + components.Length + " health=" + player.health.currentValue + " mutations=False");
    }
    private static string Describe(object? value) => value is Component c ? c.GetType().Name + ":" + c.GetInstanceID() + ":" + Path(c.transform) : value?.ToString() ?? "null";
    private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
}
