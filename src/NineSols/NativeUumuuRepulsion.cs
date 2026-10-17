using System;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;
using YiGodhome.Geometry;
namespace YiGodhome;

// Uumuu is the requested passive-contact exception: its exact live body
// repels through native Yi knockback, without dealing damage or calling Hurt.
internal sealed class NativeUumuuRepulsion : IDisposable
{
    private readonly CircleCollider2D body;
    private readonly Player player;
    private readonly GeometryLandingProbe geometry;
    private readonly Action<string> log;
    private float nextBounce;
    internal NativeUumuuRepulsion(Player player, GeometryLandingProbe geometry, Action<string> log) {
        this.player = player; this.geometry = geometry; this.log = log;
        body = new GameObject("YiGodhome Uumuu repulsion query") { layer = 2 }.AddComponent<CircleCollider2D>();
        body.isTrigger = true; body.enabled = false;
    }
    internal void Update(ContactFrame frame) {
        var target = frame.Summons.SingleOrDefault(t => t.BossBar && t.Active && t.PassiveRepel);
        body.enabled = target != null;
        if (target == null || NativeInputBridge.HostPaused || player.health.IsDead || Time.time < nextBounce) return;
        // FooExplode owns its movement/gravity during Full Control. Injecting
        // knockback AFTER entry leaves upward velocity held for the whole cast.
        // Native state entry already clears the prior recoil; do not replace it.
        if(player.CurrentStateType==PlayerStateType.FooExplode)return;
        var p = geometry.Map.ToTarget(new Point(target.X, target.Y));
        body.transform.position = new Vector3(p.X, p.Y, 0);
        body.radius = .5f * geometry.Map.Scale;
        body.transform.localScale = new Vector3(target.Width, target.Height, 1);
        Physics2D.SyncTransforms();
        if (!Physics2D.Distance(body, player.myCollider).isOverlapped) return;
        int direction = player.myCollider.bounds.center.x < p.X ? -1 : 1;
        float strength = GameCore.Instance.gameStat.GetPlayerKnockBackValue(KnockBackType.Large);
        player.KnockBack(direction, strength, strength * .5f);
        nextBounce = Time.time + .15f;
        log("NS_UUMUU_BODY_REPEL entity=" + target.Entity + " activation=" + target.Generation + " direction=" + direction + " health=" + player.health.currentValue + " nativeKnockback=True frame=" + Time.frameCount);
    }
    public void Dispose() { if (body != null) UnityEngine.Object.Destroy(body.gameObject); }
}
