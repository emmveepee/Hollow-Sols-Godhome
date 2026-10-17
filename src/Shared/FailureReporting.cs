// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using HollowSols.Foundation;
using YiGodhome.Bridge;
using UnityEngine;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace YiGodhome;

public sealed partial class HollowSolsMenuPlugin
{
    private readonly Guid reportInstance = Guid.NewGuid();
    private Guid reportAttempt = Guid.NewGuid();
    private FailureReportStore? reports;
    private ReportContext? reportContext;
    private readonly Queue<string> reportMenuLines = new Queue<string>();
    private int reportMenuCharacters;
    private Task reportWork = Task.CompletedTask;
    private Task<Guid?>? pendingReports;
    private Task<string>? preparingReport;
    private Guid? currentReport;
    private Guid preparingIncident;
    private Action? reportContinue;
    private bool reportPromptShown, reportDisarmed;
    private int reportProcess;
    private long reportProcessStart;
    private string reportBuild = "", repositoryUrl = "";
    private readonly object reportQueueGate = new object();
    private readonly object reportMenuGate = new object();
    private double preparingSince;
    private bool reportExportDelayShown;

    private Task<T> ReportWork<T>(Func<T> work)
    {
        lock (reportQueueGate) {
            var next = reportWork.ContinueWith(_ => work(), TaskScheduler.Default);
            reportWork = next;
            // Observe background errors even if shutdown prevents the UI poll.
            _ = next.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            return next;
        }
    }
    private void InitializeFailureReporting()
    {
        if (fixtureId != Guid.Empty) return; // Existing native fixtures never gain report UI/I/O.
        try {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HollowSols", "logs", "reports");
            reports = new FailureReportStore(root, Path.Combine(SaveRoot, "runtime", "runs"),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName, Environment.MachineName);
            using var process = Process.GetCurrentProcess(); reportProcess = process.Id; reportProcessStart = process.StartTime.ToUniversalTime().Ticks;
            reportBuild = "flavor=" + BuildFeatures.Flavor + " assembly=" + typeof(HollowSolsMenuPlugin).Assembly.GetName().Version
                + " module=" + typeof(HollowSolsMenuPlugin).Module.ModuleVersionId + " unity=" + Application.unityVersion + " game=" + Application.version;
            repositoryUrl = Config.Bind("Support", "RepositoryUrl", BuildFeatures.ReportRepository, "GitHub repository URL (https://github.com/OWNER/REPO). Empty disables browser submission; local export remains available.").Value;
            pendingReports = ReportWork(() => { reports.RecoverAbandoned(ReportOwnerAlive); return reports.FindPending(); });
        } catch (Exception error) { Logger.LogWarning("REPORT_INITIALIZATION_UNAVAILABLE " + error.GetType().Name); reports = null; }
    }
    private static bool ReportOwnerAlive(int id, long started)
    {
        try { using var process = Process.GetProcessById(id); return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == started; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; } // Uncertain owner is not declared dead.
    }
    private void RecordReportMenu(string line)
    {
        if (reports == null) return;
        if (line.Length > 8192) line = line.Substring(0, 8192);
        lock (reportMenuGate) {
            reportMenuLines.Enqueue(DateTime.UtcNow.ToString("O") + " " + line); reportMenuCharacters += line.Length + 34;
            while (reportMenuCharacters > 128 * 1024 && reportMenuLines.Count > 0) reportMenuCharacters -= reportMenuLines.Dequeue().Length;
        }
    }
    private void BeginReportAttempt()
    {
        reportAttempt = Guid.NewGuid(); reportContext = null; currentReport = null; reportPromptShown = false; reportDisarmed = false;
        lock (reportMenuGate) { reportMenuLines.Clear(); reportMenuCharacters = 0; }
        SyncReportContext();
    }
    private void SyncReportContext()
    {
        if (reports == null || reportDisarmed) return;
        Guid session = activeToken?.Session ?? Guid.Empty, connection = controller?.Lifecycle.Connection ?? Guid.Empty;
        Guid incident = session != Guid.Empty ? session : connection != Guid.Empty ? connection : reportAttempt;
        if (reportContext?.Incident == incident) return;
        var context = new ReportContext(incident, session, connection, Side, reportBuild, UnitySceneManager.GetActiveScene().name);
        reportContext = context;
        _ = ReportWork(() => { reports.Arm(reportInstance, context, reportProcess, reportProcessStart); return true; });
    }
    private void DisarmReports()
    {
        reportDisarmed = true;
        if (reports != null) _ = ReportWork(() => { reports.Disarm(reportInstance); return true; });
    }
    private void CompleteReportShutdown()
    {
        reportDisarmed = true;
        if (reports == null) return;
        try {
            lock (reportQueueGate) {
                if (!reports.DisarmBeforeExit(reportInstance, reportWork, TimeSpan.FromSeconds(2)))
                    Logger.LogWarning("REPORT_ORDERLY_SHUTDOWN_UNCONFIRMED workerDeadlineSeconds=2");
            }
        } catch (Exception error) { Logger.LogWarning("REPORT_ORDERLY_SHUTDOWN_UNAVAILABLE " + error.GetType().Name); }
    }
    private void CaptureReportFailure(string failure)
    {
        if (reports == null || reportDisarmed || requestedQuit || fixtureId != Guid.Empty) return;
        SyncReportContext();
        var original = reportContext!;
        var context = new ReportContext(original.Incident, original.Session, original.Connection, Side, reportBuild, UnitySceneManager.GetActiveScene().name);
        string tail; lock (reportMenuGate) tail = string.Join("\n", reportMenuLines);
        if (currentReport == context.Incident) return;
        currentReport = context.Incident;
        _ = ReportWork(() => { reports.Capture(context, failure, tail); reports.Disarm(reportInstance); return true; });
    }
    private void ShowFailureNotice(string notice, Action continued, bool shutdown = false)
    {
        reportContinue = () => { AcknowledgeReport(); continued(); };
        if (currentReport.HasValue && reports != null || reports == null && fixtureId == Guid.Empty && !requestedQuit && !reportDisarmed) {
            reportPromptShown = true;
            if (shutdown) menu?.FailureShutdownNotice(notice, PrepareBugReport, reportContinue);
            else menu?.FailureNotice(notice, PrepareBugReport, reportContinue);
        } else if (shutdown) menu?.ShutdownNotice(notice, continued);
        else menu?.Notice(notice, continued);
    }
    private void AcknowledgeReport()
    {
        var incident = currentReport; currentReport = null; reportPromptShown = false;
        if (reports != null && incident.HasValue) _ = ReportWork(() => { reports.Acknowledge(incident.Value); return true; });
    }
    private void PrepareBugReport()
    {
        if (reports == null) {
            menu?.Notice("The report exporter is unavailable. Ordinary logs remain under the Hollow Sols application logs folder. Nothing has been uploaded.", reportContinue ?? RestoreVanillaMenu);
            return;
        }
        if (!currentReport.HasValue || preparingReport != null) return;
        Guid incident = currentReport.Value;
        preparingIncident = incident;
        preparingSince = Now; reportExportDelayShown = false;
        preparingReport = ReportWork(() => reports.Export(incident));
    }
    private void UpdateFailureReporting()
    {
        if (preparingReport != null && !preparingReport.IsCompleted && Now - preparingSince > 10 && !reportExportDelayShown) {
            reportExportDelayShown = true;
            menu?.Notice("Report export is taking too long. You can continue safely; no files have been uploaded. Local report folder: " + reports!.Root,
                reportContinue ?? RestoreVanillaMenu);
        }
        if (preparingReport?.IsCompleted == true) {
            var task = preparingReport; preparingReport = null;
            if (currentReport != preparingIncident) { _ = task.Exception; return; }
            if (task.IsFaulted || task.IsCanceled) {
                _ = task.Exception;
                menu?.Notice("The report could not be exported. Your logs and committed saves are preserved. Local report folder: " + reports!.Root,
                    () => ShowFailureNotice("The bridge connection stopped. You can retry preparing the report or continue.", RestoreVanillaMenu));
            } else {
                string path = task.Result;
                Uri? issue = FailureReportStore.IssueUrl(repositoryUrl, preparingIncident,
                    reportContext?.Incident == preparingIncident ? reportBuild : "Previous run: see attached report");
                string text = FailureReportStore.OpenReport(path, issue,
                    file => { using var process = Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = true }); },
                    uri => { using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); });
                bool copied = false;
                try { GUIUtility.systemCopyBuffer = text; copied = true; } catch (Exception) { /* Visible path and Explorer remain available. */ }
                string caption = copied ? "Report ready. Attach this ZIP, describe what happened, then submit. "
                    + (issue == null ? "GitHub submission is unavailable until a repository is configured." : "Your browser was requested to open the issue form.") : text;
                menu?.Notice(caption, reportContinue ?? RestoreVanillaMenu);
            }
        }
        if (pendingReports?.IsCompleted == true && !reportPromptShown && !activeToken.HasValue && controller?.Lifecycle.State == BridgeState.Vanilla && menu?.FixtureReady == true) {
            var task = pendingReports; pendingReports = null;
            if (task.IsFaulted || task.IsCanceled) { _ = task.Exception; return; }
            if (task.Result.HasValue) { currentReport = task.Result; ShowFailureNotice("A previous Hollow Sols bridge session ended unexpectedly. You can prepare its diagnostic report. Nothing is uploaded automatically.", RestoreVanillaMenu); }
        }
        if (!reportDisarmed && controller?.Lifecycle.State != BridgeState.Vanilla && controller?.Lifecycle.State != BridgeState.Failed) SyncReportContext();
    }
}
