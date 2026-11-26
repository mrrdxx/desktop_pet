using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using DesktopPet.Core.Events;
using DesktopPet.Core.Persistence;
using Microsoft.Win32;

namespace DesktopPet.App.Platform;

/// <summary>
/// P6-001: App-level service for local usage statistics — approximates
/// foreground-active periods and the normalized executable base name of the
/// application observing them, with enable/disable and clear entry points.
/// <para>
/// Storage is deliberately independent from the pet archive
/// (state.json / settings.json): it owns a separate <c>stats.json</c> with
/// its own schema version and its own safe-write implementation. It never
/// touches <see cref="PetStateArchive"/>, never reads window titles / URLs /
/// content / keys, and never keeps full paths or PIDs as long-term identity.
/// </para>
/// <para>
/// Design (per PRODUCT R-09 / ARCHITECTURE §7 / P6-PLAN R2):
/// <list type="bullet">
/// <item>Sampling: a 5 s DispatcherTimer. Each tick reads the monotonic
/// clock once, then the idle sampler (<see cref="InputIdleSampler"/>) and
/// the foreground sampler (<see cref="ForegroundAppSampler"/>) with that
/// same snapshot, and feeds <see cref="UsageStatsCollector.SampleTick"/>.</item>
/// <item>Lifecycle: pause/lock/sleep are independent suspension reasons —
/// while any is active the collector gates sampling and nothing accumulates
/// (<see cref="UsageStatsCollector.SetPaused"/>/
/// <see cref="UsageStatsCollector.SetLocked"/>/
/// <see cref="UsageStatsCollector.SetSleeping"/>); unlock/resume each clear
/// their own reason, record their own event and rebuild the baseline. System
/// events fire on a system thread and are marshaled to the Dispatcher thread
/// (when one is available) so lifecycle handling, timer ticks and
/// persistence are all serialized on a single thread.</item>
/// <item>Persistence: every drain (sample tail / lifecycle / stop) prunes to
/// the raw retention window — while collection is disabled too — and then
/// writes a single committed snapshot to <c>stats.json</c> (safe write:
/// same-directory temp file, then move with overwrite). A failed write is
/// contained (never thrown to callers), reported via
/// <see cref="LastWriteError"/> and retried on the next drain. The drain
/// drops any pending write unit whose generation no longer matches the
/// collector (clear guard): an old write can never resurrect cleared data.</item>
/// <item>Clear: bumps the collector generation, clears segments / events /
/// pending / mismatch, removes the damaged/temp side artifacts of this
/// storage (<c>.corrupted</c>, <c>.tmp</c>) and immediately persists the
/// cleared state. The enable toggle is a settings-owned value and is
/// preserved.</item>
/// </list>
/// </para>
/// </summary>
public sealed class UsageStatsService : IDisposable
{
    private static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly UsageStatsCollector _collector;
    private readonly InputIdleSampler _idleSampler;
    private readonly ForegroundAppSampler _foregroundSampler;
    private readonly MonotonicClock _clock;
    private readonly Dispatcher? _dispatcher;
    private readonly Func<DateTimeOffset> _localNow;
    private readonly string _statsFilePath;
    private readonly bool _autoStartTimer;

    private readonly DispatcherTimer _sampleTimer;
    private bool _stopped;
    private bool _disposed;

    /// <summary>
    /// P6-004（第一小步）：上一次主动气泡展示的本地日期（yyyy-MM-dd）。
    /// 持久化在独立 stats.json，跨重启同样去重；关闭统计保留，清除统计重置。
    /// </summary>
    private string? _bubbleLastShownLocalDate;

    /// <summary>True while the last persistence failed and a later drain has
    /// not yet retried it. Drives the bounded one-shot retry so a transient
    /// write failure is not silently dropped.</summary>
    private bool _persistRetryPending;

    /// <summary>True while a clear's side-artifact deletion (<c>.corrupted</c> /
    /// <c>.tmp</c>) failed and a later drain has not yet retried it. The
    /// failure stays visible via <see cref="LastWriteError"/> and a snapshot
    /// write that succeeds after the clear must NOT mask it — the artifact
    /// removal is only considered closed after a successful retry (F-03 R2).</summary>
    private bool _clearRetryPending;

    /// <summary>Message of the last failed stats write or failed clear
    /// side-artifact deletion, or null when the most recent write succeeded
    /// (or none ran). Never thrown to callers — the failure is contained and
    /// visible here for diagnosis/tests.</summary>
    internal string? LastWriteError { get; private set; }

