using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Docnet.Core;
using Docnet.Core.Models;
using Docnet.Core.Readers;

namespace Scalpel.Services
{
    /// <summary>
    /// A Docnet document reader whose PDFium form-fill block cannot move while PDFium holds it.
    ///
    /// <para><b>Why.</b> Docnet 2.6.0 builds the <c>FPDF_FORMFILLINFO</c> PDFium needs for
    /// annotation rendering as an ordinary managed object (<c>DocumentWrapper._formInfo</c>) and
    /// passes it straight to <c>FPDFDOC_InitFormFillEnvironment</c>. The interop layer pins it only
    /// for that one call, but PDFium keeps the pointer and reads it again at teardown
    /// (<c>FPDFDOC_ExitFormFillEnvironment</c> calls <c>m_pInfo-&gt;Release</c>). A garbage
    /// collection in between compacts the object elsewhere, so PDFium reads a stale heap address;
    /// once that memory is reused with non-zero data the teardown jumps through garbage and the
    /// process dies with an <c>AccessViolationException</c> .NET Framework cannot catch. The longer a
    /// reader lives (the sidebar thumbnail loader keeps one open across every page of a document)
    /// the likelier a GC lands inside that window.</para>
    ///
    /// <para><b>Fix.</b> Stand up the form-fill environment ourselves, exactly as Docnet would
    /// (same object type, same version 1 then 2 retry), but on an object pinned with a
    /// <see cref="GCHandle"/> for the reader's whole life, and let Docnet adopt it (its lazy
    /// <c>GetFormHandle</c> returns an existing handle as is). The pin is released only after the
    /// reader's Dispose has torn the environment down. If Docnet's internals ever change shape the
    /// reflection finds nothing and the plain reader is returned, i.e. today's behaviour.</para>
    ///
    /// <para>Like every PDFium call, <see cref="Open"/> and <see cref="Dispose"/> must run on the
    /// PDFium thread (<see cref="PdfiumGate"/>).</para>
    /// </summary>
    public sealed class PinnedDocReader : IDocReader
    {
        private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Stat = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly FieldInfo? DocWrapperField;
        private static readonly FieldInfo? FormInfoField;
        private static readonly FieldInfo? FormHandleField;
        private static readonly FieldInfo? VersionField;
        private static readonly PropertyInfo? InstanceProp;
        private static readonly MethodInfo? InitFormFill;

        static PinnedDocReader()
        {
            try
            {
                var asm = typeof(DocLib).Assembly;
                DocWrapperField = asm.GetType("Docnet.Core.Readers.DocReader")?.GetField("_docWrapper", Inst);
                var wrapper = asm.GetType("Docnet.Core.Bindings.DocumentWrapper");
                FormInfoField = wrapper?.GetField("_formInfo", Inst);
                FormHandleField = wrapper?.GetField("_formHandle", Inst);
                InstanceProp = wrapper?.GetProperty("Instance", Inst);
                VersionField = FormInfoField?.FieldType.GetField("version", Inst);
                InitFormFill = asm.GetType("Docnet.Core.Bindings.fpdf_view")?.GetMethod("FPDFDOCInitFormFillEnvironment", Stat);
            }
            catch { }
        }

        private readonly IDocReader _inner;
        private GCHandle _pin;

        private PinnedDocReader(IDocReader inner, GCHandle pin)
        {
            _inner = inner;
            _pin = pin;
        }

        /// <summary>
        /// Opens <paramref name="path"/> like <c>DocLib.Instance.GetDocReader</c>, with the
        /// form-fill block pinned. Call on the PDFium thread.
        /// </summary>
        public static IDocReader Open(string path, PageDimensions dimensions)
        {
            var reader = DocLib.Instance.GetDocReader(path, dimensions);
            GCHandle pin;
            try { pin = InstallPinnedFormFill(reader); }
            catch (Exception ex) { pin = default; WarnUnpinned("exception", ex.GetType().Name + ": " + ex.Message); }
            return pin.IsAllocated ? new PinnedDocReader(reader, pin) : reader;
        }

