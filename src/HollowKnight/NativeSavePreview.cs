using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using UnityEngine;

namespace YiGodhome;

// Metadata only: no profile selection, singleton replacement, save hooks or
// native filesystem reads. Payload comes from a validated paired checkpoint.
internal static class NativeSavePreview
{
    internal static SaveStats Read(byte[] payload)
    {
        string json;
        if (GameManager.instance.gameConfig.useSaveEncryption && !Platform.Current.IsFileSystemProtected)
        {
            using var stream = new MemoryStream(payload, false);
            var formatter = new BinaryFormatter { Binder = new StringOnlyBinder() };
            if (formatter.Deserialize(stream) is not string encrypted || stream.Position != stream.Length) throw new InvalidDataException("Invalid native save metadata.");
            json = TeamCherry.SharedUtils.Encryption.Decrypt(encrypted);
        }
        else json = Encoding.UTF8.GetString(payload);
        var data = JsonUtility.FromJson<SaveGameData>(json)?.playerData ?? throw new InvalidDataException("Native save metadata missing.");
        return new SaveStats(data.maxHealthBase, data.geo, data.mapZone, data.playTime, data.MPReserveMax,
            data.permadeathMode, data.bossRushMode, data.completionPercentage, data.unlockedCompletionRate);
    }
    private sealed class StringOnlyBinder : SerializationBinder
    {
        public override Type BindToType(string assemblyName, string typeName) => typeName == typeof(string).FullName
            ? typeof(string) : throw new SerializationException("Only the native string container is permitted.");
    }
}
