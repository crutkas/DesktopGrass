// GraphicsDeviceRecovery.cpp

#include "GraphicsDeviceRecovery.h"

namespace desktopgrass {

bool RecoverSharedGraphicsDevices(
    SharedGraphicsDevices& shared,
    const std::vector<Renderer*>& renderers,
    bool replaceSharedGraph)
{
    if (replaceSharedGraph) {
        // Every renderer's per-window resources reference the graph that is
        // about to disappear. Drop them all before replacing it so nothing
        // keeps drawing against (or releasing into) a device we're about to
        // invalidate.
        for (Renderer* renderer : renderers) {
            if (renderer) {
                renderer->DiscardPerWindowResources();
            }
        }

        shared.Discard();
        if (!shared.Initialize()) {
            return false;
        }

        for (Renderer* renderer : renderers) {
            if (renderer) {
                renderer->AcknowledgeSharedDeviceRecovery();
            }
        }
    }

    bool allOk = true;
    for (Renderer* renderer : renderers) {
        if (!renderer
            || (!renderer->IsGraphicsReady()
                && !renderer->RebuildPerWindowResources())) {
            allOk = false;
        }
    }

    // Shared recovery is a batch operation. If any dependent window failed
    // to rebuild, roll every renderer back together. A later retry can then
    // rebuild the isolated per-window resources without replacing the now
    // healthy shared graph again.
    if (replaceSharedGraph && !allOk) {
        for (Renderer* renderer : renderers) {
            if (renderer) {
                renderer->DiscardPerWindowResources();
            }
        }
    }
    return allOk;
}

} // namespace desktopgrass
