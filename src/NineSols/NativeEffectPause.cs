using System;
using System.Collections.Generic;
using UnityEngine;

namespace YiGodhome;

// Lease clocks on the capture's native combat owners, never UI or scene globals.
// Native bow charging art and arrow particles deliberately use unscaled time.
internal sealed class NativeEffectPause : IDisposable
{
    private readonly Dictionary<Animator, AnimatorUpdateMode> animators = new();
    private readonly Dictionary<ParticleSystem, bool> particles = new();
    private readonly List<Animator> deadAnimators = new();
    private readonly List<ParticleSystem> deadParticles = new();
    public void Apply(IReadOnlyList<Renderer> owned)
    {
        if (!NativeInputBridge.HostPaused && !NativeHitPauseObserver.Held) { Dispose(); return; }
        for(int i=0;i<owned.Count;i++) {
            var renderer=owned[i];
            if (renderer == null) continue;
            var animator = renderer.GetComponentInParent<Animator>();
            if (animator != null && animator.updateMode == AnimatorUpdateMode.UnscaledTime) {
                if (!animators.ContainsKey(animator)) animators.Add(animator, animator.updateMode);
                animator.updateMode = AnimatorUpdateMode.Normal;
            }
            var particle = renderer.GetComponent<ParticleSystem>();
            if (particle != null && particle.main.useUnscaledTime) {
                if (!particles.ContainsKey(particle)) particles.Add(particle, true);
                var main = particle.main; main.useUnscaledTime = false;
            }
        }
        // Existing native capture/pool closure is bounded; never retain a growing
        // stream of destroyed pooled owners through a long pause.
        foreach (var a in animators.Keys) if (a == null) deadAnimators.Add(a!);
        foreach (var p in particles.Keys) if (p == null) deadParticles.Add(p!);
        foreach (var a in deadAnimators) animators.Remove(a);
        foreach (var p in deadParticles) particles.Remove(p);
        deadAnimators.Clear(); deadParticles.Clear();
        if (animators.Count + particles.Count > 512)
            throw new InvalidOperationException("Native combat pause owner capacity exceeded.");
    }
    public void Dispose()
    {
        foreach (var item in animators) if (item.Key != null) item.Key.updateMode = item.Value;
        foreach (var item in particles) if (item.Key != null) { var main = item.Key.main; main.useUnscaledTime = item.Value; }
        animators.Clear(); particles.Clear();
    }
}
