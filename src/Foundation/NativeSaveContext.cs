using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HollowSols.Foundation;

// Referenced by the early in-memory guard. Contains no Unity/native game types,
// disk save operations or loader dependencies. Native profile indexes are private
// serializer contexts, never mod-owned slot numbers or vanilla file destinations.
public static class NativeSaveContext
{
    public const string PathNamespace = "HollowSolsSessions/";
    private static readonly object Gate = new object();
    private static SessionToken? session;
    private static GameSide side;
    private static GameMode mode;
    private static bool fresh;
    public static bool FullAccess { get; private set; }
    private static byte[]? hollowKnight;
    private static long contentRevision, snapshotRevision;
    public static long ContentRevision { get { lock (Gate) return contentRevision; } }
    public static long SnapshotRevision { get { lock (Gate) return snapshotRevision; } }
    private static Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public static Action<SessionToken, byte[], Action<bool>>? HollowKnightSaveRequested;
    public static Action<SessionToken>? NineSolsSaveRequested;
    public static bool ReadOnlyVanilla { get; set; } // Explicit bounded native fixture only.
    public static bool Enabled { get { lock (Gate) return session.HasValue; } }
    public static bool ProtectingWrites => Enabled || ReadOnlyVanilla;
    public static SessionToken? Session { get { lock (Gate) return session; } }
    public static Guid AsyncOwner() { lock (Gate) return session?.Session ?? Guid.Empty; }
    public static void ValidateAsyncOwner(Guid owner)
    {
        lock (Gate)
            if (owner != (session?.Session ?? Guid.Empty)) throw new OperationCanceledException("An asynchronous native operation belongs to a different save session.");
    }
    public static GameMode Mode { get { lock (Gate) return mode; } }
    public static bool IsNewSave { get { lock (Gate) return session.HasValue && fresh; } }
    public static int SerializerIndex(GameSide game) => game == GameSide.HollowKnight ? 4 : game == GameSide.NineSols ? 3 : throw new ArgumentException("Invalid game.");

