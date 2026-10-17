using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using YiGodhome.Bridge;

internal static class Program
{
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static bool Until(Func<bool> test) => SpinWait.SpinUntil(test, 5000);
    private static void MinionAtlas()
    {
        // Production layout has no encounter exception/shared arena bounds.
        for(int count=1;count<=MinionHudAtlas.MaximumWidgets;count++)foreach(bool effects in new[]{false,true}) {
            int height=MinionHudAtlas.BarHeight(count)+(effects?1024:0);
            for(int i=0;i<count;i++) {
                var near=MinionHudAtlas.Widget(i,count,effects,3,7,1.8f,.9f);
                var far=MinionHudAtlas.Widget(i,count,effects,3000,-7000,1.8f,.9f);
                Assert(near.Width==192 && near.Height==192 && far.Width==near.Width && far.Height==near.Height,"Minion distance/count changed tile density");
                Assert(near.X>=0 && near.Y>=(effects?1024:0) && near.X+near.Width<=1024 && near.Y+near.Height<=height,"Minion atlas overlaps effects or escapes bounded tier");
                Assert(Math.Abs(near.WorldWidth-far.WorldWidth)<.001f && Math.Abs(near.WorldHeight-far.WorldHeight)<.001f,"Distance changed native widget size");
                Assert(Math.Abs((3-near.WorldX)/near.WorldWidth-.5f)<.0001f && near.WorldWidth>1.8f,"Tile lost native center/padding");
                Assert(Math.Abs(near.Width*1.8f/near.WorldWidth-MinionHudAtlas.Content)<.001f,"Native bar lost dedicated content texels");
                for(int j=0;j<i;j++) {
                    var other=MinionHudAtlas.Widget(j,count,effects,3,7,1.8f,.9f);
                    Assert(near.X>=other.X+other.Width || other.X>=near.X+near.Width || near.Y>=other.Y+other.Height || other.Y>=near.Y+near.Height,"Native HP tiles overlap");
                }
            }
        }
        var id=Guid.NewGuid();
        using var sender=new VisualEndpoint(id,Role.NineSols,summons:true);
        using var receiver=new VisualEndpoint(id,Role.HollowKnight,summons:true);
        int sequence=0;
        foreach(int count in new[]{1,32,1,0,17,0}) {
            var f=Frame(id);f.NativeFrame=++sequence;f.Width=count==0?1:1024;f.Height=count==0?1:MinionHudAtlas.BarHeight(count);
            f.Rgba=new byte[f.Width*f.Height*4];f.Rgba[0]=127;
            f.Regions=new VisualRegion[count];
            for(int i=0;i<count;i++)f.Regions[i]=MinionHudAtlas.Widget(i,count,false,3+i*30,7-i*50,1.8f,.9f);
            sender.PublishPixels(f);
            Assert(Until(()=>receiver.FreshFrame?.NativeFrame==sequence),"Minion atlas count/reuse/clear stalled");
            using var lease=receiver.AcquireFrame();var got=lease.Frame!;
            Assert(got.Regions.Length==count && got.Rgba[0]==127,"Real pipe lost minion atlas regions/pixels");
            for(int i=0;i<count;i++)Assert(got.Regions[i].X==f.Regions[i].X && got.Regions[i].WorldX==f.Regions[i].WorldX && got.Regions[i].WorldY==f.Regions[i].WorldY,"Real pipe detached minion positions from image");
        }
        var bad=Frame(id);
        foreach(var region in new[]{new VisualRegion(-1,0,1,1,0,0,1,1),new VisualRegion(0,0,1,1,float.NaN,0,1,1),
            new VisualRegion(0,0,int.MaxValue,1,0,0,1,1),new VisualRegion(0,0,1,1,0,0,0,1)}) {
            bad.Regions=new[]{region};Reject(()=>VisualCodec.Write(new MemoryStream(),bad));
        }
        bad.Regions=new[]{new VisualRegion(0,0,1,1,0,0,1,1)};
        Reject(()=>VisualCodec.Write(new MemoryStream(),bad,hud:true));
        bad.Regions=new VisualRegion[MinionHudAtlas.MaximumRegions+1];Reject(()=>VisualCodec.Write(new MemoryStream(),bad));
        var valid=Frame(id);valid.Regions=new[]{new VisualRegion(0,0,1,1,0,0,1,1)};
        using var bytes=new MemoryStream();VisualCodec.Write(bytes,valid);var wire=bytes.ToArray();
        Assert(wire.Length==VisualCodec.HeaderSize+MinionHudAtlas.RegionBytes+valid.Rgba.Length,"Visual region wire size drift");
        foreach(int length in new[]{VisualCodec.HeaderSize-1,VisualCodec.HeaderSize,VisualCodec.HeaderSize+31,wire.Length-1})Reject(()=>VisualCodec.Read(new MemoryStream(wire,0,length)));
        var corrupt=(byte[])wire.Clone();corrupt[156]=255;corrupt[157]=255;Reject(()=>VisualCodec.Read(new MemoryStream(corrupt)));
        var legacy=(byte[])wire.Clone();legacy[4]=12;Reject(()=>VisualCodec.Read(new MemoryStream(legacy)));
        using(var actor=new VisualEndpoint(Guid.NewGuid(),Role.NineSols)) {
            bool refused=false;
            try{actor.PublishPixels(valid);}catch(InvalidOperationException){refused=true;}
            Assert(refused,"Minion atlas escaped into actor channel");
        }
        Console.WriteLine("PASS: global fixed-density minion tiles,32 counts/effects/distance/overlap, real-pipe reuse/clear/positions and invalid atlas metadata.");
    }
    private static void OwnerCropSafety()
    {
        Assert(VisualCrop.KeepOwner(0, 256, -12, 12) == 0, "Already-safe camera center moved");
        for (int center = -1200; center <= 1200; center += 13)
        {
            float x = VisualCrop.KeepOwner(center, 256, -12, 12);
            float y = VisualCrop.KeepOwner(center, 128, -25, 35);
            Assert(x - 256 <= -12 && x + 256 >= 12 && y - 128 <= -25 && y + 128 >= 35,
                "Distant arrow/effect sliced primary native sprite");
        }
        Assert(VisualCrop.KeepOwner(480, 256, -12, 12) == 244, "Right-side arrow crop correction changed footprint");
        Assert(VisualCrop.KeepOwner(-480, 256, -12, 12) == -244, "Left-side arrow crop correction changed footprint");
        Console.WriteLine("PASS: bounded arrow/effect crops contain the entire native owner on both axes without moving already-safe crops.");
    }
    private static void SeparateHudEffects()
    {
        var id=Guid.NewGuid();
        using var barsOut=new VisualEndpoint(id,Role.NineSols,hud:true);
        using var barsIn=new VisualEndpoint(id,Role.HollowKnight,hud:true);
        using var fxOut=new VisualEndpoint(id,Role.NineSols,hudEffects:true);
        using var fxIn=new VisualEndpoint(id,Role.HollowKnight,hudEffects:true);
        var bars=Frame(id);bars.NativeFrame=501;
        var effect=Frame(id);effect.NativeFrame=902;effect.Width=effect.Height=1;effect.Rgba=new byte[4];effect.WorldWidth=effect.WorldHeight=1;
        barsOut.PublishPixels(bars);fxOut.PublishPixels(effect);
        Assert(Until(()=>barsIn.FreshFrame?.NativeFrame==501 && fxIn.FreshFrame?.NativeFrame==902),"HUD effect channel collided with resource HUD");
        using var lease=fxIn.AcquireFrame();Assert(lease.Frame!.Width==1 && lease.Frame.Rgba.Length==4,"Empty effect frame did not clear independently");
        Console.WriteLine("PASS: independent real-pipe resource/effect HUD channels and transparent effect clearing.");
    }
    private static void RootMenuIdentity()
    {
        var id = Guid.NewGuid();
        using var sender = new VisualEndpoint(id, Role.NineSols, menu: true);
        using var receiver = new VisualEndpoint(id, Role.HollowKnight, menu: true);
        for (int n = 0; n < 5; n++)
        {
            var frame = Frame(id); frame.NativeFrame = n + 100;
            frame.RootMenu = n == 0 || n == 3; frame.ControlsMenu = frame.ControlsBinding = n == 1;
            frame.ControlsMouseCapture=n==1;
            frame.InventoryMenu=n==2;
            if (n == 4) { frame.Width = frame.Height = 1; frame.Rgba = new byte[4]; }
            sender.PublishPixels(frame);
            Assert(Until(() => receiver.FreshFrame?.NativeFrame == n + 100), "Root/Controls classification transition stalled");
            using var lease = receiver.AcquireFrame();
            Assert(lease.Frame!.RootMenu == frame.RootMenu && lease.Frame.ControlsMenu == frame.ControlsMenu
                && lease.Frame.ControlsBinding == frame.ControlsBinding && lease.Frame.ControlsMouseCapture==frame.ControlsMouseCapture
                && lease.Frame.InventoryMenu==frame.InventoryMenu, "Root/Controls/inventory/capture identity changed");
        }
        var root = Frame(id); root.RootMenu = true;
        Reject(() => VisualCodec.Write(new MemoryStream(), root));
        root.ControlsMenu = true; Reject(() => VisualCodec.Write(new MemoryStream(), root, hud:true));
        root.ControlsMenu = false; root.Recovering = true; Reject(() => VisualCodec.Write(new MemoryStream(), root, hud:true));
        root.Recovering = false; root.Width = root.Height = 1; root.Rgba = new byte[4]; root.Hash = VisualCodec.Checksum(root.Rgba);
        Reject(() => VisualCodec.Write(new MemoryStream(), root, hud:true));
        var inventory=Frame(id);inventory.InventoryMenu=true;
        var invalidMouseCapture=Frame(id);invalidMouseCapture.ControlsMouseCapture=true;
        Reject(()=>VisualCodec.Write(new MemoryStream(),invalidMouseCapture,hud:true));
        invalidMouseCapture.ControlsMenu=true;
        Reject(()=>VisualCodec.Write(new MemoryStream(),invalidMouseCapture,hud:true));
        using(var inventoryWire=new MemoryStream()) {
            VisualCodec.Write(inventoryWire,inventory,hud:true);
            var old=inventoryWire.ToArray();old[4]=14;
            Reject(()=>VisualCodec.Read(new MemoryStream(old),hud:true));
        }
        Reject(()=>VisualCodec.Write(new MemoryStream(),inventory));
        inventory.ControlsMenu=true;Reject(()=>VisualCodec.Write(new MemoryStream(),inventory,hud:true));
        inventory.ControlsMenu=false;inventory.RootMenu=true;Reject(()=>VisualCodec.Write(new MemoryStream(),inventory,hud:true));
        inventory.RootMenu=false;inventory.Recovering=true;Reject(()=>VisualCodec.Write(new MemoryStream(),inventory,hud:true));
        inventory.Recovering=false;inventory.Width=inventory.Height=1;inventory.Rgba=new byte[4];inventory.Hash=VisualCodec.Checksum(inventory.Rgba);
        Reject(()=>VisualCodec.Write(new MemoryStream(),inventory,hud:true));
        Console.WriteLine("PASS: real-pipe Root→Controls→Inventory→Root→close identity; non-menu, mixed, recovery and empty menu flags rejected.");
    }
    private static void ThunderCoverage()
    {
        // RGB-only glow and alpha-squared screen particles cannot be decoded
        // by dividing their native RGB by the alpha they happened to write.
        var black = new byte[] { 150,240,240,10, 4,36,44,0, 0,0,0,255 };
        var white = new byte[] { 255,255,255,255, 255,255,255,255, 255,255,255,255 };
        var original = (byte[])black.Clone();
        ActorPixels.FromDualMatte(black, white, true);
        Assert(black[3] == 240 && black[0] < black[1] - 70 && black[1] == black[2], "Thunder cyan core was bleached white");
        Assert(black[7] == 44 && black[6] > black[5] && black[5] > black[4], "RGB-only cyan glow lost coverage/color");
        for (int p = 0; p < 2; p++) for (int c = 0; c < 3; c++)
            Assert(Math.Abs(black[p * 4 + c] * black[p * 4 + 3] / 255f - original[p * 4 + c]) <= 1,
                "Recovered Thunder effect changed its native black-background emission");
        Assert(black[11] == 0, "Thunder matte introduced an opaque background rectangle");
        var id = Guid.NewGuid();
        using var sender = new VisualEndpoint(id, Role.NineSols, arrows: true);
        using var receiver = new VisualEndpoint(id, Role.HollowKnight, arrows: true);
        var b = sender.RentFrame(16 * 16 * 4); var w = sender.RentFrame(b.Rgba.Length);
        Array.Clear(b.Rgba,0,b.Rgba.Length); Array.Fill(w.Rgba,(byte)255);
        b.Rgba[0] = 150; b.Rgba[1] = b.Rgba[2] = 240; b.Rgba[3] = 10;
        b.Session = id; b.NativeFrame = 1; b.GeometryFrame = 50; b.CaptureTick = Stopwatch.GetTimestamp();
        b.Width = b.Height = 16; b.WorldWidth = b.WorldHeight = 1; b.X = 20; b.Y = 15;
        sender.PublishMattePixels(b,w);
        Assert(Until(()=>receiver.FreshFrame?.NativeFrame==1),"Thunder matte arrow transport stalled");
        using(var lease=receiver.AcquireFrame()) {
            Assert(lease.Frame!.Rgba[3]==240 && lease.Frame.Rgba[0]<lease.Frame.Rgba[1]-70 && lease.Frame.Rgba[7]==0,
                "Thunder worker double-converted alpha or lost native cyan/background coverage");
            VisualPlacement.Resolve(lease.Frame,new Pose(id,50,100,100,0,0,.5f,1.5f,true),out float x,out float y);
            Assert(x==20 && y==15,"Thunder matte disturbed accepted world anchoring");
        }
        Assert(Until(()=>sender.OutstandingPixels==0),"Thunder matte pixel leases leaked");
        Console.WriteLine("PASS: native-style cyan core/RGB-only glow coverage, exact black emission, clear background, real-arrow matte transport and fixed world placement.");
    }
    private static void WorldArrowPlacement()
    {
        var id = Guid.NewGuid();
        using var actorSender = new VisualEndpoint(id, Role.NineSols);
        using var actorReceiver = new VisualEndpoint(id, Role.HollowKnight);
        using var arrowSender = new VisualEndpoint(id, Role.NineSols, arrows: true);
        using var arrowReceiver = new VisualEndpoint(id, Role.HollowKnight, arrows: true);
        var actor = Frame(id); actor.AnchorX = 4; actor.AnchorY = 5;
        actor.Motion = new Pose(id, actor.GeometryFrame, 4, 5, 0, 0, .5f, 1.5f, true);
        var arrow = Frame(id); arrow.X = 20; arrow.Y = 15; arrow.Motion = null;
        actorSender.Publish(actor); arrowSender.Publish(arrow);
        Assert(Until(() => actorReceiver.FreshFrame != null && arrowReceiver.FreshFrame != null), "Separated arrow/actor pipe failed");
        using (var a = actorReceiver.AcquireFrame())
        using (var w = arrowReceiver.AcquireFrame())
        {
            for (int n = 0; n < 100; n++)
            {
                var pose = new Pose(id, actor.GeometryFrame, 4 + n, 5 + n * .5f, 1, 1, .5f, 1.5f, true);
                VisualPlacement.Resolve(a.Frame!, pose, out float ax, out float ay);
                VisualPlacement.Resolve(w.Frame!, pose, out float wx, out float wy);
                Assert(ax == actor.X + n && ay == actor.Y + n * .5f, "Arrow layer disrupted smooth actor alignment");
                Assert(wx == 20 && wy == 15 && w.Frame!.Motion == null, "Stationary explosion followed Yi motion");
            }
        }
        // Boss warning pixels use this same world channel even without a bow.
        var warning=Frame(id);warning.NativeFrame=arrow.NativeFrame+1;warning.X=42;warning.Y=23;warning.Motion=null;
        arrowSender.Publish(warning);
        Assert(Until(()=>arrowReceiver.FreshFrame?.NativeFrame==warning.NativeFrame),"Boss warning world frame did not arrive without an arrow");
        using(var w=arrowReceiver.AcquireFrame()) for(int n=0;n<100;n++) {
            var movingYi=new Pose(id,actor.GeometryFrame,n,n*.5f,1,1,.5f,1.5f,true);
            VisualPlacement.Resolve(w.Frame!,movingYi,out float wx,out float wy);
            Assert(wx==42 && wy==23,"Boss warning inherited Yi movement correction");
        }
        var empty = arrowSender.RentFrame(4); Array.Clear(empty.Rgba, 0, 4);
        empty.Session = id; empty.GeometryFrame = arrow.GeometryFrame; empty.NativeFrame = warning.NativeFrame + 1;
        empty.CaptureTick = Stopwatch.GetTimestamp(); empty.Width = empty.Height = 1; empty.WorldWidth = empty.WorldHeight = 1;
        arrowSender.PublishPixels(empty);
        Assert(Until(() => arrowReceiver.FreshFrame?.NativeFrame == empty.NativeFrame), "Arrow effect retirement stalled");
        using (var lease = arrowReceiver.AcquireFrame()) Assert(!ActorPixels.Visible(lease.Frame!.Rgba), "Retired explosion left pixels");
        Assert(actorReceiver.FreshFrame?.NativeFrame == actor.NativeFrame, "Arrow retirement replaced actor pixels");
        Console.WriteLine("PASS: independent actor/arrow real pipes, stationary explosion across100 live movements and empty effect retirement without actor loss.");
    }
    private static void ExpandedHoldSafety() {
        var hold=new ExpandedCaptureHold();
        var sizes=new[]{(1536,768),(2560,1024),(2560,2560),(768,768),(2048,1536),(768,768)};
        for(int i=0;i<sizes.Length;i++) {
            var (w,h)=sizes[i];float x=w/40f,y=h/40f;
            var choice=hold.Choose(1,i*.3,i!=3 && i!=5,w,h,x,y);
            Assert(choice.Width>=w && choice.Height>=h && choice.HalfWidth>=x && choice.HalfHeight>=y,"Held crop clipped a native effect or reduced pixels");
            Assert(Math.Abs(choice.Width/(2*choice.HalfWidth)-20)<.001 && Math.Abs(choice.Height/(2*choice.HalfHeight)-20)<.001,"Held crop changed requested native pixel density");
            if(i>=2)Assert(choice.Width==2560 && choice.Height==2560,"Burst crop churned after reaching its peak");
        }
        var homing=new ExpandedCaptureHold().Choose(1,0,true,1536,768,38.4f,19.2f,VisualCodec.MaxSide);
        Assert(homing.Width==2560 && homing.Height==2560 && homing.HalfWidth>=38.4f && homing.HalfHeight>=19.2f,"Homing failed to reserve its existing maximum tier");
        Assert(Math.Abs(homing.Width/(2*homing.HalfWidth)-20)<.001,"Homing peak reservation changed native density");
        var reset=hold.Choose(2,1.9,false,768,768,16,16);
        Assert(reset.Width==768 && reset.HalfWidth==16,"Capture hold leaked into another room");
        hold.Choose(2,2,true,2560,1536,64,38.4f);
        var expired=hold.Choose(2,5.01,false,768,768,16,16);
        Assert(expired.Width==768 && expired.HalfWidth==16,"Expanded hold failed to expire after the burst");
    }
    private static void SeparateChannels()
    {
        Guid session = Guid.NewGuid();
        var senders = new VisualEndpoint[11]; var receivers = new VisualEndpoint[11]; var expected = new uint[11];
        try
        {
            for (int i = 0; i < 11; i++)
            {
                senders[i] = new VisualEndpoint(session, i == 9 ? Role.HollowKnight : Role.NineSols, hud: i == 1, menu: i == 2, boss: i == 3, rest: i == 4, prompt: i == 5, palette: i == 6, summons: i == 7, walls: i == 8, mask: i == 9, outline: i == 10);
                receivers[i] = new VisualEndpoint(session, i == 9 ? Role.NineSols : Role.HollowKnight, hud: i == 1, menu: i == 2, boss: i == 3, rest: i == 4, prompt: i == 5, palette: i == 6, summons: i == 7, walls: i == 8, mask: i == 9, outline: i == 10);
                var frame = Frame(session); frame.Rgba[0] = (byte)(100 + i); frame.Hash = VisualCodec.Checksum(frame.Rgba);
                expected[i] = frame.Hash; senders[i].Publish(frame);
            }
            Assert(Until(() => { for (int i = 0; i < 11; i++) if (receivers[i].FreshFrame?.Hash != expected[i]) return false; return true; }), "Actor/player HUD/menu/boss/rest/prompt/palette/summons/walls/reverse mask/returned outline channels mixed or stalled");
        }
        finally { foreach (var sender in senders) sender?.Dispose(); foreach (var receiver in receivers) receiver?.Dispose(); }
    }
    private static VisualFrame Frame(Guid id, long sequence = 1)
    {
        long tick = Stopwatch.GetTimestamp();
        byte[] pixels = new byte[4 * 16 * 16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 251);
        return new VisualFrame { Session = id, Sequence = sequence, NativeFrame = sequence, CaptureTick = tick, SentTick = tick,
            GeometryFrame = 50, Width = 16, Height = 16, WorldWidth = 8, WorldHeight = 8, X = 10, Y = 20,
            Rgba = pixels, Hash = VisualCodec.Checksum(pixels) };
    }
    private static void HudAlphaWorker()
    {
        var id=Guid.NewGuid();
        using var sender=new VisualEndpoint(id,Role.NineSols,hud:true);
        sender.PreparePixels(16*16*4);int prepared=sender.PixelAllocations;sender.PreparePixels(16*16*4);
        Assert(sender.PixelAllocations==prepared && sender.OutstandingPixels==0,"Pixel preparation leaked leases or allocated repeatedly");
        using var receiver=new VisualEndpoint(id,Role.HollowKnight,hud:true);
        var frame=sender.RentFrame(16*16*4); var metadata=Frame(id);
        frame.Session=id; frame.NativeFrame=1; frame.CaptureTick=metadata.CaptureTick; frame.GeometryFrame=50;
        frame.Width=frame.Height=16; frame.WorldWidth=frame.WorldHeight=1;
        frame.Rgba[0]=64; frame.Rgba[3]=128;
        sender.PublishPremultipliedPixels(frame);
        Assert(Until(()=>receiver.FreshFrame?.NativeFrame==1),"HUD alpha worker stalled");
        using(var lease=receiver.AcquireFrame()) Assert(lease.Frame!.Rgba[0]==128 && lease.Frame.Rgba[3]==128,"HUD was double-premultiplied or alpha changed");
        var black=sender.RentFrame(16*16*4); var white=sender.RentFrame(16*16*4);
        black.Session=id; black.NativeFrame=2; black.CaptureTick=Stopwatch.GetTimestamp(); black.GeometryFrame=50;
        black.Width=black.Height=16; black.WorldWidth=black.WorldHeight=1;
        Array.Clear(black.Rgba,0,black.Rgba.Length);
        for(int i=0;i<white.Rgba.Length;i+=4) {white.Rgba[i]=white.Rgba[i+1]=white.Rgba[i+2]=255;white.Rgba[i+3]=255;}
        black.Rgba[0]=128; white.Rgba[0]=255;white.Rgba[1]=white.Rgba[2]=127;
        sender.PublishMattePixels(black,white);
        Assert(Until(()=>receiver.FreshFrame?.NativeFrame==2),"HUD matte worker stalled");
        using(var lease=receiver.AcquireFrame()) Assert(lease.Frame!.Rgba[0]==255 && lease.Frame.Rgba[3]==128 && lease.Frame.Rgba[7]==0,"Native effect matte coverage/checksum failed");
        Assert(Until(()=>sender.OutstandingPixels==0),"HUD matte lease leaked");
        black=sender.RentFrame(16*16*4);white=sender.RentFrame(16*16*4);
        black.Session=id;black.NativeFrame=3;black.CaptureTick=Stopwatch.GetTimestamp();black.GeometryFrame=50;black.Width=black.Height=16;black.WorldWidth=black.WorldHeight=1;black.MatteBackground=64;
        Array.Clear(black.Rgba,0,black.Rgba.Length);
        for(int i=0;i<white.Rgba.Length;i+=4){white.Rgba[i]=white.Rgba[i+1]=white.Rgba[i+2]=64;white.Rgba[i+3]=255;}
        // Measured native fire sample: stored alpha27, actual coverage about84.
        black.Rgba[0]=61;black.Rgba[3]=27;white.Rgba[0]=104;white.Rgba[1]=white.Rgba[2]=43;
        sender.PublishMattePixels(black,white);
        Assert(Until(()=>receiver.FreshFrame?.NativeFrame==3),"Burning dark-matte HUD worker stalled");
        using(var lease=receiver.AcquireFrame()) {
            var rgba=lease.Frame!.Rgba;float alpha=rgba[3]/255f;
            Assert(rgba[3]>80 && rgba[3]<90,"Burning kept squared native alpha instead of actual coverage");
            Assert(Math.Abs(rgba[0]*alpha+127*(1-alpha)-147)<2 && Math.Abs(rgba[1]*alpha+127*(1-alpha)-86)<2,"Transported fire pixels no longer match native background blending");
            Assert(rgba[7]==0,"Burning recovery made the unaffected screen opaque");
        }
        Assert(Until(()=>sender.OutstandingPixels==0),"Burning matte lease leaked");
        Console.WriteLine("PASS: measured fire alpha27 recovers native coverage84 over actual HUD pipe, matches native grey within2 channel values and leaves unaffected screen transparent.");
        Console.WriteLine("PASS: HUD worker converts premultiplied/dual-matte native captures once, preserves coverage/checksums and releases both leases.");
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; } catch (EndOfStreamException) { return; }
        throw new Exception("Malformed visual frame accepted");
    }
    private static void Codec()
    {
        var arrivalGate=new YiGodhome.VerticalEntryGateLease();arrivalGate.Arm(10,100,20);
        Assert(arrivalGate.Blocks(10,20) && !arrivalGate.Blocks(11,20) && !arrivalGate.Blocks(10,21),"Arrival guard leaked to another room/gate");
        Assert(!arrivalGate.Observe(99,false,true) && !arrivalGate.Observe(100,true,true) && !arrivalGate.Observe(100,false,false)
            && arrivalGate.Pending,"Stale/entering/airborne pose released reciprocal gate");
        Assert(arrivalGate.Observe(100,false,true) && !arrivalGate.Blocks(10,20),"Matching native landing did not release reciprocal gate");
        arrivalGate.Arm(11,101,21);arrivalGate.Reset();Assert(!arrivalGate.Pending,"Room teardown leaked arrival guard");

        foreach(float rise in new[]{-40f,0f,100f,280f}) {
            var impulse=YiGodhome.AirborneEntryLaunch.CalculateComponents(0,0,40,rise,1000,300);
            double time=(impulse.Y+Math.Sqrt(impulse.Y*impulse.Y-2000*rise))/1000;
            Assert(Math.Abs(impulse.X*time-40)<.001 && Math.Abs(impulse.Y*time-500*time*time-rise)<.001,"Native entry impulse misses authored landing");
            Assert(impulse.Y*impulse.Y/2000>=Math.Max(0,rise)+44.99,"Entry loses native jump clearance");
        }

        var landing=new YiGodhome.EntryFloorAdmission();
        Assert(landing.Observe(0,true)==YiGodhome.EntryFloorAdmission.Verdict.Ready,"Grounded room entry was delayed");
        Assert(landing.Observe(10,false)==YiGodhome.EntryFloorAdmission.Verdict.Waiting
            && landing.Observe(17.99,false)==YiGodhome.EntryFloorAdmission.Verdict.Waiting,"Airborne room entry failed before bounded landing window");
        Assert(landing.Observe(18,false)==YiGodhome.EntryFloorAdmission.Verdict.Expired,"Missing terrain waited forever");
        landing.Reset();
        Assert(landing.Observe(100,false)==YiGodhome.EntryFloorAdmission.Verdict.Waiting
            && landing.Observe(100.7,true)==YiGodhome.EntryFloorAdmission.Verdict.Ready && !landing.Waiting,"Native landing did not release handoff");
        Assert(landing.Observe(120,false)==YiGodhome.EntryFloorAdmission.Verdict.Waiting,"Previous room deadline leaked into new entry");

        bool Roof(bool paired=true,bool full=true,bool godhome=true,string scene="GG_Atrium",string owner="gg_roof_lever",string fsm="Switch Control",string state="Initiate",string variable="Activated")
            => YiGodhome.GodhomeRoofAccess.Applies(paired,full,godhome,scene,owner,fsm,state,variable);
        Assert(Roof(),"Full-access Godhome roof route was not authorized");
        Assert(!Roof(paired:false) && !Roof(full:false) && !Roof(godhome:false),"Roof override leaked into vanilla/normal Godhome/experimental mode");
        Assert(!Roof(scene:"GG_Atrium_Roof") && !Roof(owner:"other_lever") && !Roof(fsm:"Other")
            && !Roof(state:"Player Data?") && !Roof(variable:"SetPlayerData"),"Roof override escaped the exact native initial access check");

        // Reproduce native delayed deselection: first wait, then hide, then a
        // later input wait before optional focus restoration. Re-selection at
        // either wait must invalidate the obsolete continuation.
        for(int reselectAt=0;reselectAt<3;reselectAt++) {
            bool selected=true,hidden=false,restored=false,disposed=false;
            System.Collections.IEnumerator NativeDeselect() {
                try {yield return "end-of-frame";hidden=true;yield return "native-input-wait";restored=true;}
                finally {disposed=true;}
            }
            var guarded=YiGodhome.OwnedMenuDeselect.Guard(NativeDeselect(),()=>selected);
            Assert(guarded.MoveNext(),"Native deselection did not begin while old selection is still current");
            selected=reselectAt==0;
            bool second=guarded.MoveNext();
            if(reselectAt==0)Assert(!second && !hidden && disposed,"Deferred native hide erased reselected menu highlight");
            else {
                Assert(second && hidden,"Ordinary native deselection was suppressed");selected=reselectAt==1;
                Assert(!guarded.MoveNext() && disposed && restored==(reselectAt==2),"Late native continuation escaped its re-selection guard");
            }
        }
        var hold=new EntryImageHold();var identity=Guid.NewGuid();
        hold.Observe(identity,12,90,10,true,true);
        Assert(hold.Holds(identity,12,90,10.49),"Brief native entry gap lost opaque image");
        Assert(!hold.Holds(identity,12,90,10.501) && !hold.Holds(Guid.NewGuid(),12,90,10.2)
            && !hold.Holds(identity,13,90,10.2) && !hold.Holds(identity,12,91,10.2),"Entry image exceeded its time/identity lease");
        hold.Observe(identity,12,90,10.3,false,true);Assert(!hold.Holds(identity,12,90,10.31),"Ready gameplay retained entry hold");
        hold.Observe(identity,12,90,10.4,true,false);Assert(!hold.Holds(identity,12,90,10.41),"Transparent native blink borrowed opaque hold");
        hold.Observe(identity,12,90,12.1,true,true);Assert(!hold.Holds(identity,12,90,12.11),"Late combat reopened entry hold");
        hold.Observe(identity,13,91,20,true,true);Assert(hold.Holds(identity,13,91,20.1),"New room did not get independent bounded entry lease");
        Assert(YiGodhome.PureVesselTitleSource.Matches("GG_Hollow_Knight","Battle Scene","Title",true),"Installed Pure Vessel title rejected");
        Assert(!YiGodhome.PureVesselTitleSource.Matches("GG_Hollow_Knight","Battle Scene","Title",false)
            && !YiGodhome.PureVesselTitleSource.Matches("GG_Workshop","Battle Scene","Title",true)
            && !YiGodhome.PureVesselTitleSource.Matches("GG_Hollow_Knight","Area Title","Title",true),"Foreign Pure Vessel title admitted");
        MinionAtlas();
        var frame = Frame(Guid.NewGuid()); frame.ContentSequence = 7; frame.ContentFrame = 123;
        using var bytes = new MemoryStream(); VisualCodec.Write(bytes, frame);
        Assert(bytes.Length == VisualCodec.HeaderSize + frame.Rgba.Length, "Wire size drift");
        bytes.Position = 0; var copy = VisualCodec.Read(bytes);
        Assert(copy.Session == frame.Session && copy.Hash == frame.Hash && copy.ContentSequence == 7 && copy.ContentFrame == 123 && copy.Rgba.AsSpan().SequenceEqual(frame.Rgba), "Roundtrip changed sequence, source frame or pixels");
        byte[] data = bytes.ToArray();
        for (int i = 0; i < VisualCodec.HeaderSize; i++)
        { int length = i; Reject(() => VisualCodec.Read(new MemoryStream(data, 0, length))); }
        foreach (int length in new[] { VisualCodec.HeaderSize, data.Length / 2, data.Length - 1 })
            Reject(() => VisualCodec.Read(new MemoryStream(data, 0, length)));
        foreach (int offset in new[] { 0, 4, data.Length - 1 })
        {
            var bad = (byte[])data.Clone(); bad[offset] ^= 255; Reject(() => VisualCodec.Read(new MemoryStream(bad)));
        }
        // Reject dimensions from the header before allocating/reading the payload.
        var huge = (byte[])data.Clone(); BitConverter.GetBytes(int.MaxValue).CopyTo(huge, 94);
        Reject(() => VisualCodec.Read(new MemoryStream(huge, 0, VisualCodec.HeaderSize)));
        foreach (Action<VisualFrame> corrupt in new Action<VisualFrame>[] {
            f => f.Session = Guid.Empty, f => f.Sequence = 0, f => f.CaptureTick = f.SentTick + 1,
            f => f.Frequency = 0, f => f.Width = VisualCodec.MaxSide+1, f => f.WorldWidth = 0, f => f.X = float.NaN,
            f => f.PreviousRoundTripMs = double.PositiveInfinity, f => f.Hash++, f => f.ScreenFade = 0.5f,
            f => { f.Recovering = true; f.ScreenFade = float.NaN; }, f => { f.Recovering = true; f.ScreenFade = 1.01f; },
            f => { f.Recovering = true; f.ScreenFade = -0.01f; } })
        { var bad = Frame(Guid.NewGuid()); corrupt(bad); Reject(() => VisualCodec.Write(new MemoryStream(), bad)); }
        Console.WriteLine("PASS: bounded visual codec, exact pixels, checksum, malformed headers and truncations");
        var recovery = Frame(Guid.NewGuid()); recovery.Recovering = true; recovery.ScreenFade = 0.625f;
        using var recoveryBytes = new MemoryStream(); VisualCodec.Write(recoveryBytes, recovery); recoveryBytes.Position = 0;
        var recoveryCopy = VisualCodec.Read(recoveryBytes);
        Assert(recoveryCopy.Recovering && recoveryCopy.ScreenFade == 0.625f && recoveryCopy.NativeFrame == recovery.NativeFrame, "Recovery detached from native visual frame");
        Reject(() => VisualCodec.Write(new MemoryStream(), recovery, hud: true));
        var invalidFlag = recoveryBytes.ToArray(); invalidFlag[110] = 4;
        Reject(() => VisualCodec.Read(new MemoryStream(invalidFlag)));
        var controlsMenu=Frame(Guid.NewGuid());controlsMenu.ControlsMenu=controlsMenu.ControlsBinding=true;
        using var controlsBytes=new MemoryStream();VisualCodec.Write(controlsBytes,controlsMenu,hud:true);controlsBytes.Position=0;
        var menuCopy=VisualCodec.Read(controlsBytes,hud:true);
        Assert(menuCopy.ControlsMenu && menuCopy.ControlsBinding,"Controls capture identity was lost across the menu wire.");
        controlsBytes.Position=0;Reject(()=>VisualCodec.Read(controlsBytes));
        controlsMenu.Width=controlsMenu.Height=1;controlsMenu.Rgba=new byte[4];controlsMenu.Hash=VisualCodec.Checksum(controlsMenu.Rgba);
        Reject(()=>VisualCodec.Write(new MemoryStream(),controlsMenu,hud:true));
        Console.WriteLine("PASS: explicit Controls identity roundtrip; actor and closed-frame flag rejection");
        var expanded = Frame(Guid.NewGuid()); expanded.Width = expanded.Height = VisualCodec.MaxSide;
        expanded.Rgba = new byte[expanded.Width * expanded.Height * 4]; expanded.Rgba[expanded.Rgba.Length - 1] = 123;
        expanded.Hash = VisualCodec.Checksum(expanded.Rgba);
        using var largeBytes = new MemoryStream(); VisualCodec.Write(largeBytes, expanded); largeBytes.Position = 0;
        var largeCopy = VisualCodec.Read(largeBytes);
        Assert(largeCopy.Width == VisualCodec.MaxSide && largeCopy.Rgba[largeCopy.Rgba.Length - 1] == 123, "Expanded actor pixels changed");
        var oversizedHeader = largeBytes.ToArray(); BitConverter.GetBytes(VisualCodec.MaxSide+1).CopyTo(oversizedHeader, 94);
        Reject(() => VisualCodec.Read(new MemoryStream(oversizedHeader, 0, VisualCodec.HeaderSize)));
        Console.WriteLine("PASS: expanded actor frame and oversized actor rejection before allocation");
    }
    private static void Exchange()
    {
        var id = Guid.NewGuid(); using var hk = new VisualEndpoint(id, Role.HollowKnight); using var ns = new VisualEndpoint(id, Role.NineSols);
        var recoveryFrame = Frame(id); recoveryFrame.Recovering = true; recoveryFrame.ScreenFade = 1;
        ns.Publish(recoveryFrame); Assert(Until(() => hk.FreshFrame?.ScreenFade == 1), "No fresh recovery visual received");
        Assert(hk.FreshFrame!.Hash == Frame(id).Hash, "Pipe changed pixels");
        Thread.Sleep(280); Assert(hk.FreshFrame == null, "Stalled frame remained visible");
        var raw = Frame(id, 2); raw.Hash = 0; ns.PublishPixels(raw);
        Assert(Until(() => hk.FreshFrame?.NativeFrame == 2), "Fresh recovery failed");
        Assert(hk.FreshFrame!.Hash == VisualCodec.Checksum(raw.Rgba), "Worker checksum did not match captured pixels");
        ns.Dispose(); Assert(Until(() => hk.FreshFrame == null), "Disconnect retained image");
        Console.WriteLine("PASS: actual visual pipe, freshness expiry, recovery and peer-exit invalidation");
    }
    private static void PeerChecks(bool wrongSession, bool outOfOrder, double transferMs)
    {
        var id = Guid.NewGuid(); using var hk = new VisualEndpoint(id, Role.HollowKnight);
        using var peer = new NamedPipeClientStream(".", "YiGodhome." + id.ToString("N") + ".visual", PipeDirection.InOut);
        peer.Connect(5000); var frame = Frame(wrongSession ? Guid.NewGuid() : id, outOfOrder ? 2 : 1);
        VisualCodec.Write(peer, frame);
        if (wrongSession || outOfOrder)
        {
            Assert(Until(() => { while (hk.TryGetEvent(out string message)) if (message.Contains("VISUAL_STOP")) return true; return false; }), "Invalid peer was not rejected");
        }
        else
        {
            using var reader = new BinaryReader(peer, System.Text.Encoding.UTF8, true);
            using var writer = new BinaryWriter(peer, System.Text.Encoding.UTF8, true);
            Assert(reader.ReadInt64() == 1 && reader.ReadInt64() == frame.SentTick, "Invalid visual acknowledgement");
            writer.Write(transferMs); writer.Flush();
            Assert(Until(() => { while (hk.TryGetEvent(out string message)) if (message.Contains("VISUAL_RECEIVED")) return true; return false; }), "No receive evidence");
        }
        Assert(hk.FreshFrame == null, "Invalid/slow first frame was displayed");
    }
    private static void IndependentHud()
    {
        var id = Guid.NewGuid();
        using var hk = new VisualEndpoint(id, Role.HollowKnight);
        using var ns = new VisualEndpoint(id, Role.NineSols);
        using var hudHost = new VisualEndpoint(id, Role.HollowKnight, hud: true);
        using var hudNative = new VisualEndpoint(id, Role.NineSols, hud: true);
        ns.Publish(Frame(id)); var hud = Frame(id, 12);
        hud.Width = 1664; hud.Height = 720; hud.Rgba = new byte[hud.Width * hud.Height * 4]; hud.Rgba[12345] = 211; hud.Hash = VisualCodec.Checksum(hud.Rgba);
        hudNative.Publish(hud);
        Assert(Until(() => hk.FreshFrame != null && hudHost.FreshFrame != null), "Concurrent HUD/image channels did not connect");
        Assert(hk.FreshFrame!.NativeFrame == 1 && hudHost.FreshFrame!.NativeFrame == 12, "HUD crossed into actor frame channel");
        Assert(hudHost.FreshFrame!.Rgba[12345] == 211 && hudHost.FreshFrame.Width == 1664, "High-resolution HUD lost pixels");
        hudNative.Dispose(); Assert(Until(() => hudHost.FreshFrame == null), "Disconnected HUD remained visible");
        ns.Publish(Frame(id, 2)); Assert(Until(() => hk.FreshFrame?.NativeFrame == 2), "HUD teardown stopped actor channel");
    }
    private static void HudBounds()
    {
        var frame = Frame(Guid.NewGuid()); frame.Width = 1664; frame.Height = 720;
        frame.Rgba = new byte[frame.Width * frame.Height * 4]; frame.Hash = VisualCodec.Checksum(frame.Rgba);
        using var stream = new MemoryStream(); VisualCodec.Write(stream, frame, hud: true);
        stream.Position = 0; Assert(VisualCodec.Read(stream, hud: true).Width == 1664, "HUD dimensions changed");
        stream.Position = 0; Assert(VisualCodec.Read(stream).Width==1664,"Rectangular actor dimensions changed");
        var header = stream.ToArray();
        BitConverter.GetBytes(2048).CopyTo(header, 94); BitConverter.GetBytes(2048).CopyTo(header, 98);
        BitConverter.GetBytes(2048 * 2048 * 4).CopyTo(header, 102);
        Reject(() => VisualCodec.Read(new MemoryStream(header, 0, VisualCodec.HeaderSize), hud: true));
        Console.WriteLine("PASS: high-resolution HUD roundtrip, independent actor bounds, oversized HUD rejection before payload allocation");
    }
    private static int Main()
    {
        // Read-only installed level442 audit: GPZ's native main title lives
        // outside persistent AreaTitle; extra adjectives are separate children.
        Assert(YiGodhome.GreyPrinceTitleSource.Matches("GG_Grey_Prince_Zote","Grey Prince Title","Title Main","Title Small Main",true),"Installed GPZ title hierarchy rejected");
        Assert(!YiGodhome.GreyPrinceTitleSource.Matches("GG_Grey_Prince_Zote","Area Title","Title Small","Title Small Main",true),"Persistent AreaTitle admitted as GPZ title");
        for(int adjective=1;adjective<=13;adjective++)
            Assert(!YiGodhome.GreyPrinceTitleSource.Matches("GG_Grey_Prince_Zote",null,"Grey Prince Title","Extra Title "+adjective,true),"GPZ adjective authorized boss bar before main title");
        Assert(!YiGodhome.GreyPrinceTitleSource.Matches("GG_Workshop","Grey Prince Title","Title Main","Title Small Main",true)
            && !YiGodhome.GreyPrinceTitleSource.Matches("GG_Grey_Prince_Zote","Grey Prince Title","Title Main","Title Small Main",false),"Foreign room/nested title spoof admitted");
        Console.WriteLine("PASS: installed GPZ scene-root main title admitted independently of AreaTitle; persistent duplicate leaf, all13 adjectives and foreign/nested owners rejected.");
        var title=new BossTitleGate();
        Assert(!title.Ready(10),"Boss bar appeared before the title card");
        title.Reveal(10);Assert(title.Ready(10) && title.Ready(10),"Boss bar hid after title disappeared");
        Assert(!title.Ready(11),"Previous boss title released the next room bar");
        title.Reveal(11);title.Reveal(11);Assert(title.Ready(11),"Repeated native title reveal revoked bar");
        Assert(!new BossTitleGate().Ready(11),"New bridge inherited prior title authorization");
        var phaseTitle=new BossTitleGate();var batch=new BossBarBatch();
        var oro=new BossBarEntry();var mato=new BossBarEntry();int oroEnters=0,matoEnters=0;
        void BarSample(bool ready,int bars) {
            if(oro.Update(ready && bars>0))oroEnters++;
            if(mato.Update(ready && bars>1))matoEnters++;
        }
        BarSample(batch.Ready(BossTitleVariant.NailmasterOro,phaseTitle.Ready(30,BossTitleVariant.NailmasterOro),1),1);
        Assert(oroEnters==0 && matoEnters==0,"Entry animation ran before Oro title");
        phaseTitle.Reveal(30,BossTitleVariant.NailmasterOro);
        Assert(!phaseTitle.Ready(30,BossTitleVariant.NailmasterOro,false),"Native Enter ran while HK intro HUD remained hidden");
        BarSample(batch.Ready(BossTitleVariant.NailmasterOro,phaseTitle.Ready(30,BossTitleVariant.NailmasterOro),1),1);
        BarSample(true,1);Assert(oroEnters==1 && matoEnters==0,"Oro native entrance duplicated or Mato appeared in phase one");
        BarSample(false,0); // Oro defeated: no active bar.
        Assert(!phaseTitle.Ready(30,BossTitleVariant.BrothersOroMato),"First title admitted combined phase");
        BarSample(batch.Ready(BossTitleVariant.BrothersOroMato,false,1),1);
        BarSample(batch.Ready(BossTitleVariant.BrothersOroMato,false,2),2);
        Assert(oroEnters==1 && matoEnters==0,"Bars entered before combined title");
        phaseTitle.Reveal(30,BossTitleVariant.BrothersOroMato);
        bool combined=phaseTitle.Ready(30,BossTitleVariant.BrothersOroMato);
        Assert(!batch.Ready(BossTitleVariant.BrothersOroMato,combined,1),"Combined phase flashed one bar while second target prepared");
        BarSample(batch.Ready(BossTitleVariant.BrothersOroMato,combined,2),2);
        Assert(oroEnters==2 && matoEnters==1,"Both bars did not start entrance together at the combined title");
        BarSample(batch.Ready(BossTitleVariant.BrothersOroMato,true,2),2);
        Assert(oroEnters==2 && matoEnters==1,"Repeated snapshots restarted native Enter");
        Assert(batch.Ready(BossTitleVariant.BrothersOroMato,true,1),"A legitimate brother death hid the surviving bar");
        Assert(phaseTitle.Ready(30,BossTitleVariant.BrothersOroMato,false),"Later native HUD hide revoked the title lease");
        Console.WriteLine("PASS: Oro title starts one native entrance; defeat hides bars; combined title waits for both and replays retained Oro/native Mato entrances once together, retaining survivor afterward.");
        Console.WriteLine("PASS: boss bar waits for native title reveal, stays after title dismissal and resets for next room/new bridge.");

        foreach(string hidden in new[]{"","Init","Out","Go Out Instant"})
            Assert(!NativeHudVisibility.Shown(true,hidden,1,1),"Native offscreen HUD leaked into Yi overlay");
        foreach(string visible in new[]{"Idle","Come In","Go Out"}) {
            Assert(NativeHudVisibility.Shown(true,visible,1,1) && NativeHudVisibility.Shown(true,visible,.5f,.5f),"Native HUD tween was replaced by a guessed boss delay");
            Assert(!NativeHudVisibility.Shown(false,visible,1,1) && !NativeHudVisibility.Shown(true,visible,0,0),"Inactive/fully shrunken native HUD remained visible");
        }
        Assert(!NativeHudVisibility.Shown(true,"Idle",float.NaN,1) && !NativeHudVisibility.Shown(true,"Idle",1,float.PositiveInfinity),"Invalid native HUD scale admitted");
        Console.WriteLine("PASS: native HUD states/activity/scale govern visibility, including intro, immediate hide, tween, repeated hide/show and invalid scale.");
        MatteExactAndCost();
        HudMatteBursts();
        WorldMatteBursts();
        Assert(!DoorWalkAdmission.AtEntrance(-5067.125f,-5140.125f),"Cold destination transform skipped native door walk");
        Assert(DoorWalkAdmission.AtEntrance(-5140.125f,-5140.125f),"Settled native entrance rejected");
        Assert(DoorWalkAdmission.AtEntrance(10.125f,10.1f),"Eighth-unit native placement tolerance rejected");
        Assert(!DoorWalkAdmission.AtEntrance(float.NaN,10) && !DoorWalkAdmission.AtEntrance(10,float.PositiveInfinity),"Invalid placement released door walk");
        Console.WriteLine("PASS: cold door walk waits for actual entrance placement rather than treating old destination as arrival.");
        DualMatte();
        var entry=new ActorEntryPixels();var cold=Frame(Guid.NewGuid());cold.Rgba=new byte[cold.Rgba.Length];
        Assert(!entry.Observe(cold,10),"Transparent first arena image released readiness");
        cold.Rgba[3]=255;Assert(!entry.Observe(cold,10),"One effect speck released actor readiness");
        for(int i=3;i<32*4;i+=4)cold.Rgba[i]=255;
        Assert(entry.Observe(cold,10),"Actual first actor artwork never released readiness");
        Array.Clear(cold.Rgba,0,cold.Rgba.Length);
        Assert(entry.Observe(cold,10),"Native post-entry blink reacquired input/readiness gates");
        Assert(!entry.Observe(cold,11),"Repeated arena scene reused previous entry readiness");
        for(int i=3;i<32*4;i+=4)cold.Rgba[i]=255;Assert(entry.Observe(cold,11),"Repeated entry did not recover");
        Array.Clear(cold.Rgba,0,cold.Rgba.Length);cold.GeometryFrame++;
        Assert(!entry.Observe(cold,11),"New room geometry inherited old image readiness");
        for(int i=3;i<32*4;i+=4)cold.Rgba[i]=255;Assert(entry.Observe(cold,11),"New geometry failed first image");
        Array.Clear(cold.Rgba,0,cold.Rgba.Length);cold.Session=Guid.NewGuid();
        Assert(!entry.Observe(cold,11),"Foreign session inherited actor readiness");
        Console.WriteLine("PASS: first actor image needs opaque artwork per room/session/geometry; later native blinking never re-gates controls.");
        try { SeparateHudEffects(); RootMenuIdentity(); ThunderCoverage(); WorldArrowPlacement(); OwnerCropSafety(); ExpandedPoolReuse();ActorWorkingSetBudget(); ExpandedHoldSafety(); ReturnPixels(); HudAlphaWorker(); SeparateChannels(); Codec(); Placement();ContinuousMotion(); HudBounds(); Exchange(); IndependentHud(); PooledExchange(); PeerChecks(true, false, 0); PeerChecks(false, true, 0); PeerChecks(false, false, 300);
            Console.WriteLine("PASS: wrong session, sequence gap and slow first-transfer rejection"); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void DualMatte()
    {
        // Native RGB-only output: transparent background, opaque black core,
        // opaque red rim, then half-covered red. Alpha from the RT is unusable.
        byte[] black = { 0,0,0,0, 0,0,0,0, 255,0,0,0, 128,0,0,0 };
        byte[] white = { 255,255,255,0, 0,0,0,0, 255,0,0,0, 255,127,127,0 };
        ActorPixels.FromDualMatte(black,white);
        Assert(black[3]==0 && black[7]==255 && black[8]==255 && black[11]==255
            && black[12]==255 && black[13]==0 && black[15]==128, "Native RGB matte coverage/color lost");
        bool rejected=false;
        try { ActorPixels.FromDualMatte(black,black); } catch(ArgumentException) { rejected=true; }
        Assert(rejected,"Aliased native matte inputs admitted");
        // Captured native LUT sample: white clips its channel attenuation.
        // Dark-scene reconstruction must preserve the native64-grey output.
        byte[] darkBlack={46,9,0,255, 0,0,0,255, 120,60,30,255};
        byte[] darkReference={82,36,28,255, 64,64,64,255, 120,60,30,255};
        ActorPixels.FromDualMatte(darkBlack,darkReference,true,64);
        Assert(darkBlack[3]>=147 && darkBlack[7]==0 && darkBlack[11]==255,"Native dark-reference coverage or ordinary UI changed");
        for(int c=0;c<3;c++) {
            int composite=(darkBlack[c]*darkBlack[3]+64*(255-darkBlack[3])+127)/255;
            Assert(Math.Abs(composite-darkReference[c])<=1,"Native hurt border colour lost on dark scene");
        }
        Console.WriteLine("PASS: RGB-only native material recovers transparent coverage, opaque black/red and partial red without double premultiplication.");
    }
    private static void ReturnPixels()
    {
        var premultiplied = new byte[] { 64, 32, 16, 128, 7, 8, 9, 255, 10, 11, 12, 0 };
        ActorPixels.StraightAlpha(premultiplied);
        Assert(premultiplied[0] == 128 && premultiplied[1] == 64 && premultiplied[2] == 32 && premultiplied[3] == 128,
            "Transparent capture RGB was multiplied twice");
        Assert(premultiplied[4] == 7 && premultiplied[8] == 10 && premultiplied[11] == 0, "Opaque/zero-alpha pixel semantics changed");
        var rgba = new byte[256 * 256 * 4];
        for (int i = 0; i < rgba.Length; i += 4) { rgba[i] = 255; rgba[i + 1] = 255; rgba[i + 2] = 255; }
        Assert(!ActorPixels.Visible(rgba), "Transparent RGB artwork revealed returned room");
        for (int i = 0; i < 31; i++) rgba[i * 4 + 3] = 255;
        Assert(!ActorPixels.Visible(rgba), "A few opaque specks exposed returned room");
        rgba[31 * 4 + 3] = 128;
        Assert(ActorPixels.Visible(rgba), "Visible returned artwork remained hidden");
        Array.Clear(rgba);
        Assert(!ActorPixels.Visible(rgba), "A later blank image inherited prior visibility");
        Console.WriteLine("PASS: return reveal requires actual opaque pixels; transparent RGB, specks and stale visibility are rejected.");
    }
    private static void ContinuousMotion() {
        var image=Frame(Guid.NewGuid());image.AnchorX=10;image.AnchorY=22;image.X=8;image.Y=21;
        image.Motion=new Pose(image.Session,image.GeometryFrame,10,22,8,0,.5f,1.5f,true);
        var track=new VisualMotionContinuity();
        for(int step=0;step<80;step++) {
            var control=new Pose(image.Session,image.GeometryFrame,10+step*.1f,22,8,0,.5f,1.5f,true);
            var pose=track.Sample(image,control,image.NativeFrame+step)!;
            VisualPlacement.Resolve(image,pose,out var x,out var y);
            Assert(Math.Abs(x-(8+step*.1f))<.0001 && y==21,"Delayed arrow image rewound continuous native motion at the old cutoff");
        }
        var old=new Pose(image.Session,image.GeometryFrame,10,22,8,0,.5f,1.5f,true);
        Assert(track.Sample(image,old,image.NativeFrame)!.X>17,"Older metadata rewound displayed movement");
        var warp=new Pose(image.Session,image.GeometryFrame,100,22,0,0,.5f,1.5f,true);
        long warpedFrame=image.NativeFrame+90;
        Assert(track.Sample(image,warp,warpedFrame)==null,"Teleport dragged pre-teleport pixels");
        Assert(track.Sample(image,warp,warpedFrame+1)==null,"Teleport wait expired before matching pixels");
        image.NativeFrame=warpedFrame;image.AnchorX=100;image.X=98;image.Motion=warp;
        Assert(track.Sample(image,warp,warpedFrame+2)!.X==100,"Fresh post-teleport image failed to resume native motion");
        image.Recovering=true;
        Assert(track.Sample(image,old,warpedFrame+3)!.X==100,"Recovery detached from its captured image");
        Assert(track.Sample(image,null,warpedFrame+4)==null,"Stale control retained active movement");
        Console.WriteLine("PASS: continuous native sprint survives delayed arrow images without rewinding; old samples, teleports, recovery and stale controls remain guarded.");
    }
    private static void Placement()
    {
        var f = Frame(Guid.NewGuid());
        var control = new Pose(f.Session, f.GeometryFrame, 10, 22, 0, 0, 0.5f, 1.5f, true);
        for (int i = 0; i < 4; i++)
        {
            f.AnchorX = 10 + i * 0.06f; f.AnchorY = 22 - i * 0.14f;
            f.X = f.AnchorX - 2; f.Y = f.AnchorY - 1;
            f.Motion = new Pose(f.Session, f.GeometryFrame, f.AnchorX, f.AnchorY, 8, -20, 0.5f, 1.5f, false);
            using var bytes = new MemoryStream(); VisualCodec.Write(bytes, f); bytes.Position = 0;
            var copy = VisualCodec.Read(bytes);
            var motion = VisualPlacement.Sample(copy, control)!;
            Assert(ReferenceEquals(motion,control), "Live native movement remained coupled to image cadence");
            VisualPlacement.Resolve(copy, motion, out float x, out float y);
            Assert(Math.Abs(x-(f.X+control.X-f.AnchorX)) < .0001 && Math.Abs(y-(f.Y+control.Y-f.AnchorY)) < .0001, "Live native anchor correction lost an axis");
            Assert(VisualPlacement.Sample(copy,control,copy.NativeFrame-1)!.Y == f.AnchorY, "Older control sample moved image backwards");
            var teleport = new Pose(f.Session,f.GeometryFrame,1000,1000,0,0,.5f,1.5f,true);
            Assert(VisualPlacement.Sample(copy,teleport)!.Y == f.AnchorY, "Unrelated teleport dragged old image");
            for (int step=0;step<5;step++) {
                var live = new Pose(f.Session,f.GeometryFrame,f.AnchorX+step*.04f,f.AnchorY+step*.03f,8,6,.5f,1.5f,false);
                var fresh = VisualPlacement.Sample(copy,live,copy.NativeFrame+step)!;
                VisualPlacement.Resolve(copy,fresh,out var sx,out var sy);
                Assert(Math.Abs(sx-(f.X+step*.04f))<.0001 && Math.Abs(sy-(f.Y+step*.03f))<.0001,"Held arrow image froze or quantized live diagonal movement");
            }
            copy.Recovering = true; copy.ScreenFade = 1;
            Assert(VisualPlacement.Sample(copy, control)!.Y == f.AnchorY, "Recovery sample detached from image");
            Assert(VisualPlacement.Sample(copy, null) == null, "Stale control admitted image motion");
            Assert(VisualPlacement.Sample(copy, new Pose(Guid.NewGuid(), f.GeometryFrame, 0, 0, 0, 0, 1, 1, true)) == null,
                "Foreign session admitted image motion");
            Assert(VisualPlacement.Sample(copy, new Pose(f.Session, f.GeometryFrame + 1, 0, 0, 0, 0, 1, 1, true)) == null,
                "Old room admitted image motion");
        }
        f.Motion = new Pose(f.Session, f.GeometryFrame, f.AnchorX, f.AnchorY, float.NaN, 0, 1, 1, false);
        Reject(() => VisualCodec.Write(new MemoryStream(), f));
        f.Motion = null; f.AnchorY = float.NaN; Reject(() => VisualCodec.Write(new MemoryStream(), f));
        Console.WriteLine("PASS: live diagonal/horizontal movement remains independent of held image cadence; older samples, teleports, recovery and foreign/stale identities remain guarded");
    }
    private static void PooledExchange()
    {
        var id = Guid.NewGuid(); using var hk = new VisualEndpoint(id, Role.HollowKnight); using var ns = new VisualEndpoint(id, Role.NineSols);
        void Publish(int n)
        {
            var f = ns.RentFrame(1024); f.Session = id; f.NativeFrame = n; f.CaptureTick = Stopwatch.GetTimestamp();
            f.GeometryFrame = 50; f.Width = f.Height = 16; f.WorldWidth = f.WorldHeight = 8;
            for (int i = 0; i < f.Rgba.Length; i++) f.Rgba[i] = (byte)(i + n);
            ns.PublishPixels(f);
        }
        Publish(1); Assert(Until(() => hk.FreshFrame?.NativeFrame == 1), "Pooled initial frame missing");
        using (var lease = hk.AcquireFrame())
        {
            var f = lease.Frame!; uint expected = VisualCodec.Checksum(f.Rgba);
            var producer = System.Threading.Tasks.Task.Run(() => { for (int i = 2; i <= 100; i++) Publish(i); });
            Assert(producer.Wait(2000), "Pixel producer blocked on consumer lease");
            Thread.Sleep(30);
            Assert(VisualCodec.Checksum(f.Rgba) == expected, "Held pixels recycled under consumer");
        }
        Assert(Until(() => hk.FreshFrame?.NativeFrame == 100), "Latest pooled frame lost after slow consumer");
        for (int n = 101; n <= 150; n++)
        {
            Publish(n); Assert(Until(() => hk.FreshFrame?.NativeFrame == n), "Pooled frame delivery failed");
            using var lease = hk.AcquireFrame();
            Assert(lease.Frame!.Rgba[700] == (byte)(700 + n) && VisualCodec.Checksum(lease.Frame.Rgba) == lease.Frame.Hash, "Reused buffer changed pixels");
        }
        Assert(ns.PixelAllocations <= 4 && hk.PixelAllocations <= 3, "Pixel buffers were not reused");
        ns.Dispose(); hk.Dispose();
        Assert(Until(() => ns.OutstandingPixels == 0 && hk.OutstandingPixels == 0), "Teardown leaked pooled frame ownership");
        Console.WriteLine("PASS: bounded pixel reuse, unsent replacement, slow consumer lease, exact checksums and teardown ownership");
    }
    private static void ActorWorkingSetBudget()
    {
        // A homing burst grows through many cold tiers before repeatedly using
        // its largest image. Cold singleton retention must not starve the hot
        // producer/worker leases and allocate another26MiB every other frame.
        var burst = new PixelBuffers();
        int[] cold = {768*768*4,1024*1024*4,1536*1024*4,1536*2048*4,2048*2048*4,2048*2560*4};
        foreach(int size in cold) {var bytes=burst.Rent(size);burst.Return(bytes);}
        var active = new byte[4][];
        for(int i=0;i<active.Length;i++)active[i]=burst.Rent(2560*2560*4);
        foreach(var bytes in active)burst.Return(bytes);
        int prepared=burst.Created;
        // Three in-flight native frames/worker leases, then one free return.
        for(int i=0;i<3;i++)active[i]=burst.Rent(2560*2560*4);
        for(int i=0;i<120;i++) {
            var next=burst.Rent(2560*2560*4);
            burst.Return(active[i%3]);active[i%3]=next;
            Assert(burst.RetainedBytes<=128L*1024*1024,"Homing burst exceeded retained budget");
        }
        foreach(var bytes in active.AsSpan(0,3))burst.Return(bytes);
        Assert(burst.Created<=prepared+1,"Cold tiers starved hot homing buffers during sustained capture");
        Assert(burst.Outstanding==0,"Homing burst leaked native image leases");
        Console.WriteLine("PASS: cold arrow tiers cannot force repeated26MiB allocations during120 full-size frames.");
        var pool = new PixelBuffers();
        int[] lengths = { 768*768*4, 1024*1024*4, 2560*2560*4, 1536*1536*4,
            2048*1536*4, 1536*768*4, 2048*2048*4, 2560*1536*4, 2560*2048*4 };
        // Exact tiers observed in the Ascended Vengefly trace. One received
        // image remains leased while the next arrives, including a large-to-
        // small-to-large burst. No captured game data is embedded here.
        byte[]? displayed = null;
        for (int cycle = 0; cycle < 3; cycle++) foreach (int size in lengths) {
            for (int image = 0; image < 3; image++) {
                var next = pool.Rent(size); next[0] = (byte)cycle;
                if (displayed != null) pool.Return(displayed);
                displayed = next;
                Assert(pool.RetainedBytes <= 128L*1024*1024, "Actor working set exceeded original budget");
            }
        }
        long allocated = pool.AllocatedBytes;
        for (int cycle = 0; cycle < 8; cycle++) foreach (int size in lengths) {
            for (int image = 0; image < 3; image++) {
                var next = pool.Rent(size);
                Assert(displayed![0] == (byte)(cycle == 0 && size == lengths[0] && image == 0 ? 2 : 11), "Trim reused a displayed image lease");
                next[0] = 11; pool.Return(displayed); displayed = next;
                Assert(pool.RetainedBytes <= 128L*1024*1024, "Actor pool budget grew during reuse");
            }
        }
        pool.Return(displayed!);
        // This deliberately oversized concurrency working set cannot retain two
        // buffers for every tier inside128MiB. Legacy whole-bucket eviction
        // allocates198 buffers for this297-image sequence; trim-spares-first
        // must improve that without pretending zero allocation is possible.
        Assert(pool.Created < 150 && pool.AllocatedBytes > allocated, "Actor spare trimming did not reduce the legacy198-buffer churn");
        Assert(pool.Outstanding == 0 && pool.EvictedBytes > 0, "Actor trim failed to retire spare copies safely");
        Console.WriteLine("PASS: nine full-size actor tiers preserve displayed leases; " + pool.Created + " buffers versus legacy198, within original128MiB retained budget (oversized working set still allocates).");
    }
    private static void MatteReference(byte[] black,byte[] white,bool preserve,int background)
    {
        for(int i=0;i<black.Length;i+=4) {
            if(black[i]==0 && black[i+1]==0 && black[i+2]==0 && white[i]==background && white[i+1]==background && white[i+2]==background){black[i+3]=0;continue;}
            if(black[i]==white[i] && black[i+1]==white[i+1] && black[i+2]==white[i+2]){black[i+3]=255;continue;}
            int transparency=0;
            for(int c=0;c<3;c++)transparency+=Math.Max(0,Math.Min(background,white[i+c]-black[i+c]))*255/background;
            int alpha=255-(transparency+1)/3;
            if(preserve)for(int c=0;c<3;c++)alpha=Math.Max(alpha,Math.Max(black[i+c],255-Math.Max(0,Math.Min(background,white[i+c]-black[i+c]))*255/background));
            for(int c=0;c<3;c++)black[i+c]=alpha==0?(byte)0:(byte)Math.Max(0,Math.Min(255,((preserve && background<255?white[i+c]*255-background*(255-alpha):black[i+c]*255)+alpha/2)/alpha));
            black[i+3]=(byte)alpha;
        }
    }
    private static void MatteExactAndCost()
    {
        var random=new Random(718);var input=new byte[65536*4];var white=new byte[input.Length];random.NextBytes(input);random.NextBytes(white);
        foreach(int background in new[]{1,17,64,127,255})foreach(bool preserve in new[]{false,true}) {
            var expected=(byte[])input.Clone();var actual=(byte[])input.Clone();
            MatteReference(expected,white,preserve,background);ActorPixels.FromDualMatte(actual,white,preserve,background);
            for(int i=0;i<actual.Length;i++)Assert(actual[i]==expected[i],"Optimized matte changed native coverage/colour rounding");
        }
        var exhaustive=new byte[256*256*4];
        for(int a=0;a<256;a++)for(int v=0;v<256;v++){int i=(a*256+v)*4;exhaustive[i]=exhaustive[i+1]=exhaustive[i+2]=(byte)v;exhaustive[i+3]=(byte)a;}
        ActorPixels.StraightAlpha(exhaustive);
        for(int a=0;a<256;a++)for(int v=0;v<256;v++)Assert(exhaustive[(a*256+v)*4]==(a==0||a==255?v:Math.Min(255,(v*255+a/2)/a)),"Straight-alpha table changed exact byte semantics");
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int n=0;n<10;n++){Buffer.BlockCopy(input,0,exhaustive,0,input.Length);ActorPixels.FromDualMatte(exhaustive,white,true,64);}
        Assert(GC.GetAllocatedBytesForCurrentThread()==before,"Warm matte conversion allocated per-pixel scratch");
        var full=new byte[1920*1080*4];var reference=new byte[full.Length];var work=new byte[full.Length];random.NextBytes(full);random.NextBytes(reference);
        var clock=Stopwatch.StartNew();
        for(int n=0;n<3;n++){Buffer.BlockCopy(full,0,work,0,full.Length);MatteReference(work,reference,true,64);}
        double old=clock.Elapsed.TotalMilliseconds/3;clock.Restart();
        for(int n=0;n<3;n++){Buffer.BlockCopy(full,0,work,0,full.Length);ActorPixels.FromDualMatte(work,reference,true,64);}
        Console.WriteLine($"PASS: matte exactness across655360 randomized pixels/five backgrounds, all65536 alpha/value pairs, zero warm allocations; offline1080p scalar={old:F2}ms optimized={clock.Elapsed.TotalMilliseconds/3:F2}ms (not a native Unity timing).");
    }
    private static void HudMatteBursts()
    {
        // Two readback mattes, two pending, two worker-owned. A stalled worker
        // can drain all six before the next burst, exposing a three-free cap.
        foreach(int cap in new[]{3,6}) {
            var pool=new PixelBuffers(64*1024,cap);
            var held=new byte[6][];
            for(int i=0;i<6;i++)held[i]=pool.Rent(8192);
            foreach(var bytes in held)pool.Return(bytes);
            int warmed=pool.Created;
            for(int cycle=0;cycle<120;cycle++) {
                for(int i=0;i<6;i++){held[i]=pool.Rent(8192);held[i][0]=(byte)i;}
                for(int i=0;i<6;i++)Assert(held[i][0]==i,"HUD matte worker/readback buffers alias");
                foreach(var bytes in held)pool.Return(bytes);
                Assert(pool.Outstanding==0 && pool.RetainedBytes<=64*1024,"HUD pool exceeded budget or leaked leases");
            }
            Assert(cap==3?pool.Created>warmed:pool.Created==warmed,"HUD six-buffer burst policy failed its allocation regression");
        }
        Console.WriteLine("PASS: six-lease HUD matte bursts reproduce three-free allocation churn; HUD-specific six-free policy reuses120 bursts without new buffers, aliasing or budget growth.");
    }
    private static void WorldMatteBursts()
    {
        foreach(int cap in new[]{3,6,8}) {
            var control=new PixelBuffers(128*1024,cap);
            var buffers=new byte[8][];
            for(int i=0;i<8;i++)buffers[i]=control.Rent(8192);
            foreach(var bytes in buffers)control.Return(bytes);
            int before=control.Created;
            for(int cycle=0;cycle<120;cycle++) {
                for(int i=0;i<8;i++)buffers[i]=control.Rent(8192);
                foreach(var bytes in buffers)control.Return(bytes);
            }
            Assert(cap==8?control.Created==before:control.Created>before,"World matte control failed to reproduce undersized retention");
        }
        // Two simultaneous GPU matte pairs + pending pair + worker pair.
        // Exercise the production arrow endpoint, not just a standalone pool.
        using var endpoint=new VisualEndpoint(Guid.NewGuid(),Role.NineSols,arrows:true);
        var held=new VisualFrame[8];
        for(int i=0;i<8;i++)held[i]=endpoint.RentFrame(1536*1536*4);
        foreach(var frame in held)frame.ReleasePixels();
        int warmed=endpoint.PixelAllocations;
        for(int cycle=0;cycle<120;cycle++) {
            for(int i=0;i<8;i++){held[i]=endpoint.RentFrame(1536*1536*4);held[i].Rgba[0]=(byte)i;}
            for(int i=0;i<8;i++)Assert(held[i].Rgba[0]==i,"Overlapping world mattes alias");
            foreach(var frame in held)frame.ReleasePixels();
            Assert(endpoint.OutstandingPixels==0 && endpoint.RetainedPixelBytes<=128L*1024*1024,"World matte lease/budget violation");
        }
        Assert(endpoint.PixelAllocations==warmed,"World matte burst still discards hot buffers");
        // Maximum-size idle returns may not all fit; the byte bound wins.
        for(int i=0;i<8;i++)held[i]=endpoint.RentFrame(2560*2560*4);
        foreach(var frame in held)frame.ReleasePixels();
        Assert(endpoint.OutstandingPixels==0 && endpoint.RetainedPixelBytes<=128L*1024*1024,"Maximum world matte burst escaped free-byte bound");
        Console.WriteLine("PASS: production world-effects endpoint reuses120 overlapping eight-lease1536px matte bursts; maximum2560px returns stay within128MiB.");
    }
    private static void ExpandedPoolReuse()
    {
        var pool=new PixelBuffers();
        // The player trace reaches ten exact actor lengths. The previous pool
        // permanently refused every size first seen after its eighth bucket.
        for(int size=1;size<=10;size++){var bytes=pool.Rent(size*4096);pool.Return(bytes);}
        int warmed=pool.Created;
        for(int cycle=0;cycle<50;cycle++) {
            var bytes=pool.Rent(9*4096);bytes[bytes.Length-1]=123;pool.Return(bytes);
            bytes=pool.Rent(10*4096);Assert(bytes.Length==10*4096,"Expanded pool changed exact wire length");pool.Return(bytes);
        }
        Assert(pool.Created==warmed,"Ninth/tenth actor lengths allocate on every tracking-arrow frame");
        Assert(pool.Outstanding==0,"Expanded pool leaked leases");
        var bounded=new PixelBuffers(64*1024);
        var held=bounded.Rent(8192);held[0]=91;
        for(int size=1;size<=40;size++){var bytes=bounded.Rent(4096+size*128);bounded.Return(bytes);Assert(bounded.RetainedBytes<=64*1024,"LRU exceeded retained byte budget");}
        Assert(held[0]==91 && bounded.Outstanding==1,"LRU eviction reused an outstanding frame");bounded.Return(held);
        Console.WriteLine("PASS: ninth/tenth actor lengths reuse after warmup instead of repeated large allocations.");
    }
}
