using System;

namespace Scalpel.Services
{
    /// <summary>
    /// Separates fast in-page wheel scrolling from page navigation at the edge. Momentum events
    /// immediately following a content scroll are ignored; after that, one standard geared-wheel
    /// notch or an equivalent accumulated precision-wheel gesture changes the page.
    /// </summary>
    public sealed class WheelPageFlipGate
    {
        private static readonly TimeSpan MomentumQuietPeriod = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan ConfirmationWindow = TimeSpan.FromMilliseconds(650);
        private const int ConfirmationDelta = 120;

        private DateTime _blockUntilUtc;
        private DateTime _lastEdgeWheelUtc;
        private int _direction;
        private int _accumulatedDelta;

        /// <summary>Call whenever a wheel event scrolled content inside the page: edge flips
        /// are blocked for a short quiet period so trackpad momentum cannot turn the page.</summary>
        public void NoteContentScroll(DateTime nowUtc)
        {
            _blockUntilUtc = nowUtc + MomentumQuietPeriod;
            ResetConfirmation();
        }

        /// <summary>Feeds one wheel <paramref name="delta"/> received at the page edge. Returns
        /// true when the gesture amounts to a deliberate page flip.</summary>
        public bool TryConfirm(int delta, DateTime nowUtc)
        {
            if (delta == 0 || nowUtc < _blockUntilUtc)
            {
                ResetConfirmation();
                return false;
            }

            int direction = Math.Sign(delta);
            if (_direction != direction || nowUtc - _lastEdgeWheelUtc > ConfirmationWindow)
            {
                _direction = direction;
                _accumulatedDelta = 0;
            }

            _lastEdgeWheelUtc = nowUtc;
            _accumulatedDelta += Math.Abs(delta);
            if (_accumulatedDelta < ConfirmationDelta) return false;

            ResetConfirmation();
            return true;
        }

        private void ResetConfirmation()
        {
            _lastEdgeWheelUtc = default;
            _direction = 0;
            _accumulatedDelta = 0;
        }
    }
}
