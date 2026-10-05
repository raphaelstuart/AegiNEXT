#pragma once
#include "media_core.h"
namespace aeginext::media
{
struct ResolvedColor
{
    int range = 0, matrix = 2, primaries = 2, transfer = 2, chromaLocation = 0, alphaMode = 0;
    uint32_t inferredFields = 0;
};
bool HasHdrEvidence(const AVFrame *frame);
bool HasHdrEvidence(const AVCodecParameters *parameters);
bool HasUnsupportedColorMetadata(const AVCodecParameters *parameters);
ResolvedColor ResolveColor(const AVFrame *frame, SourceColorContext context = {});
void ApplyColor(AVFrame *frame, const ResolvedColor &color);
}
