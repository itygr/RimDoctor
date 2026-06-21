// RimDoctor Repair — native macOS front-end for the mod-bisection engine.
// Wraps tools/bisect/rimdoctor_bisect.sh (bundled in Resources) with a GUI:
// clickable actions, a live progress bar + log, a mod-conflict hunter, and
// one-click apply/restore.
import SwiftUI
import AppKit

// ---- paths -----------------------------------------------------------------
let HOME = FileManager.default.homeDirectoryForCurrentUser.path
let BISECT_DIR = "\(HOME)/Library/Application Support/RimWorld/RimDoctor/Bisect"
let BISECT_LOG = "\(BISECT_DIR)/bisect_run.log"
let CULPRIT    = "\(BISECT_DIR)/culprit.txt"
let STATE_W    = "\(BISECT_DIR)/state_working.txt"
let STATE_C    = "\(BISECT_DIR)/state_culprits.txt"
let CONFLICT_FIXED = "\(BISECT_DIR)/conflict_fixed.txt"
let CONFLICT_CAND  = "\(BISECT_DIR)/conflict_candidates.txt"
let CFG_DIR    = "\(HOME)/Library/Application Support/RimWorld/Config"
let LIVE_CFG   = "\(CFG_DIR)/ModsConfig.xml"
let WORKING_CE = "\(CFG_DIR)/ModsConfig.WORKING-with-CE.xml"
let WORKING_X  = "\(CFG_DIR)/ModsConfig.working.xml"
let SAFE_CFG   = "\(CFG_DIR)/ModsConfig.before-rimdoctor-bisect.xml"

// mods always present, never treated as candidates
let BASELINE: Set<String> = [
    "brrainz.harmony","ilyvion.loadingprogress","ludeon.rimworld",
    "ludeon.rimworld.royalty","ludeon.rimworld.ideology","ludeon.rimworld.biotech",
    "ludeon.rimworld.anomaly","ludeon.rimworld.odyssey","tyler.rimdoctor"
]

func activeMods(_ path: String) -> [String] {
    guard let s = try? String(contentsOfFile: path, encoding: .utf8),
          let a = s.range(of: "<activeMods>"), let b = s.range(of: "</activeMods>") else { return [] }
    return s[a.upperBound..<b.lowerBound]
        .components(separatedBy: "<li>").dropFirst()
        .compactMap { chunk in chunk.range(of: "</li>").map { String(chunk[chunk.startIndex..<$0.lowerBound]).trimmingCharacters(in: .whitespacesAndNewlines) } }
}
func liCount(_ path: String) -> Int { activeMods(path).count }
func lineCount(_ path: String) -> Int {
    guard let s = try? String(contentsOfFile: path, encoding: .utf8) else { return 0 }
    return s.split(separator: "\n").filter { !$0.trimmingCharacters(in: .whitespaces).isEmpty }.count
}

// ---- engine ----------------------------------------------------------------
final class Engine: ObservableObject {
    @Published var logText  = "Welcome to RimDoctor Repair.\n\nPick an action on the left. Progress streams here live.\n\nNote: tests launch RimWorld repeatedly — don't run them while you're in a game."
    @Published var running  = false
    @Published var status   = "Idle"
    @Published var activeCount = liCount(LIVE_CFG)
    @Published var goodCount    = lineCount(STATE_W)
    @Published var culpritCount = lineCount(STATE_C)
    // progress
    @Published var determinate = false
    @Published var progress: Double = 0
    @Published var phase = ""
    @Published var elapsedText = ""

    private var proc: Process?
    private var timer: Timer?
    private var startTime: Date?
    private var currentArgs: [String] = []

    var scriptPath: String { Bundle.main.path(forResource: "rimdoctor_bisect", ofType: "sh") ?? "" }

