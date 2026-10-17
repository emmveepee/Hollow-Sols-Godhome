using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.IO.Pipes;
using System.Threading;
using YiGodhome.Bridge;

internal static class Program
{
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static bool Until(Func<bool> condition, int milliseconds = 5000) => SpinWait.SpinUntil(condition, milliseconds);
    private static int Main()
    {
        try { MenuTabs(); InputRearm(); UnconnectedShutdown(); NativeBindings(); DetailedCodec(); DetailedExchange(); Codec(); AudioCodec(); AudioExchange(); PoseCodec(); MeleeCodec(); VitalsCodec(); EncounterCodec(); InputCodec(); Exchange(); PoseExchange(); InputExchange(); EntryRenewal(); MenuRenewal(); InputBeforeFrame(); InvalidInputPeers(); WrongSession(); SeparateClocks(); BadSequence(); FrozenGameplay(); Timeout(); Console.WriteLine("PASS: all bridge checks"); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static void MenuTabs()
    {
        BindingCapture();
        MenuInteractions();
        foreach (var button in new[] { Buttons.Parry, Buttons.Talisman, Buttons.ChildPrevious, Buttons.ChildNext })
        {
            var gate = new MenuTabPress(); int presses = 0;
            for (int tick = 0; tick < 200; tick++)
            {
                var edge = gate.Advance(button | Buttons.Jump | Buttons.Bow | Buttons.HostPaused);
                // Native layout hides/disables input after changing pages,
                // then re-enables while the same forwarded command is held.
                bool interactive = tick == 0 || tick % 13 > 5;
                if (interactive && (edge & button) != 0) presses++;
                Assert((edge & ~MenuTabPress.Tabs) == 0,"Tab pulse consumed unrelated gameplay/UI metadata");
            }
            Assert(presses == 1,"One held tab command became another press after page reveal");
            gate.Advance(Buttons.None);
            Assert(gate.Advance(button) == button,"Release and fresh tab press failed to rearm");
        }
        var independent = new MenuTabPress();
        Assert(independent.Advance(Buttons.Parry) == Buttons.Parry,"First previous-tab press lost");
        Assert(independent.Advance(Buttons.Parry | Buttons.Talisman) == Buttons.Talisman,"Held LB suppressed fresh RB or repeated LB");
        Assert(independent.Advance(Buttons.Talisman) == Buttons.None,"Releasing LB replayed held RB");
        Assert(independent.Advance(Buttons.Parry | Buttons.Talisman) == Buttons.Parry,"LB did not independently rearm");
        Console.WriteLine("PASS: inventory tabs emit once across200 native ticks/page-interactivity gaps; independent release/repress and unrelated inputs remain intact.");
    }
    private static void BindingCapture()
    {
        MouseCapture();
        for(int flags=0;flags<16;flags++)Assert(BindingKeyboard.Allowed((flags&1)!=0,(flags&2)!=0,(flags&4)!=0,(flags&8)!=0)==(flags==15),
            "Native keyboard read escaped active Controls/fresh paused host/foreground HK scope");
        for(int code=97;code<=122;code++)Assert(BindingKeyboard.VirtualKey(code)==code-32,"Native letter identity changed");
        for(int code=48;code<=57;code++)Assert(BindingKeyboard.VirtualKey(code)==code,"Native digit identity changed");
        for(int code=282;code<=296;code++)Assert(BindingKeyboard.VirtualKey(code)==code-282+0x70,"Native function key identity changed");
        foreach(var pair in new[]{(304,0xa0),(303,0xa1),(306,0xa2),(305,0xa3),(308,0xa4),(307,0xa5),
            (273,0x26),(274,0x28),(275,0x27),(276,0x25),(27,0x1b),(32,0x20),(13,0x0d),(127,0x2e),(59,0xba),(61,0xbb)})
            Assert(BindingKeyboard.VirtualKey(pair.Item1)==pair.Item2,"Native modifier/navigation/punctuation mapping changed");
        Assert(BindingKeyboard.VirtualKey(0)==0 && BindingKeyboard.VirtualKey(-1)==0 && BindingKeyboard.VirtualKey(9999)==0,"Unsupported code became an arbitrary physical key");
        Console.WriteLine("PASS: host-only binding capture rejects all15 incomplete authority/focus scopes; native letters/digits/function/modifier/navigation/punctuation key mapping retained.");
    }
    private static void MouseCapture()
    {
        var id=Guid.NewGuid();var packet=Message(id,Role.HollowKnight,MessageKind.Snapshot,1);packet.Capabilities=7;
        for(int mask=0;mask<128;mask++)foreach(int wheel in new[]{-1,0,1}) {
            var input=BindingMouse.Sample(mask,wheel);
            packet.Inputs=new[]{new InputCommand(1,packet.Frame,packet.SentTick,input)};
            using var stream=new MemoryStream();Protocol.Write(stream,packet);stream.Position=0;
            var decoded=Protocol.Read(stream).Inputs[0].Controls;
            Assert(BindingMouse.Mask(decoded)==mask && decoded.Y==wheel,"Physical mouse mask/wheel changed in wire roundtrip");
            for(int button=0;button<7;button++)Assert(BindingMouse.Held(mask,button)==((mask&(1<<button))!=0),"Independent mouse button hold changed");
        }
        foreach(var input in new[]{new Controls(.123f,0,Buttons.BindingMouse|Buttons.Keyboard),new Controls(0,.5f,Buttons.BindingMouse|Buttons.Keyboard),
            new Controls(0,0,Buttons.BindingMouse),new Controls(0,0,Buttons.BindingMouse|Buttons.Keyboard|Buttons.Attack),new Controls(0,0,(Buttons)67108864)}) {
            packet.Inputs=new[]{new InputCommand(1,packet.Frame,packet.SentTick,input)};
            bool refused=false;try{Protocol.Write(new MemoryStream(),packet);}catch(InvalidDataException){refused=true;}
            Assert(refused,"Invalid/mixed physical mouse capture input accepted");
        }
        using var hk=new Endpoint(id,Role.HollowKnight,7);using var ns=new Endpoint(id,Role.NineSols,7);
        hk.PublishFrame(1);ns.PublishFrame(1);Assert(Until(()=>hk.IsPeerFresh && ns.IsPeerFresh),"Mouse capture peers did not connect");
        Controls held=default;long sequence=0;
        foreach(var input in new[]{BindingMouse.Sample(1,0),BindingMouse.Sample(0,0),BindingMouse.Sample(18,0),BindingMouse.Sample(0,1),BindingMouse.Sample(0,0),BindingMouse.Sample(0,-1),BindingMouse.Sample(0,0)}) {
            hk.PublishControls(1,input);sequence++;
            Assert(Until(()=>{held=ns.ConsumeControls(held);return ns.ConsumedInputSequence==sequence && held.Equals(input);}),"Native mouse press/release/wheel ordering lost");
        }
        hk.PublishControls(1,BindingMouse.Sample(64,0));sequence++;
        Thread.Sleep(350);hk.PublishFrame(2);ns.PublishFrame(2);
        Assert(Until(()=>hk.IsPeerFresh && ns.IsPeerFresh),"Mouse capture freshness did not recover");
        Assert(Until(()=>{held=ns.ConsumeControls(held);return ns.ConsumedInputSequence==sequence;}) && held.Equals(default),"Expired mouse binding input was replayed");
        hk.PublishControls(2,BindingMouse.Sample(64,0));sequence++;
        Assert(Until(()=>{held=ns.ConsumeControls(held);return ns.ConsumedInputSequence==sequence && BindingMouse.Mask(held)==64;}),"Identical live mouse state did not recover after expiry");
        hk.PublishControls(2,BindingMouse.Sample(64,0));
        Thread.Sleep(20);ns.ConsumeControls(held);
        Assert(ns.ConsumedInputSequence==sequence,"Mouse capture renewal exceeded10Hz budget");
        Console.WriteLine("PASS: all128 mouse button masks ×3 wheel directions roundtrip; exclusive authority and real-pipe opening release/button/wheel/expired-input ordering.");
    }
    private static void MenuInteractions()
    {
        // Different host/peer aspect ratios: only the painted image accepts clicks.
        Assert(MenuPointer.Map(960,540,240,0,1440,1080,out float u,out float v) && u==.5f && v==.5f,"Pillarboxed Controls center mapped incorrectly");
        Assert(!MenuPointer.Map(239,540,240,0,1440,1080,out _,out _),"Letterbox margin accepted a menu click");
        Assert(MenuPointer.Map(0,180,0,180,1920,720,out u,out v) && u==0 && v==0,"Bottom-left origin changed");
        Assert(MenuPointer.Map(1920,900,0,180,1920,720,out u,out v) && u==1 && v==1,"Top-right origin changed");
        Assert(!MenuPointer.Map(float.NaN,0,0,0,1920,1080,out _,out _) && !MenuPointer.Map(0,0,0,0,0,1080,out _,out _),"Invalid pointer geometry accepted");
        // Captured native rectangles may be rotated/mirrored by their canvas.
        var diamond=new[]{.5f,.2f,.2f,.5f,.5f,.8f,.8f,.5f};
        Assert(MenuPointer.Contains(diamond,.5f,.5f) && MenuPointer.Contains(diamond,.5f,.2f),"Native projected button center/edge missed");
        Assert(!MenuPointer.Contains(diamond,.21f,.21f),"Bounding rectangle accepted a point outside the native quad");
        var mirrored=new[]{.8f,.5f,.5f,.8f,.2f,.5f,.5f,.2f};
        Assert(MenuPointer.Contains(mirrored,.5f,.5f),"Mirrored native canvas failed hit testing");
        Assert(!MenuPointer.Contains(new float[8],0,0) && !MenuPointer.Contains(diamond,float.NaN,0),"Collapsed/invalid native bounds accepted");
        var clip=new[]{.4f,.3f,.4f,.7f,.7f,.7f,.7f,.3f};
        Assert(MenuPointer.Contains(diamond,.3f,.5f) && !MenuPointer.Contains(clip,.3f,.5f),"Authored mask did not exclude a clipped button edge");
        var escape=new InventoryEscapeGate();escape.Observe(true,true);
        Assert(escape.Intercept(true,true) && escape.CloseRequested,"First Esc failed to request inventory close");
        escape.Observe(false,true);
        Assert(!escape.CloseRequested && escape.Intercept(false,true),"Inventory-closing Esc also opened pause before release");
        escape.Observe(false,false);
        Assert(!escape.Intercept(false,true),"Second independent Esc was prevented from opening pause");
        Assert(!escape.Intercept(true,false),"Controller pause was consumed as physical Esc");
        var press=new MenuCommandPress();
        for(int n=0;n<200;n++)Assert(press.Advance(Buttons.MenuPointer|Buttons.Keyboard)==(n==0?Buttons.MenuPointer:Buttons.None),"Held click replayed between native ticks");
        press.Advance(Buttons.None);Assert(press.Advance(Buttons.MenuPointer)==Buttons.MenuPointer,"Fresh click failed to rearm");
        var id=Guid.NewGuid();var p=Message(id,Role.HollowKnight,MessageKind.Snapshot,1);p.Capabilities=7;
        var click=new Controls(.25f,.75f,Buttons.MenuPointer|Buttons.Keyboard|Buttons.HostPaused);
        var hover=new Controls(.3f,.6f,Buttons.MenuHover|Buttons.Keyboard|Buttons.HostPaused);
        Assert(press.Advance(Buttons.MenuHover)==Buttons.None,"Hover submitted a native button");
        foreach(var input in new[]{click,hover,new Controls(0,0,Buttons.CloseInventory|Buttons.Keyboard)}) {
            p.Inputs=new[]{new InputCommand(1,p.Frame,p.SentTick,input)};
            using var stream=new MemoryStream();Protocol.Write(stream,p);stream.Position=0;
            Assert(Protocol.Read(stream).Inputs[0].Controls.Equals(input),"Menu-only input changed in codec");
        }
        foreach(var input in new[]{new Controls(.5f,.5f,Buttons.MenuPointer),new Controls(-.1f,.5f,Buttons.MenuPointer|Buttons.Keyboard),
            new Controls(.5f,.5f,Buttons.MenuPointer|Buttons.Keyboard|Buttons.Attack),new Controls(.1f,0,Buttons.CloseInventory),
            new Controls(0,0,Buttons.CloseInventory|Buttons.Inventory),new Controls(.3f,.6f,Buttons.MenuHover|Buttons.Keyboard|Buttons.Attack),
            new Controls(.3f,.6f,Buttons.MenuHover|Buttons.MenuPointer|Buttons.Keyboard),new Controls(0,0,(Buttons)33554432)}) {
            p.Inputs=new[]{new InputCommand(1,p.Frame,p.SentTick,input)};
            bool refused=false;try{Protocol.Write(new MemoryStream(),p);}catch(InvalidDataException){refused=true;}
            Assert(refused,"Mixed gameplay/pointer or malformed menu input accepted");
        }
        using var hk=new Endpoint(id,Role.HollowKnight,7);using var ns=new Endpoint(id,Role.NineSols,7);
        hk.PublishFrame(1);ns.PublishFrame(1);Assert(Until(()=>hk.IsPeerFresh && ns.IsPeerFresh),"Pointer peers did not connect");
        Controls held=default;hk.PublishControls(1,click);
        Assert(Until(()=>{held=ns.ConsumeControls(held);return held.Equals(click);}),"Ordered pointer click not delivered");
        hk.PublishControls(1,default);
        Assert(Until(()=>{held=ns.ConsumeControls(held);return ns.ConsumedInputSequence==2 && held.Equals(default);}),"Pointer release missing");
        hk.PublishControls(1,click);Thread.Sleep(350);hk.PublishFrame(2);ns.PublishFrame(2);
        Assert(Until(()=>hk.IsPeerFresh && ns.IsPeerFresh),"Pointer freshness did not recover");
        Assert(Until(()=>{held=ns.ConsumeControls(held);return ns.ConsumedInputSequence==3;} ) && held.Equals(default),"Expired click was replayed");
        hk.PublishControls(2,hover);
        Assert(Until(()=>{held=ns.ConsumeControls(held);return held.Equals(hover);}) && press.Advance(held.Buttons)==Buttons.None,"Hover transport submitted a native button");
        Console.WriteLine("PASS: projected/mirrored native button quads/clipping, pointer/hover mapping and exclusivity, hover without submit,200-tick click/expiry fences and verified Esc release/second-press behavior.");
    }
    private static void InputRearm()
    {
        var gate = new InputRearmGate();
        var held = new Controls(1, .5f, Buttons.Dodge | Buttons.Bow);
        gate.Observe(held.Buttons, true);
        Assert(gate.Filter(held).Buttons == held.Buttons, "Initial native press was suppressed");
        for (int n = 0; n < 200; n++)
        {
            gate.Observe(held.Buttons, false); gate.Observe(held.Buttons, true);
            var filtered = gate.Filter(new Controls(held.X, held.Y, held.Buttons | Buttons.Attack | Buttons.Interact));
            Assert((filtered.Buttons & held.Buttons) == 0, "Image/readiness gap manufactured another press");
            Assert(filtered.X == held.X && filtered.Y == held.Y && filtered.Buttons == (Buttons.Attack | Buttons.Interact), "Rearm blocked movement, a new button or native UI");
        }
        gate.Observe(Buttons.Bow, true);
        gate.Observe(held.Buttons, true);
        Assert(gate.Filter(held).Buttons == Buttons.Dodge, "Independent dodge release did not rearm or rearmed held bow");
        gate.Observe(Buttons.None, false); gate.Observe(held.Buttons, true);
        Assert(gate.Filter(held).Buttons == held.Buttons, "Physical release while blocked failed to rearm");
        Console.WriteLine("PASS: 200 readiness gaps never replay held dash/bow; real independent releases rearm, movement/new buttons/UI remain available.");
    }

    private static void UnconnectedShutdown()
    {
        foreach (var role in new[] { Role.HollowKnight, Role.NineSols }) {
            IDisposable[] channels = { new Endpoint(Guid.NewGuid(), role), new ContactEndpoint(Guid.NewGuid(), role),
                new HitEndpoint(Guid.NewGuid(), role), new VisualEndpoint(Guid.NewGuid(), role), new VisualEndpoint(Guid.NewGuid(), role, arrows: true) };
            Thread.Sleep(100); // Give workers time to enter native connect/accept.
            foreach (var channel in channels)
                Assert(System.Threading.Tasks.Task.Run(() => channel.Dispose()).Wait(1000), "Preconnection disposal blocked: " + channel.GetType().Name + " " + role);
        }
        Console.WriteLine("PASS: all channel types including independent arrows dispose within one second before either role has a peer");
    }

    private static Packet Message(Guid session, Role role, MessageKind kind = MessageKind.Hello, long sequence = 0)
    {
        long now = Stopwatch.GetTimestamp();
        return new Packet { Session = session, Role = role, Kind = kind, Sequence = sequence, Frame = 123, FrameTick = now, SentTick = now };
    }
    private static void NativeBindings()
    {
        var session = Guid.NewGuid();
        var packet = Message(session, Role.NineSols, MessageKind.Snapshot, 1); packet.Capabilities = 7;
        packet.NativeBindings = new byte[] { 66, 73, 78, 68, 2, 0, 11, 0, 0, 0 };
        using var wire = new MemoryStream(); Protocol.Write(wire, packet);
        var bytes = wire.ToArray(); wire.Position = 0;
        Assert(Protocol.Read(wire).NativeBindings.SequenceEqual(packet.NativeBindings), "Native bindings lost on wire");
        for (int length = 0; length < bytes.Length; length++) {
            bool rejected = false; try { Protocol.Read(new MemoryStream(bytes.Take(length).ToArray())); } catch (IOException) { rejected = true; }
            Assert(rejected, "Truncated binding packet accepted");
        }
        foreach (var role in new[] { Role.HollowKnight, Role.NineSols }) {
            packet.Role = role; packet.Capabilities = role == Role.NineSols ? 3u : 7u;
            bool rejected = false; try { Protocol.Write(new MemoryStream(), packet); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Non-gameplay native binding authority accepted");
        }
        packet.Role = Role.NineSols; packet.Capabilities = 7; packet.NativeBindings = new byte[Protocol.MaxBindingsBytes + 1];
        bool oversized = false; try { Protocol.Write(new MemoryStream(), packet); } catch (InvalidDataException) { oversized = true; }
        Assert(oversized, "Oversized native bindings accepted");
        using var host = new Endpoint(session, Role.HollowKnight, 7); using var native = new Endpoint(session, Role.NineSols, 7);
        host.PublishFrame(1); native.PublishFrame(1);
        var input = new byte[] { 1, 2, 3 }; native.PublishNativeBindings(input); input[0] = 99;
        Assert(Until(() => host.IsPeerFresh && host.NativeBindings.SequenceEqual(new byte[] { 1, 2, 3 })), "Published bindings retained caller mutation");
        long sequence = host.Peer!.Sequence;
        Assert(Until(() => host.Peer!.Sequence > sequence + 2 && host.NativeBindings[0] == 1), "Unchanged snapshots erased native bindings");
        native.PublishNativeBindings(new byte[] { 4, 5 });
        Assert(Until(() => host.NativeBindings.SequenceEqual(new byte[] { 4, 5 })), "Changed native bindings not applied");
        native.Dispose(); Assert(Until(() => !host.IsPeerFresh && host.NativeBindings.Length == 0), "Disconnected session retained bindings");
        Console.WriteLine("PASS: bounded NS-only binding codec, every truncation, real-pipe change propagation, immutable publication and teardown");
    }

    private static void DetailedExchange()
    {
        var session=Guid.NewGuid();var id=Guid.NewGuid();
        using var host=new Endpoint(session,Role.HollowKnight,7);using var native=new Endpoint(session,Role.NineSols,7);
        host.PublishFrame(100,detailCapture:id);native.PublishFrame(100);
        Assert(Until(()=>native.FreshDetailCapture==id),"Capture request did not cross actual pipe");
        native.PublishFrame(101,detailCapture:id);Assert(Until(()=>host.FreshDetailCapture==id),"Capture acknowledgement missing");
        host.PublishFrame(101);Assert(Until(()=>native.IsPeerFresh && native.FreshDetailCapture==Guid.Empty),"Capture stop lost");
        host.Dispose();Assert(Until(()=>!native.IsPeerFresh && native.FreshDetailCapture==Guid.Empty),"Disconnect retained capture request");
        Console.WriteLine("PASS: actual-pipe incident request/ack/stop and disconnected freshness");
    }
    private static void DetailedCodec()
    {
        foreach(var role in new[]{Role.HollowKnight,Role.NineSols}) {
            var id=Guid.NewGuid();var p=new Packet{Kind=MessageKind.Snapshot,Role=role,Session=Guid.NewGuid(),Sequence=1,Frame=1,FrameTick=1,SentTick=1,Capabilities=7,DetailCapture=id};
            using var stream=new MemoryStream(); Protocol.Write(stream,p);stream.Position=0;Assert(Protocol.Read(stream).DetailCapture==id,"Incident identity lost");
            p.Kind=MessageKind.Hello;bool rejected=false;try{Protocol.Write(new MemoryStream(),p);}catch(InvalidDataException){rejected=true;}Assert(rejected,"Hello acquired capture authority");
            p.Kind=MessageKind.Snapshot;p.Capabilities=3;rejected=false;try{Protocol.Write(new MemoryStream(),p);}catch(InvalidDataException){rejected=true;}Assert(rejected,"Telemetry-only capture accepted");
        }
        Console.WriteLine("PASS: matched capture request/ack identity roundtrip and capability/hello rejection");
    }

    private static void Codec()
    {
        WireCompatibilityChecks.Run();
        var message = Message(Guid.NewGuid(), Role.HollowKnight);
        using var bytes = new MemoryStream();
        Protocol.Write(bytes, message);
        Assert(bytes.Length == Protocol.PacketSize, "Wire size drift");
        bytes.Position = 0;
        var decoded = Protocol.Read(bytes);
        Assert(decoded.Session == message.Session && decoded.Frame == 123 && decoded.SentTick == message.SentTick, "Roundtrip changed packet");
        var data = bytes.ToArray();
        foreach (int position in new[] { 0, 4, 6, 7, 72 })
        {
            var invalid = (byte[])data.Clone(); invalid[position] = 255;
            bool refused = false;
            try { Protocol.Read(new MemoryStream(invalid)); } catch (InvalidDataException) { refused = true; }
            Assert(refused, "Invalid header accepted at " + position);
        }
        for (int length = 0; length < data.Length; length++)
        {
            bool refused = false;
            try { Protocol.Read(new MemoryStream(data, 0, length)); } catch (EndOfStreamException) { refused = true; }
            Assert(refused, "Truncated packet accepted at " + length);
        }
        Console.WriteLine("PASS: fixed wire size, packet roundtrip, malformed headers and every truncation length");
    }

    private static void AudioCodec()
    {
        var p = Message(Guid.NewGuid(), Role.HollowKnight, MessageKind.Snapshot, 1); p.Capabilities = 7;
        foreach (bool paused in new[] { false, true }) {
            p.Audio = new HostAudioState(0.35f, 0.7f, paused);
            using var data = new MemoryStream(); Protocol.Write(data, p); data.Position = 0;
            var copy = Protocol.Read(data); Assert(copy.Audio?.Master == 0.35f && copy.Audio.Effects == 0.7f && copy.Audio.Paused == paused, "Host audio changed");
        }
        foreach (float bad in new[] { -0.1f, 1.1f, float.NaN, float.PositiveInfinity }) {
            p.Audio = new HostAudioState(bad, 1, false); bool rejected = false;
            try { Protocol.Write(new MemoryStream(), p); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Invalid audio volume accepted");
        }
        p.Audio = new HostAudioState(1, 1, false); p.Role = Role.NineSols;
        bool wrongOwner = false; try { Protocol.Write(new MemoryStream(), p); } catch (InvalidDataException) { wrongOwner = true; }
        Assert(wrongOwner, "Guest audio authority accepted");
        Console.WriteLine("PASS: host audio volume/pause roundtrip, strict bounds and ownership");
    }

    private static void AudioExchange()
    {
        var session = Guid.NewGuid();
        using var host = new Endpoint(session, Role.HollowKnight, 7);
        using var guest = new Endpoint(session, Role.NineSols, 7);
        host.PublishFrame(1, audio: new HostAudioState(0.4f, 0.6f, false)); guest.PublishFrame(1);
        Assert(Until(() => guest.IsPeerFresh && guest.Peer?.Audio?.Master == 0.4f), "Audio state not delivered");
        host.PublishFrame(2, audio: new HostAudioState(0.2f, 0.1f, true));
        Assert(Until(() => guest.Peer?.Audio?.Paused == true && guest.Peer.Audio.Effects == 0.1f), "Volume/pause update not delivered");
        Assert(Until(() => !guest.IsPeerFresh), "Stale audio remained fresh");
        host.PublishFrame(3, audio: new HostAudioState(0.2f, 0.1f, false));
        Assert(Until(() => guest.IsPeerFresh && guest.Peer?.Audio?.Paused == false), "Audio freshness did not recover");
        host.Dispose(); Assert(Until(() => guest.Peer == null), "Disconnected audio retained");
        Console.WriteLine("PASS: native audio metadata real-pipe update, pause, stale, recovery and disconnect");
    }

    private static void Exchange()
    {
        var session = Guid.NewGuid();
        using var hk = new Endpoint(session, Role.HollowKnight);
        using var ns = new Endpoint(session, Role.NineSols);
        hk.PublishFrame(42); ns.PublishFrame(84);
        Assert(Until(() => hk.Peer?.Frame == 84 && ns.Peer?.Frame == 42), "Bidirectional snapshots not received");
        hk.PublishFrame(43); ns.PublishFrame(85);
        Assert(Until(() => hk.Peer?.Frame == 85 && ns.Peer?.Frame == 43), "Snapshots not updated");
        Assert(hk.Peer!.RoundTripMs >= 0 && ns.Peer!.RoundTripMs >= 0, "Invalid timing");
        ns.Dispose();
        Assert(Until(() => hk.State == "disconnected" && hk.Peer == null), "Disconnect retained stale peer");
        Assert(ns.Peer == null, "Disposed peer retained state");
        Console.WriteLine("PASS: actual named-pipe exchange in both directions, updates, timing, disconnect invalidation");
    }

    private static void PoseCodec()
    {
        var id = Guid.NewGuid(); var packet = Message(id, Role.NineSols, MessageKind.Snapshot, 1);
        packet.Capabilities = 3; packet.Pose = new Pose(id, 42, 110.6f, 60, 7.5f, 0, 0.5f, 1.5f, true, jumpReach: 5.25f);
        using var bytes = new MemoryStream(); Protocol.Write(bytes, packet); bytes.Position = 0;
        var copy = Protocol.Read(bytes).Pose!;
        Assert(copy.X == packet.Pose.X && copy.GeometryFrame == 42 && copy.Grounded && copy.JumpReach == 5.25f, "Pose/reach changed in transport");
        void Reject()
        {
            bool refused = false; try { Protocol.Write(new MemoryStream(), packet); } catch (InvalidDataException) { refused = true; }
            Assert(refused, "Invalid pose accepted");
        }
        packet.Role = Role.HollowKnight; Reject(); packet.Role = Role.NineSols;
        packet.Capabilities = 1; Reject(); packet.Capabilities = 3;
        packet.Pose = new Pose(Guid.NewGuid(), 42, 0,0,0,0,1,1,false); Reject();
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, 100001 })
        { packet.Pose = new Pose(id, 42, bad,0,0,0,1,1,false); Reject(); }
        foreach (float reach in new[] { float.NaN, float.PositiveInfinity, -1, 1001 })
        { packet.Pose = new Pose(id, 42, 0,0,0,0,1,1,false, jumpReach: reach); Reject(); }
        Console.WriteLine("PASS: pose round trip, role/capability/geometry identity and nonfinite/range rejection");
    }

    private static void MeleeCodec()
    {
        ParryDebugCodec();
        var id = Guid.NewGuid(); var p = Message(id, Role.NineSols, MessageKind.Snapshot, 1); p.Capabilities = 3;
        MeleeWindow W(int collider, long activation, float x, float y, float width, float height) => new MeleeWindow { Collider = collider, Activation = activation, Swing = 71, X = x, Y = y, Width = width, Height = height, Direction=collider==11?180:90 };
        void Set(MeleeWindow[] windows) => p.Pose = new Pose(id, 42, 0, 0, 0, 0, 1, 1, false, melee: windows);
        Set(new[] { W(11, 8, 3, 4, 2, 1), W(12, 9, 4, 5, 1, 2) });
        using var bytes = new MemoryStream(); Protocol.Write(bytes, p); bytes.Position = 0;
        var copy = Protocol.Read(bytes).Pose!;
        Assert(copy.Melee.Length == 2 && copy.Melee[0].Collider == 11 && copy.Melee[0].Activation == 8 && copy.Melee[1].Height == 2, "Native attack windows changed");
        Assert(copy.Melee[0].Swing == 71 && copy.Melee[1].Swing == 71, "One native swing split across child colliders");
        Assert(copy.Melee[0].Direction==180 && copy.Melee[1].Direction==90,"Native melee direction changed during transport");
        foreach(var angle in new[]{float.NaN,float.PositiveInfinity,-1f,361f}) {
            var window=W(1,1,0,0,1,1);window.Direction=angle;Set(new[]{window});
            bool rejected=false;try {Protocol.Write(new MemoryStream(),p);}catch(InvalidDataException){rejected=true;}
            Assert(rejected,"Invalid native melee direction accepted");
        }
        var invalidSwing = W(1, 1, 0, 0, 1, 1); invalidSwing.Swing = -1; Set(new[] { invalidSwing });
        bool refusedSwing = false; try { Protocol.Write(new MemoryStream(), p); } catch (InvalidDataException) { refusedSwing = true; }
        Assert(refusedSwing, "Negative swing admitted");
        foreach (var bad in new[] {
            new[] { W(0, 1, 0, 0, 1, 1) }, new[] { W(1, 0, 0, 0, 1, 1) },
            new[] { W(1, 1, float.NaN, 0, 1, 1) }, new[] { W(1, 1, 0, 0, -1, 1) },
            new[] { W(1, 1, 0, 0, 1, 1), W(1, 2, 0, 0, 1, 1) } }) {
            Set(bad); bool refused = false; try { Protocol.Write(new MemoryStream(), p); } catch (InvalidDataException) { refused = true; }
            Assert(refused, "Invalid native attack identity/bounds accepted");
        }
        Console.WriteLine("PASS: independent melee activation/bounds transport and malformed identity rejection");
    }

    private static void VitalsCodec()
    {
        var id = Guid.NewGuid(); var packet = Message(id, Role.NineSols, MessageKind.Snapshot, 1); packet.Capabilities = 3;
        void Set(PlayerVitals v) => packet.Pose = new Pose(id, 42, 0, 0, 0, 0, 1, 1, true, v);
        foreach (bool dead in new[] { false, true }) foreach (bool active in new[] { false, true })
        {
            Set(new PlayerVitals(dead ? 0 : 141, 240, 27, 2, 4, 7, dead, active));
            using var bytes = new MemoryStream(); Protocol.Write(bytes, packet); bytes.Position = 0;
            var v = Protocol.Read(bytes).Pose!.Vitals!;
            Assert(v.Health == (dead ? 0 : 141) && v.MaxHealth == 240 && v.Injury == 27 && v.Chi == 2 && v.Ammo == 4 && v.Potions == 7 && v.Dead == dead && v.TalismanActive == active, "Native vitals changed");
        }
        foreach (var bad in new[] {
            new PlayerVitals(float.NaN,240,0,0,0,0,false,false), new PlayerVitals(241,240,0,0,0,0,false,false),
            new PlayerVitals(0,0,0,0,0,0,false,false), new PlayerVitals(0,240,float.PositiveInfinity,0,0,0,false,false),
            new PlayerVitals(0,240,0,-1,0,0,false,false), new PlayerVitals(0,240,0,0,101,0,false,false),
            new PlayerVitals(0,240,0,0,0,-1,false,false), new PlayerVitals(100,240,0,0,0,0,false,false,false,true) })
        {
            Set(bad); bool refused = false;
            try { Protocol.Write(new MemoryStream(), packet); } catch (InvalidDataException) { refused = true; }
            Assert(refused, "Invalid vitals accepted");
        }
        Set(new PlayerVitals(141,240,27,2,4,7,false,true,true,true,true));
        using var encoded = new MemoryStream(); Protocol.Write(encoded, packet); var data = encoded.ToArray();
        encoded.Position = 0; var charged = Protocol.Read(encoded).Pose!.Vitals!;
        Assert(charged.CounterCharging && charged.CounterReady && charged.EntryActive, "Native counter readiness lost");
        foreach (byte flags in new byte[] { 0, 2, 4, 8, 255 })
        {
            var invalid = (byte[])data.Clone(); invalid[129] = flags; bool refused = false;
            try { Protocol.Read(new MemoryStream(invalid)); } catch (InvalidDataException) { refused = true; }
            Assert(refused, "Invalid vitals flags accepted");
        }
        using var hk = new Endpoint(id, Role.HollowKnight, 3); using var ns = new Endpoint(id, Role.NineSols, 3);
        ns.PublishFrame(84, packet.Pose);
        Assert(Until(() => hk.FreshPose?.Vitals?.TalismanActive == true && hk.FreshPose.Vitals.Potions == 7), "Native vitals missing from actual pipe");
        Console.WriteLine("PASS: native vitals/action flags, malformed payloads and actual-pipe delivery");
    }

    private static void EncounterCodec()
    {
        Guid session = Guid.NewGuid(), encounter = Guid.NewGuid();
        var packet = Message(session, Role.NineSols, MessageKind.Snapshot, 1); packet.Capabilities = 7;
        foreach (Role role in new[] { Role.HollowKnight, Role.NineSols })
        foreach (EncounterPhase phase in Enum.GetValues<EncounterPhase>())
        {
            packet.Role = role; packet.Encounter = new EncounterStatus(encounter, 42, phase);
            bool allowed = role == Role.HollowKnight ? phase != EncounterPhase.Restoring && phase != EncounterPhase.Ready
                : phase != EncounterPhase.Preparing && phase != EncounterPhase.Victory && phase != EncounterPhase.Returned;
            using var bytes = new MemoryStream(); bool refused = false;
            try { Protocol.Write(bytes, packet); } catch (InvalidDataException) { refused = true; }
            Assert(refused != allowed, "Encounter authority not enforced");
            if (allowed) { bytes.Position = 0; var copy = Protocol.Read(bytes).Encounter!; Assert(copy.Matches(packet.Encounter) && copy.Phase == phase, "Encounter state changed"); }
        }
        packet.Role = Role.NineSols;
        foreach (var phase in new[] { EncounterPhase.Active, EncounterPhase.Restoring, EncounterPhase.Defeated, EncounterPhase.Ready, EncounterPhase.Advancing })
        foreach (long geometry in new long[] { 41, 42, 43 })
        {
            packet.Encounter = new EncounterStatus(encounter, 42, phase);
            packet.Pose = new Pose(session, geometry, 0, 0, 0, 0, 0.5f, 1.5f, true);
            bool refused = false;
            using var bytes = new MemoryStream();
            try { Protocol.Write(bytes, packet); bytes.Position = 0; Protocol.Read(bytes); } catch (InvalidDataException) { refused = true; }
            Assert(refused == !(geometry == 42 || (phase == EncounterPhase.Ready || phase == EncounterPhase.Advancing) && geometry > 42), "New geometry escaped drained return/sequence contract");
        }
        packet.Pose = null; packet.Encounter = new EncounterStatus(encounter, 42, EncounterPhase.Defeated);
        using var encoded = new MemoryStream(); Protocol.Write(encoded, packet);
        foreach (byte phase in new byte[] { 0, 10, 255 })
        {
            var data = encoded.ToArray(); data[154] = phase; bool refused = false;
            try { Protocol.Read(new MemoryStream(data)); } catch (InvalidDataException) { refused = true; }
            Assert(refused, "Invalid lifecycle payload accepted");
        }
        using var hk = new Endpoint(session, Role.HollowKnight, 7); using var ns = new Endpoint(session, Role.NineSols, 7);
        hk.PublishFrame(1, encounter: new EncounterStatus(encounter, 42, EncounterPhase.Active));
        ns.PublishFrame(1, encounter: packet.Encounter);
        Assert(Until(() => hk.FreshEncounter?.Phase == EncounterPhase.Defeated && ns.FreshEncounter?.Phase == EncounterPhase.Active), "Lifecycle absent while pose unavailable");
        hk.PublishFrame(2, encounter: new EncounterStatus(encounter, 42, EncounterPhase.Victory));
        ns.PublishFrame(2, new Pose(session, 43, 0, 0, 0, 0, 0.5f, 1.5f, true), new EncounterStatus(encounter, 42, EncounterPhase.Ready));
        Assert(Until(() => hk.FreshEncounter?.Phase == EncounterPhase.Ready && hk.FreshPose?.GeometryFrame == 43
            && ns.FreshEncounter?.Phase == EncounterPhase.Victory), "Early Hall presentation fabricated HK return or lost independent NS readiness");
        hk.PublishFrame(2, encounter: new EncounterStatus(encounter, 42, EncounterPhase.Returned));
        ns.PublishFrame(2, new Pose(session, 43, 0, 0, 0, 0, 0.5f, 1.5f, true), new EncounterStatus(encounter, 42, EncounterPhase.Ready));
        Assert(Until(() => hk.FreshEncounter?.Phase == EncounterPhase.Ready && hk.FreshPose?.GeometryFrame == 43), "Returned hub pose did not cross the real pipe");
        Assert(Until(() => hk.FreshEncounter == null && ns.FreshEncounter == null, 1500), "Stale lifecycle remained usable");
        Console.WriteLine("PASS: encounter authority, malformed states, real-pipe lifecycle without a pose and stale rejection");
    }

    private static void PoseExchange()
    {
        var id = Guid.NewGuid(); using var hk = new Endpoint(id, Role.HollowKnight, 3); using var ns = new Endpoint(id, Role.NineSols, 3);
        var pose = new Pose(id, 42, 110.6f, 60, 0,0,0.5f,1.5f,true);
        ns.PublishFrame(84, pose);
        Assert(Until(() => hk.FreshPose?.X == pose.X), "Native pose not received");
        Assert(Until(() => hk.FreshPose == null, 1500), "Stale native sample remained usable");
        var moved = new Pose(id, 42, 115, 62, 7.5f, 2, 0.5f, 1.5f, false);
        ns.PublishFrame(85, moved); Assert(Until(() => hk.FreshPose?.X == 115 && hk.FreshPose?.Y == 62 && !hk.FreshPose.Grounded), "Fresh moving pose did not recover");
        ns.Dispose(); Assert(Until(() => hk.State == "disconnected" && hk.FreshPose == null), "Disconnected pose retained");
        var mismatchId = Guid.NewGuid(); using var a = new Endpoint(mismatchId, Role.HollowKnight, 3); using var b = new Endpoint(mismatchId, Role.NineSols);
        Assert(Until(() => a.State == "disconnected" && b.State == "disconnected"), "Capability mismatch accepted");
        Console.WriteLine("PASS: real-pipe poses, stale sample rejection, fresh recovery, disconnect and capability mismatch");
    }

    private static void InputCodec()
    {
        var packet = Message(Guid.NewGuid(), Role.HollowKnight, MessageKind.Snapshot, 1); packet.Capabilities = 7;
        packet.Inputs = new[] { new InputCommand(1, 123, packet.SentTick, new Controls(1, 0, Buttons.Jump)), new InputCommand(2, 123, packet.SentTick, default) };
        using var bytes = new MemoryStream(); Protocol.Write(bytes, packet); bytes.Position = 0;
        var copy = Protocol.Read(bytes);
        Assert(bytes.Length == Protocol.PacketSize + 72 && copy.Inputs.Length == 2 && copy.Inputs[0].Controls.Buttons == Buttons.Jump && copy.Inputs[1].Sequence == 2, "Input batch changed");
        var data = bytes.ToArray();
        for (int length = Protocol.PacketSize; length < data.Length; length++)
        {
            bool refused = false; try { Protocol.Read(new MemoryStream(data, 0, length)); } catch (EndOfStreamException) { refused = true; }
            Assert(refused, "Truncated input accepted");
        }
        void Reject()
        {
            bool refused = false; try { Protocol.Write(new MemoryStream(), packet); } catch (InvalidDataException) { refused = true; }
            Assert(refused, "Invalid input accepted");
        }
        packet.Role = Role.NineSols; Reject(); packet.Role = Role.HollowKnight;
        packet.Capabilities = 3; Reject(); packet.Capabilities = 7;
        packet.InputAck = 1; Reject(); packet.InputAck = 0;
        foreach (var invalid in new[] {
            new InputCommand(0, 123, packet.SentTick, default), new InputCommand(1, 124, packet.SentTick, default),
            new InputCommand(1, 123, packet.SentTick + 1, default), new InputCommand(1, 123, packet.SentTick, new Controls(float.NaN, 0, 0)),
            new InputCommand(1, 123, packet.SentTick, new Controls(0, 1.01f, 0)), new InputCommand(1, 123, packet.SentTick, new Controls(0, 0, (Buttons)4194304)) })
        { packet.Inputs = new[] { invalid }; Reject(); }
        packet.Inputs = new[] { new InputCommand(1,123,packet.SentTick,default), new InputCommand(3,123,packet.SentTick,default) }; Reject();
        data[Protocol.PacketSize - 1] = 17; bool oversized = false; try { Protocol.Read(new MemoryStream(data)); } catch (InvalidDataException) { oversized = true; }
        Assert(oversized, "Oversized input allocation accepted");
        foreach (var buttons in new[] { Buttons.Heal, Buttons.Bow, Buttons.Talisman, Buttons.Inventory, Buttons.Cancel, Buttons.Interact, Buttons.ChildPrevious, Buttons.ChildNext, Buttons.Confirm, Buttons.HostPaused, Buttons.Keyboard, Buttons.DoorEnter, Buttons.EntryRevealed, Buttons.NativePauseMenu, Buttons.NextItem, Buttons.PreviousItem, Buttons.StatueEnter, Buttons.ParryDebug, (Buttons)4194303 })
        {
            packet.Inputs = new[] { new InputCommand(1,123,packet.SentTick,new Controls(0,0,buttons)) };
            using var extended = new MemoryStream(); Protocol.Write(extended, packet); extended.Position = 0;
            Assert(Protocol.Read(extended).Inputs[0].Controls.Buttons == buttons, "Extended native action changed in codec");
        }
        var legacy = (byte[])data.Clone(); legacy[4] = 3; legacy[5] = 0;
        bool legacyRejected = false; try { Protocol.Read(new MemoryStream(legacy)); } catch (InvalidDataException) { legacyRejected = true; }
        Assert(legacyRejected, "Legacy input peer accepted extended schema");
        Console.WriteLine("PASS: ordered input codec, bounds, role/capability/ack validation, all payload truncations");
    }

    private static void InputExchange()
    {
        InputPresentation();
        ParryDebugExchange();
        var id = Guid.NewGuid(); using var hk = new Endpoint(id, Role.HollowKnight, 7); using var ns = new Endpoint(id, Role.NineSols, 7);
        hk.PublishFrame(1); ns.PublishFrame(1);
        Assert(Until(() => hk.IsPeerFresh && ns.IsPeerFresh), "Input peers did not connect");
        var pressed = new Controls(1, 0, Buttons.Jump);
        hk.PublishControls(1, pressed); hk.PublishControls(1, default); // Both can be sent in the same packet.
        Controls held = default;
        Assert(Until(() => { held = ns.ConsumeControls(held); return held.Equals(pressed); }), "Short press lost");
        Assert(Until(() => { held = ns.ConsumeControls(held); return held.Equals(default); }), "Short release lost");
        Assert(ns.ConsumedInputSequence == 2 && Until(() => hk.Peer?.InputAck == 2), "Applied input ack missing");
        hk.PublishFrame(2); ns.PublishFrame(2); hk.PublishControls(2, pressed);
        Assert(Until(() => { held = ns.ConsumeControls(held); return held.Equals(pressed); }), "Held input missing");
        Assert(Until(() => { held = ns.ConsumeControls(held); return held.Equals(default); }, 1500), "Stale held input survived");
        hk.PublishFrame(3); ns.PublishFrame(3); hk.PublishControls(3, default); hk.PublishControls(3, pressed);
        Assert(Until(() => { held = ns.ConsumeControls(held); return held.Equals(pressed); }), "Fresh input did not recover");
        hk.Dispose(); Assert(Until(() => ns.State == "disconnected"), "Input disconnect missing");
        Assert(ns.ConsumeControls(held).Equals(default), "Disconnected held input survived");
        Console.WriteLine("PASS: real-pipe short taps, ordered native-tick consumption, acknowledgements, stale/disconnect neutralization");
    }

    private static void InputPresentation()
    {
        var id = Guid.NewGuid(); using var hk = new Endpoint(id, Role.HollowKnight, 7); using var ns = new Endpoint(id, Role.NineSols, 7);
        Assert(ns.KeyboardInput, "New session did not default to keyboard prompts");
        hk.PublishFrame(1); ns.PublishFrame(1);
        Assert(Until(() => hk.IsPeerFresh && ns.IsPeerFresh), "Presentation peers did not connect");
        var keyboard = new Controls(1, 0, Buttons.Keyboard | Buttons.Attack);
        var controller = new Controls(0, 0, Buttons.Interact);
        Controls held = default;
        hk.PublishControls(1, keyboard);
        Assert(Until(() => { held = ns.ConsumeControls(held); return held.Equals(keyboard); }), "Keyboard sample missing");
        Assert(Until(() => { held = ns.ConsumeControls(held); return held.Equals(default); }, 1500), "Stale attack/movement did not neutralize");
        Assert(ns.KeyboardInput, "Stale keyboard controls manufactured controller prompts");
        hk.PublishFrame(2); ns.PublishFrame(2); hk.PublishControls(2, controller);
        Thread.Sleep(350);
        Assert(Until(() => { held = ns.ConsumeControls(held); return ns.ConsumedInputSequence == 2; }), "Expired controller command not drained");
        Assert(ns.KeyboardInput && held.Equals(default), "Expired controller hint changed presentation or gameplay");
        controller = new Controls(1, 0, Buttons.Interact);
        hk.PublishFrame(3); ns.PublishFrame(3); hk.PublishControls(3, controller);
        Assert(Until(() => { held = ns.ConsumeControls(held); return held.Equals(controller); }), "Fresh controller command missing");
        Assert(!ns.KeyboardInput, "Real controller command did not switch prompts");
        Assert(Until(() => { held = ns.ConsumeControls(held); return held.Equals(default); }, 1500) && !ns.KeyboardInput,
            "Stale controller controls changed device or retained gameplay");
        hk.PublishFrame(4); ns.PublishFrame(4); hk.PublishControls(4, keyboard);
        Assert(Until(() => { held = ns.ConsumeControls(held); return held.Equals(keyboard); }) && ns.KeyboardInput, "Keyboard did not switch prompts back");
        hk.Dispose(); Assert(Until(() => ns.State == "disconnected"), "Presentation disconnect missing");
        Assert(ns.ConsumeControls(held).Equals(default) && ns.KeyboardInput, "Disconnect retained gameplay or manufactured controller input");
        Console.WriteLine("PASS: keyboard prompt identity survives stale/expired/disconnected input while gameplay neutralizes; fresh controller and keyboard commands switch normally.");
    }

    private static void ParryDebugCodec()
    {
        foreach(var phase in new[]{
            (Parry:false,Dealer:true,Collider:true,Hierarchy:true,Valid:true,Expected:false),
            (Parry:true,Dealer:false,Collider:true,Hierarchy:true,Valid:true,Expected:false),
            (Parry:true,Dealer:true,Collider:false,Hierarchy:true,Valid:true,Expected:false),
            (Parry:true,Dealer:true,Collider:true,Hierarchy:false,Valid:true,Expected:false),
            (Parry:true,Dealer:true,Collider:true,Hierarchy:true,Valid:false,Expected:false),
            (Parry:true,Dealer:true,Collider:true,Hierarchy:true,Valid:true,Expected:true)})
            Assert(ParryContactContours.Active(phase.Parry,phase.Dealer,phase.Collider,phase.Hierarchy,phase.Valid)==phase.Expected,
                "Idle/recovery/disabled native parry area became visible, or active parry was omitted");
        var nativeCircle=ParryContactContours.Circle(2,3,22);
        for(int i=0;i<nativeCircle.Length;i+=2) {
            double x=nativeCircle[i]-2,y=nativeCircle[i+1]-3;
            Assert(Math.Abs(x*x+y*y-484)<.002,"Circular air-deflect outline changed native radius");
        }
        var p=Message(Guid.NewGuid(),Role.NineSols,MessageKind.Snapshot,1);p.Capabilities=7;
        var box=ParryContactContours.Box(3,-2,2,4);
        void Set(float[][] paths)=>p.Pose=new Pose(p.Session,42,0,0,0,0,1,2,true,parryContacts:paths);
        Set(new[]{box,ParryContactContours.Circle(0,0,2),ParryContactContours.Capsule(1,2,2,6,true)});
        using var data=new MemoryStream();Protocol.Write(data,p);var bytes=data.ToArray();data.Position=0;
        var copy=Protocol.Read(data).Pose!;
        Assert(copy.ParryContacts.Length==3 && copy.ParryContacts[0].SequenceEqual(box),"Native parry receiver corners changed");
        Assert(box.SequenceEqual(new[]{2f,-4f,4f,-4f,4f,0f,2f,0f}),"Native box offset/size was inflated");
        foreach(bool vertical in new[]{false,true}) {
            var points=ParryContactContours.Capsule(1,2,vertical?2:6,vertical?6:2,vertical);
            var x=points.Where((_,i)=>i%2==0).ToArray();var y=points.Where((_,i)=>i%2==1).ToArray();
            Assert(Math.Abs(x.Min()-(vertical?0:-2))<.0001 && Math.Abs(x.Max()-(vertical?2:4))<.0001
                && Math.Abs(y.Min()-(vertical?-1:1))<.0001 && Math.Abs(y.Max()-(vertical?5:3))<.0001,"Sampled capsule changed native extents");
        }
        for(int length=0;length<bytes.Length;length++) {
            bool rejected=false;try{Protocol.Read(new MemoryStream(bytes,0,length));}catch(EndOfStreamException){rejected=true;}
            Assert(rejected,"Truncated parry contact snapshot accepted");
        }
        foreach(var paths in new[]{new float[9][],new[]{new float[4]},new[]{new float[7]},new[]{new float[130]},new[]{new[]{0f,0f,float.NaN,0f,1f,1f}},new[]{new[]{0f,0f,100001f,0f,1f,1f}}}) {
            Set(paths);bool rejected=false;try{Protocol.Write(new MemoryStream(),p);}catch(InvalidDataException){rejected=true;}
            Assert(rejected,"Malformed/unbounded parry contact payload accepted");
        }
        Set(new[]{box});p.Capabilities=3;bool unauthorized=false;try{Protocol.Write(new MemoryStream(),p);}catch(InvalidDataException){unauthorized=true;}
        Assert(unauthorized,"Debug contour accepted without gameplay authority");p.Capabilities=7;
        var old=(byte[])bytes.Clone();old[4]=48;old[5]=0;bool legacy=false;try{Protocol.Read(new MemoryStream(old));}catch(InvalidDataException){legacy=true;}
        Assert(legacy,"Pre-overlay gameplay48 peer accepted");
        foreach(int offset in new[]{189,190}) {
            var bad=(byte[])bytes.Clone();bad[offset]=255;bool rejected=false;try{Protocol.Read(new MemoryStream(bad));}catch(InvalidDataException){rejected=true;}
            Assert(rejected,"Unbounded parry contour header accepted");
        }
        Console.WriteLine("PASS: parry debug box/capsule extents, native corner roundtrip, bounded contours/coordinates, all truncations, role/capability and old-peer rejection.");
    }
    private static void ParryDebugExchange()
    {
        var id=Guid.NewGuid();using var hk=new Endpoint(id,Role.HollowKnight,7);using var ns=new Endpoint(id,Role.NineSols,7);
        hk.PublishFrame(1);ns.PublishFrame(1);Assert(Until(()=>hk.IsPeerFresh && ns.IsPeerFresh),"Debug peers not connected");
        var debug=new Controls(0,0,Buttons.Keyboard|Buttons.ParryDebug);Controls held=default;
        hk.PublishControls(1,debug);
        Assert(Until(()=>{held=ns.ConsumeControls(held);return ns.ParryDebugRequested;}),"F9 debug request missing");
        var pose=new Pose(id,42,0,0,0,0,1,2,true,parryContacts:new[]{ParryContactContours.Box(0,1,1,2)});
        ns.PublishFrame(2,pose);hk.PublishFrame(2);
        Assert(Until(()=>hk.FreshPose?.ParryContacts.Length==1),"Native contact contours not transported");
        Assert(Until(()=>{held=ns.ConsumeControls(held);return !ns.IsPeerFresh;},1500) && !ns.ParryDebugRequested,"Stale debug request remained enabled");
        hk.PublishFrame(3);ns.PublishFrame(3);hk.PublishControls(3,debug);
        Assert(Until(()=>{held=ns.ConsumeControls(held);return ns.ParryDebugRequested;}),"Held-open F9 request did not renew");
        hk.PublishFrame(4);ns.PublishFrame(4);hk.PublishControls(4,new Controls(0,0,Buttons.Keyboard));
        Assert(Until(()=>{held=ns.ConsumeControls(held);return !ns.ParryDebugRequested && ns.ConsumedInputSequence>=3;}),"F9 close did not revoke debug request");
        ns.PublishFrame(5,new Pose(id,42,0,0,0,0,1,2,true));hk.PublishFrame(5);
        Assert(Until(()=>hk.FreshPose?.ParryContacts.Length==0),"Closed debug retained contours");
        hk.Dispose();Assert(Until(()=>ns.State=="disconnected") && !ns.ParryDebugRequested,"Disconnect retained debug request");
        Console.WriteLine("PASS: real-pipe F9 request/contours, stale revocation, bounded neutral renewal, close and disconnect; no gameplay grants.");
    }

    private static void EntryRenewal()
    {
        var id=Guid.NewGuid();using var hk=new Endpoint(id,Role.HollowKnight,7);using var ns=new Endpoint(id,Role.NineSols,7);
        hk.PublishFrame(1);ns.PublishFrame(1);
        Assert(Until(()=>hk.IsPeerFresh && ns.IsPeerFresh),"Entry peers did not connect");
        var reveal=new Controls(0,0,Buttons.EntryRevealed|Buttons.Keyboard);
        hk.PublishControls(1,reveal);
        Thread.Sleep(350); // Consumer is busy preparing native state.
        hk.PublishFrame(2);ns.PublishFrame(2);
        Assert(Until(()=>hk.IsPeerFresh && ns.IsPeerFresh),"Entry peers did not recover freshness");
        Controls held=default;
        Assert(Until(()=>{held=ns.ConsumeControls(held);return ns.ConsumedInputSequence==1;}),"Expired entry command was not drained");
        Assert(held.Equals(default),"Expired entry command bypassed freshness");
        hk.PublishControls(2,reveal,true);
        Assert(Until(()=>{held=ns.ConsumeControls(held);return held.Equals(reveal) && ns.ConsumedInputSequence==2;}),"Fresh neutral entry renewal was lost");
        hk.PublishControls(2,reveal,true);Thread.Sleep(25);
        Assert(ns.ConsumeControls(held).Equals(reveal) && ns.ConsumedInputSequence==2,"Entry renewal exceeded its rate budget");
        foreach(var invalid in new[]{new Controls(1,0,Buttons.EntryRevealed),new Controls(0,0,Buttons.EntryRevealed|Buttons.Attack),default(Controls)}) {
            bool rejected=false;try{hk.PublishControls(2,invalid,true);}catch(ArgumentException){rejected=true;}
            Assert(rejected,"Entry renewal accepted movement/combat or absent reveal");
        }
        Console.WriteLine("PASS: expired entry acknowledgement is rejected then renewed fresh; movement/combat renewal rejected and cadence bounded");
    }

    private static void MenuRenewal()
    {
        var id=Guid.NewGuid();using var hk=new Endpoint(id,Role.HollowKnight,7);using var ns=new Endpoint(id,Role.NineSols,7);
        hk.PublishFrame(1);ns.PublishFrame(1);
        Assert(Until(()=>hk.IsPeerFresh && ns.IsPeerFresh),"Menu peers did not connect");
        var reveal=new Controls(0,0,Buttons.NativePauseMenu|Buttons.HostPaused|Buttons.Keyboard);
        hk.PublishControls(1,reveal);
        Thread.Sleep(350); // Consumer is busy preparing native state.
        hk.PublishFrame(2);ns.PublishFrame(2);
        Assert(Until(()=>hk.IsPeerFresh && ns.IsPeerFresh),"Menu peers did not recover freshness");
        Controls held=default;
        Assert(Until(()=>{held=ns.ConsumeControls(held);return ns.ConsumedInputSequence==1;}),"Expired menu request was not drained");
        Assert(held.Equals(default),"Expired menu request bypassed freshness");
        hk.PublishControls(2,reveal,renewMenuRequest:true);
        Assert(Until(()=>{held=ns.ConsumeControls(held);return held.Equals(reveal) && ns.ConsumedInputSequence==2;}),"Fresh neutral menu renewal was lost");
        hk.PublishControls(2,reveal,renewMenuRequest:true);Thread.Sleep(25);
        Assert(ns.ConsumeControls(held).Equals(reveal) && ns.ConsumedInputSequence==2,"Menu renewal exceeded its rate budget");
        foreach(var invalid in new[]{new Controls(1,0,Buttons.NativePauseMenu),new Controls(0,0,Buttons.NativePauseMenu|Buttons.Attack),default(Controls)}) {
            bool rejected=false;try{hk.PublishControls(2,invalid,renewMenuRequest:true);}catch(ArgumentException){rejected=true;}
            Assert(rejected,"Menu renewal accepted movement/combat or absent menu request");
        }
        Console.WriteLine("PASS: expired menu request is rejected then renewed fresh; movement/combat renewal rejected and cadence bounded");
    }

    private static void InputBeforeFrame()
    {
        var session = Guid.NewGuid();
        using var hk = new Endpoint(session, Role.HollowKnight, 7);
        using var ns = new Endpoint(session, Role.NineSols, 7);
        hk.PublishFrame(1); ns.PublishFrame(1);
        Assert(Until(() => hk.IsPeerFresh && ns.IsPeerFresh), "Early-input peers did not connect");
        Controls held = default;
        for (int frame = 2; frame <= 3; frame++)
        {
            var controls = frame == 2 ? new Controls(1, 0, Buttons.Attack) : default;
            long before = ns.Peer!.Sequence;
            hk.PublishControls(frame, controls);
            Assert(Until(() => ns.State == "disconnected" || ns.Peer?.Sequence >= before + 4), "Early-input exchange stalled");
            held = ns.ConsumeControls(held);
            Assert(ns.State != "disconnected" && hk.State != "disconnected" && ns.ConsumedInputSequence == frame - 2,
                "Input escaped ahead of its published frame");
            hk.PublishFrame(frame); ns.PublishFrame(frame);
            Assert(Until(() => { held = ns.ConsumeControls(held); return ns.ConsumedInputSequence == frame - 1; }), "Deferred input was lost");
            Assert(held.Equals(controls), "Deferred press/release changed");
        }
        Console.WriteLine("PASS: lifecycle press/release queued before frame publication wait without disconnect or loss");
    }

    private static void InvalidInputPeers()
    {
        var id = Guid.NewGuid();
        using (var server = new NamedPipeServerStream("YiGodhome." + id.ToString("N"), PipeDirection.InOut))
        using (var ns = new Endpoint(id, Role.NineSols, 7))
        {
            server.WaitForConnection(); var hello = Message(id, Role.HollowKnight); hello.Capabilities = 7; Protocol.Write(server, hello);
            var reply = Protocol.Read(server); var packet = Message(id, Role.HollowKnight, MessageKind.Snapshot, 1);
            packet.Capabilities = 7; packet.EchoTick = reply.SentTick;
            packet.Inputs = new[] { new InputCommand(2, 123, packet.SentTick, new Controls(1, 0, Buttons.Attack)) };
            Protocol.Write(server, packet);
            Assert(Until(() => ns.State == "disconnected") && ns.ConsumeControls(default).Equals(default), "Input gap applied");
        }
        id = Guid.NewGuid();
        using (var hk = new Endpoint(id, Role.HollowKnight, 7))
        using (var client = new NamedPipeClientStream(".", "YiGodhome." + id.ToString("N"), PipeDirection.InOut))
        {
            client.Connect(5000); var hello = Protocol.Read(client); var reply = Message(id, Role.NineSols);
            reply.Capabilities = 7; reply.EchoTick = hello.SentTick; Protocol.Write(client, reply);
            var snapshot = Protocol.Read(client); reply.Kind = MessageKind.Snapshot; reply.Sequence = 1; reply.EchoTick = snapshot.SentTick; reply.InputAck = 1;
            Protocol.Write(client, reply); Assert(Until(() => hk.State == "disconnected"), "Unsent input acknowledged");
        }
        Console.WriteLine("PASS: input sequence gap and acknowledgement of unsent input disconnect safely");
    }

    private static void WrongSession()
    {
        var session = Guid.NewGuid();
        using var hk = new Endpoint(session, Role.HollowKnight);
        using var rogue = new NamedPipeClientStream(".", "YiGodhome." + session.ToString("N"), PipeDirection.InOut);
        rogue.Connect(5000);
        Protocol.Read(rogue);
        Protocol.Write(rogue, Message(Guid.NewGuid(), Role.NineSols));
        Assert(Until(() => hk.State == "disconnected"), "Wrong-session peer accepted");
        Assert(hk.Peer == null, "Unverified peer published state");
        Console.WriteLine("PASS: wrong-session handshake rejected without publishing peer data");
    }

    private static void SeparateClocks()
    {
        var session = Guid.NewGuid();
        using var hk = new Endpoint(session, Role.HollowKnight);
        using var ns = new NamedPipeClientStream(".", "YiGodhome." + session.ToString("N"), PipeDirection.InOut);
        ns.Connect(5000);
        var hello = Protocol.Read(ns);
        var reply = Message(session, Role.NineSols);
        reply.FrameTick = 900000000000000; reply.SentTick = reply.FrameTick + 30000; reply.Frequency = 1000000;
        reply.EchoTick = hello.SentTick;
        Protocol.Write(ns, reply);
        var snapshot = Protocol.Read(ns);
        reply.Kind = MessageKind.Snapshot; reply.Sequence = 1; reply.EchoTick = snapshot.SentTick;
        Protocol.Write(ns, reply);
        Assert(Until(() => hk.Peer != null), "Different clock epoch/frequency rejected");
        Assert(Math.Abs(hk.Peer!.SenderSampleAgeMs - 30) < 0.001, "Remote sample age used local clock");
        Assert(hk.Peer.RoundTripMs >= 0 && hk.Peer.RoundTripMs < 5000, "RTT used remote epoch");
        Console.WriteLine("PASS: different remote timer epoch/frequency; timing stays within originating clocks");
    }

    private static void BadSequence()
    {
        var session = Guid.NewGuid();
        using var hk = new Endpoint(session, Role.HollowKnight);
        using var ns = new NamedPipeClientStream(".", "YiGodhome." + session.ToString("N"), PipeDirection.InOut);
        ns.Connect(5000);
        var hello = Protocol.Read(ns);
        var reply = Message(session, Role.NineSols); reply.EchoTick = hello.SentTick;
        Protocol.Write(ns, reply);
        var snapshot = Protocol.Read(ns);
        reply = Message(session, Role.NineSols, MessageKind.Snapshot, 2); reply.EchoTick = snapshot.SentTick;
        Protocol.Write(ns, reply);
        Assert(Until(() => hk.State == "disconnected"), "Skipped sequence accepted");
        Assert(hk.Peer == null, "Invalid snapshot published");
        Console.WriteLine("PASS: skipped sequence rejected before state publication");
    }

    private static void FrozenGameplay()
    {
        var session = Guid.NewGuid();
        using var hk = new Endpoint(session, Role.HollowKnight);
        using var ns = new NamedPipeClientStream(".", "YiGodhome." + session.ToString("N"), PipeDirection.InOut);
        ns.Connect(5000); var hello = Protocol.Read(ns);
        var reply = Message(session, Role.NineSols); reply.EchoTick = hello.SentTick;
        Protocol.Write(ns, reply); var snapshot = Protocol.Read(ns);
        reply.Kind = MessageKind.Snapshot; reply.Sequence = 1; reply.EchoTick = snapshot.SentTick;
        reply.FrameTick = reply.SentTick - 6 * reply.Frequency;
        Protocol.Write(ns, reply);
        Assert(Until(() => hk.Peer != null), "Frozen frame reply not received");
        Assert(!hk.IsPeerFresh, "Frozen gameplay incorrectly fresh");
        hk.CheckHealth();
        Assert(hk.State == "disconnected" && hk.Peer == null, "Live IPC with stalled gameplay did not terminate");
        Assert(hk.FreshPose == null && hk.FreshEncounter == null, "Stalled peer retained presentation");
        Console.WriteLine("PASS: replying IPC with six-second-old gameplay frame disconnects and invalidates state");
    }

    private static void Timeout()
    {
        var session = Guid.NewGuid();
        using var hk = new Endpoint(session, Role.HollowKnight);
        using var ns = new NamedPipeClientStream(".", "YiGodhome." + session.ToString("N"), PipeDirection.InOut);
        ns.Connect(5000);
        var hello = Protocol.Read(ns);
        var reply = Message(session, Role.NineSols); reply.EchoTick = hello.SentTick;
        Protocol.Write(ns, reply);
        Protocol.Read(ns); // Deliberately withhold the snapshot response.
        Assert(Until(() => { hk.CheckHealth(); return hk.State == "disconnected"; }), "Silent peer did not time out");
        Assert(hk.Peer == null, "Timed-out peer retained state");
        Console.WriteLine("PASS: silent connected peer times out and invalidates state");
    }
}
