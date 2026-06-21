#!/bin/bash
# ============================================================================
# rimdoctor_bisect.sh — automatic mod bisection for RimWorld 1.6 (macOS)
#
# Binary-searches your 232-mod list to find which mod (or interacting pair of
# halves) crashes the game. Each round it writes ModsConfig.xml with a subset,
# launches RimWorld with -quicktest (which boots straight into pawn+map gen —
# exactly the path that's been crashing), watches the Unity log, and decides
# PASS / FAIL automatically. No clicking required.
#
# Always-on baseline (never disabled): Harmony, Core + all DLCs, LoadingProgress,
# and RimDoctor itself (so it can attribute as it goes).
#
# Usage:
#   ./rimdoctor_bisect.sh            # run the full bisection
#   ./rimdoctor_bisect.sh --dry      # show the plan + candidate list, launch nothing
#   TIMEOUT=180 ./rimdoctor_bisect.sh   # override per-run wait (default 120s)
#
# Watch progress live in another terminal:
#   tail -f "$HOME/Library/Application Support/RimWorld/RimDoctor/Bisect/bisect_run.log"
#
# Stop any time with Ctrl-C — your clean vanilla+RimDoctor config is restored.
# ============================================================================
# NOTE: no `set -u` — macOS bash 3.2 errors on empty-array expansion ("${a[@]}"
# with zero elements), and the baseline test deliberately passes zero candidates.
set -o pipefail

# ---- paths ----------------------------------------------------------------
RW_APP="$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app"
RW_BIN="$RW_APP/Contents/MacOS/RimWorld by Ludeon Studios"
CFG="$HOME/Library/Application Support/RimWorld/Config"
MODSCONFIG="$CFG/ModsConfig.xml"
FULL_LIST="$CFG/ModsConfig.backup_20260617_111041.xml"   # 232-mod source of truth
SAFE_CONFIG="$CFG/ModsConfig.before-rimdoctor-bisect.xml" # restored on exit (fallback below)
WORK="$HOME/Library/Application Support/RimWorld/RimDoctor/Bisect"
RUNLOG="$WORK/bisect_run.log"
GAMELOG="$WORK/game.log"
RESULT="$WORK/culprit.txt"

# ---- tuning ---------------------------------------------------------------
# Detection is ACTIVITY-based, not a flat timer: as long as RimWorld keeps writing
# to the log (i.e. still loading 100+ mods), we keep waiting. A run is only decided
# by a crash marker, an in-game marker, a CTD, a log STALL, or the hard ceiling.
CEILING="${CEILING:-480}"          # absolute max seconds per run (slowest huge load)
STALL="${STALL:-90}"               # if log hasn't grown in this long -> hung -> FAIL
POLL=5                             # seconds between log checks
VERSION="1.6.4850 rev652"

# Baseline that is ALWAYS active (and excluded from the candidate pool).
ALWAYS_ON=(brrainz.harmony ilyvion.loadingprogress ludeon.rimworld \
  ludeon.rimworld.royalty ludeon.rimworld.ideology ludeon.rimworld.biotech \
  ludeon.rimworld.anomaly ludeon.rimworld.odyssey tyler.rimdoctor)
KNOWN_EXP=(ludeon.rimworld.royalty ludeon.rimworld.ideology ludeon.rimworld.biotech \
  ludeon.rimworld.anomaly ludeon.rimworld.odyssey)

# If ANY of these strings appears in the run's log, the run is a FAIL.
# NOTE: "Root level exception in Update/OnGUI" deliberately NOT here — RimWorld
# throws a benign MusicManagerPlay NRE under -quicktest (audio not focused) on
# EVERY run, healthy or not. Stick to crash signals from pawn/map generation.
FAIL_MARKERS=(
  "ScribedCache"
  "Error while generating pawn. Rethrowing"
  "Error while generating map"
  "Exception while generating map"
  "WorldGenStep_Factions.GenerateFresh"
  "Error in WorldGenStep"
)
# Definitive proof we got in-game PAST pawn+map generation (the crash point).
# Seeing any of these with no fail marker = immediate PASS (no need to wait out
# the full timeout). The MusicManager init only runs once play has started.
PASS_MARKERS=(
  "MusicManagerPlay"
  "InitializeMusicManager"
  "Root_Play.Update"
  "Map generated"
)

