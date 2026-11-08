using System.IO;
using System.Text;

namespace DesktopPet.App.Diagnostics;

/// <summary>
/// Minimal local bounded diagnostic logger per ARCHITECTURE §10 (PREP-001).
///
/// Design constraints:
/// - Does NOT depend on Dispatcher queue or complex locks that could deadlock
///   during termination notifications.
/// - Record failure must NOT throw a second exception, recurse, or deadlock.
/// - Single-file cap: 256 KiB (encoded bytes). Max retained files: 5.
/// - Truncates exception message/stack to prevent unbounded growth.
/// - No log framework, no auto-upload, no keyboard/screen/archive content.
/// </summary>
public sealed class DiagnosticLogger : IDisposable
{
    /// <summary>Default single-file size cap in bytes (256 KiB).</summary>
    public const int DefaultMaxFileBytes = 256 * 1024;

    /// <summary>Default maximum number of retained log files.</summary>
    public const int DefaultMaxRetainedFiles = 5;

    /// <summary>Maximum length of exception message (characters).</summary>
    private const int MaxExceptionMessageLen = 2048;

    /// <summary>Maximum length of stack trace (characters).</summary>
    private const int MaxStackTraceLen = 8192;

    private readonly string _logDirectory;
    private readonly int _maxFileBytes;
    private readonly int _maxRetainedFiles;
    private readonly object _writeLock = new();
    private bool _disposed;

    /// <summary>
    /// Creates a DiagnosticLogger rooted at the given directory.
    /// Default: %LOCALAPPDATA%\DesktopPet\logs\
    /// </summary>
    public DiagnosticLogger(string? logDirectory = null,
        int? maxFileBytes = null,
        int? maxRetainedFiles = null)
    {
        _logDirectory = logDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopPet", "logs");
        _maxFileBytes = maxFileBytes ?? DefaultMaxFileBytes;
        _maxRetainedFiles = maxRetainedFiles ?? DefaultMaxRetainedFiles;
    }

    /// <summary>Current log directory path (for diagnostics/inspection).</summary>
    public string LogDirectory => _logDirectory;

    /// <summary>
    /// Records a diagnostic event. Never throws — if writing fails, the
    /// failure is silently swallowed to avoid a second exception during
    /// termination handling.
    /// </summary>
    /// <param name="source">Event source identifier (e.g. "DispatcherUnhandled",
    /// "AppDomainUnhandled", "TaskUnobserved").</param>
    /// <param name="isTerminating">Whether this event is marked as terminating.</param>
    /// <param name="exception">The exception to record, or null.</param>
    /// <param name="appVersion">Application version/build identifier.</param>
    public void Record(
        string source,
        bool isTerminating,
        Exception? exception,
        string? appVersion = null)
    {
        if (_disposed)
            return;

        try
        {
            var entry = FormatEntry(source, isTerminating, exception, appVersion);
            WriteEntry(entry);
        }
        catch
        {
            // PREP-001: Log failure must NOT throw a second exception,
            // recurse, or deadlock. Swallow silently.
        }
    }

    /// <summary>
    /// Records a plain text diagnostic message (non-exception events).
    /// Never throws.
    /// </summary>
    public void RecordMessage(
        string source,
        string message,
        string? appVersion = null)
    {
        if (_disposed)
            return;

        try
        {
            var sb = new StringBuilder();
            AppendHeader(sb, source, isTerminating: false, appVersion);
            sb.Append("  message: ").Append(Truncate(message, MaxExceptionMessageLen));
            sb.AppendLine();
            WriteEntry(sb.ToString());
        }
        catch
        {
            // Swallow — same constraint as Record.
        }
    }

    /// <summary>
    /// PREP-001 R1 F-03: Returns true if the write lock can be acquired
    /// without waiting. For test verification of bounded lock behavior.
    /// </summary>
    internal bool TryEnterWriteLock(TimeSpan timeout)
    {
        return System.Threading.Monitor.TryEnter(_writeLock, timeout);
    }

