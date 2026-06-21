# Scalpel E2E Test Report

**Total:** 203 &nbsp; **Passed:** 139 &nbsp; **Failed:** 64 &nbsp; **Untested controls:** 0

## Summary by suite

| Suite | Total | Passed | Failed |
|---|---:|---:|---:|
| singles | 36 | 35 | 1 |
| journeys | 17 | 5 | 12 |
| pairwise | 30 | 0 | 30 |
| monkey | 120 | 99 | 21 |

## Failures (64)

### [singles] ViewGridBtn

**Reason:** expected click 'ViewGridBtn' not logged

Log context:

```
2026-06-21T07:49:39.2660000Z INFO UI/click ToggleButton
```

### [journeys] view:single

**Reason:** ModeViewTab is not selected after click

### [journeys] view:ViewContinuousBtn

**Reason:** expected click 'ViewContinuousBtn' not logged

### [journeys] view:ViewTwoPageBtn

**Reason:** expected click 'ViewTwoPageBtn' not logged

### [journeys] view:ViewGridBtn

**Reason:** expected click 'ViewGridBtn' not logged

### [journeys] view:ViewSingleBtn

**Reason:** expected click 'ViewSingleBtn' not logged

### [journeys] view:ViewFitBtn

**Reason:** expected click 'ViewFitBtn' not logged

### [journeys] mode:edit

**Reason:** ModeEditTab is not selected after click

### [journeys] tool:ToolSelectBtn

**Reason:** expected click 'ToolSelectBtn' not logged

### [journeys] tool:ToolTextBtn

**Reason:** expected click 'ToolTextBtn' not logged

### [journeys] tool:ToolHighlightBtn

**Reason:** expected click 'ToolHighlightBtn' not logged

### [journeys] tool:ToolDrawBtn

**Reason:** expected click 'ToolDrawBtn' not logged

### [journeys] tool:ToolCropBtn

**Reason:** expected click 'ToolCropBtn' not logged

### [pairwise] ToolSelectBtn->ToolTextBtn

**Reason:** expected click 'ToolTextBtn' not logged

### [pairwise] ToolSelectBtn->ToolHighlightBtn

**Reason:** expected click 'ToolHighlightBtn' not logged

### [pairwise] ToolSelectBtn->ToolDrawBtn

**Reason:** expected click 'ToolDrawBtn' not logged

### [pairwise] ToolSelectBtn->ToolImageBtn

**Reason:** expected click 'ToolImageBtn' not logged

### [pairwise] ToolSelectBtn->ToolCropBtn

**Reason:** expected click 'ToolCropBtn' not logged

### [pairwise] ToolTextBtn->ToolSelectBtn

**Reason:** expected click 'ToolSelectBtn' not logged

### [pairwise] ToolTextBtn->ToolHighlightBtn

**Reason:** expected click 'ToolHighlightBtn' not logged

### [pairwise] ToolTextBtn->ToolDrawBtn

**Reason:** expected click 'ToolDrawBtn' not logged

### [pairwise] ToolTextBtn->ToolImageBtn

**Reason:** expected click 'ToolImageBtn' not logged

### [pairwise] ToolTextBtn->ToolCropBtn

**Reason:** expected click 'ToolCropBtn' not logged

### [pairwise] ToolHighlightBtn->ToolSelectBtn

**Reason:** expected click 'ToolSelectBtn' not logged

### [pairwise] ToolHighlightBtn->ToolTextBtn

**Reason:** expected click 'ToolTextBtn' not logged

### [pairwise] ToolHighlightBtn->ToolDrawBtn

**Reason:** expected click 'ToolDrawBtn' not logged

### [pairwise] ToolHighlightBtn->ToolImageBtn

**Reason:** expected click 'ToolImageBtn' not logged

### [pairwise] ToolHighlightBtn->ToolCropBtn

**Reason:** expected click 'ToolCropBtn' not logged

### [pairwise] ToolDrawBtn->ToolSelectBtn

**Reason:** expected click 'ToolSelectBtn' not logged

### [pairwise] ToolDrawBtn->ToolTextBtn

**Reason:** expected click 'ToolTextBtn' not logged

### [pairwise] ToolDrawBtn->ToolHighlightBtn

**Reason:** expected click 'ToolHighlightBtn' not logged

### [pairwise] ToolDrawBtn->ToolImageBtn

**Reason:** expected click 'ToolImageBtn' not logged

### [pairwise] ToolDrawBtn->ToolCropBtn

**Reason:** expected click 'ToolCropBtn' not logged

### [pairwise] ToolImageBtn->ToolSelectBtn

**Reason:** expected click 'ToolSelectBtn' not logged

### [pairwise] ToolImageBtn->ToolTextBtn

**Reason:** expected click 'ToolTextBtn' not logged

### [pairwise] ToolImageBtn->ToolHighlightBtn

**Reason:** expected click 'ToolHighlightBtn' not logged

### [pairwise] ToolImageBtn->ToolDrawBtn

**Reason:** expected click 'ToolDrawBtn' not logged

### [pairwise] ToolImageBtn->ToolCropBtn

**Reason:** expected click 'ToolCropBtn' not logged

### [pairwise] ToolCropBtn->ToolSelectBtn

**Reason:** expected click 'ToolSelectBtn' not logged

### [pairwise] ToolCropBtn->ToolTextBtn

**Reason:** expected click 'ToolTextBtn' not logged

### [pairwise] ToolCropBtn->ToolHighlightBtn

**Reason:** expected click 'ToolHighlightBtn' not logged

### [pairwise] ToolCropBtn->ToolDrawBtn

**Reason:** expected click 'ToolDrawBtn' not logged

### [pairwise] ToolCropBtn->ToolImageBtn

**Reason:** expected click 'ToolImageBtn' not logged

### [monkey] ToolDrawBtn

**Reason:** control not found / not clickable

### [monkey] ViewContinuousBtn

**Reason:** control not found / not clickable

### [monkey] ToolTextBtn

**Reason:** control not found / not clickable

### [monkey] ModePagesTab

**Reason:** ModePagesTab is not selected after click

### [monkey] ViewSingleBtn

**Reason:** control not found / not clickable

### [monkey] ToolHighlightBtn

**Reason:** control not found / not clickable

### [monkey] ViewTwoPageBtn

**Reason:** control not found / not clickable

### [monkey] ToolImageBtn

**Reason:** control not found / not clickable

### [monkey] ToolDrawBtn

**Reason:** control not found / not clickable

### [monkey] ViewContinuousBtn

**Reason:** control not found / not clickable

### [monkey] ModeViewTab

**Reason:** ModeViewTab is not selected after click

### [monkey] ModePagesTab

**Reason:** ModePagesTab is not selected after click

### [monkey] ModeViewTab

**Reason:** ModeViewTab is not selected after click

### [monkey] ModeViewTab

**Reason:** ModeViewTab is not selected after click

### [monkey] ViewGridBtn

**Reason:** control not found / not clickable

### [monkey] ModePagesTab

**Reason:** ModePagesTab is not selected after click

### [monkey] ToolSignatureBtn

**Reason:** control not found / not clickable

### [monkey] ToolSelectBtn

**Reason:** control not found / not clickable

### [monkey] ViewFitBtn

**Reason:** control not found / not clickable

### [monkey] ModeViewTab

**Reason:** ModeViewTab is not selected after click

### [monkey] ViewSingleBtn

**Reason:** control not found / not clickable

## Controls never exercised

_None — full coverage._
