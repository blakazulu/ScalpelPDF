using System;
using System.IO;
using System.Linq;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    [Collection("Logger")] // shares the static Logger with LoggerTests; see the note there
    public class SignatureStoreTests
    {
        [Fact]
        public void NewStore_HasEmptySignatures()
        {
            var store = new SignatureStore();
            Assert.Empty(store.Signatures);
        }

        [Fact]
        public void Add_IncreasesCount()
        {
            var store = new SignatureStore();
            store.Add(new SavedSignature { Name = "Test" });
            Assert.Single(store.Signatures);
        }

        [Fact]
        public void Remove_DecreasesCount()
        {
            var store = new SignatureStore();
            var sig = new SavedSignature { Name = "Test" };
            store.Add(sig);
            store.Remove(sig);
            Assert.Empty(store.Signatures);
        }

        [Fact]
        public void RoundTrip_PersistAndLoad()
        {
            var dir  = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            var file = System.IO.Path.Combine(dir, "sig_test.json");

            try
            {
                var store1 = new SignatureStore(dir, file);
                store1.Add(new SavedSignature { Name = "Alpha", CanvasWidth = 400, CanvasHeight = 150 });
                store1.Add(new SavedSignature { Name = "Beta",  CanvasWidth = 300, CanvasHeight = 100 });
                store1.Persist();

                var store2 = new SignatureStore(dir, file);
                store2.Load();

                Assert.Equal(2, store2.Signatures.Count);
                Assert.Equal("Alpha", store2.Signatures[0].Name);
                Assert.Equal("Beta",  store2.Signatures[1].Name);
            }
            finally
            {
                System.IO.Directory.Delete(dir, recursive: true);
            }
        }
    
        [Fact]
        public void Load_with_corrupt_json_logs_a_signature_load_fail_event()
        {
            var dir = Path.Combine(Path.GetTempPath(), "scalpel_sigtest_" + Guid.NewGuid().ToString("N"));
            var logDir = Path.Combine(dir, "logs");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "signatures.json");
            File.WriteAllText(file, "{ this is not json");
            try
            {
                Logger.Init(logDir);
                var store = new SignatureStore(dir, file);
                store.Load();
                Logger.Shutdown();

                Assert.Empty(store.Signatures);
                var log = File.ReadAllText(Directory.GetFiles(logDir, "scalpel-*.jsonl").Single());
                Assert.Contains("\"event\":\"signature.load.fail\"", log);
                Assert.Contains("\"level\":\"ERROR\"", log);
            }
            finally
            {
                try { Logger.Shutdown(); } catch { }
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }
}
}