    /// <summary>
    /// Creates the service with production dependencies: real samplers, the
    /// shared monotonic clock, the current thread's Dispatcher and the
    /// default storage directory (%LOCALAPPDATA%\DesktopPet).
    /// </summary>
    public UsageStatsService()
        : this(
            new UsageStatsCollector(
                () => MonotonicClock.Instance.GetElapsedMs(),
                () => DateTimeOffset.Now),
            new InputIdleSampler(),
            new ForegroundAppSampler(),
            MonotonicClock.Instance,
            Dispatcher.CurrentDispatcher,
            null,
            autoStartTimer: true)
    {
    }

    /// <summary>
    /// Internal constructor with production dependencies but a caller-chosen
    /// storage directory (used by App-level isolation setup). When
    /// <paramref name="autoStartTimer"/> is false the sampling timer is
    /// never started — used by unit-test wiring so controlled
    /// <see cref="SampleTickManually"/> calls are the only sampling source.
    /// </summary>
    internal UsageStatsService(string? saveDirectory, bool autoStartTimer = true)
        : this(
            new UsageStatsCollector(
                () => MonotonicClock.Instance.GetElapsedMs(),
                () => DateTimeOffset.Now),
            new InputIdleSampler(),
            new ForegroundAppSampler(),
            MonotonicClock.Instance,
            Dispatcher.CurrentDispatcher,
            saveDirectory,
            autoStartTimer)
    {
    }

    /// <summary>
    /// Internal constructor for test injection of a custom collector,
    /// idle sampler, foreground sampler, clock, dispatcher and storage
    /// directory. Tests pass a controlled clock and fake samplers; the
    /// collector must be constructed with the same clock so the sample
    /// snapshot and the collector time domain match.
    /// </summary>
    internal UsageStatsService(
        UsageStatsCollector collector,
        InputIdleSampler idleSampler,
        ForegroundAppSampler foregroundSampler,
        MonotonicClock clock,
        Dispatcher? dispatcher = null,
        string? saveDirectory = null,
        bool autoStartTimer = true,
        Func<DateTimeOffset>? localNow = null)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _idleSampler = idleSampler ?? throw new ArgumentNullException(nameof(idleSampler));
        _foregroundSampler = foregroundSampler ?? throw new ArgumentNullException(nameof(foregroundSampler));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _dispatcher = dispatcher;
        _localNow = localNow ?? (() => DateTimeOffset.Now);
        _autoStartTimer = autoStartTimer;
        _statsFilePath = Path.Combine(
            saveDirectory
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopPet"),
            "stats.json");

