// MouseHook.h
//
// WH_MOUSE_LL global low-level mouse hook. Windows dispatches the callback to
// the thread that installed it, and the callback must return very quickly. It
// pushes a fixed-size snapshot of the event into a bounded ring buffer. The
// render loop drains the queue once per frame.

#pragma once

#include <atomic>
#include <cstddef>
#include <cstdint>

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include "Sim.h"

namespace desktopgrass {

struct RawMouseEvent {
    EventType type;
    double    timeSeconds;
    int32_t   screenX; // virtual screen coords, raw from the hook
    int32_t   screenY;
};

class MouseEventQueue {
public:
    static constexpr std::size_t CAPACITY = 1024; // power of two

    MouseEventQueue() : head_(0), tail_(0) {}

    // Producer side (low-level hook thread). Returns false if full (we drop the
    // event rather than block — UI freezes are worse than a missed gust).
    bool push(const RawMouseEvent& e) noexcept {
        const std::size_t head = head_.load(std::memory_order_relaxed);
        const std::size_t next = (head + 1) & (CAPACITY - 1);
        if (next == tail_.load(std::memory_order_acquire)) {
            return false; // full
        }
        buffer_[head] = e;
        head_.store(next, std::memory_order_release);
        return true;
    }

    // Consumer side (render thread). Returns the number of events read.
    std::size_t drain(RawMouseEvent* dst, std::size_t maxCount) noexcept {
        std::size_t n = 0;
        std::size_t tail = tail_.load(std::memory_order_relaxed);
        const std::size_t head = head_.load(std::memory_order_acquire);
        while (tail != head && n < maxCount) {
            dst[n++] = buffer_[tail];
            buffer_[tail] = {};
            tail = (tail + 1) & (CAPACITY - 1);
        }
        tail_.store(tail, std::memory_order_release);
        return n;
    }

    void clear() noexcept {
        std::size_t tail = tail_.load(std::memory_order_relaxed);
        const std::size_t head = head_.load(std::memory_order_acquire);
        while (tail != head) {
            buffer_[tail] = {};
            tail = (tail + 1) & (CAPACITY - 1);
        }
        tail_.store(head, std::memory_order_release);
    }

private:
    RawMouseEvent            buffer_[CAPACITY]{};
    std::atomic<std::size_t> head_; // producer
    std::atomic<std::size_t> tail_; // consumer
};

struct MouseHookPlatform {
    HHOOK (*install)(HOOKPROC callback) noexcept;
    BOOL (*uninstall)(HHOOK hook) noexcept;
};

class MouseHookRegistration {
public:
    MouseHookRegistration() noexcept;
    explicit MouseHookRegistration(MouseHookPlatform platform) noexcept;
    ~MouseHookRegistration();

    MouseHookRegistration(const MouseHookRegistration&) = delete;
    MouseHookRegistration& operator=(const MouseHookRegistration&) = delete;

    bool Install(MouseEventQueue* queue) noexcept;
    void Reset() noexcept;
    bool IsInstalled() const noexcept { return hook_ != nullptr; }

private:
    MouseHookPlatform platform_;
    HHOOK hook_ = nullptr;
};

} // namespace desktopgrass
