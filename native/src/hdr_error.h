#ifndef AEGINEXT_HDR_ERROR_H
#define AEGINEXT_HDR_ERROR_H

#include "aeginext_hdr.h"
#include <stdexcept>

namespace AegiNext
{
class HdrError final : public std::runtime_error
{
public:
    HdrError(an_hdr_result code, const char *message)
        : std::runtime_error(message), code(code)
    {
    }

    an_hdr_result Code() const noexcept
    {
        return code;
    }

private:
    an_hdr_result code;
};
}

#endif
