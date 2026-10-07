#include "decode_prefetcher.h"
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <functional>
#include <iostream>
#include <mutex>
#include <stdexcept>
#include <thread>

using aeginext::encode::DecodePrefetcher;
using aeginext::encode::ExportDecodedFrame;
using aeginext::media::CoreError;
using aeginext::media::ErrorCode;
using aeginext::media::FramePointer;

namespace
{
void Require(bool condition, const char *message)
{
    if (!condition)
    {
        throw std::runtime_error(message);
    }
}
ExportDecodedFrame Frame(int index)
{
    FramePointer frame(av_frame_alloc());
    Require(frame != nullptr, "Cannot allocate prefetch fixture");
    frame->format = AV_PIX_FMT_YUV420P;
    frame->width = 8;
    frame->height = 8;
    Require(av_frame_get_buffer(frame.get(), 32) == 0, "Cannot allocate prefetch fixture samples");
    frame->data[0][0] = static_cast<uint8_t>(index);
    frame->pts = 1000 + index * 41;
    frame->duration = index % 2 ? 41 : 59;
    frame->time_base = {1, 1000};
    return {std::move(frame), {1, 1000}, static_cast<uint32_t>(index)};
}
void Await(const std::function<bool()> &condition, const char *message)
{
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(3);
    while (!condition())
    {
        Require(std::chrono::steady_clock::now() < deadline, message);
        std::this_thread::yield();
    }
}

void SingleOwnerOrderAndEof()
{
    const auto caller = std::this_thread::get_id();
    std::thread::id owner;
    int next = 0;
    DecodePrefetcher prefetch([&]()
    {
        if (owner == std::thread::id{}) owner = std::this_thread::get_id();
        Require(owner == std::this_thread::get_id() && owner != caller, "Producer did not have a single exclusive worker");
        return next == 61 ? ExportDecodedFrame{} : Frame(next++);
    }, []() noexcept {});
    for (int index = 0; index < 61; ++index)
    {
        auto item = prefetch.Next();
        Require(item.frame && item.frame->pts == 1000 + index * 41 && item.frame->data[0][0] == index &&
            item.frame->duration == (index % 2 ? 41 : 59) && item.timeBase.num == 1 && item.timeBase.den == 1000 &&
            item.inferredFields == static_cast<uint32_t>(index), "Queued frame ownership, order or immutable facts changed");
    }
    Require(!prefetch.Next().frame && !prefetch.Next().frame, "EOF was not stable or drained in order");
    prefetch.Stop();
    const auto statistics = prefetch.Statistics();
    Require(statistics.capacity == 1 && statistics.produced == 61 && statistics.consumed == 61 &&
        statistics.peakQueued == 1 && statistics.peakInFlight == 1 && statistics.peakPrefetched == 1,
        "Prefetch exceeded its one-frame queued/inflight budget");
}

void FullSlotBlocksReadBeforeAllocatingAnotherFrame()
{
    std::atomic<int> reads{0};
    std::atomic<int> interrupted{0};
    DecodePrefetcher prefetch([&]() { return Frame(reads.fetch_add(1)); }, [&]() noexcept { ++interrupted; });
    Await([&]() { return prefetch.Statistics().peakQueued == 1; }, "Producer never queued its first frame");
    prefetch.Stop();
    Require(reads.load() == 1 && interrupted.load() == 1, "Full slot allowed a second read or failed to stop its producer");
    Require(prefetch.Statistics().consumed == 0, "Discarded frame was reported as consumed");
}

void EmptyConsumerAndBlockedReadWakeOnCancellation()
{
    std::mutex mutex;
    std::condition_variable changed;
    auto entered = false;
    auto interrupted = false;
    std::exception_ptr consumerError;
    DecodePrefetcher prefetch([&]() -> ExportDecodedFrame
    {
        std::unique_lock lock(mutex);
        entered = true;
        changed.notify_all();
        changed.wait(lock, [&]() { return interrupted; });
        throw CoreError(ErrorCode::Cancelled, "controlled blocked read");
    }, [&]() noexcept
    {
        std::scoped_lock lock(mutex);
        interrupted = true;
        changed.notify_all();
    });
    std::thread consumer([&]()
    {
        try
        {
            prefetch.Next();
        }
        catch (...)
        {
            consumerError = std::current_exception();
        }
    });
    {
        std::unique_lock lock(mutex);
        Require(changed.wait_for(lock, std::chrono::seconds(3), [&]() { return entered; }), "Producer did not enter blocking read");
    }
    prefetch.RequestStop();
    prefetch.Stop();
    consumer.join();
    Require(consumerError != nullptr, "Blocked consumer returned a partial frame after cancellation");
    try
    {
        std::rethrow_exception(consumerError);
    }
    catch (const CoreError &error)
    {
        Require(error.Code() == ErrorCode::Cancelled, "Prefetch cancellation lost its classification");
    }
}

void ProducerFailureIsForwardedAfterEarlierFrame()
{
    int reads = 0;
    DecodePrefetcher prefetch([&]() -> ExportDecodedFrame
    {
        if (reads++ == 0) return Frame(7);
        throw CoreError(ErrorCode::Decode, "controlled decoder failure");
    }, []() noexcept {});
    Require(prefetch.Next().frame->data[0][0] == 7, "Producer failure lost an earlier successful frame");
    auto failed = false;
    try
    {
        prefetch.Next();
    }
    catch (const CoreError &error)
    {
        failed = error.Code() == ErrorCode::Decode && std::string(error.what()) == "controlled decoder failure";
    }
    Require(failed, "Producer exception was converted to EOF or changed its classification");
    prefetch.Stop();
}

void ConsumerFailureJoinsAndReleasesRefcountedFrames()
{
    auto source = Frame(9);
    auto *extra = av_buffer_ref(source.frame->buf[0]);
    Require(extra != nullptr, "Cannot retain source fixture buffer");
    auto interrupted = false;
    try
    {
        DecodePrefetcher prefetch([&]()
        {
            FramePointer clone(av_frame_clone(source.frame.get()));
            Require(clone != nullptr, "Cannot clone refcounted frame");
            return ExportDecodedFrame{std::move(clone), source.timeBase, source.inferredFields};
        }, [&]() noexcept { interrupted = true; });
        auto item = prefetch.Next();
        Require(item.frame->data[0][0] == 9, "Consumer did not receive its refcounted view");
        throw std::runtime_error("controlled renderer failure");
    }
    catch (const std::runtime_error &error)
    {
        Require(std::string(error.what()) == "controlled renderer failure", "Prefetch destructor replaced the consumer error");
    }
    Require(interrupted && av_buffer_get_ref_count(source.frame->buf[0]) == 2,
        "Consumer failure did not join its producer and release prefetched buffers");
    av_buffer_unref(&extra);
}

void EmptySourceAndFreshInstanceAfterCancellation()
{
    for (int attempt = 0; attempt < 3; ++attempt)
    {
        int reads = 0;
        DecodePrefetcher cancelled([&]() { return Frame(reads++); }, []() noexcept {});
        cancelled.RequestStop();
        auto rejected = false;
        try
        {
            cancelled.Next();
        }
        catch (const CoreError &error)
        {
            rejected = error.Code() == ErrorCode::Cancelled;
        }
        Require(rejected, "Stopped prefetch published a queued frame");
        cancelled.Stop();
        DecodePrefetcher empty([]() { return ExportDecodedFrame{}; }, []() noexcept {});
        Require(!empty.Next().frame, "Fresh prefetch inherited a cancelled instance's state");
        empty.Stop();
        Require(!empty.Statistics().produced && !empty.Statistics().consumed, "Empty producer invented frames");
    }
}
}

int main()
{
    try
    {
        SingleOwnerOrderAndEof();
        FullSlotBlocksReadBeforeAllocatingAnotherFrame();
        EmptyConsumerAndBlockedReadWakeOnCancellation();
        ProducerFailureIsForwardedAfterEarlierFrame();
        ConsumerFailureJoinsAndReleasesRefcountedFrames();
        EmptySourceAndFreshInstanceAfterCancellation();
        std::cout << "PASS bounded decode prefetch: exclusive producer/order/EOF, full and empty waits, failure/cancel wake, refcounts/join/recovery\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
