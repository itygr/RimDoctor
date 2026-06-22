// Renders a polished Steam Workshop preview card to the path in argv[1].
import AppKit

let W = 1024, H = 576
let img = NSImage(size: NSSize(width: W, height: H))
img.lockFocus()
let ctx = NSGraphicsContext.current!
ctx.imageInterpolation = .high

// background gradient (dark slate)
let bg = NSGradient(colors: [
    NSColor(red: 0.13, green: 0.16, blue: 0.20, alpha: 1),
    NSColor(red: 0.06, green: 0.08, blue: 0.11, alpha: 1)
])!
bg.draw(in: NSRect(x: 0, y: 0, width: W, height: H), angle: -90)

// left teal accent bar
NSColor(red: 0.16, green: 0.72, blue: 0.72, alpha: 1).setFill()
NSBezierPath(rect: NSRect(x: 0, y: 0, width: 14, height: H)).fill()

func flipY(_ topY: CGFloat) -> CGFloat { CGFloat(H) - topY }

// medical cross (teal) near the title
let cx: CGFloat = 88, cyTop: CGFloat = 84
let cy = flipY(cyTop)
let armW: CGFloat = 34, armL: CGFloat = 96
NSColor(red: 0.22, green: 0.82, blue: 0.78, alpha: 1).setFill()
NSBezierPath(roundedRect: NSRect(x: cx - armW/2, y: cy - armL/2, width: armW, height: armL), xRadius: 8, yRadius: 8).fill()
NSBezierPath(roundedRect: NSRect(x: cx - armL/2, y: cy - armW/2, width: armL, height: armW), xRadius: 8, yRadius: 8).fill()

func draw(_ s: String, x: CGFloat, topY: CGFloat, size: CGFloat, color: NSColor, bold: Bool = false) {
    let f = bold ? NSFont.boldSystemFont(ofSize: size) : NSFont.systemFont(ofSize: size)
    let attr: [NSAttributedString.Key: Any] = [.font: f, .foregroundColor: color]
    let ns = s as NSString
    let h = ns.size(withAttributes: attr).height
    ns.draw(at: NSPoint(x: x, y: flipY(topY) - h), withAttributes: attr)
}

let white = NSColor.white
let dim = NSColor(white: 0.78, alpha: 1)
let green = NSColor(red: 0.55, green: 0.92, blue: 0.6, alpha: 1)

draw("RimDoctor", x: 150, topY: 44, size: 76, color: white, bold: true)
draw("by itygr   ·   mod manager + load / texture / log doctor", x: 152, topY: 132, size: 25, color: dim)

let bullets = [
    "Stops missing-texture crashes at runtime",
    "Log Doctor: plain-language errors + likely culprit mod",
    "Live TPS / FPS, per-mod tick & memory analytics + on-screen HUD",
    "Startup load-weight & save-bloat analytics, by mod",
    "Safe repairs via a generated local override mod",
]
var y: CGFloat = 210
for b in bullets {
    draw("•", x: 56, topY: y, size: 28, color: green, bold: true)
    draw(b, x: 86, topY: y, size: 28, color: NSColor(white: 0.93, alpha: 1))
    y += 50
}

// small framed checkerboard accent (intentional motif), bottom-right
let bx: CGFloat = CGFloat(W) - 150, byTop: CGFloat = CGFloat(H) - 130
let by = flipY(byTop) - 96
let cell: CGFloat = 16
NSColor(white: 1, alpha: 0.25).setStroke()
let frame = NSBezierPath(rect: NSRect(x: bx - 4, y: by - 4, width: 96 + 8, height: 96 + 8)); frame.lineWidth = 2; frame.stroke()
for r in 0..<6 {
    for c in 0..<6 {
        let magenta = (r + c) % 2 == 0
        (magenta ? NSColor(red: 1, green: 0, blue: 0.9, alpha: 1) : NSColor(white: 0.05, alpha: 1)).setFill()
        NSBezierPath(rect: NSRect(x: bx + CGFloat(c) * cell, y: by + CGFloat(r) * cell, width: cell, height: cell)).fill()
    }
}
draw("…fixes these", x: bx - 18, topY: byTop + 104, size: 18, color: NSColor(white: 0.6, alpha: 1))

img.unlockFocus()

guard let tiff = img.tiffRepresentation, let rep = NSBitmapImageRep(data: tiff),
      let png = rep.representation(using: .png, properties: [:]) else { exit(1) }
try! png.write(to: URL(fileURLWithPath: CommandLine.arguments[1]))
print("wrote \(CommandLine.arguments[1])")
