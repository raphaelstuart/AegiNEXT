#include "decode_prefetcher.h"
#include <algorithm>
#include <utility>

namespace aeginext::encode
{
DecodePrefetcher::DecodePrefetcher(std::function<ExportDecodedFrame()> producer, std::function<void()> interrupt)
    : producer_(std::move(producer)), interrupt_(std::move(interrupt))
{
    if (!producer_ || !interrupt_)
    {
        throw std::invalid_argument("Decode prefetch requires a producer and an interrupt handler");
    }
    worker_ = std::thread([this]() { Work(); });
}
DecodePrefetcher::~DecodePrefetcher()
{
    Stop();
}
void DecodePrefetcher::Work()
{
    try
    {
        while (true)
        {
            {
                std::unique_lock lock(mutex_);
                space_.wait(lock, [this]() { return stopping_ || !queued_; });
                if (stopping_)
                {
                    finished_ = true;
                    return;
                }
                statistics_.peakInFlight = 1;
                statistics_.peakPrefetched = 1;
            }
            auto item = producer_();
            {
                std::scoped_lock lock(mutex_);
                if (stopping_ || !item.frame)
                {
                    finished_ = true;
                    available_.notify_all();
                    return;
                }
                queued_.emplace(std::move(item));
                ++statistics_.produced;
                statistics_.peakQueued = 1;
                available_.notify_one();
            }
        }
    }
    catch (...)
    {
        std::scoped_lock lock(mutex_);
        error_ = std::current_exception();
        finished_ = true;
        available_.notify_all();
    }
}
ExportDecodedFrame DecodePrefetcher::Next()
{
    std::unique_lock lock(mutex_);
    available_.wait(lock, [this]() { return stopping_ || queued_ || finished_; });
    if (stopping_)
    {
        throw aeginext::media::CoreError(aeginext::media::ErrorCode::Cancelled, "Export decode prefetch stopped");
    }
    if (queued_)
    {
        auto item = std::move(*queued_);
        queued_.reset();
        ++statistics_.consumed;
        lock.unlock();
        space_.notify_one();
        return item;
    }
    if (error_)
    {
        std::rethrow_exception(error_);
    }
    return {};
}
void DecodePrefetcher::RequestStop() noexcept
{
    auto interrupt = false;
    std::optional<ExportDecodedFrame> discarded;
    {
        std::scoped_lock lock(mutex_);
        interrupt = !stopping_ && !finished_;
        stopping_ = true;
        discarded = std::move(queued_);
        queued_.reset();
    }
    available_.notify_all();
    space_.notify_all();
    if (interrupt)
    {
        interrupt_();
    }
}
void DecodePrefetcher::Stop()
{
    RequestStop();
    if (worker_.joinable())
    {
        worker_.join();
    }
}
DecodePrefetchStatistics DecodePrefetcher::Statistics() const
{
    std::scoped_lock lock(mutex_);
    return statistics_;
}
}
