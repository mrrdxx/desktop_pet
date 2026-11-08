using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DesktopPet.App.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DesktopPet.App.Tests;

/// <summary>
/// PREP-001 component tests for DiagnosticLogger.
/// Verifies: log content (time/version/source/type/stack), capacity limits,
/// file rolling, record-failure does not throw second exception, and
/// concurrent record safety.
/// Per TESTING M-07 and ARCHITECTURE §10.
/// </summary>
[TestClass]
public class DiagnosticLoggerTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(),
            "DesktopPetTests_DiagLog_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    [TestMethod]
    public void Record_WritesEntryWithTimestampSourceVersionAndException()
    {
        using var logger = new DiagnosticLogger(_tempDir);

        // Throw and catch to get a real stack trace.
        InvalidOperationException? caught = null;
        try { throw new InvalidOperationException("Test boom", new ArgumentException("Inner cause")); }
        catch (InvalidOperationException ex) { caught = ex; }

        logger.Record("TestSource", isTerminating: false, caught!, appVersion: "1.2.3");

        var files = logger.GetLogFiles();
        Assert.AreEqual(1, files.Count, "Should create exactly one log file.");

        var content = File.ReadAllText(files[0]);
        Assert.IsTrue(content.Contains("source=TestSource"), "Log must contain source identifier. Content:\n" + content);
        Assert.IsTrue(content.Contains("version=1.2.3"), "Log must contain app version. Content:\n" + content);
        Assert.IsTrue(content.Contains("terminating=False"), "Log must contain terminating flag. Content:\n" + content);
        Assert.IsTrue(content.Contains("InvalidOperationException"), "Log must contain exception type. Content:\n" + content);
        Assert.IsTrue(content.Contains("Test boom"), "Log must contain exception message. Content:\n" + content);
        Assert.IsTrue(content.Contains("stack:"), "Log must contain stack trace. Content:\n" + content);
        Assert.IsTrue(content.Contains("ArgumentException"), "Log must contain inner exception type. Content:\n" + content);
        Assert.IsTrue(content.Contains("Inner cause"), "Log must contain inner exception message. Content:\n" + content);
        // UTC timestamp in ISO 8601 format.
        Assert.IsTrue(content.Contains("thread="), "Log must contain thread ID. Content:\n" + content);
        // Verify timestamp is parseable as UTC.
        var firstLine = content.Split('\n')[0];
        var tsStart = firstLine.IndexOf('[') + 1;
        var tsEnd = firstLine.IndexOf(']');
        Assert.IsTrue(tsStart > 0 && tsEnd > tsStart, "Log must have bracketed timestamp.");
        var tsStr = firstLine.Substring(tsStart, tsEnd - tsStart);
        Assert.IsTrue(DateTime.TryParse(tsStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out _),
            "Timestamp must be parseable ISO 8601.");
    }

    [TestMethod]
    public void Record_NullException_WritesNullMarker()
    {
        using var logger = new DiagnosticLogger(_tempDir);

        logger.Record("NullTest", isTerminating: true, exception: null, appVersion: "0.0.1");

        var content = File.ReadAllText(logger.GetLogFiles()[0]);
        Assert.IsTrue(content.Contains("exception: <null>"), "Must record null exception marker.");
        Assert.IsTrue(content.Contains("terminating=True"), "Must record terminating flag.");
    }

    [TestMethod]
    public void RecordMessage_WritesNonExceptionEntry()
    {
        using var logger = new DiagnosticLogger(_tempDir);

        logger.RecordMessage("InfoSource", "Application started normally", appVersion: "1.0.0");

        var content = File.ReadAllText(logger.GetLogFiles()[0]);
        Assert.IsTrue(content.Contains("source=InfoSource"), "Must contain source.");
        Assert.IsTrue(content.Contains("Application started normally"), "Must contain message.");
        Assert.IsTrue(content.Contains("version=1.0.0"), "Must contain version.");
    }

    [TestMethod]
    public void Record_LogDirectoryNotWritable_DoesNotThrow()
    {
        // Use a path that cannot be created — simulate unwritable directory.
        // On Windows, a path with invalid characters will fail Directory.CreateDirectory.
        var badPath = Path.Combine(_tempDir, "bad<>|?dir");
        using var logger = new DiagnosticLogger(badPath);

        // Must NOT throw — log failure must not cause a second exception.
        try
        {
            logger.Record("BadDirTest", isTerminating: false,
                new Exception("Should not crash logger"), appVersion: "1.0");
        }
        catch (Exception ex)
        {
            Assert.Fail($"Record must not throw when directory is unwritable: {ex.Message}");
        }
    }

    [TestMethod]
    public void Record_AfterDispose_DoesNotThrow()
    {
        var logger = new DiagnosticLogger(_tempDir);
        logger.Dispose();

        // Recording after dispose must be a no-op, not an exception.
        try
        {
            logger.Record("PostDispose", isTerminating: false,
                new Exception("After dispose"), appVersion: "1.0");
        }
        catch (Exception ex)
        {
            Assert.Fail($"Record after Dispose must not throw: {ex.Message}");
        }
    }

    [TestMethod]
    public void Record_FileSizeCap_TriggersRollOver()
    {
        // PREP-001 R1 F-01: Strict byte cap — no file should exceed the cap.
        // Use a very small file cap to trigger rolling quickly.
        using var logger = new DiagnosticLogger(_tempDir, maxFileBytes: 512, maxRetainedFiles: 3);

        // Write enough entries to exceed 512 bytes.
        var bigEx = new InvalidOperationException(new string('X', 200));
        for (var i = 0; i < 10; i++)
        {
            logger.Record("RollTest", isTerminating: false, bigEx, appVersion: "1.0");
        }

        var files = logger.GetLogFiles();
        Assert.IsTrue(files.Count >= 2, "Should have rolled over to at least 2 files.");

        // R1 F-01: Each file must not exceed the cap (except possibly the
        // current file which may have one entry that was truncated to fit).
        foreach (var f in files)
        {
            var len = new FileInfo(f).Length;
            Assert.IsTrue(len <= 512,
                $"File {Path.GetFileName(f)} size ({len}) must not exceed cap (512 bytes).");
        }

        // R1 F-01: Total bytes bounded by maxRetainedFiles * cap, not 10x unbounded.
        var totalBytes = logger.GetTotalLogBytes();
        Assert.IsTrue(totalBytes <= 512 * 3,
            $"Total log bytes ({totalBytes}) should be bounded by maxRetainedFiles * cap ({512 * 3}).");
    }

    [TestMethod]
    public void Record_RetainedFileLimit_Enforced()
    {
        // PREP-001 R1 F-01: Strict file count — total files including current
        // must not exceed maxRetainedFiles (was previously relaxed to N+1).
        using var logger = new DiagnosticLogger(_tempDir, maxFileBytes: 256, maxRetainedFiles: 3);

        var bigEx = new InvalidOperationException(new string('Y', 200));
        // Write many entries to trigger multiple roll-overs.
        for (var i = 0; i < 30; i++)
        {
            logger.Record("RetentionTest", isTerminating: false, bigEx, appVersion: "1.0");
        }

        var files = logger.GetLogFiles();
        // R1 F-01: Should not exceed maxRetainedFiles (current + rolled files
        // all counted together, including cross-date pruning).
        Assert.IsTrue(files.Count <= 3,
            $"File count ({files.Count}) must not exceed maxRetainedFiles (3), including current file.");
    }

    [TestMethod]
    public void Record_LongException_Truncated()
    {
        using var logger = new DiagnosticLogger(_tempDir);

        // Create an exception with a very long message.
        var longMessage = new string('A', 5000);
        var ex = new InvalidOperationException(longMessage);

        logger.Record("TruncTest", isTerminating: false, ex, appVersion: "1.0");

        var content = File.ReadAllText(logger.GetLogFiles()[0]);
        Assert.IsTrue(content.Contains("[truncated]"), "Long exception message must be truncated.");
        // The message field should not contain the full 5000-char string.
        Assert.IsFalse(content.Contains(new string('A', 3000)),
            "Full untruncated long message should not appear in log.");
    }

    [TestMethod]
    public void Record_ConcurrentWrites_AllSucceedSafely()
    {
        // PREP-001 R1 F-03: With bounded lock (TryEnter(0)), concurrent writes
        // that fail to acquire the lock will have their entries discarded
        // (not blocked). This test verifies that concurrent recording does NOT
        // throw or deadlock — some entries may be discarded under contention,
        // which is the intended bounded behavior.
        using var logger = new DiagnosticLogger(_tempDir);

        // Simulate concurrent recording from multiple threads.
        var exceptions = new Exception[]
        {
            new InvalidOperationException("Concurrent 1"),
            new ArgumentException("Concurrent 2"),
            new NullReferenceException("Concurrent 3"),
            new FormatException("Concurrent 4"),
            new IOException("Concurrent 5")
        };

        System.Threading.Tasks.Parallel.For(0, 50, i =>
        {
            var ex = exceptions[i % exceptions.Length];
            try
            {
                logger.Record("ConcurrentTest", isTerminating: false, ex, appVersion: "1.0");
            }
            catch (Exception)
            {
                // R1 F-03: Record must never throw, even under contention.
                Assert.Fail("Record must not throw during concurrent writes.");
            }
        });

        // R1 F-03: At least some entries should have been written (the ones
        // that acquired the lock). The log file should exist and contain
        // entries — we don't assert ALL entries are present because the
        // bounded lock strategy intentionally discards entries that can't
        // acquire the lock.
        var files = logger.GetLogFiles();
        Assert.IsTrue(files.Count >= 1, "At least one log file should be created.");
        var content = File.ReadAllText(files[0]);
        Assert.IsTrue(content.Contains("source=ConcurrentTest"),
            "Some concurrent entries should be recorded. Content:\n" + content);
    }

    [TestMethod]
    public void Record_RepeatedLoggerFailure_DoesNotRecursOrDeadlock()
    {
        // Point to a path where the parent directory is a file (not a directory),
        // so CreateDirectory and file writes will fail.
        var blockerFile = Path.Combine(_tempDir, "blocker");
        File.WriteAllText(blockerFile, "I am a file, not a directory");
        var badLogDir = Path.Combine(blockerFile, "logs");

        using var logger = new DiagnosticLogger(badLogDir);

        // Multiple rapid calls should all be swallowed, not throw or deadlock.
        for (var i = 0; i < 20; i++)
        {
            try
            {
                logger.Record("FailTest", isTerminating: i % 2 == 0,
                    new Exception($"Failure {i}"), appVersion: "1.0");
            }
            catch (Exception ex)
            {
                Assert.Fail($"Repeated logger failure must not throw (call {i}): {ex.Message}");
            }
        }
    }

    /// <summary>
    /// PREP-001 R1 F-01: Cross-date files must be pruned globally.
    /// Pre-create old-date log files, then write a new entry — total file
    /// count must not exceed maxRetainedFiles.
    /// </summary>
    [TestMethod]
    public void Record_CrossDateFiles_PrunedToRetainedLimit()
    {
        // Use maxRetainedFiles=3.
        using var logger = new DiagnosticLogger(_tempDir, maxFileBytes: 1024, maxRetainedFiles: 3);

        // Pre-create 5 old-date files (different dates in the past).
        var oldDates = new[]
        {
            "2026-01-01", "2026-01-02", "2026-02-01", "2026-03-01", "2026-04-01"
        };
        foreach (var date in oldDates)
        {
            var path = Path.Combine(_tempDir, $"diagnostic-{date}.log");
            File.WriteAllText(path, $"old entry from {date}\n");
        }

        // Write a new entry — this triggers PruneOldDateFiles.
        logger.Record("CrossDateTest", isTerminating: false,
            new Exception("trigger prune"), appVersion: "1.0");

        var files = logger.GetLogFiles();
        // R1 F-01: Total files including old-date files must not exceed maxRetainedFiles.
        Assert.IsTrue(files.Count <= 3,
            $"Cross-date file count ({files.Count}) must not exceed maxRetainedFiles (3). " +
            $"Files: {string.Join(", ", files.Select(Path.GetFileName))}");
    }

    /// <summary>
    /// PREP-001 R1 F-01: A single entry larger than the file cap must be
    /// truncated to fit, not written unbounded.
    /// </summary>
    [TestMethod]
    public void Record_SingleEntryLargerThanCap_TruncatedToFit()
    {
        // Use a very small cap.
        var cap = 256;
        using var logger = new DiagnosticLogger(_tempDir, maxFileBytes: cap, maxRetainedFiles: 3);

        // Create an exception with a very long message that produces an
        // entry much larger than the cap.
        var longMessage = new string('Z', 2000);
        logger.Record("BigEntryTest", isTerminating: false,
            new InvalidOperationException(longMessage), appVersion: "1.0");

        var files = logger.GetLogFiles();
        Assert.AreEqual(1, files.Count, "Should have exactly one file.");

        // R1 F-01: The file must not exceed the cap.
        var fileSize = new FileInfo(files[0]).Length;
        Assert.IsTrue(fileSize <= cap,
            $"File size ({fileSize}) must not exceed cap ({cap}) even for a single large entry.");

        // R1 F-01: The entry should be truncated (contain truncated marker).
        var content = File.ReadAllText(files[0]);
        Assert.IsTrue(content.Contains("[truncated]"),
            "Large entry should be truncated with marker. Content:\n" + content);
    }

    /// <summary>
    /// PREP-001 R1 F-01: When the current file is exactly at the cap,
    /// the next write must trigger a roll (not append beyond the cap).
    /// </summary>
    [TestMethod]
    public void Record_FileExactlyAtCap_NextWriteTriggersRoll()
    {
        var cap = 512;
        using var logger = new DiagnosticLogger(_tempDir, maxFileBytes: cap, maxRetainedFiles: 3);

        // Write entries until the file is at or near the cap.
        var smallEx = new InvalidOperationException("padding");
        for (var i = 0; i < 20; i++)
        {
            logger.Record("CapBoundary", isTerminating: false, smallEx, appVersion: "1.0");
        }

        // The first file should be at or near the cap.
        var files = logger.GetLogFiles();
        var firstFile = files[0];
        var sizeAfterFirst = new FileInfo(firstFile).Length;

        // Write one more entry — this should trigger a roll since
        // current file + new entry > cap.
        logger.Record("CapBoundary", isTerminating: false, smallEx, appVersion: "1.0");

        // R1 F-01: The original file must not have grown beyond the cap.
        var originalSizeAfter = new FileInfo(firstFile).Length;
        Assert.IsTrue(originalSizeAfter <= cap,
            $"Original file size ({originalSizeAfter}) must not exceed cap ({cap}) after additional write.");

        // R1 F-01: Each file must be within the cap.
        foreach (var f in logger.GetLogFiles())
        {
            var len = new FileInfo(f).Length;
            Assert.IsTrue(len <= cap,
                $"File {Path.GetFileName(f)} size ({len}) must not exceed cap ({cap}).");
        }
    }

    /// <summary>
    /// PREP-001 R1 F-01: Precise byte verification — the total log bytes
    /// must not exceed maxRetainedFiles * maxFileBytes under sustained writes.
    /// </summary>
    [TestMethod]
    public void Record_SustainedWrites_TotalBytesBoundedPrecisely()
    {
        var cap = 300;
        var maxFiles = 3;
        using var logger = new DiagnosticLogger(_tempDir, maxFileBytes: cap, maxRetainedFiles: maxFiles);

        // Write 50 entries — should trigger multiple rolls.
        var ex = new InvalidOperationException(new string('Q', 100));
        for (var i = 0; i < 50; i++)
        {
            logger.Record("PreciseByteTest", isTerminating: false, ex, appVersion: "1.0");
        }

        var files = logger.GetLogFiles();
        var totalBytes = logger.GetTotalLogBytes();

        // R1 F-01: File count must not exceed maxRetainedFiles.
        Assert.IsTrue(files.Count <= maxFiles,
            $"File count ({files.Count}) must not exceed maxRetainedFiles ({maxFiles}).");

        // R1 F-01: Total bytes must not exceed maxRetainedFiles * cap.
        Assert.IsTrue(totalBytes <= (long)cap * maxFiles,
            $"Total bytes ({totalBytes}) must not exceed maxRetainedFiles * cap ({cap * maxFiles}).");

        // R1 F-01: Each file must not exceed the cap.
        foreach (var f in files)
        {
            Assert.IsTrue(new FileInfo(f).Length <= cap,
                $"File {Path.GetFileName(f)} must not exceed cap ({cap}).");
        }
    }

    /// <summary>
    /// PREP-001 R1 F-03: When the write lock is held by another thread,
    /// Record must return within a bounded time (not block indefinitely).
    /// Uses TryEnterWriteLock to simulate a held lock.
    /// </summary>
    [TestMethod]
    public void Record_LockHeldByAnotherThread_ReturnsBounded()
    {
        using var logger = new DiagnosticLogger(_tempDir);

        // Acquire the write lock on the current thread to simulate contention.
        Assert.IsTrue(logger.TryEnterWriteLock(TimeSpan.FromSeconds(1)),
            "Test setup: should be able to acquire write lock.");

        try
        {
            // Now Record from a different thread — it should NOT block
            // because TryEnter(0) will fail and the entry is discarded.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ex = new InvalidOperationException("should be discarded");

            // Run Record on a background thread to verify it returns quickly.
            var task = Task.Run(() =>
            {
                logger.Record("LockContention", isTerminating: false, ex, appVersion: "1.0");
            });

            // The task should complete quickly (bounded by TryEnter(0) = no wait).
            Assert.IsTrue(task.Wait(2000),
                "Record must return within bounded time when lock is held by another thread.");
            sw.Stop();

            // The entry should have been discarded (lock was held).
            // Verify no log file was created for this entry.
            var files = logger.GetLogFiles();
            // The file may or may not exist (directory creation happens inside lock),
            // but the key assertion is that Record returned without blocking.
            Assert.IsTrue(sw.ElapsedMilliseconds < 1000,
                $"Record took {sw.ElapsedMilliseconds}ms — should be near-instant when lock is unavailable.");
        }
        finally
        {
            logger.ExitWriteLock();
        }
    }

    /// <summary>
    /// PREP-001 R2 F-01: When the current log file is held open by another
    /// process with FileShare.ReadWrite (allowing append but preventing
    /// rename/delete), RollFiles will fail. WriteEntry must NOT continue
    /// appending to the full file — it must safely abandon this record.
    /// 
    /// Reproduces the exact scenario from R2: cap=256, retention=3, write
    /// a 256-byte entry, then hold the file with FileAccess.Read /
    /// FileShare.ReadWrite (allows append, blocks delete/rename), then
    /// Record again. The file must NOT grow beyond the cap.
    /// </summary>
    [TestMethod]
    public void Record_RollFails_FileHeld_DoesNotExceedCap()
    {
        var cap = 256;
        using var logger = new DiagnosticLogger(_tempDir, maxFileBytes: cap, maxRetainedFiles: 3);

        // Write one entry that fills the file to (or near) the cap.
        // Use a message long enough to fill 256 bytes after formatting.
        var fillEx = new InvalidOperationException(new string('F', 200));
        logger.Record("FillTest", isTerminating: false, fillEx, appVersion: "1.0");

        var files = logger.GetLogFiles();
        Assert.AreEqual(1, files.Count, "Should have exactly one file after first write.");
        var logFile = files[0];
        var sizeAfterFirst = new FileInfo(logFile).Length;
        Assert.IsTrue(sizeAfterFirst <= cap,
            $"File size after first write ({sizeAfterFirst}) must not exceed cap ({cap}).");

        // Hold the file open with FileAccess.Read + FileShare.ReadWrite.
        // This allows other processes to append (write) but prevents
        // rename/delete (which RollFiles needs). This precisely reproduces
        // the R2 scenario: "可追加但不可改名/删除".
        using var heldStream = new FileStream(logFile, FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite);

        // Now write another entry — the file is at/near cap, so WriteEntry
        // will try to roll. Rolling fails because the file is held.
        // WriteEntry must safely abandon this record rather than appending
        // beyond the cap.
        var secondEx = new InvalidOperationException(new string('S', 200));
        logger.Record("RollFailTest", isTerminating: false, secondEx, appVersion: "1.0");

        // R2 F-01: The file must NOT have grown beyond the cap.
        var sizeAfterSecond = new FileInfo(logFile).Length;
        Assert.IsTrue(sizeAfterSecond <= cap,
            $"File size after roll-failed write ({sizeAfterSecond}) must not exceed cap ({cap}). " +
            $"RollFiles failed (file held), so WriteEntry must have safely abandoned the record.");

        // R2 F-01: The second entry's content must NOT be in the file
        // (it was abandoned, not appended).
        var content = File.ReadAllText(logFile);
        Assert.IsFalse(content.Contains("RollFailTest"),
            "Second entry must not appear in log when rolling failed and cap would be exceeded. " +
            "Content:\n" + content);
    }

    /// <summary>
    /// PREP-001 R2 F-01: UTF-8 boundary — a multi-byte character must not
    /// be split when truncating. Verify that a message containing multi-byte
    /// UTF-8 characters (e.g. CJK) is truncated at a character boundary.
    /// </summary>
    [TestMethod]
    public void Record_MultibyteUtf8_TruncatedAtCharBoundary()
    {
        // Use a small cap that forces truncation.
        var cap = 64;
        using var logger = new DiagnosticLogger(_tempDir, maxFileBytes: cap, maxRetainedFiles: 3);

        // CJK characters are 3 bytes each in UTF-8. Create a message
        // that will produce an entry well over the cap.
        var cjkMessage = new string('猫', 100); // 100 × 3 = 300 bytes
        logger.Record("Utf8Test", isTerminating: false,
            new InvalidOperationException(cjkMessage), appVersion: "1.0");

        var files = logger.GetLogFiles();
        Assert.AreEqual(1, files.Count);
        var fileSize = new FileInfo(files[0]).Length;
        Assert.IsTrue(fileSize <= cap,
            $"File size ({fileSize}) must not exceed cap ({cap}) for multi-byte UTF-8 entry.");

        // Read the file as UTF-8 — it must not contain replacement characters
        // (U+FFFD) which would indicate a split multi-byte sequence.
        var content = File.ReadAllText(files[0]);
        Assert.IsFalse(content.Contains('\uFFFD'),
            "Truncated UTF-8 must not produce replacement characters (split multi-byte sequence). " +
            "Content:\n" + content);
        Assert.IsTrue(content.Contains("[truncated]"),
            "Multi-byte entry must be truncated with marker.");
    }

    /// <summary>
    /// PREP-001 R2 F-01: When RollFiles fails but the current file still
    /// has remaining capacity (entry fits), the write should proceed.
    /// This verifies we don't over-aggressively abandon writes that
    /// would not exceed the cap.
    /// </summary>
    [TestMethod]
    public void Record_RollFailsButRoomAvailable_StillWrites()
    {
        var cap = 512;
        using var logger = new DiagnosticLogger(_tempDir, maxFileBytes: cap, maxRetainedFiles: 3);

        // Write a small entry (well under cap).
        logger.Record("SmallEntry", isTerminating: false,
            new InvalidOperationException("small"), appVersion: "1.0");

        var files = logger.GetLogFiles();
        var logFile = files[0];
        var sizeAfterFirst = new FileInfo(logFile).Length;

        // Hold the file to prevent rolling (even though we don't need to roll
        // for this small second entry — it fits within the cap).
        using var heldStream = new FileStream(logFile, FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite);

        // Write another small entry — it fits, so even though rolling would
        // fail, the write should proceed.
        logger.Record("SecondSmall", isTerminating: false,
            new InvalidOperationException("also small"), appVersion: "1.0");

        var content = File.ReadAllText(logFile);
        Assert.IsTrue(content.Contains("SecondSmall"),
            "Second small entry should be written when it fits within the cap, " +
            "even if rolling would fail. Content:\n" + content);

        // File still within cap.
        var sizeAfterSecond = new FileInfo(logFile).Length;
        Assert.IsTrue(sizeAfterSecond <= cap,
            $"File size ({sizeAfterSecond}) must not exceed cap ({cap}).");
    }
}
