using Xunit;

// PDFium is a process-global singleton with thread affinity, reached through Docnet by many of
// these tests (directly, and through DocnetPageRasterizer in the compression, OCR, redaction and
// feature-matrix suites). Running any of that in parallel with anything else is unsound: the
// library has one global state and one form-fill environment lifecycle, and concurrent use
// produces an AccessViolationException in its teardown that no managed code can catch.
//
// Scalpel itself serializes every PDFium call onto one dedicated thread (Services/PdfiumGate.cs),
// but the test host still interleaves whole test classes, which is enough to trip it. Disabling
// assembly-level parallelization makes the suite deterministic and costs a few seconds.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
