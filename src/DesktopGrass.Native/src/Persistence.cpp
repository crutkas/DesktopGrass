#include "Persistence.h"

#include <Windows.h>

#include <algorithm>
#include <chrono>
#include <cctype>
#include <cstdio>
#include <cstdlib>
#include <ctime>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <map>
#include <optional>
#include <sstream>
#include <string_view>
#include <utility>

namespace desktopgrass::persistence {
namespace {

std::optional<std::wstring> g_stateFilePathForTest;

struct JsonValue {
    enum class Type { Null, Bool, Number, String, Array, Object };

    Type type = Type::Null;
    bool boolValue = false;
    double numberValue = 0.0;
    std::string stringValue;
    std::vector<JsonValue> arrayValue;
    std::map<std::string, JsonValue> objectValue;
};

class JsonParser {
public:
    explicit JsonParser(std::string_view text) : text_(text) {}

    bool Parse(JsonValue& out) {
        SkipWhitespace();
        if (!ParseValue(out)) {
            return false;
        }
        SkipWhitespace();
        return pos_ == text_.size();
    }

private:
    void SkipWhitespace() noexcept {
        while (pos_ < text_.size()) {
            const unsigned char c = static_cast<unsigned char>(text_[pos_]);
            if (!std::isspace(c)) break;
            ++pos_;
        }
    }

    bool Match(std::string_view literal) noexcept {
        if (text_.substr(pos_, literal.size()) != literal) {
            return false;
        }
        pos_ += literal.size();
        return true;
    }

    bool ParseValue(JsonValue& out) {
        SkipWhitespace();
        if (pos_ >= text_.size()) return false;

        const char c = text_[pos_];
        if (c == '{') return ParseObject(out);
        if (c == '[') return ParseArray(out);
        if (c == '"') {
            out.type = JsonValue::Type::String;
            return ParseString(out.stringValue);
        }
        if (c == 't') {
            if (!Match("true")) return false;
            out.type = JsonValue::Type::Bool;
            out.boolValue = true;
            return true;
        }
        if (c == 'f') {
            if (!Match("false")) return false;
            out.type = JsonValue::Type::Bool;
            out.boolValue = false;
            return true;
        }
        if (c == 'n') {
            if (!Match("null")) return false;
            out.type = JsonValue::Type::Null;
            return true;
        }
        if (c == '-' || (c >= '0' && c <= '9')) {
            return ParseNumber(out);
        }
        return false;
    }

    bool ParseObject(JsonValue& out) {
        if (text_[pos_] != '{') return false;
        ++pos_;
        out.type = JsonValue::Type::Object;
        out.objectValue.clear();

        SkipWhitespace();
        if (pos_ < text_.size() && text_[pos_] == '}') {
            ++pos_;
            return true;
        }

        while (pos_ < text_.size()) {
            SkipWhitespace();
            std::string key;
            if (!ParseString(key)) return false;
            SkipWhitespace();
            if (pos_ >= text_.size() || text_[pos_] != ':') return false;
            ++pos_;

            JsonValue value;
            if (!ParseValue(value)) return false;
            out.objectValue.emplace(std::move(key), std::move(value));

            SkipWhitespace();
            if (pos_ >= text_.size()) return false;
            if (text_[pos_] == '}') {
                ++pos_;
                return true;
            }
            if (text_[pos_] != ',') return false;
            ++pos_;
        }
        return false;
    }

    bool ParseArray(JsonValue& out) {
        if (text_[pos_] != '[') return false;
        ++pos_;
        out.type = JsonValue::Type::Array;
        out.arrayValue.clear();

        SkipWhitespace();
        if (pos_ < text_.size() && text_[pos_] == ']') {
            ++pos_;
            return true;
        }

        while (pos_ < text_.size()) {
            JsonValue value;
            if (!ParseValue(value)) return false;
            out.arrayValue.push_back(std::move(value));

            SkipWhitespace();
            if (pos_ >= text_.size()) return false;
            if (text_[pos_] == ']') {
                ++pos_;
                return true;
            }
            if (text_[pos_] != ',') return false;
            ++pos_;
        }
        return false;
    }

