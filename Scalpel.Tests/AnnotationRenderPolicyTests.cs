using Docnet.Core.Models;
using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class AnnotationRenderPolicyTests
    {
        [Fact]
        public void OutputAlwaysDrawsAnnotations()
        {
            // Print, flatten, export and thumbnails are finished artefacts: whatever the file
            // carries has to be in them.
            Assert.True(AnnotationRenderPolicy.DrawsAnnotations(AnnotationRenderPolicy.ForOutput()));
        }

        [Fact]
        public void ViewerDrawsAnnotationsOnAPlainDocument()
        {
            Assert.True(AnnotationRenderPolicy.DrawsAnnotations(
                AnnotationRenderPolicy.ForViewer(documentHasFormFields: false)));
        }

        [Fact]
        public void ViewerWithholdsAnnotationsOnAFormDocument()
        {
            // The viewer puts live controls over fillable fields; baking them in as well shows
            // through as ghost text under the control.
            Assert.False(AnnotationRenderPolicy.DrawsAnnotations(
                AnnotationRenderPolicy.ForViewer(documentHasFormFields: true)));
        }

        [Fact]
        public void OutputIgnoresWhetherTheDocumentHasFields()
        {
            // A flattened or printed form must show its values, so output never withholds.
            Assert.Equal(RenderFlags.RenderAnnotations, AnnotationRenderPolicy.ForOutput());
        }
    }
}
