using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;
using TeamCherry.SharedUtils;
using UnityEngine;

namespace YiGodhome;

internal static class SlotFourPreparation
{
    [Serializable]
    private sealed class Plan
    {
        public int profileId = 4;
        public string session = "";
        public string sourceSha256 = "";
        public bool sourceAbsent;
        public bool preserve;
        public string candidateSha256 = "";
        public string candidateFile = "godseeker-user4.dat";
        public string nativeSaveRoot = "";
    }

    public static string? ReadSourceHash()
    {
        string root = Application.persistentDataPath;
        for (var directory = new DirectoryInfo(root); directory != null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Redirected save root.");
        string path = Path.Combine(root, "user4.dat");
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Redirected slot 4.");
            return Hash(File.ReadAllBytes(path));
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public static void Stage(SaveGameData candidate, string? originalHash, bool preserve)
    {
        if (ReadSourceHash() != originalHash) throw new IOException("Slot 4 changed during assessment.");
        var plan = new Plan { session = Environment.GetEnvironmentVariable("YIGODHOME_SESSION")!,
            sourceAbsent = originalHash == null, sourceSha256 = originalHash ?? "", preserve = preserve,
            nativeSaveRoot = Application.persistentDataPath };
        var directory = Path.GetDirectoryName(Environment.GetEnvironmentVariable("YIGODHOME_BOOT_LOG"))!;
        if (!preserve)
        {
            byte[] bytes = Encode(candidate);
            plan.candidateSha256 = Hash(bytes);
            File.WriteAllBytes(Path.Combine(directory, plan.candidateFile), bytes);
        }
        File.WriteAllText(Path.Combine(directory, "slot4-plan.json"), JsonUtility.ToJson(plan, true));
    }

    private static byte[] Encode(SaveGameData save)
    {
        string json = JsonUtility.ToJson(save);
        var config = typeof(GameManager).GetField("gameConfig", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(GameManager.instance)!;
        bool encrypted = (bool)config.GetType().GetField("useSaveEncryption")!.GetValue(config) && !Platform.Current.IsFileSystemProtected;
        if (!encrypted) return Encoding.UTF8.GetBytes(json);
        string cipher = Encryption.Encrypt(json);
        if (Encryption.Decrypt(cipher) != json) throw new InvalidOperationException("Native encryption round trip failed.");
        using var stream = new MemoryStream();
        new BinaryFormatter().Serialize(stream, cipher);
        return stream.ToArray();
    }

    private static string Hash(byte[] bytes)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
    }
}
