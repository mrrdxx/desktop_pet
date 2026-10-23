namespace DesktopPet.Core.Animation;

/// <summary>
/// Drives frame-by-frame animation playback for a pet character.
/// Uses a monotonic time source (injectable for testing) to determine which frame
/// should be displayed. Handles loop modes, single-shot completion, and foot-point
/// stability when switching actions.
/// </summary>
public sealed class AnimationPlayer
{
    private readonly Func<double> _getTimeMs;
    private PetCharacterDef _character;
    private string _currentActionId = "idle";
    private double _actionStartMs;
    private bool _completed;
    private double _pauseStartMs = -1;
    private double _totalPausedMs;

    /// <summary>Raised when a Once/PingPong action finishes its full sequence.</summary>
    public event Action<string>? ActionCompleted;

    /// <summary>
    /// Creates a player with the given character and time source.
    /// </summary>
    /// <param name="character">The character definition containing actions.</param>
    /// <param name="getTimeMs">Returns elapsed time in milliseconds from a monotonic clock.</param>
    public AnimationPlayer(PetCharacterDef character, Func<double> getTimeMs)
    {
        _character = character;
        _getTimeMs = getTimeMs;
        _actionStartMs = getTimeMs();
    }

    /// <summary>Current action ID being played.</summary>
    public string CurrentActionId => _currentActionId;

    /// <summary>True if the current action has finished (Once/PingPong completed).</summary>
    public bool IsCompleted => _completed;

    /// <summary>True if the player is currently paused (animation time frozen).</summary>
    public bool IsPaused => _pauseStartMs >= 0;

    /// <summary>
    /// Pauses animation time. While paused, elapsed time does not advance
    /// for the current action. Call <see cref="Resume"/> to unfreeze.
    /// </summary>
    public void Pause()
    {
        if (_pauseStartMs >= 0)
            return; // Already paused
        _pauseStartMs = _getTimeMs();
    }

    /// <summary>
    /// Resumes animation time after a pause. The paused duration is subtracted
    /// from the action start so the animation continues from where it left off.
    /// </summary>
    public void Resume()
    {
        if (_pauseStartMs < 0)
            return; // Not paused
        var pausedDuration = _getTimeMs() - _pauseStartMs;
        _totalPausedMs += pausedDuration;
        _actionStartMs += pausedDuration;
        _pauseStartMs = -1;
    }

    /// <summary>
    /// Switches to a new action. If the action doesn't exist, falls back to "idle"
    /// and records a diagnostic via <see cref="OnActionMissing"/>.
    /// Resets frame progress; foot anchor stays stable (caller handles window offset).
    /// F-02: If the player is currently paused, the new action starts paused
    /// at its own time origin so that resuming later continues from frame 0
    /// of the new action without accumulating wall-clock gap.
    /// </summary>
    public void PlayAction(string actionId)
    {
        if (string.IsNullOrEmpty(actionId))
            actionId = "idle";

        if (_character.Actions.TryGetValue(actionId, out _))
        {
            _currentActionId = actionId;
        }
        else
        {
            // Missing action fallback: log once, switch to idle.
            OnActionMissing?.Invoke(actionId);
            _currentActionId = "idle";
        }

        var now = _getTimeMs();
        _actionStartMs = now;
        _completed = false;
        _totalPausedMs = 0;

        // F-02: Preserve pause state across action switches. If the player was
        // paused, the new action starts frozen at its beginning so wall-clock
        // time does not advance its progress until Resume is called.
        if (_pauseStartMs >= 0)
            _pauseStartMs = now;
    }

    /// <summary>
    /// Raised when a requested action is not found and the player falls back to idle.
    /// The string argument is the missing action ID, for diagnostic logging.
    /// </summary>
    public event Action<string>? OnActionMissing;

