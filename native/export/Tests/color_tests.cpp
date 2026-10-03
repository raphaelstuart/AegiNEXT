#include "color_pipeline.h"
#include <cmath>
#include <iostream>
#include <stdexcept>
using namespace aeginext::encode;
void Require(bool value, const char *message)
{
    if (!value) throw std::runtime_error(message);
}
int main()
{
    try
    {
        for (const auto trc : {AVCOL_TRC_BT709, AVCOL_TRC_IEC61966_2_1, AVCOL_TRC_SMPTE2084, AVCOL_TRC_ARIB_STD_B67})
        {
            const auto hdr = trc == AVCOL_TRC_SMPTE2084 || trc == AVCOL_TRC_ARIB_STD_B67;
            ColorPipeline colors(hdr ? AVCOL_SPC_BT2020_NCL : AVCOL_SPC_BT709,
                hdr ? AVCOL_PRI_BT2020 : AVCOL_PRI_BT709, trc);
            for (const double code : {0.05, 0.3, 0.6, 0.9})
            {
                const Color source{code, 0, 0};
                const auto roundtrip = colors.Encode(colors.Decode(source));
                Require(std::abs(roundtrip[0] - code) < 1e-8 && std::abs(roundtrip[1]) < 1e-8,
                    "Transfer/matrix roundtrip lost precision.");
                Require(colors.Composite(source, {0, 0, 0, 0}, 203) == source,
                    "Transparent subtitle changed background code values.");
            }
            const auto background = colors.Encode({100, 100, 100});
            const auto mixed = colors.Decode(colors.Composite(background, {0.5f, 0.5f, 0.5f, 0.5f}, 203));
            Require(std::abs(mixed[0] - 151.5) < 0.001 && std::abs(mixed[2] - 151.5) < 0.001,
                "Premultiplied subtitle was not composited in absolute linear light.");
        }
        ColorPipeline pq(AVCOL_SPC_BT2020_NCL, AVCOL_PRI_BT2020, AVCOL_TRC_SMPTE2084);
        for (const double nits : {203.0, 1000.0, 4000.0, 10000.0})
        {
            const auto encoded = pq.Encode({nits, nits, nits});
            Require(std::abs(pq.Decode(encoded)[0] - nits) < 0.001, "HDR highlight was clipped to SDR or quantized early.");
        }
        for (const Color excursion : {Color{0.03, -0.08, 0.06}, Color{0.98, 0.15, 0.15}})
        {
            const auto mixed = pq.Composite(excursion, {0.5f, 0.5f, 0.5f, 0.5f}, 203);
            for (const auto channel : mixed)
                Require(std::isfinite(channel), "PQ reconstruction excursion entered EOTF outside its physical domain.");
            Require(pq.Composite(excursion, {0, 0, 0, 0}, 203) == excursion,
                "Transparent excursion was incorrectly clipped.");
        }
        const auto red = pq.Decode(pq.Composite({0, 0, 0}, {1, 0, 0, 1}, 203));
        Require(std::abs(red[0] / 203 - 0.6274) < 0.0002 && std::abs(red[1] / 203 - 0.0691) < 0.0002,
            "Linear sRGB subtitle was not transformed into BT2020 primaries.");
        std::cout << "PASS precise SDR/PQ/HLG roundtrip, transparent preservation, 203 nit linear alpha, HDR highlights, gamut\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
