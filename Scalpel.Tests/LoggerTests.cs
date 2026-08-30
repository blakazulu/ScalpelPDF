using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    // Logger is a process-wide static; every test class that calls Logger.Init must share this
    // collection so xUnit runs them sequentially instead of letting their Init calls collide.
    [Collection("Logger")]
    public class LoggerTests : IDisposable
    {
        private readonly string _dir;

        public LoggerTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "scalpel_logtest_" + Guid.NewGuid().ToString("N"));
        }

        public void Dispose()
        {
            try { Logger.Shutdown(); } catch { }
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
        }

        private string ReadLogLines() // returns the single session file's content
        {
            var file = Directory.GetFiles(_dir, "scalpel-*.jsonl").Single();
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            return sr.ReadToEnd();
        }

        [Fact]
        public void Info_writes_one_valid_json_line_with_expected_fields()
        {
            Logger.Init(_dir);
            Logger.Info("File", "open.success", "Opened invoice.pdf", new { pages = 12 });
            Logger.Flush();

            var lines = ReadLogLines().Split('\n').Where(l => l.Trim().Length > 0).ToArray();
            Assert.Single(lines);

            using var doc = JsonDocument.Parse(lines[0]);
            var root = doc.RootElement;
            Assert.Equal("INFO", root.GetProperty("level").GetString());
            Assert.Equal("File", root.GetProperty("cat").GetString());
            Assert.Equal("open.success", root.GetProperty("event").GetString());
            Assert.Equal("Opened invoice.pdf", root.GetProperty("msg").GetString());
            Assert.Equal(12, root.GetProperty("data").GetProperty("pages").GetInt32());
            Assert.False(string.IsNullOrEmpty(root.GetProperty("ts").GetString()));
        }

        [Fact]
        public void MinLevel_filters_out_lower_levels()
        {
            Logger.Init(_dir, minLevel: Logger.Level.Info);
            Logger.Debug("UI", "click", "should be dropped");
            Logger.Info("UI", "click", "should be kept");
            Logger.Flush();

            var lines = ReadLogLines().Split('\n').Where(l => l.Trim().Length > 0).ToArray();
            Assert.Single(lines);
            Assert.Contains("should be kept", lines[0]);
        }

        [Fact]
        public void Disabled_creates_no_file_and_calls_are_noops()
        {
            Logger.Init(_dir, enabled: false);
            Logger.Info("UI", "click", "nothing");
            Logger.Flush();

            Assert.Empty(Directory.GetFiles(_dir, "scalpel-*.jsonl"));
        }

        [Fact]
        public void Init_deletes_logs_older_than_ninety_days_keeps_recent()
        {
            Directory.CreateDirectory(_dir);
            var old = Path.Combine(_dir, "scalpel-20000101-000000.jsonl");
            var monthOld = Path.Combine(_dir, "scalpel-20000201-000000.jsonl");
            var recent = Path.Combine(_dir, "scalpel-20990101-000000.jsonl");
            File.WriteAllText(old, "{}\n");
            File.WriteAllText(monthOld, "{}\n");
            File.WriteAllText(recent, "{}\n");
            File.SetLastWriteTime(old, DateTime.Now.AddDays(-91));
            File.SetLastWriteTime(monthOld, DateTime.Now.AddDays(-30));
            File.SetLastWriteTime(recent, DateTime.Now.AddDays(-1));

            Logger.Init(_dir);
            Logger.Shutdown();

            Assert.False(File.Exists(old));
            Assert.True(File.Exists(monthOld));   // a month-old bug report must still be readable
            Assert.True(File.Exists(recent));
        }

        [Fact]
        public void Init_caps_log_count_keeping_the_newest_files()
        {
            Directory.CreateDirectory(_dir);
            // 205 fresh (within retention) files; the cap must trim the OLDEST down to 200,
            // plus the new session file opened by Init itself.
            for (int i = 0; i < 205; i++)
            {
                var f = Path.Combine(_dir, $"scalpel-2099{i:D4}-000000.jsonl");
                File.WriteAllText(f, "{}\n");
                File.SetLastWriteTime(f, DateTime.Now.AddMinutes(-(205 - i)));
            }

            Logger.Init(_dir);
            Logger.Shutdown();

            var remaining = Directory.GetFiles(_dir, "scalpel-*.jsonl");
            Assert.Equal(201, remaining.Length);
            Assert.False(File.Exists(Path.Combine(_dir, "scalpel-20990000-000000.jsonl"))); // oldest gone
            Assert.False(File.Exists(Path.Combine(_dir, "scalpel-20990004-000000.jsonl")));
            Assert.True(File.Exists(Path.Combine(_dir, "scalpel-20990005-000000.jsonl")));  // 200th-newest kept
            Assert.True(File.Exists(Path.Combine(_dir, "scalpel-20990204-000000.jsonl")));  // newest kept
        }

        [Fact]
        public void Error_serializes_exception_into_error_object_on_json_line()
        {
            Logger.Init(_dir);

            Exception captured;
            try { throw new InvalidOperationException("boom"); } catch (Exception ex) { captured = ex; }
            Logger.Error("Error", "op.fail", "operation failed", captured);
            Logger.Flush();

            var lines = ReadLogLines().Split('\n').Where(l => l.Trim().Length > 0).ToArray();
            Assert.Single(lines);

            using var doc = JsonDocument.Parse(lines[0]);
            var root = doc.RootElement;
            Assert.Equal("ERROR", root.GetProperty("level").GetString());
            Assert.True(root.TryGetProperty("error", out var error), "error property must exist");
            Assert.Equal("InvalidOperationException", error.GetProperty("type").GetString());
            Assert.Equal("boom", error.GetProperty("message").GetString());
        }

        [Fact]
        public void ClearLogs_deletes_prior_files_but_keeps_current_session()
        {
            Directory.CreateDirectory(_dir);
            var prior = Path.Combine(_dir, "scalpel-20990101-000000.jsonl");
            File.WriteAllText(prior, "{}\n");
            File.SetLastWriteTime(prior, DateTime.Now.AddDays(-1));

            Logger.Init(_dir);
            Logger.Info("App", "app.start", "x");
            Logger.ClearLogs();

            Assert.False(File.Exists(prior));
            Assert.Single(Directory.GetFiles(_dir, "scalpel-*.jsonl")); // only the live session file remains
        }
    }
}
