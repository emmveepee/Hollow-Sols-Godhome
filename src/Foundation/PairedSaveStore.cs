using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace HollowSols.Foundation;

public enum CheckpointBoundary { Prepared, HollowKnightFlushed, NineSolsFlushed, ManifestFlushed, Published }

// Only HK's coordinator receives this store. Native serializers supply opaque,
// complete payloads; the store never calls either game's native save APIs.
public sealed class PairedSaveStore
{
    public const int MaxPayloadBytes = 64 * 1024 * 1024;
    private const uint Magic = 0x53534850;
    private const ushort Version = 1;
    private const string NamespaceMarker = "HollowSols paired saves schema 1\n";
    private readonly string root;
    private readonly Action<CheckpointBoundary>? boundary;
    private readonly Action<string>? leaseTrace;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> ActiveLeases = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();

    // No native save directory default. The caller must supply the dedicated namespace.
    public PairedSaveStore(string dedicatedRoot, Action<CheckpointBoundary>? boundary = null, Action<string>? leaseTrace = null)
    {
        root = Path.GetFullPath(dedicatedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetFileName(root), "HollowSols", StringComparison.Ordinal))
            throw new ArgumentException("A dedicated HollowSols namespace is required.");
        this.boundary = boundary;
        this.leaseTrace = leaseTrace;
        CheckAncestors(root);
        Directory.CreateDirectory(root);
        using var lease = Lock();
        string marker = Path.Combine(root, "namespace");
        CheckAncestors(marker);
        if (!File.Exists(marker))
        {
            if (Directory.EnumerateFileSystemEntries(root).Any(p => Path.GetFileName(p) != "store.lock"))
                throw new IOException("Refusing to adopt an unrecognized save directory.");
            WriteNew(marker, Encoding.UTF8.GetBytes(NamespaceMarker));
        }
        if (File.ReadAllText(marker) != NamespaceMarker) throw new IOException("Unknown paired-save namespace schema.");
    }

    public SlotSummary[] ListSlots()
    {
        using var lease = Lock();
        var result = new SlotSummary[ModSlot.Count];
        for (int i = 0; i < result.Length; i++)
        {
            var slot = new ModSlot(i + 1);
            string path = SlotPath(slot);
            if (!Directory.Exists(path)) { result[i] = new SlotSummary(slot, null, SlotCondition.Empty, 0); continue; }
            SavePair? pair = null;
            try
            {
                pair = ReadPair(slot);
                var checkpoint = Recover(pair);
                result[i] = new SlotSummary(slot, pair, checkpoint.Recovered ? SlotCondition.RecoveryAvailable : SlotCondition.Ready, checkpoint.Generation);
            }
            catch (Exception e) when (IsInvalidData(e)) { result[i] = new SlotSummary(slot, pair, SlotCondition.Unrecoverable, 0); }
        }
        return result;
    }

    // Invocation must come from the confirmed native new-game flow. Fixture callers
    // are explicitly authorized by tests; connection/startup never calls Create.
    public PairedCheckpoint Create(ModSlot slot, NewGameChoice choice, Guid session, byte[] hk, byte[] ns)
    {
        slot.Validate(); choice.Validate(); ValidatePayloads(session, hk, ns);
        using var lease = Lock();
        string path = SlotPath(slot);
        if (Directory.Exists(path) || File.Exists(path)) throw new IOException("This slot already contains data; creation cannot replace it.");
        Directory.CreateDirectory(path);
        var pair = new SavePair(slot, Guid.NewGuid(), choice);
        // Preserve failed creation as occupied/incomplete; never invent a replacement.
        WriteNew(Path.Combine(path, "pair"), PairBytes(pair));
        return WriteCheckpoint(pair, 1, session, hk, ns);
    }

    public SavePair ReserveCreation(ModSlot slot, NewGameChoice choice)
    {
        slot.Validate(); choice.Validate(); using var lease = Lock();
        string path = SlotPath(slot);
        if (Directory.Exists(path) || File.Exists(path)) throw new IOException("This slot already contains data.");
        Directory.CreateDirectory(path); var pair = new SavePair(slot, Guid.NewGuid(), choice);
        WriteNew(Path.Combine(path, "pair"), PairBytes(pair)); return pair;
    }
    public SavePair InspectPair(ModSlot slot, Guid expected)
    {
        using var lease = Lock(); var pair = ReadPair(slot);
        if (pair.Id != expected) throw new SaveUnavailableException(); return pair;
    }
    public PairedCheckpoint CompleteCreation(SessionToken token, byte[] hk, byte[] ns)
    {
        ValidatePayloads(token.Session, hk, ns); using var lease = Lock(); var pair = ReadPair(token.Slot);
        if (pair.Id != token.Pair || Generations(pair).Any()) throw new IOException("Creation identity is stale or already has checkpoint data.");
        return WriteCheckpoint(pair, 1, token.Session, hk, ns);
    }

    public PairedCheckpoint Load(ModSlot slot, Guid expectedPair)
    {
        using var lease = Lock();
        var pair = ReadPair(slot);
        if (pair.Id != expectedPair) throw new SaveUnavailableException();
        return Recover(pair);
    }

    public PairedCheckpoint Save(ModSlot slot, Guid expectedPair, long expectedGeneration, Guid session, byte[] hk, byte[] ns, Guid transaction = default)
    {
        ValidatePayloads(session, hk, ns);
        using var lease = Lock();
        var pair = ReadPair(slot);
        if (pair.Id != expectedPair) throw new SaveUnavailableException();
        var current = Recover(pair);
        if (current.Generation != expectedGeneration) throw new IOException("A newer save exists; this operation is stale.");
        long highest = Generations(pair).Select(p => p.Generation).DefaultIfEmpty(0).Max();
        return WriteCheckpoint(pair, checked(highest + 1), session, hk, ns, transaction);
    }

    // Retained archive is deletion from the four-slot menu, not destructive repair.
    // Coordinator must end/drain any live session before calling this method.
    public string DeleteConfirmed(ModSlot slot, Guid confirmedPair)
    {
        using var lease = Lock();
        var pair = ReadPair(slot);
        if (confirmedPair == Guid.Empty || pair.Id != confirmedPair) throw new IOException("Deletion confirmation no longer matches this save.");
        string archive = Path.Combine(root, "deleted");
        CheckAncestors(archive); Directory.CreateDirectory(archive);
        string target = Path.Combine(archive, pair.Id.ToString("N") + "-" + Guid.NewGuid().ToString("N"));
        CheckTree(SlotPath(slot));
        Directory.Move(SlotPath(slot), target);
        return target;
    }
    public SlotDeletionConfirmation PrepareDeletion(ModSlot slot)
    {
        using var lease = Lock();
        return new SlotDeletionConfirmation(slot, root, DeletionFingerprint(slot));
    }
    public string DeleteConfirmed(SlotDeletionConfirmation confirmation)
    {
        if (confirmation == null || confirmation.Root != root) throw new IOException("Foreign deletion confirmation.");
        using var lease = Lock();
        if (DeletionFingerprint(confirmation.Slot) != confirmation.Fingerprint) throw new IOException("This slot changed after deletion was confirmed.");
        string archive = Path.Combine(root, "deleted"); CheckAncestors(archive); Directory.CreateDirectory(archive);
        string target = Path.Combine(archive, "slot-" + confirmation.Slot.Number + "-" + Guid.NewGuid().ToString("N"));
        Directory.Move(SlotPath(confirmation.Slot), target); return target;
    }
    private string DeletionFingerprint(ModSlot slot)
    {
        string path = SlotPath(slot);
        if (!Directory.Exists(path)) throw new IOException("This slot is empty.");
        CheckTree(path);
        var entries = Directory.GetFileSystemEntries(path, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (entries.Length > 100000) throw new IOException("Save directory exceeds the confirmation budget.");
        var text = new StringBuilder(); text.Append(Directory.GetCreationTimeUtc(path).Ticks);
        foreach (string entry in entries) {
            text.Append('|').Append(entry.Substring(path.Length)).Append(':').Append(File.GetLastWriteTimeUtc(entry).Ticks);
            if (File.Exists(entry)) text.Append(':').Append(new FileInfo(entry).Length);
        }
        string pair = Path.Combine(path, "pair");
        if (File.Exists(pair) && new FileInfo(pair).Length <= 64) text.Append(':').Append(Convert.ToBase64String(File.ReadAllBytes(pair)));
        using var hash = SHA256.Create(); return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private IDisposable Lock([System.Runtime.CompilerServices.CallerMemberName] string operation = "")
    {
        CheckAncestors(root);
        string path = Path.Combine(root, "store.lock"); CheckAncestors(path);
        // No waiting on Unity's main thread; a busy store is a retryable UI error.
        leaseTrace?.Invoke("STORE_LEASE_REQUEST operation=" + operation);
        using var hash = SHA256.Create();
        string identity = Environment.OSVersion.Platform == PlatformID.Win32NT ? root.ToUpperInvariant() : root;
        string name = "HollowSols.SaveStore." + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "");
        if (!ActiveLeases.TryAdd(name, 0)) throw new IOException("The paired save store is already in use by this process.");
        System.Threading.Mutex? mutex = null; bool acquired = false; FileStream? stream = null;
        try {
            mutex = new System.Threading.Mutex(false, name);
            try { acquired = mutex.WaitOne(0); } catch (System.Threading.AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("The paired save store is busy. Try again after the current operation finishes.");
            if (!File.Exists(path)) { using var marker = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read); }
            // The kernel mutex serializes cooperating processes. A read-only
            // marker handle avoids exclusive-share conflicts with file scanners;
            // an explicitly exclusive external handle still makes us fail closed.
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            leaseTrace?.Invoke("STORE_LEASE_OPEN operation=" + operation); return new StoreLease(stream, mutex, name, leaseTrace, operation);
        } catch { try { stream?.Dispose(); if (acquired) mutex!.ReleaseMutex(); } finally { mutex?.Dispose(); ActiveLeases.TryRemove(name, out _); } throw; }
    }
    private sealed class StoreLease : IDisposable
    {
        private readonly FileStream stream; private readonly System.Threading.Mutex mutex; private readonly string name; private readonly Action<string>? trace; private readonly string operation;
        internal StoreLease(FileStream stream, System.Threading.Mutex mutex, string name, Action<string>? trace, string operation) { this.stream = stream; this.mutex = mutex; this.name = name; this.trace = trace; this.operation = operation; }
        public void Dispose() { try { stream.Dispose(); } finally { try { mutex.ReleaseMutex(); mutex.Dispose(); } finally { ActiveLeases.TryRemove(name, out _); } } trace?.Invoke("STORE_LEASE_CLOSED operation=" + operation); }
    }

    private string SlotPath(ModSlot slot)
    {
        slot.Validate();
        string path = Path.Combine(root, "slot-" + slot.Number.ToString(CultureInfo.InvariantCulture));
        CheckAncestors(path);
        return path;
    }

    private SavePair ReadPair(ModSlot slot)
    {
        string path = Path.Combine(SlotPath(slot), "pair"); CheckAncestors(path);
        using var reader = new BinaryReader(new MemoryStream(ReadBounded(path, 64)), Encoding.UTF8, false);
        var pair = DecodePair(reader);
        if (!pair.Slot.Equals(slot) || reader.BaseStream.Position != reader.BaseStream.Length) throw new SaveUnavailableException();
        return pair;
    }

    private static byte[] PairBytes(SavePair pair)
    {
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes, Encoding.UTF8, true);
        EncodePair(writer, pair); writer.Flush(); return bytes.ToArray();
    }
    private static void EncodePair(BinaryWriter writer, SavePair pair)
    {
        writer.Write(Magic); writer.Write(Version); writer.Write((byte)pair.Slot.Number);
        writer.Write(pair.Id.ToByteArray()); writer.Write((byte)pair.Choice.Mode); writer.Write(pair.Choice.FullGodhomeAccess);
    }
    private static SavePair DecodePair(BinaryReader reader)
    {
        if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != Version) throw new SaveUnavailableException();
        var slot = new ModSlot(reader.ReadByte()); var id = ReadGuid(reader); var mode = (GameMode)reader.ReadByte();
        byte full = reader.ReadByte(); if (full > 1) throw new SaveUnavailableException();
        return new SavePair(slot, id, new NewGameChoice(mode, true, full == 1));
    }
    private static Guid ReadGuid(BinaryReader reader)
    {
        var value = reader.ReadBytes(16); if (value.Length != 16) throw new EndOfStreamException();
        var id = new Guid(value); if (id == Guid.Empty) throw new SaveUnavailableException(); return id;
    }

    private PairedCheckpoint WriteCheckpoint(SavePair pair, long generation, Guid session, byte[] hk, byte[] ns, Guid transaction = default)
    {
        // Snapshot before publishing anything; caller mutation cannot alter stored bytes.
        hk = (byte[])hk.Clone(); ns = (byte[])ns.Clone();
        if (transaction == Guid.Empty) transaction = Guid.NewGuid();
        string directory = Path.Combine(SlotPath(pair.Slot), "generation-" + generation.ToString("D20", CultureInfo.InvariantCulture) + "-" + transaction.ToString("N"));
        CheckAncestors(directory); Directory.CreateDirectory(directory);
        WriteNew(Path.Combine(directory, "prepare"), PairBytes(pair)); boundary?.Invoke(CheckpointBoundary.Prepared);
        WriteNew(Path.Combine(directory, "hk.payload"), hk); boundary?.Invoke(CheckpointBoundary.HollowKnightFlushed);
        WriteNew(Path.Combine(directory, "ns.payload"), ns); boundary?.Invoke(CheckpointBoundary.NineSolsFlushed);
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes, Encoding.UTF8, true);
        EncodePair(writer, pair); writer.Write(generation); writer.Write(transaction.ToByteArray()); writer.Write(session.ToByteArray());
        writer.Write(hk.Length); writer.Write(Hash(hk)); writer.Write(ns.Length); writer.Write(Hash(ns)); writer.Flush();
        WriteNew(Path.Combine(directory, "commit.pending"), bytes.ToArray()); boundary?.Invoke(CheckpointBoundary.ManifestFlushed);
        // This rename publishes both immutable payloads together to cooperating readers.
        // Filesystem/power-loss durability of directory metadata is not cross-process atomicity.
        File.Move(Path.Combine(directory, "commit.pending"), Path.Combine(directory, "commit"));
        boundary?.Invoke(CheckpointBoundary.Published);
        return ReadCheckpoint(pair, directory, generation, transaction);
    }

    private PairedCheckpoint Recover(SavePair pair)
    {
        var generations = Generations(pair).OrderByDescending(g => g.Generation).ToArray();
        bool recovered = false;
        foreach (var group in generations.GroupBy(g => g.Generation))
        {
            // Duplicate sequence numbers are ambiguous; never choose by timestamp.
            if (group.Count() != 1) { recovered = true; continue; }
            var candidate = group.Single();
            try
            {
                var checkpoint = ReadCheckpoint(pair, candidate.Path, candidate.Generation, candidate.Transaction);
                checkpoint.Recovered = recovered;
                return checkpoint;
            }
            catch (Exception e) when (IsInvalidData(e)) { recovered = true; }
        }
        throw new SaveUnavailableException();
    }

    private IEnumerable<(string Path, long Generation, Guid Transaction)> Generations(SavePair pair)
    {
        string path = SlotPath(pair.Slot); CheckTree(path);
        foreach (string directory in Directory.EnumerateDirectories(path))
        {
            string name = Path.GetFileName(directory);
            if (name.Length != 64 || !name.StartsWith("generation-", StringComparison.Ordinal) || name[31] != '-'
                || !long.TryParse(name.Substring(11, 20), NumberStyles.None, CultureInfo.InvariantCulture, out long generation) || generation < 1
                || !Guid.TryParseExact(name.Substring(32), "N", out var transaction) || transaction == Guid.Empty)
                throw new SaveUnavailableException();
            yield return (directory, generation, transaction);
        }
    }

    private static PairedCheckpoint ReadCheckpoint(SavePair expected, string directory, long generation, Guid transaction)
    {
        string path = Path.Combine(directory, "commit");
        using var reader = new BinaryReader(new MemoryStream(ReadBounded(path, 256)), Encoding.UTF8, false);
        var pair = DecodePair(reader);
        if (pair.Id != expected.Id || !pair.Slot.Equals(expected.Slot) || pair.Choice.Mode != expected.Choice.Mode
            || pair.Choice.FullGodhomeAccess != expected.Choice.FullGodhomeAccess || reader.ReadInt64() != generation || ReadGuid(reader) != transaction)
            throw new SaveUnavailableException();
        var session = ReadGuid(reader);
        byte[] hk = ReadPayload(reader, Path.Combine(directory, "hk.payload"));
        byte[] ns = ReadPayload(reader, Path.Combine(directory, "ns.payload"));
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new SaveUnavailableException();
        return new PairedCheckpoint(pair, generation, transaction, session, hk, ns);
    }

    private static byte[] ReadPayload(BinaryReader reader, string path)
    {
        int length = reader.ReadInt32(); if (length <= 0 || length > MaxPayloadBytes) throw new SaveUnavailableException();
        byte[] digest = reader.ReadBytes(32); if (digest.Length != 32) throw new EndOfStreamException();
        byte[] payload = ReadBounded(path, MaxPayloadBytes);
        if (payload.Length != length || !Hash(payload).SequenceEqual(digest)) throw new SaveUnavailableException();
        return payload;
    }
    private static byte[] Hash(byte[] bytes) { using var sha = SHA256.Create(); return sha.ComputeHash(bytes); }
    private static void ValidatePayloads(Guid session, byte[] hk, byte[] ns)
    {
        if (session == Guid.Empty) throw new ArgumentException("A save requires its gameplay session identity.");
        if (hk == null || ns == null || hk.Length == 0 || ns.Length == 0 || hk.Length > MaxPayloadBytes || ns.Length > MaxPayloadBytes)
            throw new ArgumentException("Both bounded native payloads are required.");
    }
    private static bool IsInvalidData(Exception e) => e is IOException || e is SaveUnavailableException || e is ArgumentException || e is UnauthorizedAccessException;
    private static byte[] ReadBounded(string path, int max)
    {
        CheckAncestors(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 1 || stream.Length > max) throw new SaveUnavailableException();
        byte[] bytes = new byte[(int)stream.Length]; int offset = 0;
        while (offset < bytes.Length) { int read = stream.Read(bytes, offset, bytes.Length - offset); if (read == 0) throw new EndOfStreamException(); offset += read; }
        return bytes;
    }
    private static void WriteNew(string path, byte[] bytes)
    {
        CheckAncestors(path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
    }
    private static void CheckTree(string path)
    {
        CheckAncestors(path);
        foreach (string entry in Directory.EnumerateFileSystemEntries(path))
        { CheckAncestors(entry); if (Directory.Exists(entry)) CheckTree(entry); }
    }
    private static void CheckAncestors(string path)
    {
        for (string? next = Path.GetFullPath(path); !string.IsNullOrEmpty(next); next = Path.GetDirectoryName(next))
            if ((File.Exists(next) || Directory.Exists(next)) && (File.GetAttributes(next) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked save paths are not permitted.");
    }
}