        private static int _warned;

        /// <summary>
        /// Logs, once per session, that readers are falling back to Docnet's own (unpinned, crash-
        /// prone) form-fill block - the signal that a Docnet upgrade renamed the internals this
        /// class reaches into.
        /// </summary>
        private static void WarnUnpinned(string reason, string detail)
        {
            if (System.Threading.Interlocked.Exchange(ref _warned, 1) != 0) return;
            try
            {
                Logger.Warn("Pdfium", "formfill.unpinned",
                    "PDF reader form-fill block could not be pinned; using Docnet's own",
                    new { reason, detail });
            }
            catch { }
        }

        /// <summary>The first Docnet member the reflection could not find, or null when all were found.</summary>
        private static string? MissingMember() =>
            DocWrapperField is null ? "DocReader._docWrapper"
            : FormInfoField is null ? "DocumentWrapper._formInfo"
            : FormHandleField is null ? "DocumentWrapper._formHandle"
            : VersionField is null ? "FPDF_FORMFILLINFO.version"
            : InstanceProp is null ? "DocumentWrapper.Instance"
            : InitFormFill is null ? "fpdf_view.FPDFDOCInitFormFillEnvironment"
            : null;

        /// <summary>
        /// True when <paramref name="reader"/>'s form-fill block is pinned. For tests.
        /// </summary>
        internal static bool IsFormFillPinned(IDocReader reader) =>
            reader is PinnedDocReader p && p._pin.IsAllocated
            && ReferenceEquals(p._pin.Target, FormFillInfoOf(reader));

        /// <summary>
        /// The object PDFium was handed as its form-fill info, or null. For tests.
        /// </summary>
        internal static object? FormFillInfoOf(IDocReader reader)
        {
            try
            {
                var inner = reader is PinnedDocReader p ? p._inner : reader;
                var wrapper = DocWrapperField?.GetValue(inner);
                return wrapper is null ? null : FormInfoField?.GetValue(wrapper);
            }
            catch { return null; }
        }

        private static GCHandle InstallPinnedFormFill(IDocReader reader)
        {
            if (MissingMember() is string missing) { WarnUnpinned("missing member", missing); return default; }
            var wrapper = DocWrapperField!.GetValue(reader);
            if (wrapper is null) { WarnUnpinned("missing member", "DocReader._docWrapper is null"); return default; }
            if (FormHandleField!.GetValue(wrapper) is not null) return default;   // already set up
            var document = InstanceProp!.GetValue(wrapper);
            if (document is null) return default;

            var info = Activator.CreateInstance(FormInfoField!.FieldType);
            // Pinned before PDFium ever sees it, so the address it keeps is the one it reads later.
            var pin = GCHandle.Alloc(info, GCHandleType.Pinned);
            bool environmentCreated = false;
            try
            {
                // Docnet's own GetFormHandle tries version 1, then 2.
                for (int version = 1; version <= 2; version++)
                {
                    VersionField!.SetValue(info, version);
                    var handle = InitFormFill!.Invoke(null, [document, info]);
                    if (handle is null) continue;
                    environmentCreated = true;
                    FormInfoField.SetValue(wrapper, info);
                    FormHandleField.SetValue(wrapper, handle);
                    return pin;
                }
            }
            catch (Exception ex)
            {
                WarnUnpinned("exception", ex.GetType().Name + ": " + ex.Message);
            }
            // If PDFium created an environment but handing it to Docnet failed, PDFium still
            // holds a pointer to the block and nothing will ever tear that environment down:
            // the block must stay pinned for good (a small deliberate leak, never a crash).
            if (!environmentCreated) pin.Free();
            return default;
        }

        public PdfVersion GetPdfVersion() => _inner.GetPdfVersion();
        public int GetPageCount() => _inner.GetPageCount();
        public IPageReader GetPageReader(int pageIndex) => _inner.GetPageReader(pageIndex);

        public void Dispose()
        {
            // Tear the environment down first; only then may the block move or be collected.
            _inner.Dispose();
            if (_pin.IsAllocated) _pin.Free();
        }
    }
}
