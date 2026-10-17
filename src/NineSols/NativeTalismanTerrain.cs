using System;
using HarmonyLib;
using UnityEngine;

namespace YiGodhome;

// HK edge floors have no solid below their surface. Native grounded talisman
// cliff probes start below Yi's feet; verify that exact surface from above.
internal sealed class NativeTalismanTerrain : IDisposable
{
    private readonly Harmony harmony = new Harmony("local.yigodhome.talisman-terrain");
    private static NativeTalismanTerrain? active;
    private readonly Player player;
    private readonly Func<Transform> terrain;
    private readonly Action<string> log;
    private int samples;
    private int correctedFrame = -1;
    private static readonly System.Reflection.PropertyInfo CachedBounds = AccessTools.Property(typeof(Actor), "cachedBounds");
    public NativeTalismanTerrain(Player player, Func<Transform> terrain, Action<string> log)
    {
        this.player = player; this.terrain = terrain; this.log = log;
        if (active != null) throw new InvalidOperationException("Overlapping talisman terrain leases.");
        active = this;
        try {
            harmony.Patch(AccessTools.Method(typeof(Actor), "IsMovingOutCliff"),
                prefix: new HarmonyMethod(typeof(NativeTalismanTerrain), nameof(Before)),
                postfix: new HarmonyMethod(typeof(NativeTalismanTerrain), nameof(Observe)));
            harmony.Patch(AccessTools.Method(typeof(PlayerFooAttackState), "OnAnimationMove"),
                postfix: new HarmonyMethod(typeof(NativeTalismanTerrain), nameof(AnimationMove)));
        } catch { active = null; harmony.UnpatchSelf(); throw; }
    }
    // Native code zeroes its by-value argument after finding a cliff. Harmony's
    // postfix sees that modified argument, so preserve the requested delta first.
    private static void Before(Vector2 offset, out Vector2 __state) => __state = offset;
    private static void Observe(Actor __instance, Vector2 __state, bool predict, ref ValueTuple<bool, Vector2> __result)
    {
        var owner = active;
        var offset = __state;
        if (owner == null || __instance != owner.player || owner.player.CurrentStateType != PlayerStateType.FooAttack
            || !owner.player.onGround || !__result.Item1 || offset.x == 0) return;
        var bounds = (Bounds)CachedBounds.GetValue(owner.player);
        var origin = new Vector2(bounds.center.x + bounds.extents.x * Mathf.Sign(offset.x) + (predict ? offset.x : 0), bounds.min.y - .0625f);
        int mask = owner.player.solid_layer.value | owner.player.OnewayLayer.value | owner.player.onewaySlope_layer.value;
        var ray = Physics2D.Raycast(origin, Vector2.down, bounds.extents.x + .125f, mask);
        var surfaceOrigin = new Vector2(origin.x, bounds.min.y + .125f);
        var surface = Physics2D.Raycast(surfaceOrigin, Vector2.down, bounds.extents.x + .3125f, mask);
        bool supported = ray.collider == null && surface.collider != null && !surface.collider.isTrigger
            && surface.fraction > 0 && surface.normal.y > .5f
            && surface.point.y <= bounds.min.y + .0625f && surface.collider.transform.IsChildOf(owner.terrain());
        if (supported) { __result = new ValueTuple<bool, Vector2>(false, offset); owner.correctedFrame = Time.frameCount; }
        if (owner.samples++ < 256) owner.log("NS_TALISMAN_TERRAIN frame=" + Time.frameCount + " nativeCliff=True corrected=" + supported
            + " offsetX=" + offset.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            + " origin=" + origin.ToString("F4") + " nativeRay=" + (ray.collider == null ? "none" : ray.collider.name)
            + " surface=" + (surface.collider == null ? "none" : surface.collider.name)
            + " surfaceY=" + surface.point.y + " normalY=" + surface.normal.y + " resultCliff=" + __result.Item1);
    }
    private static void AnimationMove(Vector3 __result)
    {
        var owner = active;
        if (owner == null || owner.correctedFrame != Time.frameCount || owner.samples > 256) return;
        owner.log("NS_TALISMAN_NATIVE_MOVE frame=" + Time.frameCount + " grounded=" + owner.player.onGround
            + " nativeOffsetX=" + __result.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            + " position=" + owner.player.transform.position.ToString("F4") + " forcedMovement=False");
        owner.correctedFrame = -1;
    }
    public void Dispose() { if (active == this) active = null; harmony.UnpatchSelf(); }
}
