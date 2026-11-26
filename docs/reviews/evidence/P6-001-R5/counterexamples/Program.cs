using System.IO;
using System.Text.Json;
using DesktopPet.App;
using DesktopPet.App.Persistence;
class Program
{
 [STAThread] static void Main(string[] args)
 {
  var dir=Path.GetFullPath(args[0]);Directory.CreateDirectory(dir);
  File.WriteAllText(Path.Combine(dir,"settings.json"),"{\"schemaVersion\":1,\"usageStatsEnabled\":true}");
  var saved=new ArchiveService(dir).LoadSettings().Settings.UsageStatsEnabled;
  var app=new App();try{
   app.ProbeSetupIsolated(dir,"Local\\PetP6Review_"+Guid.NewGuid().ToString("N"));
   app.TestSetupIsolatedSave(dir);
   app.SetUsageStatsServiceForTest(null);
   app.InitializeUsageStatsForIsolatedProbe(dir);
   Console.WriteLine(JsonSerializer.Serialize(new {
    windowCreated=app.MainWindow is not null,
    persistedEnabledBeforeSetup=saved,
    observedEnabledAfterScenarioSetup=app.IsUsageStatsEnabled,
    restartToggleRestored=saved==app.IsUsageStatsEnabled,
    systemEventsSubscribed=app.TestSystemEventsSubscribed
   },new JsonSerializerOptions{WriteIndented=true}));
  }finally{app.TestCleanup();}
 }
}
