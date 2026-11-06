using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using DesktopPet.App;
using DesktopPet.Core.Animation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Point = System.Windows.Point;

namespace DesktopPet.App.Tests;

[TestClass]
public class DragFeedbackIntegrationTests
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static void Invoke(MainWindow w,string method) => typeof(MainWindow).GetMethod(method,Private)!.Invoke(w,method=="OnUpdateTick"?[null,EventArgs.Empty]:null);
    private static void Tick(MainWindow w)=>Invoke(w,"OnUpdateTick");
    private static DragReleaseFeedback Feedback(MainWindow w)=>(DragReleaseFeedback)typeof(MainWindow).GetField("_dragRelease",Private)!.GetValue(w)!;
    private static string ImagePath(MainWindow w)=>((BitmapImage)((Image)w.FindName("PetImage")).Source).UriSource.LocalPath;
    private static void BeginBody(MainWindow w) => Assert.IsTrue(w.BeginCapturedPointerInteraction(new Point(60,65),new Point(w.Left+60,w.Top+65)));

    [STATestMethod]
    public void SharedGestureEntry_StopsPositionImmediately_OnlyFaceSettles()
    {
        var w=new MainWindow(); double now=0;
        try
        {
            w.TestGetElapsedMs=()=>now; w.TestGetCursorPos=()=>(900,900);
            BeginBody(w); StringAssert.EndsWith(ImagePath(w),"drag_gentle.png");
            Assert.IsTrue(w.IsDragging, "Establish a live drag before checking release.");
            w.ProcessCapturedPointerMove(new Point(75,75),new Point(175,175),true,10);
            now=20; w.EndCapturedPointerInteraction();
            double left=w.Left,top=w.Top;
            Assert.IsFalse(w.IsDragging);
            Assert.AreEqual("idle",w.CurrentAction); Assert.IsTrue(Feedback(w).IsActive);
            StringAssert.EndsWith(ImagePath(w),"drag_gentle.png");
            now=110; Tick(w); StringAssert.EndsWith(ImagePath(w),"release_settle.png");
            now=200; Tick(w); StringAssert.EndsWith(ImagePath(w),"idle_0.png");
            now=270; Tick(w); Assert.IsFalse(Feedback(w).IsActive);
            Assert.AreEqual(left,w.Left); Assert.AreEqual(top,w.Top);
            Assert.AreEqual(0.0,w.CurrentFrameOffset.y);
        }
        finally {w.Close();}
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NewBodyOrHeadPress_ImmediatelyCancelsRelease(bool head)
    {
        var w=new MainWindow(); double now=0;
        try
        {
            w.TestGetElapsedMs=()=>now; w.TestGetCursorPos=()=>(900,900);
            BeginBody(w); w.EndCapturedPointerInteraction(); now=100; Tick(w);
            var local=new Point(60,head?25:65);
            Assert.IsTrue(w.BeginCapturedPointerInteraction(local,new Point(w.Left+local.X,w.Top+local.Y)));
            Assert.IsFalse(Feedback(w).IsActive);
            Assert.AreEqual(head,w.IsPettingCandidate); Assert.AreEqual(!head,w.IsDragging);
            StringAssert.EndsWith(ImagePath(w),head?"idle_0.png":"drag_gentle.png");
        }
        finally {w.Close();}
    }

    [STATestMethod]
    [DataRow("menu")]
    [DataRow("grievance")]
    public void OtherOwner_CancelsFeedback_AndOldReleaseNeverReturns(string owner)
    {
        var w=new MainWindow(); double now=0;
        try
        {
            w.TestGetElapsedMs=()=>now; w.TestGetCursorPos=()=>(900,900);
            BeginBody(w); w.EndCapturedPointerInteraction();
            if(owner=="menu") typeof(MainWindow).GetField("_isContextMenuOpen",Private)!.SetValue(w,true);
            else ((AnimationPlayer)typeof(MainWindow).GetField("_animPlayer",Private)!.GetValue(w)!).PlayAction("grievance");
            now=50; Tick(w); Assert.IsFalse(Feedback(w).IsActive);
            StringAssert.EndsWith(ImagePath(w),owner=="menu"?"idle_0.png":"grievance_0.png");
            if(owner=="menu") typeof(MainWindow).GetField("_isContextMenuOpen",Private)!.SetValue(w,false);
            now=100; Tick(w); Assert.IsFalse(Feedback(w).IsActive);
        }
        finally {w.Close();}
    }

    [STATestMethod]
    public void PauseDuringRelease_FreezesImage_AndResumesRemainingFrame()
    {
        var w=new MainWindow(); double now=0;
        try
        {
            w.TestGetElapsedMs=()=>now; w.TestGetCursorPos=()=>(900,900);
            BeginBody(w); w.EndCapturedPointerInteraction(); now=100; Tick(w); w.PauseAnimation();
            now=90000; Tick(w); StringAssert.EndsWith(ImagePath(w),"release_settle.png");
            w.ResumeAnimation(); Tick(w); StringAssert.EndsWith(ImagePath(w),"release_settle.png");
            now=90080; Tick(w); StringAssert.EndsWith(ImagePath(w),"idle_0.png");
            now=90150; Tick(w); Assert.IsFalse(Feedback(w).IsActive);
        }
        finally {w.Close();}
    }

    [STATestMethod]
    public void EndDragWithoutActualDrag_DoesNotInventRelease()
    {
        var w=new MainWindow();
        try {Invoke(w,"EndDrag"); Assert.IsFalse(Feedback(w).IsActive); StringAssert.EndsWith(ImagePath(w),"idle_0.png");}
        finally {w.Close();}
    }
}