    /// <summary>
    /// Updates the character definition (e.g. after hot-reload).
    /// Preserves the current action if it still exists; otherwise falls back to idle.
    /// </summary>
    public void UpdateCharacter(PetCharacterDef character)
    {
        _character = character;
        if (!_character.Actions.ContainsKey(_currentActionId))
        {
            _currentActionId = "idle";
            _actionStartMs = _getTimeMs();
            _completed = false;
        }
    }

    /// <summary>
    /// Computes the current frame index and the frame def to display.
    /// Returns the idle frame if the character has no actions at all.
    /// </summary>
    public (int frameIndex, FrameDef frame) GetCurrentFrame()
    {
        if (!_character.Actions.TryGetValue(_currentActionId, out var action))
        {
            // Should not happen since PlayAction falls back to idle, but guard anyway.
            action = _character.Actions.TryGetValue("idle", out var idle) ? idle : null!;
            if (action is null)
                throw new InvalidOperationException("No actions available in character.");
        }

        var frames = action.Frames;
        if (frames.Count == 0)
            throw new InvalidOperationException($"Action '{_currentActionId}' has no frames.");

        // F-02: While paused, freeze elapsed at the pause point so the animation
        // does not advance during wall-clock time.
        var currentMs = _getTimeMs();
        var effectiveMs = _pauseStartMs >= 0 ? _pauseStartMs : currentMs;
        var elapsed = effectiveMs - _actionStartMs;
        if (elapsed < 0)
            elapsed = 0;

        // Calculate total cycle duration.
        int totalMs = 0;
        foreach (var f in frames)
            totalMs += f.DurationMs;

        if (totalMs <= 0)
            totalMs = frames.Count * 200; // safety fallback

        switch (action.Loop)
        {
            case LoopMode.Once:
            {
                if (elapsed >= totalMs)
                {
                    if (!_completed)
                    {
                        _completed = true;
                        ActionCompleted?.Invoke(_currentActionId);
                    }
                    // Hold on last frame.
                    return (frames.Count - 1, frames[^1]);
                }
                int idx = FrameAtElapsed(frames, elapsed);
                return (idx, frames[idx]);
            }
            case LoopMode.PingPong:
            {
                // Forward + backward = 2 * totalMs per cycle.
                int cycleMs = totalMs * 2;
                double cyclePos = elapsed % cycleMs;
                int idx;
                if (cyclePos < totalMs)
                    idx = FrameAtElapsed(frames, cyclePos);
                else
                    idx = FrameAtElapsed(frames, cycleMs - cyclePos - 1);
                // Clamp
                if (idx < 0) idx = 0;
                if (idx >= frames.Count) idx = frames.Count - 1;
                return (idx, frames[idx]);
            }
            case LoopMode.Loop:
            default:
            {
                double cyclePos = elapsed % totalMs;
                int idx = FrameAtElapsed(frames, cyclePos);
                return (idx, frames[idx]);
            }
        }
    }

    /// <summary>
    /// Returns the render offset for the current frame. This is the TranslateTransform
    /// value the App should apply to the Image control so that the frame's visual foot
    /// stays at the same world position as the character's foot anchor.
    /// </summary>
    /// <returns>(deltaX, deltaY) in source pixels to apply as TranslateTransform.</returns>
    public (double deltaX, double deltaY) GetFootOffset()
    {
        var (_, frame) = GetCurrentFrame();
        // OffsetX/Y describes how far the frame's image content is shifted from the
        // standard position. If a frame's content is drawn 9px higher than idle (foot
        // at y=101 instead of y=110), OffsetY=9 compensates by moving the image down
        // 9px so the visual foot aligns with FootAnchorY. The Transform applies the
        // offset directly — no negation.
        return (frame.OffsetX, frame.OffsetY);
    }

    private static int FrameAtElapsed(List<FrameDef> frames, double elapsedMs)
    {
        double acc = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            acc += frames[i].DurationMs;
            if (elapsedMs < acc)
                return i;
        }
        return frames.Count - 1;
    }
}
