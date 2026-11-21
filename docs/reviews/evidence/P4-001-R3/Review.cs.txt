using DesktopPet.App.Platform;
using DesktopPet.Core.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

[TestClass]
public class ReviewP4001R3
{
    [TestMethod]
    public void FirstValidSampleAfterMissingOnlyReestablishesBaseline()
    {
        var session = new ActivitySession();
        session.Tick(0, 0);
        session.Tick(1000, 1000);
        session.Tick(2000, null);
        session.Tick(3000, 3000);
        Assert.AreEqual(1000d, session.AccumulatedActiveMs);
        session.Tick(4000, 4000);
        Assert.AreEqual(2000d, session.AccumulatedActiveMs);
    }

    [TestMethod]
    public void RecentInputBeforeClockOriginRemainsValid()
    {
        var session = new ActivitySession();
        var sampler = new BeforeOriginSampler();
        var firstInput = sampler.GetLastInputMs(100);
        Assert.AreEqual(-900d, firstInput);
        session.Tick(100, firstInput);
        sampler.CurrentTick += 1000;
        session.Tick(1100, sampler.GetLastInputMs(1100));
        Assert.AreEqual(1000d, session.AccumulatedActiveMs);
    }

    private sealed class BeforeOriginSampler : InputIdleSampler
    {
        public uint CurrentTick = 10000;

        internal override uint? GetLastInputTickMs() => 9000;
        internal override uint? GetCurrentTickMs() => CurrentTick;
    }
}
