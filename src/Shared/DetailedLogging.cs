#if HOLLOWSOLS_PUBLIC_RELEASE
using YiGodhome.Bridge;
namespace YiGodhome;
public sealed partial class BaselinePlugin
{
    private DetailedEventCapture? detailedCapture;
    private void WriteNativeDetail(string line) { }
#if HOLLOW_KNIGHT
    private void SetDetailedLogging(bool enabled) { }
#endif
    private void UpdateDetailedLogging() { }
}
#else
using System;
using System.IO;
using System.Globalization;
using UnityEngine;
using YiGodhome.Bridge;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace YiGodhome;
public sealed partial class BaselinePlugin
{
    private DetailedEventCapture? detailedCapture;
    private double nextDetailSample;
    private void WriteNativeDetail(string line) {
        lock(logLock)if(detailedCapture?.Active==true)detailedCapture.Write(DateTime.UtcNow.ToString("O")+" "+line);
    }
    private double nextNativeDetail;
#if HOLLOW_KNIGHT
    private double detailPeerAt;
    private bool detailPeerSeen;
    private float traceFixtureAt = -1;
    private bool traceFixtureDone;
#else
    private Guid lastDetailRequest;
#endif
    private string DetailContext()
    {
        string manifest = Path.Combine(Path.GetDirectoryName(eventLog)!,"deployment.json");
        string builds = File.Exists(manifest) ? File.ReadAllText(manifest) : "unavailable";
        return "protocol=" + Protocol.Version + " unity=" + Application.unityVersion + " gameVersion=" + Application.version
            + " scene=" + UnitySceneManager.GetActiveScene().name + " encounter=" + encounterStatus?.Id
            + " deployment=" + builds.Replace("\r", "").Replace("\n", " ");
    }
#if HOLLOW_KNIGHT
    private void SetDetailedLogging(bool enabled)
    {
        if (!SessionConfiguration.Current.ManualTools && !SessionConfiguration.Current.Has("trace-probe")) return;
        if (enabled && endpoint?.IsPeerFresh != true) return;
        lock(logLock) {
            try { detailedCapture?.Sync(enabled ? Guid.NewGuid() : Guid.Empty,clock.Elapsed.TotalSeconds,DetailContext()); }
            catch (Exception error) when(error is IOException || error is UnauthorizedAccessException) { detailedCapture?.Stop("context-io-failure"); }
        }
        detailPeerAt = clock.Elapsed.TotalSeconds; detailPeerSeen = false;
        Log("DETAIL_REQUEST active=" + (detailedCapture?.Active == true) + " id=" + detailedCapture?.ActiveId);
    }
#endif
    private void UpdateDetailedLogging()
    {
        if (detailedCapture == null) return;
        lock(logLock) {
#if HOLLOW_KNIGHT
            bool tracePhaseReady = EncounterProfile.Selected.Family != EncounterProfile.NightmareGrimm || contactSource?.Latest != null
                && Array.Exists(contactSource.Latest.Volumes, v => v.Active && (v.Traits & AttackTraits.LargeParryPush) != 0);
            if (SessionConfiguration.Current.Has("trace-probe") && endpoint?.IsPeerFresh == true && hubPresence?.Ready == true && (tracePhaseReady || traceFixtureAt >= 0) && !traceFixtureDone) {
                if (traceFixtureAt < 0) { traceFixtureAt = Time.realtimeSinceStartup; SetDetailedLogging(true); }
                else if (Time.realtimeSinceStartup - traceFixtureAt >= 25) { SetDetailedLogging(false); traceFixtureDone = true; }
            }
            if (detailedCapture.Active) {
                if (endpoint?.IsPeerFresh != true) detailedCapture.Stop("peer-stale");
                else if (endpoint.FreshDetailCapture == detailedCapture.ActiveId) detailPeerSeen = true;
                else if (detailPeerSeen) detailedCapture.Stop("peer-capture-ended");
                else if (clock.Elapsed.TotalSeconds - detailPeerAt > 2) detailedCapture.Stop("peer-capture-not-acknowledged");
            }
#else
            var request = endpoint?.Peer?.DetailCapture ?? Guid.Empty;
            if ((SessionConfiguration.Current.ManualTools || SessionConfiguration.Current.Has("trace-probe")) && endpoint?.IsPeerFresh == true && request != lastDetailRequest) {
                lastDetailRequest = request;
                try { detailedCapture.Sync(request,clock.Elapsed.TotalSeconds,DetailContext()); }
                catch (Exception error) when(error is IOException || error is UnauthorizedAccessException) { detailedCapture.Stop("context-io-failure"); }
            }
            if (detailedCapture.Active && endpoint?.IsPeerFresh != true) detailedCapture.Stop("peer-stale");
#endif
            detailedCapture.Tick(clock.Elapsed.TotalSeconds);
            if (!detailedCapture.Active || clock.Elapsed.TotalSeconds < nextDetailSample) return;
            nextDetailSample = clock.Elapsed.TotalSeconds + .1;
#if HOLLOW_KNIGHT
            long detailCostStart = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            void Detail(string line) => detailedCapture.Write(DateTime.UtcNow.ToString("O") + " " + line);
            var peer = endpoint?.Peer;
            Detail("DETAIL_SAMPLE role=" + Game + " frame=" + Time.frameCount + " realtime=" + Time.realtimeSinceStartup.ToString("R",CultureInfo.InvariantCulture)
                + " gameplayTime=" + Time.time.ToString("R",CultureInfo.InvariantCulture) + " focused=" + Application.isFocused
                + " scene=" + UnitySceneManager.GetActiveScene().name + " encounter=" + encounterStatus?.Id + " phase=" + encounterStatus?.Phase
                + " peerFresh=" + endpoint?.IsPeerFresh + " peerFrame=" + peer?.Frame + " peerSequence=" + peer?.Sequence
                + " inputAck=" + peer?.InputAck + " contactStopped=" + contactEndpoint?.Stopped + " hitStopped=" + hitEndpoint?.Stopped);
#if HOLLOW_KNIGHT
            Detail("DETAIL_HOST paused=" + (GameManager.instance?.gameState.ToString() == "PAUSED") + " menu=" + playerDebugMenu?.Open + " collision=" + playerDebugMenu?.Collision
                + " imageGeometry=" + visualEndpoint?.FreshFrame?.GeometryFrame + " imageSequence=" + visualEndpoint?.FreshFrame?.Sequence);
            DetailedCombatSnapshot.Write(Detail,endpoint?.FreshPose,contactSource?.Latest);
            if (clock.Elapsed.TotalSeconds >= nextNativeDetail) { nextNativeDetail = clock.Elapsed.TotalSeconds + .2; contactSource?.TraceNative(Detail); }
            if (EncounterProfile.Selected.Family == EncounterProfile.NightmareGrimm) HostWorkCost.Detail.Record(detailCostStart, contactSource?.Latest?.Volumes.Length ?? 0, "detail", Log);
#else
            var player = memoryProbe?.ActivePlayer;
            Detail("DETAIL_YI state=" + player?.CurrentStateType + " freeze=" + player?.freeze + " paused=" + NativeInputBridge.HostPaused
                + " animationSpeed=" + player?.animator.speed + " controls=" + inputBridge?.DetailedControls.Buttons + " x=" + inputBridge?.DetailedControls.X
                + " y=" + inputBridge?.DetailedControls.Y + " pendingOutcomes=" + pendingHitKinds.Count + " retainedReceivers=" + NativeMinionOwner.Retained);
            if (player != null) Detail("DETAIL_YI_CONFIGURATION rootSitting=" + player.IsSitAtSavePoint
                + " bridgeRoot=" + NativeRestPoint.Active + " nativeToMenu=" + (GameCore.Instance?.savePanelUiController?.ToMenu??false)
                + " weaponSwitchLocked=" + NativeMenuBoundary.WeaponSwitchLocked(player)
                + " talismanEquipped=" + player.mainAbilities.FooAttackAbility.IsEquipped
                + " outstandingDeposits=" + (FooManager.IsAvailable() && FooManager.Instance.IsAnyDeposit)
                + " chi=" + player.chiContainer.Value + " inventory=" + NativeMenu.InventoryOpen + " controls=" + NativeMenu.ControlsOpen);
            if (clock.Elapsed.TotalSeconds >= nextNativeDetail) {
                nextNativeDetail = clock.Elapsed.TotalSeconds + .2;
                NativeMenuBoundary.TraceConfiguration(Detail); memoryProbe?.Geometry?.TraceNoskCorner(Detail);
            }
            DetailedCombatSnapshot.Write(Detail,memoryProbe?.SamplePose(),contactEndpoint?.FreshFrame);
#endif
        }
    }
}

#endif
