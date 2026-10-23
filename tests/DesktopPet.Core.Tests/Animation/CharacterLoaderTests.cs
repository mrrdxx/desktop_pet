using DesktopPet.Core.Animation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests.Animation;

[TestClass]
public class CharacterLoaderTests
{
    private static readonly string SampleJson = """
    {
      "id": "test-pet",
      "frameWidth": 120,
      "frameHeight": 120,
      "footAnchorX": 60,
      "footAnchorY": 110,
      "actions": {
        "idle": {
          "loop": "loop",
          "frames": [
            { "imagePath": "idle_0.png", "durationMs": 800, "offsetX": 0, "offsetY": 0 },
            { "imagePath": "idle_1.png", "durationMs": 200, "offsetX": 0, "offsetY": 0 }
          ]
        },
        "dodge": {
          "loop": "once",
          "frames": [
            { "imagePath": "dodge_0.png", "durationMs": 600, "offsetX": 0, "offsetY": 0 }
          ]
        }
      }
    }
    """;

    [TestMethod]
    public void Load_ValidJson_ReturnsCharacterWithCorrectFields()
    {
        var def = CharacterLoader.Load(SampleJson);

        Assert.AreEqual("test-pet", def.Id);
        Assert.AreEqual(120, def.FrameWidth);
        Assert.AreEqual(120, def.FrameHeight);
        Assert.AreEqual(60, def.FootAnchorX);
        Assert.AreEqual(110, def.FootAnchorY);
        Assert.AreEqual(2, def.Actions.Count);
    }

