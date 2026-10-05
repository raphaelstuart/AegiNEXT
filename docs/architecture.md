# Architecture and implementation boundaries

[English](architecture.md) | [简体中文](zh-CN/architecture.md)

## Product scope and status

AegiNext uses .NET 10, C#, Avalonia, and root namespace `AegiNext`. Initial targets are macOS and Windows; Linux is deferred. Implemented modules include rational time/project models, editing/undo, SRT/TXT/project storage, shared F16 rendering, video decode/SDR preview, SDL3 audio, waveform/spectral analysis, and independent export. Module tests do not prove every UI operation, hardware route, or minimum OS.

The product edits subtitle timing/text/style, keyframes, motion paths, and scripted effects over a background video, then exports. It is not a multi-video editor or batch transcoder. Ordinary edge drag trims; Ctrl+edge explicitly stretches time. ASS/Aegisub export is later work; project JSON is not an ASS/Lua intermediate representation.

Mapped SDR preview takes priority while HDR export retains precision. The macOS HDR probe remains optional; Windows native HDR display is deferred. Neither workbench nor export depends on its display window.

## Modules and dependencies

| Location | Responsibility |
|---|---|
| Core | Pure C# rational time, media facts, versioned project/scene tree, validation/evaluation. PreparedProjectScene validates snapshots and caches indexes; SceneEvaluator evaluates local time, paths, and keyframes. |
| Application | Transactions/undo, SRT/TXT, save/load, import/Save As relocation. Video stays external; managed font/image paths can include SHA-256. |
| Rendering | Skia/HarfBuzz linear F16 surfaces and shared text/karaoke/scene rendering, transforms, masks, groups, blur, blend, and SDR composition. Some retained model types have no subtitle-workbench UI. |
| Media | FFprobe/process adapter, decode/audio/export C ABI ownership, seek/playback/CPU SDR conversion, analysis, worker lifecycle, progress/cancel/commit. |
| Desktop | Avalonia workbench, panels, controls, playback, timeline, themes/language. Uses services; never sends windows/view models to worker. |
| ExportWorker | Independent windowless process; receives snapshot/resource root, reuses Rendering, drives native export and audio copy/AAC, returns temporary output. |
| native/decoder | Locked FFmpeg software decode, AVFrame ownership, seek/frame facts, libswscale CPU CMS. |
| native/audio | Locked FFmpeg decode/resample and SDL3 output, preserving media time and consumed-sample progress. |
| native/export | High-precision CPU composition plus H.264/HEVC software/hardware encoding. **No libplacebo dependency.** |
| Optional native HDR | Objective-C++, libplacebo/MoltenVK FP16 EDR diagnostic, isolated from editor/export. |
| Tests and native contract tests | Domain/resources/pixels/process and handle lifetime/real-media output; UI/device/display evidence is separately scoped. |

Dependencies point inward: Desktop → Application/Media/Rendering → Core; ExportWorker → Application/Media/Rendering → Core. Media may use Rendering; Rendering uses only Core and Skia/HarfBuzz. Core uses no UI, native media, or process API. Worker uses no Desktop code or preview screenshots.

## Time, project, and resource contracts

`MediaTime` stores normalized rational seconds with long numerator/positive denominator and BigInteger intermediates. Reduced overflow throws. Default is 0/1. Time bases/timestamps/ranges are validated sealed records; missing PTS is null, never guessed from frame numbers or zero.

`MediaTimestamp` equality compares raw PTS/time-base representation; compare `ToMediaTime()` for the same instant. Rescaling 1/15360 to 1/61440 can preserve rational presentation times. Frame rate is not PTS. Ranges are half-open `[Start, End)`; general scene layers can overlap. Project origin is zero, `projectTime = mediaTime - MediaOrigin`. Seek uses absolute selected-stream time base and exact preroll without resetting origin/speed.

Layer times are project-absolute. Keyframes/paths/karaoke use content time: `LocalTime = time - Start + AnimationOffset`. Trim compensates offset and retains speed; stretch scales local time rationally. Child transforms are parent-relative; each subtitle has one matching same-ID subtitle layer.

`LayerAnimationTiming` bounds points to visible content and inserts trim boundaries while retaining interpolation subintervals. Project **v3** saves stable tracks, unique TrackId, natural ink positioning, full vectors/colors; v1/v2 are unsupported. Same-track subtitle overlap is forbidden; track order never changes composition order.

`SubtitleTrack` stores `DefaultStyle`, preset ID/name, and `AutoApplyStyle`. Personal libraries supply choices; the project owns immutable snapshots. `SubtitleStylePresetService.PrepareAsync` prepares/reuses fonts even for an empty project. Workspace prompts Yes/No/Cancel for existing clips before Application commits default/resources and, optionally, those clip styles in one Undo. Disabled automatic application preserves the track snapshot while new clips use the Styles panel's selected preset. Timeline sends stable Track/Preset IDs, never reads panel controls. Split/move preserve existing style identity.

`SubtitleLine.KaraokeStyle` is one `KaraokeHighlightStyle` appearance snapshot, not per-grapheme duplicated data. Fill/stroke/shadow do not alter font/size/layout. Missing snapshot keeps legacy segment color. Reapply preserves content time; split inherits the side with segments; merge validates actual appearance, excluding source ID/name. Optional added v3 fields may be absent; unknown/duplicate keys remain invalid.

`LayerTransform` stores double-precision Position/Scale/Pivot and Rotation. AnimationValue distinguishes scalar number, vector `{x,y}`, and color `{red,green,blue,alpha}` with a shared Core JSON converter used by project storage and worker wire. Old v3 components migrate per property using time unions, preserving component easing/trim phase; mixed/duplicate representations reject. New writes use full properties. All editing shares one snapshot and Undo stack.

