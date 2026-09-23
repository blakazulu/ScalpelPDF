using System;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class SingleInstanceProtocolTests
    {
        [Fact]
        public void Encode_then_Decode_round_trips_args_with_spaces_and_flags()
        {
            string[] args = ["/edit", @"C:\My Docs\annual report (final).pdf"];

            string wire = SingleInstanceProtocol.Encode(args);
            string[] back = SingleInstanceProtocol.Decode(wire);

            Assert.Equal(args, back);
        }

        [Fact]
        public void Decode_of_an_empty_launch_yields_no_args()
        {
            string wire = SingleInstanceProtocol.Encode(Array.Empty<string>());
            Assert.Empty(SingleInstanceProtocol.Decode(wire));
        }

        [Fact]
        public void Decode_rejects_a_message_without_the_magic_header()
        {
            Assert.Throws<FormatException>(() => SingleInstanceProtocol.Decode("hello\r\nworld"));
        }

        [Fact]
        public void PickLaunchTarget_finds_the_first_existing_file_and_the_edit_flag()
        {
            string existing = System.IO.Path.GetTempFileName();
            try
            {
                var (file, edit) = SingleInstanceProtocol.PickLaunchTarget(["/edit", @"C:\definitely\missing.pdf", existing], System.IO.File.Exists);
                Assert.Equal(existing, file);
                Assert.True(edit);

                var (none, noEdit) = SingleInstanceProtocol.PickLaunchTarget([@"C:\definitely\missing.pdf"], System.IO.File.Exists);
                Assert.Null(none);
                Assert.False(noEdit);
            }
            finally { try { System.IO.File.Delete(existing); } catch { } }
        }

        [Fact]
        public void PickLaunchTargets_returns_every_existing_file_in_order()
        {
            var existing = new[] { @"C:\a.pdf", @"C:\b.pdf" };
            var (files, edit) = SingleInstanceProtocol.PickLaunchTargets(
                [@"C:\a.pdf", "/edit", @"C:\missing.pdf", @"C:\b.pdf"],
                p => Array.IndexOf(existing, p) >= 0);
            Assert.Equal(existing, files);
            Assert.True(edit);
        }

        [Fact]
        public void PickLaunchTargets_drops_duplicates_of_the_same_file()
        {
            var (files, _) = SingleInstanceProtocol.PickLaunchTargets(
                [@"C:\a.pdf", @"c:\A.PDF"], _ => true);
            Assert.Single(files);
        }

        [Fact]
        public void PickLaunchTargets_survives_a_throwing_existence_check()
        {
            var (files, _) = SingleInstanceProtocol.PickLaunchTargets(
                [@"C:\a.pdf"], _ => throw new System.IO.IOException());
            Assert.Empty(files);
        }
    }
}
