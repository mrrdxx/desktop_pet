using DesktopPet.Core.Persistence;

namespace DesktopPet.Core.Social;

/// <summary>
/// Result of a single social coordinator tick evaluation.
/// </summary>
public readonly struct SocialTickResult
{
    /// <summary>Current session snapshot (may be idle/empty if no active session).</summary>
    public SocialSession Session { get; }

    /// <summary>True when a social session is currently active (blocks rare events).</summary>
    public bool IsSocialActive { get; }

    /// <summary>Animation/expression action requested for a pet, if any.</summary>
    public SocialExpression? InitiatorExpression { get; }

    /// <summary>Animation/expression action requested for the other pet, if any.</summary>
    public SocialExpression? TargetExpression { get; }

    /// <summary>True if a new session started this tick.</summary>
    public bool SessionStarted { get; }

    /// <summary>True if the session ended this tick (finished, rejected, or cancelled).</summary>
    public bool SessionEnded { get; }

    /// <summary>
    /// P3-003: Seeking step intent. When non-null, the App layer should move
    /// the seeker's window Left by this delta (DIP) along the bottom edge.
    /// Positive = move right, negative = move left, zero = arrived/stop.
    /// </summary>
    public double? SeekStepDelta { get; }

    /// <summary>P3-003: PetId of the seeker in a seeking session, if any.</summary>
    public string? SeekerId { get; }

    /// <summary>
    /// P3-004: Chase step intent for the leader's window. When non-null, the
    /// App layer should move the leader's window Left by this delta (DIP).
    /// Positive = move right, negative = move left, zero = stop.
    /// </summary>
    public double? ChaseLeaderDelta { get; }

    /// <summary>
    /// P3-004: Chase step intent for the follower's window. When non-null,
    /// the App layer should move the follower's window Left by this delta (DIP).
    /// Positive = move right, negative = move left, zero = stop.
    /// </summary>
    public double? ChaseFollowerDelta { get; }

    /// <summary>P3-004: PetId of the leader in a chase session, if any.</summary>
    public string? ChaseLeaderId { get; }

    /// <summary>P3-004: PetId of the follower in a chase session, if any.</summary>
    public string? ChaseFollowerId { get; }

    public SocialTickResult(
        SocialSession session,
        bool isSocialActive,
        SocialExpression? initiatorExpression,
        SocialExpression? targetExpression,
        bool sessionStarted,
        bool sessionEnded,
        double? seekStepDelta = null,
        string? seekerId = null,
        double? chaseLeaderDelta = null,
        double? chaseFollowerDelta = null,
        string? chaseLeaderId = null,
        string? chaseFollowerId = null)
    {
        Session = session;
        IsSocialActive = isSocialActive;
        InitiatorExpression = initiatorExpression;
        TargetExpression = targetExpression;
        SessionStarted = sessionStarted;
        SessionEnded = sessionEnded;
        SeekStepDelta = seekStepDelta;
        SeekerId = seekerId;
        ChaseLeaderDelta = chaseLeaderDelta;
        ChaseFollowerDelta = chaseFollowerDelta;
        ChaseLeaderId = chaseLeaderId;
        ChaseFollowerId = chaseFollowerId;
    }

    /// <summary>Convenience result for idle state.</summary>
    public static SocialTickResult Idle => new(
        default, false, null, null, false, false);
}

/// <summary>
/// Expression/animation intent for a pet during a social session.
/// </summary>
public enum SocialExpression
{
    /// <summary>Look toward the other pet (curious/attentive pose).</summary>
    Notice,
    /// <summary>Acknowledge and briefly respond (happy/acknowledging pose).</summary>
    Respond,
    /// <summary>Dismiss/reject (slight turn-away pose).</summary>
    Reject,
    /// <summary>Return to idle after session ends.</summary>
    ReturnToIdle
}

/// <summary>
/// Input state for a single pet at evaluation time.
/// </summary>
public readonly struct PetSocialState
{
    /// <summary>PetId of this pet.</summary>
    public string PetId { get; }

    /// <summary>Personality of this pet.</summary>
    public Personality Personality { get; }

    /// <summary>Window center X in DIP (screen coordinates).</summary>
    public double CenterX { get; }

    /// <summary>Window center Y in DIP (screen coordinates).</summary>
    public double CenterY { get; }

    /// <summary>True when this pet is currently being dragged.</summary>
    public bool IsDragging { get; }

    /// <summary>True when this pet is a petting candidate (head/body pressed).</summary>
    public bool IsPettingCandidate { get; }

    /// <summary>True when this pet is in physics motion.</summary>
    public bool IsPhysicsActive { get; }

    /// <summary>True when this pet is expressing grievance.</summary>
    public bool IsExpressingGrievance { get; }

    /// <summary>True when this pet is probing (rare event).</summary>
    public bool IsProbing { get; }

    /// <summary>True when the app is globally paused.</summary>
    public bool IsPaused { get; }

    /// <summary>Monotonic timestamp (ms) of the last non-idle activity (drag end, physics stop, etc.).</summary>
    public double LastActivityMs { get; }

    /// <summary>
    /// P3-003: Monitor identifier for the display this pet is currently on.
    /// Used to determine whether two pets share the same bottom-edge activity surface.
    /// Null when monitor information is unavailable (treated as never matching).
    /// </summary>
    public string? MonitorId { get; }

    /// <summary>
    /// P3-003: Bottom-edge Y coordinate of the work area this pet rests on (DIP).
    /// Used to determine whether two pets share the same bottom-edge activity surface.
    /// When the pet is airborne (not resting on the bottom edge), this may differ
    /// from CenterY and signals an unreachable surface.
    /// </summary>
    public double BottomEdgeY { get; }

    /// <summary>
    /// P3-003: True when this pet is resting on the bottom edge of its work area
    /// (not airborne, not in physics). Used to gate seeking — an airborne pet
    /// cannot be sought along the bottom edge.
    /// </summary>
    public bool IsOnBottomEdge { get; }

    /// <summary>
    /// P3-004: Left edge of the work area in DIP (screen coordinates).
    /// Used for chase clearance checks. Null when monitor information is
    /// unavailable (treated as no chase).
    /// </summary>
    public double? WorkAreaLeft { get; }

    /// <summary>
    /// P3-004: Right edge of the work area in DIP (screen coordinates).
    /// Used for chase clearance checks. Null when monitor information is
    /// unavailable (treated as no chase).
    /// </summary>
    public double? WorkAreaRight { get; }

    /// <summary>
    /// P3-004 R2 F-01: Full window width in DIP. Used for complete-window
    /// edge clearance checks — the leader's leading edge (not center) must
    /// have at least ChaseClearanceDip of space. Default 120 (standard pet
    /// window width) when not specified.
    /// </summary>
    public double WindowWidth { get; }

    public PetSocialState(
        string petId,
        Personality personality,
        double centerX,
        double centerY,
        bool isDragging,
        bool isPettingCandidate,
        bool isPhysicsActive,
        bool isExpressingGrievance,
        bool isProbing,
        bool isPaused,
        double lastActivityMs,
        string? monitorId = null,
        double bottomEdgeY = 0,
        bool isOnBottomEdge = true,
        double? workAreaLeft = null,
        double? workAreaRight = null,
        double windowWidth = 120)
    {
        PetId = petId;
        Personality = personality;
        CenterX = centerX;
        CenterY = centerY;
        IsDragging = isDragging;
        IsPettingCandidate = isPettingCandidate;
        IsPhysicsActive = isPhysicsActive;
        IsExpressingGrievance = isExpressingGrievance;
        IsProbing = isProbing;
        IsPaused = isPaused;
        LastActivityMs = lastActivityMs;
        MonitorId = monitorId;
        BottomEdgeY = bottomEdgeY;
        IsOnBottomEdge = isOnBottomEdge;
        WorkAreaLeft = workAreaLeft;
        WorkAreaRight = workAreaRight;
        WindowWidth = windowWidth;
    }

    /// <summary>True when this pet is in an interruptible state (any user-facing activity).</summary>
    public bool IsBusy =>
        IsDragging || IsPettingCandidate || IsPhysicsActive ||
        IsExpressingGrievance || IsProbing || IsPaused;

    /// <summary>True when this pet has been idle for at least the given duration.</summary>
    public bool IsIdleFor(double minMs, double nowMs) =>
        !IsBusy && (nowMs - LastActivityMs) >= minMs;
}

