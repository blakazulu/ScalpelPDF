using Docnet.Core.Models;

namespace Scalpel.Services
{
    /// <summary>
    /// Decides whether a page rasterization should draw the annotations the PDF already carries.
    ///
    /// <para>Scalpel used to render every page with a bare <c>GetImage</c>, which tells PDFium to
    /// paint page content only. Highlights, stamps, ink, sticky notes and filled field values
    /// added by another editor were therefore invisible in the viewer and, worse, silently
    /// dropped from print, flatten and image export.</para>
    ///
    /// <para>The one place the flag has to be withheld is the interactive viewer of a form
    /// document: Scalpel draws fillable fields as live WPF controls on top of the page, so a
    /// baked copy underneath shows through as ghost text. Output paths have no such overlay and
    /// want everything.</para>
    /// </summary>
    public static class AnnotationRenderPolicy
    {
        /// <summary>
        /// Flags for a rasterization whose result is the finished artefact: print, flatten,
        /// image export, page thumbnails. Always includes annotations.
        /// </summary>
        public static RenderFlags ForOutput() => RenderFlags.RenderAnnotations;

        /// <summary>
        /// Flags for the interactive page view. Annotations are included unless the document has
        /// fillable fields, which the viewer renders as live controls of its own.
        /// </summary>
        public static RenderFlags ForViewer(bool documentHasFormFields)
            => documentHasFormFields ? default : RenderFlags.RenderAnnotations;

        /// <summary>True when the flag set asks for annotations to be painted.</summary>
        public static bool DrawsAnnotations(RenderFlags flags)
            => (flags & RenderFlags.RenderAnnotations) != 0;
    }
}