    bool ParseString(std::string& out) {
        if (pos_ >= text_.size() || text_[pos_] != '"') return false;
        ++pos_;
        out.clear();

        while (pos_ < text_.size()) {
            const char c = text_[pos_++];
            if (c == '"') return true;
            if (c == '\\') {
                if (pos_ >= text_.size()) return false;
                const char esc = text_[pos_++];
                switch (esc) {
                case '"': out.push_back('"'); break;
                case '\\': out.push_back('\\'); break;
                case '/': out.push_back('/'); break;
                case 'b': out.push_back('\b'); break;
                case 'f': out.push_back('\f'); break;
                case 'n': out.push_back('\n'); break;
                case 'r': out.push_back('\r'); break;
                case 't': out.push_back('\t'); break;
                case 'u':
                    if (pos_ + 4 > text_.size()) return false;
                    out.push_back('?');
                    pos_ += 4;
                    break;
                default:
                    return false;
                }
            } else {
                out.push_back(c);
            }
        }
        return false;
    }

    bool ParseNumber(JsonValue& out) {
        const std::size_t start = pos_;
        if (text_[pos_] == '-') ++pos_;
        if (pos_ >= text_.size()) return false;

        if (text_[pos_] == '0') {
            ++pos_;
        } else if (text_[pos_] >= '1' && text_[pos_] <= '9') {
            while (pos_ < text_.size() && text_[pos_] >= '0' && text_[pos_] <= '9') {
                ++pos_;
            }
        } else {
            return false;
        }

        if (pos_ < text_.size() && text_[pos_] == '.') {
            ++pos_;
            if (pos_ >= text_.size() || text_[pos_] < '0' || text_[pos_] > '9') return false;
            while (pos_ < text_.size() && text_[pos_] >= '0' && text_[pos_] <= '9') {
                ++pos_;
            }
        }

        if (pos_ < text_.size() && (text_[pos_] == 'e' || text_[pos_] == 'E')) {
            ++pos_;
            if (pos_ < text_.size() && (text_[pos_] == '+' || text_[pos_] == '-')) ++pos_;
            if (pos_ >= text_.size() || text_[pos_] < '0' || text_[pos_] > '9') return false;
            while (pos_ < text_.size() && text_[pos_] >= '0' && text_[pos_] <= '9') {
                ++pos_;
            }
        }

        const std::string token(text_.substr(start, pos_ - start));
        char* endPtr = nullptr;
        const double value = std::strtod(token.c_str(), &endPtr);
        if (endPtr == token.c_str() || *endPtr != '\0') return false;

        out.type = JsonValue::Type::Number;
        out.numberValue = value;
        return true;
    }

