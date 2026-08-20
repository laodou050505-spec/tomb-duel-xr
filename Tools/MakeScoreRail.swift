import Foundation
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers

let args = CommandLine.arguments
guard args.count >= 3 else { fputs("usage: MakeScoreRail input.png output.png\n", stderr); exit(2) }
let inputURL = URL(fileURLWithPath: args[1]) as CFURL
let outputURL = URL(fileURLWithPath: args[2]) as CFURL
guard let source = CGImageSourceCreateWithURL(inputURL, nil),
      let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else { exit(3) }
let width = image.width
let height = image.height
let bytesPerRow = width * 4
var pixels = [UInt8](repeating: 0, count: height * bytesPerRow)
let colorSpace = CGColorSpaceCreateDeviceRGB()
guard let context = CGContext(data: &pixels, width: width, height: height,
                              bitsPerComponent: 8, bytesPerRow: bytesPerRow,
                              space: colorSpace,
                              bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { exit(4) }
context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
func clamp(_ value: Int) -> UInt8 { UInt8(max(0, min(255, value))) }
var minX = width
var minY = height
var maxX = -1
var maxY = -1
for y in 0..<height {
    for x in 0..<width {
        let i = y * bytesPerRow + x * 4
        let r = Int(pixels[i])
        let g = Int(pixels[i + 1])
        let b = Int(pixels[i + 2])
        let neutral = max(r, max(g, b)) - min(r, min(g, b)) < 12
        let white = min(r, min(g, b)) > 232 && neutral
        if white {
            pixels[i + 3] = 0
        } else {
            let fringe = neutral && min(r, min(g, b)) > 210
            if fringe { pixels[i + 3] = clamp((232 - min(r, min(g, b))) * 5) }
            if pixels[i + 3] > 8 {
                minX = min(minX, x); minY = min(minY, y)
                maxX = max(maxX, x); maxY = max(maxY, y)
            }
        }
    }
}
guard maxX >= minX && maxY >= minY else { exit(5) }
let margin = 4
let cropMinX = max(0, minX - margin)
let cropMinY = max(0, minY - margin)
let cropMaxX = min(width - 1, maxX + margin)
let cropMaxY = min(height - 1, maxY + margin)
let cropWidth = cropMaxX - cropMinX + 1
let cropHeight = cropMaxY - cropMinY + 1
var cropped = [UInt8](repeating: 0, count: cropWidth * cropHeight * 4)
for y in 0..<cropHeight {
    let sourceRow = (cropMinY + y) * bytesPerRow
    let destRow = y * cropWidth * 4
    let start = sourceRow + cropMinX * 4
    cropped.replaceSubrange(destRow..<(destRow + cropWidth * 4), with: pixels[start..<(start + cropWidth * 4)])
}
guard let outContext = CGContext(data: &cropped, width: cropWidth, height: cropHeight,
                                 bitsPerComponent: 8, bytesPerRow: cropWidth * 4,
                                 space: colorSpace,
                                 bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue),
      let outImage = outContext.makeImage(),
      let destination = CGImageDestinationCreateWithURL(outputURL, UTType.png.identifier as CFString, 1, nil) else { exit(6) }
CGImageDestinationAddImage(destination, outImage, nil)
guard CGImageDestinationFinalize(destination) else { exit(7) }
print("ScoreRail matte written: \(cropWidth)x\(cropHeight)")
