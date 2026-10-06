#pragma once
#include "aeginext_audio.h"
#include <memory>

class AudioOutput
{
public:
    virtual ~AudioOutput() = default;
    virtual void write(const float *samples, int frames) = 0;
    virtual void pause(bool paused) = 0;
    virtual void clear() = 0;
    virtual void gain(float gain) = 0;
    virtual an_audio_clock_snapshot snapshot() = 0;
    virtual int latency() const = 0;
};

std::unique_ptr<AudioOutput> create_system_audio_output();
