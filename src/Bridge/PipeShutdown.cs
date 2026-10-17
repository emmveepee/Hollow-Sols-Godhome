using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace YiGodhome.Bridge;

// Unity Mono can block Dispose while another thread owns a synchronous native
// connect/read. Invalidate state first; never close that handle on Unity's thread.
internal static class PipeShutdown
{
    internal static void Close(PipeStream? pipe)
    {
        if (pipe == null) return;
        ThreadPool.QueueUserWorkItem(_ => {
            try { pipe.Dispose(); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        });
    }
}