mkdir -p "$WORK"

# ---- helpers --------------------------------------------------------------
log() { echo "[$(date '+%H:%M:%S')] $*" | tee -a "$RUNLOG"; }

in_array() { local n="$1"; shift; local x; for x in "$@"; do [ "$x" = "$n" ] && return 0; done; return 1; }

# write ModsConfig.xml with ALWAYS_ON + the given subset (subset in load order)
write_config() {
  local subset=("$@")
  printf '\xEF\xBB\xBF' > "$MODSCONFIG"
  {
    echo '<?xml version="1.0" encoding="utf-8"?>'
    echo '<ModsConfigData>'
    echo "  <version>$VERSION</version>"
    echo '  <activeMods>'
    # order: harmony, loadingprogress, core, dlcs, <subset>, rimdoctor last
    echo '    <li>brrainz.harmony</li>'
    echo '    <li>ilyvion.loadingprogress</li>'
    echo '    <li>ludeon.rimworld</li>'
    local e; for e in "${KNOWN_EXP[@]}"; do echo "    <li>$e</li>"; done
    local m; for m in "${subset[@]}"; do echo "    <li>$m</li>"; done
    echo '    <li>tyler.rimdoctor</li>'
    echo '  </activeMods>'
    echo '  <knownExpansions>'
    for e in "${KNOWN_EXP[@]}"; do echo "    <li>$e</li>"; done
    echo '  </knownExpansions>'
    echo '</ModsConfigData>'
  } >> "$MODSCONFIG"
}

# launch RimWorld -quicktest, watch log, return 0=PASS 1=FAIL
run_test() {
  : > "$GAMELOG"
  "$RW_BIN" -quicktest -logfile "$GAMELOG" >/dev/null 2>&1 &
  local pid=$!
  local waited=0 mk last_size=0 cur_size=0 stalled=0
  while [ "$waited" -lt "$CEILING" ]; do
    # real crash marker? (checked first — a crash during gen is decisive)
    for mk in "${FAIL_MARKERS[@]}"; do
      if grep -qF "$mk" "$GAMELOG" 2>/dev/null; then
        log "    fail marker: \"$mk\" after ${waited}s -> FAIL"
        kill "$pid" 2>/dev/null; wait "$pid" 2>/dev/null
        return 1
      fi
    done
    # got in-game past pawn+map gen? decisive PASS, stop early
    for mk in "${PASS_MARKERS[@]}"; do
      if grep -qF "$mk" "$GAMELOG" 2>/dev/null; then
        log "    in-game marker: \"$mk\" after ${waited}s -> PASS"
        kill "$pid" 2>/dev/null; wait "$pid" 2>/dev/null
        return 0
      fi
    done
    # crash to desktop before reaching either?
    if ! kill -0 "$pid" 2>/dev/null; then
      log "    process exited early (CTD) after ${waited}s -> FAIL"
      return 1
    fi
    # activity check: while the log keeps growing, it's still loading — keep waiting.
    cur_size=$(wc -c < "$GAMELOG" 2>/dev/null | tr -d ' '); cur_size=${cur_size:-0}
    if [ "$cur_size" -gt "$last_size" ]; then
      last_size=$cur_size; stalled=0
    else
      stalled=$((stalled+POLL))
      if [ "$stalled" -ge "$STALL" ]; then
        log "    log idle ${stalled}s (no progress, no marker) after ${waited}s -> FAIL (hung)"
        kill "$pid" 2>/dev/null; wait "$pid" 2>/dev/null
        return 1
      fi
    fi
    sleep "$POLL"; waited=$((waited+POLL))
  done
  kill "$pid" 2>/dev/null; wait "$pid" 2>/dev/null
  log "    hit ceiling ${CEILING}s with no in-game marker -> FAIL"
  return 1
}

