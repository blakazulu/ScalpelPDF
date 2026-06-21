#if DEBUG
using System.Collections.Generic;
using System.Windows;

namespace Scalpel
{
    public partial class MainWindow
    {
        partial void SeedShotAnnotations(AppMode mode, int page)
        {
            // Canvas-space coordinates (top-left origin) over the displayed page.
            if (mode == AppMode.Edit)
            {
                AddAnnotation(new HighlightAnnotation
                {
                    PageIndex = page,
                    Bounds = new Rect(150, 150, 360, 22),
                });
                AddAnnotation(new TextAnnotation
                {
                    PageIndex = page,
                    Position = new Point(160, 250),
                    Content = "Review this section",
                    FontSize = 16,
                });
                AddAnnotation(new InkAnnotation
                {
                    PageIndex = page,
                    StrokeWidth = 3,
                    Points =
                    [
                        new(160, 310), new(200, 290), new(240, 318), new(280, 290), new(320, 314),
                    ],
                });
            }
            else if (mode == AppMode.Sign)
            {
                AddAnnotation(new SignatureAnnotation
                {
                    PageIndex = page,
                    Position = new Point(360, 480),
                    Scale = 0.6,
                    Strokes =
                    [
                        [ new(0, 40), new(30, 5), new(55, 45), new(85, 8), new(120, 42), new(160, 12) ],
                    ],
                });
            }

            // Redraw the page so the freshly-added overlays appear before capture.
            RenderPage(page);
        }
    }
}
#endif
