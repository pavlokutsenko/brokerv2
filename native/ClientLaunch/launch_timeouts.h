#pragma once

// Keep these aligned with PriceCheck.Windows.LaunchTimeouts.
namespace launch_timeouts {
constexpr unsigned agent_ready_ms = 60000;
constexpr unsigned heartbeat_ms = 15000;
constexpr unsigned network_ms = 30000;
constexpr unsigned relay_accept_ms = 60000;
constexpr unsigned server_selection_ms = 60000;
constexpr unsigned character_selection_ms = 90000;
}