DSL parsing/budgets/rational compilation live in Core/Effects; application transactions in Application; personal templates in Application/Presets; coordination in Desktop/Workspace; editor in Controls/Editing; management in Settings/Effects. Fixed segments precede flexible allocation; source declares short-clip policy. All seven builtins use the same compile path and do not copy another clip's absolute times.

Save stores immutable snapshots. Save As copies managed small resources, keeping large video external, and commits only after validation/copy/hash checks. Changed resources are not silently trusted.

## Shared rendering and SDR preview

Working surfaces use extended linear sRGB, premultiplied RGBA F16, and explicit reference white (default 203 nits). RGB can exceed [0,1]; alpha is coverage and never brightness-scaled. F16 groups, three-box blur, and extended-linear blends avoid SDR clamping in intermediate Skia paths. Preview/export share scene evaluation/rendering and project resource resolution.

Subtitle positioning combines canvas Anchor, ink Pivot, and pixel Offset in parent space. Automatic nine-position alignment uses actual ink, giving exactly zero centered horizontal offset. ProjectLayerGeometry supplies the same bounds/pivot/transforms to drawing and editing. Domain models do not depend on Skia; an independent position resolver bridges measurement. Presentation controls own bitmaps; business view models do not. `.aegistyles` exchanges position/font styles; shared VectorDraftInput has no project ownership.

Highlighting uses the same shaped text: draw base outside the active region and highlight fill/stroke/shadow inside, avoiding retained base outlines. Legacy default uses the old color override path. Worker and Preview share it.

Locked libswscale maps original frames to opaque sRGB BGRA8 with explicit policy, refusing unknown color. Low 960×540 is the default selectable preview cap; Standard 1280×720 and High 1920×1080 are personal choices, without upscaling smaller inputs. Interaction uses at most 540p and release returns to the selected cap. The background is decoded to linear display space, composed with project overlays, then encoded to sRGB. Nominal display white is 203 nits; overlay RGB scales by `project.ReferenceWhiteNits / 203`, while alpha/background do not. Cache identity includes source PTS, scene revision, output size, and evaluation time, so animation/karaoke/interval changes invalidate reuse.

SDR output is a display derivative, never HDR export input. Tone mapping, screenshots, and display mapping cannot represent original high-precision frames. Text currently has unidirectional paragraph shaping and paragraph fallback; complete mixed bidi/per-run fallback remain future boundaries. See [rendering](rendering.md) and [preview](video-preview.md).

## Offline export pipeline

Media/Encoding starts the adjacent worker with snapshot, resource root, and parameters. It works in a private temporary directory:

1. FFmpeg decodes integer YUV with absolute PTS, crop, depth, and frame color. libswscale resamples **within the encoded domain** to YUV444P16, without SDR/BGRA8 or HDR-clamping CMS intermediates.
2. Covered pixels use official FFmpeg matrices/EOTFs into double-precision absolute linear brightness. PQ uses ST 2084; HLG uses 1000-nit/zero-black reference conditions and OOTF; SDR white is 203 nits. PQ reconstruction overshoot is bounded to its valid domain, not SDR white.
3. Shared F16 premultiplied linear-sRGB overlays are transformed to source primaries and brightness-scaled by project white, then source-over composed. Transparent regions preserve encoded-domain values at this stage; final subsampling/quantization/CRF still affects output.
4. Inverse EOTF returns to source encoding, then H.264 8-bit or HEVC 10-bit. Auto chooses SDR/libx264 and HDR/libx265; x265 CRF 0 explicitly enables lossless. GPU selection requests an actually initialized hardware encoder for SDR and does not silently fall back. HDR hardware export is rejected pending its metadata contract.
5. NUT preserves fine PTS, then pinned FFmpeg muxes selected copy/AAC/no audio. Parent validates output and atomically moves to a new target; cancellation/failure cleans only this job's directory.

Canvas must match visible video and SAR must be square. Require supported opaque integer YUV and known strictly increasing presentation times. Negative PTS can fail NUT; no silent reset. Rotation/stereo/ICC/interlace/corruption/dynamic format/color and additional HDR semantics reject.

Complete stable PQ mastering is retained; partial/changing mastering rejects. Source MaxCLL/MaxFALL may become invalid after composition, so output uses unknown 0/0. HLG follows fixed conditions. Dolby Vision/HDR10+/HDR Vivid preservation is outside first-version support.

## HDR diagnostic, builds, and evidence

Optional macOS HDR uses Vulkan/MoltenVK, FP16 Linear Display P3, and CAMetalLayer EDR. Upload scales by sourceWhite/203; libplacebo maps for headroom. That is nominal white, not a physical brightness measurement. Swapchain and offscreen GPU readback are verified separately. Windows D3D11/scRGB display is deferred; see [native HDR](native-hdr.md).

Lock FFmpeg 9.0.2 and manifest library versions, synchronized Skia/HarfBuzz, and independent HDR dependencies. Managed/Decoder/Audio/Export/Workbench targets have distinct scopes: [building](building.md).

Recorded tests cover color/ABI, real SDR/PQ/HLG worker outputs, highlights/alpha, metadata, VFR rational time, audio hashes, cancel/nonoverwrite, and SRT→save/load→Save As→worker output. Device/UI/platform scopes are independently recorded; do not infer Windows acceptance from macOS evidence.

`publish.ps1` creates 0.1.0 self-contained packages with runtime/worker/tools/native closure, licenses, and hashes. macOS paths are rewritten/ad-hoc signed and actual OS minimum recorded (current local package: 27). Windows main/worker/native are x64 and have run under Parallels ARM64 emulation. Notarization/installers/Windows code signing are outside development packaging. See [publishing](publishing.md) and dated checkpoints.
