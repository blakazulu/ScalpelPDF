using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Scalpel.Services
{
    /// <summary>
    /// Wire format for forwarding a launch (command-line args) from a second Scalpel process to
    /// the one already running. Pure and WPF-free so it can be unit-tested.
    /// Message = magic header line, then one arg per line, CRLF-separated.
    /// </summary>
    public static class SingleInstanceProtocol
    {
        public const string Magic = "SCALPEL-LAUNCH-1";

        public static string Encode(IEnumerable<string> args)
        {
            var sb = new StringBuilder(Magic).Append("\r\n");
            foreach (var a in args) sb.Append(a.Replace("\r", "").Replace("\n", "")).Append("\r\n");
            return sb.ToString();
        }

        public static string[] Decode(string wire)
        {
            var lines = wire.Split(["\r\n"], StringSplitOptions.None);
            if (lines.Length == 0 || lines[0] != Magic) throw new FormatException("Not a Scalpel launch message.");
            var args = new List<string>();
            for (int i = 1; i < lines.Length; i++)
                if (lines[i].Length > 0) args.Add(lines[i]);
            return [.. args];
        }

        /// <summary>
        /// Every argument that is an existing file, in order and without duplicates, plus whether
        /// <c>/edit</c> appears anywhere. Each file opens as its own tab.
        /// </summary>
        public static (IReadOnlyList<string> files, bool edit) PickLaunchTargets(
            IEnumerable<string> args, Func<string, bool> fileExists)
        {
            var files = new List<string>();
            bool edit = false;
            foreach (var a in args)
            {
                if (string.Equals(a, "/edit", StringComparison.OrdinalIgnoreCase)) { edit = true; continue; }
                bool exists = false;
                try { exists = fileExists(a); } catch { }
                if (exists && !files.Exists(f => DocumentPath.Same(f, a))) files.Add(a);
            }
            return (files, edit);
        }

        /// <summary>
        /// Same rule the main window applies to its own command line: the first argument that
        /// is an existing file is the document to open; <c>/edit</c> anywhere requests Edit mode.
        /// </summary>
        public static (string? file, bool edit) PickLaunchTarget(IEnumerable<string> args, Func<string, bool> fileExists)
        {
            var (files, edit) = PickLaunchTargets(args, fileExists);
            return (files.Count > 0 ? files[0] : null, edit);
        }
    }

    /// <summary>
    /// Single-instance coordination: a per-user-session mutex decides who is primary, and a
    /// named pipe carries later launches (file-association double-clicks, the Store tile) to
    /// the primary so they open as tabs in the existing window instead of a new process.
    ///
    /// Set <c>SCALPEL_MULTI_INSTANCE=1</c> in the environment to opt out entirely (the E2E
    /// harness runs several instances in parallel on purpose).
    /// </summary>
    public sealed class SingleInstanceServer : IDisposable
    {
        public const string DisableEnvVar = "SCALPEL_MULTI_INSTANCE";

        private static readonly string Suffix =
            $"{Environment.UserDomainName}.{Environment.UserName}.s{Process.GetCurrentProcess().SessionId}".ToLowerInvariant();
        private static string MutexName => $@"Local\ScalpelPDF.SingleInstance.{Suffix}";
        private static string PipeName  => $"ScalpelPDF.SingleInstance.{Suffix}";

        public static bool IsDisabled =>
            string.Equals(Environment.GetEnvironmentVariable(DisableEnvVar), "1", StringComparison.Ordinal);

        private readonly Mutex _mutex;
        private volatile bool _disposed;
        private Thread? _thread;

        private SingleInstanceServer(Mutex m) => _mutex = m;

        /// <summary>Become the primary instance, or null if another instance already is.</summary>
        public static SingleInstanceServer? TryAcquire()
        {
            try
            {
                var m = new Mutex(initiallyOwned: true, MutexName, out bool created);
                if (created) return new SingleInstanceServer(m);
                m.Dispose();
                return null;
            }
            catch { return null; }
        }

        /// <summary>Start accepting forwarded launches; <paramref name="onLaunch"/> runs on a pool thread.</summary>
        public void Start(Action<string[]> onLaunch)
        {
            _thread = new Thread(() => ServeLoop(onLaunch)) { IsBackground = true, Name = "Scalpel single-instance pipe" };
            _thread.Start();
        }

        private void ServeLoop(Action<string[]> onLaunch)
        {
            int myPid = Process.GetCurrentProcess().Id;
            while (!_disposed)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.None);
                    pipe.WaitForConnection();
                    if (_disposed) return;
                    // Not `using`: the client hangs up right after sending, and a StreamWriter's
                    // Dispose would try to flush into the closed pipe ("Pipe is broken").
                    // AutoFlush already pushed the handshake line; nothing else needs flushing.
                    var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
                    var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                    // Handshake: tell the client our PID so it can grant us foreground rights.
                    writer.WriteLine(myPid.ToString());
                    var body = reader.ReadToEnd();
                    try { pipe.Disconnect(); } catch { }
                    string[] args;
                    try { args = SingleInstanceProtocol.Decode(body); }
                    catch (Exception ex) { Logger.Warn("App", "instance.forward.bad", ex.Message); continue; }
                    try { onLaunch(args); }
                    catch (Exception ex) { Logger.Error("App", "instance.forward.fail", "Forwarded launch handler threw", ex); }
                }
                catch (Exception ex)
                {
                    if (_disposed) return;
                    Logger.Warn("App", "instance.pipe.fail", ex.Message);
                    Thread.Sleep(250); // don't spin if the pipe cannot be created
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Unblock WaitForConnection with a throwaway client so the loop observes _disposed.
            try
            {
                using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                c.Connect(200);
            }
            catch { }
            try { _mutex.ReleaseMutex(); } catch { }
            try { _mutex.Dispose(); } catch { }
        }

        // ── client side ─────────────────────────────────────────────────

        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int dwProcessId);

        /// <summary>
        /// Hand this launch's args to the running primary. Returns false when no primary
        /// answered in time; the caller should then just start normally so the user never
        /// loses an open because of a hung or half-started sibling.
        /// </summary>
        public static bool TryForward(IEnumerable<string> args, int timeoutMs = 2000)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                pipe.Connect(timeoutMs);
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                var pidLine = reader.ReadLine();
                if (int.TryParse(pidLine, out int primaryPid))
                    try { AllowSetForegroundWindow(primaryPid); } catch { }
                writer.Write(SingleInstanceProtocol.Encode(args));
                writer.Flush();
                pipe.WaitForPipeDrain();
                return true;
            }
            catch { return false; }
        }
    }
}
