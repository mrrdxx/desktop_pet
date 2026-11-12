using DesktopPet.App;
using DesktopPet.Core.Persistence;
using System.IO;
using System.Windows;
using System.Windows.Threading;
class Program {
 [STAThread]
 static int Main(string[] args) {
  var data=Path.GetFullPath(args[0]); Directory.CreateDirectory(data);
  var marker=Path.Combine(data,"lifecycle.txt");
  var app=new App();
  app.ProbeSetupIsolated(data,"Local\\DesktopPet.Reviewer."+Guid.NewGuid().ToString("N"));
  app.TestSetupIsolatedSave(data);
  app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  app.Exit+=(_,e)=>File.AppendAllText(marker,$"OnExit:{e.ApplicationExitCode}\n");
  app.Startup+=(_,_)=> {
   File.AppendAllText(marker,"Startup\n");
   app.TestState!.Pets=new(){new(){PetId="bear-1",WindowLeft=150,WindowTop=120,Grievance=61},new(){PetId="bear-2",WindowLeft=350,WindowTop=220,Grievance=72}};
   foreach(var pet in app.TestState.Pets) {app.CreatePetWindow(pet);app.FindWindowByPetId(pet.PetId)!.Show();}
   app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>{
    File.AppendAllText(marker,"ProductionShutdownRequested\n");
    app.ShutdownMainWindow();
    File.AppendAllText(marker,"ProductionShutdownCallReturned\n");
   }));
  };
  File.AppendAllText(marker,"BeforeApplicationRun\n");
  var code=app.Run();
  File.AppendAllText(marker,$"ApplicationRunReturned:{code}\n");
  return code;
 }
}
