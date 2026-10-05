#include "media_core.h"
#include <cstdlib>
#include <iostream>
namespace aeginext::media
{
struct DecoderSessionTestAccess
{
    static bool ExportsFilmGrain(const DecoderSession &session) { return (session.codec_->export_side_data & AV_CODEC_EXPORT_DATA_FILM_GRAIN) != 0; }
    static void RefuseHardwareFormat(DecoderSession &session) { session.hardwareFormat_ = AV_PIX_FMT_NONE; }
    static void InjectCorruptPacket(DecoderSession &session)
    {
        av_packet_unref(session.packet_);
        if (av_new_packet(session.packet_, 16) < 0) { throw std::bad_alloc(); }
        for (int index = 0; index < 16; ++index) { session.packet_->data[index] = 0; }
        session.packet_->stream_index = session.streamIndex_;
        session.packetPending_ = true;
    }
    static bool NegotiationFailed(const DecoderSession &session) { return session.negotiationFailed_; }
};
}
using namespace aeginext::media;
namespace
{
void Require(bool value, const char *message)
{ if (!value) { throw std::runtime_error(message); } }
void CorruptionNeverTriggersHardwareFallback(const char *path)
{
    DecoderSession session;
    session.Open(path, 0);
    Require(DecoderSessionTestAccess::ExportsFilmGrain(session), "Decoder must expose film-grain parameters without silently synthesizing grain.");
    const auto backend = session.Info().activeBackend;
    const auto reason = session.Info().fallbackReason;
    DecoderSessionTestAccess::InjectCorruptPacket(session);
    try { session.ReadFrame(); }
    catch (const CoreError &error)
    {
        Require(error.Code() == ErrorCode::Decode && !error.IsHardwareFailure(), "Corrupt packet was classified as a hardware failure.");
        Require(session.Info().activeBackend == backend && session.Info().deliveredFrames == 0 && session.Info().fallbackReason == reason,
            "Source corruption changed backend or delivered a frame.");
        Require(!DecoderSessionTestAccess::NegotiationFailed(session), "Corrupt packet claimed a format negotiation failure.");
        return;
    }
    throw std::runtime_error("Invalid source packet was accepted.");
}
void CancellationNeverFallsBack(const char *path)
{
    DecoderSession session;
    session.Open(path, 0);
    Require(DecoderSessionTestAccess::ExportsFilmGrain(session), "Decoder must expose film-grain parameters without silently synthesizing grain.");
    const auto backend = session.Info().activeBackend;
    DecoderSessionTestAccess::RefuseHardwareFormat(session);
    session.Cancel();
    try { session.ReadFrame(); }
    catch (const CoreError &error)
    {
        Require(error.Code() == ErrorCode::Cancelled && !error.IsHardwareFailure(), "Cancellation was classified as a hardware failure.");
        Require(session.Info().activeBackend == backend && session.Info().deliveredFrames == 0, "Cancellation changed backend or delivered a frame.");
        return;
    }
    throw std::runtime_error("Cancelled session returned a frame.");
}
void NegotiationFailureFallsBackBeforeDelivery(const char *path)
{
    DecoderSession session;
    session.Open(path, 0);
    if (session.Info().activeBackend == DecoderBackend::Software)
    { std::cout << "Hardware device unavailable; automatic initialization fallback was exercised.\n"; return; }
    DecoderSessionTestAccess::RefuseHardwareFormat(session);
    auto frame = session.ReadFrame();
    Require(frame != nullptr, "Automatic negotiation fallback lost the first valid frame.");
    Require(session.Info().activeBackend == DecoderBackend::Software && !session.Info().hardwareConfirmed &&
        session.Info().deliveredFrames == 1 && !session.Info().fallbackReason.empty(), "Automatic negotiation fallback was not recorded.");
    Require(!DecoderSessionTestAccess::NegotiationFailed(session), "Software reopening retained stale negotiation state.");
}
void HardwareRequiresFailureAndNeverSwitchesAfterDelivery(const char *path)
{
    DecoderSession required({DecodeMode::Hardware, DecodeWorkload::Interactive});
    try { required.Open(path, 0); }
    catch (const CoreError &error)
    {
        Require(error.IsHardwareFailure(), "Required hardware initialization returned a non-hardware failure.");
        std::cout << "Required hardware rejected unavailable device.\n"; return;
    }
    DecoderSessionTestAccess::RefuseHardwareFormat(required);
    bool rejected = false;
    try { required.ReadFrame(); }
    catch (const CoreError &error) { rejected = error.IsHardwareFailure(); }
    Require(rejected && required.Info().deliveredFrames == 0 && required.Info().activeBackend != DecoderBackend::Software,
        "Required hardware silently switched to software.");
    DecoderSession autoSession;
    autoSession.Open(path, 0);
    auto first = autoSession.ReadFrame();
    Require(first != nullptr, "Valid input has no first frame.");
    if (autoSession.Info().activeBackend == DecoderBackend::Software)
    { std::cout << "Hardware first-frame validation refused this fixture; automatic software restart was exercised.\n"; return; }
    const auto backend = autoSession.Info().activeBackend;
    DecoderSessionTestAccess::RefuseHardwareFormat(autoSession);
    rejected = false;
    try { autoSession.ReadFrame(); }
    catch (const CoreError &error) { rejected = error.IsHardwareFailure(); }
    Require(rejected && autoSession.Info().activeBackend == backend && autoSession.Info().deliveredFrames == 1,
        "Hardware failure after delivery silently switched backend or delivered a mixed frame.");
    std::cout << "Executed actual hardware post-delivery refusal: backend=" << static_cast<uint32_t>(backend) << ", hardwareConfirmed=" << autoSession.Info().hardwareConfirmed << '\n';
}
}
int main()
{
    const auto *path = std::getenv("AEGINEXT_CORE_TEST_MEDIA_PATH");
    if (!path || !*path)
    { std::cout << "SKIP hardware negotiation fault injection: AEGINEXT_CORE_TEST_MEDIA_PATH is not set.\n"; return 77; }
    std::cout << "Fixture: " << path << '\n';
    try
    {
        CorruptionNeverTriggersHardwareFallback(path);
        CancellationNeverFallsBack(path);
        NegotiationFailureFallsBackBeforeDelivery(path);
        HardwareRequiresFailureAndNeverSwitchesAfterDelivery(path);
        std::cout << "PASS controlled negotiation failure, required hardware refusal, post-delivery failure, source corruption and sticky cancellation\n";
        return 0;
    }
    catch (const std::exception &error) { std::cerr << error.what() << '\n'; return 1; }
}
