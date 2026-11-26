using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using DesktopPet.App;
using DesktopPet.App.Platform;
using DesktopPet.Core.Events;
using DesktopPet.Core.Persistence;

class Program
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    static App app=null!;static string root="";static DateTimeOffset now=new(2026,11,27,9,35,0,TimeSpan.FromHours(8));static int clicks;static bool hold;static MainWindow pet=null!;
    static void Log(object payload)=>File.AppendAllText(Path.Combine(root,"events.jsonl"),JsonSerializer.Serialize(new{utc=DateTimeOffset.UtcNow,data=payload})+Environment.NewLine);
    [STAThread] static void Main(string[] args)
    {
        root=Path.GetFullPath(args[0]);hold=args.Length>1&&args[1]=="--hold";Directory.CreateDirectory(root);
        app=new App{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.ProbeSetupIsolated(root,@"Global\P6004Reviewer."+Guid.NewGuid().ToString("N"));app.TestSetupIsolatedSave(root);
        if(!app._probeIsolatedMode) throw new InvalidOperationException("Refuse to run without isolated OnStartup guard");
        var buckets=new List<UsageStatsBucket>();for(int age=1;age<=7;age++){var d=now.AddDays(-age);buckets.Add(new(){AppId=age<=5?"notepad":"chrome",Date=d.ToString("yyyy-MM-dd"),BucketStartLocal=new DateTimeOffset(d.Year,d.Month,d.Day,9,30,0,d.Offset).ToString("O"),ActiveMs=60000,SessionCount=1});}
        File.WriteAllText(Path.Combine(root,"stats.json"),JsonSerializer.Serialize(new UsageStatsArchive{Buckets=buckets},new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        var service=new UsageStatsService(new UsageStatsCollector(()=>0,()=>now),new InputIdleSampler(),new ForegroundAppSampler(),MonotonicClock.Instance,dispatcher:null,saveDirectory:root,autoStartTimer:false,localNow:()=>now);
        app.SetUsageStatsServiceForTest(service);service.Start();app.SetUsageStatsEnabled(true);app.TestStatsBubbleLocalNow=()=>now;
        var fixture=new Window{Title="P6-004 验收底板 R3",Width=800,Height=530,Left=200,Top=180};var layout=new DockPanel{Margin=new Thickness(12)};fixture.Content=layout;
        var tools=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(tools,Dock.Top);layout.Children.Add(tools);
        void Button(string label,Action action){var b=new Button{Content=label,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,8)};b.Click+=(_,_)=>action();tools.Children.Add(b);}
        Button("展示下一日气泡",()=>{now=now.AddDays(1);var before=GetForegroundWindow();app.TryShowUsageStatsBubble();if(hold && app.TestStatsBubbleWindow is { IsVisible:true } shown) shown.ShowNear(shown.CurrentMessage,pet.GetPetRectDipForStatsBubble(),pet.GetWorkAreaDipForStatsBubble(),autoHide:false);Log(new{kind="show-request-manual-close",now,visible=app.TestStatsBubbleVisible,foregroundBefore=before.ToInt64(),foregroundAfter=GetForegroundWindow().ToInt64(),marker=service.BubbleLastShownLocalDate});});
        Button("记录当前状态",()=>Log(new{kind="checkpoint",clicks,petLeft=pet.Left,petTop=pet.Top,bubbleVisible=app.TestStatsBubbleVisible,foreground=GetForegroundWindow().ToInt64()}));
        Button("结束验收",()=>{Log(new{kind="finish",clicks});fixture.Close();app.ShutdownMainWindow();});
        var text=new TextBox{Text="这是独立验收底板。点击小熊透明角落应把点击交给这里；拖动实体应只移动小熊。\n所有统计是合成样本，不代表真实使用记录。",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalContentAlignment=VerticalAlignment.Top,Padding=new Thickness(10),FontSize=16};layout.Children.Add(text);
        text.PreviewMouseLeftButtonDown+=(_,e)=>{clicks++;var p=e.GetPosition(text);Log(new{kind="fixture-click",clicks,x=p.X,y=p.Y,petLeft=pet.Left,petTop=pet.Top});};fixture.Show();
        app.TestState!.Pets=new(){new(){PetId="bear-1",Personality=Personality.Proactive,WindowLeft=540,WindowTop=410,Grievance=0}};app.CreatePetWindow(app.TestState.Pets[0]);pet=app.FindWindowByPetId("bear-1")!;pet.TestGetLocalNow=()=>now.DateTime;pet.TestGetElapsedMs=()=>0;pet.Show();
        var sample=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};sample.Tick+=(_,_)=>{var b=app.TestStatsBubbleWindow;Log(new{kind="sample",clicks,petLeft=pet.Left,petTop=pet.Top,drag=pet.IsDragging,bubbleVisible=app.TestStatsBubbleVisible,bubbleLeft=b?.Left,bubbleTop=b?.Top,viewer=app.TestStatsViewerWindow?.IsVisible==true,foreground=GetForegroundWindow().ToInt64()});};sample.Start();
        var timeout=new DispatcherTimer{Interval=TimeSpan.FromMinutes(8)};timeout.Tick+=(_,_)=>{timeout.Stop();Log(new{kind="timeout"});fixture.Close();app.ShutdownMainWindow();};timeout.Start();
        File.WriteAllText(Path.Combine(root,"ready.json"),JsonSerializer.Serialize(new{pid=Environment.ProcessId,fixtureTitle=fixture.Title,petTitle=pet.Title,now}));
        app.Run();sample.Stop();timeout.Stop();File.WriteAllText(Path.Combine(root,"exit.json"),JsonSerializer.Serialize(new{normalExit=true,statsServiceNull=app.UsageStatsService==null}));
    }
}



