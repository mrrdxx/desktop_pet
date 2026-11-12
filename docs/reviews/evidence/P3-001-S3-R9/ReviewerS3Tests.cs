using System;
using System.Linq;
using DesktopPet.App;
using DesktopPet.Core.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;
[TestClass]
public class ReviewerS3Tests
{
    private App Setup()
    {
        var app = (App?)System.Windows.Application.Current ?? new App();
        app.TestSetupIsolatedSave(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "review-s3-" + Guid.NewGuid()));
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
        } finally { app.TestCleanup(); }
    }
}
