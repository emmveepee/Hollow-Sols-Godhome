using System;
using System.Linq;
using System.Reflection;
using System.IO;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

internal sealed class NativeEnemyProbe : IDisposable
{
    private readonly Action<string> log;
    private readonly Player player;
    private readonly NativeCombatObserver observer;
    private readonly NativeInputBridge input;
    private readonly float requestedAt;
    private MonsterBase? enemy;
    private float readyAt, nextSample;
    private float nextTransferWait;
    private bool ready;
    private Transform? entryTarget;
    private float parryUntil, nextParry;
    private Buttons defensiveButton;
    private float healStageAt = -1;
    private float healBefore;
    private bool healVerified;
    private int potionsBefore;
    private PausedTargetMaintenance? maintenance;
    private bool duplicateCheck;
    private readonly bool contactProbe;
    private NativeContactFixture? contactFixture;
    private readonly bool transfer;
    private readonly bool reuseScene;
    private readonly bool deathProbe;
    private bool sawDeath, sawRevival;
    public bool TransferReady { get; private set; }
    public MonsterBase? TransferOwner => TransferReady ? enemy : null;
    public void PrepareContactLease()
    {
        if (!TransferReady || enemy == null || !enemy.Inited) throw new InvalidOperationException("Contact owner is not initialized.");
        if (!enemy.enabled && !enemy.animator.enabled && enemy.fsm.isPaused) return;
        // Native root rest can resume the retained scene receiver while there
        // are no combat leases. Reacquire only a verified clean exact owner.
        if (enemy.health.IsDead || enemy.postureSystem.PostureValue != enemy.postureSystem.MaxPostureValue
            || enemy.postureSystem.InternalInjury != 0
            || enemy.GetComponentsInChildren<FooAttachable>(true).Any(f => f.bindingFoo != null && f.bindingFoo.HasFoo()))
            throw new InvalidOperationException("Resumed native owner is not clean for contact reacquisition.");
        enemy.Pause();
        foreach (var dealer in enemy.GetComponentsInChildren<DamageDealer>(true)) dealer.gameObject.SetActive(false);
        if (enemy.enabled || enemy.animator.enabled || !enemy.fsm.isPaused)
            throw new InvalidOperationException("Native contact reacquisition did not pause the owner.");
        log("NS_CONTACT_OWNER_REACQUIRED owner=" + enemy.GetInstanceID() + " clean=True nativePause=True frame=" + Time.frameCount);
    }
    public NativeEnemyProbe(Action<string> log, Player player, bool transfer = false, bool reuseScene = false)
    {
        this.log = log; this.player = player;
        this.transfer = transfer;
        this.reuseScene = reuseScene;
        contactProbe = YiGodhome.Bridge.SessionConfiguration.Current.Has("contact-probe");
        deathProbe = YiGodhome.Bridge.SessionConfiguration.Current.Has("death-probe");
        if (!ApplicationCore.IsInBossMemoryMode) throw new InvalidOperationException("Native enemy fixture requires memory mode.");
        var point = Resources.FindObjectsOfTypeAll<TeleportPointData>().Single(p => p.name == "VR_Challenge_A2_S5");
        if (!point.IsAcquired || point.IsDisabled || string.IsNullOrEmpty(point.sceneName)) throw new InvalidOperationException("Audited memory boss point unavailable.");
        observer = new NativeCombatObserver(log);
        if (deathProbe) observer.ObserveLifecycle();
        observer.TargetHitCompleted = (core, hit) => {
            if (maintenance == null || duplicateCheck || core.monsterBase != enemy) return;
            duplicateCheck = true;
            try
            {
                float before = core.postureSystem.PostureValue;
                core.DecreasePosture(hit);
                float after = core.postureSystem.PostureValue;
                if (before != after) throw new InvalidOperationException("Native same-frame duplicate caused damage.");
                log("NS_ENEMY_DUPLICATE_REJECTED frame=" + Time.frameCount + " posture=" + after);
            }
            finally { duplicateCheck = false; }
        };
        observer.IncomingContact = hit => {
            if (transfer || deathProbe) return;
            if (!ready || Time.realtimeSinceStartup < nextParry) return;
            bool contactDefense = contactFixture != null && Time.realtimeSinceStartup - healStageAt >= 29 && Time.realtimeSinceStartup - healStageAt < 30;
            if (!contactDefense && (healStageAt >= 0 || Time.realtimeSinceStartup - readyAt > 12)) return;
            var damage = hit.dealer.GetComponent<DamageDealer>();
            defensiveButton = damage != null && !damage.parriable ? Buttons.Dodge : Buttons.Parry;
            parryUntil = Time.realtimeSinceStartup + 0.09f; nextParry = Time.realtimeSinceStartup + 0.3f;
            log("NS_ENEMY_REACTIVE_DEFENSE frame=" + Time.frameCount + " button=" + defensiveButton + " source=" + hit.dealer.name);
        };
        input = new NativeInputBridge(player, Controls, () => (ready || entryTarget != null) && player.actions.Enabled, log);
        requestedAt = Time.realtimeSinceStartup;
        if (reuseScene)
        {
            if (!transfer || !NativeCanvas.IsServiceContext())
                throw new InvalidOperationException("Native owner reuse outside transfer scene.");
            log("NS_ENEMY_REUSE scene=A2_S5_BossHorseman_Final");
            return;
        }
        log("NS_ENEMY_REQUEST point=" + point.name + " rawScene=" + point.sceneName + " position=" + point.TeleportPosition + " saveIO=disabled core="+GameCore.Instance.currentCoreState+" playerActive="+player.gameObject.activeSelf);
        GameCore.Instance.SetReviveSavePoint(point);
        GameCore.Instance.TeleportToSavePoint(point, false, 0);
    }
    public void Update()
    {
        if (!ApplicationCore.IsInBossMemoryMode) throw new InvalidOperationException("Enemy fixture left memory mode.");
        if (!ready && Time.realtimeSinceStartup - requestedAt > (transfer ? 0 : 4) && player.actions.Enabled)
        {
            if (transfer)
            {
                var logic = Resources.FindObjectsOfTypeAll<Boss_SpearHorse_Logic>().SingleOrDefault(l => l.gameObject.scene.name == "A2_S5_BossHorseman_Final" && l.gameObject.scene.isLoaded);
                if (logic != null || NativeCanvas.Current != null)
                {
                    const BindingFlags fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                    var candidate = NativeCanvas.Current?.Receiver ?? (MonsterBase)typeof(Boss_SpearHorse_Logic).GetField("boss", fields)!.GetValue(logic);
                    var sensor = logic == null ? null : (Component)typeof(Boss_SpearHorse_Logic).GetField("playerSensor", fields)!.GetValue(logic);
                    // Native EnterLevelAwake/Reset must finish before borrowing the graph.
                    if (candidate?.Inited == true && Time.realtimeSinceStartup >= nextTransferWait) {
                        nextTransferWait=Time.realtimeSinceStartup+5;
                        log("NS_NATIVE_TRANSFER_GATE core="+GameCore.Instance.currentCoreState+" playerSelf="+player.gameObject.activeSelf+" playerHierarchy="+player.gameObject.activeInHierarchy+" currentLevel="+GameCore.Instance.gameLevel?.SceneName);
                    }
                    if (candidate != null && candidate.Inited
                        && GameCore.Instance.currentCoreState == GameCore.GameCoreState.Playing
                        && GameCore.Instance.player == player && player.gameObject.activeInHierarchy
                        && player.myCollider.enabled && player.myCollider.bounds.size.x > 0)
                    {
                        if (sensor != null) sensor.gameObject.SetActive(false);
                        candidate.Pause();
                        foreach (var dealer in candidate.GetComponentsInChildren<DamageDealer>(true)) dealer.gameObject.SetActive(false);
                        candidate.gameObject.SetActive(true);
                        if (!candidate.gameObject.activeInHierarchy) throw new InvalidOperationException("Initialized target has an inactive parent.");
                        NativeSummonAudit.Dump(candidate, log);
                        enemy = candidate;
                        ready = true; readyAt = Time.realtimeSinceStartup;
                        log("NS_ENEMY_DIRECT_READY name=" + enemy.name + " initialized=True intro=False elapsed=" + (readyAt - requestedAt));
                        log("NS_ENEMY_READY name=" + enemy.name + " player=" + player.transform.position + " enemy=" + enemy.transform.position);
                        NativeCombatAudit.Dump(log, player);
                    }
                }
            }
            else
            {
                if (entryTarget == null)
                {
                    var logic = Resources.FindObjectsOfTypeAll<Boss_SpearHorse_Logic>().SingleOrDefault(l => l.gameObject.scene.name == "A2_S5_BossHorseman_Final");
                    if (logic != null)
                    {
                        var sensor = (Component)typeof(Boss_SpearHorse_Logic).GetField("playerSensor", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(logic);
                        entryTarget = sensor.transform;
                        log("NS_ENEMY_ENTRY_TARGET position=" + entryTarget.position + " player=" + player.transform.position);
                    }
                }
                var enemies = Resources.FindObjectsOfTypeAll<MonsterBase>().Where(m => m != null && m.gameObject.activeInHierarchy).ToArray();
                foreach (var m in enemies) log("NS_ENEMY_CANDIDATE name=" + m.name + " type=" + m.GetType().Name + " position=" + m.transform.position);
                enemy = enemies.OrderBy(m => Vector2.Distance(m.transform.position, player.transform.position)).FirstOrDefault();
                if (enemy != null)
                {
                    ready = true; readyAt = Time.realtimeSinceStartup;
                    log("NS_ENEMY_READY name=" + enemy.name + " player=" + player.transform.position + " enemy=" + enemy.transform.position);
                    NativeCombatAudit.Dump(log, player);
                }
            }
        }
        if (!ready && Time.realtimeSinceStartup - requestedAt > 30) throw new InvalidOperationException("Native enemy did not become available.");
        if (!TransferReady) input.Update();
        if (deathProbe)
        {
            if (!sawDeath && player.health.IsDead)
            { sawDeath = true; log("NS_DEATH_OBSERVED player=" + player.GetInstanceID() + " health=" + player.health.currentValue + " state=" + player.CurrentStateType); }
            if (sawDeath && !sawRevival && !player.health.IsDead && player.health.currentValue == player.health.maxHealth.Value && player.actions.Enabled)
            { sawRevival = true; log("NS_REVIVE_VERIFIED player=" + player.GetInstanceID() + " health=" + player.health.currentValue + " ammo=" + player.ammo.Value + " potions=" + player.potion.DrinkTimesLeft + " chi=" + player.chiContainer.Value + " scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name); }
            if (Time.realtimeSinceStartup >= nextSample)
            {
                nextSample = Time.realtimeSinceStartup + 1;
                log("NS_DEATH_SAMPLE dead=" + player.health.IsDead + " health=" + player.health.currentValue + " state=" + player.CurrentStateType + " actions=" + player.actions.Enabled + " position=" + player.transform.position);
            }
            return;
        }
        if (transfer)
        {
            if (!TransferReady && ready && enemy != null && player.actions.Enabled && Time.realtimeSinceStartup - readyAt >= 0.25f)
            {
                if (reuseScene)
                {
                    int talismans = enemy.GetComponentsInChildren<FooAttachable>(true).Count(f => f.bindingFoo != null && f.bindingFoo.HasFoo());
                    var posture = enemy.postureSystem;
                    if (enemy.health.IsDead || posture.PostureValue != posture.MaxPostureValue || posture.InternalInjury != 0 || talismans != 0)
                        throw new InvalidOperationException("Native reset left combat damage or talismans on the reused owner.");
                    log("NS_WARM_OWNER_VERIFIED owner=" + enemy.GetInstanceID() + " posture=" + posture.PostureValue + " injury=0 talismans=0");
                }
                enemy.Pause();
                maintenance = enemy.gameObject.AddComponent<PausedTargetMaintenance>(); maintenance.Initialize(enemy);
                foreach (var dealer in enemy.GetComponentsInChildren<DamageDealer>(true)) dealer.gameObject.SetActive(false);
                input.Dispose(); TransferReady = true;
                log("NS_CONTACT_OWNER_READY owner=" + enemy.GetInstanceID() + " state=" + enemy.CurrentState + " health=" + player.health.currentValue);
            }
            return;
        }
        if (ready && enemy != null && !player.health.IsDead && player.health.currentValue < 240 && healStageAt < 0 && Time.realtimeSinceStartup - readyAt > 12)
        {
            healStageAt = Time.realtimeSinceStartup; healBefore = player.health.currentValue;
            potionsBefore = player.potion.DrinkTimesLeft;
            enemy.Pause(); log("NS_ENEMY_HEAL_FIXTURE nativeEnemyPaused=True health=" + healBefore);
            maintenance = enemy.gameObject.AddComponent<PausedTargetMaintenance>(); maintenance.Initialize(enemy);
            log("NS_ENEMY_PAUSED_CACHE nativeFrameCache=True movement=False");
        }
        if (healStageAt >= 0 && !healVerified && !player.health.IsDead && player.health.currentValue > healBefore && player.potion.DrinkTimesLeft == potionsBefore - 1)
        { healVerified = true; log("NS_ENEMY_HEAL_VERIFIED before=" + healBefore + " after=" + player.health.currentValue + " potions=" + player.potion.DrinkTimesLeft); }
        if (contactProbe && healVerified && enemy != null)
        {
            float t = Time.realtimeSinceStartup - healStageAt;
            if (t >= 23 && contactFixture == null) contactFixture = new NativeContactFixture(enemy, log, independent: true);
            contactFixture?.SetContact(player, t >= 24 && t < 25 || t >= 29 && t < 30 || t >= 34 && t < 35);
        }
        if (ready && Time.realtimeSinceStartup >= nextSample)
        {
            nextSample = Time.realtimeSinceStartup + 1;
            log("NS_ENEMY_SAMPLE elapsed=" + (Time.realtimeSinceStartup - readyAt) + " health=" + player.health.currentValue + " chi=" + player.chiContainer.Value
                + " player=" + player.transform.position + " enemy=" + (enemy == null ? "none" : enemy.transform.position.ToString()) + " state=" + (enemy == null ? "none" : enemy.CurrentState.ToString()));
        }
    }
    private Controls Controls()
    {
        if (player.health.IsDead) return default;
        if (!ready) return entryTarget == null ? default : new Controls(Math.Sign(entryTarget.position.x - player.transform.position.x), 0, Buttons.None);
        if (deathProbe) return default;
        if (transfer) return default;
        if (enemy == null) return default;
        float elapsed = Time.realtimeSinceStartup - readyAt;
        float delta = enemy.transform.position.x - player.transform.position.x;
        float move = Math.Abs(delta) > 45 ? Math.Sign(delta) : 0;
        Buttons buttons = Buttons.None;
        if (healStageAt >= 0)
        {
            float healTime = Time.realtimeSinceStartup - healStageAt;
            if (contactProbe && healTime >= 18)
                return new Controls(0, 0, Time.realtimeSinceStartup < parryUntil || healTime >= 33.72f && healTime < 33.82f ? Buttons.Parry : Buttons.None);
            if (healVerified && healTime > 5)
            {
                Buttons attack = healTime < 13 ? (healTime % 0.8f < 0.1f ? Buttons.Attack : Buttons.None)
                    : healTime < 18 ? (player.chiContainer.Value >= 1 || player.CurrentStateType == PlayerStateType.FooAttack ? Buttons.Talisman : Buttons.None)
                    : (healTime % 1.5f < 0.1f ? Buttons.Bow : Buttons.None);
                return new Controls(Math.Abs(delta) > 28 ? Math.Sign(delta) : 0, 0, attack);
            }
            return new Controls(healTime < 1.4f ? -Math.Sign(delta) : 0, 0, healTime > 1.5f && healTime < 1.65f ? Buttons.Heal : Buttons.None);
        }
        if (Time.realtimeSinceStartup < parryUntil) buttons = defensiveButton;
        else if (player.CurrentStateType == PlayerStateType.FooAttack || player.chiContainer.Value >= 1 && elapsed % 4 < 0.8f) buttons = Buttons.Talisman;
        else if (elapsed < 2) { if (elapsed % 0.65f < 0.12f) buttons = Buttons.Attack; }
        else if (elapsed > 30 && elapsed < 34) { move = -Math.Sign(delta); }
        else if (elapsed >= 34 && elapsed < 38) { move = 0; if (elapsed % 1.5f < 0.1f) buttons = Buttons.Heal; }
        else if (elapsed > 40) { move = 0; if (elapsed % 1 < 0.1f) buttons = Buttons.Bow; }
        return new Controls(move, 0, buttons);
    }
    public void Dispose() { input.Dispose(); observer.Dispose(); contactFixture?.Dispose(); if (maintenance != null) UnityEngine.Object.Destroy(maintenance); }
}
