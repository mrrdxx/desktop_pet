namespace DesktopPet.Core.Social;

/// <summary>
/// Tunable parameters for the mutual-notice social coordinator (PRODUCT R-05, P3-002).
/// All values are debug defaults; they may be adjusted after live feel calibration.
/// </summary>
public sealed class SocialConfig
{
    /// <summary>Minimum interval between notice attempts for a single pet (ms).</summary>
    public double NoticeIntervalMs { get; set; } = 8000;

    /// <summary>Probability that a Proactive pet initiates notice per evaluation window.</summary>
    public double NoticeProbability { get; set; } = 0.15;

    /// <summary>Probability that a SlowPaced pet initiates notice per evaluation window.</summary>
    public double SlowPacedNoticeProbability { get; set; } = 0.05;

    /// <summary>Maximum center-to-center distance (DIP) for notice to be possible.</summary>
    public double NoticeDistanceMaxDip { get; set; } = 600;

    /// <summary>Duration limit for the Noticing phase (ms).</summary>
    public double NoticeDurationMs { get; set; } = 3000;

    /// <summary>Response delay for a Proactive pet (ms).</summary>
    public double RespondDelayMs { get; set; } = 800;

    /// <summary>Response delay for a SlowPaced pet (ms). Observable personality difference.</summary>
    public double SlowPacedRespondDelayMs { get; set; } = 2000;

    /// <summary>Duration limit for the Responding phase (ms).</summary>
    public double RespondDurationMs { get; set; } = 2500;

    /// <summary>Probability that the noticed pet responds (vs. rejects).</summary>
    public double RespondProbability { get; set; } = 0.6;

    /// <summary>Cooldown after a session ends for both participants (ms).</summary>
    public double CooldownMs { get; set; } = 15000;

    /// <summary>Total session timeout from Noticing start (ms). Exceeding cancels the session.</summary>
    public double SessionTimeoutMs { get; set; } = 8000;

    /// <summary>Minimum continuous idle time both pets must have before notice can start (ms).</summary>
    public double MinIdleMs { get; set; } = 3000;

    // ── P3-003 Seeking parameters (PRODUCT R-05, ARCHITECTURE §6) ───────

    /// <summary>Delay after A is released before B evaluates whether to seek (ms).</summary>
    public double SeekReactDelayMs { get; set; } = 1500;

    /// <summary>Per-tick step distance along the bottom edge (DIP, ~30 FPS).</summary>
    public double SeekStepDip { get; set; } = 4;

    /// <summary>Minimum center-to-center gap the seeker maintains upon arrival (DIP).</summary>
    public double SeekStopDistanceDip { get; set; } = 120;

    /// <summary>Maximum total distance the seeker is willing to travel along the bottom edge (DIP).</summary>
    public double SeekMaxDistanceDip { get; set; } = 1200;

    /// <summary>Total timeout for the Seeking phase (ms). Exceeding cancels the session.</summary>
    public double SeekTimeoutMs { get; set; } = 20000;

    /// <summary>Cooldown after a seeking session ends for both participants (ms).</summary>
    public double SeekCooldownMs { get; set; } = 20000;

    /// <summary>Vertical tolerance to determine both pets share the same bottom-edge activity surface (DIP).</summary>
    public double SeekBottomToleranceDip { get; set; } = 8;

    // ── P3-004 Chase parameters (PRODUCT R-05, ARCHITECTURE §6) ──────────

    /// <summary>Minimum interval between chase invite evaluations for a given pet pair (ms).</summary>
    public double ChaseAttemptIntervalMs { get; set; } = 30000;

    /// <summary>Probability that a chase invite succeeds when conditions are met.</summary>
    public double ChaseInviteProbability { get; set; } = 0.20;

    /// <summary>Cooldown after a chase session ends (accept, reject, timeout, or interrupt) for both participants (ms).</summary>
    public double ChaseCooldownMs { get; set; } = 90000;

    /// <summary>Maximum duration of the chasing phase (ms). Exceeding ends the session.</summary>
    public double ChaseDurationMs { get; set; } = 3000;

    /// <summary>Maximum total travel distance per pet during chasing (DIP).</summary>
    public double ChaseMaxTravelDip { get; set; } = 240;

    /// <summary>Leader speed along the bottom edge (DIP/s).</summary>
    public double ChaseLeaderSpeedDipPerSec { get; set; } = 80;

    /// <summary>Follower speed along the bottom edge (DIP/s).</summary>
    public double ChaseFollowerSpeedDipPerSec { get; set; } = 100;

    /// <summary>Minimum center-to-center distance maintained between leader and follower during chase (DIP).</summary>
    public double ChaseMinDistanceDip { get; set; } = 120;

    /// <summary>Minimum center-to-center distance for a chase invite to be considered (DIP).</summary>
    public double ChaseDistanceMinDip { get; set; } = 120;

    /// <summary>Maximum center-to-center distance for a chase invite to be considered (DIP).</summary>
    public double ChaseDistanceMaxDip { get; set; } = 300;

    /// <summary>Minimum clear space along the travel direction for a chase to be feasible (DIP).</summary>
    public double ChaseClearanceDip { get; set; } = 120;

    /// <summary>
    /// Creates a deep copy of this config.
    /// </summary>
    public SocialConfig Clone() => new()
    {
        NoticeIntervalMs = NoticeIntervalMs,
        NoticeProbability = NoticeProbability,
        SlowPacedNoticeProbability = SlowPacedNoticeProbability,
        NoticeDistanceMaxDip = NoticeDistanceMaxDip,
        NoticeDurationMs = NoticeDurationMs,
        RespondDelayMs = RespondDelayMs,
        SlowPacedRespondDelayMs = SlowPacedRespondDelayMs,
        RespondDurationMs = RespondDurationMs,
        RespondProbability = RespondProbability,
        CooldownMs = CooldownMs,
        SessionTimeoutMs = SessionTimeoutMs,
        MinIdleMs = MinIdleMs,
        SeekReactDelayMs = SeekReactDelayMs,
        SeekStepDip = SeekStepDip,
        SeekStopDistanceDip = SeekStopDistanceDip,
        SeekMaxDistanceDip = SeekMaxDistanceDip,
        SeekTimeoutMs = SeekTimeoutMs,
        SeekCooldownMs = SeekCooldownMs,
        SeekBottomToleranceDip = SeekBottomToleranceDip,
        ChaseAttemptIntervalMs = ChaseAttemptIntervalMs,
        ChaseInviteProbability = ChaseInviteProbability,
        ChaseCooldownMs = ChaseCooldownMs,
        ChaseDurationMs = ChaseDurationMs,
        ChaseMaxTravelDip = ChaseMaxTravelDip,
        ChaseLeaderSpeedDipPerSec = ChaseLeaderSpeedDipPerSec,
        ChaseFollowerSpeedDipPerSec = ChaseFollowerSpeedDipPerSec,
        ChaseMinDistanceDip = ChaseMinDistanceDip,
        ChaseDistanceMinDip = ChaseDistanceMinDip,
        ChaseDistanceMaxDip = ChaseDistanceMaxDip,
        ChaseClearanceDip = ChaseClearanceDip,
    };
}
