using System.Text.Json;

namespace Scalpel.Services
{
    internal sealed class SignatureStore
    {
        private readonly string _dir;
        private readonly string _file;

        private static readonly string DefaultDir  = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Scalpel");

        public SignatureStore()
            : this(DefaultDir, System.IO.Path.Combine(DefaultDir, "signatures.json")) { }

        internal SignatureStore(string dir, string file)
        {
            _dir  = dir;
            _file = file;
        }

        private List<SavedSignature> _items = [];

        public IReadOnlyList<SavedSignature> Signatures => _items;

        public void Load()
        {
            try
            {
                if (System.IO.File.Exists(_file))
                {
                    var json = System.IO.File.ReadAllText(_file);
                    _items = JsonSerializer.Deserialize<List<SavedSignature>>(json) ?? [];
                }
            }
            catch (Exception ex)
            {
                _items = [];
                Logger.Error("Sign", "signature.load.fail", "Could not read saved signatures", ex, new { file = _file });
            }
        }

        public void Persist()
        {
            try
            {
                System.IO.Directory.CreateDirectory(_dir);
                var json = JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(_file, json);
            }
            catch (Exception ex)
            {
                // Best effort - the session keeps its in-memory list - but make the loss visible.
                Logger.Error("Sign", "signature.save.fail", "Could not save signatures", ex, new { file = _file });
            }
        }

        public void Add(SavedSignature sig) => _items.Add(sig);

        public void Remove(SavedSignature sig) => _items.Remove(sig);
    }
}
