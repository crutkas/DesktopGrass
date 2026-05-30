#include "../third_party/catch2/catch.hpp"
#include "Constants.h"
#include "Persistence.h"
#include "Sim.h"

#include <filesystem>
#include <fstream>

using namespace desktopgrass;

namespace {

Sim make_sim(Scene scene = Scene::Winter) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, 1.0);
    sim_set_scene(sim, scene);
    return sim;
}

std::filesystem::path snow_state_path(const char* name) {
    std::filesystem::path dir = std::filesystem::current_path()
        / ".copilot-scratch"
        / "native-snow-accumulation-tests"
        / name;
    std::error_code ec;
    std::filesystem::remove_all(dir, ec);
    std::filesystem::create_directories(dir);
    return dir / "state.json";
}

void write_text(const std::filesystem::path& path, const std::string& text) {
    std::filesystem::create_directories(path.parent_path());
    std::ofstream file(path, std::ios::binary | std::ios::trunc);
    file << text;
}

} // namespace

TEST_CASE("snow accumulation constants are pinned", "[snow][accumulation]") {
    REQUIRE(SNOW_ACCUMULATION_RATE == Approx(0.012).margin(1e-12));
    REQUIRE(SNOW_DEPTH_MAX == Approx(30.0).margin(1e-12));
    REQUIRE(SNOW_DEPTH_MIN_RENDER == Approx(0.3).margin(1e-12));
    REQUIRE(SNOW_LAYER_COLOR_TOP == 0xFFFFFFFFu);
    REQUIRE(SNOW_LAYER_COLOR_BOTTOM == 0xFFE8E8F0u);
    REQUIRE(SNOW_LAYER_HIGHLIGHT == 0xFFFFFFFFu);
    REQUIRE(SNOW_TOP_UNDULATION_AMP == Approx(2.5).margin(1e-12));
    REQUIRE(SNOW_TOP_UNDULATION_WAVELENGTH == Approx(90.0).margin(1e-12));
    REQUIRE(SNOW_TOP_UNDULATION_PHASE_SALT == 0x5E0A1ull);
}

TEST_CASE("fresh Winter sim starts with no snow accumulation", "[snow][accumulation]") {
    Sim sim = make_sim(Scene::Winter);
    REQUIRE(sim.snowDepth == Approx(0.0).margin(1e-12));
}

TEST_CASE("Winter snow accumulation follows pinned rate", "[snow][accumulation]") {
    Sim sim = make_sim(Scene::Winter);
    sim_tick(sim, 100.0, nullptr, 0);
    REQUIRE(sim.snowDepth == Approx(1.2).margin(1e-9));
}

TEST_CASE("Winter snow accumulation clamps at max depth", "[snow][accumulation]") {
    Sim sim = make_sim(Scene::Winter);
    const double enough = SNOW_DEPTH_MAX / SNOW_ACCUMULATION_RATE + 1000.0;
    sim_tick(sim, enough, nullptr, 0);
    REQUIRE(sim.snowDepth == Approx(SNOW_DEPTH_MAX).margin(1e-9));
}

TEST_CASE("switching away from Winter resets snow accumulation", "[snow][accumulation]") {
    Sim sim = make_sim(Scene::Winter);
    sim_set_snow_depth(sim, 15.0);

    sim_set_scene(sim, Scene::Grass);
    REQUIRE(sim.snowDepth == Approx(0.0).margin(1e-12));

    sim_set_scene(sim, Scene::Winter);
    REQUIRE(sim.snowDepth == Approx(0.0).margin(1e-12));
}

TEST_CASE("Desert scene never accumulates snow", "[snow][accumulation]") {
    Sim sim = make_sim(Scene::Desert);
    sim_tick(sim, 10000.0, nullptr, 0);
    REQUIRE(sim.snowDepth == Approx(0.0).margin(1e-12));
}

TEST_CASE("Grass scene never accumulates snow", "[snow][accumulation]") {
    Sim sim = make_sim(Scene::Grass);
    sim_tick(sim, 10000.0, nullptr, 0);
    REQUIRE(sim.snowDepth == Approx(0.0).margin(1e-12));
}

