using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using YiGodhome.Bridge;
namespace YiGodhome;

// Incidental native corpse response has no health bar or encounter authority.
internal sealed class NativeDefeatedHead : IDisposable {
    private static NativeDefeatedHead? current;
    private readonly Harmony hooks=new("local.yigodhome.zote.corpse.hit");
    private readonly EnemyDeathEffects effects;
    private static readonly System.Reflection.FieldInfo Corpse=AccessTools.Field(typeof(EnemyDeathEffects),"corpse");
    private readonly HashSet<long> swings=new();
    private GameObject? corpse;
    private GameObject? head;
    private Recoil? recoil;
    private bool emitting,addedRecoil;
    private readonly Action<string>? log;
    private int hitSamples;
    internal NativeDefeatedHead(HealthManager boss,Action<string>? log) {
        this.log=log;
        effects=boss.GetComponent<EnemyDeathEffects>();
        if(effects==null)throw new InvalidOperationException("Native GPZ death effects missing.");
        current=this;
        hooks.Patch(AccessTools.Method(typeof(EnemyDeathEffects),"EmitCorpse"),prefix:new HarmonyMethod(typeof(NativeDefeatedHead),nameof(BeginEmit)),finalizer:new HarmonyMethod(typeof(NativeDefeatedHead),nameof(Emitted)));
        hooks.Patch(AccessTools.Method(typeof(HutongGames.PlayMaker.Actions.SetParent),"OnEnter"),prefix:new HarmonyMethod(typeof(NativeDefeatedHead),nameof(Detaching)));
    }
    private static void BeginEmit(EnemyDeathEffects __instance){if(current!=null && __instance==current.effects)current.emitting=true;}
    private static void Detaching(HutongGames.PlayMaker.Actions.SetParent __instance) {
        var owner=current;var child=__instance.Owner;
        // Detachment is authored by the corpse FSM after EmitCorpse returns.
        // Keep exact corpse ancestry as the boundary, not the emission call stack.
        if(owner==null || !owner.emitting && owner.corpse==null || child==null || child.name!="Head" || child.GetComponent<ObjectBounce>()==null)return;
        var parent=child.transform.parent;
        for(int i=0;i<4 && parent!=null;i++,parent=parent.parent)
            if(parent.name.StartsWith("Corpse Grey Prince",StringComparison.Ordinal) && (owner.emitting || parent.gameObject==owner.corpse)){owner.Bind(child);return;}
    }
    private void Bind(GameObject child) {
        if(head!=null && head!=child)throw new InvalidOperationException("Duplicate native GPZ head.");
        head=child;recoil=child.GetComponent<Recoil>();
        log?.Invoke("HK_ZOTE_HEAD_BOUND head="+child.GetInstanceID()+" nativeCorpse=True frame="+Time.frameCount);
        if(recoil==null) {
            // The detached physics prop has no health/damage responder. Reuse
            // HK's own nail recoil component and native default initialization.
            recoil=child.AddComponent<Recoil>();addedRecoil=true;
            AccessTools.Method(typeof(Recoil),"Reset").Invoke(recoil,Array.Empty<object>());
        }
    }
    private static void Emitted(EnemyDeathEffects __instance) {
        var owner=current;if(owner==null || __instance!=owner.effects)return;
        owner.emitting=false;
        owner.corpse=Corpse.GetValue(__instance) as GameObject;
        if(owner.corpse!=null && (!owner.corpse.name.StartsWith("Corpse Grey Prince",StringComparison.Ordinal)
            || owner.corpse.GetComponent<HealthManager>()!=null))throw new InvalidOperationException("Native GPZ corpse identity changed.");
        if(owner.head==null && owner.corpse!=null) {
            var child=owner.corpse.transform.Find("Head");if(child!=null)owner.Bind(child.gameObject);
        }
    }
    internal void Melee(YiGodhome.Bridge.Pose pose) {
        if(head==null || !head.activeInHierarchy || recoil==null)return;
        var shape=head.GetComponent<Collider2D>();if(shape==null || !shape.enabled)return;
        foreach(var window in pose.Melee) {
            if(window.Swing<=0 || swings.Contains(window.Swing))continue;
            var bounds=new Bounds(new Vector3(window.X,window.Y,shape.bounds.center.z),new Vector3(window.Width,window.Height,1));
            if(!bounds.Intersects(shape.bounds))continue;
            if(swings.Count>=128)swings.Clear();
            swings.Add(window.Swing);
            recoil.CancelRecoil();recoil.RecoilByDirection(window.X<shape.bounds.center.x?0:2,1);
            if(hitSamples++<16)log?.Invoke("HK_ZOTE_HEAD_MELEE swing="+window.Swing+" head="+head.GetInstanceID()+" frame="+Time.frameCount+" noBossDamage=True");
        }
    }
    public void Dispose(){hooks.UnpatchSelf();if(current==this)current=null;if(addedRecoil && recoil!=null)UnityEngine.Object.Destroy(recoil);swings.Clear();corpse=head=null;}
}
