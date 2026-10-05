#include "decoder_context.h"
namespace aeginext::decode
{
std::unique_ptr<FrameOwner> DecoderContext::ReadNext()
{
    auto frame = session_.ReadFrame();
    if (!frame) { return nullptr; }
    return std::make_unique<FrameOwner>(std::move(frame), session_.StreamTimeBase(), session_.ColorContext());
}
an_decode_ratio DecoderContext::StreamTimeBase() const
{
    const auto value = session_.StreamTimeBase();
    return {value.num, value.den};
}
}
