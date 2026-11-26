using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.App.Platform;
using DesktopPet.Core.Events;
using DesktopPet.Core.Persistence;
using DesktopPet.Core.Social;

class Program
{
    static readonly DateTimeOffset Now=new(2026,11,27,9,35,0,TimeSpan.FromHours(8));
    static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
    static void Pump(double milliseconds) {var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    [STAThread] static int Main(string[] args) {
        string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);var observations=new List<object>();
        var app=new App{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        MainWindow? pet=null;StatsBubbleWindow? bubble=null;
        try {
            var coordinator=new SocialCoordinator(new SocialConfig{NoticeProbability=1,SlowPacedNoticeProbability=1,MinIdleMs=0,NoticeIntervalMs=0,NoticeDistanceMaxDip=9999},new Random(42));
            PetSocialState State(string id,double x)=>new(id,Personality.Proactive,x,100,false,false,false,false,false,false,0);
            var start=coordinator.Tick(10000,State("a",100),State("b",200));
            pet=new MainWindow{Left=-9500,Top=-9500,TestGetLocalNow=()=>Now.DateTime,StateContext=new PetStateContext{PetId="b",SocialCoordinator=coordinator}};
            pet.Show();pet.Left=-9500;pet.Top=-9500;
            bool shared=(bool)typeof(MainWindow).GetProperty("IsInSharedSocialSession",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(pet)!;
            bool local=pet.IsSocialActive;bool available=pet.IsAvailableForStatsBubble;
            var prediction=new UsageStatsFrequencyResult{IsEnabled=true,Candidates=new[]{new UsageStatsFrequencyCandidate{AppId="notepad",SlotStart="09:30",SupportDateCount=5,SessionCount=5}}};
            var decision=UsageStatsBubblePlanner.Decide(prediction,Now,null,false,available);
            observations.Add(new{scenario="shared-session-before-pet-tick",phase=start.Session.Phase.ToString(),sharedActive=shared,localSocialFlag=local,availableForBubble=available,plannerWouldShow=decision.ShouldShow,pass=shared&&!available&&!decision.ShouldShow});
            coordinator.ForceCancel(CancelReason.RecoverRequested,10001);
            observations.Add(new{scenario="no-session-control",availableForBubble=pet.IsAvailableForStatsBubble,pass=pet.IsAvailableForStatsBubble});
            pet.Close();pet=null;

            string dir=Path.Combine(root,"summary");Directory.CreateDirectory(dir);
            var old=Now.AddDays(-30);var archive=new UsageStatsArchive{Buckets=new(){new(){AppId="notepad",Date=old.ToString("yyyy-MM-dd"),BucketStartLocal=old.ToString("O"),ActiveMs=5000,SessionCount=1},new(){AppId="notepad",Date=Now.AddDays(-1).ToString("yyyy-MM-dd"),BucketStartLocal=Now.AddDays(-1).ToString("O"),ActiveMs=5000,SessionCount=1}}};
            File.WriteAllText(Path.Combine(dir,"stats.json"),JsonSerializer.Serialize(archive,Json));
            using(var service=new UsageStatsService(new UsageStatsCollector(()=>0,()=>Now),new InputIdleSampler(),new ForegroundAppSampler(),MonotonicClock.Instance,dispatcher:null,saveDirectory:dir,autoStartTimer:false,localNow:()=>Now)) {
                service.Start();var before=service.GetRecordedSummary();service.DrainPending();var after=service.GetRecordedSummary();
                observations.Add(new{scenario="viewer-summary-before-after-normal-prune",before,after,pass=before.RecordedDateCount==after.RecordedDateCount&&before.ApproximateForegroundActiveMs==after.ApproximateForegroundActiveMs});
            }

            // Off-screen only: no activation, no cursor injection. Pump the real WPF timer.
            bubble=new StatsBubbleWindow();bubble.ShowNear("审查用隔离气泡",new Rect(-9500,-9500,120,120),new Rect(-12000,-12000,4000,4000));
            var watch=Stopwatch.StartNew();bool initiallyVisible=bubble.IsVisible;Pump(8500);bool hidden=!bubble.IsVisible;
            observations.Add(new{scenario="real-wpf-dispatcher-autohide-offscreen",initiallyVisible,hiddenAfterPump=hidden,elapsedMs=watch.ElapsedMilliseconds,pass=initiallyVisible&&hidden});
        } catch(Exception ex) {observations.Add(new{scenario="harness-error",error=ex.ToString(),pass=false});}
        finally {pet?.Close();bubble?.Close();}
        string output=JsonSerializer.Serialize(observations,Json);File.WriteAllText(Path.Combine(root,"observations.json"),output);Console.WriteLine(output);return 0;
    }
}