    func run(_ args: [String], label: String, stall: Int = 240, ceiling: Int = 600) {
        guard !running else { return }
        guard !scriptPath.isEmpty, FileManager.default.fileExists(atPath: scriptPath) else {
            status = "Engine script missing from app bundle."; return
        }
        running = true; status = "Running: \(label)…"
        determinate = (args.first == "--build"); progress = 0; phase = "Starting…"
        startTime = Date(); currentArgs = args
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/bash")
        p.arguments = [scriptPath] + args
        var env = ProcessInfo.processInfo.environment
        env["STALL"] = String(stall); env["CEILING"] = String(ceiling)
        p.environment = env
        p.terminationHandler = { [weak self] pr in
            DispatchQueue.main.async {
                self?.running = false
                self?.status = "Finished: \(label) (exit \(pr.terminationStatus))"
                self?.phase = ""; self?.progress = 0
                self?.refresh(); self?.stopTail()
            }
        }
        do { try p.run() } catch {
            running = false; status = "Launch failed: \(error.localizedDescription)"; return
        }
        proc = p; startTail()
    }

    func stop() {
        proc?.terminate()
        let k = Process(); k.executableURL = URL(fileURLWithPath: "/usr/bin/pkill")
        k.arguments = ["-f", "RimWorld by Ludeon Studios -quicktest"]; try? k.run()
        status = "Stopped by user"
    }

    func applyConfig(_ src: String, label: String) {
        guard FileManager.default.fileExists(atPath: src) else { status = "\(label): file not found"; return }
        do {
            if FileManager.default.fileExists(atPath: LIVE_CFG) { try? FileManager.default.removeItem(atPath: LIVE_CFG) }
            try FileManager.default.copyItem(atPath: src, toPath: LIVE_CFG)
            status = "Applied: \(label) (\(liCount(LIVE_CFG)) mods active)"; refresh()
        } catch { status = "\(label) failed: \(error.localizedDescription)" }
    }

    // set up + launch the --conflict hunt for one target mod
    func huntConflict(target: String) {
        let t = target.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !t.isEmpty else { status = "Enter a mod packageId first."; return }
        let cands = activeMods(LIVE_CFG).filter { !BASELINE.contains($0) && $0 != t }
        guard !cands.isEmpty else { status = "No candidate mods in the active list."; return }
        try? FileManager.default.createDirectory(atPath: BISECT_DIR, withIntermediateDirectories: true)
        try? (t + "\n").write(toFile: CONFLICT_FIXED, atomically: true, encoding: .utf8)
        try? (cands.joined(separator: "\n") + "\n").write(toFile: CONFLICT_CAND, atomically: true, encoding: .utf8)
        run(["--conflict"], label: "Conflict hunt: \(t)")
    }

    func refresh() {
        activeCount = liCount(LIVE_CFG)
        goodCount = lineCount(STATE_W)
        culpritCount = lineCount(STATE_C)
        if let s = try? String(contentsOfFile: BISECT_LOG, encoding: .utf8) { logText = s }
        if running, let st = startTime {
            let e = Date().timeIntervalSince(st)
            let m = Int(e) / 60, s = Int(e) % 60
            elapsedText = String(format: "%d:%02d elapsed", m, s)
            let lines = logText.split(separator: "\n").map(String.init)
            phase = lines.reversed().first(where: { $0.contains("TEST [") || $0.contains("Round ") || $0.contains("Batch @") || $0.contains("CHECK") }) ?? "Loading mods…"
            if let r = phase.range(of: "] ") { phase = String(phase[r.upperBound...]) }
            if determinate, let (i, total) = lastBatch(lines), total > 0 {
                progress = Double(i) / Double(total)
            } else {
                progress = min(e / 480.0, 0.97)   // time estimate for a single ~8-min load
            }
        }
    }
    // parse last "Batch @i/total"
    private func lastBatch(_ lines: [String]) -> (Int, Int)? {
        for line in lines.reversed() where line.contains("Batch @") {
            guard let at = line.range(of: "Batch @") else { continue }
            let rest = line[at.upperBound...]
            let nums = rest.prefix(while: { $0.isNumber || $0 == "/" }).split(separator: "/")
            if nums.count == 2, let i = Int(nums[0]), let t = Int(nums[1]) { return (i, t) }
        }
        return nil
    }
    private func startTail() { timer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in self?.refresh() } }
    private func stopTail() { timer?.invalidate(); timer = nil }
}

