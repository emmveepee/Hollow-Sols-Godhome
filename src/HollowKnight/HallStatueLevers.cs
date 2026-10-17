using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using YiGodhome.Bridge;
using Pose = YiGodhome.Bridge.Pose;

namespace YiGodhome;

// Reuse the authored lever receiver, including its busy/swap completion gate.
internal sealed class HallStatueLevers : IDisposable
{
    private static HallStatueLevers? current;
    private readonly Harmony harmony = new Harmony("local.yigodhome.dream-lever");
    private BossStatue? toggling;
    private static void PreserveDreamDisplay(BossStatue __instance, ref bool __1)
    {
        if (current?.toggling == __instance && current.restored.ContainsKey(__instance)) __1 = false;
    }
    private readonly BossStatueLever[] levers;
    private readonly GameObject nail;
    private readonly Collider2D nailCollider;
    private readonly List<GameObject> replacements = new List<GameObject>();
    private readonly Dictionary<GameObject, bool> originals = new Dictionary<GameObject, bool>();
    private readonly Dictionary<Transform, Vector3> mountPositions = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<BossStatue, BossStatueDreamToggle> restored = new Dictionary<BossStatue, BossStatueDreamToggle>();
    private readonly Dictionary<BossStatue, BossStatue.StatueSwapEndEvent> subscriptions = new Dictionary<BossStatue, BossStatue.StatueSwapEndEvent>();
    private readonly Dictionary<BossStatueLever, object?> priorOwners = new Dictionary<BossStatueLever, object?>();
    private readonly Dictionary<(int Lever, int Collider), long> consumed = new Dictionary<(int, int), long>();
    private readonly Dictionary<int, long> swings = new Dictionary<int, long>();
    private readonly Action<string> log;
    private BossStatueLever? fixtureLever;
    private BossStatue? fixtureStatue;
    private bool fixtureInitial, fixtureFinished;
    private int fixtureToggles;
    internal bool FixtureComplete=>fixtureFinished;
    private static readonly System.Reflection.FieldInfo statueOwner = AccessTools.Field(typeof(BossStatueLever), "bossStatue");
    private static readonly System.Reflection.FieldInfo canToggle = AccessTools.Field(typeof(BossStatueLever), "canToggle");
    private static readonly System.Reflection.FieldInfo nativeToggle = AccessTools.Field(typeof(BossStatue), "dreamToggle");
    private static readonly System.Reflection.MethodInfo hit = AccessTools.Method(typeof(BossStatueLever), "OnTriggerEnter2D");
    public HallStatueLevers(Action<string> log)
    {
        this.log = log;
        current = this;
        harmony.Patch(AccessTools.Method(typeof(BossStatue), "SetDreamVersion"), prefix: new HarmonyMethod(typeof(HallStatueLevers), nameof(PreserveDreamDisplay)));
        var authored = Resources.FindObjectsOfTypeAll<BossStatueLever>().Where(l => l.gameObject.scene.name == "GG_Workshop" && statueOwner.GetValue(l) is BossStatue).ToArray();
        if (authored.Length == 0) throw new InvalidOperationException("No native Hall lever template.");
        foreach (var dream in Resources.FindObjectsOfTypeAll<BossStatueDreamToggle>().Where(d => d.gameObject.scene.name == "GG_Workshop")) {
            var statue = dream.GetComponentInParent<BossStatue>();
            if (statue == null || !statue.HasDreamVersion || !ReferenceEquals(nativeToggle.GetValue(statue), dream)) continue;
            // Every inspected Hall pedestal already owns a dormant alt_lever.
            // Its artwork origin differs from the dream orb and from other
            // pedestals. Preserve this exact authored mounting, not a clone offset.
            var lever = statue.GetComponentsInChildren<BossStatueLever>(true)
                .Single(l => l.transform.parent.name == "alt_lever");
            var obj = lever.gameObject;
            if (statue.statueStatePD == EncounterProfile.DungDefender.Statue) {
                // Defender has a wider authored plinth. Match the accepted
                // Crystal Guardian lever's clearance from the right edge,
                // moving the bracket, receiver and effects as one assembly.
                var reference = Resources.FindObjectsOfTypeAll<BossStatue>().Single(s =>
                    s.gameObject.scene.name == "GG_Workshop" && s.statueStatePD == EncounterProfile.CrystalGuardian.Statue);
                var referenceLever = reference.GetComponentsInChildren<BossStatueLever>(true).Single(l => l.transform.parent.name == "alt_lever");
                SpriteRenderer Plinth(BossStatue s) => s.GetComponentsInChildren<SpriteRenderer>(true)
                    .Single(r => r.name == "GG_statues_0001_plinth_02");
                float clearance = referenceLever.transform.position.x - Plinth(reference).bounds.max.x;
                float shift = Plinth(statue).bounds.max.x + clearance - lever.transform.position.x;
                if (!float.IsFinite(shift) || shift < 0 || shift > 2) throw new InvalidOperationException("Defender native lever mounting changed.");
                var mount = obj.transform.parent;
                mountPositions.Add(mount, mount.position);
                mount.position += Vector3.right * shift;
                log("HK_DEFENDER_LEVER_ALIGNED shift=" + shift + " nativePlinthClearance=" + clearance);
            }
            originals.Add(obj, obj.activeSelf);
            originals.Add(obj.transform.parent.gameObject, obj.transform.parent.gameObject.activeSelf);
            priorOwners.Add(lever, statueOwner.GetValue(lever));
            obj.transform.parent.gameObject.SetActive(true);
            // Native SetOwner immediately swaps an already-selected dream statue.
            // Preserve the existing display and bind its exact completion callback.
            statueOwner.SetValue(lever, statue);
            var finished = (BossStatue.StatueSwapEndEvent)Delegate.CreateDelegate(typeof(BossStatue.StatueSwapEndEvent), lever,
                AccessTools.Method(typeof(BossStatueLever), "<SetOwner>b__9_0"));
            statue.OnStatueSwapFinished += finished;
            subscriptions.Add(statue, finished);
            lever.SetState(true); obj.SetActive(true);
            nativeToggle.SetValue(statue, lever);
            restored.Add(statue, dream);
            originals.Add(dream.gameObject, dream.gameObject.activeSelf); dream.gameObject.SetActive(false);
            replacements.Add(obj);
            log("HK_STATUE_LEVER_REPLACED statue=" + statue.statueStatePD + " nativeReceiver=True");
        }
        levers = authored.Concat(replacements.Select(o => o.GetComponent<BossStatueLever>())).Distinct().ToArray();
        log("HK_STATUE_LEVERS_READY authored=" + authored.Length + " replacements=" + replacements.Count + " capacity=32");
        if (levers.Length > 32) throw new InvalidOperationException("Hall lever capacity exceeded.");
        foreach (var lever in levers) {
            var owner = (BossStatue)statueOwner.GetValue(lever);
            log("HK_STATUE_LEVER_BOUND statue=" + owner.statueStatePD + " dream=" + owner.dreamStatueStatePD
                + " replacement=" + replacements.Contains(lever.gameObject));
        }
        if (SessionConfiguration.Current.Has("lever-probe")) {
            fixtureLever = levers.Single(l => statueOwner.GetValue(l) is BossStatue s
                && (s.statueStatePD == EncounterProfile.Selected.Statue || s.dreamStatueStatePD == EncounterProfile.Selected.Statue));
            fixtureStatue = (BossStatue)statueOwner.GetValue(fixtureLever);
            fixtureInitial = fixtureStatue.UsingDreamVersion;
        }
        nail = new GameObject("YiGodhome Native Lever Hit") { tag = "Nail Attack", layer = LayerMask.NameToLayer("Ignore Raycast") };
        nailCollider = nail.AddComponent<BoxCollider2D>(); nailCollider.enabled = false;
    }
    public Vector3 FixturePlacement()
    {
        if (!SessionConfiguration.Current.Has("lever-probe") || fixtureLever == null) throw new InvalidOperationException("No authorized lever fixture.");
        var bounds = fixtureLever.GetComponent<Collider2D>().bounds;
        log("HK_LEVER_FIXTURE lever=" + fixtureLever.GetInstanceID() + " replacement=" + replacements.Contains(fixtureLever.gameObject)
            + " initial=" + fixtureInitial + " x=" + bounds.center.x + " y=" + bounds.center.y);
        return bounds.center + new Vector3(-1, 1, 0);
    }
    public void Update(Pose? pose)
    {
        if (pose == null) return;
        if (!fixtureFinished && fixtureToggles >= 2 && fixtureStatue != null && fixtureLever != null
            && (bool)canToggle.GetValue(fixtureLever)) {
            var display = (GameObject)AccessTools.Field(typeof(BossStatue), "statueDisplay").GetValue(fixtureStatue);
            var alt = (GameObject?)AccessTools.Field(typeof(BossStatue), "statueDisplayAlt").GetValue(fixtureStatue);
            bool visible = new[] { display, alt }.Where(o => o != null && o.activeInHierarchy)
                .SelectMany(o => o!.GetComponentsInChildren<Renderer>()).Any(r => r.enabled);
            if (!visible) throw new InvalidOperationException("Dream lever removed its statue artwork.");
            fixtureFinished = true;
            log("HK_LEVER_FIXTURE_COMPLETE lever=" + fixtureLever.GetInstanceID() + " nativeSwapFinished=True dream=" + fixtureStatue.UsingDreamVersion + " meleeToggles=" + fixtureToggles + " statueVisible=True");
        }
        foreach (var lever in levers) {
            if (lever == null || !lever.gameObject.activeInHierarchy) continue;
            var collider = lever.GetComponent<Collider2D>();
            if (collider == null || !collider.enabled) continue;
            foreach (var window in pose.Melee) {
                if (window.Swing > 0 && swings.TryGetValue(lever.GetInstanceID(), out long swing) && swing == window.Swing) continue;
                var key = (lever.GetInstanceID(), window.Collider);
                if (consumed.TryGetValue(key, out long prior) && prior >= window.Activation) continue;
                var shape = new Bounds(new Vector3(window.X, window.Y, collider.bounds.center.z), new Vector3(window.Width, window.Height, 1));
                if (!collider.bounds.Intersects(shape)) continue;
                consumed[key] = window.Activation;
                if (window.Swing > 0) swings[lever.GetInstanceID()] = window.Swing;
                nail.transform.position = shape.center;
                var owner = (BossStatue)statueOwner.GetValue(lever);
                bool before = owner.UsingDreamVersion;
                toggling = owner;
                try { hit.Invoke(lever, new object[] { nailCollider }); }
                finally { toggling = null; }
                if (lever == fixtureLever && owner.UsingDreamVersion != before) fixtureToggles++;
                if (restored.TryGetValue(owner, out var dream) && owner.UsingDreamVersion != before) {
                    // Dream toggles change the existing statue's native lighting,
                    // not its mesh. Run the authored fade on the live lever owner.
                    foreach (string field in new[] { "dreamBurstEffect", "dreamBurstEffectOff" }) {
                        var effect = (GameObject?)AccessTools.Field(typeof(BossStatueDreamToggle), field).GetValue(dream);
                        if (effect != null) effect.SetActive(field == "dreamBurstEffect" ? owner.UsingDreamVersion : !owner.UsingDreamVersion);
                    }
                    lever.StartCoroutine((System.Collections.IEnumerator)AccessTools.Method(typeof(BossStatueDreamToggle), "Fade").Invoke(dream, new object[] { owner.UsingDreamVersion }));
                    lever.SetState(true); // Native non-swapping completion precedes the lever's busy assignment.
                    log("HK_DREAM_LEVER_TOGGLED statue=" + owner.statueStatePD + " dream=" + owner.UsingDreamVersion + " meshSwap=False nativeFade=True");
                }
                log("HK_STATUE_LEVER_MELEE lever=" + lever.GetInstanceID() + " collider=" + window.Collider + " activation=" + window.Activation + " swing=" + window.Swing + " geometry=" + pose.GeometryFrame + " frame=" + Time.frameCount + " changed=" + (owner.UsingDreamVersion != before));
            }
        }
    }
    public void Dispose()
    {
        harmony.UnpatchSelf(); if (current == this) current = null;
        foreach (var pair in subscriptions) if (pair.Key != null) pair.Key.OnStatueSwapFinished -= pair.Value;
        foreach (var pair in restored) if (pair.Key != null && pair.Value != null) nativeToggle.SetValue(pair.Key, pair.Value);
        foreach (var pair in priorOwners) if (pair.Key != null) statueOwner.SetValue(pair.Key, pair.Value);
        foreach (var pair in mountPositions) if (pair.Key != null) pair.Key.position = pair.Value;
        foreach (var entry in originals) if (entry.Key != null) entry.Key.SetActive(entry.Value);
        UnityEngine.Object.Destroy(nail); consumed.Clear(); swings.Clear();
    }
}
