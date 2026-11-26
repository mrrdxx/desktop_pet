using System.IO;
using System.Text.Json;
using DesktopPet.App.Platform;
using DesktopPet.Core.Events;

class Program
{
 static readonly List<object> Results = new();
 static string Root = "";
 static void Check(string name, object expected, object actual, bool pass) => Results.Add(new {name, expected, actual, pass});
 static double Sum(UsageStatsCollector c) => c.Segments.Sum(x=>x.DurationMs);
 [STAThread] static void Main(string[] args)
 {
  Root=Path.GetFullPath(args[0]); Directory.CreateDirectory(Root);
  double now=0; var day=new DateTimeOffset(2026,11,26,12,0,0,TimeSpan.FromHours(8));
  UsageStatsCollector New()=>new(()=>now,()=>day.AddMilliseconds(now));
  foreach(var missing in new double?[]{null,double.NaN,double.PositiveInfinity,100000})
  {
   now=0;var c=New();c.Enable();c.SampleTick("editor",missing);now=5000;c.SampleTick("editor",missing);now=10000;c.SampleTick("browser",missing);
   Check("Invalid input "+(missing?.ToString()??"null"),0,Sum(c),Sum(c)==0);
  }
  now=0;var failed=New();failed.Enable();failed.SampleTick("editor",0);now=5000;failed.SampleTick("editor",5000);now=10000;failed.SampleTick(null,10000,true);
  Check("Failed foreground interval must not extend known app",5000,Sum(failed),Sum(failed)==5000);
  now=0;var gap=New();gap.Enable();gap.SampleTick("editor",0);now=5000;gap.SampleTick("editor",5000);now=10000;gap.SampleTick("editor",10000);now=60000;gap.SampleTick("editor",60000);gap.Disable();
  Check("Long gap preserves previously verified duration",10000,Sum(gap),Sum(gap)==10000);
  foreach(var mode in new[]{"pause","lock","sleep","overlap"})
  {
   now=0;var c=New();var dir=Path.Combine(Root,mode);using var s=new UsageStatsService(c,new Idle(),new Foreground(),MonotonicClock.Instance,null,dir,false);
   s.Start();s.Enable();s.SampleTickManually();now=5000;s.SampleTickManually();
   if(mode=="pause")s.OnPauseToggled(true);else if(mode=="lock")s.OnSessionSwitch(7);else if(mode=="sleep")s.OnPowerModeChanged(3);else {s.OnPauseToggled(true);s.OnSessionSwitch(7);s.OnSessionSwitch(8);}
   var before=Sum(c);now=10000;s.SampleTickManually();now=15000;s.SampleTickManually();now=20000;s.Disable();
   Check("No accumulation while "+mode,before,Sum(c),Sum(c)==before);
  }
  now=0;var active=New();var activeDir=Path.Combine(Root,"active-save");using(var s=new UsageStatsService(active,new Idle(),new Foreground(),MonotonicClock.Instance,null,activeDir,false))
  {s.Start();s.Enable();s.OnAppStart();s.SampleTickManually();now=5000;s.SampleTickManually();now=10000;s.SampleTickManually();
   using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(activeDir,"stats.json")));var count=doc.RootElement.GetProperty("segments").GetArrayLength();Check("Verified active duration checkpointed before shutdown",">0",count,count>0);}
  var corruptDir=Path.Combine(Root,"corrupt-clear");Directory.CreateDirectory(corruptDir);File.WriteAllText(Path.Combine(corruptDir,"stats.json"),"{\"privateHistory\":\"old-sentinel\", BROKEN");
  now=0;using(var s=new UsageStatsService(New(),new Idle(),new Foreground(),MonotonicClock.Instance,null,corruptDir,false)){s.Start();s.ClearAll();var retained=Directory.GetFiles(corruptDir).Where(p=>File.ReadAllText(p).Contains("old-sentinel")).Select(Path.GetFileName).ToArray();Check("Clear removes preserved historical payload",0,retained,retained.Length==0);}
  now=0;var ioDir=Path.Combine(Root,"io");using(var s=new UsageStatsService(New(),new Idle(),new Foreground(),MonotonicClock.Instance,null,ioDir,false)){s.Start();s.Enable();s.SampleTickManually();Directory.CreateDirectory(Path.Combine(ioDir,"stats.json"));now=5000;s.SampleTickManually();now=10000;string? error=null;try{s.Disable();}catch(Exception e){error=e.GetType().Name;}Check("Optional statistics I/O failure contained", "no uncaught exception",error??"none",error==null);Directory.Delete(Path.Combine(ioDir,"stats.json"));}
  Console.WriteLine(JsonSerializer.Serialize(Results,new JsonSerializerOptions{WriteIndented=true}));
 }
 sealed class Idle:InputIdleSampler {internal override uint? GetLastInputTickMs()=>100; internal override uint? GetCurrentTickMs()=>100;}
 sealed class Foreground:ForegroundAppSampler {public override string? TryGetForegroundAppId(out bool failed){failed=false;return "editor";}}
}
