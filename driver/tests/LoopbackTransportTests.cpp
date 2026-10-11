#include "../src/EndpointsCommon/AudioFlowLoopback.h"
#include "../src/EndpointsCommon/AudioFlowTiming.h"
#include <cstdio>
#include <cstdlib>
#include <vector>
#include <algorithm>

static AudioFlowLoopback mixer; // Avoid a large stack frame in tests too.
static unsigned checks = 0;
static void Check(bool ok, const char* name)
{
    ++checks;
    if (!ok) { std::fprintf(stderr, "FAIL: %s\n", name); std::exit(1); }
}
static std::vector<short> Read(unsigned long long frame, unsigned frames)
{
    std::vector<short> result(frames * 2, 12345);
    mixer.Read(frame, result.data(), frames);
    return result;
}
static bool Silent(const std::vector<short>& data)
{
    return std::all_of(data.begin(), data.end(), [](short s) { return s == 0; });
}

int main()
{
    // With a 10 ms period, a callback at 55 ms reports five completed packets.
    // The old single subtraction leaves 45 ms debt, causing duplicate events
    // at 56/57/58/59 ms even though no additional packet has completed.
    constexpr unsigned long long period = 100000;
    auto remainder = AudioFlowNotificationRemainder(550000 - period, period);
    Check(remainder == 50000, "late notification retains only sub-period remainder");
    unsigned events = 0;
    for (unsigned tick = 56; tick <= 60; ++tick)
    {
        remainder += 10000;
        if (remainder >= period)
        {
            ++events;
            Check(tick == 60, "no duplicate packet notifications after delayed DPC");
            remainder = AudioFlowNotificationRemainder(remainder - period, period);
        }
    }
    Check(events == 1 && remainder == 0, "normal notification phase resumes after late DPC");
    Check(AudioFlowNotificationRemainder(0, period) == 0, "exact period has no carry");
    Check(AudioFlowNotificationRemainder(123456789, period) == 56789, "long stalls have bounded notification carry");
    mixer.Initialize();
    Check(Silent(Read(100000, 200)), "no render stream returns silence");
    const auto a = mixer.Acquire();
    const auto b = mixer.Acquire();
    const short stereo[] = {1234, -5678, 2345, -6789, 0, 0};
    mixer.Write(a, 100000, stereo, 3);
    Check(Read(100000, 3) == std::vector<short>(stereo, stereo + 6), "stereo samples preserved exactly");
    Check(Read(100000, 3) == Read(100000, 3), "capture clients do not consume shared data");
    Check(Silent(Read(99999, 1)) && Silent(Read(100003, 100)), "underflow is zero, not replayed audio");
    const short added[] = {2000, 3000};
    mixer.Write(b, 100001, added, 1);
    Check(Read(100001, 1) == std::vector<short>({4345, -3789}), "streams mixed on common timeline");
    const short loud[] = {30000, -30000};
    mixer.Write(a, 200000, loud, 1);
    mixer.Write(b, 200000, loud, 1);
    Check(Read(200000, 1) == std::vector<short>({32767, -32768}), "mix saturates without integer wrap");
    mixer.Release(b);
    Check(Read(200000, 1) == std::vector<short>({30000, -30000}), "closing source removes its mix");
    mixer.SetMuted(true);
    mixer.Write(a, 200001, stereo, 3);
    Check(Silent(Read(200000, 4)), "protected output is muted");
    mixer.SetMuted(false);
    Check(Silent(Read(200000, 4)), "unmuting cannot expose protected history");
    mixer.Write(a, 300000, stereo, 3);
    mixer.Reset(a);
    Check(Silent(Read(300000, 3)), "restart forgets prior audio");
    std::vector<short> ramp((AudioFlowLoopback::CapacityFrames + 100) * 2);
    for (unsigned i = 0; i < ramp.size(); ++i) ramp[i] = static_cast<short>((i * 19) % 20000 - 10000);
    const unsigned frames = static_cast<unsigned>(ramp.size() / 2);
    mixer.Write(a, 400000, ramp.data(), frames);
    Check(Silent(Read(400000, 100)), "overflow discards overwritten frames");
    Check(Read(400100, AudioFlowLoopback::CapacityFrames) == std::vector<short>(ramp.begin() + 200, ramp.end()),
        "ring wrap preserves the entire retained interval");
    mixer.Write(a, 500000, stereo, 3);
    Check(Silent(Read(400100, 3)), "timeline gap cannot return stale buffer contents");
    mixer.Write(a, 499000, stereo, 3);
    Check(Silent(Read(500000, 3)), "rewind invalidates old timeline");

    // A late render DPC may skip frames that are no longer in the DMA ring.
    // Capture reads 30 ms behind render: a forward gap must not erase the
    // still-valid audio preceding it, otherwise a short miss becomes a longer
    // dropout. Only the missing interval should become silence.
    mixer.Initialize();
    const auto gapSource = mixer.Acquire();
    std::vector<short> beforeGap(480 * 2, 1234);
    std::vector<short> afterGap(480 * 2, -2345);
    mixer.Write(gapSource, 550000, beforeGap.data(), 480);
    mixer.Write(gapSource, 550528, afterGap.data(), 480);
    Check(Read(550000, 480) == beforeGap, "forward gap preserves delayed capture history");
    Check(Silent(Read(550480, 48)), "missing render interval is silent");
    Check(Read(550528, 480) == afterGap, "audio after a gap is preserved");

    // Repeated one-millisecond gaps on the common clock, with delayed capture.
    // Exercise wrap-around and silence stale ring values instead of replaying them.
    mixer.Initialize();
    const auto repeatedGapSource = mixer.Acquire();
    constexpr unsigned long long gapEpoch = 700000;
    for (unsigned packet = 0; packet < 1000; ++packet)
    {
        std::vector<short> signal(480 * 2, static_cast<short>(1000 + packet));
        mixer.Write(repeatedGapSource, gapEpoch + packet * 528, signal.data(), 480);
        if (packet >= 3)
        {
            const auto earlier = packet - 3;
            auto history = Read(gapEpoch + earlier * 528, 528);
            Check(std::all_of(history.begin(), history.begin() + 960,
                [earlier](short value) { return value == static_cast<short>(1000 + earlier); }),
                "repeated gaps preserve earlier delayed audio across ring wraps");
            Check(std::all_of(history.begin() + 960, history.end(), [](short value) { return value == 0; }),
                "repeated gaps silence exactly the missing frames across ring wraps");
        }
    }
    mixer.Initialize();
    for (unsigned i = 0; i < AudioFlowLoopback::SourceCount; ++i)
        Check(mixer.Acquire() == i, "all render slots available");
    Check(mixer.Acquire() == AudioFlowLoopback::InvalidSource, "source exhaustion bounded");
    mixer.Release(4);
    Check(mixer.Acquire() == 4 && Silent(Read(499000, 3)), "released slots start empty");

    // Emulate render and capture DPCs with different start times and packet sizes.
    // Render finishes 10 ms blocks; capture reads 7 ms blocks 30 ms behind QPC.
    mixer.Initialize();
    const auto source = mixer.Acquire();
    constexpr unsigned long long epoch = 600000;
    unsigned rendered = 0, captured = 0;
    for (unsigned tick = 1; tick < 3000; ++tick)
    {
        if (tick % 10 == 0)
        {
            std::vector<short> packet(480 * 2);
            for (unsigned i = 0; i < 480; ++i)
            {
                packet[i * 2] = static_cast<short>((rendered + i) % 20000 - 10000);
                packet[i * 2 + 1] = static_cast<short>(-(packet[i * 2]));
            }
            mixer.Write(source, epoch + rendered, packet.data(), 480);
            rendered += 480;
        }
        if (tick >= 37 && (tick - 37) % 7 == 0)
        {
            auto data = Read(epoch + captured, 336);
            bool exact = true;
            for (unsigned i = 0; i < 336; ++i)
            {
                const auto expected = static_cast<short>((captured + i) % 20000 - 10000);
                exact &= data[i * 2] == expected && data[i * 2 + 1] == -expected;
            }
            Check(exact, "independent DPC schedules preserve continuous stereo audio");
            captured += 336;
        }
    }
    std::printf("PASS: %u loopback transport checks\n", checks);
}
