using System;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// A visual sequence does not own attack admission or counter settlement.
internal sealed class NativeRagePresentation : IDisposable
{
    private readonly Transform parent;
    private readonly Action<string> log;
    private readonly Action sound;
    private NativeContentRepository.Lease? warning;
    private readonly MonsterBase owner;
    private readonly GeometryLandingProbe geometry;
    private long sequence, lastAction;
    private Guid encounter;
    private bool released;
    private Renderer[] warningRenderers = Array.Empty<Renderer>();
    internal Renderer[] Renderers => released ? Array.Empty<Renderer>()
        : warningRenderers;
    internal long CapturedSequence => 0;
    internal long HostFrame => 0;
    internal NativeRagePresentation(MonsterBase owner, GeometryLandingProbe geometry, Action<string> log, Action sound)
    { this.owner = owner; this.geometry = geometry; parent = owner.transform; this.log = log; this.sound = sound; }
    internal void Update(ContactFrame? frame, Vector3 position, float diameter)
    {
        if (frame == null || frame.CrimsonSequence == 0) { Retire("inactive-or-stale"); return; }
        if (sequence != frame.CrimsonSequence || encounter != frame.Encounter) {
            Retire("replacement");
            bool armor = EncounterProfile.ForScene(geometry.SourceScene)?.ArmorAndHead == true;
            if (armor) {
                var repository = NativeContentRepository.Current ?? throw new InvalidOperationException("Rage content repository missing.");
                warning = repository.Borrow(NativeContentKind.JiequanWarning);
            }
            if (warning != null) { warning.Root.transform.SetParent(parent, true); warningRenderers = warning.Root.GetComponentsInChildren<Renderer>(true); }
            sequence = frame.CrimsonSequence; encounter = frame.Encounter; released = false;
            if (armor) sound();
            log("NS_RAGE_VISUAL_BEGIN encounter=" + encounter + " sequence=" + sequence + " action=" + frame.AttackId
                + " warningOwner=" + (warning?.Root.GetInstanceID() ?? 0) + " nativeWarning=True localHostOutline=True frame=" + Time.frameCount);
        }
        if (!released && frame.CrimsonCharge >= 1) {
            released = true; warning?.Dispose(); warning = null; warningRenderers = Array.Empty<Renderer>();
            log("NS_RAGE_VISUAL_RELEASE encounter=" + encounter + " sequence=" + sequence + " action=" + frame.AttackId + " frame=" + Time.frameCount);
        }
        if (warning != null) warning.Root.transform.position = position;
        if (lastAction != frame.AttackId) {
            lastAction = frame.AttackId;
            log("NS_RAGE_VISUAL_STRIKE encounter=" + encounter + " sequence=" + sequence + " action=" + lastAction
                + " released=" + released + " frame=" + Time.frameCount);
        }
    }
    private void Retire(string reason)
    {
        warning?.Dispose(); warning = null; warningRenderers = Array.Empty<Renderer>();
        if (sequence != 0) log("NS_RAGE_VISUAL_END encounter=" + encounter + " sequence=" + sequence + " reason=" + reason + " contentLeases=0 outlineOwners=" + 0 + " frame=" + Time.frameCount);
        sequence = lastAction = 0; released = false;
    }
    public void Dispose() { Retire("disposed"); }
}
