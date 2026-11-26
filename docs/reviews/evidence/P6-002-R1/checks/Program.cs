using System.IO;
using System.Text.Json;
using DesktopPet.Core.Events;
using DesktopPet.Core.Persistence;
using DesktopPet.App.Platform;

class Program
{
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    static readonly DateTimeOffset Origin = new(2026,11,27,23,29,55,TimeSpan.FromHours(8));
    static void Need(bool value, string message) { if (!value) throw new Exception(message); }
    static void Near(double actual, double expected) => Need(Math.Abs(actual-expected)<0.001,$"{actual} != {expected}");
    [STAThread] static int Main(string[] args)
    {
        string root=Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        var results=new List<object>(); bool all=true;
        void Check(string name, Action body) { try { body(); results.Add(new {name,pass=true}); } catch(Exception ex) { all=false; results.Add(new {name,pass=false,error=ex.ToString()}); } }
        Check("ExactBoundary_NoEmptyNextBucket_OneSessionAcrossMidnight",()=> {
            double t=0; var c=new UsageStatsCollector(()=>t,()=>Origin.AddMilliseconds(t)); c.Enable();
            c.SampleTick("a",t); t=5000; c.SampleTick("a",t);
            Need(c.Buckets.Count==1,"boundary created empty bucket");
            for(int i=0;i<362;i++){t+=5000;c.SampleTick("a",t);}
            Near(c.Buckets.Sum(x=>x.ActiveMs),t); Need(c.Buckets.Sum(x=>x.SessionCount)==1,"cross bucket counted again");
            Need(c.Buckets.Select(x=>x.Date).Distinct().Count()==2,"midnight not split");
        });
        Check("DeterministicRandomTrace_ExpectedPerBucketTotalsAndSessionStarts",()=> {
            double t=0; var c=new UsageStatsCollector(()=>t,()=>Origin.AddMilliseconds(t)); c.Enable();
            var rng=new Random(21475); string? prev=null, committedApp=null; double prevT=0; bool open=false;
            var expected=new Dictionary<string,(double ms,int count)>();
            for(int i=0;i<6000;i++) {
                t+=rng.Next(1,15001); string app="app"+rng.Next(3); bool valid=rng.Next(9)!=0;
                if(valid && prev!=null) {
                    bool starts=!open||committedApp!=prev; var cursor=Origin.AddMilliseconds(prevT); double remain=t-prevT;
                    while(remain>0.00001) {
                        var b=new DateTimeOffset(cursor.Year,cursor.Month,cursor.Day,cursor.Hour,cursor.Minute/30*30,0,cursor.Offset);
                        string key=prev+"|"+b.ToString("O"); double part=Math.Min(remain,(b.AddMinutes(30)-cursor).TotalMilliseconds);
                        expected.TryGetValue(key,out var v); expected[key]=(v.ms+part,v.count+(starts?1:0)); starts=false;
                        remain-=part; cursor=cursor.AddMilliseconds(part);
                    }
                    committedApp=prev; open=true;
                }
                c.SampleTick(app,valid?t:null);
                if(valid){prev=app;prevT=t;} else {prev=null;open=false;}
            }
            Need(expected.Count==c.Buckets.Count,"bucket count mismatch");
            foreach(var b in c.Buckets) {var e=expected[b.AppId+"|"+b.BucketStartLocal]; Near(b.ActiveMs,e.ms); Need(b.SessionCount==e.count,"session mismatch");}
        });
        Check("WholeSnapshot_RepeatedDrain_RestartNewActivity_ClearRestart",()=> {
            var dir=Path.Combine(root,"snapshot");
            using(var h=new Harness(dir)) {h.Service.Start();h.Service.Enable();h.Tick();h.Tick();h.Tick();for(int i=0;i<4;i++)h.Service.DrainPending();Near(h.Service.Buckets.Sum(x=>x.ActiveMs),10000);}
            using(var h=new Harness(dir)) {h.Service.Start();Near(h.Service.Buckets.Sum(x=>x.ActiveMs),10000);h.Service.Enable();h.Tick();h.Tick();Near(h.Service.Buckets.Sum(x=>x.ActiveMs),15000);Need(h.Service.Buckets.Sum(x=>x.SessionCount)==2,"new process session");h.Service.ClearAll();Need(h.Service.IsEnabled,"clear changed switch");}
            using(var h=new Harness(dir)) {h.Service.Start();Need(h.Service.Buckets.Count==0,"clear resurrected buckets");}
        });
        Check("WriteFailure_RetrySnapshotDoesNotDuplicateBuckets",()=> {
            var dir=Path.Combine(root,"retry"); using var h=new Harness(dir); h.Service.Start();h.Service.Enable();h.Tick();h.Tick();
            using(var locked=new FileStream(h.Service.StatsFilePath,FileMode.Open,FileAccess.Read,FileShare.Read)) {h.Tick();Need(h.Service.LastWriteError!=null,"expected contained replace failure");}
            h.Service.DrainPending();h.Service.DrainPending();Need(h.Service.LastWriteError==null,"retry not recovered");
            var disk=JsonSerializer.Deserialize<UsageStatsArchive>(File.ReadAllText(h.Service.StatsFilePath),Json)!;Near(disk.Buckets.Sum(x=>x.ActiveMs),10000);Need(disk.Buckets.Sum(x=>x.SessionCount)==1,"retry added session");
        });
        Check("V1Load_PreservesRaw_NoInventedBuckets_UpgradesOnNewActivity",()=> {
            var dir=Path.Combine(root,"v1");Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir,"stats.json"),"{\"schemaVersion\":1,\"segments\":[{\"appId\":\"old\",\"date\":\"2026-11-27\",\"startLocal\":\"2026-11-27T10:00:00+08:00\",\"durationMs\":5000}],\"events\":[]}");
            using var h=new Harness(dir);h.Service.Start();Need(h.Service.Segments.Count==1&&h.Service.Buckets.Count==0,"legacy load policy mismatch");h.Service.Enable();h.Tick();h.Tick();
            var disk=JsonSerializer.Deserialize<UsageStatsArchive>(File.ReadAllText(h.Service.StatsFilePath),Json)!;
            Need(disk.SchemaVersion==2&&disk.Segments.Any(x=>x.AppId=="old"),"legacy lost");Near(disk.Buckets.Sum(x=>x.ActiveMs),5000);
        });
        Check("RetentionExactDates_Raw7_Aggregate30_WhileDisabled",()=> {
            double t=0;var c=new UsageStatsCollector(()=>t,()=>Origin);
            var a=new UsageStatsArchive(); foreach(int age in new[]{6,7,29,30}) {var d=Origin.AddDays(-age);a.Segments.Add(new(){AppId="a",Date=d.ToString("yyyy-MM-dd"),DurationMs=5000});a.Buckets.Add(new(){AppId="a",Date=d.ToString("yyyy-MM-dd"),BucketStartLocal=d.ToString("O"),ActiveMs=5000,SessionCount=1});}
            c.Restore(a);c.PruneToRetention();Need(!c.IsEnabled,"default switched");Need(c.Segments.Count==1&&c.Buckets.Count==3,"inclusive boundary incorrect");
        });
        File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new {allPass=all,results},Json)); Console.WriteLine(JsonSerializer.Serialize(new {allPass=all,results},Json));return all?0:1;
    }
    sealed class Idle : InputIdleSampler {public Func<double> Now=()=>0;public override double? GetLastInputMs(double now)=>Now();}
    sealed class Foreground : ForegroundAppSampler {public override string? TryGetForegroundAppId(out bool failed){failed=false;return "a";}}
    sealed class Harness : IDisposable {
        double t; public UsageStatsService Service;
        public Harness(string dir) {Directory.CreateDirectory(dir);var c=new UsageStatsCollector(()=>t,()=>Origin.AddMilliseconds(t));var idle=new Idle{Now=()=>t};Service=new(c,idle,new Foreground(),MonotonicClock.Instance,dispatcher:null,saveDirectory:dir,autoStartTimer:false);}
        public void Tick(){t+=5000;Service.SampleTickManually();}public void Dispose()=>Service.Dispose();
    }
}