# test a subset: prints PASS/FAIL to log, returns 0=PASS 1=FAIL
test_subset() {
  local label="$1"; shift
  local subset=("$@")
  log "  TEST [$label] — ${#subset[@]} candidate mod(s) active"
  write_config "${subset[@]}"
  if run_test; then return 0; else return 1; fi
}

restore_safe() {
  if [ -f "$SAFE_CONFIG" ]; then cp "$SAFE_CONFIG" "$MODSCONFIG"
  else write_config; fi   # at minimum: clean vanilla + RimDoctor
  log "Restored safe config (vanilla + RimDoctor)."
}
trap 'echo; log "Interrupted."; restore_safe; exit 130' INT TERM

# ---- build candidate list (backup activeMods minus ALWAYS_ON, in order) ----
candidates=()
in_active=0
while IFS= read -r line; do
  case "$line" in
    *"<activeMods>"*)  in_active=1; continue;;
    *"</activeMods>"*) in_active=0; continue;;
  esac
  [ "$in_active" -eq 1 ] || continue
  case "$line" in *"<li>"*"</li>"*) ;; *) continue;; esac
  id="${line#*<li>}"; id="${id%</li>*}"; id="$(echo "$id" | xargs)"
  [ -n "$id" ] || continue
  in_array "$id" "${ALWAYS_ON[@]}" && continue
  candidates+=("$id")
done < "$FULL_LIST"

: > "$RUNLOG"
log "RimDoctor mod bisection"
log "Source list : $FULL_LIST"
log "Candidates  : ${#candidates[@]} mods (baseline of ${#ALWAYS_ON[@]} always on)"
log "Per-run : activity-based (stall ${STALL}s, ceiling ${CEILING}s)   Log: $GAMELOG"
log ""

# --check : test the CURRENT live ModsConfig.xml exactly as-is. Does NOT rewrite or
# restore it — so a PASS leaves your full modlist in place, ready to play.
if [ "${1:-}" = "--check" ]; then
  if [ ! -x "$RW_BIN" ]; then log "ERROR: RimWorld binary not found: $RW_BIN"; exit 1; fi
  log "CHECK — launching current live ModsConfig.xml as-is..."
  if run_test; then log "================ CHECK: PASS (got in-game) ================"; exit 0
  else log "================ CHECK: FAIL (crashed/hung) ================"; exit 2; fi
fi

