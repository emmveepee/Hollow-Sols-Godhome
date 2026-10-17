using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace YiGodhome.Bridge;

// Unity's installed Mono float/double writer allocates a temporary byte array.
// Its integer overload reuses the writer buffer; IEEE bits keep the wire exact.
internal sealed class WireWriter : BinaryWriter
{
    [StructLayout(LayoutKind.Explicit)]
    private struct SingleBits { [FieldOffset(0)] internal float Value; [FieldOffset(0)] internal int Bits; }
    [StructLayout(LayoutKind.Explicit)]
    private struct DoubleBits { [FieldOffset(0)] internal double Value; [FieldOffset(0)] internal long Bits; }
    internal WireWriter(Stream stream) : base(stream, Encoding.UTF8, true) { }
    public override void Write(float value) => base.Write(new SingleBits { Value = value }.Bits);
    public override void Write(double value) => base.Write(new DoubleBits { Value = value }.Bits);
}
