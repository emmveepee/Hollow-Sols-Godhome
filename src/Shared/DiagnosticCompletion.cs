using System;
using System.IO;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

public sealed partial class BaselinePlugin
{
    private double nextCompletionPoll;
#if HOLLOW_KNIGHT
    private bool diagnosticCompletionRequested;
    private int completedHubCombats;
#endif
    private static readonly string[] completionExcludedFixtures={"retry-probe","travel-probe","wall-probe","rest-probe",
        "lever-probe","configuration-probe","hud-reference-probe"};
    private bool AutomaticCombatCompletionAllowed {
        get {
            var config=SessionConfiguration.Current;
            if(!config.RunFixtures || !hubCombat || smokeSeconds<=0
                || config.FailureBoundary.Length!=0 || config.Pantheon!=0)return false;
            if(config.Has("trace-probe") && EncounterProfile.Selected.Family==EncounterProfile.Collector && smokeSeconds>=580)return false;
            foreach(var flag in completionExcludedFixtures)
                if(config.Has(flag)) {
#if HOLLOW_KNIGHT
                    if(flag=="configuration-probe" && configurationMenu?.FixtureComplete==true)continue;
                    if(flag=="lever-probe" && hubPresence?.LeverFixtureComplete==true)continue;
#else
                    if(flag=="configuration-probe")continue; // Host request proves its fixture completed.
                    if(flag=="lever-probe")continue;
#endif
                    return false;
                }
            return true;
        }
    }
    private string CompletionPath(string game,string name) {
        string run=Path.GetDirectoryName(eventLog)!;
        string root=Path.GetDirectoryName(Path.GetDirectoryName(run))!;
        return Path.Combine(root,game,sessionId.ToString(),name);
    }
    private static bool MatchesCompletionToken(string path,string token) => File.Exists(path)
        && new FileInfo(path).Length<=128 && File.ReadAllText(path).Trim()==token;
#if HOLLOW_KNIGHT
    private void RecordCompletedDiagnosticCombat() {
        completedHubCombats++;
        if(!AutomaticCombatCompletionAllowed)return;
        int planned=SessionConfiguration.Current.Has("invulnerable-probe")?1:ScriptedHubEntryLimit;
        if(completedHubCombats<planned || hubCombatEntries<planned || encounterStatus==null)return;
        bool mechanical=SessionConfiguration.Current.Has("kick-probe") || SessionConfiguration.Current.Has("crimson-probe") || SessionConfiguration.Current.Has("pv-tuning-probe");
        string token=sessionId+"|"+encounterStatus.Id+(mechanical?"|mechanics":"");
        File.WriteAllText(CompletionPath("HollowKnight","diagnostic-complete.request"),token);
        diagnosticCompletionRequested=true;
        Log("HK_DIAGNOSTIC_COMPLETE entries="+hubCombatEntries+" returns="+completedHubCombats
            +" planned="+planned+" encounter="+encounterStatus.Id+" nativeReturn=True drained=True");
    }
#endif
    private bool FinishCompletedDiagnosticCombat() {
        if(!AutomaticCombatCompletionAllowed || clock.Elapsed.TotalSeconds<nextCompletionPoll)return false;
        nextCompletionPoll=clock.Elapsed.TotalSeconds+.1;
        bool fixture=SessionConfiguration.Current.Has("configuration-probe") || SessionConfiguration.Current.Has("lever-probe");
        if(!fixture && encounterStatus==null)return false;
        bool mechanical=SessionConfiguration.Current.Has("kick-probe") || SessionConfiguration.Current.Has("crimson-probe") || SessionConfiguration.Current.Has("pv-tuning-probe");
        string token=fixture?sessionId+"|fixture":sessionId+"|"+encounterStatus!.Id+(mechanical?"|mechanics":"");
#if HOLLOW_KNIGHT
        if(mechanical && !diagnosticCompletionRequested && encounterStatus!.Phase==EncounterPhase.Active
            && (SessionConfiguration.Current.Has("pv-tuning-probe")?hostHits?.NativeCounterHolds>=3:hostHits?.AcceptedDefenses>=8)) {
            File.WriteAllText(CompletionPath("HollowKnight","diagnostic-complete.request"),token);
            diagnosticCompletionRequested=true;
            Log("HK_DIAGNOSTIC_MECHANICS_COMPLETE defenses="+hostHits?.AcceptedDefenses+" nativeHolds="+hostHits?.NativeCounterHolds+" victoryClaimed=False");
        }
        if(fixture && (hubPresence?.Ready!=true || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="GG_Workshop" || GameManager.instance.IsGamePaused()))return false;
        if(!diagnosticCompletionRequested && fixture) {
            File.WriteAllText(CompletionPath("HollowKnight","diagnostic-complete.request"),token);
            diagnosticCompletionRequested=true;Log("HK_DIAGNOSTIC_FIXTURE_COMPLETE nativeResult=True drained=True");
        }
        if(!diagnosticCompletionRequested || !MatchesCompletionToken(CompletionPath("NineSols","diagnostic-complete.ack"),token))return false;
#else
        if((fixture?NativeRoomEntry.Active || NativeMenu.Open:mechanical?encounterStatus!.Phase!=EncounterPhase.Active && encounterStatus.Phase!=EncounterPhase.Ready:encounterStatus!.Phase!=EncounterPhase.Ready) || pendingHitKinds.Count!=0
            || !MatchesCompletionToken(CompletionPath("HollowKnight","diagnostic-complete.request"),token))return false;
        File.WriteAllText(CompletionPath("NineSols","diagnostic-complete.ack"),token);
#endif
        quitting=true;
        lock(logLock)detailedCapture?.Stop("diagnostic-complete");
        Log("SMOKE_EXIT_REQUEST game="+Game+" reason=diagnostic-complete encounter="+encounterStatus?.Id
            +" elapsedMs="+clock.ElapsedMilliseconds+" frame="+Time.frameCount);
        Application.Quit();
        return true;
    }
}
