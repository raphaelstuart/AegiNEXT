#include "frame_row_executor.h"
#include <algorithm>

namespace aeginext::encode
{
namespace
{
constexpr size_t ROW_CHUNK = 32;
constexpr size_t MAX_THREADS = 4;
}
FrameRowExecutor::FrameRowExecutor(size_t threads)
{
    const auto available = std::max(1U, std::thread::hardware_concurrency());
    const auto count = std::clamp<size_t>(threads, 1, std::min<size_t>(available, MAX_THREADS));
    workers_.reserve(count - 1);
    try
    {
        for (size_t index = 1; index < count; ++index)
        {
            workers_.emplace_back([this]() { Work(); });
        }
    }
    catch (...)
    {
        Stop();
        throw;
    }
}
FrameRowExecutor::~FrameRowExecutor()
{
    Stop();
}
void FrameRowExecutor::Stop()
{
    {
        std::scoped_lock lock(mutex_);
        stopping_ = true;
    }
    ready_.notify_all();
    for (auto &worker : workers_)
    {
        worker.join();
    }
}
void FrameRowExecutor::Execute(size_t rows, const std::function<void(size_t, size_t)> &action)
{
    if (workers_.empty() || rows <= ROW_CHUNK)
    {
        for (size_t begin = 0; begin < rows; begin += ROW_CHUNK)
        {
            action(begin, std::min(begin + ROW_CHUNK, rows));
        }
        return;
    }
    {
        std::scoped_lock lock(mutex_);
        action_ = action;
        rows_ = rows;
        nextRow_.store(0, std::memory_order_relaxed);
        aborted_.store(false, std::memory_order_relaxed);
        error_ = nullptr;
        pending_ = workers_.size();
        ++generation_;
    }
    ready_.notify_all();
    RunRows();
    std::exception_ptr error;
    {
        std::unique_lock lock(mutex_);
        finished_.wait(lock, [this]() { return pending_ == 0; });
        error = error_;
        action_ = {};
    }
    if (error)
    {
        std::rethrow_exception(error);
    }
}
void FrameRowExecutor::Work()
{
    uint64_t observed = 0;
    while (true)
    {
        std::unique_lock lock(mutex_);
        ready_.wait(lock, [this, observed]() { return stopping_ || generation_ != observed; });
        if (stopping_)
        {
            return;
        }
        observed = generation_;
        lock.unlock();
        RunRows();
        lock.lock();
        --pending_;
        if (pending_ == 0)
        {
            finished_.notify_one();
        }
    }
}
void FrameRowExecutor::RunRows()
{
    try
    {
        while (!aborted_.load(std::memory_order_relaxed))
        {
            const auto begin = nextRow_.fetch_add(ROW_CHUNK, std::memory_order_relaxed);
            if (begin >= rows_)
            {
                return;
            }
            action_(begin, std::min(begin + ROW_CHUNK, rows_));
        }
    }
    catch (...)
    {
        std::scoped_lock lock(mutex_);
        if (!error_)
        {
            error_ = std::current_exception();
        }
        aborted_.store(true, std::memory_order_relaxed);
    }
}
}