    public static void Enter(GameSide game, SessionToken token, byte[]? nativePayload, GameMode gameMode = GameMode.Godhome, bool fullAccess = false)
    {
        token.Slot.Validate();
        if (token.Session == Guid.Empty || token.Connection == Guid.Empty || token.Pair == Guid.Empty) throw new ArgumentException("Missing paired session identity.");
        lock (Gate)
        {
            if (session.HasValue) throw new InvalidOperationException("Drain the prior native save context before entering another.");
            side = game;
            mode = gameMode;
            fresh = nativePayload == null;
            contentRevision = snapshotRevision = 0;
            FullAccess = fullAccess;
            if (game == GameSide.NineSols) files = nativePayload == null ? new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) : NativeSaveBundle.Decode(nativePayload);
            else if (game == GameSide.HollowKnight) hollowKnight = nativePayload == null ? null : (byte[])nativePayload.Clone();
            else throw new ArgumentException("Invalid native game.");
            directories.Clear(); directories.Add("");
            foreach (string name in files.Keys) AddParents(name);
            session = token;
        }
    }
    public static void Leave(SessionToken expected)
    {
        lock (Gate)
        {
            VerifySession(expected); session = null; hollowKnight = null; FullAccess = false;
            contentRevision = snapshotRevision = 0;
            files.Clear(); directories.Clear();
        }
    }
    public static byte[] Snapshot(SessionToken expected)
    {
        lock (Gate)
        {
            VerifySession(expected);
            snapshotRevision = contentRevision;
            if (side == GameSide.HollowKnight) return hollowKnight == null ? throw new IOException("Native HK has not produced save data.") : (byte[])hollowKnight.Clone();
            return NativeSaveBundle.Encode(files);
        }
    }
    public static string SlotPath(int nativeIndex)
    {
        lock (Gate) { VerifyIndex(nativeIndex); return PathNamespace + session!.Value.Session.ToString("N") + "/native"; }
    }
    public static string RedirectNativeLoad(string path, bool fromResource)
    {
        lock (Gate) {
            if (!session.HasValue || side != GameSide.NineSols) throw new InvalidOperationException("No isolated Nine Sols load context.");
            if (fromResource) {
                if (!fresh) throw new SaveUnavailableException();
                return path;
            }
            string normalized = path.Replace('\\', '/').Trim('/');
            if (normalized == "saveslot3") return SlotPath(3);
            string expected = SlotPath(3);
            if (normalized != expected) throw new InvalidOperationException("Native load path escaped its paired session.");
            return expected;
        }
    }
    public static bool ShouldHandlePath(string? path)
    {
        lock (Gate) return session.HasValue || path != null && path.Replace('\\', '/').TrimStart('/').StartsWith(PathNamespace, StringComparison.Ordinal);
    }
    public static bool ShouldHandleWritePath(string? path) => ReadOnlyVanilla || ShouldHandlePath(path);
    public static void RefuseNativeSlotDeletion()
    { if (Enabled) throw new InvalidOperationException("Delete the confirmed Hollow Sols pair through its HK menu."); }
    public static void VerifyIndex(int index)
    {
        lock (Gate)
            if (!session.HasValue || index != SerializerIndex(side)) throw new InvalidOperationException("A native slot index escaped its isolated serializer context.");
    }
    public static bool HKOccupied(int index)
    { lock (Gate) { VerifyIndex(index); return hollowKnight != null; } }
    public static bool NSOccupied(int index)
    { lock (Gate) { VerifyIndex(index); return files.ContainsKey("flags.sav"); } }
    public static void HKRead(int index, Action<byte[]?>? callback)
    {
        byte[]? bytes;
        lock (Gate) { VerifyIndex(index); bytes = hollowKnight == null ? null : (byte[])hollowKnight.Clone(); }
        callback?.Invoke(bytes);
    }
    public static void HKWrite(int index, byte[] bytes, Action<bool>? callback)
    {
        SessionToken token; Action<SessionToken, byte[], Action<bool>>? handler;
        lock (Gate)
        {
            VerifyIndex(index);
            if (side != GameSide.HollowKnight || bytes == null || bytes.Length < 1 || bytes.Length > PairedSaveStore.MaxPayloadBytes) throw new ArgumentException("Invalid HK payload.");
            hollowKnight = (byte[])bytes.Clone(); token = session!.Value; handler = HollowKnightSaveRequested;
        }
        if (handler == null) throw new InvalidOperationException("Paired persistence coordinator is unavailable.");
        handler(token, (byte[])bytes.Clone(), success => { lock (Gate) { if (!session.HasValue || !session.Value.Equals(token)) return; } callback?.Invoke(success); });
    }
    public static void HKOccupiedCallback(int index, Action<bool>? callback) => callback?.Invoke(HKOccupied(index));
    public static void HKSpace(int index, Action<bool>? callback) { VerifyIndex(index); callback?.Invoke(true); }
    public static void HKClear(int index, Action<bool>? callback) { VerifyIndex(index); callback?.Invoke(false); }

    public static bool FileExists(string path)
    { lock (Gate) return files.ContainsKey(Key(path)); }
    public static byte[] Read(string path)
    {
        lock (Gate)
        {
            if (!files.TryGetValue(Key(path), out var value)) throw new FileNotFoundException("The isolated native file is absent.");
            return (byte[])value.Clone();
        }
    }
    public static void Write(string path, byte[] bytes)
    {
        lock (Gate)
        {
            string key = Key(path);
            if (bytes == null || bytes.Length > PairedSaveStore.MaxPayloadBytes) throw new IOException("Native file exceeds save budget.");
            var snapshot = new Dictionary<string, byte[]>(files, StringComparer.OrdinalIgnoreCase) { [key] = bytes };
            NativeSaveBundle.Encode(snapshot); // Enforce names, file count and total size before mutation.
            files[key] = (byte[])bytes.Clone(); AddParents(key); contentRevision = checked(contentRevision + 1);
        }
        if (path.Replace('\\', '/').EndsWith("/flags.sav", StringComparison.OrdinalIgnoreCase)) NotifyNineSolsSave();
    }
    public static void Rename(string source, string destination)
    {
        lock (Gate)
        {
            string from = Key(source), to = Key(destination);
            if (!files.TryGetValue(from, out var value)) throw new FileNotFoundException("Native rename source is absent.");
            if (files.ContainsKey(to)) throw new IOException("Native rename destination already exists.");
            var snapshot = new Dictionary<string, byte[]>(files, StringComparer.OrdinalIgnoreCase); snapshot.Remove(from); snapshot.Add(to, value);
            NativeSaveBundle.Encode(snapshot); files = snapshot; AddParents(to); contentRevision = checked(contentRevision + 1);
        }
        if (destination.Replace('\\', '/').EndsWith("/flags.sav", StringComparison.OrdinalIgnoreCase)) NotifyNineSolsSave();
    }
    public static void DeleteFile(string path) { lock (Gate) { if (files.Remove(Key(path))) contentRevision = checked(contentRevision + 1); } }
    public static bool DirectoryExists(string path) { lock (Gate) return directories.Contains(Key(path, true)); }
    public static void CreateDirectory(string path)
    { lock (Gate) { string key = Key(path, true); if (key.Length > 0) ValidateDirectory(key); directories.Add(key); AddParents(key + "/placeholder"); } }
    public static string[] GetDirectories(string path)
    {
        lock (Gate)
        {
            string clean = path.Trim('/');
            if (clean.Length == 0) return new[] { SlotPath(SerializerIndex(side)) };
            string key = Key(path, true), prefix = key.Length == 0 ? "" : key + "/";
            return directories.Where(d => d.Length > prefix.Length && d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && d.IndexOf('/', prefix.Length) < 0).Select(d => SlotPath(SerializerIndex(side)) + "/" + d).ToArray();
        }
    }
    public static void DeleteDirectory(string path)
    {
        lock (Gate)
        {
            string key = Key(path, true);
            if (key.Length == 0) throw new IOException("A native operation cannot delete the paired save root.");
            var removed = files.Keys.Where(n => n.StartsWith(key + "/", StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (string name in removed) files.Remove(name);
            if (removed.Length > 0) contentRevision = checked(contentRevision + 1);
            directories.RemoveWhere(n => n == key || n.StartsWith(key + "/", StringComparison.OrdinalIgnoreCase));
        }
    }
    public static void NotifyNineSolsSave()
    {
        SessionToken token;
        lock (Gate) { if (!session.HasValue || side != GameSide.NineSols) return; token = session.Value; }
        NineSolsSaveRequested?.Invoke(token);
    }
    private static string Key(string path, bool directory = false)
    {
        if (!session.HasValue || side != GameSide.NineSols) throw new IOException("The native save session has ended.");
        string prefix = SlotPath(SerializerIndex(side)); string clean = path.Replace('\\', '/').Trim('/');
        if (directory && clean == prefix) return "";
        if (!clean.StartsWith(prefix + "/", StringComparison.Ordinal)) throw new IOException("Native save operation targeted a vanilla or stale path.");
        string key = clean.Substring(prefix.Length + 1);
        if (directory) ValidateDirectory(key); else NativeSaveBundle.Encode(new Dictionary<string, byte[]> { [key] = Array.Empty<byte>() });
        return key;
    }
    private static void ValidateDirectory(string key) => NativeSaveBundle.Encode(new Dictionary<string, byte[]> { [key + "/placeholder"] = Array.Empty<byte>() });
    private static void AddParents(string key)
    { int end = key.LastIndexOf('/'); while (end > 0) { directories.Add(key.Substring(0, end)); end = key.LastIndexOf('/', end - 1); } }
    private static void VerifySession(SessionToken expected)
    { if (!session.HasValue || !session.Value.Equals(expected)) throw new InvalidOperationException("Stale native save session."); }
}
