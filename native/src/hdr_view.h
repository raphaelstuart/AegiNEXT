#ifndef AEGINEXT_HDR_VIEW_H
#define AEGINEXT_HDR_VIEW_H

#import <AppKit/AppKit.h>
#import <QuartzCore/CAMetalLayer.h>

@interface ANHdrView : NSView <CALayerDelegate>
@property(nonatomic, readonly) CAMetalLayer *metalLayer;
@end

#endif
