# The Console — Music Rig Setup Brief

*A handoff spec for implementing a headless Mac-mini music rig's control/monitor/record layer. Written for a fresh Claude instance with no prior context. Self-contained.*

---

## 0. What this is, and what it is not

You are helping set up the **host-side control surface, metering, recording, and rig-automation** for a personal music rig. This is a hardware + macOS-automation + OSC project. It is **not** the "noto" app itself.

**noto** is a separate self-hosted personal-archive web app that runs in **Docker** on the same Mac mini. This brief touches noto at exactly one point: a filesystem **inbox** (`~/noto-inbox/`) where recordings and marker files are dropped, which noto later ingests and analyzes. **The audio analysis / ML / region-detection features of noto are out of scope here** — they are a later phase. Your job is everything that produces files and controls the rig.

**The single most important architectural fact:** noto runs in a Docker container, and **Docker on macOS cannot see host audio hardware (CoreAudio), the RME interface, keyboard/HID events, or WiFi.** Therefore *everything* in this brief runs **natively on the macOS host** and communicates with noto only by writing files into `~/noto-inbox/`. Do not attempt to drive audio hardware, HID, or networking from inside the container. The inbox is the seam.

Everything host-side also shares the Mac's **NTP-correct system clock**, which is why we record and timestamp on the host rather than on the standalone recorders (whose internal clocks drift).

---

## 1. The rig

