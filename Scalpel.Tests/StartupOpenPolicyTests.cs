using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class StartupOpenPolicyTests
    {
        [Fact]
        public void Command_line_files_always_open()
        {
            Assert.Equal(StartupOpen.CommandLine, StartupOpenPolicy.Decide(1, documentAlreadyOpen: false, queuedOpens: 0));
            Assert.Equal(StartupOpen.CommandLine, StartupOpenPolicy.Decide(2, documentAlreadyOpen: true, queuedOpens: 3));
        }

        [Fact]
        public void Nothing_open_restores_the_last_session()
        {
            Assert.Equal(StartupOpen.Restore, StartupOpenPolicy.Decide(0, documentAlreadyOpen: false, queuedOpens: 0));
        }

        [Fact]
        public void A_forwarded_file_already_open_skips_restore()
        {
            // A second launch forwarded a file before Loaded ran: it wins (R18), and restore must
            // not stamp restored paths onto the session holding it.
            Assert.Equal(StartupOpen.Nothing, StartupOpenPolicy.Decide(0, documentAlreadyOpen: true, queuedOpens: 0));
        }

        [Fact]
        public void A_forwarded_file_still_queued_skips_restore()
        {
            Assert.Equal(StartupOpen.Nothing, StartupOpenPolicy.Decide(0, documentAlreadyOpen: false, queuedOpens: 1));
        }

        [Fact]
        public void An_open_still_in_progress_skips_restore()
        {
            // A forwarded open can be mid-flight (a password/repair prompt, or the >20-files
            // confirmation) when Loaded runs inside the modal's nested message loop: at that
            // instant nothing is on the session yet and nothing is queued, so documentAlreadyOpen
            // and queuedOpens alone would miss it. openInProgress must still force Nothing.
            Assert.Equal(StartupOpen.Nothing, StartupOpenPolicy.Decide(0, documentAlreadyOpen: false, queuedOpens: 0, openInProgress: true));
        }

        [Fact]
        public void Command_line_files_win_even_while_an_open_is_in_progress()
        {
            Assert.Equal(StartupOpen.CommandLine, StartupOpenPolicy.Decide(1, documentAlreadyOpen: false, queuedOpens: 0, openInProgress: true));
        }

        [Theory]
        [InlineData(false, false, false, true)]   // the empty start state
        [InlineData(true,  false, false, false)]  // holds a document
        [InlineData(false, true,  false, false)]  // deferred: a real file stands behind it
        [InlineData(false, false, true,  false)]  // dirty
        public void Only_the_empty_start_state_is_reused(bool hasDoc, bool deferred, bool dirty, bool expected)
        {
            Assert.Equal(expected, StartupOpenPolicy.IsReusablePlaceholder(hasDoc, deferred, dirty));
        }
    }
}