    [TestMethod]
    public void Load_ValidJson_ParsesActionsAndFrames()
    {
        var def = CharacterLoader.Load(SampleJson);

        Assert.IsTrue(def.Actions.ContainsKey("idle"));
        Assert.IsTrue(def.Actions.ContainsKey("dodge"));

        var idle = def.Actions["idle"];
        Assert.AreEqual(2, idle.Frames.Count);
        Assert.AreEqual(LoopMode.Loop, idle.Loop);
        Assert.AreEqual("idle_0.png", idle.Frames[0].ImagePath);
        Assert.AreEqual(800, idle.Frames[0].DurationMs);

        var dodge = def.Actions["dodge"];
        Assert.AreEqual(1, dodge.Frames.Count);
        Assert.AreEqual(LoopMode.Once, dodge.Loop);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidDataException))]
    public void Load_NoActions_Throws()
    {
        var json = """{"id":"x","frameWidth":120,"frameHeight":120,"actions":{}}""";
        CharacterLoader.Load(json);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidDataException))]
    public void Load_NoIdleAction_Throws()
    {
        var json = """
        {
          "id": "x",
          "frameWidth": 120,
          "frameHeight": 120,
          "actions": {
            "walk": { "loop": "loop", "frames": [{"imagePath":"w.png","durationMs":100}] }
          }
        }
        """;
        CharacterLoader.Load(json);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidDataException))]
    public void Load_EmptyFrames_Throws()
    {
        var json = """
        {
          "id": "x",
          "frameWidth": 120,
          "frameHeight": 120,
          "actions": {
            "idle": { "loop": "loop", "frames": [] }
          }
        }
        """;
        CharacterLoader.Load(json);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidDataException))]
    public void Load_AbsoluteImagePath_Throws()
    {
        var json = """
        {
          "id": "x",
          "frameWidth": 120,
          "frameHeight": 120,
          "actions": {
            "idle": { "loop": "loop", "frames": [{"imagePath":"C:\\\\abs\\\\path.png","durationMs":100}] }
          }
        }
        """;
        CharacterLoader.Load(json);
    }

    [TestMethod]
    public void Load_DefaultFootAnchor_WhenNotSpecified()
    {
        var json = """
        {
          "id": "x",
          "frameWidth": 100,
          "frameHeight": 80,
          "actions": {
            "idle": { "loop": "loop", "frames": [{"imagePath":"i.png","durationMs":100}] }
          }
        }
        """;
        var def = CharacterLoader.Load(json);
        // FootAnchorX defaults to FrameWidth/2, FootAnchorY to FrameHeight-10
        Assert.AreEqual(50, def.FootAnchorX);
        Assert.AreEqual(70, def.FootAnchorY);
    }

    [TestMethod]
    public void Load_PingPongLoop_Parsed()
    {
        var json = """
        {
          "id": "x",
          "frameWidth": 120,
          "frameHeight": 120,
          "actions": {
            "idle": { "loop": "pingpong", "frames": [{"imagePath":"i.png","durationMs":100}] }
          }
        }
        """;
        var def = CharacterLoader.Load(json);
        Assert.AreEqual(LoopMode.PingPong, def.Actions["idle"].Loop);
    }

    [TestMethod]
    public void Load_DefaultDuration_WhenZero()
    {
        var json = """
        {
          "id": "x",
          "frameWidth": 120,
          "frameHeight": 120,
          "actions": {
            "idle": { "loop": "loop", "frames": [{"imagePath":"i.png","durationMs":0}] }
          }
        }
        """;
        var def = CharacterLoader.Load(json);
        Assert.AreEqual(200, def.Actions["idle"].Frames[0].DurationMs);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidDataException))]
    public void Load_PathTraversal_Throws()
    {
        var json = """
        {
          "id": "x",
          "frameWidth": 120,
          "frameHeight": 120,
          "actions": {
            "idle": { "loop": "loop", "frames": [{"imagePath":"../outside.png","durationMs":100}] }
          }
        }
        """;
        CharacterLoader.Load(json);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidDataException))]
    public void Load_NegativeFrameWidth_Throws()
    {
        var json = """
        {
          "id": "x",
          "frameWidth": -10,
          "frameHeight": 120,
          "actions": {
            "idle": { "loop": "loop", "frames": [{"imagePath":"i.png","durationMs":100}] }
          }
        }
        """;
        CharacterLoader.Load(json);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidDataException))]
    public void Load_ZeroFrameHeight_Throws()
    {
        var json = """
        {
          "id": "x",
          "frameWidth": 120,
          "frameHeight": 0,
          "actions": {
            "idle": { "loop": "loop", "frames": [{"imagePath":"i.png","durationMs":100}] }
          }
        }
        """;
        CharacterLoader.Load(json);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidDataException))]
    public void Load_FootAnchorOutOfBounds_Throws()
    {
        var json = """
        {
          "id": "x",
          "frameWidth": 120,
          "frameHeight": 120,
          "footAnchorX": 200,
          "footAnchorY": 110,
          "actions": {
            "idle": { "loop": "loop", "frames": [{"imagePath":"i.png","durationMs":100}] }
          }
        }
        """;
        CharacterLoader.Load(json);
    }

    [TestMethod]
    public void Load_NestedRelativePath_Accepted()
    {
        var json = """
        {
          "id": "x",
          "frameWidth": 120,
          "frameHeight": 120,
          "actions": {
            "idle": { "loop": "loop", "frames": [{"imagePath":"subdir/i.png","durationMs":100}] }
          }
        }
        """;
        var def = CharacterLoader.Load(json);
        Assert.AreEqual("subdir/i.png", def.Actions["idle"].Frames[0].ImagePath);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidDataException))]
    public void Load_SiblingDirectoryPrefix_Throws()
    {
        // F-03: A path like "../pet-sibling/outside.png" must be rejected
        // even if the sibling directory name shares a prefix with the base
        // directory (e.g. "pet" vs "pet-sibling"). The fix ensures the
        // base directory comparison includes a trailing separator.
        var json = """
        {
          "id": "x",
          "frameWidth": 120,
          "frameHeight": 120,
          "actions": {
            "idle": { "loop": "loop", "frames": [{"imagePath":"../pet-sibling/outside.png","durationMs":100}] }
          }
        }
        """;
        CharacterLoader.Load(json);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidDataException))]
    public void Load_ParentDirectoryTraversal_Throws()
    {
        // F-03: Any path that escapes upward must be rejected.
        var json = """
        {
          "id": "x",
          "frameWidth": 120,
          "frameHeight": 120,
          "actions": {
            "idle": { "loop": "loop", "frames": [{"imagePath":"../../escape.png","durationMs":100}] }
          }
        }
        """;
        CharacterLoader.Load(json);
    }
}
