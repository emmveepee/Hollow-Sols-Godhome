using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using YiGodhome.Bridge;
using YiGodhome.Geometry;

namespace YiGodhome;

// Observe native authored player melee; never synthesize a swing from input.
internal sealed class NativeMeleeWindows
{
    private readonly (EffectDealer Dealer, Collider2D Collider)[] sources;
    private readonly Dictionary<int, (bool Active, long Generation)> states = new Dictionary<int, (bool, long)>();
    private readonly List<MeleeWindow> windows = new List<MeleeWindow>(8);
    private readonly Player player;
    private long swing;
    private int animation;
    private float animationTime;
    private bool swinging;
    private int birthSamples;
    public NativeMeleeWindows(Player player, Action<string> log)
    {
        this.player = player;
        sources = player.GetComponentsInChildren<EffectDealer>(true)
            .Where(d => d.detailType == EffectDetailType.PlayerNormalAttack || d.detailType == EffectDetailType.PlayerThirdAttack
                || d.detailType == EffectDetailType.PlayerChargeAttack)
            .SelectMany(d => d.GetComponents<Collider2D>().Select(c => (d, c))).ToArray();
        log("NS_MELEE_WINDOWS_BOUND sources=" + sources.Length + " nativeAuthored=True");
    }
    public MeleeWindow[] Sample(CoordinateMap map)
    {
        windows.Clear();
        var clip = player.animator.GetCurrentAnimatorStateInfo(0);
        bool nativeSwing = player.CurrentStateType == PlayerStateType.Attack || player.CurrentStateType.ToString().Contains("ChargeAttack");
        if (nativeSwing && (!swinging || animation != clip.fullPathHash || clip.normalizedTime < animationTime - .05f)) swing++;
        swinging = nativeSwing; animation = clip.fullPathHash; animationTime = clip.normalizedTime;
        foreach (var source in sources) {
            var d = source.Dealer; var c = source.Collider;
            if (d == null || c == null) continue;
            int id = c.GetInstanceID(); states.TryGetValue(id, out var state);
            var b = c.bounds;
            bool enabled = nativeSwing && d.enabled && c.enabled && c.gameObject.activeInHierarchy && b.size.x > 0 && b.size.y > 0;
            if (enabled && !state.Active) state.Generation++;
            state.Active = enabled; states[id] = state;
            if (!enabled) continue;
            var center = map.ToSource(new Point(b.center.x, b.center.y));
            var direction=d.GetDirection();
            windows.Add(new MeleeWindow { Collider = id, Activation = state.Generation, Swing = swing, X = center.X, Y = center.Y,
                Width = b.size.x / map.Scale, Height = b.size.y / map.Scale, Direction=Mathf.Repeat(Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg,360) });
        }
        if (windows.Count > 8) throw new InvalidOperationException("Native melee window capacity exceeded.");
        return windows.Count == 0 ? Array.Empty<MeleeWindow>() : windows.ToArray();
    }
    internal void CheckNewReceiver(Collider2D[] receivers,Action<string> log,int entity)
    {
        bool attacking=player.CurrentStateType==PlayerStateType.Attack || player.CurrentStateType.ToString().Contains("ChargeAttack");
        if(!attacking)return;
        // A projected body can be born already overlapping an authored attack.
        // Ask the native dealer once; its validity/facing/receiver ledgers own
        // damage and duplicate admission. Never enable or replay an old dealer.
        foreach(var source in sources) {
            var d=source.Dealer;var c=source.Collider;
            if(d==null || c==null || !d.isActiveAndEnabled || !d.IsValid || !c.enabled || !c.gameObject.activeInHierarchy)continue;
            foreach(var receiver in receivers) {
                if(receiver==null || !receiver.enabled || !receiver.gameObject.activeInHierarchy || !c.bounds.Intersects(receiver.bounds)
                    || !c.Distance(receiver).isOverlapped)continue;
                bool accepted=d.HitEffectReceiverCheck(receiver,default(RaycastHit2D));
                if(birthSamples++<64)log("NS_NATIVE_BIRTH_MELEE entity="+entity+" collider="+c.GetInstanceID()+" swing="+swing+" accepted="+accepted+" currentNativeAttack=True frame="+Time.frameCount);
                if(accepted)break;
            }
        }
    }
}
