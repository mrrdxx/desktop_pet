using DesktopPet.Core.Animation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.Core.Tests.Animation;

[TestClass]
public class AnimationPlayerTests
{
    private static PetCharacterDef CreateTestCharacter()
    {
        return new PetCharacterDef
        {
            Id = "test",
            FrameWidth = 120,
            FrameHeight = 120,
            FootAnchorX = 60,
            FootAnchorY = 110,
            Actions = new Dictionary<string, ActionDef>(StringComparer.OrdinalIgnoreCase)
            {
                ["idle"] = new ActionDef
                {
                    Id = "idle",
                    Loop = LoopMode.Loop,
                    Frames = [
                        new FrameDef { ImagePath = "idle_0.png", DurationMs = 500 },
                        new FrameDef { ImagePath = "idle_1.png", DurationMs = 500 }
                    ]
                },
                ["dodge"] = new ActionDef
                {
                    Id = "dodge",
                    Loop = LoopMode.Once,
                    Frames = [
                        new FrameDef { ImagePath = "dodge_0.png", DurationMs = 300 },
                        new FrameDef { ImagePath = "dodge_1.png", DurationMs = 300 }
                    ]
                },
                ["curious"] = new ActionDef
                {
                    Id = "curious",
                    Loop = LoopMode.Loop,
                    Frames = [
                        new FrameDef { ImagePath = "curious_0.png", DurationMs = 400 }
                    ]
                }
            }
        };
    }

    [TestMethod]
    public void GetCurrentFrame_InitialState_ReturnsFirstIdleFrame()
    {
        double time = 0;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);

