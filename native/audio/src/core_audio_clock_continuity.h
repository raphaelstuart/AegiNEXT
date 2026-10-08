#pragma once
#include "core_audio_clock_observation.h"
#include <cstdint>

class CoreAudioClockContinuity
{
public:
    CoreAudioClockObservation observe(int64_t sample_frame, bool discontinuity, bool route_matches, bool within_startup_grace)
    {
        if (!route_matches)
        {
            return CoreAudioClockObservation::INVALID;
        }
        if (awaiting_start && sample_frame <= last_frame)
        {
            return within_startup_grace ? CoreAudioClockObservation::WAITING : CoreAudioClockObservation::INVALID;
        }
        if (observed && sample_frame < last_frame)
        {
            return CoreAudioClockObservation::INVALID;
        }
        observed = true;
        last_frame = sample_frame;
        awaiting_start = false;
        if (discontinuity)
        {
            ++discontinuities;
        }
        return CoreAudioClockObservation::ACCEPTED;
    }

    bool can_wait_for_timestamp(bool queue_not_running, bool paused) const
    {
        return queue_not_running && (paused || awaiting_start);
    }

    void begin_run()
    {
        awaiting_start = true;
    }

    void reset()
    {
        observed = false;
        last_frame = 0;
        awaiting_start = true;
        discontinuities = 0;
    }

private:
    bool observed = false;
    bool awaiting_start = true;
    int64_t last_frame = 0;
    uint64_t discontinuities = 0;
};