# --conflict : find the single mod that deadlocks with a fixed set. Reads
#   $WORK/conflict_fixed.txt      (mods always present, e.g. Zoology + CE)
#   $WORK/conflict_candidates.txt (mods to binary-search)
# fixed-only must PASS, fixed+all must FAIL; then bisects candidates to the one.
if [ "${1:-}" = "--conflict" ]; then
  if [ ! -x "$RW_BIN" ]; then log "ERROR: RimWorld binary not found: $RW_BIN"; exit 1; fi
  FIX=(); CAND=()
  while IFS= read -r l; do [ -n "$l" ] && FIX+=("$l"); done < "$WORK/conflict_fixed.txt"
  while IFS= read -r l; do [ -n "$l" ] && CAND+=("$l"); done < "$WORK/conflict_candidates.txt"
  : > "$RUNLOG"
  log "CONFLICT HUNT"
  log "Fixed (${#FIX[@]}): ${FIX[*]}"
  log "Candidates to search: ${#CAND[@]}"
  log ""
  log "Sanity: fixed set alone should PASS..."
  if ! test_subset "fixed-only" "${FIX[@]}"; then
    log "  fixed set alone FAILS -> the fixed mods break by themselves, not a conflict with others."
    echo "FIXED-SELF-BREAK" > "$RESULT"; restore_safe; exit 0
  fi
  log "Sanity: fixed + all candidates should FAIL..."
  if test_subset "fixed+all" "${FIX[@]}" "${CAND[@]}"; then
    log "  fixed + all PASSED -> conflict did not reproduce. Cannot hunt."
    echo "NO-REPRO" > "$RESULT"; restore_safe; exit 0
  fi
  log "  reproduced -> binary search begins"; log ""
  suspects=("${CAND[@]}"); round=0
  while [ "${#suspects[@]}" -gt 1 ]; do
    round=$((round+1)); n=${#suspects[@]}; h=$(((n+1)/2))
    f=("${suspects[@]:0:h}"); s=("${suspects[@]:h}")
    log "Round $round: $n suspects -> ${#f[@]}/${#s[@]}"
    if test_subset "fixed + first(${#f[@]})" "${FIX[@]}" "${f[@]}"; then
      if test_subset "fixed + second(${#s[@]})" "${FIX[@]}" "${s[@]}"; then
        log "  neither half fails with fixed -> multi-mod / interaction spans split."
        log "  remaining: ${suspects[*]}"; echo "MULTI: ${suspects[*]}" > "$RESULT"; restore_safe; exit 0
      else suspects=("${s[@]}"); fi
    else suspects=("${f[@]}"); fi
    log "  -> ${#suspects[@]} suspect(s) remain"
  done
  c="${suspects[0]}"
  log ""
  log "Confirm: fixed + $c should FAIL..."
  if test_subset "confirm" "${FIX[@]}" "$c"; then
    log "  fixed + $c PASSED alone -> not a clean single conflict (needs >1 partner)."
    echo "PARTIAL: ${FIX[0]} conflicts with a group incl. $c" > "$RESULT"
  else
    log "================ ${FIX[0]} CONFLICTS WITH: $c ================"
    echo "${FIX[0]} <-> $c" > "$RESULT"
  fi
  restore_safe; exit 0
fi

if [ "${1:-}" = "--dry" ]; then
  log "DRY RUN — candidate load order:"
  i=1; for c in "${candidates[@]}"; do printf '  %3d. %s\n' "$i" "$c" | tee -a "$RUNLOG"; i=$((i+1)); done
  exit 0
fi

# --test <id> [id...] : run ONE quicktest with baseline + the given mods, report
# PASS/FAIL, restore safe config, exit. Used to confirm a suspected interaction.
if [ "${1:-}" = "--test" ]; then
  shift
  if [ ! -x "$RW_BIN" ]; then log "ERROR: RimWorld binary not found: $RW_BIN"; exit 1; fi
  log "TARGETED TEST — baseline + [$*]"
  if test_subset "targeted" "$@"; then log "RESULT: PASS (got in-game)"; else log "RESULT: FAIL (crashed/hung)"; fi
  restore_safe
  exit 0
fi

# --pair <A ids...> -- <B ids...> : resolve a two-group interaction.
# Group A and Group B each PASS alone but A+B FAILS. Hold A, bisect B to a single
# b*; hold b*, bisect A to a single a*; confirm a*+b* fails. Pins the pair.
if [ "${1:-}" = "--pair" ]; then
  shift
  GA=(); GB=(); sep=0
  for x in "$@"; do
    if [ "$x" = "--" ]; then sep=1; continue; fi
    if [ "$sep" -eq 0 ]; then GA+=("$x"); else GB+=("$x"); fi
  done
  if [ ! -x "$RW_BIN" ]; then log "ERROR: RimWorld binary not found: $RW_BIN"; exit 1; fi
  : > "$RUNLOG"
  log "INTERACTION RESOLVER"
  log "Group A (${#GA[@]}): ${GA[*]}"
  log "Group B (${#GB[@]}): ${GB[*]}"
  log ""

  log "Phase 1: hold all of Group A, narrow Group B..."
  sB=("${GB[@]}")
  while [ "${#sB[@]}" -gt 1 ]; do
    n=${#sB[@]}; h=$(((n+1)/2)); f=("${sB[@]:0:h}"); s=("${sB[@]:h}")
    if test_subset "A + B.first(${#f[@]})" "${GA[@]}" "${f[@]}"; then
      if test_subset "A + B.second(${#s[@]})" "${GA[@]}" "${s[@]}"; then
        log "  neither B-half fails with full A -> B side needs >1 mod / spans split."
        log "  Remaining B suspects: ${sB[*]}"; echo "B-MULTI: ${sB[*]}" > "$RESULT"; restore_safe; exit 0
      else sB=("${s[@]}"); fi
    else sB=("${f[@]}"); fi
    log "  -> ${#sB[@]} B suspect(s): ${sB[*]}"
  done
  bstar="${sB[0]}"; log "  >> B culprit: $bstar"; log ""

  log "Phase 2: hold $bstar, narrow Group A..."
  sA=("${GA[@]}")
  while [ "${#sA[@]}" -gt 1 ]; do
    n=${#sA[@]}; h=$(((n+1)/2)); f=("${sA[@]:0:h}"); s=("${sA[@]:h}")
    if test_subset "A.first(${#f[@]}) + b" "${f[@]}" "$bstar"; then
      if test_subset "A.second(${#s[@]}) + b" "${s[@]}" "$bstar"; then
        log "  neither A-half fails with $bstar -> A side needs >1 mod."
        log "  Remaining A suspects: ${sA[*]}"; echo "A-MULTI: ${sA[*]} (with $bstar)" > "$RESULT"; restore_safe; exit 0
      else sA=("${s[@]}"); fi
    else sA=("${f[@]}"); fi
    log "  -> ${#sA[@]} A suspect(s): ${sA[*]}"
  done
  astar="${sA[0]}"; log "  >> A culprit: $astar"; log ""

  log "Confirm: does $astar + $bstar alone fail?"
  if test_subset "pair-confirm" "$astar" "$bstar"; then
    log "  pair PASSED alone -> not a clean 2-mod pair (more mods involved)."
    echo "PAIR-UNCONFIRMED a=$astar b=$bstar" > "$RESULT"
  else
    log "================ INTERACTION: $astar  <->  $bstar ================"
    echo "$astar <-> $bstar" > "$RESULT"
  fi
  restore_safe; exit 0
fi

# --build : additive rebuild. Start from baseline, re-add candidates in load order
# in batches. A batch that passes is accepted wholesale; a batch that fails is
# scanned mod-by-mod against the accumulated good set, so every conflicting/broken
# mod (including interaction offenders) is culled. Ends with a MAXIMAL working
# modlist applied live + a full culprit list. Checkpoints after every batch.
if [ "${1:-}" = "--build" ]; then
  BATCH="${BATCH:-15}"
  WORKING_OUT="$CFG/ModsConfig.working.xml"
  STATE_W="$WORK/state_working.txt"; STATE_C="$WORK/state_culprits.txt"
  if [ ! -x "$RW_BIN" ]; then log "ERROR: RimWorld binary not found: $RW_BIN"; exit 1; fi

  W=(); CULP=()
  if [ "${2:-}" = "--fresh" ]; then
    rm -f "$STATE_W" "$STATE_C"; log "ADDITIVE REBUILD (fresh start)"
  else
    # RESUME: reload prior working set + culprits so short sessions accumulate.
    [ -f "$STATE_W" ] && while IFS= read -r l; do [ -n "$l" ] && W+=("$l"); done < "$STATE_W"
    [ -f "$STATE_C" ] && while IFS= read -r l; do [ -n "$l" ] && CULP+=("$l"); done < "$STATE_C"
    log "ADDITIVE REBUILD (resume: ${#W[@]} good, ${#CULP[@]} culprits already known)"
  fi
  log "batch ${BATCH}; checkpoint+state after every batch; safe to Ctrl-C and resume"
  log ""

  # remaining = candidates not yet processed (not in W, not in CULP), in order
  todo=()
  for c in "${candidates[@]}"; do
    in_array "$c" "${W[@]}" && continue
    in_array "$c" "${CULP[@]}" && continue
    todo+=("$c")
  done
  log "Remaining to test: ${#todo[@]} of ${#candidates[@]}"
  log ""

  if ! test_subset "baseline"; then log "baseline FAILED — environment problem. Abort."; restore_safe; exit 1; fi

  save_state() {
    : > "$STATE_W"; printf '%s\n' "${W[@]}" >> "$STATE_W" 2>/dev/null
    : > "$STATE_C"; printf '%s\n' "${CULP[@]}" >> "$STATE_C" 2>/dev/null
    write_config "${W[@]}"; cp "$MODSCONFIG" "$WORKING_OUT"
  }

  i=0; total=${#todo[@]}
  while [ "$i" -lt "$total" ]; do
    batch=("${todo[@]:i:BATCH}")
    log "Batch @${i}/${total} (working=${#W[@]}, culprits=${#CULP[@]}): +${#batch[@]}"
    if test_subset "batch@${i}" "${W[@]}" "${batch[@]}"; then
      W+=("${batch[@]}"); log "  batch OK -> working=${#W[@]}"
    else
      log "  batch FAILED -> scanning ${#batch[@]} mods individually..."
      for m in "${batch[@]}"; do
        if test_subset "try $m" "${W[@]}" "$m"; then
          W+=("$m")
        elif test_subset "reconfirm $m" "${W[@]}" "$m"; then
          W+=("$m"); log "  ~ recovered on retry -> kept $m (flaky fail)"
        else
          CULP+=("$m"); log "  >>> CULPRIT: $m (failed 2x)"
        fi
        save_state
      done
    fi
    i=$((i+BATCH)); save_state
  done
  log ""
  log "===== REBUILD COMPLETE ====="
  log "Working : ${#W[@]} mods"
  log "Culprits: ${#CULP[@]} -> ${CULP[*]}"
  write_config "${W[@]}"; cp "$MODSCONFIG" "$WORKING_OUT"
  { echo "WORKING (${#W[@]}):"; printf '  %s\n' "${W[@]}"; echo; \
    echo "CULPRITS (${#CULP[@]}):"; printf '  %s\n' "${CULP[@]}"; } > "$RESULT"
  log "Applied working modlist as live config. Copy: $WORKING_OUT  Report: $RESULT"
  exit 0
fi

if [ ! -x "$RW_BIN" ]; then log "ERROR: RimWorld binary not found/executable: $RW_BIN"; exit 1; fi

# ---- sanity gates ---------------------------------------------------------
log "Sanity 1/2: baseline alone (no candidates) should PASS..."
if test_subset "baseline-only"; then log "  baseline OK."; else
  log "  baseline FAILED — environment problem, not a mod. Aborting."; restore_safe; exit 1
fi
log ""
log "Sanity 2/2: full candidate set should FAIL (reproduce the crash)..."
if test_subset "full-set" "${candidates[@]}"; then
  log "  full set PASSED — the crash did NOT reproduce headlessly via -quicktest."
  log "  (The crash may need a specific scenario/start. Restoring config; bisect can't proceed.)"
  restore_safe; exit 0
else
  log "  full set FAILED as expected — beginning binary search."
fi
log ""

# ---- binary search --------------------------------------------------------
suspects=("${candidates[@]}")
round=0
while [ "${#suspects[@]}" -gt 1 ]; do
  round=$((round+1))
  n="${#suspects[@]}"; half=$(( (n+1)/2 ))
  first=("${suspects[@]:0:half}")
  second=("${suspects[@]:half}")
  log "Round $round: $n suspects -> split ${#first[@]} / ${#second[@]}"
  if test_subset "round${round}-first" "${first[@]}"; then
    # first half PASSED -> culprit in second half (or interaction)
    log "  first half OK; testing second half..."
    if test_subset "round${round}-second" "${second[@]}"; then
      log "  BOTH halves pass alone but full set fails -> INTERACTION between the two halves."
      log "  First-half group:"; printf '    %s\n' "${first[@]}" | tee -a "$RUNLOG"
      log "  Second-half group:"; printf '    %s\n' "${second[@]}" | tee -a "$RUNLOG"
      printf 'INTERACTION between two halves — see %s\n' "$RUNLOG" > "$RESULT"
      break
    else
      suspects=("${second[@]}")
    fi
  else
    suspects=("${first[@]}")
  fi
  log "  -> ${#suspects[@]} suspect(s) remain"
  log ""
done

log ""
if [ "${#suspects[@]}" -eq 1 ]; then
  log "================ CULPRIT: ${suspects[0]} ================"
  echo "${suspects[0]}" > "$RESULT"
fi
log "Done. Full transcript: $RUNLOG"
restore_safe