    std::string_view text_;
    std::size_t pos_ = 0;
};

const JsonValue* FindMember(const JsonValue& object, const std::string& name) {
    if (object.type != JsonValue::Type::Object) return nullptr;
    const auto it = object.objectValue.find(name);
    return it == object.objectValue.end() ? nullptr : &it->second;
}

std::optional<int> ReadInt(const JsonValue& object, const std::string& name) {
    const JsonValue* value = FindMember(object, name);
    if (!value || value->type != JsonValue::Type::Number) return std::nullopt;
    return static_cast<int>(value->numberValue);
}

std::optional<double> ReadDouble(const JsonValue& object, const std::string& name) {
    const JsonValue* value = FindMember(object, name);
    if (!value || value->type != JsonValue::Type::Number) return std::nullopt;
    return value->numberValue;
}

std::optional<bool> ReadBool(const JsonValue& object, const std::string& name) {
    const JsonValue* value = FindMember(object, name);
    if (!value || value->type != JsonValue::Type::Bool) return std::nullopt;
    return value->boolValue;
}

std::optional<std::string> ReadString(const JsonValue& object, const std::string& name) {
    const JsonValue* value = FindMember(object, name);
    if (!value || value->type != JsonValue::Type::String) return std::nullopt;
    return value->stringValue;
}

std::string JsonEscape(std::string_view text) {
    std::string out;
    for (char c : text) {
        switch (c) {
        case '"': out += "\\\""; break;
        case '\\': out += "\\\\"; break;
        case '\b': out += "\\b"; break;
        case '\f': out += "\\f"; break;
        case '\n': out += "\\n"; break;
        case '\r': out += "\\r"; break;
        case '\t': out += "\\t"; break;
        default:
            if (static_cast<unsigned char>(c) < 0x20) {
                char buffer[7]{};
                std::snprintf(buffer, sizeof(buffer), "\\u%04x", static_cast<unsigned char>(c));
                out += buffer;
            } else {
                out.push_back(c);
            }
            break;
        }
    }
    return out;
}

std::string SceneToString(Scene scene) noexcept {
    switch (scene) {
    case Scene::Grass:  return "Grass";
    case Scene::Desert: return "Desert";
    case Scene::Winter: return "Winter";
    }
    return "Grass";
}

Scene SceneFromString(const std::string& scene) noexcept {
    if (scene == "Desert") return Scene::Desert;
    if (scene == "Winter") return Scene::Winter;
    return Scene::Grass;
}

std::string CritterToString(CritterKind critter) noexcept {
    switch (critter) {
    case CritterKind::None:  return "None";
    case CritterKind::Sheep: return "Sheep";
    case CritterKind::Cat:   return "Cat";
    case CritterKind::Bunny: return "Bunny";
    }
    return "None";
}

CritterKind CritterFromString(const std::string& critter) noexcept {
    if (critter == "Sheep") return CritterKind::Sheep;
    if (critter == "Cat") return CritterKind::Cat;
    if (critter == "Bunny") return CritterKind::Bunny;
    return CritterKind::None;
}

std::string CurrentUtcTimestamp() {
    const auto now = std::chrono::system_clock::now();
    const std::time_t time = std::chrono::system_clock::to_time_t(now);
    std::tm utc{};
    gmtime_s(&utc, &time);

    std::ostringstream out;
    out << std::put_time(&utc, "%Y-%m-%dT%H:%M:%SZ");
    return out.str();
}

bool TryParseMonitorKey(const std::string& key, MonitorState& monitor) {
    int consumed = 0;
    const int matched = sscanf_s(key.c_str(), "%dx%d@%d,%d%n",
                                    &monitor.width,
                                    &monitor.height,
                                    &monitor.left,
                                    &monitor.top,
                                    &consumed);
    return matched == 4 && consumed == static_cast<int>(key.size());
}

std::string Serialize(const AppState& state) {
    std::ostringstream out;
    out << std::setprecision(17);
    out << "{\n";
    out << "  \"version\": 1,\n";
    out << "  \"savedAt\": \"" << CurrentUtcTimestamp() << "\",\n";
    out << "  \"scene\": \"" << SceneToString(state.scene) << "\",\n";
    out << "  \"critter\": \"" << CritterToString(state.critter) << "\",\n";
    out << "  \"critterCount\": " << state.critterCountOverride << ",\n";
    out << "  \"autoStart\": " << (state.autoStart ? "true" : "false") << ",\n";
    out << "  \"monitors\": {\n";

    for (std::size_t i = 0; i < state.monitors.size(); ++i) {
        const MonitorState& monitor = state.monitors[i];
        out << "    \"" << JsonEscape(MonitorKey(monitor)) << "\": {\n";
        out << "      \"cuts\": [";
        if (!monitor.cuts.empty()) {
            out << "\n";
            for (std::size_t j = 0; j < monitor.cuts.size(); ++j) {
                const CutRecord& cut = monitor.cuts[j];
                out << "        { \"bladeIndex\": " << cut.bladeIndex
                    << ", \"cutTime\": " << cut.cutTime << " }";
                if (j + 1 < monitor.cuts.size()) out << ",";
                out << "\n";
            }
            out << "      ";
        }
        out << "]\n";
        out << "    }";
        if (i + 1 < state.monitors.size()) out << ",";
        out << "\n";
    }

    out << "  }\n";
    out << "}\n";
    return out.str();
}

bool ParseAppState(const JsonValue& root, AppState& out) {
    if (root.type != JsonValue::Type::Object) return false;

    const int version = ReadInt(root, "version").value_or(0);
    if (version != 1) {
        OutputDebugStringA("DesktopGrass persistence: unsupported state.json version; starting fresh.\n");
        return false;
    }

    AppState parsed;
    parsed.version = 1;

    auto sceneName = ReadString(root, "scene");
    if (!sceneName) sceneName = ReadString(root, "currentScene");
    parsed.scene = SceneFromString(sceneName.value_or("Grass"));

    auto critterName = ReadString(root, "critter");
    if (!critterName) critterName = ReadString(root, "currentCritter");
    parsed.critter = CritterFromString(critterName.value_or("None"));

    auto critterCount = ReadInt(root, "critterCount");
    if (!critterCount) critterCount = ReadInt(root, "critterCountOverride");
    parsed.critterCountOverride = critterCount.value_or(0);
    if (parsed.critterCountOverride < 0 || parsed.critterCountOverride > PET_COUNT_MAX_PER_MONITOR) {
        parsed.critterCountOverride = 0;
    }
    parsed.autoStart = ReadBool(root, "autoStart").value_or(false);

    const JsonValue* monitors = FindMember(root, "monitors");
    if (monitors && monitors->type == JsonValue::Type::Object) {
        for (const auto& [key, value] : monitors->objectValue) {
            MonitorState monitor;
            if (!TryParseMonitorKey(key, monitor)) {
                continue;
            }

            const JsonValue* cuts = FindMember(value, "cuts");
            if (cuts && cuts->type == JsonValue::Type::Array) {
                for (const JsonValue& cutValue : cuts->arrayValue) {
                    if (cutValue.type != JsonValue::Type::Object) continue;
                    const auto bladeIndex = ReadInt(cutValue, "bladeIndex");
                    const auto cutTime = ReadDouble(cutValue, "cutTime");
                    if (!bladeIndex || !cutTime) continue;
                    monitor.cuts.push_back(CutRecord{ *bladeIndex, *cutTime });
                }
            }
            parsed.monitors.push_back(std::move(monitor));
        }
    }

    out = std::move(parsed);
    return true;
}

std::wstring DefaultStateFilePath() {
    wchar_t* localAppData = nullptr;
    std::size_t length = 0;
    _wdupenv_s(&localAppData, &length, L"LOCALAPPDATA");

    std::filesystem::path path = localAppData && length > 0
        ? std::filesystem::path(localAppData)
        : std::filesystem::current_path();

    if (localAppData) {
        std::free(localAppData);
    }

    path /= L"DesktopGrass";
    path /= L"state.json";
    return path.wstring();
}

} // namespace

std::string MonitorKey(int width, int height, int left, int top) {
    return std::to_string(width) + "x" + std::to_string(height) + "@"
         + std::to_string(left) + "," + std::to_string(top);
}

std::string MonitorKey(const MonitorState& monitor) {
    return MonitorKey(monitor.width, monitor.height, monitor.left, monitor.top);
}

std::wstring GetStateFilePath() {
    if (g_stateFilePathForTest) {
        return *g_stateFilePathForTest;
    }
    return DefaultStateFilePath();
}

void SetStateFilePathForTest(const std::wstring& path) {
    if (path.empty()) {
        g_stateFilePathForTest.reset();
    } else {
        g_stateFilePathForTest = path;
    }
}

bool LoadAppState(AppState& out) {
    const std::filesystem::path path(GetStateFilePath());
    std::ifstream file(path, std::ios::binary);
    if (!file) {
        return false;
    }

    std::ostringstream buffer;
    buffer << file.rdbuf();

    const std::string json = buffer.str();
    JsonValue root;
    JsonParser parser(json);
    if (!parser.Parse(root)) {
        OutputDebugStringA("DesktopGrass persistence: malformed state.json; starting fresh.\n");
        return false;
    }

    return ParseAppState(root, out);
}

bool SaveAppState(const AppState& state) {
    const std::filesystem::path path(GetStateFilePath());
    const std::filesystem::path directory = path.parent_path();
    if (!directory.empty()) {
        std::error_code ec;
        std::filesystem::create_directories(directory, ec);
        if (ec) return false;
    }

    const std::filesystem::path tempPath(path.wstring() + L".tmp");
    {
        std::ofstream file(tempPath, std::ios::binary | std::ios::trunc);
        if (!file) return false;
        const std::string json = Serialize(state);
        file.write(json.data(), static_cast<std::streamsize>(json.size()));
        if (!file) return false;
    }

    if (!MoveFileExW(tempPath.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        std::error_code ec;
        std::filesystem::remove(tempPath, ec);
        return false;
    }

    return true;
}

} // namespace desktopgrass::persistence
