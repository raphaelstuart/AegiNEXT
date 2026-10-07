#pragma once
#include "export_stage.h"
#include <array>
#include <chrono>
#include <cstdint>

namespace aeginext::encode
{
class ExportPerformance final
{
public:
    ExportPerformance();
    template<class Action>
    auto Measure(ExportStage stage, Action &&action)
    {
        if (!enabled_)
        {
            return action();
        }
        const auto start = std::chrono::steady_clock::now();
        auto result = action();
        nanoseconds_[static_cast<size_t>(stage)] += static_cast<uint64_t>(
            std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - start).count());
        return result;
    }
    void Report(uint64_t frames, uint64_t decodeNanoseconds, uint64_t downloadNanoseconds) const;
private:
    bool enabled_ = false;
    std::chrono::steady_clock::time_point start_;
    std::array<uint64_t, static_cast<size_t>(ExportStage::Count)> nanoseconds_{};
};
}
