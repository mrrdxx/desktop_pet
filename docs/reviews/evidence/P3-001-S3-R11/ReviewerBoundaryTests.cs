using System;
using System.Linq;
using DesktopPet.App;
using DesktopPet.App.Platform;
using DesktopPet.Core.Geometry;
using DesktopPet.Core.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;
[TestClass]
public class ReviewerBoundaryTests {
 [STATestMethod]
 public void Reviewer_AddAtFourCorners_AndRecover_FullWindowsFit() {
  var ms=MonitorInterop.EnumerateMonitors();
  Assert.IsTrue(ms.Count>0);
  var m=MonitorLocator.FindPrimaryMonitor(ms);
  var left=m.WorkArea.Bounds.Left/m.DpiScaleX;
  var right=m.WorkArea.Bounds.Right/m.DpiScaleX;
  var top=m.WorkArea.Bounds.Top/m.DpiScaleY;
  var bottom=m.WorkArea.Bounds.Bottom/m.DpiScaleY;
  foreach(var corner in new[]{(left+1,top+1),(right-121,top+1),(left+1,bottom-121),(right-121,bottom-121)}) {
   var app=(App?)System.Windows.Application.Current??new App(); app.ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown;
   try {
    app.TestSetupIsolatedSave(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"review-boundary-"+Guid.NewGuid()));
    app.TestState!.Pets=new(){new(){PetId="bear-1",WindowLeft=corner.Item1,WindowTop=corner.Item2}};
    app.CreatePetWindow(app.TestState.Pets[0]);
    var a=app.FindWindowByPetId("bear-1")!; a.Show(); app.MarkStateDirty();
    Assert.IsTrue(app.AddPet()); Console.WriteLine($"corner={corner} windows="+string.Join(";",app.AllPetWindows.Select(w=>$"{w.StateContext!.PetId}:{w.Left},{w.Top}"))+" archive="+string.Join(";",app.TestState!.Pets.Select(p=>$"{p.PetId}:{p.WindowLeft},{p.WindowTop}")));
    Check(app,left,right,top,bottom);
    app.OnRecoverRequested(null,EventArgs.Empty);
    Check(app,left,right,top,bottom);
   } finally {app.TestCleanup();}
  }
 }
 static void Check(App app,double left,double right,double top,double bottom) {
  var ws=app.AllPetWindows.ToArray(); Assert.AreEqual(2,ws.Length);
  foreach(var w in ws) {
   Assert.IsTrue(w.Left>=left-0.1 && w.Left+w.Width<=right+0.1,$"Horizontal boundary {w.Left}+{w.Width} in {left}..{right}");
   Assert.IsTrue(w.Top>=top-0.1 && w.Top+w.Height<=bottom+0.1,$"Vertical boundary {w.Top}+{w.Height} in {top}..{bottom}");
  }
  Assert.IsFalse(ws[0].Left==ws[1].Left && ws[0].Top==ws[1].Top, $"Two windows fully overlap at ({ws[0].Left},{ws[0].Top}) after AddPet/recover.");
 }
}




