// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using HollowSols.Foundation;

internal static class ControlWireCompatibilityChecks
{
    public static void Run()
    {
        var references = typeof(BridgeLifecycle).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();
        Require(!references.Any(name => name.StartsWith("Unity", StringComparison.Ordinal)
            || name == "Assembly-CSharp" || name.StartsWith("BepInEx", StringComparison.Ordinal)
            || name == "0Harmony" || name.StartsWith("Mono.Cecil", StringComparison.Ordinal)),
            "Foundation acquired an engine, game or loader assembly dependency");
        // Literal v6 Load/creation frame, independent of the production encoder.
        // Mod slot4 here is synthetic metadata, never a native save destination.
        byte[] expected = Convert.FromHexString(
            "4853424306000601070000000000000033221100554477668899AABBCCDDEEFF" +
            new string('0', 96) + // No connection-offer payload.
            "1111111122223333445566778899AABB433221106554877698A9BACBDCEDFE0F" +
            "CCDDEEFFAABB88997766554433221100040900000000000000" +
            new string('0', 48) + "00010000000000");
        var hello = new BridgeHello(GameSide.HollowKnight, Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));
        var token = new SessionToken(Guid.Parse("11111111-2222-3333-4455-66778899aabb"),
            Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f"), Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100"), new ModSlot(4), 9);
        var message = new ControlMessage(ControlKind.Load, hello, 7, session: token, creating: true);
        using var output = new MemoryStream(); ControlProtocol.Write(output, message);
        Require(expected.Length == 168 && output.ToArray().SequenceEqual(expected), "Control v6 binary compatibility changed");
        using var input = new MemoryStream(expected); var read = ControlProtocol.Read(input);
        Require(input.Position == 168 && read.Kind == ControlKind.Load && read.Side == GameSide.HollowKnight
            && read.Sequence == 7 && read.Hello.Activation == hello.Activation && read.Session!.Value.Equals(token)
            && read.Creating && !read.Recovered && read.Transaction == Guid.Empty && read.Generation == 0,
            "Independent control creation fixture decoded differently");
        foreach (int offset in new[] { 4, 7, 161, 162, 163 }) {
            var invalid = (byte[])expected.Clone(); invalid[offset] = offset == 7 ? (byte)2 : (byte)255;
            Reject(() => ControlProtocol.Read(new MemoryStream(invalid)));
        }
        Console.WriteLine("PASS: independent control6 creation/session/slot fixture and version/authority/flag/extension rejection");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action read)
    {
        try { read(); } catch (InvalidDataException) { return; }
        throw new Exception("Independent malformed control fixture accepted");
    }
}