/// <summary>
/// Coordinates the single shared mutual-notice social session between two pets.
/// Pure logic: no WPF, no timers, no IO. All state is driven by Tick calls.
/// (PRODUCT R-05, ARCHITECTURE §6 P3-002)
/// </summary>
public sealed class SocialCoordinator
{
    private readonly SocialConfig _config;
    private readonly Random _random;
    private SocialSession _session;
    private readonly Dictionary<string, double> _cooldownEndByPet = new();
    // P3-004 R1 F-02: Chase cooldown is managed independently from the
    // mutual-notice/seek cooldown. This prevents a 90s chase cooldown from
    // blocking mutual-notice evaluation or a new release's seeking opportunity.
    private readonly Dictionary<string, double> _chaseCooldownEndByPet = new();
    private readonly Dictionary<string, double> _lastNoticeAttemptByPet = new();
    private double _lastTickMs = double.NegativeInfinity;

    // P3-003: Seeking state (tracked alongside the session struct).
    private double _seekStartedAtMs;
    private double _seekSeekerStartX;

    // P3-003 R1 F-01: Release candidate — only set when a pet is actually
    // dragged and released by the user (not from idle transitions, pause
    // resume, or reload). Consumed exactly once when a seeking session
    // starts; cleared on cancel, timeout, pet removal, or ForceCancel.
    // This ensures seeking never fires spontaneously from static idle.
    private string? _seekReleasePetId;
    private double _seekReleaseAtMs;
    private bool _seekReleaseWasThrown;

    // P3-003 R1 F-02: Last seek advance timestamp. Used to compute elapsed
    // time since the last step, so movement speed is independent of how
    // many window timer callbacks fire within the same interval.
    private double _lastSeekAdvanceMs;

    // P3-004: Chase state (tracked alongside the session struct).
    // Per-pair chase attempt dedup: key = "petA|petB" (sorted by PetId).
    private readonly Dictionary<string, double> _lastChaseAttemptByPair = new();
    // Chase movement tracking.
    private double _chaseStartedAtMs;
    private double _lastChaseAdvanceMs;
    private double _chaseLeaderStartX;
    private double _chaseFollowerStartX;

    /// <summary>Current session snapshot (read-only for callers).</summary>
    public SocialSession CurrentSession => _session;

    /// <summary>True when a social session is currently active.</summary>
    public bool IsSocialActive => _session.IsActive;

    /// <summary>Config used by this coordinator (cloned on construction).</summary>
    public SocialConfig Config => _config;

    /// <summary>
    /// Creates a coordinator with the given config and random source.
    /// Production should pass a shared Random; tests should pass a seeded Random.
    /// </summary>
    public SocialCoordinator(SocialConfig? config = null, Random? random = null)
    {
        _config = (config ?? new SocialConfig()).Clone();
        _random = random ?? new Random();
        _session = default;
    }

    /// <summary>
    /// Evaluates a single tick. Call once per OnUpdateTick with the current monotonic time
    /// and both pets' states. Returns the resulting session state and any expression intents.
    /// 
    /// When called from multiple per-pet OnUpdateTick handlers with the same nowMs,
    /// only the first call advances the state machine; subsequent calls with the
    /// same nowMs return the current session snapshot without re-evaluating.
    /// </summary>
    /// <param name="nowMs">Current monotonic timestamp in ms.</param>
    /// <param name="petA">First pet's social state.</param>
    /// <param name="petB">Second pet's social state. Null if only one pet exists.</param>
    public SocialTickResult Tick(double nowMs, PetSocialState petA, PetSocialState? petB)
    {
        // P3-002: Deduplicate calls from multiple per-pet OnUpdateTick handlers.
        // When both pets' timers fire at the same nowMs, only the first call
        // should advance the state machine. Subsequent calls return a snapshot.
        //
        // P3-003 R1 F-02: During Seeking, double equality on nowMs is not
        // sufficient — two DispatcherTimers may fire at 1000.0 and 1000.1ms.
        // Use a time-window dedup: if the last tick was within 16ms (half a
        // 33ms frame), treat it as the same frame and return a snapshot
        // without re-advancing. This prevents speed doubling with dual windows.
        if (nowMs == _lastTickMs)
        {
            return new SocialTickResult(_session, _session.IsActive,
                null, null, false, false);
        }
        if (_session.Phase == SocialPhase.Seeking && nowMs - _lastTickMs < 16.0)
        {
            return new SocialTickResult(_session, _session.IsActive,
                null, null, false, false);
        }
        // P3-004: Same dedup for Chasing — prevent speed doubling with dual windows.
        if (_session.Phase == SocialPhase.Chasing && nowMs - _lastTickMs < 16.0)
        {
            return new SocialTickResult(_session, _session.IsActive,
                null, null, false, false);
        }
        _lastTickMs = nowMs;

        // Only one pet — cancel any active session and stay idle.
        if (petB is null)
        {
            if (_session.IsActive)
            {
                var cancelled = _session.WithCancelled(CancelReason.OnlyOnePet, nowMs);
                _session = default;
                StartCooldown(cancelled.InitiatorId, cancelled.TargetId, nowMs);
                return new SocialTickResult(cancelled, false,
                    SocialExpression.ReturnToIdle, SocialExpression.ReturnToIdle, false, true);
            }
            return SocialTickResult.Idle;
        }

        var b = petB.Value;

        // 1. Check interrupts on active session.
        if (_session.IsActive)
        {
            var interrupt = CheckInterrupt(petA, b, nowMs);
            if (interrupt is not null)
                return interrupt.Value;

            // 2. Advance the session state machine.
            return AdvanceSession(nowMs, petA, b);
        }

        // 3. Check cooldown (mutual-notice + seek cooldown).
        // P3-004 R1 F-02: Chase cooldown is separate — it does NOT block
        // mutual-notice or seeking. Only the shared cooldown blocks those.
        if (IsInCooldown(petA.PetId, nowMs) || IsInCooldown(b.PetId, nowMs))
            return SocialTickResult.Idle;

        // 4. Try to start a new mutual-notice session.
        var noticeResult = TryStartSession(nowMs, petA, b);
        if (noticeResult.IsSocialActive || noticeResult.SessionStarted)
            return noticeResult;

        // 5. P3-003: Try to start a seeking session (after notice attempt fails).
        // P3-004 R1 F-02: Seeking has priority over chase — a valid release
        // candidate must get its seeking opportunity before chase can fire.
        var seekResult = TryStartSeeking(nowMs, petA, b);
        if (seekResult is not null)
            return seekResult.Value;

        // 6. P3-004: Try to start a chase invite (after notice and seeking fail).
        // P3-004 R1 F-02: Chase has its own independent cooldown. A pet in
        // chase cooldown can still do mutual-notice and seeking (checked above).
        var chaseResult = TryStartChaseInvite(nowMs, petA, b);
        if (chaseResult is not null)
            return chaseResult.Value;

        return SocialTickResult.Idle;
    }

