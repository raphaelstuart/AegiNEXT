# macOS native HDR diagnostic

[English](native-hdr.md) | [简体中文](zh-CN/native-hdr.md)

## Scope

The original Phase 1 / Step 1.3 presents static F16 text/shapes through an Avalonia native child view into macOS EDR. Use --hdr-probe. The **current default entry is the subtitle workbench**; this independent diagnostic has its own nonproduct UI and no video clock/decode integration. SDR preview and HDR-preserving export take product priority; Windows HDR display is deferred and not a workbench build prerequisite.

```text
AegiNext.Rendering (linear sRGB, premultiplied RGBA F16)
  → AegiNext.Media (validation, SafeHandle, C ABI)
  → native F32 normalized upload
  → libplacebo / Vulkan / MoltenVK
  → FP16 Linear Display P3 / CAMetalLayer EDR
```

Core gains no graphics/native dependency. NSView presentation bypasses Avalonia Bitmap/RGBA8.

## Dependencies and build

native/dependencies.json locks versions/licenses: libplacebo 7.360.1, MoltenVK 1.4.2, Vulkan-Headers 1.4.357.0. Require Vulkan/custom entry/shaderc capabilities; version/capability mismatch stops CMake without alternate rendering.

Historical local tools: CMake 4.4.3, Ninja 1.13.2, pkgconf 3.0.7, shaderc 2026.4. Newest future Homebrew packages are not a reproducible source for these locks.

```sh
brew install powershell
pwsh -NoProfile -File ./build.ps1 -Target All -CheckEnvironment
pwsh -NoProfile -File ./build.ps1 -Target All -InstallDependencies -RunTests -TestProjects Media
```

A historical mirror failure was bypassed per invocation with HOMEBREW_API_DOMAIN=https://formulae.brew.sh/api and HOMEBREW_BOTTLE_DOMAIN=https://ghcr.io/v2/homebrew/core, without persistent changes.

RID/config outputs include artifacts/native/osx-arm64/Release/libaeginext_media.dylib. Matching Media builds copy existing output; All builds native first. Missing library does not block ordinary workbench; probe reports load failure. See [building](building.md).

The historical artifacts/AegiNext.app was a temporary UI bundle, **not a release package**. Although own deployment target is macOS 14, local libplacebo requires 27, producing a linker warning. Later complete packages rewrite closure/sign/signature metadata and record actual minimum; see [publishing](publishing.md). macOS 14 dependency rebuild/notarization is not proven by these diagnostics.

## Pixels and brightness

- Tight little-endian RGBA Half input; ABI also permits padded rows. Managed HdrFrame copies/owns input; Present finishes pointer use before return.
- Premultiplied extended linear sRGB, negative/>1 RGB permitted; finite alpha [0,1]. Alpha zero requires zero RGB.
- Explicit ReferenceWhiteNits; caller SourcePeakNits bounds positive unpremultiplied RGB with 0.1% quantization tolerance, not measured maximum luminance or screen brightness.
- F32 upload scales RGB × ReferenceWhiteNits / 203 and preserves alpha, avoiding Half intermediate overflow/loss. Nonfinite/nonzero underflow rejects.
- libplacebo Linear metadata does not itself rescale pixels; explicit normalization is required. Target peak each frame is 203 × currentHeadroom.
- EDR 1 is current-screen SDR white; 203 is nominal application scale, not a claim of physical 203-nit display.
- Read screen/backing scale/headroom each frame, preserving aspect. Invisible/minimized/zero-size/no drawable returns NOT_READY for later retry.
- Verify actual 4×16-bit float/Linear Display P3 swapchain, RGBA16Float CAMetalLayer, extended-linear P3, enabled EDR, and no additional EDRMetadata mapping; mismatch fails.

## ABI and lifetime

native/include/aeginext_hdr.h ABI 1 has 72-byte status/32-byte GPU validation; size/version required. Error codes/UTF-8 buffers carry errors; native exceptions never cross ABI.

Session owns NSView; Avalonia borrows/attaches. Create/present/GPU checks require main thread. SafeHandle finalizer queues destruction to main as fallback; normal NativeControlHost explicitly destroys. Detach stops timer only; DestroyNativeControlCore is the actual destruction boundary, so reattach cannot borrow a dead view.

Close cancels/awaits automatic check, removes host, awaits native destruction, records live count/report, then exits. Event-handler-local using owns the token source; Closing cancels it, and finally unsubscribes/releases. No additional synchronous window Dispose path.

## Running and evidence

```sh
dotnet run --project src/AegiNext.Desktop --configuration Release --no-build -- \
  --hdr-probe \
  --hdr-probe-font Tests/AegiNext.Rendering.Tests/Fixtures/NotoSans.ttf \
  --hdr-probe-report artifacts/verification/step-1.3-hdr-manual.json
```

Use --hdr-probe-auto for automation; exit 0 success/1 failure. Inspect Failure/GPU/lifecycle JSON. It waits for real presentation, resizes, verifies synchronous reattach identity, actual detach/rebuild, and final zero live sessions.

Pattern: 0/0.18/0.5/1/2/4 reference-white grays, 2× RGB, translucent bright circle, optional shaped 2× text.

Evidence levels:

1. Managed CPU frame/white/peak/Half/alpha/copy/ABI tests, no GPU.
2. Native CTest with six CPU/ABI groups, no GPU session.
3. Actual F16 upload/readback, negatives/extremes/sRGB→P3/equivalent whites/4×white; offscreen, not screenshot.
4. Swapchain/layer properties, host scaling/reattach/destruction.
5. Manual brightness/edges/screens/display-setting changes; screenshots cannot prove physical HDR luminance.

Historical results are in [the original checkpoint](README.md#implementation-evidence). These probe tests do not prove Windows/macOS14/video PTS/realtime/minimize restore/complex composition/export/colorimeter measurements.
