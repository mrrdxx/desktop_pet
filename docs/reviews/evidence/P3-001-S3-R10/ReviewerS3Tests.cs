using System;
using System.Linq;
using System.Reflection;
using DesktopPet.App.Persistence;
using DesktopPet.App;
using DesktopPet.Core.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;
[TestClass]
public class ReviewerS3Tests
{
    private string _dir = "";
    private App Setup()
    {
        var app = (App?)System.Windows.Application.Current ?? new App();
        _dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(), "review-s3-" + Guid.NewGuid()); app.TestSetupIsolatedSave(_dir);
        app.TestState!.Pets = new() { new() { PetId = "bear-1", WindowLeft=100, WindowTop=100 }, new() { PetId="bear-2", WindowLeft=300, WindowTop=100 } };
        foreach (var pet in app.TestState.Pets) app.CreatePetWindow(pet);
        return app;
    }
    [STATestMethod]
    public void Reviewer_Readd_MustNotReuseRemovedId()
    {
        var app=Setup();
        try {
            Assert.IsTrue(app.RemovePet("bear-2"));
            Assert.IsTrue(app.AddPet());
            var added=app.TestState!.Pets.Single(p=>p.PetId!="bear-1");
            Assert.AreNotEqual("bear-2", added.PetId, "Removed identity must not be reused.");
        } finally { app.TestCleanup(); }
    }
    [STATestMethod]
    public void Reviewer_RemoveFirst_MustRetainOriginalPet()
    {
        var app=Setup();
        try {
            var removed=app.RemovePet("bear-1");
            Assert.IsFalse(removed, "R-05 allows removal of the second pet, retaining the original.");
        } finally { app.TestCleanup(); }
    }
    [STATestMethod]
    public void Reviewer_Recover_MustCaptureOffsetPosition()
    {
        var app=Setup();
        try {
            app.OnRecoverRequested(null, EventArgs.Empty);
            var b=app.FindWindowByPetId("bear-2")!;
            var saved=app.TestState!.Pets.Single(p=>p.PetId=="bear-2");
            Assert.AreEqual(b.Left, saved.WindowLeft, "Recover must mark the final offset position dirty.");
            var coordinator=(SaveCoordinator)typeof(App).GetField("_saveCoordinator",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(app)!;
            coordinator.Flush();
            var (loaded,_,_)=new ArchiveService(_dir).LoadState();
            Assert.AreEqual(b.Left,loaded.Pets.Single(p=>p.PetId=="bear-2").WindowLeft, "File must contain final recover position without recapture.");
        } finally { app.TestCleanup(); }
    }
}

