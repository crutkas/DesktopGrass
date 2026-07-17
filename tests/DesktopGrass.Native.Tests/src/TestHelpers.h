#pragma once

#pragma warning(push)
#pragma warning(disable : 26466)
#include "CppUnitTest.h"
#pragma warning(pop)

#include <algorithm>
#include <cmath>
#include <limits>
#include <type_traits>

using Microsoft::VisualStudio::CppUnitTestFramework::Assert;
using Microsoft::VisualStudio::CppUnitTestFramework::Logger;

class Near final
{
public:
    explicit constexpr Near(double expected) noexcept : expected_(expected)
    {
    }

    [[nodiscard]] constexpr Near margin(double value) const noexcept
    {
        Near result = *this;
        result.margin_ = value;
        return result;
    }

    [[nodiscard]] constexpr Near epsilon(double value) const noexcept
    {
        Near result = *this;
        result.epsilon_ = value;
        return result;
    }

    template <typename T, std::enable_if_t<std::is_arithmetic_v<T>, int> = 0>
    friend bool operator==(T actual, const Near &expected) noexcept
    {
        return expected.Matches(static_cast<double>(actual));
    }

    template <typename T, std::enable_if_t<std::is_arithmetic_v<T>, int> = 0>
    friend bool operator==(const Near &expected, T actual) noexcept
    {
        return actual == expected;
    }

    template <typename T, std::enable_if_t<std::is_arithmetic_v<T>, int> = 0>
    friend bool operator!=(T actual, const Near &expected) noexcept
    {
        return !(actual == expected);
    }

    template <typename T, std::enable_if_t<std::is_arithmetic_v<T>, int> = 0>
    friend bool operator!=(const Near &expected, T actual) noexcept
    {
        return !(actual == expected);
    }

private:
    [[nodiscard]] bool Matches(double actual) const noexcept
    {
        const double relativeTolerance =
            epsilon_ * (std::abs(expected_) + scale_);
        return std::abs(actual - expected_) <=
               (std::max)(margin_, relativeTolerance);
    }

    double expected_;
    double margin_ = 0.0;
    double epsilon_ = std::numeric_limits<float>::epsilon() * 100.0;
    double scale_ = 0.0;
};
