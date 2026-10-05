# SDR video preview

[English](video-preview.md) | [简体中文](zh-CN/video-preview.md)

## Module and ownership boundaries

native/decoder extends ABI 1 with SDR_PREVIEW=2, converter ownership, BGRA8 delivery, leaving decoder/frame structs unchanged. Media/Preview adapts it; Desktop/Controllers orchestrates it. Core has no pixels/FFmpeg dependency.

SdrVideoConverter synchronously borrows an independently referenced DecodedVideoFrame and serially converts on a worker without taking source ownership. Output is independent opaque top-down tight BGRA8 sRGB with square pixels. Conversion/later seek/decoder close/another output mutation never rewrites raw planes/PTS/HDR facts.

Preview is never HDR composition/export input. Export reads original high-precision frames; retaining original metadata alone is not export-quality evidence.

## Explicit color policy

Lock FFmpeg 9.0.2/swscale 10.1.102 with shared SDK/manifest/runtime checks. sws_alloc_context dynamic-frame API selects stable CPU backend, perceptual intent, strict behavior, fixed rounding/dither. Workers clamp actual logical cores to 1–4; each converter still has one serialized consumer. Legacy sws_init_context/default relative-colorimetric intent is unsuitable for this CMS/highlight mapping. [API](https://github.com/FFmpeg/FFmpeg/blob/n9.0.2/libswscale/swscale.h), [CMS](https://github.com/FFmpeg/FFmpeg/blob/n9.0.2/libswscale/cms.c).

Do not enable optional SWS_FULL_CHR_H_INT. Pinned BT.709 YUV420 CMS RGBA64 intermediates showed row chroma contamination, reproduced with constant nonneutral color and eliminated by disabling it. The output path's signed negative-chroma interpolation differs; evidence supports it as the cause, without patching FFmpeg. [Output implementation](https://github.com/FFmpeg/FFmpeg/blob/n9.0.2/libswscale/output.c). Tests cover constant row hue, odd crops, workers 1/2/3/4, SDR/sRGB/PQ/HLG properties.

Targets use RGB matrix/full range/BT.709 primaries/sRGB transfer, no inherited HDR side data. Without target mastering, pinned CMS uses 203-nit software reference, not measured monitor brightness/ICC calibration. [Metadata interpretation](https://github.com/FFmpeg/FFmpeg/blob/n9.0.2/libswscale/format.c).

- PQ uses absolute EOTF; absent mastering defaults to 10000-nit source peak. Valid static mastering affects mapping peak/gamut, not PQ code meaning. Invalid mastering rejects.
- HLG conversion removes mastering from the borrowed conversion view, uses 1000-nit/zero-black/gamma 1.2 with OOTF; original frame facts remain.
- MaxCLL/MaxFALL remain unchanged; this CMS does not use them as exposure/mastering peak.
- BT.709 and sRGB have separate pinned EOTFs.

Policies follow [color functions](https://github.com/FFmpeg/FFmpeg/blob/n9.0.2/libavutil/csp.c) and metadata reading. Cross-platform tests use tolerances/properties, not byte-identical SIMD floats.

## Inputs and geometry

Supported inputs are three-component opaque progressive integer RGB/YUV 8–16-bit with explicit BT.709/BT.2020 primaries, BT.709/sRGB/PQ/HLG transfer, range/matrix, and known chroma location for subsampling. Managed common-format screening is followed by actual native descriptors/backend validation. YUVJ/limited conflict rejects.

Unknown color is never inferred from resolution or replaced by stream facts. Alpha/interlace/float/display-matrix rotation/stereo/dynamic HDR/Dolby Vision/ICC/raw color/film-grain/ambient-view metadata are unsupported and diagnosed.

CMS runs at coded size; precise BGRA crop follows; independent bilinear BGRA resizing fits the selected cap. Odd crop avoids YUV alignment rounding. Aspect is visible width × SAR / visible height; absent SAR uses explicit 1:1 preview policy without rewriting source facts. Media defaults remain 1280×720, output budget 16,777,216 pixels and coded budget 33,177,600. Desktop selects default Low 960×540, Standard 1280×720, High 1920×1080, never enlarging small media; interaction stays ≤540p and returns to the selected level. Coded intermediates remain full-sized, so these caps do not prove realtime 4K/8K decode performance.

## Desktop lifetime

Controller owns even an opening session; close/replace asynchronously closes it rather than merely canceling OpenAsync's command token. File epoch/session identity/playback generation/controller revision are checked after conversion and at actual UI delivery; old file/seek pixels cannot return.

One frame consumer and bounded awaited Dispatcher delivery prevent growth. EOF waits for control so reseek can present. Close invalidates identity first, stops source/consumer, waits conversion, then releases presentation. Already read raw frames belong to consumer. Synchronous CMS cancellation checks before/after, not preemption within one call.

The clock advances independently. Unconverted queued frames may expire, but newest converted valid-generation/nondecreasing-PTS frames are delivered even if UI has crossed their original end, avoiding endless discard when conversion costs one frame interval. EOF accepts only the selected tail. Snapshot records PresentedFrameTime, PresentedAtPosition, PresentedGeneration. Open/seek/pause/close reset measurements; only successful identity-checked callbacks publish them. Source time/clock semantics do not change.

Current workbench integrates audio, subtitles, editing, and independent HDR export. Actual appearance/smoothness requires desktop acceptance, separately from numeric/lifetime tests and [CPU performance](interactive-preview-performance.md).
