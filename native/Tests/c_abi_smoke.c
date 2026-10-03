#include "aeginext_hdr.h"
#include <stddef.h>

_Static_assert(sizeof(an_hdr_status) == 72, "an_hdr_status layout changed");
_Static_assert(sizeof(an_hdr_verification) == 32, "an_hdr_verification layout changed");
_Static_assert(offsetof(an_hdr_status, submitted_frames) == 48, "submitted_frames offset changed");

uint32_t aeginext_c_abi_smoke(void)
{
    return an_hdr_abi_version();
}
