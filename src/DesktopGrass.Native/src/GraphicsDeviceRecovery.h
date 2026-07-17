// GraphicsDeviceRecovery.h
//
// Coordinates process-wide recovery of a SharedGraphicsDevices graph across
// every Renderer that depends on it. A device-level failure detected by any
// one window means the shared graph is gone for all of them; recovering
// must replace it exactly once and rebuild every Renderer's per-window
// resources against the replacement before any of them draws again with
// stale COM references.
//
// Partial success — the shared graph comes back but a window fails to
// rebuild, or vice versa — is reported as failure so callers retry the
// whole batch together on the next attempt instead of leaving some windows
// stale while others recovered.

#pragma once

#include <vector>

#include "Renderer.h"
#include "SharedGraphicsDevices.h"

namespace desktopgrass {

// When `replaceSharedGraph` is true, discards every renderer, replaces the
// shared graph once, and rebuilds all renderers as one all-or-nothing batch.
// When false, retries only unready per-window resources and leaves healthy
// renderers and the shared graph untouched.
bool RecoverSharedGraphicsDevices(
    SharedGraphicsDevices& shared,
    const std::vector<Renderer*>& renderers,
    bool replaceSharedGraph);

} // namespace desktopgrass
