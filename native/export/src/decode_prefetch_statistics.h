#pragma once
#include <cstdint>

namespace aeginext::encode
{
struct DecodePrefetchStatistics final
{
    uint64_t capacity = 1;
    uint64_t produced = 0;
    uint64_t consumed = 0;
    uint64_t peakQueued = 0;
    uint64_t peakInFlight = 0;
    uint64_t peakPrefetched = 0;
};
}
