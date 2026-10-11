// AudioFlow's fixed-format render mixer. No allocation, floating point, or OS calls.
// The miniport serializes every operation with its loopback spin lock.
#pragma once

class AudioFlowLoopback
{
public:
    using uint32_t = unsigned int;
    using uint64_t = unsigned long long;
    using int16_t = short;
    using int32_t = int;
    static_assert(sizeof(int16_t) == 2 && sizeof(int32_t) == 4 && sizeof(uint64_t) == 8, "PCM types");
    static constexpr uint32_t SampleRate = 48000;
    static constexpr uint32_t Channels = 2;
    static constexpr uint32_t FrameBytes = 4;
    static constexpr uint32_t SourceCount = 9; // Six host and three offload pins.
    static constexpr uint32_t CapacityFrames = 8192;
    static constexpr uint32_t DelayFrames = 1440; // 30 ms for independently scheduled DPCs.
    static constexpr uint32_t InvalidSource = 0xFFFFFFFFu;

    void Initialize()
    {
        muted = false;
        for (auto& source : sources)
        {
            source.occupied = false;
            source.first = source.end = 0;
        }
    }

    uint32_t Acquire()
    {
        for (uint32_t i = 0; i < SourceCount; ++i)
        {
            if (!sources[i].occupied)
            {
                sources[i].occupied = true;
                Reset(i);
                return i;
            }
        }
        return InvalidSource;
    }

    void Reset(uint32_t id)
    {
        if (id < SourceCount) sources[id].first = sources[id].end = 0;
    }

    void Release(uint32_t id)
    {
        if (id < SourceCount)
        {
            Reset(id);
            sources[id].occupied = false;
        }
    }

    void SetMuted(bool value)
    {
        // Forget protected history on both transitions, including before unmuting.
        if (value != muted)
            for (uint32_t i = 0; i < SourceCount; ++i) Reset(i);
        muted = value;
    }

    void Write(uint32_t id, uint64_t frame, const int16_t* samples, uint32_t frames)
    {
        if (muted || id >= SourceCount || !sources[id].occupied || frames == 0) return;
        auto& source = sources[id];
        // Preserve history that delayed capture has not read yet. A forward
        // gap silences only the missing frames; it must not erase the earlier
        // valid interval. Rewinds and gaps beyond ring capacity start fresh.
        if (frame < source.end || frame - source.end >= CapacityFrames)
            source.first = source.end = frame;
        else if (frame > source.end)
        {
            for (uint64_t missing = source.end; missing < frame; ++missing)
            {
                const auto offset = static_cast<uint32_t>(missing % CapacityFrames) * Channels;
                source.samples[offset] = source.samples[offset + 1] = 0;
            }
        }
        if (frames > CapacityFrames)
        {
            const auto skip = frames - CapacityFrames;
            frame += skip;
            samples += skip * Channels;
            frames = CapacityFrames;
            source.first = frame;
        }
        for (uint32_t i = 0; i < frames; ++i)
        {
            const auto offset = static_cast<uint32_t>((frame + i) % CapacityFrames) * Channels;
            source.samples[offset] = samples[i * Channels];
            source.samples[offset + 1] = samples[i * Channels + 1];
        }
        source.end = frame + frames;
        if (source.end - source.first > CapacityFrames) source.first = source.end - CapacityFrames;
    }

    void Read(uint64_t frame, int16_t* samples, uint32_t frames) const
    {
        // Reading never consumes data: multiple capture clients see the same mix.
        for (uint32_t i = 0; i < frames; ++i)
        {
            int32_t left = 0, right = 0;
            const auto current = frame + i;
            const auto offset = static_cast<uint32_t>(current % CapacityFrames) * Channels;
            if (!muted)
                for (const auto& source : sources)
                    if (source.occupied && current >= source.first && current < source.end)
                    {
                        left += source.samples[offset];
                        right += source.samples[offset + 1];
                    }
            samples[i * Channels] = Saturate(left);
            samples[i * Channels + 1] = Saturate(right);
        }
    }

private:
    static int16_t Saturate(int32_t value)
    {
        return static_cast<int16_t>(value > 32767 ? 32767 : value < -32768 ? -32768 : value);
    }
    struct Source
    {
        bool occupied;
        uint64_t first, end;
        int16_t samples[CapacityFrames * Channels];
    };
    bool muted;
    Source sources[SourceCount];
};
