using System;
using System.Linq;
using System.IO;
using YiGodhome.Geometry;

static class Program
{
    static void AtomicPublication()
    {
        string root = Path.Combine(Path.GetTempPath(), "YiGodhomePublication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "geometry.bin");
        byte[] old = { 1, 2, 3 }, next = { 4, 5, 6, 7 };
        double time = 0;
        var messages = new System.Collections.Generic.List<string>();
        var publication = new SnapshotPublication(() => time);
        try {
            publication.Publish(path, old, old.Length, messages.Add);
            using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                bool renamed = true;
                try { publication.Publish(path, next, next.Length, messages.Add); }
                catch (SnapshotPublicationDeferredException) { renamed = false; }
                Require(reader.ReadByte() == 1, "Atomic rename mutated existing reader bytes");
                Require(File.ReadAllBytes(path).SequenceEqual(renamed ? next : old), "Reader observed partial replacement");
            }
            publication.Publish(path, next, next.Length, messages.Add);
            Require(File.ReadAllBytes(path).SequenceEqual(next), "Replacement did not complete after reader close");
            File.WriteAllBytes(path, old);
            using (var blocker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                try { publication.Publish(path, next, next.Length, messages.Add); throw new Exception("Locked publication succeeded"); }
                catch (SnapshotPublicationDeferredException) { }
                Require(watch.ElapsedMilliseconds < 100, "Publication blocked rather than deferring");
                Require(File.ReadAllBytes(path).SequenceEqual(old), "Failed rename damaged previous snapshot");
                time = .5;
                try { publication.Publish(path, next, next.Length, messages.Add); throw new Exception("Retry ignored sharing lock"); }
                catch (SnapshotPublicationDeferredException) { }
                Require(Directory.GetFiles(root, "*.pending-*").Length == 1, "Retries accumulated pending snapshots");
            }
            publication.Publish(path, next, next.Length, messages.Add);
            Require(File.ReadAllBytes(path).SequenceEqual(next) && messages.Any(m => m.Contains("RECOVERED")), "Publication did not recover after lock release");
            Require(Directory.GetFiles(root, "*.pending-*").Length == 0, "Successful rename leaked pending snapshot");
            using (var blocker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                time = 2;
                try { publication.Publish(path, old, old.Length, messages.Add); throw new Exception("Persistent lock succeeded"); }
                catch (SnapshotPublicationDeferredException) { }
                time = 3.01;
                bool terminal = false;
                try { publication.Publish(path, old, old.Length, messages.Add); }
                catch (SnapshotPublicationDeferredException) { throw new Exception("Publication retried beyond its finite deadline"); }
                catch (IOException) { terminal = true; }
                Require(terminal && File.ReadAllBytes(path).SequenceEqual(next), "Expired publication did not fail safely");
            }
            Require(messages.Any(m => m.Contains("win32=") && m.Contains("targetAttributes=")) && messages.Any(m => m.Contains("PUBLICATION_FAILED")), "Publication diagnostics lost native failure evidence");
            publication.CancelPending(messages.Add);
            using (var blocker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                time = 20;
                try { publication.Publish(path, old, old.Length, messages.Add); throw new Exception("Sharing lock ignored after cancellation"); }
                catch (SnapshotPublicationDeferredException) { }
                publication.CancelPending(messages.Add);
                Require(Directory.GetFiles(root, "*.pending-*").Length == 0, "Cancelled refresh left pending geometry");
                time = 40;
                try { publication.Publish(path, old, old.Length, messages.Add); throw new Exception("Sharing lock ignored after refresh reset"); }
                catch (SnapshotPublicationDeferredException) { }
            }
            publication.CancelPending(messages.Add);
            bool invalid = false;
            try { publication.Publish(Path.Combine(root, "absent", "file"), old, old.Length, messages.Add); }
            catch (SnapshotPublicationDeferredException) { throw new Exception("Unexpected filesystem error was retried"); }
            catch (IOException) { invalid = true; }
            Require(invalid, "Unexpected filesystem error was swallowed");
            Console.WriteLine("PASS: native atomic publication preserves old readers/bytes, defers sharing locks without sleeps, recovers, bounds pending files/deadline and records native failures.");
        }
        finally { Directory.Delete(root, true); }
    }
    static void WallContactGrid() {
        foreach(string scene in new[]{"GG_Workshop","GG_Atrium","GG_Ghost_Markoth","GG_Uumuu","GG_Flukemarm"})
        foreach(var wall in RoomFeatures.ClimbingWalls(scene)) {
            float left=wall.SolidCenter-WallRunStrip.Thickness*.5f,right=wall.SolidCenter+WallRunStrip.Thickness*.5f;
            var shape=new Shape {Closed=true,Points=new[]{new Point(left,wall.Bottom),new Point(right,wall.Bottom),new Point(right,wall.Top),new Point(left,wall.Top)}};
            Require(wall.Matches(shape),"Authored wall strip was not recognized");
            var original=shape.Points.ToArray();
            for(int n=0;n<256;n++) {
                var map=new CoordinateMap(new Point(4.7391f+n*.011f,5),new Point(-5084.75f+n*.017f,-3228),20);
                float shift=wall.NativeContactOffset(shape,map);
                float native=map.ToTarget(new Point(wall.Facing>0?right:left,0)).X,aligned=native+shift;
                Require(Math.Abs(aligned*8-Math.Round(aligned*8))<.001,"Room-anchor fraction leaked into wall contact");
                Require(Math.Abs(shift)<.1251f && shift*wall.Facing<=.0001f,"Grid alignment moved the wall into Yi or beyond one native eighth");
                float low=map.ToTarget(new Point(left,0)).X+shift,high=map.ToTarget(new Point(right,0)).X+shift;
                Require(Math.Abs(high-low-WallRunStrip.Thickness*map.Scale)<.002,"Wall alignment changed backing thickness");
                float zone=map.ToTarget(new Point(wall.ContactX+wall.Facing*.3f,0)).X+shift;
                Require(Math.Abs(zone-aligned-wall.Facing*.3f*map.Scale)<.002,"Wall-run enable zone detached from aligned face");
                Require(shape.Points.SequenceEqual(original),"Native alignment mutated host geometry");
            }
        }
        Console.WriteLine("PASS: all marked runnable faces align across256 fractional entry anchors in both directions; backing, trigger offset and host geometry remain intact.");
    }
    private sealed class ShortRead : MemoryStream {
        internal ShortRead(byte[] bytes) : base(bytes) { }
        public override int Read(byte[] buffer,int offset,int count) => base.Read(buffer,offset,Math.Min(count,3));
    }
    static void SnapshotComparison() {
        byte[] expected = Enumerable.Range(0,10003).Select(i => (byte)i).ToArray(), scratch = new byte[4096];
        using var exact = new ShortRead(expected);
        Require(SnapshotContent.Matches(exact,expected,expected.Length,scratch), "Short reads lost matching snapshot bytes");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) Require(SnapshotContent.Matches(exact,expected,expected.Length,scratch), "Reusable comparison changed bytes");
        Require(GC.GetAllocatedBytesForCurrentThread() == before, "Warmed streaming snapshot comparison allocated payload copies");
        foreach (int position in new[] {0,4095,4096,10002}) {
            var wrong = (byte[])expected.Clone(); wrong[position] ^= 1; using var file = new ShortRead(wrong);
            Require(!SnapshotContent.Matches(file,expected,expected.Length,scratch), "Changed snapshot byte accepted");
        }
        using var shortFile = new MemoryStream(expected,0,expected.Length-1);
        Require(!SnapshotContent.Matches(shortFile,expected,expected.Length,scratch), "Truncated snapshot matched");
        using var longFile = new MemoryStream(expected.Concat(new byte[]{0}).ToArray());
        Require(!SnapshotContent.Matches(longFile,expected,expected.Length,scratch), "Trailing snapshot data matched");
        var snapshot = new Snapshot {SourceSession=Guid.NewGuid(),Scene="GG_Grimm",Frame=1,Floor=new Point(0,0),PlayerWidth=1,
            RestPoints=new[]{new Point(1,0)},Shapes=new[]{new Shape{HazardId=4,Closed=true,Points=new[]{new Point(0,0),new Point(1,0),new Point(1,1),new Point(0,1)}}}};
        Codec.Validate(snapshot); before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++)Codec.Validate(snapshot);
        Require(GC.GetAllocatedBytesForCurrentThread()==before,"Warmed geometry validation allocated duplicate-check collections");
        snapshot.RestPoints=new[]{new Point(1,0),new Point(1,0)};Reject(()=>Codec.Validate(snapshot));
        snapshot.RestPoints=Array.Empty<Point>();Codec.Validate(snapshot);
        snapshot.Shapes=new[]{snapshot.Shapes[0],snapshot.Shapes[0]};Reject(()=>Codec.Validate(snapshot));
        snapshot.Shapes=new[]{snapshot.Shapes[0]};Codec.Validate(snapshot);
        Exception? error=null;
        var workers=Enumerable.Range(0,4).Select(_=>new System.Threading.Thread(()=>{try{for(int i=0;i<1000;i++)Codec.Validate(snapshot);}catch(Exception e){System.Threading.Interlocked.CompareExchange(ref error,e,null);}})).ToArray();
        foreach(var worker in workers)worker.Start();foreach(var worker in workers)worker.Join();if(error!=null)throw error;
        Console.WriteLine("PASS: allocation-free short-read snapshot comparison rejects changed/truncated/trailing bytes; warmed validation rejects duplicates after failure and isolates caller threads.");
    }
    static void ArmorOneWay() {
        var box=new Shape {Closed=true,OneWay=true,Points=new[]{new Point(10,0),new Point(16,0),new Point(16,2),new Point(10,2)}};
        foreach(string scene in new[]{"GG_False_Knight","GG_Failed_Champion"}) {
            var room=new Snapshot {SourceSession=Guid.NewGuid(),Scene=scene,Frame=1,PlayerWidth=.5f,Shapes=new[]{box}};
            using var bytes=new MemoryStream();Codec.Write(bytes,room);bytes.Position=0;
            var decoded=Codec.Read(bytes);
            Require(decoded.Shapes[0].OneWay && decoded.Shapes[0].Points[2].Y==2,"Native armor top/layer semantics lost in transfer");
            var ended=new Snapshot {SourceSession=room.SourceSession,Scene=scene,Frame=1,PlayerWidth=.5f,Shapes=new[]{new Shape{Points=new[]{new Point(0,0),new Point(20,0)}}}};
            Codec.ValidateRefresh(decoded,ended);
            Require(!ended.Shapes[0].OneWay,"Stagger platform remained after native removal");
            box.HazardId=3;Reject(()=>Codec.Validate(room));box.HazardId=0;
            box.Closed=false;Reject(()=>Codec.Validate(room));box.Closed=true;
            box.Points[2]=new Point(16,3);Reject(()=>Codec.Validate(room));box.Points[2]=new Point(16,2);
            byte[] old=bytes.ToArray();old[4]=5;using var legacy=new MemoryStream(old);Reject(()=>Codec.Read(legacy));
        }
        Console.WriteLine("PASS: both armor room one-way top/identity round trips, native removal, invalid hazard/open/sloping platforms and legacy geometry rejected.");
    }
    static void CombinedStrike() {
        float[] body={-1,-2,1,-2,1,2,-1,2},needle={0,0,4,-3,3,-4};
        var original=(float[])needle.Clone();
        var hull=StrikeHull.Combine(needle,body);
        foreach(var contour in new[]{body,needle})for(int j=0;j<contour.Length;j+=2) {
            for(int i=0;i<hull.Length;i+=2){int n=(i+2)%hull.Length;
                float cross=(hull[n]-hull[i])*(contour[j+1]-hull[i+1])-(hull[n+1]-hull[i+1])*(contour[j]-hull[i]);
                Require(cross>=-.00001f,"Combined strike omitted native body/needle");}
        }
        Require(needle.SequenceEqual(original),"Combined strike mutated cached native attack contour");
        var held=(float[])hull.Clone();
        for(int i=0;i<30;i++)StrikeHull.Combine(new float[]{10,10,11,10,11,11,10,11},body);
        Require(hull.SequenceEqual(held),"Scratch reuse mutated a queued combined strike");
        Reject(()=>StrikeHull.Combine(new float[]{0,0,1,0,2,0},new float[]{0,0,1,0,2,0}));
        Reject(()=>StrikeHull.Combine(new float[]{float.NaN,0,1,0,0,1},body));
        Console.WriteLine("PASS: one combined convex strike contains full body/native needle, preserves source/queued contours across scratch reuse and rejects invalid/degenerate geometry.");
    }
    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--lock-child")
        {
            try { using var gate = new SnapshotFileLock(args[1]); return 0; }
            catch (IOException) { return 23; }
        }
        try
        {
            AtomicPublication();
            SnapshotComparison();
            ArmorOneWay();
            CombinedStrike();
            NoskRectangle();
            RadiancePlatformEdges();
            WallContactGrid();
            var hubWalls = RoomFeatures.ClimbingWalls("GG_Atrium");
            var faces = new[] { (130f,64f,90f,1f), (85f,41f,60f,-1f), (134f,41f,60f,1f), (134f,18f,34f,1f), (139f,22f,34f,-1f) };
            Require(hubWalls.Length == faces.Length, "Hub wall count differs from player markup");
            for (int i = 0; i < faces.Length; i++) {
                var wall = hubWalls[i]; var face = faces[i];
                Require(Math.Abs(wall.ContactX - (face.Item1 + face.Item4 * .08f)) < .001f
                    && wall.Bottom == face.Item2 && wall.Top == face.Item3 && wall.Facing == face.Item4, "Hub wall lost marked face/extent/direction");
                float solidFace = wall.SolidCenter + wall.Facing * WallRunStrip.Thickness / 2;
                Require(Math.Abs(solidFace - wall.ContactX) < .001f, "Runnable contact does not match imported solid face");
            }
            var hall = RoomFeatures.ClimbingWalls("GG_Workshop");
            Require(hall.Length == 4 && hall[0].X == 16 && hall[1].X == 21 && hall[2].X == 207 && hall[3].X == 211,
                "Hall of Gods walls were not restored");
            Require(RoomFeatures.ClimbingWalls("GG_Ghost_Markoth").Length == 2 && RoomFeatures.ClimbingWalls("GG_Uumuu").Length == 2
                && RoomFeatures.ClimbingWalls("GG_Flukemarm").Length == 2, "Hub wall removal changed boss-arena features");
            var campaignSession = Guid.NewGuid();
            var campaign = new Snapshot { SourceSession = campaignSession, Scene = "Tutorial_01", Frame = 1, Floor = new Point(0, 0), PlayerWidth = .5f,
                Shapes = new[] { new Shape { Closed = true, Points = new[] { new Point(0, 0), new Point(1, 0), new Point(0, 1) } } } };
            Reject(() => Codec.Validate(campaign));
            try {
                Codec.ExperimentalSceneAuthorization = (session, scene) => session == campaignSession && scene == "Tutorial_01";
                using var packet = new MemoryStream(); Codec.Write(packet, campaign); packet.Position = 0;
                Require(Codec.Read(packet).SourceSession == campaignSession, "Authorized campaign geometry changed session identity.");
                campaign.SourceSession = Guid.NewGuid(); Reject(() => Codec.Validate(campaign)); campaign.SourceSession = campaignSession;
            } finally { Codec.ExperimentalSceneAuthorization = null; }
            Reject(() => Codec.Validate(campaign));
            Console.WriteLine("PASS: experimental room geometry requires exact session authorization and reverts to Godhome-only on release.");
            var platform=new Snapshot {Floor=new Point(80,5),PlayerWidth=.5f,Shapes=new[]{
                new Shape{Points=new[]{new Point(69,0),new Point(69,5),new Point(96,5),new Point(96,0)}},
                new Shape{Points=new[]{new Point(96,0),new Point(96,5),new Point(103,5),new Point(103,0)}},
                new Shape{Points=new[]{new Point(110,5),new Point(115,5)}}}};
            if(!CombatEnvelope.TryFromPlatform(platform,out var reach)||reach.Left!=66||reach.Right!=106||reach.Floor!=2)throw new Exception("Platform reach merged a gap or lost off-edge margins");
            float[] Box(float x,float y)=>new[]{x,y,x+.5f,y,x+.5f,y+.5f,x,y+.5f};
            if(reach.Outside(Box(68,6))||reach.Outside(Box(105.8f,6))||reach.Outside(Box(80,40))||reach.Outside(Box(80,1.8f)))throw new Exception("Reach retired a jump, returning upper trajectory or partly intersecting shape");
            if(!reach.Outside(Box(106.1f,6))||!reach.Outside(Box(65,6))||!reach.Outside(Box(80,1)))throw new Exception("Whole shapes outside margin retained");
            platform.Shapes=Array.Empty<Shape>();if(CombatEnvelope.TryFromPlatform(platform,out _))throw new Exception("Missing platform created a cutoff");
            Console.WriteLine("PASS: contiguous platform extent, off-edge/below margins, whole-shape cutoff, returning upper trajectories and missing-ground fallback.");
            var alpha = new byte[16*16*4];
            for(int y=2;y<14;y++) for(int x=2;x<14;x++) if(x<6 || x>=10 || y<6 || y>=10) alpha[(y*16+x)*4+3]=255;
            var boundaries = AlphaContours.Extract(alpha,16);
            Require(boundaries.Length==2,"Aura lost inner hole or outer silhouette.");
            float Area(Point[] loop) { float sum=0; for(int i=0;i<loop.Length;i++){var a=loop[i];var b=loop[(i+1)%loop.Length];sum+=a.X*b.Y-b.X*a.Y;} return sum*.5f; }
            Require(Area(boundaries[0])*Area(boundaries[1])<0,"Aura hole outward direction matches exterior.");
            Array.Clear(alpha); for(int y=1;y<5;y++) for(int x=1;x<5;x++) alpha[(y*16+x)*4+3]=255;
            for(int y=10;y<15;y++) for(int x=10;x<15;x++) alpha[(y*16+x)*4+3]=255;
            Require(AlphaContours.Extract(alpha,16).Length==2,"Aura joined disconnected native parts.");
            bool capped=false; try{AlphaContours.Extract(alpha,16,12);}catch(InvalidDataException){capped=true;}
            Require(capped,"Aura contour capacity not enforced.");
            Array.Clear(alpha); Require(AlphaContours.Extract(alpha,16).Length==0,"Blank frame retained old aura.");
            var noisy = new byte[128*128*4];
            for(int y=1;y<125;y+=8) for(int x=1;x<125;x+=8)
                for(int dy=0;dy<4;dy++) for(int dx=0;dx<4;dx++) noisy[((y+dy)*128+x+dx)*4+3]=255;
            var bounded=AlphaContours.ExtractBounded(noisy,128,160);
            Require(bounded.Length>0 && bounded.Length<=32 && bounded.Sum(l=>l.Length)<=160,"Noisy native alpha escaped decorative budget.");
            Require(bounded.All(l=>l.Length>=4 && Area(l)!=0),"Bounded decorative contour collapsed or lost winding.");
            Require(AlphaContours.ExtractBounded(new byte[128*128*4],128).Length==0,"Bounded blank alpha retained shape.");
            Console.WriteLine("PASS: local aura closed silhouettes, opposite hole winding, disconnected parts, blank frame and contour budget.");
            var priorSession = Environment.GetEnvironmentVariable("YIGODHOME_SESSION");
            try
            {
                var session = Guid.NewGuid();
                Environment.SetEnvironmentVariable("YIGODHOME_SESSION", session.ToString());
                string source = Path.Combine(Path.GetTempPath(), "evidence", "HollowKnight", session.ToString(), "godhome-geometry.bin");
                string live = Codec.RuntimePath(source);
                Require(live == Path.Combine(Path.GetTempPath(), "YiGodhome", session.ToString(), "godhome-geometry.bin"), "live geometry remained in evidence folder");
                Require(Codec.RuntimePath(live) == live, "runtime resolution is not idempotent");
                string foreign = source.Replace(session.ToString(), Guid.NewGuid().ToString());
                Require(Codec.RuntimePath(foreign) == foreign, "another session was redirected");
                string staged = source.Replace("HollowKnight", "NineSols");
                Require(Codec.RuntimePath(staged) == staged, "staged native snapshot was redirected");
                Environment.SetEnvironmentVariable("YIGODHOME_SESSION", null);
                Require(Codec.RuntimePath(source) == source, "offline evidence reader was redirected");
            }
            finally { Environment.SetEnvironmentVariable("YIGODHOME_SESSION", priorSession); }
            Console.WriteLine("PASS: live geometry isolated by session; staged/offline evidence paths preserved.");
            var snapshot = new Snapshot { SourceSession = Guid.NewGuid(), Scene = "GG_Atrium", Frame = 50, Floor = new Point(110.6f, 60), PlayerWidth = 0.5f, RestPoints = new[] { new Point(110.6f, 60) },
                Shapes = new[] {
                    new Shape { Points = new[] { new Point(100,60), new Point(120,60), new Point(120,80) }, Radius = 0.01f, AdjacentStart = true, Start = new Point(99,60) },
                    new Shape { Closed = true, Points = new[] { new Point(90,50), new Point(100,50), new Point(100,60) } } } };
            using var stream = new MemoryStream(); Codec.Write(stream, snapshot); byte[] bytes = stream.ToArray();
            var hazard = new Shape { HazardId = -123, Closed = true, Points = new[] { new Point(-4,0), new Point(4,0), new Point(4,2), new Point(-4,2) } };
            var hazardSnapshot = new Snapshot { SourceSession = snapshot.SourceSession, Scene = snapshot.Scene, Frame = snapshot.Frame,
                Floor = snapshot.Floor, PlayerWidth = snapshot.PlayerWidth, Shapes = new[] { snapshot.Shapes[0], hazard } };
            using (var hazardStream = new MemoryStream())
            {
                Codec.Write(hazardStream, hazardSnapshot); hazardStream.Position = 0;
                Require(Codec.Read(hazardStream).Shapes[1].HazardId == -123, "hazard identity lost");
            }
            hazardSnapshot.Shapes = new[] { hazard, hazard }; Reject(() => Codec.Validate(hazardSnapshot));
            hazardSnapshot.Shapes = new[] { hazard }; hazard.Closed = false; Reject(() => Codec.Validate(hazardSnapshot)); hazard.Closed = true;
            hazard.Points[1] = new Point(4,1); Reject(() => Codec.Validate(hazardSnapshot)); hazard.Points[1] = new Point(4,0);
            Console.WriteLine("PASS: explicit hazard identity round trip; duplicate IDs, open and nonrectangular voids rejected.");
            stream.Position = 0; var copy = Codec.Read(stream);
            foreach (byte style in new byte[] { 1, 2, 3, 4 })
            {
                copy.EntryStyle = style; copy.EntryStartX = copy.Floor.X - 2;
                using var entrance = new MemoryStream(); Codec.Write(entrance, copy); entrance.Position = 0;
                var decoded = Codec.Read(entrance);
                Require(decoded.EntryStyle == style && decoded.EntryStartX == copy.EntryStartX, "entrance placement lost");
                Reject(() => Codec.ValidateRefresh(snapshot, decoded));
            }
            foreach(byte vertical in new byte[]{5,6}) {
                copy.EntryStyle=vertical;copy.EntryStartX=copy.Floor.X;copy.EntryStartY=copy.Floor.Y+(vertical==5?27:-8);
                using var sky=new MemoryStream();Codec.Write(sky,copy);sky.Position=0;var landed=Codec.Read(sky);
                Require(landed.EntryStartY==copy.EntryStartY && landed.EntryStyle==vertical,"Vertical entry height/style lost");
                landed.EntryStartY++;Reject(()=>Codec.ValidateRefresh(copy,landed));
                var old=sky.ToArray();old[4]=6;Reject(()=>Codec.Read(new MemoryStream(old)));
            }
            copy.EntryStyle=5;copy.EntryStartY=copy.Floor.Y;Reject(()=>Codec.Validate(copy));
            copy.EntryStartY=float.NaN;Reject(()=>Codec.Validate(copy));copy.EntryStartY=0;
            copy.EntryStyle=7;Reject(()=>Codec.Validate(copy));
            copy.EntryStyle = 2; copy.EntryStartX = copy.Floor.X + 11; Reject(() => Codec.Validate(copy));
            copy.EntryStartX = float.NaN; Reject(() => Codec.Validate(copy));
            copy.EntryStyle = 0; copy.EntryStartX = 0;
            Console.WriteLine("PASS: bounded native entrance styles and immutable per-room placement.");
            var exchangePath = Path.Combine(Path.GetTempPath(), "YiGodhomeGeometry-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllBytes(exchangePath, bytes);
                int ChildLock()
                {
                    var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                    if ((Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "").Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                        start.ArgumentList.Add(typeof(Program).Assembly.Location);
                    start.ArgumentList.Add("--lock-child"); start.ArgumentList.Add(exchangePath);
                    using var child = System.Diagnostics.Process.Start(start)!;
                    if (!child.WaitForExit(10000)) { child.Kill(); throw new Exception("Child lock test timed out"); }
                    return child.ExitCode;
                }
                using (var held = new SnapshotFileLock(exchangePath)) Require(ChildLock() == 23, "Other process entered held geometry lock");
                Require(ChildLock() == 0, "Other process could not acquire released geometry lock");
                using (var held = new SnapshotFileLock(exchangePath)) {
                    System.Threading.Tasks.Task.Run(() => {
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        Require(!Codec.TryReadFileBytes(exchangePath, out var pending) && pending.Length == 0, "Contended frame read must return no bytes");
                        Require(watch.ElapsedMilliseconds < 50, "Contended frame read waited on the writer");
                    }).GetAwaiter().GetResult();
                }
                Require(Codec.TryReadFileBytes(exchangePath, out var resumed) && resumed.SequenceEqual(bytes), "Frame read did not recover after writer release");
                Console.WriteLine("PASS: contended frame reads return immediately without partial bytes and recover after release.");
                Console.WriteLine("PASS: independent process excluded while held and admitted after release.");
                var readerTask = System.Threading.Tasks.Task.Run(() => {
                    for (int i = 0; i < 1000; i++) { using var read = new MemoryStream(Codec.ReadFileBytes(exchangePath)); Require(Codec.Read(read).Frame == 50, "torn file snapshot"); }
                });
                var publication = new SnapshotPublication();
                for (int i = 0; i < 1000; i++) { using var gate = new SnapshotFileLock(exchangePath); publication.Publish(exchangePath, bytes, bytes.Length, _ => { }); }
                readerTask.GetAwaiter().GetResult();
                Console.WriteLine("PASS: 1000 concurrent atomic replacements and bounded snapshot reads without sharing violations or torn frames.");
            }
            finally { File.Delete(exchangePath); File.Delete(exchangePath + ".pending"); }
            Require(copy.SourceSession == snapshot.SourceSession && copy.Shapes[0].AdjacentStart && copy.Shapes[0].Radius == 0.01f && copy.Shapes[1].Closed, "shape round trip");
            var map = new CoordinateMap(snapshot.Floor, new Point(-4280, -2192), 20);
            for (int i = -100; i <= 100; i++)
            {
                var p = new Point(i * 0.25f, i * -0.33f); var back = map.ToSource(map.ToTarget(p));
                Require(Math.Abs(back.X - p.X) < 0.0001f && Math.Abs(back.Y - p.Y) < 0.0001f, "coordinate round trip");
            }
            var anchor = map.ToTarget(snapshot.Floor); Require(anchor.X == -4280 && anchor.Y == -2192, "floor anchor");
            var velocity = map.VelocityToTarget(new Point(8.3f,16.65f)); Require(Math.Abs(velocity.X - 166) < 0.001f && Math.Abs(velocity.Y - 333) < 0.001f, "velocity scale");
            copy.Shapes = new[] { copy.Shapes[1] }; Codec.ValidateRefresh(snapshot, copy);
            copy.Frame++; Reject(() => Codec.ValidateRefresh(snapshot, copy)); copy.Frame--;
            copy.Scene = "GG_Workshop"; Reject(() => Codec.ValidateRefresh(snapshot, copy)); copy.Scene = snapshot.Scene;
            copy.SourceSession = Guid.NewGuid(); Reject(() => Codec.ValidateRefresh(snapshot, copy)); copy.SourceSession = snapshot.SourceSession;
            copy.PlayerWidth *= 2; Reject(() => Codec.ValidateRefresh(snapshot, copy)); copy.PlayerWidth = snapshot.PlayerWidth;
            copy.Floor = new Point(0, 0); Reject(() => Codec.ValidateRefresh(snapshot, copy)); copy.Floor = snapshot.Floor;
            Require(copy.RestPoints.Length == 1 && copy.RestPoints[0].X == 110.6f && copy.RestPoints[0].Y == 60, "rest placement changed in transport");
            copy.RestPoints = Array.Empty<Point>(); Reject(() => Codec.ValidateRefresh(snapshot, copy));
            copy.RestPoints = new[] { new Point(111, 60) }; Reject(() => Codec.ValidateRefresh(snapshot, copy));
            copy.RestPoints = new[] { new Point(float.NaN, 60) }; Reject(() => Codec.Validate(copy));
            copy.RestPoints = new Point[33]; Reject(() => Codec.Validate(copy));
            copy.RestPoints = new[] { new Point(110.6f, 60), new Point(110.6f, 60) }; Reject(() => Codec.Validate(copy));
            copy.RestPoints = snapshot.RestPoints;
            Console.WriteLine("PASS: live shape removal preserves room mapping; foreign session/room/generation and anchor/scale changes rejected.");
            for (int length = 0; length < bytes.Length; length++)
            {
                using var truncated = new MemoryStream(bytes, 0, length); Reject(() => Codec.Read(truncated));
            }
            using (var trailing = new MemoryStream()) { trailing.Write(bytes); trailing.WriteByte(0); trailing.Position = 0; Reject(() => Codec.Read(trailing)); }
            byte[] wrong = (byte[])bytes.Clone(); wrong[4] = 99; Reject(() => Codec.Read(new MemoryStream(wrong)));
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 100001 })
            { snapshot.Floor = new Point(value, 0); Reject(() => Codec.Validate(snapshot)); }
            snapshot.Floor = new Point(0,0); snapshot.Shapes[0].Points = new[] { new Point(0,0), new Point(0,0) }; Reject(() => Codec.Validate(snapshot));
            Reject(() => new CoordinateMap(new Point(0,0), new Point(0,0), 0));
            Reject(() => new CoordinateMap(new Point(0,0), new Point(0,0), float.NaN));
            // Shape-count and point-count fields are validated before allocating their arrays.
            wrong = (byte[])bytes.Clone(); int countOffset = 4 + 2 + 16 + 1 + "GG_Atrium".Length + 8 + 8 + 4;
            Array.Copy(BitConverter.GetBytes(int.MaxValue), 0, wrong, countOffset, 4); Reject(() => Codec.Read(new MemoryStream(wrong)));
            wrong = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(int.MaxValue), 0, wrong, countOffset + 4 + 1 + 4 + 16, 4); Reject(() => Codec.Read(new MemoryStream(wrong)));
            Console.WriteLine("PASS: geometry round trip, coordinate/velocity transform, every truncation, trailing data, invalid versions/counts/coordinates and repeated points.");
            if (args.Length == 1)
            {
                using var file = File.OpenRead(args[0]); var live = Codec.Read(file);
                Console.WriteLine("PASS: native export scene=" + live.Scene + " shapes=" + live.Shapes.Length + " source=" + live.SourceSession);
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    static void RadiancePlatformEdges() {
        static bool JumpHits(Point[] points,float x,float from,float to) {
            for(int i=0;i<points.Length-1;i++) {
                var a=points[i];var b=points[i+1];
                if(a.Y==b.Y && x>=Math.Min(a.X,b.X) && x<=Math.Max(a.X,b.X) && a.Y>=from && a.Y<=to)return true;
            }
            return false;
        }
        foreach(var size in new[]{(W:2.5836055f,H:2.5330005f),(W:2.5836055f,H:.93868494f),(W:4.272402f,H:.9833678f)})
        foreach(int facing in new[]{-1,1}) {
            float x=64.239998f,y=61.46f;
            var box=new[]{new Point(x-facing*size.W*.5f,y),new Point(x+facing*size.W*.5f,y),new Point(x+facing*size.W*.5f,y+size.H),new Point(x-facing*size.W*.5f,y+size.H)};
            var shape=RadiancePlatforms.Outline(box);
            Require(RadiancePlatforms.Matches("GG_Radiance",shape),"Native ascent box signature missed");
            Require(!shape.Closed && !shape.OneWay && shape.Points.Length==5,"Ascent became one-way or filled ceiling trap");
            Require(!RadiancePlatforms.Matches("GG_Nosk",shape),"Ascent normalization leaked to another room");
            for(int offset=0;offset<64;offset++) {
                var map=new CoordinateMap(new Point(60+offset*.0173f,10),new Point(-3210.037f+offset*.013f,-1800),20);
                var points=RadiancePlatforms.MapOutline(shape,map);
                float left=points.Min(p=>p.X),right=points.Max(p=>p.X);
                float oldLeft=box.Select(map.ToTarget).Min(p=>p.X),oldRight=box.Select(map.ToTarget).Max(p=>p.X);
                Require(left>=oldLeft && left-oldLeft<.126f && right<=oldRight && oldRight-right<.126f,"Ascent face moved beyond a native contact cell");
                Require(left*8==Math.Floor(left*8) && right*8==Math.Floor(right*8),"Fractional native ascent face retained");
                for(int i=0;i<4;i++) {
                    var a=points[i];var b=points[i+1];
                    Require(a.X==b.X || a.Y==b.Y,"Ascent side has a protruding lip");
                    Require(a.Y==map.ToTarget(shape.Points[i]).Y,"Ascent floor/ceiling height changed");
                }
                Require(points[0].X==points[4].X && points[0].Y==points[4].Y,"Ascent solid perimeter has a gap");
                // A vertical jump under the slab crosses its bottom edge; a side
                // jump outside either flat face never intersects that underside.
                float bottom=points.Min(p=>p.Y),top=points.Max(p=>p.Y);
                Require(bottom<top && left<right,"Ascent lost solid floor/ceiling");
                Require(JumpHits(points,(left+right)*.5f,bottom-2,top+2),"A jump through the center missed the solid underside");
                Require(!JumpHits(points,left-.0625f,bottom-2,top+2) && !JumpHits(points,right+.0625f,bottom-2,top+2),"A jump flush outside a side face hit a phantom underside lip");
            }
            shape.HazardId=1;Require(!RadiancePlatforms.Matches("GG_Radiance",shape),"Dream void normalized as solid terrain");
        }
        Console.WriteLine("PASS: all three native ascent platform sizes/mirrored scales keep a closed four-sided solid edge perimeter, grid-flat side faces and unchanged floor/ceiling heights.");
    }
    static void NoskRectangle()
    {
        var left=new Shape {Points=new[]{new Point(64,1),new Point(64,0),new Point(96,0),new Point(96,6),new Point(88,6),new Point(88,3),new Point(71,3),new Point(71,26),new Point(64,26),new Point(64,1)}};
        var right=new Shape {Points=new[]{new Point(96,1),new Point(96,0),new Point(128,0),new Point(128,26),new Point(118,26),new Point(118,4),new Point(104,4),new Point(104,6),new Point(96,6),new Point(96,1)}};
        var other=new Shape {Closed=true,Points=new[]{new Point(70,-2),new Point(120,-2),new Point(120,2),new Point(70,2)}};
        var original=new[]{left,right,other};var fixedShapes=NoskPlatform.Normalize("GG_Nosk",original);
        Require(fixedShapes.Length==4 && ReferenceEquals(fixedShapes[2],other),"Nosk repair changed unrelated terrain");
        var platform=fixedShapes[3];Require(!platform.Closed && platform.Points.Length==5 && platform.Points[0].X==88 && platform.Points[1].X==104 && platform.Points[2].Y==6 && platform.Points[4].X==88 && platform.Points[4].Y==3,"Nosk rectangular edge outline lost native faces/top");
        Require(left.Points.Length==10 && right.Points.Length==10,"Nosk repair mutated source arrays");
        Require(ReferenceEquals(NoskPlatform.Normalize("GG_Nosk",fixedShapes),fixedShapes),"Nosk repair duplicated on refresh");
        Require(ReferenceEquals(NoskPlatform.Normalize("GG_Nosk_V",original),original),"Nosk repair leaked into another arena");
        bool refused=false;try{NoskPlatform.Normalize("GG_Nosk",new[]{left,other});}catch(InvalidOperationException){refused=true;}
        Require(refused,"Nosk accepted a partial platform match");
        Require(NoskPlatform.AlignNativeLeftFace(-2639.8f)==-2639.75f,"Nosk negative native wall grid alignment changed");
        foreach(float x in new[]{-2639.8f,-10.125f,-.0625f,0,1.0625f,100.08f}) {
            float aligned=NoskPlatform.AlignNativeLeftFace(x);
            Require(aligned>=x && aligned-x<.125f && aligned*8==Math.Floor(aligned*8),"Nosk alignment moved more than one native grid cell");
            Require(NoskPlatform.AlignNativeLeftFace(aligned)==aligned,"Nosk wall alignment is not idempotent");
        }
        Console.WriteLine("PASS: Nosk single rectangular platform, preserved right face/top and surrounding terrain, immutable/idempotent room-scoped repair.");
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Reject(Action action)
    {
        try { action(); } catch (IOException) { return; } catch (InvalidDataException) { return; }
        throw new Exception("Malformed geometry was accepted.");
    }
}
