using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace YiGodhome;

// Native MonsterBase.AfterAnimationUpdate normally clears this frame-local cache.
// Pause suppresses that entire method, including movement, gravity and this cache.
// Keep only its frame boundary for the isolated paused-target diagnostic.
internal sealed class PausedTargetMaintenance : MonoBehaviour
{
    private MonsterBase? target;
    private IDictionary? hits;
    public void Initialize(MonsterBase target)
    {
        if (target.enabled || target.animator.enabled || !target.fsm.isPaused)
            throw new InvalidOperationException("Target maintenance requires native Pause.");
        this.target = target;
        hits = (IDictionary)typeof(MonsterBase).GetField("thisFrameHitDealer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(target);
    }
    private void LateUpdate()
    {
        if (target != null && !target.enabled && target.fsm.isPaused) hits?.Clear();
    }
}