TEST_CASE("snow accumulation persists through v2 state round trip", "[snow][accumulation][persistence]") {
    const auto path = snow_state_path("v2-round-trip");
    persistence::SetStateFilePathForTest(path.wstring());

    Sim running = make_sim(Scene::Winter);
    sim_set_snow_depth(running, 18.0);

    persistence::AppState state;
    state.scene = Scene::Winter;
    persistence::MonitorState monitor;
    monitor.width = 1920;
    monitor.height = 1080;
    monitor.left = 0;
    monitor.top = 0;
    monitor.snowDepth = running.snowDepth;
    state.monitors.push_back(monitor);

    REQUIRE(persistence::SaveAppState(state));

    persistence::AppState loaded;
    REQUIRE(persistence::LoadAppState(loaded));
    REQUIRE(loaded.version == 2);
    REQUIRE(loaded.monitors.size() == 1);

    Sim fresh = sim_init(CANONICAL_TEST_SEED, 1920.0, 1.0);
    sim_set_scene(fresh, loaded.scene);
    sim_set_snow_depth(fresh, loaded.monitors[0].snowDepth);
    REQUIRE(fresh.snowDepth == Approx(18.0).margin(1e-9));
}

TEST_CASE("v1 state files load with zero snow accumulation", "[snow][accumulation][persistence]") {
    const auto path = snow_state_path("v1-backward-compat");
    persistence::SetStateFilePathForTest(path.wstring());
    write_text(path,
        "{\n"
        "  \"version\": 1,\n"
        "  \"scene\": \"Winter\",\n"
        "  \"autoStart\": true,\n"
        "  \"monitors\": {\n"
        "    \"1920x1080@0,0\": { \"cuts\": [] }\n"
        "  }\n"
        "}\n");

    persistence::AppState loaded;
    REQUIRE(persistence::LoadAppState(loaded));
    REQUIRE(loaded.version == 2);
    REQUIRE(loaded.monitors.size() == 1);
    REQUIRE(loaded.monitors[0].snowDepth == Approx(0.0).margin(1e-12));
}

TEST_CASE("snow top edge undulation remains bounded above ground", "[snow][accumulation]") {
    Sim sim = make_sim(Scene::Winter);
    sim_set_snow_depth(sim, 12.0);

    for (double x = 0.0; x <= 1920.0; x += 17.0) {
        const double top = snow_top_y_at(sim, x);
        REQUIRE(top >= sim.windowHeight - sim.snowDepth - SNOW_TOP_UNDULATION_AMP - 1e-9);
        REQUIRE(top <= sim.windowHeight - sim.snowDepth + SNOW_TOP_UNDULATION_AMP + 1e-9);
        REQUIRE(top <= sim.windowHeight + 1e-9);
    }
}

TEST_CASE("snowflakes despawn when they touch accumulated snow", "[snow][accumulation]") {
    Sim sim = make_sim(Scene::Winter);
    sim_set_snow_depth(sim, 10.0);
    const double x = 100.0;

    Entity flake{};
    flake.kind = EntityKind::Snowflake;
    flake.x = x;
    flake.y = snow_top_y_at(sim, x);
    flake.lifetime = 100.0;
    sim.entities.clear();
    sim.entities.push_back(flake);

    sim_tick_entities(sim, 0.0);
    REQUIRE(sim.entities.empty());
}

TEST_CASE("snow depth exposes tree base burial offset", "[snow][accumulation]") {
    Sim sim = make_sim(Scene::Winter);
    sim_set_snow_depth(sim, 15.0);
    REQUIRE(snow_tree_base_y_offset(sim) == Approx(12.5).margin(1e-12));

    sim_set_snow_depth(sim, 1.0);
    REQUIRE(snow_tree_base_y_offset(sim) == Approx(0.0).margin(1e-12));
}

TEST_CASE("snow depth identity snapshot is deterministic across implementations", "[snow][accumulation]") {
    Sim sim = make_sim(Scene::Winter);
    sim_tick(sim, 1234.5, nullptr, 0);
    REQUIRE(sim.snowDepth == Approx(14.814).margin(1e-9));
}
