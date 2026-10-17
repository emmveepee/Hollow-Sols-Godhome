using System;
using System.IO;
using System.Linq;
using YiGodhome.Bridge;
static class DetailedCaptureChecks
{
    static void Assert(bool b,string why) { if(!b) throw new Exception(why); }
    public static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"YiCapture-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var id=Guid.NewGuid(); string dir=Path.Combine(root,"time");
            using(var c=new DetailedEventCapture(dir,"session=test role=HK",4096,2)) {
                c.Observe("before"); c.Sync(id,10,"context"); Assert(c.ActiveId==id,"Capture did not start");
                c.Tick(11.9); Assert(c.Active,"Premature timeout"); c.Tick(12); Assert(!c.Active && c.Status.Contains(id.ToString("N").Substring(0,8)),"Timeout or retained user capture ID failed");
                c.Sync(id,20,"context"); Assert(!c.Active,"Expired command replay restarted logging");
                string file=Path.Combine(dir,"detail-"+id.ToString("N")+".log"); string text=File.ReadAllText(file);
                c.Observe("after"); Assert(File.ReadAllText(file)==text && text.Contains("PRELUDE before") && text.Contains("reason=time-limit"),"Closed evidence mutated or timeout missing");
                c.Sync(Guid.Empty,20,""); c.Sync(Guid.NewGuid(),21,"new"); Assert(c.Active,"Fresh capture refused"); c.Stop("manual");
            }
            dir=Path.Combine(root,"capacity");
            using(var c=new DetailedEventCapture(dir,"bounded",1024)) {
                for(int i=0;i<DetailedEventCapture.MaxCaptures;i++) { c.Sync(Guid.NewGuid(),i,""); c.Write(new string('x',2000)); Assert(!c.Active,"Size limit failed"); }
                c.Sync(Guid.NewGuid(),20,""); Assert(!c.Active,"Capture count limit failed");
                var files=Directory.GetFiles(dir); Assert(files.Length==8 && files.All(f=>new FileInfo(f).Length<=1024) && files.All(f=>File.ReadAllText(f).Contains("DETAIL_END")),"Disk/end-marker bound failed");
            }
            dir=Path.Combine(root,"existing");Directory.CreateDirectory(dir);string existing=Path.Combine(dir,"detail-"+id.ToString("N")+".log");File.WriteAllText(existing,"preserve");
            using(var c=new DetailedEventCapture(dir,"io")) { c.Sync(id,0,""); Assert(!c.Active && File.ReadAllText(existing)=="preserve","Existing evidence overwritten"); }
            using(var c=new DetailedEventCapture(Path.Combine(root,"peer"),"peer")) { c.Sync(id,0,""); c.Sync(Guid.Empty,.1,""); Assert(!c.Active,"Peer stop failed"); c.Sync(Guid.Empty,.2,""); }
            Console.WriteLine("PASS: focused capture default-off, timeout/replay, size/capacity, prelude, peer stop, closed evidence and I/O collision");
        } finally { var resolved=Path.GetFullPath(root); if(!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("YiCapture-",StringComparison.Ordinal)) throw new Exception("Unsafe test cleanup target"); Directory.Delete(resolved,true); }
    }
}
