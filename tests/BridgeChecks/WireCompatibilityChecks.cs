// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using YiGodhome.Bridge;

internal static class WireCompatibilityChecks
{
    public static void Run()
    {
        // Independent little-endian v52 fixture: an empty parry-contact count
        // follows melee windows. Do not generate expected bytes
        // with Protocol.Write: a coordinated writer/reader drift must fail here.
        var session = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var detail = Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100");
        byte[] expected = Convert.FromHexString(
            "594748423400020133221100554477668899AABBCCDDEEFF" +
            "0100000000000000020000000000000003000000000000000400000000000000" +
            "0000000000000000E80300000000000007000000" +
            new string('0', 206) + // Absent pose, vitals and encounter, bytes76..178.
            "030000003F0000803E0000CCDDEEFFAABB88997766554433221100" +
            "00000000000000000000000001" +
            "050000000000000002000000000000000300000000000000000000BF0000803F01000000");
        var packet = new Packet {
            Kind = MessageKind.Snapshot, Role = Role.HollowKnight, Session = session,
            Sequence = 1, Frame = 2, FrameTick = 3, SentTick = 4, Frequency = 1000,
            Capabilities = 7, Audio = new HostAudioState(.5f, .25f, true), DetailCapture = detail,
            Inputs = new[] { new InputCommand(5, 2, 3, new Controls(-.5f, 1, Buttons.Jump)) }
        };
        using var encoded = new MemoryStream();
        Protocol.Write(encoded, packet);
        Require(expected.Length == 255 && encoded.ToArray().SequenceEqual(expected), "Gameplay v52 binary compatibility changed");
        using var input = new MemoryStream(expected);
        var decoded = Protocol.Read(input);
        Require(input.Position == expected.Length && decoded.Session == session && decoded.Sequence == 1
            && decoded.Frame == 2 && decoded.FrameTick == 3 && decoded.SentTick == 4 && decoded.Frequency == 1000
            && decoded.Role == Role.HollowKnight && decoded.Capabilities == 7 && decoded.DetailCapture == detail
            && decoded.Audio?.Paused == true && decoded.Audio.Master == .5f && decoded.Audio.Effects == .25f
            && decoded.Inputs.Length == 1 && decoded.Inputs[0].Sequence == 5 && decoded.Inputs[0].Frame == 2
            && decoded.Inputs[0].Tick == 3 && decoded.Inputs[0].Controls.Equals(new Controls(-.5f, 1, Buttons.Jump)),
            "Independent gameplay fixture decoded differently");
        foreach (int offset in new[] { 4, 7, 206 }) {
            var malformed = (byte[])expected.Clone();
            malformed[offset] = 255; // Version, role, or binding length.
            Reject(() => Protocol.Read(new MemoryStream(malformed)));
        }
        var legacy=(byte[])expected.Clone();legacy[4]=51;Reject(()=>Protocol.Read(new MemoryStream(legacy)));

        // Independent v3 acknowledgement: applied hit, native health123, no hold/death.
        byte[] ackBytes = Convert.FromHexString("59474941030033221100554477668899AABBCCDDEEFF0900000000000000017B0000000000");
        var ack = new HitAck { Session = session, Sequence = 9, Disposition = HitDisposition.Applied, Health = 123 };
        using var ackOutput = new MemoryStream(); HitCodec.WriteAck(ackOutput, ack);
        Require(ackOutput.ToArray().SequenceEqual(ackBytes), "Hit acknowledgement v3 binary compatibility changed");
        using var ackInput = new MemoryStream(ackBytes); var received = HitCodec.ReadAck(ackInput);
        Require(ackInput.Position == 37 && received.Session == session && received.Sequence == 9
            && received.Disposition == HitDisposition.Applied && received.Health == 123 && !received.ParryPause && !received.TargetKilled,
            "Independent hit acknowledgement decoded differently");
        Console.WriteLine("PASS: independent gameplay52/input/audio and hit-ack3 binary fixtures; pre-overlay peer rejected");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action read)
    {
        try { read(); } catch (Exception error) when (error is InvalidDataException || error is EndOfStreamException) { return; }
        throw new Exception("Independent malformed gameplay fixture accepted");
    }
}
