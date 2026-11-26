using System.IO;
using System.Reflection;
using System.Text.Json;
using DesktopPet.App;
using DesktopPet.App.Platform;
using DesktopPet.Core.Events;

class Program
{
 static List<object> results=new();
 static void Check(string name,object expected,object? actual,bool pass)=>results.Add(new{name,expected,actual,pass});
 [STAThread] static void Main(string[] args)
 {
  var root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);double now=0;
  var date=new DateTimeOffset(2026,11,26,12,0,0,TimeSpan.FromHours(8));
  UsageStatsCollector New()=>new(()=>now,()=>date.AddMilliseconds(now));
  foreach(var mode in new[]{"pause","lock","sleep","overlap"}){
   now=0;var c=New();var f=new Foreground();using var s=new UsageStatsService(c,new Idle(()=>now),f,MonotonicClock.Instance,null,Path.Combine(root,mode),false);
   s.Start();s.Enable();s.SampleTickManually();now=5000;s.SampleTickManually();
   if(mode=="pause")s.OnPauseToggled(true);else if(mode=="lock")s.OnSessionSwitch(7);else if(mode=="sleep")s.OnPowerModeChanged(3);else{s.OnPauseToggled(true);s.OnSessionSwitch(7);s.OnSessionSwitch(8);}
   var oldCalls=f.Calls;var oldTotal=c.Segments.Sum(x=>x.DurationMs);now=10000;s.SampleTickManually();now=15000;s.SampleTickManually();s.Disable();
   Check(mode+" preserves committed duration",5000,c.Segments.Sum(x=>x.DurationMs),oldTotal==5000 && c.Segments.Sum(x=>x.DurationMs)==5000);
   Check(mode+" does not read foreground",oldCalls,f.Calls,f.Calls==oldCalls);
  }
  now=0;var r=New();r.Enable();r.SampleTick("editor",0);now=5000;r.SampleTick("editor",5000);now=1000;r.SampleTick("editor",1000);now=5000;r.SampleTick("editor",5000);
  Check("Clock rollback does not duplicate verified interval",5000,r.Segments.Sum(x=>x.DurationMs),r.Segments.Sum(x=>x.DurationMs)==5000);
  var dir=Path.Combine(root,"locked-sidecar");Directory.CreateDirectory(dir);var backup=Path.Combine(dir,"stats.json.corrupted");File.WriteAllText(backup,"old-private-sentinel");
  now=0;using(var s=new UsageStatsService(New(),new Idle(()=>now),new Foreground(),MonotonicClock.Instance,null,dir,false)){
   s.Start();using(var locked=new FileStream(backup,FileMode.Open,FileAccess.Read,FileShare.Read)){s.ClearAll();Check("Failed backup deletion is observable",true,s.LastWriteError,!string.IsNullOrWhiteSpace(s.LastWriteError));Check("Retained history exposes incomplete clear status",true,File.Exists(backup) && !string.IsNullOrWhiteSpace(s.LastWriteError),File.Exists(backup) && !string.IsNullOrWhiteSpace(s.LastWriteError));}
   s.DrainPending();Check("Clear retry removes remaining historical backup",false,File.Exists(backup),!File.Exists(backup));
  }
  var app=new App();var adir=Path.Combine(root,"paused-disabled-start");Directory.CreateDirectory(adir);try{
   app.TestSetupIsolatedSave(adir);typeof(App).GetField("_isPaused",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(app,true);
   typeof(App).GetMethod("InitializeUsageStats",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(app,new object?[]{adir});
   app.SetUsageStatsEnabled(true);var service=app.UsageStatsService!;var c=(UsageStatsCollector)typeof(UsageStatsService).GetField("_collector",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(service)!;
   Check("Enable stats after paused default-off startup preserves pause",true,c.IsSuspended,c.IsSuspended);
  }finally{app.TestCleanup();}
  Console.WriteLine(JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
 }
 sealed class Idle(Func<double> now):InputIdleSampler {public override double? GetLastInputMs(double _) => now();}
 sealed class Foreground:ForegroundAppSampler {public int Calls;public override string? TryGetForegroundAppId(out bool failed){Calls++;failed=false;return "editor";}}
}

