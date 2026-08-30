using System;
using System.Linq;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class TempSweepTests
    {
        [Fact]
        public void MakeName_embeds_owner_pid_and_tag()
        {
            var g = Guid.NewGuid();
            string name = TempSweep.MakeName(4242, "repaired", g);

            Assert.StartsWith("scalpel_p4242_repaired_", name);
            Assert.EndsWith(".pdf", name);
            Assert.Equal(4242, TempSweep.OwnerPid(name));
        }

        [Fact]
        public void OwnerPid_is_null_for_legacy_names_without_a_pid()
        {
            Assert.Null(TempSweep.OwnerPid("scalpel_repaired_0123456789abcdef0123456789abcdef.pdf"));
            Assert.Null(TempSweep.OwnerPid("scalpel_dec_abc.pdf"));
            Assert.Null(TempSweep.OwnerPid(@"C:\x\scalpel_pXYZ_tag_abc.pdf"));
        }

        [Fact]
        public void OwnerPid_parses_from_a_full_path()
        {
            Assert.Equal(77, TempSweep.OwnerPid(@"C:\Users\me\AppData\Local\Scalpel\Temp\scalpel_p77_netopen_abc.pdf"));
        }

        [Fact]
        public void Sweepable_keeps_files_owned_by_a_live_process_and_drops_the_rest()
        {
            string[] files =
            [
                @"T\scalpel_p100_repaired_a.pdf", // live sibling instance -> KEEP
                @"T\scalpel_p200_repaired_b.pdf", // dead (crashed) instance -> sweep
                @"T\scalpel_repaired_c.pdf",      // legacy name, no owner -> sweep
                @"T\scalpel_p300_dec_d.pdf",      // our own pid -> sweep (we cannot own files before startup)
            ];

            var sweep = TempSweep.Sweepable(files, selfPid: 300, isProcessAlive: pid => pid == 100 || pid == 300).ToArray();

            Assert.DoesNotContain(@"T\scalpel_p100_repaired_a.pdf", sweep);
            Assert.Contains(@"T\scalpel_p200_repaired_b.pdf", sweep);
            Assert.Contains(@"T\scalpel_repaired_c.pdf", sweep);
            Assert.Contains(@"T\scalpel_p300_dec_d.pdf", sweep);
        }
    }
}
