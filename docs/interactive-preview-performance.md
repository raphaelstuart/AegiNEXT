# Interactive preview CPU performance

[English](interactive-preview-performance.md) | [简体中文](zh-CN/interactive-preview-performance.md)

Measured 2026-10-05 on macOS ARM64. Each group has 32 targets; cold-start sample retained separately, percentiles use the other 31. Raw samples/PTS: artifacts/verification/interaction-performance.json.

Real decode/SDR conversion used a 3840×2160 project and four naturally sized animated subtitles. The baseline references the retained pre-change AegiNextWorkspacePreview.app renderer; the same driver also references current source. Timings include CPU decode/conversion/composition, excluding UI upload/display refresh/input latency.

| Media / strategy | Decode p95 | Conversion p95 | Composition p95 | Total p50 | Total p95 |
|---|---:|---:|---:|---:|---:|
| Source, before | 2.8 ms | 13.6 ms | 34.9 ms | 47.8 ms | 50.6 ms |
| Source, precise | 2.9 ms | 13.4 ms | 9.3 ms | 23.8 ms | 25.2 ms |
| Source, interactive | 3.5 ms | 11.2 ms | 5.7 ms | 17.6 ms | 19.2 ms |
| 4K VFR long GOP, before | 316.6 ms | 35.7 ms | 35.1 ms | 305.3 ms | 384.8 ms |
| 4K VFR long GOP, after | 298.2 ms | 35.1 ms | 9.5 ms | 274.8 ms | 340.6 ms |
| 4K VFR long GOP, interactive | 301.5 ms | 33.3 ms | 5.7 ms | 271.5 ms | 338.5 ms |

Stress media was transcoded from the same source: 3840×2160, eight seconds, one I-frame, nonuniform frame intervals, alternating targets between four/seven seconds. Original FFmpeg log and samples are retained.

GOP redecoding dominates this case; latest background is not proven real-time. Interactive scheduling composites current target subtitles/effects on recent background using one active request plus one replaceable pending target. At measurement time release restored a 1280×720 cap. The later selectable-quality change defaults to 960×540 and restores the user's selected cap on release; **these historical timings have not been remeasured for every quality**. CPU gains and actual touchpad/display smoothness are separate acceptance scopes.

Cancellation occurs at safe stages; canceled caches require complete regeneration. Old request sequence/scene revision/evaluation time/endpoint identity cannot replace current results. Scoped scheduler/pixel/cancellation recovery tests were executed for the recorded milestone.
