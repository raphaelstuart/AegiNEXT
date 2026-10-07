#pragma once
#include <atomic>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <exception>
#include <functional>
#include <mutex>
#include <thread>
#include <vector>

namespace aeginext::encode
{
class FrameRowExecutor final
{
public:
    explicit FrameRowExecutor(size_t threads);
    ~FrameRowExecutor();
    FrameRowExecutor(const FrameRowExecutor &) = delete;
    FrameRowExecutor &operator=(const FrameRowExecutor &) = delete;
    void Execute(size_t rows, const std::function<void(size_t, size_t)> &action);
private:
    void Work();
    void RunRows();
    void Stop();
    std::mutex mutex_;
    std::condition_variable ready_;
    std::condition_variable finished_;
    std::vector<std::thread> workers_;
    std::function<void(size_t, size_t)> action_;
    std::exception_ptr error_;
    std::atomic<size_t> nextRow_{0};
    std::atomic<bool> aborted_{false};
    size_t rows_ = 0;
    size_t pending_ = 0;
    uint64_t generation_ = 0;
    bool stopping_ = false;
};
}