        _sampleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(UsageStatsCollector.DefaultForegroundSampleMs)
        };
        _sampleTimer.Tick += OnSampleTimerTick;
    }

    /// <summary>True while collection is enabled (user switched it on).</summary>
    public bool IsEnabled => _collector.IsEnabled;

    /// <summary>Collector generation; bumped by every clear (write guard).</summary>
    public int Generation => _collector.Generation;

    /// <summary>Committed segments — exposed for test verification.</summary>
    public IReadOnlyList<UsageStatsSegment> Segments => _collector.Segments;

    /// <summary>Committed lifecycle events — exposed for test verification.</summary>
    public IReadOnlyList<UsageStatsEventRecord> Events => _collector.Events;

    /// <summary>
    /// P6-002: committed aggregated 30-minute buckets — exposed for test
    /// verification and the isolated probe observation log.
    /// </summary>
    public IReadOnlyList<UsageStatsBucket> Buckets => _collector.Buckets;

    /// <summary>Committed identification-failure count — exposed for test verification.</summary>
    public ulong CoverageMismatchCount => _collector.CoverageMismatchCount;

    /// <summary>
    /// P6-003: evaluates internal frequency candidates from the current
    /// committed buckets (see <see cref="UsageStatsFrequencyJudge"/>).
    /// <para>
    /// Computed on demand — no prediction is cached, so turning collection off
    /// or clearing stats makes the next call empty immediately; a restart
    /// re-evaluates the restored buckets and never restores an old prediction.
    /// This is an internal result only: no bubble, no pet movement, no icon
    /// location and no app launching (those belong to P6-004).
    /// </para>
    /// </summary>
    public UsageStatsFrequencyResult GetFrequencyPredictions()
        => UsageStatsFrequencyJudge.Evaluate(
            _collector.Buckets, _localNow(), _collector.IsEnabled);

    /// <summary>
    /// P6-004（第一小步）：“已记录的统计”汇总，只看已提交聚合桶，名称沿用
    /// R-09 口径（近似前台活跃时长、会话次数）。
    /// <para>
    /// P6-004 R1 F-02：读取侧即按含今天的 30 天聚合保留窗口过滤（与采集器
    /// 的 <see cref="UsageStatsCollector.PruneToRetention"/> 同口径），因此
    /// 首次查询就排除过期桶，不依赖下一次 drain 的物理清理；查询不写盘，
    /// 不延长保留期。
    /// </para>
    /// </summary>
    public UsageStatsRecordedSummary GetRecordedSummary()
        => UsageStatsBubblePlanner.SummarizeRecorded(_collector.Buckets, _localNow());

    /// <summary>
    /// P6-004（第一小步）：上一次主动气泡展示的本地日期（yyyy-MM-dd）或 null。
    /// 作为每日去重的输入，重启后由 <c>stats.json</c> 恢复。
    /// </summary>
    internal string? BubbleLastShownLocalDate => _bubbleLastShownLocalDate;

    /// <summary>
    /// P6-004（第一小步）：记录一次主动气泡已展示，并立即落盘，使每日去重
    /// 跨重启生效。清除统计会重置该日期。
    /// </summary>
    public void RecordStatsBubbleShown(string localDate)
    {
        if (string.IsNullOrWhiteSpace(localDate)) return;
        _bubbleLastShownLocalDate = localDate;
        PersistSnapshot();
    }

    /// <summary>Path of the independent stats archive (for tests / diagnosis).</summary>
    internal string StatsFilePath => _statsFilePath;

    /// <summary>
    /// True while sampling is suspended for ANY active reason (global pause,
    /// session lock or system sleep — any combination). Read-only forwarding
    /// of the collector state, exposed for the isolated live probe to record
    /// suspend/resume baselines in its observation log.
    /// </summary>
    internal bool IsSuspended => _collector.IsSuspended;

    /// <summary>
    /// Starts the service: restores the persisted archive (kept even while
    /// collection is disabled — existing in-retention data survives a
    /// restart) and begins the sampling timer. Call once after app startup.
    /// </summary>
    public void Start()
    {
        if (_disposed || _stopped) return;

        var archive = TryLoadArchive();
        _collector.Restore(archive);
        _bubbleLastShownLocalDate = archive.BubbleLastShownLocalDate;
        if (_autoStartTimer)
            _sampleTimer.Start();
    }

    /// <summary>
    /// Stops sampling and persists anything pending (with retention pruning).
    /// Does NOT dispose — call <see cref="Dispose"/> for full cleanup.
    /// </summary>
    public void Stop()
    {
        if (_disposed || _stopped) return;
        _stopped = true;
        _sampleTimer.Stop();
        _collector.Disable();
        DrainPending();
    }

    /// <summary>
    /// Registers the app-start lifecycle event (pet itself started). The
    /// collector records it only while enabled; recorded once per process
    /// startup by the App layer.
    /// </summary>
    public void OnAppStart()
    {
        _collector.OnAppStart();
    }

    /// <summary>
    /// Enables collection. Existing in-retention data stays; the baseline
    /// starts fresh from the next sample.
    /// </summary>
    public void Enable()
    {
        _collector.Enable();
    }

    /// <summary>
    /// Disables collection: stops new sampling (no new data), drops any
    /// unverified tail (already-committed segments stay), persists and
    /// prunes. Retention cleanup keeps running on later drains even while
    /// disabled. Never throws for stats I/O failures.
    /// </summary>
    public void Disable()
    {
        _collector.Disable();
        DrainPending();
    }

    /// <summary>
    /// Clears all statistics (segments, events, pending writes, mismatch
    /// counter), removes this storage's side artifacts (<c>.corrupted</c>
    /// preserved-damaged copy and <c>.tmp</c> temp, if any) and immediately
    /// persists the cleared state. The enable toggle and the settings are
    /// owned by the caller and preserved. The generation bump rejects any
    /// in-flight write from before the clear — cleared data can never be
    /// resurrected. Never throws for stats I/O failures.
    /// </summary>
    public void ClearAll()
    {
        _collector.ClearAll();
        // P6-004（第一小步）：清除同时重置主动气泡的每日去重标记（旧展示
        // 依据已不存在）。关闭统计不经过这里，因此只保留当天已展示标记。
        _bubbleLastShownLocalDate = null;
        if (!DeleteSideArtifacts())
        {
            // F-03 R2: a locked/un-deletable artifact could not be removed
            // right now. Keep the failure visible (LastWriteError) and retry
            // on the next drain — the cleared snapshot persisted below must
            // not mask the incomplete removal (no silent drop).
            _clearRetryPending = true;
        }
        PersistSnapshot();
    }

    /// <summary>
    /// Deletes this storage's damaged/temp side artifacts only (<c>.corrupted</c>
    /// and <c>.tmp</c> next to <c>stats.json</c>). Never deletes anything
    /// else and never throws: a failed cleanup is reported via
    /// <see cref="LastWriteError"/> and retried by a later drain. Returns
    /// true when every artifact is gone (or was already absent).
    /// </summary>
    private bool DeleteSideArtifacts()
    {
        var failed = false;
        foreach (var path in new[] { _statsFilePath + ".corrupted", _statsFilePath + ".tmp" })
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                System.Diagnostics.Trace.TraceWarning("Could not delete stats artifact {0}: {1}", path, ex.Message);
                if (!failed)
                {
                    failed = true;
                    LastWriteError = ex.Message;
                }
            }
        }
        return !failed;
    }

    /// <summary>
    /// Called by App when the global pause state changes: pausing suspends
    /// sampling (the paused interval is never attributed); unpausing clears
    /// only the pause reason — other suspension reasons (lock/sleep) stay
    /// active independently, and any unverified tail is dropped on the
    /// transition.
    /// </summary>
    public void OnPauseToggled(bool paused)
    {
        if (_dispatcher is not null && _dispatcher.CheckAccess() == false)
        {
            _dispatcher.BeginInvoke(new Action(() => OnPauseToggledCore(paused)));
            return;
        }
        OnPauseToggledCore(paused);
    }

    /// <summary>
    /// Called when the system session switches. <paramref name="reason"/> is
    /// the SessionSwitchReason value (lock = 7 → suspend sampling for the
    /// lock reason; unlock = 8 → clear the lock reason and record the unlock
    /// event).
    /// </summary>
    public void OnSessionSwitch(int reason)
    {
        if (_dispatcher is not null && _dispatcher.CheckAccess() == false)
        {
            _dispatcher.BeginInvoke(new Action(() => OnSessionSwitchCore(reason)));
            return;
        }
        OnSessionSwitchCore(reason);
    }

    /// <summary>
    /// Called when the system power mode changes. <paramref name="mode"/> is
    /// the PowerModes value (Suspend = 3 → suspend sampling for the sleep
    /// reason; Resume = 1 → clear the sleep reason and record the resume
    /// event).
    /// </summary>
    public void OnPowerModeChanged(int mode)
    {
        if (_dispatcher is not null && _dispatcher.CheckAccess() == false)
        {
            _dispatcher.BeginInvoke(new Action(() => OnPowerModeChangedCore(mode)));
            return;
        }
        OnPowerModeChangedCore(mode);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }

    // ── sampling ────────────────────────────────────────────────────────

    /// <summary>
    /// One sampling tick used by the production timer. Reads the monotonic
    /// clock once and feeds the same snapshot to the idle and foreground
    /// samplers (single clock domain), then drains pending writes.
    /// </summary>
    private void OnSampleTimerTick(object? sender, EventArgs e)
    {
        if (_disposed || _stopped) return;
        SampleTick();
    }

    /// <summary>
    /// Internal test entry: runs one sampling tick with the service's
    /// injected samplers, then drains. Mirrors the production timer path.
    /// </summary>
    internal void SampleTickManually()
    {
        SampleTick();
    }

    private void SampleTick()
    {
        // F-01 R2: gate BEFORE reading the idle/foreground samplers. While
        // suspended (pause / lock / sleep — any combination) the service
        // must not read the samplers at all; retention pruning and the drain
        // still run. Previously the foreground sampler was still invoked
        // during suspension (validation observed 2→4 calls).
        if (_collector.IsEnabled && !_collector.IsSuspended)
        {
            var nowMs = _clock.GetElapsedMs();
            var lastInputMs = _idleSampler.GetLastInputMs(nowMs);
            var appId = _foregroundSampler.TryGetForegroundAppId(out var resolutionFailed);
            _collector.SampleTick(appId, lastInputMs, resolutionFailed);
        }
        DrainPending();
    }

    // ── lifecycle dispatch (marshaled to the Dispatcher thread) ─────────

    private void OnPauseToggledCore(bool paused)
    {
        _collector.SetPaused(paused);
        DrainPending();
    }

    private void OnSessionSwitchCore(int reason)
    {
        // SessionSwitchReason.SessionLock = 7, SessionUnlock = 8.
        if (reason == 7)
        {
            _collector.SetLocked(true);
            DrainPending();
        }
        else if (reason == 8)
        {
            _collector.OnUnlock();
            DrainPending();
        }
    }

    private void OnPowerModeChangedCore(int mode)
    {
        // PowerModes.Suspend = 3, PowerModes.Resume = 1.
        if (mode == (int)PowerModes.Suspend)
        {
            _collector.SetSleeping(true);
            DrainPending();
        }
        else if (mode == (int)PowerModes.Resume)
        {
            _collector.OnResume();
            DrainPending();
        }
    }

    // ── persistence: single write queue with generation guard ───────────

    /// <summary>
    /// Drains pending increments into the single write path: prunes to the
    /// raw retention window (also while collection is disabled), drops
    /// pending units whose generation no longer matches (clear guard), and
    /// persists the committed snapshot. No-op when nothing changed — unless
    /// a previous write failed and still awaits its one-shot retry. Never
    /// throws for stats I/O failures.
    /// </summary>
    internal void DrainPending()
    {
        if (_disposed) return;

        // F-03 R2: retry a failed clear's side-artifact deletion before
        // anything else. A successful retry closes the clear (failure state
        // cleared); a still-failing retry keeps the error visible and the
        // drain below must not mask it.
        if (_clearRetryPending)
        {
            if (DeleteSideArtifacts())
            {
                _clearRetryPending = false;
                LastWriteError = null;
            }
        }

        var removed = _collector.PruneToRetention();
        var write = _collector.DrainPending();
        if (write is null && removed == 0 && !_persistRetryPending)
            return;

        // Generation guard: a pending unit produced before a clear must not
        // be written (no resurrection of cleared data).
        if (write is not null && write.Generation != _collector.Generation)
            return;

        PersistSnapshot();
    }

    /// <summary>
    /// Serializes the current committed snapshot to <c>stats.json</c> with a
    /// safe write (same-directory temp file, then move with overwrite).
    /// A failed write is contained: not thrown, surfaced via
    /// <see cref="LastWriteError"/> and retried on the next drain.
    /// Independent from <see cref="Persistence.ArchiveService"/> — the stats
    /// archive is never mixed with the pet archive.
    /// </summary>
    private void PersistSnapshot()
    {
        var archive = new UsageStatsArchive
        {
            SchemaVersion = UsageStatsArchive.CurrentSchemaVersion,
            LastSavedUtc = DateTime.UtcNow.ToString("O"),
            CoverageMismatchCount = _collector.CoverageMismatchCount,
            Segments = _collector.Segments.ToList(),
            Events = _collector.Events.ToList(),
            Buckets = _collector.Buckets.ToList(),
            BubbleLastShownLocalDate = _bubbleLastShownLocalDate
        };
        if (SafeWrite(archive))
        {
            // F-04: a successful write clears the write-failure state —
            // unless a clear's side-artifact deletion is still pending retry
            // (F-03 R2): that incomplete clear must remain visible.
            if (!_clearRetryPending)
            {
                _persistRetryPending = false;
                LastWriteError = null;
            }
        }
        else
        {
            // Bounded one-shot retry: a later drain tries once more. The
            // failure is not thrown and stays visible via LastWriteError.
            _persistRetryPending = true;
        }
    }

    private bool SafeWrite(UsageStatsArchive archive)
    {
        try
        {
            var directory = Path.GetDirectoryName(_statsFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var tempPath = _statsFilePath + ".tmp";
            var json = JsonSerializer.Serialize(archive, s_jsonOpts);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _statsFilePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LastWriteError = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Loads the stats archive tolerantly: a missing file yields a fresh
    /// archive; a corrupted file is preserved as <c>.corrupted</c> and a
    /// fresh archive is returned (never guessed data).
    /// </summary>
    private UsageStatsArchive TryLoadArchive()
    {
        if (!File.Exists(_statsFilePath))
            return new UsageStatsArchive();

        try
        {
            var json = File.ReadAllText(_statsFilePath);
            return JsonSerializer.Deserialize<UsageStatsArchive>(json, s_jsonOpts)
                ?? new UsageStatsArchive();
        }
        catch (JsonException)
        {
            System.Diagnostics.Trace.TraceWarning("Corrupted stats archive; preserving as .corrupted: {0}", _statsFilePath);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Trace.TraceWarning("Cannot read stats archive: {0}", ex.Message);
        }
        catch (NotSupportedException ex)
        {
            System.Diagnostics.Trace.TraceWarning("Unsupported stats archive: {0}", ex.Message);
        }

        try
        {
            var corruptedPath = _statsFilePath + ".corrupted";
            if (File.Exists(corruptedPath))
                File.Delete(corruptedPath);
            File.Move(_statsFilePath, corruptedPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            System.Diagnostics.Trace.TraceWarning("Could not preserve corrupted stats archive: {0}", ex.Message);
        }
        return new UsageStatsArchive();
    }
}