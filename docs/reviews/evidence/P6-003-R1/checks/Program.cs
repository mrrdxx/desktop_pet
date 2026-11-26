using System.IO;
using System.Text.Json;
using DesktopPet.App.Platform;
using DesktopPet.Core.Events;
using DesktopPet.Core.Persistence;

class Program
{
    static readonly DateTimeOffset Now = new(2026,11,27,12,0,0,TimeSpan.FromHours(8));
    static readonly JsonSerializerOptions Json = new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
    static UsageStatsBucket Bucket(int age,string app="notepad") {var d=Now.AddDays(-age);return new(){AppId=app,Date=d.ToString("yyyy-MM-dd"),BucketStartLocal=new DateTimeOffset(d.Year,d.Month,d.Day,9,30,0,d.Offset).ToString("O"),ActiveMs=5000,SessionCount=1};}
    static object View(UsageStatsService s) {var r=s.GetFrequencyPredictions();return new{r.ObservedDateCount,count=r.Candidates.Count,bucketDates=s.Buckets.Select(x=>x.Date).Order().ToArray()};}
    [STAThread] static int Main(string[] args) {
        string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);var observations=new List<object>();
        foreach(bool boundary in new[]{true,false}) {
            var dir=Path.Combine(root,boundary?"boundary":"control");Directory.CreateDirectory(dir);
            var buckets=new List<UsageStatsBucket>();
            foreach(int age in boundary?new[]{30,1,2,3,4}:new[]{5,1,2,3,4}) buckets.Add(Bucket(age));
            buckets.Add(Bucket(6,"other"));buckets.Add(Bucket(7,"other"));
            File.WriteAllText(Path.Combine(dir,"stats.json"),JsonSerializer.Serialize(new UsageStatsArchive{Buckets=buckets},Json));
            var collector=new UsageStatsCollector(()=>0,()=>Now);
            using var service=new UsageStatsService(collector,new Idle(),new Foreground(),MonotonicClock.Instance,dispatcher:null,saveDirectory:dir,autoStartTimer:false,localNow:()=>Now);
            service.Start();service.Enable(); var before=View(service);int beforeCount=service.GetFrequencyPredictions().Candidates.Count;
            service.DrainPending();var after=View(service);int afterCount=service.GetFrequencyPredictions().Candidates.Count;
            observations.Add(new{scenario=boundary?"oldest-complete-day":"in-retention-control",before,after,stableAcrossNormalDrain=beforeCount==afterCount});
            service.Disable();bool disabledEmpty=service.GetFrequencyPredictions().Candidates.Count==0;
            service.Enable();service.ClearAll();bool clearEmpty=service.GetFrequencyPredictions().Candidates.Count==0;
            observations.Add(new{scenario=boundary?"boundary-lifecycle":"control-lifecycle",disabledEmpty,clearEmpty});
        }
        var output=new{localNow=Now,collectorRetentionDays=UsageStatsCollector.AggregatedRetentionDays,judgeWindowDays=UsageStatsFrequencyOptions.DefaultWindowDays,observations};
        string content=JsonSerializer.Serialize(output,Json);File.WriteAllText(Path.Combine(root,"observations.json"),content);Console.WriteLine(content);return 0;
    }
    sealed class Idle:InputIdleSampler {public override double? GetLastInputMs(double now)=>0;}
    sealed class Foreground:ForegroundAppSampler {public override string? TryGetForegroundAppId(out bool failed){failed=false;return "notepad";}}
}
