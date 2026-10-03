#ifndef AEGINEXT_TEXTURE_OWNER_H
#define AEGINEXT_TEXTURE_OWNER_H

#include "hdr_error.h"
#include <libplacebo/gpu.h>

namespace AegiNext
{
class TextureOwner final
{
public:
    TextureOwner(pl_gpu gpu, const pl_tex_params &parameters)
        : gpu(gpu), texture(pl_tex_create(gpu, &parameters))
    {
        if (!texture)
        {
            throw HdrError(AN_HDR_NATIVE_FAILURE, "libplacebo could not allocate a GPU texture.");
        }
    }

    ~TextureOwner()
    {
        pl_tex_destroy(gpu, &texture);
    }

    TextureOwner(const TextureOwner &) = delete;
    TextureOwner &operator=(const TextureOwner &) = delete;

    pl_tex Get() const noexcept
    {
        return texture;
    }

private:
    pl_gpu gpu;
    pl_tex texture;
};
}

#endif
