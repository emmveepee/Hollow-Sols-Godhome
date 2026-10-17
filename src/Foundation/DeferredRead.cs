// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Threading.Tasks;

namespace HollowSols.Foundation;

// Request/Poll/Cancel belong to the game thread. Only the supplied immutable
// file read runs on a worker. At most one running and one replacement read exist.
// Cancellation retires adoption; it never interrupts a store lease or native API.
public sealed class DeferredRead<T> where T : class
{
    private sealed class Request
    {
        internal readonly long Version;
        internal readonly Guid Connection;
        internal readonly Func<T> Read;
        internal Request(long version, Guid connection, Func<T> read)
        { Version = version; Connection = connection; Read = read; }
    }
    private Request? queued, running;
    private Task<T>? work;
    public long Version { get; private set; }
    public bool Busy => work != null || queued != null;

    public void Begin(Guid connection, Func<T> read)
    {
        if (connection == Guid.Empty) throw new ArgumentException("A live connection is required.");
        if (read == null) throw new ArgumentNullException(nameof(read));
        queued = new Request(++Version, connection, read);
    }

    public void Cancel() { ++Version; queued = null; }

    public bool Poll(Guid connection, out T? result)
    {
        result = null;
        if (work != null && work.IsCompleted)
        {
            var completed = work; var request = running!;
            work = null; running = null;
            bool current = request.Version == Version && request.Connection == connection;
            try
            {
                var value = completed.GetAwaiter().GetResult();
                if (current) { result = value; return true; }
            }
            catch { if (current) throw; } // Observe retired failures without failing a later connection.
        }
        if (work == null && queued != null)
        {
            var request = queued; queued = null;
            if (request.Connection == connection)
            {
                running = request; work = Task.Run(request.Read);
                // Also observe a fault if the owning component is destroyed and
                // never polls again. No continuation touches game state or UI.
                _ = work.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        return false;
    }
}