        var (idx, frame) = player.GetCurrentFrame();
        Assert.AreEqual(0, idx);
        Assert.AreEqual("idle_0.png", frame.ImagePath);
    }

    [TestMethod]
    public void GetCurrentFrame_Loop_AdvancesFrames()
    {
        double time = 0;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);

        // At 0ms: frame 0
        Assert.AreEqual("idle_0.png", player.GetCurrentFrame().frame.ImagePath);

        // At 500ms: frame 1
        time = 500;
        Assert.AreEqual("idle_1.png", player.GetCurrentFrame().frame.ImagePath);

        // At 1000ms: loops back to frame 0
        time = 1000;
        Assert.AreEqual("idle_0.png", player.GetCurrentFrame().frame.ImagePath);

        // At 1200ms: still frame 0 (within second cycle)
        time = 1200;
        Assert.AreEqual("idle_0.png", player.GetCurrentFrame().frame.ImagePath);
    }

    [TestMethod]
    public void PlayAction_SwitchesToNewAction()
    {
        double time = 0;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);

        player.PlayAction("curious");
        Assert.AreEqual("curious", player.CurrentActionId);
        Assert.AreEqual("curious_0.png", player.GetCurrentFrame().frame.ImagePath);
    }

    [TestMethod]
    public void PlayAction_MissingAction_FallsBackToIdle()
    {
        double time = 0;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);

        string? missingAction = null;
        player.OnActionMissing += id => missingAction = id;

        player.PlayAction("nonexistent");

        Assert.AreEqual("idle", player.CurrentActionId);
        Assert.AreEqual("nonexistent", missingAction);
    }

    [TestMethod]
    public void PlayAction_NullOrEmpty_FallsBackToIdle()
    {
        double time = 0;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);

        player.PlayAction("curious");
        player.PlayAction("");
        Assert.AreEqual("idle", player.CurrentActionId);
    }

    [TestMethod]
    public void OnceAction_CompletesAndHoldsLastFrame()
    {
        double time = 0;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);

        player.PlayAction("dodge");

        // At 0ms: first frame
        Assert.AreEqual("dodge_0.png", player.GetCurrentFrame().frame.ImagePath);
        Assert.IsFalse(player.IsCompleted);

        // At 300ms: second frame
        time = 300;
        Assert.AreEqual("dodge_1.png", player.GetCurrentFrame().frame.ImagePath);
        Assert.IsFalse(player.IsCompleted);

        // At 600ms: completed, holds last frame
        time = 600;
        Assert.AreEqual("dodge_1.png", player.GetCurrentFrame().frame.ImagePath);
        Assert.IsTrue(player.IsCompleted);
    }

    [TestMethod]
    public void OnceAction_FiresCompletionEvent()
    {
        double time = 0;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);

        string? completedAction = null;
        player.ActionCompleted += id => completedAction = id;

        player.PlayAction("dodge");

        // Not completed yet
        player.GetCurrentFrame();
        Assert.IsNull(completedAction);

        // Advance past total duration (600ms)
        time = 600;
        player.GetCurrentFrame();
        Assert.AreEqual("dodge", completedAction);
    }

    [TestMethod]
    public void PlayAction_ResetsCompletionState()
    {
        double time = 0;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);

        player.PlayAction("dodge");
        time = 600;
        player.GetCurrentFrame();
        Assert.IsTrue(player.IsCompleted);

        // Switch to idle and back
        player.PlayAction("idle");
        Assert.IsFalse(player.IsCompleted);
    }

    [TestMethod]
    public void PingPong_PlaysForwardThenBackward()
    {
        double time = 0;
        var character = new PetCharacterDef
        {
            Id = "test",
            FrameWidth = 120,
            FrameHeight = 120,
            FootAnchorX = 60,
            FootAnchorY = 110,
            Actions = new Dictionary<string, ActionDef>(StringComparer.OrdinalIgnoreCase)
            {
                ["idle"] = new ActionDef
                {
                    Id = "idle",
                    Loop = LoopMode.PingPong,
                    Frames = [
                        new FrameDef { ImagePath = "f0.png", DurationMs = 100 },
                        new FrameDef { ImagePath = "f1.png", DurationMs = 100 },
                        new FrameDef { ImagePath = "f2.png", DurationMs = 100 }
                    ]
                }
            }
        };

        var player = new AnimationPlayer(character, () => time);

        // Forward: 0→f0, 100→f1, 200→f2
        Assert.AreEqual("f0.png", player.GetCurrentFrame().frame.ImagePath);
        time = 100;
        Assert.AreEqual("f1.png", player.GetCurrentFrame().frame.ImagePath);
        time = 200;
        Assert.AreEqual("f2.png", player.GetCurrentFrame().frame.ImagePath);

        // Backward: 300→f2 (start of backward), 400→f1, 500→f0
        time = 300;
        Assert.AreEqual("f2.png", player.GetCurrentFrame().frame.ImagePath);
        time = 400;
        Assert.AreEqual("f1.png", player.GetCurrentFrame().frame.ImagePath);
        time = 500;
        Assert.AreEqual("f0.png", player.GetCurrentFrame().frame.ImagePath);

        // Cycle repeats: 600→f0 (new cycle starts)
        time = 600;
        Assert.AreEqual("f0.png", player.GetCurrentFrame().frame.ImagePath);
        time = 700;
        Assert.AreEqual("f1.png", player.GetCurrentFrame().frame.ImagePath);
    }

    [TestMethod]
    public void GetFootOffset_ReturnsFrameOffsetDirectly()
    {
        // F-04: GetFootOffset returns (OffsetX, OffsetY) directly as the
        // TranslateTransform value — no negation. The offset compensates
        // for content shifts to keep the visual foot stable.
        double time = 0;
        var character = new PetCharacterDef
        {
            Id = "test",
            FrameWidth = 120,
            FrameHeight = 120,
            FootAnchorX = 60,
            FootAnchorY = 110,
            Actions = new Dictionary<string, ActionDef>(StringComparer.OrdinalIgnoreCase)
            {
                ["idle"] = new ActionDef
                {
                    Id = "idle",
                    Loop = LoopMode.Loop,
                    Frames = [new FrameDef { ImagePath = "i.png", DurationMs = 100, OffsetX = 5, OffsetY = -3 }]
                }
            }
        };

        var player = new AnimationPlayer(character, () => time);
        var (dx, dy) = player.GetFootOffset();
        Assert.AreEqual(5, dx);
        Assert.AreEqual(-3, dy);
    }

    [TestMethod]
    public void GetFootOffset_ZeroOffset_ReturnsZero()
    {
        // F-04: A frame with zero offset should produce a zero Transform,
        // meaning no visual shift is applied.
        double time = 0;
        var character = new PetCharacterDef
        {
            Id = "test",
            FrameWidth = 120,
            FrameHeight = 120,
            FootAnchorX = 60,
            FootAnchorY = 110,
            Actions = new Dictionary<string, ActionDef>(StringComparer.OrdinalIgnoreCase)
            {
                ["idle"] = new ActionDef
                {
                    Id = "idle",
                    Loop = LoopMode.Loop,
                    Frames = [new FrameDef { ImagePath = "i.png", DurationMs = 100, OffsetX = 0, OffsetY = 0 }]
                }
            }
        };

        var player = new AnimationPlayer(character, () => time);
        var (dx, dy) = player.GetFootOffset();
        Assert.AreEqual(0, dx);
        Assert.AreEqual(0, dy);
    }

    [TestMethod]
    public void GetFootOffset_ActionSwitch_FootStaysStable()
    {
        // F-04: When switching from idle (offset 0) to curious (offset 9),
        // GetFootOffset must return the frame's offset directly so the App
        // can apply it as a TranslateTransform. The world foot position is:
        //   windowTop + transformY + footAnchorY
        // idle:   windowTop + 0   + 110 = windowTop + 110
        // curious: windowTop + 9   + 110 = windowTop + 119
        // But curious_0.png has its content drawn 9px higher (foot at y=101),
        // so the visual foot in the image is at 101. After applying transformY=9:
        //   visual foot in window = 101 + 9 = 110 — same as idle.
        // This test verifies the offset values; the App test verifies the
        // actual world foot coordinate equality.
        double time = 0;
        var character = new PetCharacterDef
        {
            Id = "test",
            FrameWidth = 120,
            FrameHeight = 120,
            FootAnchorX = 60,
            FootAnchorY = 110,
            Actions = new Dictionary<string, ActionDef>(StringComparer.OrdinalIgnoreCase)
            {
                ["idle"] = new ActionDef
                {
                    Id = "idle",
                    Loop = LoopMode.Loop,
                    Frames = [new FrameDef { ImagePath = "idle_0.png", DurationMs = 800, OffsetX = 0, OffsetY = 0 }]
                },
                ["curious"] = new ActionDef
                {
                    Id = "curious",
                    Loop = LoopMode.Loop,
                    Frames = [new FrameDef { ImagePath = "curious_0.png", DurationMs = 500, OffsetX = 0, OffsetY = 9 }]
                }
            }
        };

        var player = new AnimationPlayer(character, () => time);

        // idle: offset should be (0, 0)
        player.PlayAction("idle");
        var (idleDx, idleDy) = player.GetFootOffset();
        Assert.AreEqual(0, idleDx, "idle offset X must be 0");
        Assert.AreEqual(0, idleDy, "idle offset Y must be 0");

        // curious: offset should be (0, 9) — compensates for content drawn 9px higher
        player.PlayAction("curious");
        var (curiousDx, curiousDy) = player.GetFootOffset();
        Assert.AreEqual(0, curiousDx, "curious offset X must be 0");
        Assert.AreEqual(9, curiousDy, "curious offset Y must be 9 to compensate for content shift");

        // Switch back to idle: offset returns to (0, 0)
        player.PlayAction("idle");
        var (backDx, backDy) = player.GetFootOffset();
        Assert.AreEqual(0, backDx, "idle offset X after switch back must be 0");
        Assert.AreEqual(0, backDy, "idle offset Y after switch back must be 0");
    }

    [TestMethod]
    public void UpdateCharacter_PreservesCurrentActionIfExists()
    {
        double time = 0;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);
        player.PlayAction("curious");

        var newCharacter = CreateTestCharacter();
        newCharacter.Actions["curious"].Frames[0] =
            new FrameDef { ImagePath = "curious_new.png", DurationMs = 200 };

        player.UpdateCharacter(newCharacter);
        Assert.AreEqual("curious", player.CurrentActionId);
        Assert.AreEqual("curious_new.png", player.GetCurrentFrame().frame.ImagePath);
    }

    [TestMethod]
    public void UpdateCharacter_FallsBackToIdleIfActionRemoved()
    {
        double time = 0;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);
        player.PlayAction("dodge");

        var newCharacter = new PetCharacterDef
        {
            Id = "test2",
            FrameWidth = 120,
            FrameHeight = 120,
            FootAnchorX = 60,
            FootAnchorY = 110,
            Actions = new Dictionary<string, ActionDef>(StringComparer.OrdinalIgnoreCase)
            {
                ["idle"] = new ActionDef
                {
                    Id = "idle",
                    Loop = LoopMode.Loop,
                    Frames = [new FrameDef { ImagePath = "idle_new.png", DurationMs = 100 }]
                }
            }
        };

        player.UpdateCharacter(newCharacter);
        Assert.AreEqual("idle", player.CurrentActionId);
    }

    [TestMethod]
    public void GetCurrentFrame_NegativeTime_ClampedToZero()
    {
        double time = -100;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);
        var (idx, _) = player.GetCurrentFrame();
        Assert.AreEqual(0, idx);
    }

    [TestMethod]
    public void SingleFrame_Once_CompletesAtDuration()
    {
        double time = 0;
        var character = new PetCharacterDef
        {
            Id = "test",
            FrameWidth = 120,
            FrameHeight = 120,
            FootAnchorX = 60,
            FootAnchorY = 110,
            Actions = new Dictionary<string, ActionDef>(StringComparer.OrdinalIgnoreCase)
            {
                ["idle"] = new ActionDef
                {
                    Id = "idle",
                    Loop = LoopMode.Loop,
                    Frames = [new FrameDef { ImagePath = "idle_0.png", DurationMs = 100 }]
                },
                ["dodge"] = new ActionDef
                {
                    Id = "dodge",
                    Loop = LoopMode.Once,
                    Frames = [new FrameDef { ImagePath = "dodge_0.png", DurationMs = 600 }]
                }
            }
        };

        var player = new AnimationPlayer(character, () => time);
        player.PlayAction("dodge");

        // Before threshold: not completed
        time = 599;
        Assert.AreEqual("dodge_0.png", player.GetCurrentFrame().frame.ImagePath);
        Assert.IsFalse(player.IsCompleted);

        // At threshold: completed
        time = 600;
        Assert.AreEqual("dodge_0.png", player.GetCurrentFrame().frame.ImagePath);
        Assert.IsTrue(player.IsCompleted);

        // After threshold: still completed, holds frame
        time = 1000;
        Assert.AreEqual("dodge_0.png", player.GetCurrentFrame().frame.ImagePath);
        Assert.IsTrue(player.IsCompleted);
    }

    [TestMethod]
    public void SingleFrame_Once_FiresCompletionEventOnce()
    {
        double time = 0;
        var character = new PetCharacterDef
        {
            Id = "test",
            FrameWidth = 120,
            FrameHeight = 120,
            FootAnchorX = 60,
            FootAnchorY = 110,
            Actions = new Dictionary<string, ActionDef>(StringComparer.OrdinalIgnoreCase)
            {
                ["idle"] = new ActionDef
                {
                    Id = "idle",
                    Loop = LoopMode.Loop,
                    Frames = [new FrameDef { ImagePath = "idle_0.png", DurationMs = 100 }]
                },
                ["dodge"] = new ActionDef
                {
                    Id = "dodge",
                    Loop = LoopMode.Once,
                    Frames = [new FrameDef { ImagePath = "dodge_0.png", DurationMs = 600 }]
                }
            }
        };

        int completionCount = 0;
        var player = new AnimationPlayer(character, () => time);
        player.ActionCompleted += _ => completionCount++;
        player.PlayAction("dodge");

        time = 600;
        player.GetCurrentFrame();
        Assert.AreEqual(1, completionCount);

        // Repeated queries should not fire again
        time = 1000;
        player.GetCurrentFrame();
        Assert.AreEqual(1, completionCount);
    }

    [TestMethod]
    public void SingleFrame_Once_ResetOnReplay()
    {
        double time = 0;
        var character = new PetCharacterDef
        {
            Id = "test",
            FrameWidth = 120,
            FrameHeight = 120,
            FootAnchorX = 60,
            FootAnchorY = 110,
            Actions = new Dictionary<string, ActionDef>(StringComparer.OrdinalIgnoreCase)
            {
                ["idle"] = new ActionDef
                {
                    Id = "idle",
                    Loop = LoopMode.Loop,
                    Frames = [new FrameDef { ImagePath = "idle_0.png", DurationMs = 100 }]
                },
                ["dodge"] = new ActionDef
                {
                    Id = "dodge",
                    Loop = LoopMode.Once,
                    Frames = [new FrameDef { ImagePath = "dodge_0.png", DurationMs = 600 }]
                }
            }
        };

        var player = new AnimationPlayer(character, () => time);
        player.PlayAction("dodge");
        time = 600;
        player.GetCurrentFrame();
        Assert.IsTrue(player.IsCompleted);

        // Replay: reset completion
        player.PlayAction("dodge");
        Assert.IsFalse(player.IsCompleted);
        time = 601;
        Assert.IsFalse(player.IsCompleted);
    }

    [TestMethod]
    public void PauseResume_Loop_PhasePreservedAcrossWallClock()
    {
        double time = 0;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);

        // idle: 500ms per frame, 2 frames, 1000ms cycle
        time = 250; // mid frame 0
        Assert.AreEqual("idle_0.png", player.GetCurrentFrame().frame.ImagePath);

        // Pause and let wall clock advance 200ms
        player.Pause();
        Assert.IsTrue(player.IsPaused);
        time = 450;
        // While paused, frame should not advance
        Assert.AreEqual("idle_0.png", player.GetCurrentFrame().frame.ImagePath);

        // Resume — the 200ms paused gap should NOT count
        player.Resume();
        Assert.IsFalse(player.IsPaused);
        Assert.AreEqual("idle_0.png", player.GetCurrentFrame().frame.ImagePath);

        // Only 1ms of active time after resume → still frame 0
        time = 451;
        Assert.AreEqual("idle_0.png", player.GetCurrentFrame().frame.ImagePath);

        // At 250 + 1 + 249 = 500ms active → frame 1
        time = 700;
        Assert.AreEqual("idle_1.png", player.GetCurrentFrame().frame.ImagePath);
    }

    [TestMethod]
    public void PauseResume_Once_DoesNotCompleteDuringPause()
    {
        double time = 0;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);
        player.PlayAction("dodge"); // 600ms total (300+300)

        time = 300; // frame 1
        Assert.AreEqual("dodge_1.png", player.GetCurrentFrame().frame.ImagePath);
        Assert.IsFalse(player.IsCompleted);

        // Pause for a long wall-clock time
        player.Pause();
        time = 5000;
        player.GetCurrentFrame();
        Assert.IsFalse(player.IsCompleted);

        // Resume — only 300ms active so far, not 600
        player.Resume();
        time = 5001;
        Assert.IsFalse(player.IsCompleted);

        // Now advance to 300 + 300 = 600ms active → completes
        time = 5300;
        Assert.AreEqual("dodge_1.png", player.GetCurrentFrame().frame.ImagePath);
        Assert.IsTrue(player.IsCompleted);
    }

    [TestMethod]
    public void PauseResume_Idempotent()
    {
        double time = 0;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);

        player.Pause();
        player.Pause(); // double pause: no-op
        Assert.IsTrue(player.IsPaused);

        player.Resume();
        player.Resume(); // double resume: no-op
        Assert.IsFalse(player.IsPaused);
    }

    [TestMethod]
    public void PlayAction_PreservesPauseState()
    {
        // F-02: PlayAction must NOT implicitly clear pause. When the player
        // is paused (e.g. user paused via tray), switching actions during
        // drag/menu interruption should keep the player paused so that
        // resuming later starts the new action from its beginning without
        // accumulating wall-clock gap.
        double time = 0;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);

        player.Pause();
        Assert.IsTrue(player.IsPaused);

        // Switch action while paused — player should remain paused.
        player.PlayAction("curious");
        Assert.IsTrue(player.IsPaused);

        // Wall clock advances while paused — new action should not progress.
        time = 5000;
        var frame = player.GetCurrentFrame();
        Assert.AreEqual("curious_0.png", frame.frame.ImagePath);

        // Resume — the new action starts from its beginning (effective elapsed 0).
        player.Resume();
        Assert.IsFalse(player.IsPaused);
        time = 5001;
        frame = player.GetCurrentFrame();
        Assert.AreEqual("curious_0.png", frame.frame.ImagePath);
    }

    [TestMethod]
    public void PauseActionSwitchDragIdleWaitResume_PhaseConsistent()
    {
        // F-02: Simulates the full tray-pause → drag → release → wait → resume
        // sequence. The animation must not advance during pause or during
        // the wall-clock wait after release. After resume, only active time
        // counts. Uses "curious" as the drag-substitute action since the
        // test character has no "drag" action.
        double time = 0;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);

        // idle playing normally, advance to mid frame 0.
        time = 250;
        Assert.AreEqual("idle_0.png", player.GetCurrentFrame().frame.ImagePath);

        // User pauses via tray.
        player.Pause();
        Assert.IsTrue(player.IsPaused);

        // User starts dragging (action switch while paused).
        player.PlayAction("curious");
        Assert.IsTrue(player.IsPaused); // Still paused

        // Wall clock advances during drag — curious frame frozen.
        time = 1000;
        Assert.AreEqual("curious_0.png", player.GetCurrentFrame().frame.ImagePath);

        // User releases (action switch back to idle while still paused).
        player.PlayAction("idle");
        Assert.IsTrue(player.IsPaused);

        // More wall clock passes — idle should be frozen at frame 0 of new action.
        time = 5000;
        Assert.AreEqual("idle_0.png", player.GetCurrentFrame().frame.ImagePath);

        // User resumes — animation starts from idle frame 0, effective elapsed 0.
        player.Resume();
        Assert.IsFalse(player.IsPaused);

        // 1ms active time — still frame 0.
        time = 5001;
        Assert.AreEqual("idle_0.png", player.GetCurrentFrame().frame.ImagePath);

        // 500ms active time — now frame 1 (idle cycle = 1000ms, frame 0 = 500ms).
        time = 5500;
        Assert.AreEqual("idle_1.png", player.GetCurrentFrame().frame.ImagePath);
    }

    [TestMethod]
    public void PauseMenuInterruptWaitResume_PhaseConsistent()
    {
        // F-02: Simulates tray-pause → menu opens (EndDrag → PlayAction(idle))
        // → menu closes → wait → resume. The menu interruption must not
        // implicitly resume the player.
        double time = 0;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);

        // Play idle to mid cycle.
        time = 300;
        Assert.AreEqual("idle_0.png", player.GetCurrentFrame().frame.ImagePath);

        // User pauses.
        player.Pause();
        Assert.IsTrue(player.IsPaused);

        // Menu opens, which calls EndDrag → PlayAction("idle").
        // Player should remain paused.
        player.PlayAction("idle");
        Assert.IsTrue(player.IsPaused);

        // Wall clock advances during menu interaction.
        time = 8000;
        Assert.AreEqual("idle_0.png", player.GetCurrentFrame().frame.ImagePath);

        // Menu closes, user resumes.
        player.Resume();
        Assert.IsFalse(player.IsPaused);

        // Only active time counts — 1ms after resume, still frame 0.
        time = 8001;
        Assert.AreEqual("idle_0.png", player.GetCurrentFrame().frame.ImagePath);

        // 500ms active → frame 1.
        time = 8500;
        Assert.AreEqual("idle_1.png", player.GetCurrentFrame().frame.ImagePath);
    }

    [TestMethod]
    public void PauseOnceActionSwitchDoesNotCompleteDuringPause()
    {
        // F-02: A Once action switched to while paused must not complete
        // during the pause, even if wall clock far exceeds its duration.
        double time = 0;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);

        player.Pause();
        Assert.IsTrue(player.IsPaused);

        // Switch to dodge (Once, 600ms) while paused.
        player.PlayAction("dodge");
        Assert.IsTrue(player.IsPaused);

        // Wall clock far exceeds 600ms — must not complete.
        time = 10000;
        Assert.IsFalse(player.IsCompleted);
        Assert.AreEqual("dodge_0.png", player.GetCurrentFrame().frame.ImagePath);

        // Resume — only 300ms active → not completed yet.
        player.Resume();
        time = 10300;
        Assert.IsFalse(player.IsCompleted);
        Assert.AreEqual("dodge_1.png", player.GetCurrentFrame().frame.ImagePath);

        // 600ms active → completed.
        time = 10600;
        Assert.AreEqual("dodge_1.png", player.GetCurrentFrame().frame.ImagePath);
        Assert.IsTrue(player.IsCompleted);
    }

    [TestMethod]
    public void PlayAction_WhileNotPaused_DoesNotPause()
    {
        // F-02: Sanity check — PlayAction on a non-paused player must not
        // introduce a pause.
        double time = 0;
        var player = new AnimationPlayer(CreateTestCharacter(), () => time);

        Assert.IsFalse(player.IsPaused);
        player.PlayAction("curious");
        Assert.IsFalse(player.IsPaused);

        time = 100;
        Assert.AreEqual("curious_0.png", player.GetCurrentFrame().frame.ImagePath);
    }

    [TestMethod]
    public void GetCurrentFrame_SingleFrame_AlwaysReturnsThatFrame()
    {
        double time = 0;
        var character = new PetCharacterDef
        {
            Id = "test",
            FrameWidth = 120,
            FrameHeight = 120,
            FootAnchorX = 60,
            FootAnchorY = 110,
            Actions = new Dictionary<string, ActionDef>(StringComparer.OrdinalIgnoreCase)
            {
                ["idle"] = new ActionDef
                {
                    Id = "idle",
                    Loop = LoopMode.Loop,
                    Frames = [new FrameDef { ImagePath = "only.png", DurationMs = 100 }]
                }
            }
        };

        var player = new AnimationPlayer(character, () => time);
        time = 0;
        Assert.AreEqual("only.png", player.GetCurrentFrame().frame.ImagePath);
        time = 500;
        Assert.AreEqual("only.png", player.GetCurrentFrame().frame.ImagePath);
    }
}
