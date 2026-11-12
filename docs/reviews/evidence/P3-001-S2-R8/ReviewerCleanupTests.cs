using System;
using System.Collections;
using System.Reflection;
using System.Windows.Threading;
using DesktopPet.App;
using DesktopPet.Core.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;
[TestClass]
public class ReviewerCleanupTests
{
    [STATestMethod]
    public void Reviewer_UnshownAndAlreadyClosed_CleanupStopsTimersAndClearsDictionary()
    {
        var app = (App?)System.Windows.Application.Current ?? new App();
        MainWindow? a = null, b = null;
        try
        {
            app.TestSetupIsolatedSave(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "review-r8-" + Guid.NewGuid()));
            app.TestState!.Pets = new() { new() { PetId = "bear-1" }, new() { PetId = "bear-2" } };
            foreach (var pet in app.TestState.Pets) app.CreatePetWindow(pet);
            a = app.FindWindowByPetId("bear-1")!;
            b = app.FindWindowByPetId("bear-2")!;
            var timerField = typeof(MainWindow).GetField("_updateTimer", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var ta = (DispatcherTimer)timerField.GetValue(a)!;
            var tb = (DispatcherTimer)timerField.GetValue(b)!;
            Assert.IsFalse(a.IsVisible); Assert.IsFalse(b.IsVisible);
            Assert.IsTrue(ta.IsEnabled); Assert.IsTrue(tb.IsEnabled);
            // Retain an already-closed window in the registration dictionary.
            var handler = (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), app,
                typeof(App).GetMethod("OnPetWindowClosed", BindingFlags.NonPublic | BindingFlags.Instance)!);
            a.Closed -= handler;
            a.Close();
            Assert.IsFalse(ta.IsEnabled);
            app.TestCleanup();
            Assert.IsFalse(ta.IsEnabled); Assert.IsFalse(tb.IsEnabled);
            var dictionary = (IDictionary)typeof(App).GetField("_petWindows", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
            Assert.AreEqual(0, dictionary.Count);
            app.TestCleanup();
            Assert.IsFalse(tb.IsEnabled);
        }
        finally
        {
            a?.Close(); b?.Close(); app.TestCleanup();
        }
    }
}
