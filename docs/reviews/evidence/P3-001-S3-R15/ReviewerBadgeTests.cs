using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPet.App;
using DesktopPet.App.Persistence;
using DesktopPet.Core.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;
[TestClass]
public class ReviewerBadgeTests {
 [STATestMethod]
 public void Reviewer_BadgeFactory_SaveReload_OwnershipAndLayout() {
  var app=(App?)Application.Current??new App(); app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  var dir=Path.Combine(Path.GetTempPath(),"review-badge-"+Guid.NewGuid());
  try {
   app.TestSetupIsolatedSave(dir);
   app.TestState!.Pets=new(){new(){PetId="stable-A",Personality=Personality.Proactive},new(){PetId="stable-B",Personality=Personality.SlowPaced}};
   foreach(var p in app.TestState.Pets)app.CreatePetWindow(p);
   Check(app.FindWindowByPetId("stable-A")!,false);Check(app.FindWindowByPetId("stable-B")!,true);
   app.FlushFinalState();app.TestCleanup();
   var (loaded,_,_)=new ArchiveService(dir).LoadState(); loaded.Pets.Reverse();
   app.TestSetupIsolatedSave(dir,loaded);
   foreach(var p in loaded.Pets)app.CreatePetWindow(p);
   Check(app.FindWindowByPetId("stable-A")!,false);Check(app.FindWindowByPetId("stable-B")!,true);
  } finally {app.TestCleanup();}
 }
 static void Check(MainWindow w,bool triangle) {
  Assert.AreEqual(120d,w.Width);Assert.AreEqual(120d,w.Height);
  var badge=(Canvas)w.FindName("BadgeCanvas");Assert.IsFalse(badge.IsHitTestVisible);Assert.AreEqual(1,badge.Children.Count);
  Assert.AreEqual(triangle?typeof(System.Windows.Shapes.Polygon):typeof(System.Windows.Shapes.Ellipse),badge.Children[0].GetType());
  var shape=(System.Windows.Shapes.Shape)badge.Children[0]; var fill=((SolidColorBrush)shape.Fill).Color;
  Assert.AreEqual(triangle?(byte)0xE8:(byte)0x3A,fill.R);
  Assert.AreEqual(triangle?(byte)0x3A:(byte)0xE8,fill.B);
  var root=(FrameworkElement)w.Content;root.Measure(new Size(120,120));root.Arrange(new Rect(0,0,120,120));root.UpdateLayout();
  var bitmap=new RenderTargetBitmap(120,120,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
  var output=Path.Combine(@"C:\Users\mrrdxx\Desktop\pet\artifacts\p3-001-s3-r15",triangle?"orange.png":"blue.png");
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var f=File.Create(output);encoder.Save(f);
 }
}
