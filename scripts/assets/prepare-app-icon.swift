import AppKit

let arguments = CommandLine.arguments
guard arguments.count == 3,
      let source = NSImage(contentsOfFile: arguments[1]),
      let bitmap = NSBitmapImageRep(
          bitmapDataPlanes: nil, pixelsWide: 1024, pixelsHigh: 1024,
          bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true,
          isPlanar: false, colorSpaceName: .deviceRGB,
          bytesPerRow: 0, bitsPerPixel: 0),
      let context = NSGraphicsContext(bitmapImageRep: bitmap)
else
{
    throw NSError(domain: "AegiNext.IconExport", code: 1,
                  userInfo: [NSLocalizedDescriptionKey: "Expected a readable source PNG and output path."])
}

NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = context
context.imageInterpolation = .high
let safeArea = NSRect(x: 64, y: 64, width: 896, height: 896)
NSBezierPath(roundedRect: safeArea, xRadius: 180, yRadius: 180).addClip()
source.draw(in: NSRect(x: 0, y: 0, width: 1024, height: 1024),
            from: .zero, operation: .copy, fraction: 1,
            respectFlipped: false, hints: nil)
NSGraphicsContext.restoreGraphicsState()

guard let png = bitmap.representation(using: .png, properties: [:])
else
{
    throw NSError(domain: "AegiNext.IconExport", code: 2,
                  userInfo: [NSLocalizedDescriptionKey: "Unable to encode the application icon."])
}
try png.write(to: URL(fileURLWithPath: arguments[2]), options: .atomic)
