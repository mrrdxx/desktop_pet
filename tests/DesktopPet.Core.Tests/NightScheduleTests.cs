using DesktopPet.Core.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests;

/// <summary>
/// P4-003: boundary tests for the night sleepy window
/// (23:30 inclusive → 06:00 exclusive), including midnight cross-over.
/// </summary>
[TestClass]
public sealed class NightScheduleTests
{
    [TestMethod]
    public void IsNight_JustBeforeStart_2329_IsDay()
    {
        Assert.IsFalse(NightSchedule.IsNight(new TimeOnly(23, 29)));
    }

    [TestMethod]
    public void IsNight_AtStart_2330_IsNight()
    {
        Assert.IsTrue(NightSchedule.IsNight(new TimeOnly(23, 30)));
    }

    [TestMethod]
    public void IsNight_BeforeMidnight_2359_IsNight()
    {
        Assert.IsTrue(NightSchedule.IsNight(new TimeOnly(23, 59, 59, 999)));
    }

    [TestMethod]
    public void IsNight_AtMidnight_IsNight()
    {
        Assert.IsTrue(NightSchedule.IsNight(new TimeOnly(0, 0)));
    }

    [TestMethod]
    public void IsNight_DeepNight_0300_IsNight()
    {
        Assert.IsTrue(NightSchedule.IsNight(new TimeOnly(3, 0)));
    }

    [TestMethod]
    public void IsNight_JustBeforeEnd_0559_IsNight()
    {
        Assert.IsTrue(NightSchedule.IsNight(new TimeOnly(5, 59, 59, 999)));
    }

    [TestMethod]
    public void IsNight_AtEnd_0600_IsDay()
    {
        Assert.IsFalse(NightSchedule.IsNight(new TimeOnly(6, 0)));
    }

    [TestMethod]
    public void IsNight_Noon_IsDay()
    {
        Assert.IsFalse(NightSchedule.IsNight(new TimeOnly(12, 0)));
    }
}
