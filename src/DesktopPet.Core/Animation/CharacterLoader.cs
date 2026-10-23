using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopPet.Core.Animation;

/// <summary>
/// Loads a <see cref="PetCharacterDef"/> from JSON text following the resource format
/// defined in ARCHITECTURE §8. Validates frame dimensions and durations.
/// </summary>
public static class CharacterLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Parses character JSON and validates structural constraints.
    /// Returns the character def or throws on malformed/invalid input.
    /// Validates: frame dimensions, durations, foot anchors, and relative path safety.
    /// </summary>
    public static PetCharacterDef Load(string json)
    {
        var dto = JsonSerializer.Deserialize<CharacterDto>(json, JsonOptions)
            ?? throw new InvalidDataException("Character JSON is null or malformed.");

        // Validate frame dimensions: must be positive.
        if (dto.FrameWidth <= 0)
            throw new InvalidDataException($"FrameWidth must be positive, got {dto.FrameWidth}.");
        if (dto.FrameHeight <= 0)
            throw new InvalidDataException($"FrameHeight must be positive, got {dto.FrameHeight}.");

        var def = new PetCharacterDef
        {
            Id = string.IsNullOrWhiteSpace(dto.Id) ? "default" : dto.Id,
            FrameWidth = dto.FrameWidth,
            FrameHeight = dto.FrameHeight,
            FootAnchorX = dto.FootAnchorX >= 0 ? dto.FootAnchorX : dto.FrameWidth / 2,
            FootAnchorY = dto.FootAnchorY >= 0 ? dto.FootAnchorY : dto.FrameHeight - 10
        };

        // Validate foot anchor is within frame bounds.
        if (def.FootAnchorX < 0 || def.FootAnchorX > def.FrameWidth)
            throw new InvalidDataException($"FootAnchorX {def.FootAnchorX} is outside frame width {def.FrameWidth}.");
        if (def.FootAnchorY < 0 || def.FootAnchorY > def.FrameHeight)
            throw new InvalidDataException($"FootAnchorY {def.FootAnchorY} is outside frame height {def.FrameHeight}.");

        if (dto.Actions is null || dto.Actions.Count == 0)
            throw new InvalidDataException("Character must define at least one action.");

        foreach (var (actionId, actionDto) in dto.Actions)
        {
            if (string.IsNullOrWhiteSpace(actionId))
                throw new InvalidDataException("Action ID cannot be empty.");

            if (actionDto.Frames is null || actionDto.Frames.Count == 0)
                throw new InvalidDataException($"Action '{actionId}' must have at least one frame.");

            var loop = actionDto.Loop?.ToLowerInvariant() switch
            {
                "once" => LoopMode.Once,
                "loop" => LoopMode.Loop,
                "pingpong" or "ping-pong" => LoopMode.PingPong,
                _ => LoopMode.Loop
            };

            var frames = new List<FrameDef>();
            foreach (var f in actionDto.Frames)
            {
                if (string.IsNullOrWhiteSpace(f.ImagePath))
                    throw new InvalidDataException($"Action '{actionId}' has a frame with empty ImagePath.");

                // Path must be relative (no drive, no absolute path).
                if (Path.IsPathRooted(f.ImagePath))
                    throw new InvalidDataException($"Action '{actionId}' frame ImagePath must be relative: {f.ImagePath}");

                // Reject path traversal: normalize and check it doesn't escape the resource directory.
                // F-03: Ensure directory separator boundary so sibling directories with
                // a shared prefix (e.g. "pet" vs "pet-sibling") are not accepted.
                var normalized = Path.GetFullPath(Path.Combine(".", f.ImagePath));
                var baseDir = Path.GetFullPath(".");
                // Ensure baseDir ends with separator for strict prefix comparison.
                if (!baseDir.EndsWith(Path.DirectorySeparatorChar))
                    baseDir += Path.DirectorySeparatorChar;
                // normalized must be within baseDir (equal to it or a subdirectory).
                if (!normalized.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase) &&
                    !normalized.Equals(baseDir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Action '{actionId}' frame ImagePath escapes resource directory: {f.ImagePath}");

                // Validate duration: must be positive after default fallback.
                var duration = f.DurationMs > 0 ? f.DurationMs : 200;

                frames.Add(new FrameDef
                {
                    ImagePath = f.ImagePath,
                    DurationMs = duration,
                    OffsetX = f.OffsetX,
                    OffsetY = f.OffsetY
                });
            }

            def.Actions[actionId] = new ActionDef
            {
                Id = actionId,
                Frames = frames,
                Loop = loop
            };
        }

        // "idle" is required as the fallback action.
        if (!def.Actions.ContainsKey("idle"))
            throw new InvalidDataException("Character must define an 'idle' action for fallback.");

        return def;
    }

    // --- DTO types for JSON deserialization ---

    private sealed class CharacterDto
    {
        public string Id { get; init; } = "";
        public int FrameWidth { get; init; }
        public int FrameHeight { get; init; }
        public int FootAnchorX { get; init; } = -1;
        public int FootAnchorY { get; init; } = -1;
        public Dictionary<string, ActionDto> Actions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ActionDto
    {
        public string? Loop { get; init; }
        public List<FrameDto> Frames { get; init; } = [];
    }

    private sealed class FrameDto
    {
        public string ImagePath { get; init; } = "";
        public int DurationMs { get; init; }
        public int OffsetX { get; init; }
        public int OffsetY { get; init; }
    }
}
