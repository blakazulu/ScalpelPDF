using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Scalpel.Services
{
    /// <summary>
    /// Runs every PDFium call on one dedicated thread.
    ///
    /// <para>PDFium is single-threaded, keeps global state, and Docnet stands up a form-fill
    /// environment per document which it tears down on Dispose. Two documents open at once corrupt
    /// that state, and the failure is an <c>AccessViolationException</c> inside
    /// <c>FPDFDOC_ExitFormFillEnvironment</c> - which .NET Framework will not let the app catch
    /// (Scalpel deliberately omits <c>HandleProcessCorruptedStateExceptions</c>), so it kills the
    /// process with no crash dialog and nothing in the log.</para>
    ///
    /// <para>Scalpel really does render concurrently: sidebar thumbnails run two at a time while
    /// background tasks stream continuous and grid tiles and the UI thread renders the primary
    /// page. A plain lock is not enough - measurement showed the fault still occurs when documents
    /// are created and destroyed on <i>different</i> threads even when properly serialized, because
    /// the affinity is to the thread, not just to one-at-a-time access.</para>
    ///
    /// <para><b>Why individual calls and not whole scopes.</b> The obvious design - hold the thread
    /// for a whole render loop - deadlocks. The streaming loops call <c>Dispatcher.Invoke</c>
    /// between pages to hand each tile to the UI and to throttle themselves; if that ran on the
    /// PDFium thread it would block waiting for the UI thread, while the UI thread was blocked
    /// waiting for its own PDFium work. So each PDFium call is marshalled on its own and the
    /// caller keeps its own thread for everything else, which leaves the dispatcher free.</para>
    /// </summary>
    public static class PdfiumGate
    {
        private static readonly BlockingCollection<Action> Work = new();
        private static readonly Thread Worker;

        static PdfiumGate()
        {
            Worker = new Thread(Pump)
            {
                IsBackground = true,     // must never hold the process open
                Name = "PDFium",
            };
            Worker.Start();
        }

        private static void Pump()
        {
            foreach (Action job in Work.GetConsumingEnumerable())
            {
                try { job(); }
                catch { }   // every job captures its own outcome; this is the last-resort net
            }
        }

        /// <summary>True when the caller is already the PDFium thread.</summary>
        private static bool OnPdfiumThread => Thread.CurrentThread == Worker;

        /// <summary>Runs one PDFium call on the PDFium thread and waits for it.</summary>
        public static void Run(Action work)
        {
            if (work is null) return;

            // Re-entrancy: a job that calls back in must not deadlock waiting for itself.
            if (OnPdfiumThread) { work(); return; }

            using var done = new ManualResetEventSlim(false);
            Exception? failure = null;

            Work.Add(() =>
            {
                try { work(); }
                catch (Exception ex) { failure = ex; }
                finally { done.Set(); }
            });

            done.Wait();
            if (failure is not null) Rethrow(failure);
        }

        /// <summary>Runs one PDFium call that produces a value, as above.</summary>
        public static T Run<T>(Func<T> work)
        {
            if (OnPdfiumThread) return work();

            using var done = new ManualResetEventSlim(false);
            T result = default!;
            Exception? failure = null;

            Work.Add(() =>
            {
                try { result = work(); }
                catch (Exception ex) { failure = ex; }
                finally { done.Set(); }
            });

            done.Wait();
            if (failure is not null) Rethrow(failure);
            return result;
        }

        /// <summary>
        /// Rethrows a job's exception on the calling thread with its original stack preserved,
        /// so a failure inside PDFium work still reads like the caller's own.
        /// </summary>
        private static void Rethrow(Exception ex)
            => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
    }
}
