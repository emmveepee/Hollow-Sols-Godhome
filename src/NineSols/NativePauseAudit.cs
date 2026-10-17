using System;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// Bounded fixture observes the capture's existing native owner list. No scene scan.
internal sealed class NativePauseAudit
{
    private readonly Action<string> log;
    private float started = -1;
    private string? baseline;
    private uint pixels;
    private int samples, changes, pixelChanges, arrows, effects;
    private long geometry;
    private float resumeAt = -1;
    private int resumeSamples, resumeChanges;
    public NativePauseAudit(Action<string> log) { this.log = log; }
    public void Observe(Player player, IReadOnlyList<Renderer> renderers, int arrowCount, Bridge.Pose pose, byte[] rgba)
    {
        if (!NativeInputBridge.HostPaused) {
            if (started >= 0) {
                log("NS_PAUSE_GRAPH_END geometry=" + geometry + " samples=" + samples + " seconds=" + (Time.realtimeSinceStartup - started).ToString("R", CultureInfo.InvariantCulture)
                    + " graphChanges=" + changes + " pixelChanges=" + pixelChanges + " effects=" + effects + " arrows=" + arrows + " frame=" + Time.frameCount);
                started = -1; resumeAt = Time.realtimeSinceStartup; resumeSamples = resumeChanges = 0;
            }
            if (resumeAt >= 0) {
                resumeSamples++;
                if (baseline != Signature(renderers) || pixels != VisualCodec.Checksum(rgba)) resumeChanges++;
                if (Time.realtimeSinceStartup - resumeAt >= .3f) {
                    log("NS_PAUSE_GRAPH_RESUMED geometry=" + geometry + " samples=" + resumeSamples + " changes=" + resumeChanges + " frame=" + Time.frameCount);
                    resumeAt = -1; baseline = null;
                }
            }
            return;
        }
        if (started < 0) {
            started = Time.realtimeSinceStartup; geometry = pose.GeometryFrame;
            samples = changes = pixelChanges = 0; arrows = arrowCount; effects = renderers.Count - 1;
            log("NS_PAUSE_GRAPH_BEGIN geometry=" + geometry + " state=" + player.CurrentStateType + " effects=" + effects + " arrows=" + arrows + " frame=" + Time.frameCount);
            foreach (var r in renderers) {
                var a = r.GetComponentInParent<Animator>(); var p = r.GetComponent<ParticleSystem>();
                log("NS_PAUSE_GRAPH_OWNER id=" + r.GetInstanceID() + " name=" + r.name + " parent=" + r.transform.parent?.name
                    + " animatorMode=" + (a == null ? "none" : a.updateMode.ToString()) + " particleUnscaled=" + (p != null && p.main.useUnscaledTime));
            }
        }
        // Allow one short transport/Animator completion boundary to settle.
        if (Time.realtimeSinceStartup - started < .08f) return;
        string signature = Signature(renderers);
        uint hash = VisualCodec.Checksum(rgba);
        if (baseline == null) { baseline = signature; pixels = hash; }
        else { if (baseline != signature) { if (changes < 2) log("NS_PAUSE_GRAPH_CHANGED before=" + baseline + " after=" + signature + " unityTimeScale=" + Time.timeScale); changes++; } if (pixels != hash) pixelChanges++; }
        samples++;
    }
    private static string Signature(IReadOnlyList<Renderer> renderers) => string.Join(";", renderers.OrderBy(r => r.GetInstanceID()).Select(r => {
            var sprite = r as SpriteRenderer;
            var animator = r.GetComponentInParent<Animator>();
            var particle = r.GetComponent<ParticleSystem>();
            return r.GetInstanceID() + ":" + r.name + ":" + r.transform.position.ToString("F4") + ":" + r.transform.lossyScale.ToString("F4")
                + ":" + (sprite?.sprite?.GetInstanceID() ?? 0) + ":" + (particle == null ? "none" : particle.time.ToString("R", CultureInfo.InvariantCulture))
                + ":" + (animator == null ? "none" : animator.GetCurrentAnimatorStateInfo(0).normalizedTime.ToString("R", CultureInfo.InvariantCulture));
        }));
}
