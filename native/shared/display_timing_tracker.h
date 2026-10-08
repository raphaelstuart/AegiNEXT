#pragma once
#include "display_timing.h"
#include <limits>
extern "C"
{
#include <libavutil/frame.h>
#include <libavutil/mathematics.h>
}
namespace aeginext::media
{
class DisplayTimingTracker final
{
public:
    void Reset(AVRational streamTimeBase, int64_t streamStart, AVRational averageRate, AVRational nominalRate)
    {
        streamTimeBase_ = streamTimeBase;
        streamStart_ = streamStart;
        framePeriod_ = Valid(averageRate) && Valid(nominalRate) && av_cmp_q(averageRate, nominalRate) == 0
            ? av_inv_q(averageRate) : AVRational{};
        previous_ = {};
        previousDuration_ = 0;
        previousDurationBase_ = {};
    }
    DisplayTiming Read(const AVFrame &frame)
    {
        const auto frameBase = Valid(frame.time_base) ? frame.time_base : streamTimeBase_;
        DisplayTiming result;
        if (frame.pts != AV_NOPTS_VALUE)
        {
            result = {frame.pts, frameBase, DISPLAY_ORIGINAL_PTS};
        }
        else if (frame.best_effort_timestamp != AV_NOPTS_VALUE)
        {
            result = {frame.best_effort_timestamp, streamTimeBase_, DISPLAY_BEST_EFFORT};
        }
        else if (previous_.value != AV_NOPTS_VALUE)
        {
            const auto useDuration = previousDuration_ > 0 && Valid(previousDurationBase_);
            const auto period = useDuration ? previousDurationBase_ : framePeriod_;
            if (Valid(period))
            {
                const auto increment = useDuration ? previousDuration_ : 1;
                const auto maximumDelta = av_rescale_q_rnd(increment, period, previous_.timeBase, AV_ROUND_UP);
                if (maximumDelta > 0 && previous_.value <= std::numeric_limits<int64_t>::max() - maximumDelta)
                {
                    const auto value = av_add_stable(previous_.timeBase, previous_.value, period, increment);
                    if (value != AV_NOPTS_VALUE && value > previous_.value)
                    {
                        result = {value, previous_.timeBase, previous_.evidence |
                            (useDuration ? DISPLAY_PREVIOUS_DURATION : DISPLAY_DECLARED_FRAME_RATE)};
                    }
                }
            }
        }
        else if (streamStart_ != AV_NOPTS_VALUE)
        {
            result = {streamStart_, streamTimeBase_, DISPLAY_STREAM_START};
        }
        streamStart_ = AV_NOPTS_VALUE;
        previous_ = result;
        previousDuration_ = frame.duration;
        previousDurationBase_ = frameBase;
        return result;
    }
private:
    static bool Valid(AVRational value) noexcept { return value.num > 0 && value.den > 0; }
    AVRational streamTimeBase_{};
    AVRational framePeriod_{};
    int64_t streamStart_ = AV_NOPTS_VALUE;
    DisplayTiming previous_{};
    int64_t previousDuration_ = 0;
    AVRational previousDurationBase_{};
};
}
