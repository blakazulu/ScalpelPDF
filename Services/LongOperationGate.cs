using System;
using System.Threading;

namespace Scalpel.Services
{
    /// <summary>Counts operations that finish after an await (compress, OCR, flatten, ...).
    /// Tab switching and closing are refused while any is running, so a result can never land
    /// in a document other than the one it started on.</summary>
    public sealed class LongOperationGate
    {
        private int _count;
        public bool IsBusy => Volatile.Read(ref _count) > 0;

        public IDisposable Begin()
        {
            Interlocked.Increment(ref _count);
            return new Token(this);
        }

        private sealed class Token(LongOperationGate owner) : IDisposable
        {
            private int _done;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _done, 1) == 0) Interlocked.Decrement(ref owner._count);
            }
        }
    }
}
