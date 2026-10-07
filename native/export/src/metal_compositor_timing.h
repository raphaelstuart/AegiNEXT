#pragma once

namespace aeginext::encode::experimental
{
/// <summary>Measures the complete CPU-visible Metal composition operation, including transfers and synchronization.</summary>
struct MetalCompositorTiming final
{
    double uploadMilliseconds = 0;
    double dispatchAndWaitMilliseconds = 0;
    double gpuMilliseconds = 0;
    double readbackMilliseconds = 0;
    double totalMilliseconds = 0;
};
}
