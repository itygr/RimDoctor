// Renders a 1024x1024 app icon: teal rounded square + white medical cross.
import AppKit

let size = 1024
let img = NSImage(size: NSSize(width: size, height: size))
img.lockFocus()

let rect = NSRect(x: 0, y: 0, width: size, height: size)
let bg = NSBezierPath(roundedRect: rect, xRadius: 185, yRadius: 185)
bg.addClip()
let grad = NSGradient(colors: [
    NSColor(red: 0.13, green: 0.66, blue: 0.66, alpha: 1.0),
    NSColor(red: 0.05, green: 0.39, blue: 0.45, alpha: 1.0)
])!
grad.draw(in: rect, angle: -90)

// white medical cross
NSColor.white.setFill()
let cx = CGFloat(size) / 2, cy = CGFloat(size) / 2
let armW: CGFloat = 168, armL: CGFloat = 560
let vert = NSRect(x: cx - armW/2, y: cy - armL/2, width: armW, height: armL)
let horz = NSRect(x: cx - armL/2, y: cy - armW/2, width: armL, height: armW)
NSBezierPath(roundedRect: vert, xRadius: 34, yRadius: 34).fill()
NSBezierPath(roundedRect: horz, xRadius: 34, yRadius: 34).fill()

img.unlockFocus()

guard let tiff = img.tiffRepresentation,
      let rep = NSBitmapImageRep(data: tiff),
      let png = rep.representation(using: .png, properties: [:]) else {
    FileHandle.standardError.write("icon render failed\n".data(using: .utf8)!); exit(1)
}
let out = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "icon_1024.png"
try! png.write(to: URL(fileURLWithPath: out))
print("wrote \(out)")
