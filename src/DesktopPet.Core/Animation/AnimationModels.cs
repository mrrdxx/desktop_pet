namespace DesktopPet.Core.Animation;

/// <summary>
/// Loop mode for an action's frame sequence.
/// </summary>
public enum LoopMode
{
    /// <summary>Play frames once then hold the last frame.</summary>
    Once,
    /// <summary>Repeat the frame sequence indefinitely.</summary>
    Loop,
    /// <summary>Play forward then backward (ping-pong).</summary>
    PingPong
}

/// <summary>
/// A single frame in an action animation.
/// All coordinates are unscaled source pixels.
/// </summary>
public sealed class FrameDef
{
    /// <summary>Relative path to the PNG file inside the pet resource directory.</summary>
    public string ImagePath { get; init; } = string.Empty;

    /// <summary>Duration this frame is displayed, in milliseconds.</summary>
    public int DurationMs { get; init; } = 200;

    /// <summary>
    /// Horizontal offset to apply as a render transform compensation, in source pixels.
    /// Positive values shift the image right. When a frame's visual content is drawn
    /// shifted from the standard position, this offset compensates so the visual foot
    /// stays at the character's foot anchor in world coordinates.
    /// </summary>
    public int OffsetX { get; init; }

    /// <summary>
    /// Vertical offset to apply as a render transform compensation, in source pixels.
    /// Positive values shift the image down. When a frame's visual content is drawn
    /// higher than the standard position (e.g. a "curious" pose with feet at y=101
    /// instead of y=110), OffsetY=9 shifts the image down 9px so the visual foot
    /// aligns with FootAnchorY in world coordinates.
    /// </summary>
    public int OffsetY { get; init; }
}

/// <summary>
/// A named action (e.g. idle, curious, dodge, drag) with a frame sequence and loop mode.
/// </summary>
public sealed class ActionDef
{
    /// <summary>Action identifier, e.g. "idle", "curious", "dodge", "drag".</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Ordered frame list for this action.</summary>
    public List<FrameDef> Frames { get; init; } = [];

    /// <summary>How the frame sequence repeats.</summary>
    public LoopMode Loop { get; init; } = LoopMode.Loop;
}

/// <summary>
/// Describes a pet character: frame dimensions, foot anchor, interaction regions, and available actions.
/// Coordinates are unscaled source pixels per ARCHITECTURE §8.
/// </summary>
public sealed class PetCharacterDef
{
    /// <summary>Character identifier, e.g. "default".</summary>
    public string Id { get; init; } = "default";

    /// <summary>Frame width in source pixels (all frames share this).</summary>
    public int FrameWidth { get; init; } = 120;

    /// <summary>Frame height in source pixels (all frames share this).</summary>
    public int FrameHeight { get; init; } = 120;

    /// <summary>
    /// Foot anchor X in source pixels — the bottom-center of the character.
    /// Used to keep the pet's feet stable when switching actions.
    /// </summary>
    public int FootAnchorX { get; init; } = 60;

    /// <summary>
    /// Foot anchor Y in source pixels — the bottom-center of the character.
    /// </summary>
    public int FootAnchorY { get; init; } = 110;

    /// <summary>Available actions keyed by action ID.</summary>
    public Dictionary<string, ActionDef> Actions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
