using System;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using InControl;
using UnityEngine;
using YiGodhome.Bridge;

namespace YiGodhome;

// Serialize only the supported gameplay actions using the installed native
// InControl format. Native Controls owns rebinding and settings persistence.
internal sealed class NativeInputBindings
{
    private readonly Endpoint endpoint;
    private readonly Action<string> log;
    private byte[] last = Array.Empty<byte>();
    private float next;
    private static readonly System.Reflection.MethodInfo SaveAction = AccessTools.Method(typeof(PlayerAction), "Save", new[] { typeof(BinaryWriter) });
    internal NativeInputBindings(Endpoint endpoint, Action<string> log) { this.endpoint = endpoint; this.log = log; }
    internal void Update()
    {
        if (Time.realtimeSinceStartup < next) return;
        next = Time.realtimeSinceStartup + .25f;
        var actions = ApplicationCore.Instance.playerInput.gameplayActions;
        var selected = new[] { actions.MoveLeft, actions.MoveRight, actions.MoveUp, actions.MoveDown,
            actions.Jump, actions.Attack, actions.Parry, actions.Dodge, actions.Heal, actions.WeaponAttack, actions.FooAttack, actions.Map,
            actions.NextItem, actions.PreviousItem, actions.Hack };
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true)) {
            writer.Write(0x444E4942u); writer.Write((ushort)2); writer.Write(selected.Length);
            foreach (var action in selected) SaveAction.Invoke(action, new object[] { writer });
        }
        var snapshot = bytes.ToArray();
        if (last.SequenceEqual(snapshot)) return;
        endpoint.PublishNativeBindings(snapshot); last = snapshot;
        log("NS_NATIVE_YI_BINDINGS_PUBLISHED actions=" + selected.Length + " bytes=" + snapshot.Length + " settingsUnchanged=True");
    }
}
