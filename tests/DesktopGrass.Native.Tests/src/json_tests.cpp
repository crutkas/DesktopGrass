#include "TestHelpers.h"
#include "Json.h"

#include <string>
#include <string_view>

using namespace desktopgrass;
using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace {

std::string nested_array(std::size_t depth) {
    return std::string(depth, '[') + "0" + std::string(depth, ']');
}

std::string nested_object(std::size_t depth) {
    std::string text;
    for (std::size_t i = 0; i < depth; ++i) {
        text += R"({"value":)";
    }
    text += "0";
    text.append(depth, '}');
    return text;
}

} // namespace

namespace DesktopGrassNativeTests
{
TEST_CLASS(JsonTests)
{
public:
TEST_METHOD(NestingAtLimitIsAccepted) {
    json::Value value;
    Assert::IsTrue(json::Parse(nested_array(json::kMaxNestingDepth), value));
    Assert::IsTrue(json::Parse(nested_object(json::kMaxNestingDepth), value));
}

TEST_METHOD(NestingBeyondLimitIsRejected) {
    json::Value value;
    Assert::IsFalse(json::Parse(nested_array(json::kMaxNestingDepth + 1), value));
    Assert::IsFalse(json::Parse(nested_object(json::kMaxNestingDepth + 1), value));
}

TEST_METHOD(BmpUnicodeEscapesDecodeToUtf8) {
    json::Value value;
    Assert::IsTrue(json::Parse(R"({"value":"\u0041\u00DF\u6771"})", value));

    const auto decoded = json::ReadString(value, "value");
    Assert::IsTrue(decoded.has_value());
    Assert::IsTrue(*decoded == std::string("A\xC3\x9F\xE6\x9D\xB1"));
}

TEST_METHOD(SurrogatePairDecodesToUtf8) {
    json::Value value;
    Assert::IsTrue(json::Parse(R"({"value":"\uD83D\uDE03"})", value));

    const auto decoded = json::ReadString(value, "value");
    Assert::IsTrue(decoded.has_value());
    Assert::IsTrue(*decoded == std::string("\xF0\x9F\x98\x83"));
}

TEST_METHOD(MalformedUnicodeEscapesAreRejected) {
    constexpr std::string_view malformed[] = {
        R"("\u123")",
        R"("\u12G4")",
        R"("\uD83D")",
        R"("\uD83Dtext")",
        R"("\uD83D\u0041")",
        R"("\uD83D\uDE0G")",
        R"("\uDE03")",
    };

    for (const std::string_view text : malformed) {
        json::Value value;
        Assert::IsFalse(json::Parse(text, value));
    }
}

TEST_METHOD(JsoncCommentsAndTrailingCommasRemainCompatible) {
    constexpr std::string_view text = R"(
        {
            // Existing configuration comments remain valid.
            "items": [
                "\u0041",
            ],
            /* Object trailing commas remain valid. */
        }
    )";

    json::Value value;
    Assert::IsTrue(json::Parse(text, value));
    const json::Value* items = json::FindMember(value, "items");
    Assert::IsTrue(items != nullptr);
    Assert::IsTrue(items->type == json::Value::Type::Array);
    Assert::IsTrue(items->arrayValue.size() == 1);
    Assert::IsTrue(items->arrayValue[0].stringValue == "A");
}
};
}
