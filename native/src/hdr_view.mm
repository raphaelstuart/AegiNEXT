#import "hdr_view.h"

@implementation ANHdrView

- (CALayer *)makeBackingLayer
{
    return [CAMetalLayer layer];
}

- (BOOL)isFlipped
{
    return YES;
}

- (BOOL)wantsUpdateLayer
{
    return YES;
}

- (CAMetalLayer *)metalLayer
{
    return (CAMetalLayer *)self.layer;
}

@end
