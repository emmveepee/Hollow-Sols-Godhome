using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace YiGodhome.Geometry;

// A deferred publication is not a successful room capture. The caller must
// retain its readiness gate and retry on a later frame, never in a sleep loop.
public sealed class SnapshotPublicationDeferredException : IOException { }

public sealed class SnapshotPublication
{
    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string source, string destination, uint flags);
    private readonly Func<double> now;
    private string? target, pending;
    private double? failedAt;
    private int attempts;
    public SnapshotPublication() : this(() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency) { }
    public SnapshotPublication(Func<double> now) { this.now = now; }

    public void CancelPending(Action<string> log)
    {
        Cleanup(log);
        failedAt = null; attempts = 0;
    }

    // Caller owns SnapshotFileLock. Pending files are private, and only the
    // native atomic rename exposes a complete validated snapshot to readers.
    public void Publish(string path, byte[] bytes, int count, Action<string> log)
    {
        if (count < 0 || count > bytes.Length || count > Codec.MaxBytes) throw new ArgumentOutOfRangeException(nameof(count));
        if (target != path) {
            Cleanup(log);
            target = path; pending = path + ".pending-" + Guid.NewGuid().ToString("N");
            failedAt = null; attempts = 0;
        }
        attempts++;
        try {
            using (var file = new FileStream(pending!, FileMode.Create, FileAccess.Write, FileShare.None)) file.Write(bytes, 0, count);
            // Unlike Mono File.Replace, this does not delete the destination
            // before the rename. Existing delete-sharing readers keep old bytes.
            if (!MoveFileEx(pending!, path, 1)) {
                int code = Marshal.GetLastWin32Error();
                throw new IOException("Atomic geometry rename failed: " + new Win32Exception(code).Message, unchecked((int)0x80070000) | code);
            }
        }
        catch (IOException error) { Failed(error, error.HResult & 0xffff, log); throw; }
        catch (UnauthorizedAccessException error) { Failed(error, 5, log); throw; }
        if (failedAt.HasValue)
            log("GEOMETRY_PUBLICATION_RECOVERED path=" + path + " attempts=" + attempts + " elapsedMs=" + ((now() - failedAt.Value) * 1000).ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
        failedAt = null; attempts = 0;
    }

    private void Failed(Exception error, int code, Action<string> log)
    {
        double time = now();
        bool first = !failedAt.HasValue;
        failedAt ??= time;
        bool retry = (code == 5 || code == 32 || code == 33) && time - failedAt.Value < 1;
        if (first || !retry)
            log("GEOMETRY_PUBLICATION_" + (retry ? "DEFERRED" : "FAILED") + " path=" + target + " pending=" + pending
                + " win32=" + code + " hresult=" + error.HResult + " attempts=" + attempts
                + " targetAttributes=" + Attributes(target!) + " pendingAttributes=" + Attributes(pending!)
                + " elapsedMs=" + ((time - failedAt.Value) * 1000).ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
        if (retry) throw new SnapshotPublicationDeferredException();
        Cleanup(log);
        throw new IOException("Geometry publication failed within its one-second contention budget; win32=" + code + " path=" + target, error);
    }
    private static string Attributes(string path)
    {
        try { return File.GetAttributes(path).ToString(); }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return error.GetType().Name; }
    }
    private void Cleanup(Action<string> log)
    {
        if (pending == null) return;
        try { File.Delete(pending); }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        { log("GEOMETRY_PUBLICATION_CLEANUP_FAILED path=" + pending + " hresult=" + error.HResult); }
    }
}