// ---- UI --------------------------------------------------------------------
struct ActionButton: View {
    let title: String; let subtitle: String; let systemImage: String
    let tint: Color; let disabled: Bool; let action: () -> Void
    var body: some View {
        Button(action: action) {
            HStack(spacing: 10) {
                Image(systemName: systemImage).font(.system(size: 18)).frame(width: 24)
                VStack(alignment: .leading, spacing: 1) {
                    Text(title).font(.system(size: 13, weight: .semibold))
                    Text(subtitle).font(.system(size: 10)).foregroundColor(.secondary)
                }
                Spacer()
            }
            .padding(.vertical, 8).padding(.horizontal, 12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: 9).fill(tint.opacity(disabled ? 0.05 : 0.14)))
            .overlay(RoundedRectangle(cornerRadius: 9).stroke(tint.opacity(disabled ? 0.1 : 0.35), lineWidth: 1))
        }
        .buttonStyle(.plain).disabled(disabled)
    }
}

struct ContentView: View {
    @StateObject var engine = Engine()
    @State private var showConflict = false
    @State private var conflictTarget = ""

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 12) {
                Image(systemName: "cross.case.fill").font(.system(size: 26)).foregroundColor(.teal)
                VStack(alignment: .leading, spacing: 2) {
                    Text("RimDoctor Repair").font(.system(size: 19, weight: .bold))
                    Text("Find the mods that break your list — automatically.").font(.system(size: 11)).foregroundColor(.secondary)
                }
                Spacer()
                statusPill
            }.padding(16)
            Divider()
            HStack(spacing: 18) {
                stat("Active mods", "\(engine.activeCount)", .teal)
                stat("Verified good", "\(engine.goodCount)", .green)
                stat("Breakers found", "\(engine.culpritCount)", .orange)
                Spacer()
            }.padding(.horizontal, 16).padding(.vertical, 10)
            Divider()
            HStack(alignment: .top, spacing: 0) {
                VStack(alignment: .leading, spacing: 9) {
                    Text("ACTIONS").font(.system(size: 10, weight: .bold)).foregroundColor(.secondary).padding(.bottom, 2)
                    ActionButton(title: "Test Current Modlist", subtitle: "Launch active list, confirm it loads",
                                 systemImage: "play.circle.fill", tint: .blue, disabled: engine.running) {
                        engine.run(["--check"], label: "Test Current Modlist")
                    }
                    ActionButton(title: "Full Auto-Repair", subtitle: "Find every breaker, build a working list",
                                 systemImage: "wrench.and.screwdriver.fill", tint: .teal, disabled: engine.running) {
                        engine.run(["--build"], label: "Full Auto-Repair")
                    }
                    ActionButton(title: "Find a Mod's Conflict…", subtitle: "Pin what one mod deadlocks with",
                                 systemImage: "scope", tint: .purple, disabled: engine.running) {
                        showConflict = true
                    }
                    ActionButton(title: "Show Breakers Found", subtitle: "Read the latest culprit report",
                                 systemImage: "list.bullet.clipboard.fill", tint: .orange, disabled: engine.running) {
                        if let s = try? String(contentsOfFile: CULPRIT, encoding: .utf8) {
                            engine.logText = "=== Breaker / working report ===\n\n" + s
                        } else { engine.logText = "No report yet — run Full Auto-Repair first." }
                    }
                    Divider().padding(.vertical, 4)
                    Text("APPLY A CONFIG").font(.system(size: 10, weight: .bold)).foregroundColor(.secondary)
                    ActionButton(title: "Apply Working List", subtitle: "Switch to the repaired modlist",
                                 systemImage: "checkmark.seal.fill", tint: .green, disabled: engine.running) {
                        let src = FileManager.default.fileExists(atPath: WORKING_CE) ? WORKING_CE : WORKING_X
                        engine.applyConfig(src, label: "Working List")
                    }
                    ActionButton(title: "Restore Safe (Vanilla)", subtitle: "Vanilla + RimDoctor only",
                                 systemImage: "shield.fill", tint: .gray, disabled: engine.running) {
                        engine.applyConfig(SAFE_CFG, label: "Safe config")
                    }
                    Spacer()
                    if engine.running {
                        Button(role: .destructive) { engine.stop() } label: {
                            HStack { Image(systemName: "stop.fill"); Text("Stop").fontWeight(.semibold) }
                                .frame(maxWidth: .infinity).padding(.vertical, 7)
                        }.buttonStyle(.borderedProminent).tint(.red)
                    }
                }
                .frame(width: 250).padding(16)
                Divider()
                VStack(alignment: .leading, spacing: 6) {
                    // progress bar (load-time)
                    if engine.running {
                        VStack(alignment: .leading, spacing: 4) {
                            if engine.determinate {
                                ProgressView(value: engine.progress).progressViewStyle(.linear)
                            } else {
                                ProgressView(value: engine.progress).progressViewStyle(.linear)
                                    .tint(.blue)
                            }
                            HStack {
                                Text(engine.phase).font(.system(size: 10)).foregroundColor(.secondary).lineLimit(1)
                                Spacer()
                                Text(engine.elapsedText).font(.system(size: 10, weight: .medium)).foregroundColor(.secondary)
                            }
                        }
                        .padding(8)
                        .background(RoundedRectangle(cornerRadius: 8).fill(Color.blue.opacity(0.06)))
                    }
                    HStack {
                        Text("PROGRESS LOG").font(.system(size: 10, weight: .bold)).foregroundColor(.secondary)
                        Spacer()
                    }
                    ScrollViewReader { sp in
                        ScrollView {
                            Text(engine.logText)
                                .font(.system(size: 11, design: .monospaced))
                                .textSelection(.enabled)
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .padding(8).id("logbottom")
                        }
                        .background(RoundedRectangle(cornerRadius: 8).fill(Color.black.opacity(0.04)))
                        .onChange(of: engine.logText) { _, _ in withAnimation { sp.scrollTo("logbottom", anchor: .bottom) } }
                    }
                }.padding(16)
            }
        }
        .frame(minWidth: 880, minHeight: 580)
        .onAppear { engine.refresh() }
        .sheet(isPresented: $showConflict) { conflictSheet }
    }

    var conflictSheet: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Find a Mod's Conflict").font(.system(size: 16, weight: .bold))
            Text("Enter a mod's packageId. RimDoctor will hold it in and binary-search the rest of your active list to pin the single mod it deadlocks with.")
                .font(.system(size: 11)).foregroundColor(.secondary).fixedSize(horizontal: false, vertical: true)
            TextField("e.g. com.abobashark.zoologymod", text: $conflictTarget)
                .textFieldStyle(.roundedBorder).font(.system(size: 12, design: .monospaced))
            Text("⚠️ This launches RimWorld many times (~40–60 min). Don't run it while you're in a game.")
                .font(.system(size: 10)).foregroundColor(.orange)
            HStack {
                Spacer()
                Button("Cancel") { showConflict = false }.keyboardShortcut(.cancelAction)
                Button("Start Hunt") {
                    showConflict = false
                    engine.huntConflict(target: conflictTarget)
                }.keyboardShortcut(.defaultAction).buttonStyle(.borderedProminent)
                    .disabled(conflictTarget.trimmingCharacters(in: .whitespaces).isEmpty)
            }
        }
        .padding(20).frame(width: 440)
    }

    var statusPill: some View {
        HStack(spacing: 6) {
            Circle().fill(engine.running ? Color.green : Color.gray).frame(width: 8, height: 8)
            Text(engine.status).font(.system(size: 11, weight: .medium)).lineLimit(1)
        }
        .padding(.horizontal, 10).padding(.vertical, 6)
        .background(Capsule().fill(Color.secondary.opacity(0.12)))
    }

    func stat(_ label: String, _ value: String, _ color: Color) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            Text(value).font(.system(size: 22, weight: .bold)).foregroundColor(color)
            Text(label).font(.system(size: 10)).foregroundColor(.secondary)
        }
    }
}

@main
struct RimDoctorRepairApp: App {
    var body: some Scene {
        WindowGroup { ContentView() }
        .windowResizability(.contentSize)
    }
}