    /// <summary>
    /// PREP-001 R1 F-03: Releases the write lock acquired by
    /// TryEnterWriteLock. For test verification only.
    /// </summary>
    internal void ExitWriteLock()
    {
        if (System.Threading.Monitor.IsEntered(_writeLock))
            System.Threading.Monitor.Exit(_writeLock);
    }

    private static string FormatEntry(
        string source,
        bool isTerminating,
        Exception? exception,
        string? appVersion)
    {
        var sb = new StringBuilder();
        AppendHeader(sb, source, isTerminating, appVersion);

        if (exception is not null)
        {
            sb.Append("  type: ").Append(exception.GetType().FullName ?? exception.GetType().Name);
            sb.AppendLine();
            sb.Append("  message: ").Append(Truncate(exception.Message, MaxExceptionMessageLen));
            sb.AppendLine();
            var stack = exception.StackTrace;
            if (!string.IsNullOrEmpty(stack))
            {
                sb.Append("  stack: ").Append(Truncate(stack, MaxStackTraceLen));
                sb.AppendLine();
            }
            // Record inner exception type/message (one level) for context.
            if (exception.InnerException is not null)
            {
                sb.Append("  inner.type: ").Append(
                    exception.InnerException.GetType().FullName
                    ?? exception.InnerException.GetType().Name);
                sb.AppendLine();
                sb.Append("  inner.message: ").Append(
                    Truncate(exception.InnerException.Message, MaxExceptionMessageLen));
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("  exception: <null>");
        }

        return sb.ToString();
    }

    private static void AppendHeader(
        StringBuilder sb, string source, bool isTerminating, string? appVersion)
    {
        // UTC timestamp per ARCHITECTURE §10.
        sb.Append('[').Append(DateTime.UtcNow.ToString("O")).Append(']');
        sb.Append(" source=").Append(source);
        sb.Append(" terminating=").Append(isTerminating);
        if (!string.IsNullOrEmpty(appVersion))
            sb.Append(" version=").Append(appVersion);
        // Thread info for diagnostics (managed thread ID, not keyboard/screen).
        sb.Append(" thread=").Append(Environment.CurrentManagedThreadId);
        sb.AppendLine();
    }

    private void WriteEntry(string entry)
    {
        // PREP-001 R1 F-03: Use bounded lock — TryEnter(0) means no waiting.
        // If another thread holds the lock, this entry is silently discarded
        // rather than blocking indefinitely. This is critical for termination
        // paths where another thread may be stuck in I/O.
        if (!System.Threading.Monitor.TryEnter(_writeLock, 0))
            return; // Bounded: discard this entry instead of waiting.

        try
        {
            if (_disposed)
                return;

            Directory.CreateDirectory(_logDirectory);
            var logFile = CurrentLogFilePath();

            // PREP-001 R1 F-01: Encode the entry to UTF-8 bytes *before* writing,
            // and compute the exact byte count. This prevents a single entry
            // from crossing the hard byte cap.
            var entryBytes = Encoding.UTF8.GetBytes(entry);

            // If a single entry exceeds the file cap, truncate it to fit.
            // This is a last-resort safety: normal entries are far smaller.
            if (entryBytes.Length > _maxFileBytes)
            {
                entryBytes = TruncateUtf8Bytes(entryBytes, _maxFileBytes);
            }

            // PREP-001 R1 F-01: Check size cap including the bytes we are about
            // to write. If current file size + new entry would exceed the cap,
            // roll first. This ensures no file ever exceeds the hard limit
            // (except by at most one entry that was truncated to fit exactly).
            //
            // PREP-001 R2 F-01: If rolling fails (e.g. file is held by another
            // process with read share, preventing rename/delete), we must NOT
            // continue appending to the full file — that would exceed the cap.
            // Instead, verify that the current file has enough remaining capacity
            // for this entry. If not, safely abandon this record.
            try
            {
                if (File.Exists(logFile))
                {
                    var fileInfo = new FileInfo(logFile);
                    if (fileInfo.Length + entryBytes.Length > _maxFileBytes)
                    {
                        var rolled = RollFiles();
                        if (rolled)
                        {
                            // Rolling succeeded — the old file was moved away,
                            // a fresh file will be created by AppendAllText.
                            logFile = CurrentLogFilePath();
                        }
                        else
                        {
                            // PREP-001 R2 F-01: Rolling failed — the current file
                            // could not be renamed/deleted. Check if the current
                            // file still has enough capacity for this entry.
                            // If not, safely abandon this record rather than
                            // exceeding the cap.
                            var currentSize = new FileInfo(logFile).Length;
                            if (currentSize + entryBytes.Length > _maxFileBytes)
                            {
                                return; // Safe abandon: don't exceed cap.
                            }
                            // Current file has room despite roll failure
                            // (e.g. roll was needed but the file shrank due
                            // to external cleanup). Proceed to append.
                        }
                    }
                }
            }
            catch
            {
                // PREP-001 R2 F-01: If we can't check the size or roll, verify
                // remaining capacity defensively. If the check itself fails,
                // we cannot guarantee the cap — safely abandon this record.
                try
                {
                    if (File.Exists(logFile))
                    {
                        var currentSize = new FileInfo(logFile).Length;
                        if (currentSize + entryBytes.Length > _maxFileBytes)
                            return; // Safe abandon: can't verify capacity.
                    }
                }
                catch
                {
                    return; // Safe abandon: can't even check file size.
                }
            }

            // PREP-001 R1 F-01: Write the entry first, then prune.
            // Pruning after the write ensures the newly created file is
            // counted in the total file count.
            File.AppendAllText(logFile, Encoding.UTF8.GetString(entryBytes));

            // PREP-001 R1 F-01: Global cleanup of old-date files after write,
            // ensuring total file count (including the just-written file)
            // does not exceed _maxRetainedFiles.
            PruneOldDateFiles();
        }
        finally
        {
            System.Threading.Monitor.Exit(_writeLock);
        }
    }

    /// <summary>
    /// PREP-001 R1 F-01: Truncates a UTF-8 byte array to at most maxBytes,
    /// ensuring we don't split a multi-byte character in the middle.
    /// Appends a newline so the log remains readable.
    /// </summary>
    private static byte[] TruncateUtf8Bytes(byte[] bytes, int maxBytes)
    {
        if (bytes.Length <= maxBytes)
            return bytes;

        // Find a safe truncation point that doesn't split a UTF-8 sequence.
        var cutAt = maxBytes;
        // A continuation byte starts with 10xxxxxx (0x80..0xBF).
        // Walk back until we're not in the middle of a multi-byte char.
        while (cutAt > 0 && (bytes[cutAt] & 0xC0) == 0x80)
            cutAt--;

        // If even the truncation marker "...[truncated]\n" won't fit,
        // just cut at the boundary.
        var marker = Encoding.UTF8.GetBytes("...[truncated]\n");
        if (cutAt + marker.Length > maxBytes)
        {
            cutAt = Math.Max(0, maxBytes - marker.Length);
            // Re-align to character boundary after adjusting for marker.
            while (cutAt > 0 && (bytes[cutAt] & 0xC0) == 0x80)
                cutAt--;
        }

        var result = new byte[cutAt + marker.Length];
        Array.Copy(bytes, result, cutAt);
        Array.Copy(marker, 0, result, cutAt, marker.Length);
        return result;
    }

    /// <summary>
    /// PREP-001 R1 F-01: Globally prunes log files across all dates so that
    /// total file count (including the current file) does not exceed
    /// _maxRetainedFiles. Old-date files that accumulate over multiple days
    /// are cleaned up here, not just same-date rolled files.
    /// </summary>
    private void PruneOldDateFiles()
    {
        try
        {
            if (!Directory.Exists(_logDirectory))
                return;

            // Get all diagnostic-*.log files (current + rolled, all dates).
            var allFiles = Directory.GetFiles(_logDirectory, "diagnostic-*.log")
                .OrderByDescending(f => f)
                .ToArray();

            if (allFiles.Length <= _maxRetainedFiles)
                return;

            // Delete the oldest files beyond the retention limit.
            // Files are sorted descending (newest first), so we delete
            // from index _maxRetainedFiles onwards.
            for (var i = _maxRetainedFiles; i < allFiles.Length; i++)
            {
                try { File.Delete(allFiles[i]); }
                catch { }
            }
        }
        catch
        {
            // Best-effort cleanup — don't block logging on prune failure.
        }
    }

    private string CurrentLogFilePath()
    {
        var dateStamp = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return Path.Combine(_logDirectory, $"diagnostic-{dateStamp}.log");
    }

    /// <summary>
    /// Rolls log files: keeps at most _maxRetainedFiles - 1 old files,
    /// renames current to .1, .2, etc., then a fresh file will be created
    /// on next write.
    /// PREP-001 R1 F-01: Also calls PruneOldDateFiles to enforce global
    /// file count across all dates.
    /// PREP-001 R2 F-01: Returns true if the current file was successfully
    /// moved away (rolling succeeded); false if the move failed (e.g. file
    /// is held by another process). Callers must check the return value
    /// before appending to avoid exceeding the cap.
    /// </summary>
    private bool RollFiles()
    {
        var baseFile = CurrentLogFilePath();
        var currentMoved = false;

        // Delete the oldest file beyond retention limit.
        var oldest = Path.Combine(_logDirectory,
            $"diagnostic-{DateTime.UtcNow:yyyy-MM-dd}.{_maxRetainedFiles}.log");
        if (File.Exists(oldest))
        {
            try { File.Delete(oldest); }
            catch { }
        }

        // Shift .N-1 → .N, .N-2 → .N-1, ..., .1 → .2
        for (var i = _maxRetainedFiles - 1; i >= 1; i--)
        {
            var from = Path.Combine(_logDirectory,
                $"diagnostic-{DateTime.UtcNow:yyyy-MM-dd}.{i}.log");
            var to = Path.Combine(_logDirectory,
                $"diagnostic-{DateTime.UtcNow:yyyy-MM-dd}.{i + 1}.log");
            if (File.Exists(from))
            {
                try
                {
                    if (File.Exists(to))
                        File.Delete(to);
                    File.Move(from, to);
                }
                catch { }
            }
        }

        // Move current → .1
        if (File.Exists(baseFile))
        {
            var rolled = Path.Combine(_logDirectory,
                $"diagnostic-{DateTime.UtcNow:yyyy-MM-dd}.1.log");
            try
            {
                if (File.Exists(rolled))
                    File.Delete(rolled);
                File.Move(baseFile, rolled);
                currentMoved = true;
            }
            catch { }
        }
        else
        {
            // No current file to move — rolling is trivially "succeeded"
            // since there's nothing blocking a fresh file creation.
            currentMoved = true;
        }

        // PREP-001 R1 F-01: After same-date rolling, also prune old-date
        // files globally to enforce total file count ≤ _maxRetainedFiles.
        PruneOldDateFiles();

        return currentMoved;
    }

    /// <summary>
    /// Returns all log files in the directory, sorted by name.
    /// For test inspection only.
    /// </summary>
    internal IReadOnlyList<string> GetLogFiles()
    {
        try
        {
            if (!Directory.Exists(_logDirectory))
                return Array.Empty<string>();
            return Directory.GetFiles(_logDirectory, "diagnostic-*.log")
                .OrderBy(f => f)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Returns the total size in bytes of all log files.
    /// For test inspection only.
    /// </summary>
    internal long GetTotalLogBytes()
    {
        try
        {
            var files = GetLogFiles();
            long total = 0;
            foreach (var f in files)
            {
                try { total += new FileInfo(f).Length; }
                catch { }
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        // PREP-001 R1 F-03: Use bounded lock — TryEnter(0) means no waiting.
        // If another thread holds the lock (e.g. stuck in I/O during a crash),
        // we discard the dispose attempt rather than blocking indefinitely.
        if (!System.Threading.Monitor.TryEnter(_writeLock, 0))
            return; // Bounded: don't hang on dispose.
        try
        {
            _disposed = true;
        }
        finally
        {
            System.Threading.Monitor.Exit(_writeLock);
        }
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength] + "...[truncated]";
    }
}
