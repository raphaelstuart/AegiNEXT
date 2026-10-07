R"metal(
#include <metal_stdlib>
using namespace metal;

struct Parameters
{
    uint pixelCount;
    uint srgb;
    float yOffset;
    float yScale;
    float uvScale;
    float referenceWhite;
};

float DecodeSdr(float value, uint srgb)
{
    if (value < 0.0f)
    {
        return 0.0f;
    }
    if (srgb != 0)
    {
        return 203.0f * (value < 12.92f * 0.0031308f ? value / 12.92f :
            pow((value + 0.055f) / 1.055f, 2.4f));
    }
    return 203.0f * pow(value, 2.4f);
}

float EncodeSdr(float nits, uint srgb)
{
    if (nits < 0.0f)
    {
        return 0.0f;
    }
    const float value = nits / 203.0f;
    if (srgb != 0)
    {
        return value < 0.0031308f ? 12.92f * value : 1.055f * pow(value, 1.0f / 2.4f) - 0.055f;
    }
    return pow(value, 1.0f / 2.4f);
}

kernel void CompositeSdr(device const ushort *background [[buffer(0)]],
    device const float4 *layer [[buffer(1)]], device ushort *output [[buffer(2)]],
    constant Parameters &parameters [[buffer(3)]], device atomic_uint &error [[buffer(4)]],
    uint index [[thread_position_in_grid]])
{
    if (index >= parameters.pixelCount)
    {
        return;
    }
    const ulong count = parameters.pixelCount;
    const float4 pixel = layer[index];
    const uint alphaBits = as_type<uint>(pixel.a);
    if ((alphaBits & 0x7fffffffu) == 0u)
    {
        output[index] = background[index];
        output[index + count] = background[index + count];
        output[index + count * 2] = background[index + count * 2];
        return;
    }
    if (!all(isfinite(pixel)) || (alphaBits >> 31) != 0u || pixel.a > 1.0f)
    {
        atomic_store_explicit(&error, 1u, memory_order_relaxed);
        return;
    }
    const float y = (float(background[index]) - parameters.yOffset) / parameters.yScale;
    const float u = (float(background[index + count]) - 32768.0f) / parameters.uvScale;
    const float v = (float(background[index + count * 2]) - 32768.0f) / parameters.uvScale;
    const float kr = 0.2126f;
    const float kg = 0.7152f;
    const float kb = 0.0722f;
    const float3 rgb = float3(y + 2.0f * (1.0f - kr) * v,
        y - 2.0f * kb * (1.0f - kb) / kg * u - 2.0f * kr * (1.0f - kr) / kg * v,
        y + 2.0f * (1.0f - kb) * u);
    const float3 nits = float3(DecodeSdr(rgb.r, parameters.srgb), DecodeSdr(rgb.g, parameters.srgb),
        DecodeSdr(rgb.b, parameters.srgb)) * (1.0f - pixel.a) + pixel.rgb * parameters.referenceWhite;
    const float3 encoded = float3(EncodeSdr(nits.r, parameters.srgb), EncodeSdr(nits.g, parameters.srgb),
        EncodeSdr(nits.b, parameters.srgb));
    const float luma = kr * encoded.r + kg * encoded.g + kb * encoded.b;
    const float3 codes = float3(luma * parameters.yScale + parameters.yOffset,
        (encoded.b - luma) / (2.0f * (1.0f - kb)) * parameters.uvScale + 32768.0f,
        (encoded.r - luma) / (2.0f * (1.0f - kr)) * parameters.uvScale + 32768.0f);
    if (!all(isfinite(codes)))
    {
        atomic_store_explicit(&error, 2u, memory_order_relaxed);
        return;
    }
    const ushort3 result = ushort3(floor(clamp(codes, 0.0f, 65535.0f) + 0.5f));
    output[index] = result.x;
    output[index + count] = result.y;
    output[index + count * 2] = result.z;
}
)metal"