    /// <summary>
    /// P3-003 R1 F-01: Notify the coordinator that a pet was just released
    /// from a user drag. This is the ONLY way to establish a seeking
    /// candidate — spontaneous idle, pause/resume, or reload cannot trigger
    /// seeking. The candidate is consumed exactly once when a seeking
    /// session starts, and is cleared on cancel, timeout, ForceCancel, or
    /// pet removal.
    /// </summary>
    /// <param name="petId">The pet that was dragged and released.</param>
    /// <param name="nowMs">Monotonic timestamp of the release.</param>
    /// <param name="wasThrown">True if the release was a throw (physics
    /// active). Seeking must wait for physics to naturally stop before
    /// evaluating reachability.</param>
    public void NotifyPetReleased(string petId, double nowMs, bool wasThrown)
    {
        _seekReleasePetId = petId;
        _seekReleaseAtMs = nowMs;
        _seekReleaseWasThrown = wasThrown;
    }

    /// <summary>
    /// Force-cancels any active session (e.g. on pet removal, global pause).
    /// </summary>
    public SocialTickResult ForceCancel(CancelReason reason, double nowMs)
    {
        // P3-003 R1 F-01: Always clear any pending release candidate on force cancel.
        _seekReleasePetId = null;

        if (!_session.IsActive)
            return SocialTickResult.Idle;

        var wasSeeking = _session.Phase is SocialPhase.Seeking or SocialPhase.Arrived;
        // P3-004 R2 F-02: Chase-originated sessions carry ChaseLeaderId
        // through all phases including Rejected. Use ChaseLeaderId to
        // identify chase sessions, not just the three active chase phases.
        // This ensures a rejected chase invite that gets interrupted
        // (e.g. by drag) uses chase cooldown, not the shorter shared cooldown.
        var wasChasing = _session.ChaseLeaderId is not null;
        var cancelled = _session.WithCancelled(reason, nowMs);
        _session = default;
        if (wasChasing)
            StartChaseCooldown(cancelled.InitiatorId, cancelled.TargetId, nowMs);
        else if (wasSeeking)
            StartSeekCooldown(cancelled.InitiatorId, cancelled.TargetId, nowMs);
        else
            StartCooldown(cancelled.InitiatorId, cancelled.TargetId, nowMs);
        return new SocialTickResult(cancelled, false,
            SocialExpression.ReturnToIdle, SocialExpression.ReturnToIdle, false, true);
    }

    private SocialTickResult? CheckInterrupt(PetSocialState petA, PetSocialState petB, double nowMs)
    {
        var (stateInit, stateTgt) = MapByRole(petA, petB);

        if (stateInit.IsDragging || stateTgt.IsDragging)
            return CancelSession(CancelReason.DragStarted, nowMs);
        if (stateInit.IsPettingCandidate || stateTgt.IsPettingCandidate)
            return CancelSession(CancelReason.PettingCandidate, nowMs);
        if (stateInit.IsPaused || stateTgt.IsPaused)
            return CancelSession(CancelReason.Paused, nowMs);
        if (stateInit.IsPhysicsActive || stateTgt.IsPhysicsActive)
            return CancelSession(CancelReason.PhysicsActive, nowMs);
        if (stateInit.IsProbing || stateTgt.IsProbing)
            return CancelSession(CancelReason.PettingCandidate, nowMs);

        // P3-003 R1 F-03: During Seeking, check BOTH pets' support and
        // target visibility — not just the target. If either pet loses
        // bottom-edge support, changes monitor, or the target is no longer
        // on a valid monitor, cancel immediately.
        if (_session.Phase == SocialPhase.Seeking)
        {
            if (!stateInit.IsOnBottomEdge ||
                !stateTgt.IsOnBottomEdge ||
                !SameMonitor(stateInit, stateTgt))
            {
                return CancelSession(CancelReason.SeekTargetGone, nowMs);
            }
        }

        // P3-004: During chase phases, check BOTH pets' support and monitor.
        if (_session.Phase is SocialPhase.ChaseInviting or SocialPhase.Chasing or SocialPhase.ChaseArrived)
        {
            if (!stateInit.IsOnBottomEdge ||
                !stateTgt.IsOnBottomEdge ||
                !SameMonitor(stateInit, stateTgt))
            {
                return CancelSession(CancelReason.SeekTargetGone, nowMs);
            }
        }

        return null;
    }

    private SocialTickResult CancelSession(CancelReason reason, double nowMs)
    {
        var wasSeeking = _session.Phase is SocialPhase.Seeking or SocialPhase.Arrived;
        // P3-004 R2 F-02: Chase-originated sessions carry ChaseLeaderId
        // through all phases including Rejected. Use ChaseLeaderId to
        // identify chase sessions, not just the three active chase phases.
        var wasChasing = _session.ChaseLeaderId is not null;
        var cancelled = _session.WithCancelled(reason, nowMs);
        _session = default;
        if (wasChasing)
            StartChaseCooldown(cancelled.InitiatorId, cancelled.TargetId, nowMs);
        else if (wasSeeking)
            StartSeekCooldown(cancelled.InitiatorId, cancelled.TargetId, nowMs);
        else
            StartCooldown(cancelled.InitiatorId, cancelled.TargetId, nowMs);
        return new SocialTickResult(cancelled, false,
            SocialExpression.ReturnToIdle, SocialExpression.ReturnToIdle, false, true);
    }

