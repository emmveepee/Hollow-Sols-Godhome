using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HollowSols.Foundation;

// Local bulk snapshots use an isolated session namespace. Control frames carry
// only tokens/transaction IDs; neither peer supplies a filesystem path.
public sealed class NativeFileExchange
{
    private readonly string root;
    public NativeFileExchange(string root)
    {
        this.root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Path.GetFileName(this.root) != "HollowSols") throw new ArgumentException("Dedicated namespace required.");
    }
    public void Publish(SessionToken token, Guid transaction, byte[] payload)
    {
        if (payload.Length < 1 || payload.Length > PairedSaveStore.MaxPayloadBytes) throw new ArgumentException("Invalid snapshot size.");
        string path = PathFor(token, transaction); Guard(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) { if (!Read(token, transaction).SequenceEqual(payload)) throw new IOException("A snapshot transaction changed its bytes."); return; }
        // Older Unity/Mono on Windows does not support extended-length paths.
        // Keep the temporary leaf bounded instead of appending a second UUID to
        // the transaction filename; the envelope still owns the full identity.
        string pending = Path.Combine(Path.GetDirectoryName(path)!, "p-" + Guid.NewGuid().ToString("N"));
        using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(0x58535348U); writer.Write((ushort)1); writer.Write(token.Connection.ToByteArray()); writer.Write(token.Session.ToByteArray());
            writer.Write(token.Pair.ToByteArray()); writer.Write((byte)token.Slot.Number); writer.Write(token.Ordinal); writer.Write(transaction.ToByteArray());
            writer.Write(payload.Length); writer.Write(Hash(payload)); writer.Write(payload); writer.Flush(); stream.Flush(true);
        }
        File.Move(pending, path);
    }
    // The same strict reader, scheduled off the caller's native frame. Identity,
    // path isolation and checksum failures remain failures, never empty saves.
    public System.Threading.Tasks.Task<byte[]> ReadAsync(SessionToken token,Guid transaction)
        => System.Threading.Tasks.Task.Run(()=>Read(token,transaction));
    public byte[] Read(SessionToken token, Guid transaction)
    {
        string path = PathFor(token, transaction); Guard(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > PairedSaveStore.MaxPayloadBytes + 128) throw new IOException("Snapshot exceeds its budget.");
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != 0x58535348U || reader.ReadUInt16() != 1 || Id(reader) != token.Connection || Id(reader) != token.Session
            || Id(reader) != token.Pair || reader.ReadByte() != token.Slot.Number || reader.ReadInt64() != token.Ordinal || Id(reader) != transaction)
            throw new IOException("Foreign or stale native snapshot.");
        int length = reader.ReadInt32(); if (length < 1 || length > PairedSaveStore.MaxPayloadBytes) throw new IOException("Invalid snapshot length.");
        byte[] hash = reader.ReadBytes(32), payload = reader.ReadBytes(length);
        if (payload.Length != length || !hash.SequenceEqual(Hash(payload)) || stream.Position != stream.Length) throw new IOException("Native snapshot is incomplete or corrupt.");
        return payload;
    }
    private string PathFor(SessionToken token, Guid transaction)
    {
        token.Slot.Validate(); if (token.Connection == Guid.Empty || token.Session == Guid.Empty || token.Pair == Guid.Empty) throw new ArgumentException("Session identity required.");
        return Path.Combine(root, "transport", token.Connection.ToString("N"), token.Session.ToString("N"), transaction == Guid.Empty ? "initial.ns" : transaction.ToString("N") + ".ns");
    }
    private static Guid Id(BinaryReader reader) { byte[] bytes = reader.ReadBytes(16); if (bytes.Length != 16) throw new EndOfStreamException(); return new Guid(bytes); }
    private static byte[] Hash(byte[] bytes) { using var sha = SHA256.Create(); return sha.ComputeHash(bytes); }
    private static void Guard(string path)
    {
        for (string? parent = Path.GetFullPath(path); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
            if ((File.Exists(parent) || Directory.Exists(parent)) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked snapshot paths are prohibited.");
    }
}
