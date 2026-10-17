using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HollowSols.Foundation;

// NS's save is a set of native flag/meta/backup files, rather than one HK blob.
// This container preserves those bytes without interpreting native progression.
// It does not extract paths to disk; a scoped native filesystem adapter is required.
public static class NativeSaveBundle
{
    public const int MaxFiles = 128;
    private const uint Magic = 0x42534E48;
    private const ushort Version = 1;
    public static byte[] Encode(IReadOnlyDictionary<string, byte[]> files)
    {
        if (files == null || files.Count < 1 || files.Count > MaxFiles) throw new ArgumentException("Invalid native file count.");
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes, Encoding.UTF8, true);
        writer.Write(Magic); writer.Write(Version); writer.Write((ushort)files.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            ValidateName(entry.Key);
            if (!names.Add(entry.Key) || entry.Value == null || entry.Value.Length > PairedSaveStore.MaxPayloadBytes) throw new ArgumentException("Invalid or aliased native file.");
            if (bytes.Length + entry.Key.Length + entry.Value.Length + 6 > PairedSaveStore.MaxPayloadBytes) throw new ArgumentException("Native bundle exceeds checkpoint budget.");
            byte[] name = Encoding.ASCII.GetBytes(entry.Key);
            writer.Write((ushort)name.Length); writer.Write(name); writer.Write(entry.Value.Length); writer.Write(entry.Value);
        }
        writer.Flush(); return bytes.ToArray();
    }
    public static Dictionary<string, byte[]> Decode(byte[] payload)
    {
        if (payload == null || payload.Length > PairedSaveStore.MaxPayloadBytes) throw new ArgumentException("Invalid native bundle size.");
        using var reader = new BinaryReader(new MemoryStream(payload), Encoding.UTF8, false);
        if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != Version) throw new InvalidDataException("Unknown native bundle schema.");
        int count = reader.ReadUInt16(); if (count < 1 || count > MaxFiles) throw new InvalidDataException("Invalid native file count.");
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            int nameSize = reader.ReadUInt16(); if (nameSize < 1 || nameSize > 256) throw new InvalidDataException("Invalid native name size.");
            var encoded = reader.ReadBytes(nameSize);
            if (encoded.Length != nameSize || encoded.Any(b => b > 127)) throw new InvalidDataException("Invalid native filename encoding.");
            string name = Encoding.ASCII.GetString(encoded); ValidateName(name);
            int length = reader.ReadInt32();
            if (files.ContainsKey(name) || length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("Invalid native file payload.");
            files.Add(name, reader.ReadBytes(length));
        }
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing native bundle bytes.");
        return files;
    }
    private static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 256 || name.Any(c => !(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z'
            || c >= '0' && c <= '9' || c == '_' || c == '-' || c == '.' || c == '/'))) throw new ArgumentException("Unsafe native bundle name.");
        var parts = name.Split('/'); if (parts.Length > 8) throw new ArgumentException("Native bundle path is too deep.");
        foreach (string part in parts)
        {
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal)
                || stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" || stem == "CONIN$" || stem == "CONOUT$"
                || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] >= '0' && stem[3] <= '9')
                throw new ArgumentException("Unsafe native bundle path segment.");
        }
    }
}