    private SocialTickResult AdvanceSession(double nowMs, PetSocialState petA, PetSocialState petB)
    {
        var (stateInit, stateTgt) = MapByRole(petA, petB);

        switch (_session.Phase)
        {
            case SocialPhase.Noticing:
            {
                // Session timeout check.
                if (nowMs - _session.StartedAtMs >= _config.SessionTimeoutMs)
                    return CancelSession(CancelReason.Timeout, nowMs);

                // Noticing phase duration limit.
                if (nowMs - _session.PhaseStartedAtMs >= _config.NoticeDurationMs)
                {
                    // No response received — treat as rejected/ended.
                    var finished = _session.WithPhase(SocialPhase.Finished, nowMs);
                    _session = default;
                    StartCooldown(finished.InitiatorId, finished.TargetId, nowMs);
                    return new SocialTickResult(finished, false,
                        SocialExpression.ReturnToIdle, SocialExpression.ReturnToIdle, false, true);
                }

                // Check if target's respond delay has elapsed.
                var respondDelay = stateTgt.Personality == Personality.SlowPaced
                    ? _config.SlowPacedRespondDelayMs
                    : _config.RespondDelayMs;

                if (nowMs - _session.PhaseStartedAtMs >= respondDelay)
                {
                    // Roll for respond vs reject.
                    if (_random.NextDouble() < _config.RespondProbability)
                    {
                        var responding = _session.WithPhase(SocialPhase.Responding, nowMs);
                        _session = responding;
                        return new SocialTickResult(responding, true,
                            SocialExpression.Notice, SocialExpression.Respond, false, false);
                    }
                    else
                    {
                        var rejected = _session.WithPhase(SocialPhase.Rejected, nowMs);
                        _session = rejected;
                        return new SocialTickResult(rejected, true,
                            SocialExpression.Notice, SocialExpression.Reject, false, false);
                    }
                }

                // Still noticing.
                return new SocialTickResult(_session, true,
                    SocialExpression.Notice, null, false, false);
            }

            case SocialPhase.Responding:
            {
                if (nowMs - _session.StartedAtMs >= _config.SessionTimeoutMs)
                    return CancelSession(CancelReason.Timeout, nowMs);

                if (nowMs - _session.PhaseStartedAtMs >= _config.RespondDurationMs)
                {
                    var finished = _session.WithPhase(SocialPhase.Finished, nowMs);
                    _session = default;
                    StartCooldown(finished.InitiatorId, finished.TargetId, nowMs);
                    return new SocialTickResult(finished, false,
                        SocialExpression.ReturnToIdle, SocialExpression.ReturnToIdle, false, true);
                }

                return new SocialTickResult(_session, true,
                    SocialExpression.Notice, SocialExpression.Respond, false, false);
            }

            case SocialPhase.Rejected:
            {
                // Rejected is brief — end immediately on next tick.
                var finished = _session.WithPhase(SocialPhase.Finished, nowMs);
                _session = default;
                // P3-004: Chase-originated rejections use chase cooldown.
                if (finished.ChaseLeaderId is not null)
                    StartChaseCooldown(finished.InitiatorId, finished.TargetId, nowMs);
                else
                    StartCooldown(finished.InitiatorId, finished.TargetId, nowMs);
                return new SocialTickResult(finished, false,
                    SocialExpression.ReturnToIdle, SocialExpression.ReturnToIdle, false, true);
            }

            case SocialPhase.Seeking:
            {
                return AdvanceSeeking(nowMs, petA, petB);
            }

            case SocialPhase.Arrived:
            {
                // Arrived is brief — end on next tick with cooldown.
                var finished = _session.WithPhase(SocialPhase.Finished, nowMs);
                _session = default;
                StartSeekCooldown(finished.InitiatorId, finished.TargetId, nowMs);
                return new SocialTickResult(finished, false,
                    SocialExpression.ReturnToIdle, SocialExpression.ReturnToIdle, false, true);
            }

            case SocialPhase.ChaseInviting:
            {
                return AdvanceChaseInviting(nowMs, petA, petB);
            }

            case SocialPhase.Chasing:
            {
                return AdvanceChasing(nowMs, petA, petB);
            }

            case SocialPhase.ChaseArrived:
            {
                // ChaseArrived is brief — end on next tick with chase cooldown.
                var finished = _session.WithPhase(SocialPhase.Finished, nowMs);
                _session = default;
                StartChaseCooldown(finished.InitiatorId, finished.TargetId, nowMs);
                return new SocialTickResult(finished, false,
                    SocialExpression.ReturnToIdle, SocialExpression.ReturnToIdle, false, true);
            }

            default:
                // Should not reach here for Idle/Finished/Cancelled/Cooldown.
                return SocialTickResult.Idle;
        }
    }

    private SocialTickResult TryStartSession(double nowMs, PetSocialState petA, PetSocialState petB)
    {
        // Both must be idle for MinIdleMs.
        if (!petA.IsIdleFor(_config.MinIdleMs, nowMs))
            return SocialTickResult.Idle;
        if (!petB.IsIdleFor(_config.MinIdleMs, nowMs))
            return SocialTickResult.Idle;

        // Distance check.
        var dx = petA.CenterX - petB.CenterX;
        var dy = petA.CenterY - petB.CenterY;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist > _config.NoticeDistanceMaxDip)
            return SocialTickResult.Idle;

        // Try petA as initiator.
        var attemptA = TryPetAsInitiator(petA, petB, nowMs);
        if (attemptA is not null)
            return attemptA.Value;

        // Try petB as initiator.
        var attemptB = TryPetAsInitiator(petB, petA, nowMs);
        if (attemptB is not null)
            return attemptB.Value;

