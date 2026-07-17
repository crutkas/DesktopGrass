#pragma once

#include "DisplayTopology.h"

#include <vector>

namespace desktopgrass::topology {

bool TryCaptureWin32Topology(std::vector<MonitorSnapshot>& monitors) noexcept;

} // namespace desktopgrass::topology
