namespace YiGodhome.Bridge;

// Unity key-code to Win32 virtual-key translation, used only for native rebinding.
public static class BindingKeyboard
{
    public static bool Allowed(bool requested,bool controls,bool freshPausedHost,bool foregroundHost)
        => requested && controls && freshPausedHost && foregroundHost;
    public static int VirtualKey(int code)
    {
        if(code>=97 && code<=122)return code-32;
        if(code>=48 && code<=57)return code;
        if(code>=256 && code<=265)return code-256+0x60;
        if(code>=282 && code<=296)return code-282+0x70;
        switch(code) {
            case 8:return 0x08;case 9:return 0x09;case 12:return 0x0c;case 13:return 0x0d;
            case 19:return 0x13;case 27:return 0x1b;case 32:return 0x20;case 127:return 0x2e;
            case 39:return 0xde;case 44:return 0xbc;case 45:return 0xbd;case 46:return 0xbe;
            case 47:return 0xbf;case 59:return 0xba;case 61:return 0xbb;case 91:return 0xdb;
            case 92:return 0xdc;case 93:return 0xdd;case 96:return 0xc0;
            case 266:return 0x6e;case 267:return 0x6f;case 268:return 0x6a;case 269:return 0x6d;
            case 270:return 0x6b;case 271:return 0x0d;case 272:return 0xbb;
            case 273:return 0x26;case 274:return 0x28;case 275:return 0x27;case 276:return 0x25;
            case 277:return 0x2d;case 278:return 0x24;case 279:return 0x23;case 280:return 0x21;case 281:return 0x22;
            case 300:return 0x90;case 301:return 0x14;case 302:return 0x91;
            case 303:return 0xa1;case 304:return 0xa0;case 305:return 0xa3;case 306:return 0xa2;
            case 307:return 0xa5;case 308:return 0xa4;case 309:case 312:return 0x5c;
            case 310:case 311:return 0x5b;case 313:return 0xa5;case 315:return 0x2f;
            case 316:case 317:return 0x2c;case 318:return 0x03;case 319:return 0x5d;
            default:return 0;
        }
    }
}