        return SocialTickResult.Idle;
    }

    private SocialTickResult? TryPetAsInitiator(
        PetSocialState initiator, PetSocialState target, double nowMs)
    {
        // Check notice interval for this pet.
        _lastNoticeAttemptByPet.TryGetValue(initiator.PetId, out var lastAttempt);

        if (nowMs - lastAttempt < _config.NoticeIntervalMs)
            return null;

        // Record attempt time.
        _lastNoticeAttemptByPet[initiator.PetId] = nowMs;

        // Roll for notice probability.
        var prob = initiator.Personality == Personality.SlowPaced
            ? _config.SlowPacedNoticeProbability
            : _config.NoticeProbability;

        if (_random.NextDouble() >= prob)
            return null;

        // Start a new session.
        var sessionId = $"social-{nowMs:F0}-{initiator.PetId}";
        var session = new SocialSession(
            sessionId, initiator.PetId, target.PetId,
            SocialPhase.Noticing, nowMs, nowMs);
        _session = session;

        return new SocialTickResult(session, true,
            SocialExpression.Notice, null, true, false);
    }

    private (PetSocialState init, PetSocialState tgt) MapByRole(
        PetSocialState petA, PetSocialState petB)
    {
        if (_session.InitiatorId == petB.PetId)
            return (petB, petA);
        return (petA, petB);
    }

    private bool IsInCooldown(string petId, double nowMs)
    {
        return _cooldownEndByPet.TryGetValue(petId, out var end) && nowMs < end;
    }

    private void StartCooldown(string petA, string petB, double nowMs)
    {
        _cooldownEndByPet[petA] = nowMs + _config.CooldownMs;
        _cooldownEndByPet[petB] = nowMs + _config.CooldownMs;
    }

    // ── P3-003: Seeking state machine ───────────────────────────────────

    /// <summary>
    /// P3-003: Try to start a seeking session. Conditions:
    /// - Both pets idle and stable (not busy).
    /// - Both on the same monitor's bottom edge (|centerY diff| ≤ tolerance, same MonitorId).
    /// - Horizontal distance &gt; SeekStopDistanceDip (not already close enough).
    /// - Horizontal distance ≤ SeekMaxDistanceDip (within travel range).
    /// - SeekReactDelayMs has elapsed since the last activity of either pet.
    /// - Not in seek cooldown.
    /// The pet farther from center (the one that was "left behind") becomes the seeker.
    /// </summary>
    private SocialTickResult? TryStartSeeking(double nowMs, PetSocialState petA, PetSocialState petB)
    {
        // P3-003 R1 F-01: Seeking only starts when there is an unconsumed
        // release candidate from an actual user drag. Spontaneous idle,
        // pause/resume, or reload must NOT trigger seeking.
        if (_seekReleasePetId is null)
            return null;

        // The released pet must still be one of the two current pets.
        // If it was removed, clear the stale candidate.
        var releaseId = _seekReleasePetId;
        PetSocialState releasedPet, otherPet;
        if (petA.PetId == releaseId)
        {
            releasedPet = petA;
            otherPet = petB;
        }
        else if (petB.PetId == releaseId)
        {
            releasedPet = petB;
            otherPet = petA;
        }
        else
        {
            // Released pet no longer exists — clear stale candidate.
            _seekReleasePetId = null;
            return null;
        }

        // P3-003 R1 F-01: If the release was a throw, the released pet must
        // have physics inactive (naturally stopped) before seeking can start.
        // We check IsPhysicsActive on the released pet's state.
        // This is a TEMPORARY wait — the candidate is preserved.
        if (_seekReleaseWasThrown && releasedPet.IsPhysicsActive)
            return null;

        // P3-003 R1 F-01: React delay is measured from the actual release
        // moment, not from LastActivityMs which can be set by any busy→idle
        // transition.
        // This is a TEMPORARY wait — the candidate is preserved.
        if (nowMs - _seekReleaseAtMs < _config.SeekReactDelayMs)
            return null;

        // P3-003 R2 F-01: Candidate lifecycle — after the react delay has
        // passed and physics (if thrown) has stopped, the candidate has a
        // finite lifetime bounded by SeekTimeoutMs. If the candidate is
        // older than that, it is stale and must be discarded — a release
        // that could not start seeking within the timeout window should
        // not suddenly fire much later when conditions happen to change.
        if (nowMs - _seekReleaseAtMs > _config.SeekTimeoutMs)
        {
            _seekReleasePetId = null;
            return null;
        }

        // Both must be idle (not busy).
        // P3-003 R2 F-01: A busy pet is a temporary condition (animation,
        // probe, grievance) — preserve the candidate and retry next tick.
        if (petA.IsBusy || petB.IsBusy)
            return null;

        // P3-003 R2 F-01: Both must be on the bottom edge (not airborne).
        // After the react delay and physics stop, if a pet is NOT on the
        // bottom edge, that is a STABLE unreachable condition — the pet
        // is airborne or on a different surface. Clear the candidate so
        // it doesn't linger and fire when the pet later lands somewhere.
        if (!petA.IsOnBottomEdge || !petB.IsOnBottomEdge)
        {
            _seekReleasePetId = null;
            return null;
        }

        // P3-003 R2 F-01: Must be on the same monitor. Different monitors
        // is a STABLE unreachable condition — clear the candidate.
        if (!SameMonitor(petA, petB))
        {
            _seekReleasePetId = null;
            return null;
        }

        // P3-003 R2 F-01: Must be on the same bottom-edge activity surface
        // (vertical tolerance). A large vertical gap is a STABLE unreachable
        // condition — clear the candidate.
        if (Math.Abs(petA.BottomEdgeY - petB.BottomEdgeY) > _config.SeekBottomToleranceDip)
        {
            _seekReleasePetId = null;
            return null;
        }

        // Seek cooldown check.
        // P3-003 R2 F-01: Cooldown is a temporary condition — preserve the
        // candidate and retry next tick (the candidate lifetime limit above
        // prevents indefinite waiting).
        if (IsInSeekCooldown(petA.PetId, nowMs) || IsInSeekCooldown(petB.PetId, nowMs))
            return null;

        // Horizontal distance (along bottom edge).
        var hDist = Math.Abs(petA.CenterX - petB.CenterX);

        // Already close enough — no need to seek.
        if (hDist <= _config.SeekStopDistanceDip)
        {
            // Consume the candidate so it doesn't linger.
            _seekReleasePetId = null;
            return null;
        }

        // Too far — beyond max travel range.
        if (hDist > _config.SeekMaxDistanceDip)
        {
            _seekReleasePetId = null;
            return null;
        }

        // P3-003 R1 F-01: The released pet is the target (it was dragged
        // to a new position). The OTHER pet is the seeker (it was left
        // behind and now travels toward the released pet).
        string seekerId, targetId;
        double seekerX, targetX;
        seekerId = otherPet.PetId;
        targetId = releasedPet.PetId;
        seekerX = otherPet.CenterX;
        targetX = releasedPet.CenterX;

        // Consume the release candidate.
        _seekReleasePetId = null;

        // Start seeking session.
        var sessionId = $"seek-{nowMs:F0}-{seekerId}";
        var session = new SocialSession(
            sessionId, seekerId, targetId,
            SocialPhase.Seeking, nowMs, nowMs);
        _session = session;
        _seekStartedAtMs = nowMs;
        _seekSeekerStartX = seekerX;
        _lastSeekAdvanceMs = nowMs;

        // First step: move toward target.
        var direction = targetX > seekerX ? 1.0 : -1.0;
        var step = Math.Min(_config.SeekStepDip, hDist - _config.SeekStopDistanceDip);
        if (step <= 0)
            step = _config.SeekStepDip;

        return new SocialTickResult(session, true,
            SocialExpression.Notice, null, true, false,
            seekStepDelta: direction * step,
            seekerId: seekerId);
    }

    /// <summary>
    /// P3-003: Advance the Seeking phase. Moves the seeker toward the target
    /// along the bottom edge. Stops when within SeekStopDistanceDip,
    /// cancels on timeout or max distance exceeded.
    /// 
    /// P3-003 R1 F-02: Step distance is computed from elapsed time since the
    /// last advance, not from the number of Tick calls. This ensures the
    /// movement speed is independent of how many window timer callbacks fire
    /// within the same interval. The reference tick duration is ~33ms (30 FPS).
    /// </summary>
    private SocialTickResult AdvanceSeeking(double nowMs, PetSocialState petA, PetSocialState petB)
    {
        var (stateSeeker, stateTarget) = MapByRole(petA, petB);

        // Timeout check.
        if (nowMs - _seekStartedAtMs >= _config.SeekTimeoutMs)
            return CancelSession(CancelReason.Timeout, nowMs);

        // P3-003 R1 F-03: Validate BOTH pets' support during seeking — not
        // just the target. If either pet loses bottom-edge support or
        // changes monitor, cancel immediately.
        if (!stateSeeker.IsOnBottomEdge || !stateTarget.IsOnBottomEdge)
            return CancelSession(CancelReason.SeekTargetGone, nowMs);
        if (!SameMonitor(stateSeeker, stateTarget))
            return CancelSession(CancelReason.SeekTargetGone, nowMs);

        // Current horizontal distance.
        var hDist = Math.Abs(stateSeeker.CenterX - stateTarget.CenterX);

        // Arrived: within stop distance.
        if (hDist <= _config.SeekStopDistanceDip)
        {
            var arrived = _session.WithPhase(SocialPhase.Arrived, nowMs);
            _session = arrived;
            return new SocialTickResult(arrived, true,
                SocialExpression.Notice, null, false, false,
                seekStepDelta: 0,
                seekerId: _session.InitiatorId);
        }

        // Max distance exceeded.
        var traveled = Math.Abs(stateSeeker.CenterX - _seekSeekerStartX);
        if (traveled >= _config.SeekMaxDistanceDip)
        {
            // Give up — arrived at max travel distance.
            var arrived = _session.WithPhase(SocialPhase.Arrived, nowMs);
            _session = arrived;
            return new SocialTickResult(arrived, true,
                SocialExpression.Notice, null, false, false,
                seekStepDelta: 0,
                seekerId: _session.InitiatorId);
        }

        // P3-003 R1 F-02: Compute step from elapsed time, not call count.
        // Reference: SeekStepDip per ~33ms tick. Multiple callbacks within
        // the same ~33ms window produce only one step's worth of movement.
        var elapsedMs = nowMs - _lastSeekAdvanceMs;
        var tickInterval = 33.0; // ~30 FPS reference
        var stepCount = Math.Floor(elapsedMs / tickInterval);
        if (stepCount < 1)
        {
            // Not enough time has passed for another step — output zero delta.
            return new SocialTickResult(_session, true,
                SocialExpression.Notice, null, false, false,
                seekStepDelta: 0,
                seekerId: _session.InitiatorId);
        }

        // Advance the reference time by the consumed steps.
        _lastSeekAdvanceMs += stepCount * tickInterval;

        // Step toward target.
        var direction = stateTarget.CenterX > stateSeeker.CenterX ? 1.0 : -1.0;
        var baseStep = Math.Min(_config.SeekStepDip, hDist - _config.SeekStopDistanceDip);
        if (baseStep <= 0)
            baseStep = _config.SeekStepDip;
        var totalStep = baseStep * stepCount;

        // Don't overshoot the stop distance.
        var remaining = hDist - _config.SeekStopDistanceDip;
        if (totalStep > remaining)
            totalStep = remaining;

        return new SocialTickResult(_session, true,
            SocialExpression.Notice, null, false, false,
            seekStepDelta: direction * totalStep,
            seekerId: _session.InitiatorId);
    }

    /// <summary>
    /// P3-003: Check whether two pets are on the same monitor.
    /// Both must have a non-null MonitorId that matches.
    /// </summary>
    private static bool SameMonitor(PetSocialState a, PetSocialState b)
    {
        if (a.MonitorId is null || b.MonitorId is null)
            return false;
        return a.MonitorId == b.MonitorId;
    }

    private bool IsInSeekCooldown(string petId, double nowMs)
    {
        // Seek cooldown reuses the same cooldown dictionary but with
        // SeekCooldownMs duration. Since StartSeekCooldown writes with
        // SeekCooldownMs, the existing IsInCooldown check already covers it.
        // This method is kept for clarity but delegates to the same dict.
        return _cooldownEndByPet.TryGetValue(petId, out var end) && nowMs < end;
    }

    private void StartSeekCooldown(string petA, string petB, double nowMs)
    {
        _cooldownEndByPet[petA] = nowMs + _config.SeekCooldownMs;
        _cooldownEndByPet[petB] = nowMs + _config.SeekCooldownMs;
    }

    // ── P3-004: Chase state machine ─────────────────────────────────────

    /// <summary>
    /// P3-004: Build a stable per-pair key for chase attempt dedup.
    /// The key is sorted by PetId so that (A,B) and (B,A) map to the same entry.
    /// </summary>
    private static string ChasePairKey(string petA, string petB)
    {
        return string.CompareOrdinal(petA, petB) <= 0
            ? $"{petA}|{petB}"
            : $"{petB}|{petA}";
    }

    /// <summary>
    /// P3-004: Try to start a chase invite. Conditions (PRODUCT R-05):
    /// - Coordinator is idle (no active session, not in cooldown).
    /// - Both pets stable idle, on the same monitor's bottom edge.
    /// - Center distance 120–300 DIP.
    /// - At least 120 DIP clear space along one horizontal direction.
    /// - Per-pair attempt interval (ChaseAttemptIntervalMs) has elapsed.
    /// - Roll succeeds with ChaseInviteProbability.
    /// The pet on the "open" side (with more clearance) becomes the leader;
    /// the other is the follower. The invite phase reuses the Noticing
    /// expression (curious look) while waiting for the target's response.
    /// </summary>
    private SocialTickResult? TryStartChaseInvite(double nowMs, PetSocialState petA, PetSocialState petB)
    {
        // P3-004 R1 F-02: Chase has its own independent cooldown — check
        // the separate chase cooldown dictionary, not the shared one.
        if (IsInChaseCooldown(petA.PetId, nowMs) || IsInChaseCooldown(petB.PetId, nowMs))
            return null;

        // P3-004 R1 F-02: Both pets must have been stably idle for at least
        // MinIdleMs. This prevents chase from firing immediately after a
        // release while a seeking candidate is still waiting for its turn.
        // IsBusy is not enough — a pet that just became idle this tick
        // should not be grabbed for chase.
        if (!petA.IsIdleFor(_config.MinIdleMs, nowMs) ||
            !petB.IsIdleFor(_config.MinIdleMs, nowMs))
            return null;

        // P3-004 R1 F-02: If there is an unconsumed release candidate that
        // has not yet expired (within SeekTimeoutMs), chase must NOT fire —
        // the seeking opportunity takes priority. The candidate is preserved
        // for TryStartSeeking on a future tick.
        if (_seekReleasePetId is not null)
        {
            var candidateAge = nowMs - _seekReleaseAtMs;
            if (candidateAge <= _config.SeekTimeoutMs)
                return null;
        }

        // Both must be idle (not busy).
        if (petA.IsBusy || petB.IsBusy)
            return null;

        // Both must be on the bottom edge.
        if (!petA.IsOnBottomEdge || !petB.IsOnBottomEdge)
            return null;

        // Same monitor.
        if (!SameMonitor(petA, petB))
            return null;

        // Same bottom-edge surface.
        if (Math.Abs(petA.BottomEdgeY - petB.BottomEdgeY) > _config.SeekBottomToleranceDip)
            return null;

        // Per-pair attempt interval.
        var pairKey = ChasePairKey(petA.PetId, petB.PetId);
        _lastChaseAttemptByPair.TryGetValue(pairKey, out var lastAttempt);
        if (nowMs - lastAttempt < _config.ChaseAttemptIntervalMs)
            return null;
        _lastChaseAttemptByPair[pairKey] = nowMs;

        // Center distance check.
        var hDist = Math.Abs(petA.CenterX - petB.CenterX);
        if (hDist < _config.ChaseDistanceMinDip || hDist > _config.ChaseDistanceMaxDip)
            return null;

        // P3-004 R2 F-01: Determine travel direction using real work-area
        // bounds and ChaseClearanceDip, measured from the COMPLETE WINDOW
        // EDGE — not the window center. The leader's leading edge (the
        // edge in the travel direction) must have at least ChaseClearanceDip
        // of clear space to the work-area boundary. If neither direction
        // has enough clearance, no chase starts. Both pets must share the
        // same work-area bounds (same monitor), already verified above.
        var waLeft = petA.WorkAreaLeft ?? 0;
        var waRight = petA.WorkAreaRight ?? 0;
        // P3-004 R3 F-01: Use each pet's OWN window width for clearance
        // checks, not a single shared halfW. The two pets may have
        // different window widths, so the leader's leading-edge clearance
        // must use the leader's halfW, not petA's.
        var halfWa = petA.WindowWidth / 2.0;
        var halfWb = petB.WindowWidth / 2.0;

        // Compute clearance for each direction using complete window edges.
        // Rightward: leader is the rightmost pet, clearance = waRight - (rightmost center + leaderHalfW).
        // Leftward: leader is the leftmost pet, clearance = (leftmost center - leaderHalfW) - waLeft.
        double rightClearance, leftClearance;
        string rightLeaderId, leftLeaderId, rightFollowerId, leftFollowerId;
        double rightLeaderX, leftLeaderX, rightFollowerX, leftFollowerX;

        if (petA.CenterX <= petB.CenterX)
        {
            // A is left, B is right.
            // Rightward: B leads (rightmost), clearance = waRight - (B.centerX + halfWb).
            rightClearance = waRight - (petB.CenterX + halfWb);
            rightLeaderId = petB.PetId;
            rightFollowerId = petA.PetId;
            rightLeaderX = petB.CenterX;
            rightFollowerX = petA.CenterX;
            // Leftward: A leads (leftmost), clearance = (A.centerX - halfWa) - waLeft.
            leftClearance = (petA.CenterX - halfWa) - waLeft;
            leftLeaderId = petA.PetId;
            leftFollowerId = petB.PetId;
            leftLeaderX = petA.CenterX;
            leftFollowerX = petB.CenterX;
        }
        else
        {
            // A is right, B is left.
            // Rightward: A leads (rightmost), clearance = waRight - (A.centerX + halfWa).
            rightClearance = waRight - (petA.CenterX + halfWa);
            rightLeaderId = petA.PetId;
            rightFollowerId = petB.PetId;
            rightLeaderX = petA.CenterX;
            rightFollowerX = petB.CenterX;
            // Leftward: B leads (leftmost), clearance = (B.centerX - halfWb) - waLeft.
            leftClearance = (petB.CenterX - halfWb) - waLeft;
            leftLeaderId = petB.PetId;
            leftFollowerId = petA.PetId;
            leftLeaderX = petB.CenterX;
            leftFollowerX = petA.CenterX;
        }

        // Pick the direction with sufficient clearance. Prefer the direction
        // with more clearance; if only one qualifies, use that. If neither
        // qualifies, no chase.
        double direction;
        string leaderId, followerId;
        double leaderX, followerX;

        var rightOk = rightClearance >= _config.ChaseClearanceDip;
        var leftOk = leftClearance >= _config.ChaseClearanceDip;

        if (rightOk && leftOk)
        {
            // Pick the direction with more clearance.
            if (rightClearance >= leftClearance)
            {
                direction = 1.0;
                leaderId = rightLeaderId;
                followerId = rightFollowerId;
                leaderX = rightLeaderX;
                followerX = rightFollowerX;
            }
            else
            {
                direction = -1.0;
                leaderId = leftLeaderId;
                followerId = leftFollowerId;
                leaderX = leftLeaderX;
                followerX = leftFollowerX;
            }
        }
        else if (rightOk)
        {
            direction = 1.0;
            leaderId = rightLeaderId;
            followerId = rightFollowerId;
            leaderX = rightLeaderX;
            followerX = rightFollowerX;
        }
        else if (leftOk)
        {
            direction = -1.0;
            leaderId = leftLeaderId;
            followerId = leftFollowerId;
            leaderX = leftLeaderX;
            followerX = leftFollowerX;
        }
        else
        {
            // Neither direction has enough clearance — no chase.
            return null;
        }

        // Roll for invite success.
        if (_random.NextDouble() >= _config.ChaseInviteProbability)
            return null;

        // Start chase invite session. Initiator = follower (the one who
        // "invites" to play), Target = leader (the one who will run ahead).
        // The invite phase reuses the Noticing expression.
        var sessionId = $"chase-{nowMs:F0}-{followerId}";
        var session = new SocialSession(
            sessionId, followerId, leaderId,
            SocialPhase.ChaseInviting, nowMs, nowMs,
            chaseLeaderId: leaderId, chaseDirection: direction);
        _session = session;

        return new SocialTickResult(session, true,
            SocialExpression.Notice, SocialExpression.Notice, true, false);
    }

    /// <summary>
    /// P3-004: Advance the ChaseInviting phase. Waits for the target's
    /// (leader's) response using the same personality-based delay and
    /// RespondProbability as mutual-notice. On accept → Chasing; on
    /// reject → Finished with chase cooldown.
    /// </summary>
    private SocialTickResult AdvanceChaseInviting(double nowMs, PetSocialState petA, PetSocialState petB)
    {
        var (stateInit, stateTgt) = MapByRole(petA, petB);
        // stateInit = follower (inviter), stateTgt = leader (invited)

        // Session timeout.
        if (nowMs - _session.StartedAtMs >= _config.SessionTimeoutMs)
            return CancelSession(CancelReason.Timeout, nowMs);

        // Invite duration limit (reuse NoticeDurationMs as the invite window).
        if (nowMs - _session.PhaseStartedAtMs >= _config.NoticeDurationMs)
        {
            // No response — treat as rejected/ended.
            var finished = _session.WithPhase(SocialPhase.Finished, nowMs);
            _session = default;
            StartChaseCooldown(finished.InitiatorId, finished.TargetId, nowMs);
            return new SocialTickResult(finished, false,
                SocialExpression.ReturnToIdle, SocialExpression.ReturnToIdle, false, true);
        }

        // Check if target's respond delay has elapsed.
        var respondDelay = stateTgt.Personality == Personality.SlowPaced
            ? _config.SlowPacedRespondDelayMs
            : _config.RespondDelayMs;

        if (nowMs - _session.PhaseStartedAtMs >= respondDelay)
        {
            if (_random.NextDouble() < _config.RespondProbability)
            {
                // Accepted — transition to Chasing.
                var chasing = _session.WithPhase(SocialPhase.Chasing, nowMs);
                _session = chasing;
                _chaseStartedAtMs = nowMs;
                _lastChaseAdvanceMs = nowMs;

                // Record starting positions from the current snapshot.
                // Leader is the target, follower is the initiator.
                _chaseLeaderStartX = stateTgt.CenterX;
                _chaseFollowerStartX = stateInit.CenterX;

                // First step: both move in the chase direction.
                var direction = _session.ChaseDirection ?? 1.0;
                var leaderId = _session.ChaseLeaderId ?? stateTgt.PetId;
                var followerId = stateInit.PetId;

                var elapsedSec = 0.0; // first tick: no elapsed time yet
                var leaderStep = _config.ChaseLeaderSpeedDipPerSec * elapsedSec;
                var followerStep = _config.ChaseFollowerSpeedDipPerSec * elapsedSec;

                return new SocialTickResult(chasing, true,
                    SocialExpression.Notice, SocialExpression.Respond, false, false,
                    chaseLeaderDelta: leaderStep,
                    chaseFollowerDelta: followerStep,
                    chaseLeaderId: leaderId,
                    chaseFollowerId: followerId);
            }
            else
            {
                // Rejected — end with chase cooldown.
                var rejected = _session.WithPhase(SocialPhase.Rejected, nowMs);
                _session = rejected;
                return new SocialTickResult(rejected, true,
                    SocialExpression.Notice, SocialExpression.Reject, false, false);
            }
        }

        // Still inviting.
        return new SocialTickResult(_session, true,
            SocialExpression.Notice, SocialExpression.Notice, false, false);
    }

    /// <summary>
    /// P3-004: Advance the Chasing phase. Both pets move along the bottom
    /// edge in the chase direction. The leader runs at ChaseLeaderSpeedDipPerSec,
    /// the follower pursues at ChaseFollowerSpeedDipPerSec, but the follower
    /// is constrained to maintain at least ChaseMinDistanceDip gap.
    ///
    /// Ends when:
    /// - ChaseDurationMs elapsed → ChaseArrived
    /// - Either pet has traveled ChaseMaxTravelDip → ChaseArrived
    /// - Either pet loses support or changes monitor → Cancel
    /// </summary>
    private SocialTickResult AdvanceChasing(double nowMs, PetSocialState petA, PetSocialState petB)
    {
        var (stateInit, stateTgt) = MapByRole(petA, petB);
        // stateInit = follower, stateTgt = leader

        var direction = _session.ChaseDirection ?? 1.0;
        var leaderId = _session.ChaseLeaderId ?? stateTgt.PetId;
        var followerId = stateInit.PetId;

        // Duration limit.
        if (nowMs - _chaseStartedAtMs >= _config.ChaseDurationMs)
        {
            var arrived = _session.WithPhase(SocialPhase.ChaseArrived, nowMs);
            _session = arrived;
            return new SocialTickResult(arrived, true,
                SocialExpression.ReturnToIdle, SocialExpression.ReturnToIdle, false, false,
                chaseLeaderDelta: 0, chaseFollowerDelta: 0,
                chaseLeaderId: leaderId, chaseFollowerId: followerId);
        }

        // Support and monitor checks (also done in CheckInterrupt, but
        // double-check here since AdvanceSession is called after CheckInterrupt
        // returns null — the state may have changed between calls in edge cases).
        if (!stateInit.IsOnBottomEdge || !stateTgt.IsOnBottomEdge)
            return CancelSession(CancelReason.SeekTargetGone, nowMs);
        if (!SameMonitor(stateInit, stateTgt))
            return CancelSession(CancelReason.SeekTargetGone, nowMs);

        // P3-004 R1 F-03: Cap single-step compensation to prevent large
        // jumps after stalls. ARCHITECTURE: limit single compensation and
        // discard backlog. The max single-step elapsed is 100ms; anything
        // beyond that is discarded (treated as if only 100ms elapsed).
        var elapsedMs = nowMs - _lastChaseAdvanceMs;
        if (elapsedMs < 16.0)
        {
            // Not enough time — output zero delta (no movement this tick).
            return new SocialTickResult(_session, true,
                SocialExpression.Notice, SocialExpression.Respond, false, false,
                chaseLeaderDelta: 0, chaseFollowerDelta: 0,
                chaseLeaderId: leaderId, chaseFollowerId: followerId);
        }

        // F-03: Discard backlog beyond 100ms — only compensate for at most
        // one stalled frame, not the entire gap.
        const double MaxChaseStepMs = 100.0;
        if (elapsedMs > MaxChaseStepMs)
            elapsedMs = MaxChaseStepMs;

        _lastChaseAdvanceMs = nowMs;
        var elapsedSec = elapsedMs / 1000.0;

        // Leader step: moves at leader speed in the chase direction.
        var leaderStep = _config.ChaseLeaderSpeedDipPerSec * elapsedSec * direction;

        // Follower step: moves at follower speed in the chase direction,
        // but must not close the gap below ChaseMinDistanceDip.
        var currentGap = Math.Abs(stateTgt.CenterX - stateInit.CenterX);
        var followerStep = _config.ChaseFollowerSpeedDipPerSec * elapsedSec * direction;

        // If follower is faster than leader, it could close the gap.
        // Constrain follower so the gap never goes below ChaseMinDistanceDip.
        var followerSpeed = _config.ChaseFollowerSpeedDipPerSec;
        var leaderSpeed = _config.ChaseLeaderSpeedDipPerSec;
        if (followerSpeed > leaderSpeed)
        {
            // The follower gains on the leader by (followerSpeed - leaderSpeed) * elapsedSec.
            var gain = (followerSpeed - leaderSpeed) * elapsedSec;
            var excessGain = currentGap - _config.ChaseMinDistanceDip - gain;
            if (excessGain < 0)
            {
                // Reduce follower step to maintain minimum distance.
                var reduction = -excessGain;
                var followerStepMag = Math.Abs(followerStep) - reduction;
                if (followerStepMag < 0)
                    followerStepMag = 0;
                followerStep = followerStepMag * direction;
            }
        }

        // Max travel distance check: if either pet has already traveled
        // ChaseMaxTravelDip, end the chase.
        var leaderTraveled = Math.Abs(stateTgt.CenterX - _chaseLeaderStartX);
        var followerTraveled = Math.Abs(stateInit.CenterX - _chaseFollowerStartX);
        if (leaderTraveled >= _config.ChaseMaxTravelDip ||
            followerTraveled >= _config.ChaseMaxTravelDip)
        {
            var arrived = _session.WithPhase(SocialPhase.ChaseArrived, nowMs);
            _session = arrived;
            return new SocialTickResult(arrived, true,
                SocialExpression.ReturnToIdle, SocialExpression.ReturnToIdle, false, false,
                chaseLeaderDelta: 0, chaseFollowerDelta: 0,
                chaseLeaderId: leaderId, chaseFollowerId: followerId);
        }

        // Cap steps so neither pet exceeds the max travel limit this tick.
        var leaderRemaining = _config.ChaseMaxTravelDip - leaderTraveled;
        var followerRemaining = _config.ChaseMaxTravelDip - followerTraveled;
        if (Math.Abs(leaderStep) > leaderRemaining)
            leaderStep = leaderRemaining * direction;
        if (Math.Abs(followerStep) > followerRemaining)
            followerStep = followerRemaining * direction;

        // P3-004 R2 F-01: Pre-apply boundary check using the SAME pair of
        // snapshots. Verify both pets' projected COMPLETE WINDOW EDGES stay
        // within the work area — not just the center. If either pet's
        // leading or trailing edge would exceed the boundary, BOTH stop
        // (zero deltas) and the session ends. This prevents single-sided
        // writes and ensures the full window (not just center) respects
        // the work-area bounds.
        // P3-004 R3 F-01: Use each pet's OWN window width for the
        // boundary projection check. The leader (stateTgt) and follower
        // (stateInit) may have different window widths, so each pet's
        // projected edges must use its own halfW.
        var waLeft = stateTgt.WorkAreaLeft ?? 0;
        var waRight = stateTgt.WorkAreaRight ?? 0;
        var leaderHalfW = stateTgt.WindowWidth / 2.0;
        var followerHalfW = stateInit.WindowWidth / 2.0;
        var leaderProjectedCenter = stateTgt.CenterX + leaderStep;
        var followerProjectedCenter = stateInit.CenterX + followerStep;
        var leaderProjectedLeft = leaderProjectedCenter - leaderHalfW;
        var leaderProjectedRight = leaderProjectedCenter + leaderHalfW;
        var followerProjectedLeft = followerProjectedCenter - followerHalfW;
        var followerProjectedRight = followerProjectedCenter + followerHalfW;

        if (leaderProjectedLeft < waLeft || leaderProjectedRight > waRight ||
            followerProjectedLeft < waLeft || followerProjectedRight > waRight)
        {
            // Either pet's window edge would go out of bounds — both stop, end chase.
            var arrived = _session.WithPhase(SocialPhase.ChaseArrived, nowMs);
            _session = arrived;
            return new SocialTickResult(arrived, true,
                SocialExpression.ReturnToIdle, SocialExpression.ReturnToIdle, false, false,
                chaseLeaderDelta: 0, chaseFollowerDelta: 0,
                chaseLeaderId: leaderId, chaseFollowerId: followerId);
        }

        // F-01: Verify relative order and minimum spacing on the final
        // clipped deltas. If the follower would overtake the leader,
        // zero the follower step.
        var projectedGap = Math.Abs(leaderProjectedCenter - followerProjectedCenter);
        if (projectedGap < _config.ChaseMinDistanceDip)
        {
            followerStep = 0;
        }

        return new SocialTickResult(_session, true,
            SocialExpression.Notice, SocialExpression.Respond, false, false,
            chaseLeaderDelta: leaderStep,
            chaseFollowerDelta: followerStep,
            chaseLeaderId: leaderId,
            chaseFollowerId: followerId);
    }

    private bool IsInChaseCooldown(string petId, double nowMs)
    {
        return _chaseCooldownEndByPet.TryGetValue(petId, out var end) && nowMs < end;
    }

    private void StartChaseCooldown(string petA, string petB, double nowMs)
    {
        _chaseCooldownEndByPet[petA] = nowMs + _config.ChaseCooldownMs;
        _chaseCooldownEndByPet[petB] = nowMs + _config.ChaseCooldownMs;
    }
}
