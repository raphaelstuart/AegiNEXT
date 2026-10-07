#pragma once
#include <array>

namespace aeginext::encode
{
struct PreparedForeground final
{
    std::array<double, 3> linear;
    double alpha;
};
}
