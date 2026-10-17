namespace YiGodhome.Bridge;

public static class ActorPixels
{
    private static readonly byte[] coverage64=Coverage(64),coverage255=Coverage(255);
    private static readonly byte[] straight=Resolve(false),dark64=Resolve(true);
    internal static void Prepare()=>System.GC.KeepAlive(straight);
    private static byte[] Coverage(int background) {
        var table=new byte[511];
        for(int delta=-255;delta<=255;delta++)table[delta+255]=(byte)(System.Math.Max(0,System.Math.Min(background,delta))*255/background);
        return table;
    }
    private static byte[] Resolve(bool dark) {
        var table=new byte[65536];
        for(int alpha=1;alpha<256;alpha++)for(int value=0;value<256;value++)
            table[(alpha<<8)|value]=(byte)System.Math.Max(0,System.Math.Min(255,((dark?value*255-64*(255-alpha):value*255)+alpha/2)/alpha));
        return table;
    }
    // Some native particle materials write RGB only. Two unchanged native
    // renders recover their coverage without replacing the shader or artwork.
    // black is overwritten with straight RGBA; inputs may not alias each other.
    public static void FromDualMatte(byte[] black, byte[] white, bool preserveBlack = false, int matteBackground = 255)
    {
        if (black.Length != white.Length || black.Length % 4 != 0 || ReferenceEquals(black, white))
            throw new System.ArgumentException("Distinct equal RGBA matte buffers required.");
        if(matteBackground<1 || matteBackground>255) throw new System.ArgumentOutOfRangeException(nameof(matteBackground));
        var coverage=matteBackground==64?coverage64:matteBackground==255?coverage255:null;
        for (int i = 0; i < black.Length; i += 4) {
            if(black[i]==0 && black[i+1]==0 && black[i+2]==0
                && white[i]==matteBackground && white[i+1]==matteBackground && white[i+2]==matteBackground) { black[i+3]=0; continue; }
            if(black[i]==white[i] && black[i+1]==white[i+1] && black[i+2]==white[i+2]) { black[i+3]=255; continue; }
            int r=white[i]-black[i],g=white[i+1]-black[i+1],b=white[i+2]-black[i+2];
            int tr=coverage!=null?coverage[r+255]:System.Math.Max(0,System.Math.Min(matteBackground,r))*255/matteBackground;
            int tg=coverage!=null?coverage[g+255]:System.Math.Max(0,System.Math.Min(matteBackground,g))*255/matteBackground;
            int tb=coverage!=null?coverage[b+255]:System.Math.Max(0,System.Math.Min(matteBackground,b))*255/matteBackground;
            int transparency=tr+tg+tb;
            int alpha = 255 - (transparency + 1) / 3;
            // A camera color grade can attenuate each background channel
            // differently. HUD overlays must retain the strongest native
            // coverage so their rendered RGB is not clipped during resolve.
            if(preserveBlack) {
                alpha=System.Math.Max(alpha,System.Math.Max(black[i],255-tr));
                alpha=System.Math.Max(alpha,System.Math.Max(black[i+1],255-tg));
                alpha=System.Math.Max(alpha,System.Math.Max(black[i+2],255-tb));
            }
            // White clips the native hurt LUT before its dark-scene attenuation
            // can be measured. A dark neutral reference preserves that coverage;
            // retain its native output at the reference tone when channels differ.
            if(!preserveBlack || matteBackground==255) {
                int key=alpha<<8;black[i]=straight[key|black[i]];black[i+1]=straight[key|black[i+1]];black[i+2]=straight[key|black[i+2]];
            } else if(matteBackground==64) {
                int key=alpha<<8;black[i]=dark64[key|white[i]];black[i+1]=dark64[key|white[i+1]];black[i+2]=dark64[key|white[i+2]];
            } else for (int c = 0; c < 3; c++) black[i+c] = alpha == 0 ? (byte)0
                : (byte)System.Math.Max(0,System.Math.Min(255, ((preserveBlack && matteBackground<255
                    ? white[i+c]*255-matteBackground*(255-alpha) : black[i+c]*255) + alpha / 2) / alpha));
            black[i+3] = (byte)alpha;
        }
    }
    // Rendering onto a transparent target produces premultiplied RGB. The HK
    // sprite shader premultiplies its input, so undo that once at the wire edge.
    public static void StraightAlpha(byte[] rgba)
    {
        for (int i = 0; i < rgba.Length; i += 4) {
            int alpha = rgba[i + 3];
            if (alpha == 0 || alpha == 255) continue;
            int key=alpha<<8;rgba[i]=straight[key|rgba[i]];rgba[i+1]=straight[key|rgba[i+1]];rgba[i+2]=straight[key|rgba[i+2]];
        }
    }
    // A matched/uploaded image can still be transparent during native revival
    // or blink. Require actual opaque artwork before exposing a returned room.
    public static bool Visible(byte[] rgba)
    {
        int pixels = 0;
        for (int i = 3; i < rgba.Length; i += 4)
            if (rgba[i] >= 128 && ++pixels >= 32) return true;
        return false;
    }
}
