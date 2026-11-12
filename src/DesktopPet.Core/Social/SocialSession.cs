namespace DesktopPet.Core.Social;

/// <summary>
/// Phases of a mutual-notice social session.
/// </summary>
public enum SocialPhase
{
    /// <summary>No active session; pets are independent.</summary>
    Idle,
    /// <summary>Initiator is looking toward the other pet, waiting for a response.</summary>
    Noticing,
    /// <summary>Both pets are engaged in a brief mutual response.</summary>
    Responding,
    /// <summary>The noticed pet rejected the notice; brief dismissal expression.</summary>
    Rejected,
    /// <summary>Session completed normally; both pets enter cooldown.</summary>
    Finished,
    /// <summary>Seeker is travelling along the bottom edge toward the other pet (P3-003).</summary>
    Seeking,
    /// <summary>Seeker has arrived within stop distance; brief pause before cooldown (P3-003).</summary>
    Arrived,
    /// <summary>Initiator is inviting the other pet to chase; waiting for response (P3-004).</summary>
    ChaseInviting,
    /// <summary>Both pets are chasing along the bottom edge — leader runs, follower pursues (P3-004).</summary>
    Chasing,
    /// <summary>Chase ended (boundary/time/distance limit reached); brief pause before cooldown (P3-004).</summary>
    ChaseArrived,
    /// <summary>Session was cancelled by interrupt or timeout; both pets enter cooldown.</summary>
    Cancelled,
    /// <summary>Cooldown period after a session; no new sessions can start.</summary>
    Cooldown
}

/// <summary>
/// Reason a social session was cancelled.
/// </summary>
public enum CancelReason
{
    None,
    DragStarted,
    PettingCandidate,
    Paused,
    PetRemoved,
    RecoverRequested,
    PhysicsActive,
    Timeout,
    OnlyOnePet,
    /// <summary>Seek target was removed, went off-screen, or became unreachable (P3-003).</summary>
    SeekTargetGone
}

/// <summary>
/// Represents a single mutual-notice social session between two pets.
/// Immutable snapshot of session state at a given tick.
/// </summary>
public readonly struct SocialSession
{
    /// <summary>Unique session identifier.</summary>
    public string SessionId { get; }

    /// <summary>PetId of the pet that initiated the notice.</summary>
    public string InitiatorId { get; }

    /// <summary>PetId of the pet being noticed.</summary>
    public string TargetId { get; }

    /// <summary>Current phase of the session.</summary>
    public SocialPhase Phase { get; }

    /// <summary>Monotonic timestamp (ms) when the session started (entered Noticing).</summary>
    public double StartedAtMs { get; }

    /// <summary>Monotonic timestamp (ms) when the current phase started.</summary>
    public double PhaseStartedAtMs { get; }

    /// <summary>Reason for cancellation, if applicable.</summary>
    public CancelReason CancelReason { get; }

    /// <summary>
    /// P3-004: PetId of the leader (the pet running ahead) in a chase session.
    /// Null for non-chase sessions.
    /// </summary>
    public string? ChaseLeaderId { get; }

    /// <summary>
    /// P3-004: Travel direction in a chase session: +1 = rightward, -1 = leftward.
    /// Null for non-chase sessions.
    /// </summary>
    public double? ChaseDirection { get; }

    public SocialSession(
        string sessionId,
        string initiatorId,
        string targetId,
        SocialPhase phase,
        double startedAtMs,
        double phaseStartedAtMs,
        CancelReason cancelReason = CancelReason.None,
        string? chaseLeaderId = null,
        double? chaseDirection = null)
    {
        SessionId = sessionId;
        InitiatorId = initiatorId;
        TargetId = targetId;
        Phase = phase;
        StartedAtMs = startedAtMs;
        PhaseStartedAtMs = phaseStartedAtMs;
        CancelReason = cancelReason;
        ChaseLeaderId = chaseLeaderId;
        ChaseDirection = chaseDirection;
    }

    /// <summary>True when the session is in an active (non-idle, non-cooldown) phase.</summary>
    public bool IsActive =>
        Phase is SocialPhase.Noticing or SocialPhase.Responding or SocialPhase.Rejected
            or SocialPhase.Seeking or SocialPhase.Arrived
            or SocialPhase.ChaseInviting or SocialPhase.Chasing or SocialPhase.ChaseArrived;

    public SocialSession WithPhase(SocialPhase phase, double nowMs) => new(
        SessionId, InitiatorId, TargetId, phase, StartedAtMs, nowMs, CancelReason,
        ChaseLeaderId, ChaseDirection);

    public SocialSession WithCancelled(CancelReason reason, double nowMs) => new(
        SessionId, InitiatorId, TargetId, SocialPhase.Cancelled, StartedAtMs, nowMs, reason,
        ChaseLeaderId, ChaseDirection);

    /// <summary>P3-004: Create a chase session with leader and direction metadata.</summary>
    public SocialSession WithChase(SocialPhase phase, double nowMs,
        string chaseLeaderId, double chaseDirection) => new(
        SessionId, InitiatorId, TargetId, phase, StartedAtMs, nowMs, CancelReason.None,
        chaseLeaderId, chaseDirection);
}
