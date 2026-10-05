# Export ABI and timeline polish checkpoint

[English](export-abi-and-timeline-polish.md) | [简体中文](../zh-CN/checkpoints/export-abi-and-timeline-polish.md)

## Completed behavior

- Debug native export output was still ABI 1 while managed code required ABI 2. Reproduced four CPU/GPU worker failures, rebuilt the matching native outputs, and retained the runtime compatibility guard with actionable version/size/path diagnostics.
- Export CMake writes a public-header SHA256 stamp after successful native compilation. Managed build rejects absent/stale metadata before copying old output. Debug and Release remain isolated; managed-only compilation without native output is still allowed.
- Committed prior implementation changes as `38cd786` (47 files). Existing tests, documentation, and README changes were excluded and their index state preserved. Subsequent UI implementation is present in the workspace commit `bcd7c02`; updated skills/documentation and managed tests remain in the working tree/index.
- Ordinary business text, registered window captions/dialogs, timeline text, and anchor diagrams share typography resources. Badges center measured line boxes; oversized headings/icons and DSL/log multiline editing retain their appropriate layout.
- A single triangle expands/collapses all track animation rows. No separate diamond control remains; clips stay visible while collapsed.
- Timeline and progress-bar scrubbing present intermediate video frames before release, including return to the starting position. Canvas-only cancellation no longer cancels transport scrubbing. Exact release and interactive quality behavior remain intact.
- Quality and SDR/HDR controls use one 32-DIP overlay row.
- Updated the repository `aeginext-controls` skill and regression map; its installed symlink uses the same content.

## Automated evidence

- Native export CTest: 2/2 in each macOS/Windows x64 Debug/Release configuration (8 executed tests).
- Media ABI/real-worker export: 8/8 in each configuration (32 executed tests). macOS CPU and VideoToolbox GPU H.264/HEVC succeed; Parallels reports unavailable hardware encoders without fallback, and CPU recovery succeeds.
- Build contract regression: 12/12, covering missing/stale/malformed stamps, current lowercase hashes, managed-only output, and design-time analysis.
- UI regressions: macOS Debug 55/55; Windows x64 Release 55/55 (35 primary plus 20 related cases per platform). Covers held-pointer presentation, track expansion, mixed typography, full thumb geometry, narrow/wide floating preview, Dock icons/tabs, DSL input/completion, keyframe hover labels, expanded-clip curves, and late-seek protection. Test contexts close their windows.
- Rider error analysis, affected builds, and skill validation passed; final checks are recorded in `artifacts/verification/`.

## Visual scope

Headless captures include dark/light Dock and script-editor views plus narrow/wide preview overlays. These support automated geometry checks; live video responsiveness with long-GOP/4K media, touchpad behavior, multi-DPI text, and physical native window chrome remain user visual acceptance items. Existing running workbenches are not closed or restarted automatically.

Product version remains `0.1.0`; no project-format change or new release package is included in this repair.
