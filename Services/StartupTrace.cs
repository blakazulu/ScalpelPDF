using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Scalpel.Services
{
    /// <summary>
    /// Opt-in startup timing used when benchmarking launch time. Normal launches do no file I/O.
    /// Set <c>SCALPEL_STARTUP_TRACE</c> before starting the process: either to an output file
    /// path, or to <c>1</c> / <c>true</c> to write <c>startup-trace.log</c> into the app's
    /// per-user log folder (<c>%LOCALAPPDATA%\Scalpel\logs</c>).
    /// </summary>
    public static class StartupTrace
    {
        private const string TraceEnvironmentVariable = "SCALPEL_STARTUP_TRACE";
        private static readonly object Gate = new();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly string? OutputPath = ResolveOutputPath();
        private static bool _headerWritten;

        /// <summary>True when the environment variable requested a trace.</summary>
        public static bool Enabled => !string.IsNullOrWhiteSpace(OutputPath);

        /// <summary>Appends one "elapsed-ms &lt;tab&gt; stage" line. No-op unless enabled; never throws.</summary>
        public static void Mark(string stage)
        {
            if (!Enabled) return;

            try
            {
                lock (Gate)
                {
                    var directory = Path.GetDirectoryName(OutputPath!);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                    var sb = new StringBuilder();
                    if (!_headerWritten)
                    {
                        _headerWritten = true;
                        var process = Process.GetCurrentProcess();
                        sb.Append("# Scalpel startup trace | pid=")
                          .Append(process.Id)
                          .Append(" | processStartUtc=")
                          .Append(process.StartTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                          .Append(" | traceStartUtc=")
                          .Append(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))
                          .AppendLine();
                    }

                    sb.Append(Clock.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture))
                      .Append('\t')
                      .AppendLine(stage);
                    File.AppendAllText(OutputPath!, sb.ToString(), new UTF8Encoding(false));
                }
            }
            catch
            {
                // Diagnostics must never make startup fail.
            }
        }

        private static string? ResolveOutputPath()
        {
            try
            {
                string? value = Environment.GetEnvironmentVariable(TraceEnvironmentVariable);
                if (string.IsNullOrWhiteSpace(value)) return null;
                value = value!.Trim();
                if (value.Equals("1", StringComparison.Ordinal) ||
                    value.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Scalpel", "logs", "startup-trace.log");
                }
                return value;
            }
            catch
            {
                return null;
            }
        }
    }
}
