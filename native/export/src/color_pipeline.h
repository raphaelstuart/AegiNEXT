#pragma once
#include <array>
extern "C"
{
#include <libavutil/csp.h>
}
namespace aeginext::encode
{
using Color = std::array<double, 3>;
class ColorPipeline final
{
public:
    ColorPipeline(AVColorSpace matrix, AVColorPrimaries primaries, AVColorTransferCharacteristic transfer);
    Color Decode(Color yuv) const;
    Color Encode(Color nits) const;
    Color Composite(Color backgroundYuv, const std::array<float, 4> &premultiplied, double referenceWhite) const;
private:
    double kr_;
    double kb_;
    double kg_;
    double displayWhite_;
    bool pq_;
    av_csp_eotf_function decode_;
    av_csp_eotf_function encode_;
    std::array<Color, 3> layerMatrix_;
};
}
