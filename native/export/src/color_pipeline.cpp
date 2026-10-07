#include "color_pipeline.h"
#include <cmath>
#include <algorithm>
#include <stdexcept>
namespace aeginext::encode
{
namespace
{
using Matrix = std::array<Color, 3>;
Color Multiply(const Matrix &m, Color v)
{
    Color out{};
    for (int y = 0; y < 3; ++y)
        for (int x = 0; x < 3; ++x) out[y] += m[y][x] * v[x];
    return out;
}
Matrix Inverse(const Matrix &m)
{
    const auto d = m[0][0] * (m[1][1]*m[2][2]-m[1][2]*m[2][1]) - m[0][1] * (m[1][0]*m[2][2]-m[1][2]*m[2][0]) + m[0][2] * (m[1][0]*m[2][1]-m[1][1]*m[2][0]);
    if (!std::isfinite(d) || std::abs(d) < 1e-12) throw std::invalid_argument("Degenerate color primaries");
    Matrix out{};
    for (int y = 0; y < 3; ++y)
        for (int x = 0; x < 3; ++x)
            out[y][x] = (m[(x+1)%3][(y+1)%3]*m[(x+2)%3][(y+2)%3]-m[(x+1)%3][(y+2)%3]*m[(x+2)%3][(y+1)%3])/d;
    return out;
}
Matrix Primaries(AVColorPrimaries id)
{
    const auto *p = av_csp_primaries_desc_from_id(id);
    if (!p) throw std::invalid_argument("Unsupported color primaries");
    Matrix m{};
    const AVCIExy xy[3]{p->prim.r,p->prim.g,p->prim.b};
    for (int c = 0; c < 3; ++c)
    {
        const auto x = av_q2d(xy[c].x), y = av_q2d(xy[c].y);
        m[0][c] = x/y; m[1][c] = 1; m[2][c] = (1-x-y)/y;
    }
    const auto x = av_q2d(p->wp.x), y = av_q2d(p->wp.y);
    const auto scale = Multiply(Inverse(m), {x/y,1,(1-x-y)/y});
    for (int r = 0; r < 3; ++r)
        for (int c = 0; c < 3; ++c) m[r][c] *= scale[c];
    return m;
}
}
ColorPipeline::ColorPipeline(AVColorSpace matrix, AVColorPrimaries primaries, AVColorTransferCharacteristic transfer)
{
    const auto *coeff = av_csp_luma_coeffs_from_avcsp(matrix);
    if (!coeff || (matrix != AVCOL_SPC_BT709 && matrix != AVCOL_SPC_BT2020_NCL && matrix != AVCOL_SPC_BT470BG && matrix != AVCOL_SPC_SMPTE170M))
        throw std::invalid_argument("Explicit supported non-constant-luminance YUV matrix is required");
    if (primaries != AVCOL_PRI_BT709 && primaries != AVCOL_PRI_BT2020 &&
        primaries != AVCOL_PRI_BT470BG && primaries != AVCOL_PRI_SMPTE170M)
        throw std::invalid_argument("Export requires supported BT601, BT709 or BT2020 primaries");
    if (transfer != AVCOL_TRC_BT709 && transfer != AVCOL_TRC_SMPTE170M && transfer != AVCOL_TRC_IEC61966_2_1 && transfer != AVCOL_TRC_SMPTE2084 && transfer != AVCOL_TRC_ARIB_STD_B67)
        throw std::invalid_argument("Export requires explicit SDR, PQ or HLG transfer");
    if ((transfer == AVCOL_TRC_SMPTE2084 || transfer == AVCOL_TRC_ARIB_STD_B67) && primaries != AVCOL_PRI_BT2020)
        throw std::invalid_argument("HDR export requires BT2020 primaries");
    kr_ = av_q2d(coeff->cr); kg_ = av_q2d(coeff->cg); kb_ = av_q2d(coeff->cb);
    pq_ = transfer == AVCOL_TRC_SMPTE2084;
    displayWhite_ = transfer == AVCOL_TRC_ARIB_STD_B67 ? 1000 : transfer == AVCOL_TRC_SMPTE2084 ? 10000 : 203;
    decode_ = av_csp_itu_eotf(transfer); encode_ = av_csp_itu_eotf_inv(transfer);
    if (!decode_ || !encode_) throw std::invalid_argument("No official EOTF exists for source transfer");
    const auto source = Primaries(AVCOL_PRI_BT709), destination = Inverse(Primaries(primaries));
    layerMatrix_ = {};
    for (int r = 0; r < 3; ++r)
        for (int c = 0; c < 3; ++c)
            for (int k = 0; k < 3; ++k) layerMatrix_[r][c] += destination[r][k] * source[k][c];
}
Color ColorPipeline::Decode(Color yuv) const
{
    Color rgb{yuv[0]+2*(1-kr_)*yuv[2], yuv[0]-2*kb_*(1-kb_)/kg_*yuv[1]-2*kr_*(1-kr_)/kg_*yuv[2], yuv[0]+2*(1-kb_)*yuv[1]};
    if (pq_)
        for (auto &component : rgb) component = std::clamp(component, 0.0, 1.0);
    decode_(displayWhite_, 0, rgb.data());
    return rgb;
}
Color ColorPipeline::Encode(Color nits) const
{
    encode_(displayWhite_, 0, nits.data());
    const auto y = kr_*nits[0]+kg_*nits[1]+kb_*nits[2];
    return {y,(nits[2]-y)/(2*(1-kb_)),(nits[0]-y)/(2*(1-kr_))};
}
Color ColorPipeline::Composite(Color backgroundYuv, const std::array<float, 4> &premultiplied, double referenceWhite) const
{
    for (const auto value : premultiplied)
        if (!std::isfinite(value)) throw std::invalid_argument("Non-finite subtitle sample");
    const double alpha = premultiplied[3];
    if (alpha < 0 || alpha > 1 || !std::isfinite(referenceWhite) || referenceWhite <= 0)
        throw std::invalid_argument("Invalid subtitle opacity or reference white");
    if (alpha == 0) return backgroundYuv;
    auto backdrop = Decode(backgroundYuv);
    const auto layer = Multiply(layerMatrix_, {premultiplied[0],premultiplied[1],premultiplied[2]});
    for (int c = 0; c < 3; ++c) backdrop[c] = backdrop[c]*(1-alpha)+layer[c]*referenceWhite;
    return Encode(backdrop);
}
PreparedForeground ColorPipeline::PrepareForeground(const std::array<float, 4> &premultiplied, double referenceWhite) const
{
    for (const auto value : premultiplied)
        if (!std::isfinite(value)) throw std::invalid_argument("Non-finite subtitle sample");
    const double alpha = premultiplied[3];
    if (alpha < 0 || alpha > 1 || !std::isfinite(referenceWhite) || referenceWhite <= 0)
        throw std::invalid_argument("Invalid subtitle opacity or reference white");
    return {Multiply(layerMatrix_, {premultiplied[0],premultiplied[1],premultiplied[2]}), alpha};
}
Color ColorPipeline::CompositePrepared(Color backgroundYuv, const PreparedForeground &foreground, double referenceWhite) const
{
    const double alpha = foreground.alpha;
    if (alpha == 0) return backgroundYuv;
    auto backdrop = Decode(backgroundYuv);
    const auto &layer = foreground.linear;
    for (int c = 0; c < 3; ++c) backdrop[c] = backdrop[c]*(1-alpha)+layer[c]*referenceWhite;
    return Encode(backdrop);
}
}
