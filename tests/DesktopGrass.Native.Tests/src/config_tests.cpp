#include "TestHelpers.h"
#include "Config.h"

#include <filesystem>
#include <fstream>
#include <sstream>
#include <string>

using namespace desktopgrass;

namespace {

std::filesystem::path test_config_path(const char* name) {
    std::filesystem::path dir = std::filesystem::current_path()
        / ".copilot-scratch"
        / "native-config-tests"
        / name;
    std::error_code ec;
    std::filesystem::remove_all(dir, ec);
    std::filesystem::create_directories(dir);
    return dir / "config.json";
}

void write_text(const std::filesystem::path& path, const std::string& text) {
    std::filesystem::create_directories(path.parent_path());
    std::ofstream file(path, std::ios::binary | std::ios::trunc);
    file << text;
}

std::string read_text(const std::filesystem::path& path) {
    std::ifstream file(path, std::ios::binary);
    std::ostringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

} // namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(ConfigTests)
{
public:
TEST_METHOD(ConfigMissingFileYieldsDefaultsAndWritesATemplate) {
    const std::filesystem::path path = test_config_path("missing");
    Assert::IsFalse(std::filesystem::exists(path));

    const config::Config cfg = config::LoadConfig(path.wstring());

    Assert::IsTrue(cfg.targetFps == config::kTargetFpsDefault);
    Assert::IsTrue(cfg.bladeDensity == Near(config::kBladeDensityDefault));

    // A default file should have been created and be re-readable (it is JSONC).
    Assert::IsTrue(std::filesystem::exists(path));
    const config::Config reread = config::LoadConfig(path.wstring());
    Assert::IsTrue(reread.targetFps == config::kTargetFpsDefault);
    Assert::IsTrue(reread.bladeDensity == Near(config::kBladeDensityDefault));
}

TEST_METHOD(ConfigValidValuesAreParsed) {
    const std::filesystem::path path = test_config_path("valid");
    write_text(path, "{ \"version\": 1, \"targetFps\": 60, \"bladeDensity\": 1.5 }");

    const config::Config cfg = config::LoadConfig(path.wstring());
    Assert::IsTrue(cfg.targetFps == 60);
    Assert::IsTrue(cfg.bladeDensity == Near(1.5));
}

TEST_METHOD(ConfigOutOfRangeValuesAreClamped) {
    const std::filesystem::path path = test_config_path("clamp");
    write_text(path, "{ \"targetFps\": 1000, \"bladeDensity\": 99.0 }");

    config::Config cfg = config::LoadConfig(path.wstring());
    Assert::IsTrue(cfg.targetFps == config::kTargetFpsMax);
    Assert::IsTrue(cfg.bladeDensity == Near(config::kBladeDensityMax));

    write_text(path, "{ \"targetFps\": 0, \"bladeDensity\": 0.0 }");
    cfg = config::LoadConfig(path.wstring());
    Assert::IsTrue(cfg.targetFps == config::kTargetFpsMin);
    Assert::IsTrue(cfg.bladeDensity == Near(config::kBladeDensityMin));
}

TEST_METHOD(ConfigOversizedIntegerValuesFallBackSafely) {
    const std::filesystem::path path = test_config_path("oversized-integer");

    write_text(path, "{ \"targetFps\": 1e300 }");
    config::Config cfg = config::LoadConfig(path.wstring());
    Assert::IsTrue(cfg.targetFps == config::kTargetFpsDefault);

    write_text(path, "{ \"targetFps\": -1e300 }");
    cfg = config::LoadConfig(path.wstring());
    Assert::IsTrue(cfg.targetFps == config::kTargetFpsDefault);
}

TEST_METHOD(ConfigJSONCCommentsAndTrailingCommasAreTolerated) {
    const std::filesystem::path path = test_config_path("jsonc");
    write_text(path,
        "{\n"
        "  // a comment\n"
        "  \"targetFps\": 24, /* inline */\n"
        "  \"bladeDensity\": 2.0,\n"  // trailing comma below
        "}\n");

    const config::Config cfg = config::LoadConfig(path.wstring());
    Assert::IsTrue(cfg.targetFps == 24);
    Assert::IsTrue(cfg.bladeDensity == Near(2.0));
}

TEST_METHOD(ConfigMalformedFileFallsBackToDefaultsAndIsPreserved) {
    const std::filesystem::path path = test_config_path("malformed");
    write_text(path, "{ not valid json ");

    const config::Config cfg = config::LoadConfig(path.wstring());
    Assert::IsTrue(cfg.targetFps == config::kTargetFpsDefault);
    Assert::IsTrue(cfg.bladeDensity == Near(config::kBladeDensityDefault));

    // The user's (broken) file must be left untouched for them to fix.
    Assert::IsTrue(read_text(path) == "{ not valid json ");
}

TEST_METHOD(ConfigMissingKeysFallBackToPerKeyDefaults) {
    const std::filesystem::path path = test_config_path("partial");
    write_text(path, "{ \"targetFps\": 45 }");

    const config::Config cfg = config::LoadConfig(path.wstring());
    Assert::IsTrue(cfg.targetFps == 45);
    Assert::IsTrue(cfg.bladeDensity == Near(config::kBladeDensityDefault));
    Assert::IsTrue(cfg.swaySpeed == Near(config::kSwaySpeedDefault));
    Assert::IsTrue(cfg.swayAmplitude == Near(config::kSwayAmplitudeDefault));
}

TEST_METHOD(ConfigKeysAreMatchedCaseInsensitively) {
    const std::filesystem::path path = test_config_path("case-insensitive");
    write_text(path,
        "{ \"TargetFps\": 60, \"BLADEDENSITY\": 1.5, "
        "\"SwaySpeed\": 0.5, \"swayamplitude\": 2.0 }");

    const config::Config cfg = config::LoadConfig(path.wstring());
    Assert::IsTrue(cfg.targetFps == 60);
    Assert::IsTrue(cfg.bladeDensity == Near(1.5));
    Assert::IsTrue(cfg.swaySpeed == Near(0.5));
    Assert::IsTrue(cfg.swayAmplitude == Near(2.0));
}

TEST_METHOD(ConfigSwayKnobsParseClampAndRejectNonFinite) {
    const std::filesystem::path path = test_config_path("sway");

    // Defaults when absent.
    write_text(path, "{ }");
    config::Config cfg = config::LoadConfig(path.wstring());
    Assert::IsTrue(cfg.swaySpeed == Near(config::kSwaySpeedDefault));
    Assert::IsTrue(cfg.swayAmplitude == Near(config::kSwayAmplitudeDefault));

    // Valid values parsed.
    write_text(path, "{ \"swaySpeed\": 0.5, \"swayAmplitude\": 2.0 }");
    cfg = config::LoadConfig(path.wstring());
    Assert::IsTrue(cfg.swaySpeed == Near(0.5));
    Assert::IsTrue(cfg.swayAmplitude == Near(2.0));

    // Out-of-range clamped to bounds.
    write_text(path, "{ \"swaySpeed\": 99.0, \"swayAmplitude\": -5.0 }");
    cfg = config::LoadConfig(path.wstring());
    Assert::IsTrue(cfg.swaySpeed == Near(config::kSwaySpeedMax));
    Assert::IsTrue(cfg.swayAmplitude == Near(config::kSwayAmplitudeMin));

    // Non-finite (inf from overflow) falls back to default, never poisons the sim.
    write_text(path, "{ \"swaySpeed\": 1e999, \"swayAmplitude\": 1e999 }");
    cfg = config::LoadConfig(path.wstring());
    Assert::IsTrue(cfg.swaySpeed == Near(config::kSwaySpeedDefault));
    Assert::IsTrue(cfg.swayAmplitude == Near(config::kSwayAmplitudeDefault));
}
};
}
