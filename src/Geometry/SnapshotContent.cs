using System;
using System.IO;

namespace YiGodhome.Geometry;

// Caller owns both streams/buffers and the existing geometry exchange lock.
// Scratch never becomes a published snapshot or an asynchronous frame.
internal static class SnapshotContent
{
    internal static bool Matches(Stream existing, byte[] expected, int count, byte[] scratch)
    {
        if (count < 0 || count > expected.Length || scratch.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (existing.Length != count) return false;
        existing.Position = 0;
        int offset = 0;
        while (offset < count) {
            int received = existing.Read(scratch, 0, Math.Min(scratch.Length, count - offset));
            if (received == 0) return false;
            for (int i = 0; i < received; i++) if (scratch[i] != expected[offset + i]) return false;
            offset += received;
        }
        return existing.Length == count;
    }
}