**Hardware present:**
- Mac mini (assume Apple Silicon unless told otherwise) — the rig brain, effectively headless (has a display, but it's awkwardly placed / not comfortably usable).
- RME Fireface UCX II audio interface.
- Ableton Live + Ableton Push 3.
- Surface Go tablet (Windows) — spare, to be repurposed as a touch control surface.
- Zoom H1e field recorder (relevant only to noto ingest later; ignore for this brief).

**Hardware to acquire:**
- Pocket travel router (GL.iNet-class) — network consistency.
- 3×3 macropad — QMK or KMK firmware (e.g. Adafruit MacroPad RP2040, or any 9-key QMK pad). Must send distinct key/hotkey events macOS can bind.

**Software to install (all free unless noted):**
- **Open Stage Control** (free, open-source) — the OSC surface server. *Recommended.* Alternative: TouchOSC (~$10, Hexler).
- **AbletonOSC** (free remote script, ideoforms/AbletonOSC) — exposes Ableton's Live Object Model over OSC.
- **Hammerspoon** (free) — macOS automation; binds macropad keys to Lua/shell actions.
- **Recorder** — either **ffmpeg** (free) or **Audio Hijack** (~$70, Rogue Amoeba, nicer UX).

---

## 2. The three interaction layers

| Layer | Device | Interaction | Job | Software |
|---|---|---|---|---|
| **Keys** | 3×3 macropad | Physical | OS/rig control — works when the GUI is wedged | Hammerspoon |
| **Touch** | Surface Go | Interactive | Mixing (RME) + Ableton control Push can't reach | Open Stage Control (browser) |
| **Display** | Rig's main screen | Read-only | Just show levels | Open Stage Control (browser, meters-only page) |

Design rule: **keys own the OS, touch owns OSC, the display just watches.** No overlap — OSC can't force-quit a frozen app; a meter page isn't interactive.

---

## 3. Networking (do this first — everything depends on it)

**Goal:** the Mac mini and the control tablets (Surface Go, and an iPad if used) must be on the same local network with the **same SSID/subnet at home and at a venue**, so OSC and noto's web UI "just work" everywhere.

**Recommended: travel router.**
- Mac mini connects to the router by **Ethernet** (wired = reliable for a rig).
- Router broadcasts **one SSID everywhere**. Surface Go / iPad join it.
- At a venue it runs as a local island (no internet needed — OSC and noto are local). At home, uplink its WAN to the house network if internet is wanted.
- This sidesteps all macOS access-point limitations.

**Why not a USB WiFi AP (rejected):** macOS has no USB-WiFi drivers on Apple Silicon, and its Internet Sharing AP only runs on the built-in radio. A single WiFi radio also cannot be client and AP simultaneously (802.11 MAC constraint). So "onboard=client + USB=AP" is not viable.

**Fallback if no router:** built-in WiFi mode-toggle — client at home, AP via Internet Sharing at venue (works without internet upstream) — flipped by a macropad key. Toggling Internet Sharing from a script is hacky (`com.apple.nat` plist + `launchctl`) and home/venue end up on different subnets. Only if you refuse the router.

---

## 4. RME TotalMix: loopback record bus + OSC feed

Two setup tasks in TotalMix FX, both config (no code).

### 4a. Loopback record bus (for recording software/rig output)
- Pick an **unused hardware output pair — ADAT 7/8** — as a dedicated record bus.
- In TotalMix submix mode, route the software-playback channels (and/or hardware inputs) you want captured **to ADAT 7/8 OUT**.
- Enable **Loopback on ADAT 7/8 OUT**. The signal now appears on **ADAT 7/8 IN**, recordable by any host app.
- Loopback disconnects the *physical* input on that channel — harmless here because ADAT 7/8 are unused. (Never put loopback on the main out, or you lose the main input.)

### 4b. OSC meter + control feed
- In TotalMix **Options → OSC**: enable OSC, set the remote target to the **Open Stage Control server** (host IP/port), and turn on **"Send Peak Level"** (this is what emits meter data).
- Meter addresses look like `/1/level2Left`. **Known quirk:** channels are banked (up to ~48 faders/bank); reaching channels beyond the first 16 requires bank configuration. Nail down the exact addressing for the actual channel count during implementation.

---

## 5. OSC surface — Open Stage Control (server model)

**Why Open Stage Control over TouchOSC for this rig:** it's a **server + browser-client** architecture. The server (on the Mac) receives TotalMix's OSC **once** and fans it out to any number of browser clients over websockets. So:
- **Rig display** → fullscreen browser showing a **meters-only** page (passive).
- **Surface Go** → browser showing the **interactive mixer + Ableton** page.
- TotalMix only sends OSC to **one** destination (the server), sidestepping multi-target config. Bidirectional throughout. Free.

(TouchOSC remains a valid alternative if the user prefers its editor, but then each device runs the app and TotalMix must target each — more config.)

### Build tasks
1. Install Open Stage Control on the Mac; run its server.
2. Point TotalMix OSC at the server; confirm **live meters render** (moving, not just static faders) — **VERIFY THIS EARLY**, it's the load-bearing assumption for the whole meter layer.
3. Build the **meters-only page** → open fullscreen in a browser on the rig's main display.
4. Build the **interactive mixer page** (big legible faders/meters — the user's explicit goal is to beat TotalMix Remote's cramped UX) → open in the Surface Go's browser.
5. Add **Ableton control widgets** to the Surface Go page (see §6).

---

## 6. AbletonOSC — Ableton control + optional per-track meters

**Install:** AbletonOSC as a Control Surface (Live's Remote Scripts folder; select it in Live's Link/MIDI prefs). It listens on UDP **11000**, replies on **11001**. It **coexists with Push 3** (Live supports multiple control-surface slots) — *verify no conflict during setup.*

**Primary use — control Ableton things Push 3 can't:**
- Clip **loop length / loop points** via clip properties (`loop_start`, `loop_end`, `looping`) and similar. **VERIFY the exact OSC address strings** against the installed AbletonOSC version (the API is `/live/clip/get|set/<property>` with a track+clip index).
- Any other LOM-exposed parameter the user wants surfaced.

**Optional — per-track meters:** `output_meter_level` per track, via `start_listen`. **Caveat:** continuous meter feedback creates heavy OSC traffic and can slow Live. If implemented: rate-limit at the server, and subscribe **selectively** (visible/armed tracks), not all tracks blindly. Treat RME meters as the accurate/authoritative levels; Ableton track meters are coarse "what's playing" context.

---

## 7. Recording (host-side → inbox)

**Goal:** a timestamped WAV of the software/rig output lands in `~/noto-inbox/`, timestamped by the **Mac clock** (always correct).

**Decide the source with the user:**
- **(a) Tap an app's audio directly** (e.g. Ableton, or system audio) — no loopback needed, no channel cost, no feedback risk. Best done with **Audio Hijack** (source = app, filename template with date/time, hotkey/menu-bar trigger).
- **(b) Record the full RME output mix** (everything summed at the record bus) — use the **ADAT 7/8 loopback** from §4a, recorded by any host recorder.

**ffmpeg option (free, scriptable, integrates with a macropad key):**
```bash
# List devices to find the RME input index:
ffmpeg -f avfoundation -list_devices true -i ""
# Record the loopback input to the inbox, Mac-clock timestamp:
ffmpeg -f avfoundation -i ":<RME_INPUT_INDEX>" -c:a pcm_f32le \
  ~/noto-inbox/rme-$(date +%Y%m%d-%H%M%S).wav
```
(avfoundation channel selection on a multichannel RME can be fiddly — expect to map channels.)

**Audio Hijack option (paid, nicer):** source block → recorder block with a date/time filename template writing into `~/noto-inbox/`, triggered by hotkey. Note: Audio Hijack does **not** reliably support live marker insertion mid-record — use the marks sidecar (§8) instead of trying to embed markers.

A start/stop **record key** on the macropad (triggering the ffmpeg script, or an Audio Hijack hotkey) is a good idea.

---

## 8. Macropad + Hammerspoon (the physical keys)

**Hardware:** flash the 3×3 pad (QMK/KMK) so each key sends a **distinct hotkey** (e.g. `hyper+F13..F21`). **Software:** Hammerspoon binds those hotkeys to Lua/shell actions.

**Proposed 9-key layout** (finalize with user):

| Key | Action | Implementation notes |
|---|---|---|
| 1 | Boot Ableton clean (dismiss file-recovery dialog) | Watch for Live launch; click dialog button via accessibility (`hs.axuielement`) or AppleScript System Events |
| 2 | Force-restart Ableton (OOM-wedge rescue) | `killall` Live process + relaunch. This is the key that matters most when the GUI is frozen |
| 3 | Clean shutdown (**deliberate: hold / double-tap**) | `osascript -e 'tell app "System Events" to shut down'` |
| 4 | Write "interesting" mark | Append Mac-clock ISO timestamp to marks sidecar (§8a) |
| 5 | Start/stop recording | Trigger the §7 recorder |
| 6 | WiFi mode toggle (only if no travel router) | else spare |
| 7–9 | Spare / future | e.g. mute monitors, launch Ableton, toggle meter page |

**Safety rule:** destructive keys (shutdown, kill) require a deliberate gesture (hold or double-tap) so an accidental brush can't end a take or kill the rig mid-session.

**Optional enhancement:** a Hammerspoon watcher on Live's memory footprint that warns *before* it wedges, demoting key #2 from routine-use to backstop.

### 8a. Marks sidecar format
When the mark key is pressed, append one line to a sidecar file in the inbox (e.g. `~/noto-inbox/marks-<date>.jsonl`):
```json
{"ts": "2026-07-18T21:14:07.312-07:00", "kind": "interesting"}
```
noto later aligns these wall-clock timestamps to whichever recording was active at that moment, dropping region markers precisely. (This is the software analog of a field recorder's mark button, but with an accurate clock.) Keep the format simple and documented; noto's ingest will read it.

---

## 9. Suggested build order

1. **Travel router** — consistent network. Foundational; do first.
2. **TotalMix** — loopback record bus (ADAT 7/8) + enable OSC with Send Peak Level.
3. **Open Stage Control** — server up, receiving TotalMix OSC; build the **meters-only page on the rig display**. First visible win; also proves the meter feed.
4. **Interactive mixer page** on the Surface Go.
5. **AbletonOSC** — install; add Ableton control widgets (loop lengths); optionally selective per-track meters.
6. **Recorder** — host recording from the chosen source → `~/noto-inbox/` with Mac-clock timestamps.
7. **Macropad + Hammerspoon** — bind keys; implement OS actions + marks sidecar.
8. *(Out of scope — noto)* ingest + analysis of the inbox.

Each step is independently testable and useful on its own.

---

## 10. Facts established vs. to-verify

**Established (researched/tested this session):**
- Docker on macOS **can** see a dynamically-mounted `/Volumes` disk inside an already-running container (tested) — relevant to noto's later card-import, not this brief.
- Docker on macOS has **no** Metal/GPU passthrough to containers (CPU-only) — relevant to noto's later ML, not this brief.
- TotalMix **loopback**: output → same-numbered input (RME docs).
- TotalMix **OSC** emits level meters when "Send Peak Level" is on (RME forum/docs).
- **AbletonOSC** exposes `output_meter_level` + `start_listen`; meter feedback is heavy and must be rate-limited/selective (community docs).
- macOS: single WiFi radio can't be client+AP at once; no USB-WiFi drivers on Apple Silicon; Internet Sharing AP works without upstream internet.
- RME UCX II DURec records inputs **and** outputs, standalone; RTC timestamps files but battery-backup is unconfirmed (moot — we record on the host instead).

**To verify during implementation:**
- [ ] The OSC tool's TotalMix view renders **live moving meters**, not just faders. (Highest priority — the meter layer depends on it.)
- [ ] Exact **AbletonOSC addresses** for clip loop start/end on the installed version.
- [ ] TotalMix OSC **channel addressing** beyond the first 16 channels (bank config) for the actual channel count.
- [ ] **AbletonOSC + Push 3** coexist without a control-surface conflict.
- [ ] avfoundation **channel mapping** for recording the specific RME loopback input.

---

## 11. Open decisions for the user

- **OSC tool:** Open Stage Control (recommended, free, server-fans-out) vs TouchOSC (paid, polished editor).
- **Recorder:** ffmpeg (free, scriptable, macropad-friendly) vs Audio Hijack (paid, nicer UX).
- **Record source:** (a) tap an app directly vs (b) full RME output mix via ADAT 7/8 loopback.
- **Networking:** travel router (recommended) vs built-in-WiFi mode-toggle key.
- **Macropad model** and final key assignments.
