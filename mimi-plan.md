# 耳 — Nōto Learns to Listen

*A design for audio in nōto: import, listening, regions, and taste.*

---

## The jam problem

You sat down with a guitar for two hours. Somewhere in there — maybe minute 34, maybe minute 71 — you played the thing. You know you did. You felt it at the time. And now there's a 1.4 GB WAV on a microSD card and the honest truth is you will never listen to it again, because listening to it again costs two hours and you don't have two hours, and even if you did, you'd have to *find* the moment, which means scrubbing, which means the moment goes past at 8× and sounds like a wasp.

So the recording sits there. And the next one sits next to it. And after a year you have a hundred hours of your own playing that functions, practically speaking, as a landfill.

This is not a storage problem. Storage is solved and cheap. This is an **attention** problem: you have far more recorded music than you have ears to hear it with, and the bottleneck is not the disk, it's you.

Which means the tool's job is not to store your audio. It's to **spend attention on your behalf** and hand you back a shortlist.

---

## You have already solved this once

Here's the thing that should make this whole design feel obvious rather than novel.

Read `noto-vision.md` again — the origin story. You had 1,100 assistant messages across four conversations, and the question was *which of these actually landed?* Not which were well-written. Which ones **named something you already sensed**, or **opened a direction you hadn't considered**.

And the answer was: a sliding window. Forty messages wide, twenty of stride, "like a Hanning window in signal processing." Move it across the corpus. At each stop, have a machine read the surrounding context and pull out the passages that carried something. Classify each one — **Mirror** or **Catalyst**. Overlap the windows so nothing falls in a boundary crack. Filter the noise. 315 survive out of 1,100.

That is *exactly the same problem as the jam*, and you already know the shape of the answer. You even described it in signal-processing metaphor, which is funny, because this time the signal is an actual signal.

So this is not a new subsystem bolted onto nōto. It is nōto's founding move, run over sound instead of text.

The archetypes even survive the translation. Mirror and Catalyst become:

- **Keeper** — this is finished, or close. It named something. Don't lose it.
- **Seed** — this isn't finished, but there's something in it. Come back and grow it.

**My leaning:** keep those two and resist adding a third. The whole value of Mirror/Catalyst was that two categories force a decision and three invite a shrug.

---

## What the machine can actually hear

Now the honest part, because this is where audio ML projects go to die. People imagine the computer "listens and finds the good bits." The computer does no such thing. The computer measures things. Some of those measurements correlate with good bits. Let's be specific about which, ranked by how much I actually trust them.

### 1. The mark button (trust: total)

This one made the whole design better and it isn't ML at all.

**The H1e has a mark button.** Up to 99 marks per file, pressed live, while you play. Rewind and fast-forward jump between them.

Every heuristic below this line is a *guess at what you would have pressed*. If you just press it, there's no guessing. It's the only signal in the entire system that is not an inference — it's you, at the moment, saying *that one*.

Two caveats: pausing auto-inserts a mark, so not every mark is intentional (we can spot those — they land at pause boundaries). And you have to remember to press it, which mid-flow is exactly when you won't. So it's ground truth when present, not a substitute for the rest.

*(⚠️ Unverified: whether marks are written as a standard RIFF `cue ` chunk or something Zoom-proprietary. Zoom's manual documents the feature and never says what it writes. This is question #2 of the three below.)*

### 2. Return (trust: high)

**If you played a figure and then came back to it, you liked it.** You may not have decided to like it. Your hands decided.

This is measurable — chroma self-similarity across the recording, looking for off-diagonal structure. A phrase at 12:30 that closely matches something at 34:10 and again at 51:02 is a phrase you kept reaching for. In two hours of free playing, *recurrence is preference leaking out through your hands.*

I trust this more than any spectral heuristic, because it's a behavioral trace rather than an aesthetic judgment. The machine isn't deciding the music is good. It's noticing that you kept going back.

### 3. Settle (trust: medium-high)

Improvisation has a texture to its failure. When it isn't working, tempo wanders, key drifts, you stop and restart. When it *is* working, things lock: tempo variance drops, harmonic center holds, the energy stops thrashing and starts *arcing*.

So: sustained stretches of low tempo-variance and stable key, especially after a period of instability, are flow states. That's the "you found it" signature. It's measurable with beat tracking and chroma stability.

### 4. Voice (trust: medium-high, and cheap)

**You talk during jams.** Everyone does. "Ooh." "That's it." "Again, again." "Wait, what was that." "Okay from the top."

Whisper, gated by voice activity detection so it only runs on the ~2% of the recording that's speech, turns those into timestamped verbal bookmarks. It's nearly free and it's remarkably high-signal, because you only say "that's it" when it's it.

This also matters for field recordings, where you narrate — and it's the one place where the existing `tech-plan.md:158-160` (`TranscriptionService`, `TranscriptionWorker`, never built) finally gets its moment.

### 5. Novelty (trust: medium)

Foote novelty over a self-similarity matrix — the classic. Finds *boundaries*: where the music changes character. Good for cutting the recording into candidate regions. Mediocre for ranking them, because "different" and "good" are not the same thing and free improvisation is full of changes that are just you getting bored.

**Use novelty to cut, not to score.** This distinction matters and most naive implementations blur it.

### 6. Semantic similarity (trust: unknown, but the highest ceiling)

This is the audio-text embedding idea, and it's the one that could make the whole thing feel like magic: models that put audio and text in a **shared** vector space. Embed a 30-second region of audio; embed the string *"sparse, dark, a lot of space"*; measure cosine distance between them. If the space is any good, you can **search your jams in English.**

And — because it's one space — you can also do region-to-region: *"find me other moments that sound like this one"* across every recording you own. Across years.

**⚠️ Honest confidence flag:** the research pass on this got stopped before it reported, so what follows is from my own knowledge and is stale by construction. LAION-CLAP is the model family I know (512-dimensional, checkpoints trained on AudioSet and a music/speech variant). It works, it's open, it runs on modest hardware. Whether it's still the right choice — versus MERT, MuQ-MuLan, or something from 2025-26 I don't know about — **I have not verified and should not pretend to have.** Before we build on it, that's a real research question, not a rubber stamp.

What I'm confident about is the *shape*: a shared audio-text space is the right abstraction, and nōto's schema (below) is ready for it regardless of which model wins.

### And what it cannot hear

Taste. It cannot hear taste.

Nothing above knows whether *you* like the thing. Return is a proxy. Settle is a proxy. Every score in this system is a proxy, and I want that stated plainly in the design doc rather than discovered later with disappointment.

Which is precisely why the last section of this plan is the important one.

---

## Mimi: the ear

None of the above is expressible in C#. There's no librosa for .NET, and there won't be. So the analysis lives in a **Python sidecar**, and this is not a compromise — it's the pattern nōto already uses. Look at `docker-compose.yml`: Ollama is a sidecar. It's reachable at a URL. The URL is a config value (`Embeddings__Endpoint`). The app doesn't know or care where it runs.

So: **`mimi`** (耳, *ear* — and it rhymes with ノート's habit of borrowing sounds). A small FastAPI service.

```
POST /probe        → duration, sr, channels, peak_dbfs, bext, ixml, cues
POST /measure      → rms, novelty curve, onsets, chroma, tempo, key, silences
POST /cut          → candidate regions [{start, end, why[]}]
POST /embed/audio  → vector for a time span
POST /embed/text   → vector for a string  (same space — this is the whole trick)
POST /transcribe   → VAD-gated whisper segments
```

### Where does it run? (the fork that dissolves)

Docker Desktop on macOS runs a Linux VM, and **there is no Metal/MPS passthrough into Linux containers.** So a containerized `mimi` is CPU-only, which for CLAP-scale models is fine-ish and for Whisper-large is painful.

The tempting conclusion is "run it natively on the host for the GPU," which breaks the single-`docker compose` workflow you deliberately built in commit `0788113`.

But look again at the Ollama precedent and the fork disappears:

```yaml
Ears__Endpoint: "http://mimi:7070"              # compose default, CPU
# Ears__Endpoint: "http://host.docker.internal:7070"   # native on host, Metal
```

**My leaning:** ship it in compose, CPU, because `docker compose up` staying true is worth more than throughput on day one — you're batch-analyzing overnight, not serving requests. If it turns out to be too slow, running `mimi` natively is *one environment variable*, not a rewrite. Design for the escape hatch, don't take it preemptively.

*(⚠️ The Metal-passthrough claim is from my knowledge, not this session's research. It's a stable, well-known limitation and I'd be surprised if it changed, but it deserves a five-minute check before we commit — because if it's wrong, the fork gets much easier.)*

### The LLM does not listen

One deliberate design choice: **the language model never touches the audio.** It reads the *measurements*.

`mimi` produces structured facts — "3:41–5:10, tempo stabilizes at 96 after two minutes of drift, chroma centers on D minor, this material recurs at 34:10 and 51:02, you said 'again' at 5:04." The LLM turns that into the sentence you actually read:

> **3:41 – 5:10** · sparse, and the tempo settles here for the first time. You find a figure and come back to it twice more later. You said "again" at the end of it.

That's cheap, it's local-ish, it's honest, and — critically — **it can explain itself.** Every claim traces to a measurement. When the ranking is wrong, you can see *why* it was wrong, which is the difference between a tool you can calibrate and a magic box you learn to distrust.

---

## Everything is an entity, including a moment

Here's where nōto's schema turns out to have been quietly ready for this the whole time.

Two new entity types. **No migration to the core tables.**

**`recording`** — one per imported session. The attachment points at the master WAV. `meta` carries source, duration, sample rate, peak, content hashes, `recorded_at` and *where that timestamp came from*.

**`region`** — a span of time inside a recording. `meta` carries `{recording_id, start_sec, end_sec, score, why[], archetype}`. Its `body` is the note **you** write.

And a region is an *entity*, which means — for free, with no new code —

- it gets **embeddings** (`embeddings` table already keys on entity)
- it gets **links** (`links` already goes entity→entity)
- it can be **threaded** (threading is just a link)
- it appears in the **stream**
- it's found by **search**
- it can be cited by a **work**
- it can link to a **song**

You didn't design the entity model for audio and it absorbs audio without a seam. That's not luck, that's what "the five things" bought you.

### Two embedding providers, one table

This is the detail that made me grin. `embedding_providers` is already a **table**, with a per-row `dimensions` column, and the unique index is `(entity_id, provider_id)` — *per provider*. So:

| provider | dims | embeds |
|---|---|---|
| `mxbai-embed-large` | 1024 | the **text** of regions and notes |
| `clap` (or successor) | 512 | the **audio** of regions — and text queries into the same space |

They coexist. No schema change. A text query gets embedded by *both* and searches *both*: text-space finds your written notes, audio-space finds the actual sound. Union the results.

Two small real things this touches, so we're not surprised later:

- `EmbeddingService.EmbedEntity` (`src/Noto.Server/Services/EmbeddingService.cs:84`) hardcodes `$"{Title} {Body}"` → text embedding. The audio provider needs its own path. Provider dispatch on the existing `Provider` column (`"ollama"` → `"clap"`), not a parallel service.
- `GetUnembeddedEntityIds` filters `Body != null && Body != ""`. A freshly-cut region has audio and *no body yet* — it'd be skipped. The audio worker needs its own query.
- (While we're in there: `SearchSemantic` returns `Distance` as a hardcoded `0.0`. Pre-existing, unrelated, but it'll bite the moment we want to rank across two vector spaces — you can't merge two result sets without real distances.)

---

## The taste vector

Everything so far is scaffolding for this.

You mark regions as Keepers. Thirty of them, over six months. Each one has an audio embedding. Now:

**Rank the regions of every new jam by their similarity to the regions you have kept.**

Not by novelty. Not by RMS. Not by any hand-tuned weight I guessed at in this document. By *proximity to the things you already said yes to.* The system stops applying a general theory of good music and starts applying **yours**.

And this is the same loop the vision doc already describes — *"Something lands. You mark it as resonant. It feeds back into your material."* Same loop. New sense organ.

**One strong opinion:** use **k-NN against the kept regions, not a centroid.** This matters more than it sounds. Taste is multimodal — you probably love sparse dark ambient drones *and* you love a driving groove. Average those two into a single centroid vector and you get the midpoint, which is *neither*, and which nothing you'd actually like sits near. You'd have built a machine that reliably recommends lukewarm. Nearest-neighbor against the actual kept set preserves the clusters and lets you have several tastes at once, which you do.

The rejections are data too. A region proposed and dismissed is a labeled negative, and after enough of them the ranking can learn the difference instead of re-proposing the same kind of noodling forever.

**And the honest caveat:** this needs volume before it's worth anything. Thirty keepers minimum, realistically more. Which is why it's phase 5 and not phase 1 — it's the reward for the ritual, not the entry fee.

---

## One door

Your actual ask: *"building the right simple workflow that helps me import stuff."*

Three sources — RME masters, H1e field, H1e jams — and the temptation is three doors. Resist it. **One folder.**

```
~/noto-inbox/          ← drop anything here. that's it. that's the workflow.
  _imported/           ← files move here once they're safely in
```

Drop files in, or `cp -r /Volumes/H1E/*.WAV ~/noto-inbox/`, and walk away. A poller watches it — **poll, not inotify**, because filesystem events don't reliably cross Docker's bind mounts on macOS, and because `scripts/run.sh` already establishes polling as the house style with its `.reload` sentinel. It waits for the file size to stop changing (you're still copying), then ingests.

**Why one door and not three:** classification is *cheap* — a few sampled windows through the audio-text model tells you music-vs-nature-vs-speech in seconds. The expensive part is the full analysis. So classify first, then run the right pipeline. The taxonomy is a **proposal shown in the review screen**, not a decision you have to make while holding a card reader. Which is also just what the vision doc demands: *"auto-suggests... but never acts without confirmation."*

Provenance (RME vs H1e) doesn't need asking at all — it's sitting in the file metadata. The H1e writes iXML with its own fingerprint; a DAW bounce doesn't.

*(Optional `jams/` and `field/` subfolders as overrides, for when you already know and want to skip the guess. Hints, not requirements.)*

### What ingest actually does

One streaming pass, because you're already reading every byte off the card and the extra work is free:

1. **Hash twice** — whole file (exact dupes) *and* the `data` chunk payload alone (same audio, edited metadata). The second one is the important one. BWF MetaEdit has done exactly this for years; match their definition so the numbers are comparable.
2. **Probe** — duration, sample rate, channels, and **peak**, which is not optional here (see below).
3. **Timestamp** — from the filename if you've set `YYMMDD-HHMMSS` mode, else `bext`, else mtime. **Store which one it came from.** Never present a guessed timestamp as a fact — a field recording labeled with the wrong dawn is worse than one labeled *unknown*.
4. **Reassemble the splits** — the H1e cuts a new file every 2 GB (~87 min at 48k, ~43 at 96k) and writes iXML `FAMILY_UID` / `FILE_SET_INDEX` / `TOTAL_FILES` precisely so they can be put back together. Almost nobody reads those fields. We will. **One session is one `recording` entity, regardless of how many files it arrived as.**
5. **Proxy + peaks** (below).
6. Create the entity, queue analysis, move the source to `_imported/` so the inbox self-empties and pending work is visible at a glance.
7. **Skip `/Trash/`** on the card (those are takes you deleted) and `/Export/` (derived renders of files you already have).

### Then: the review screen

This is the ritual, and it's the actual product.

> **3 recordings · 47 min · 14 regions**
> *jam · yesterday 21:14 · 22 min*
> — 3:41 you found something, returned to it twice
> — 11:02 you said "that's it"
> — 18:30 sparse, tempo settles

Keep, dismiss, note. That's the whole interaction. It's the Saturday-morning scroll from the vision doc, and it takes four minutes instead of two hours.

---

## The waveform that doesn't die

Blunt: `/annotate` as it exists today will not survive contact with your real files, and it's worth being precise about why, because two separate things kill it.

`AudioAnnotate.razor:186` base64-encodes the entire file and passes it through `JS.InvokeVoidAsync("eval", ...)`, capped at 100 MB. And then wavesurfer decodes the whole thing in WebAudio to draw the waveform. A 40-minute 32-bit-float H1e file is **~1 GB on disk and ~1.8 GB decoded in RAM.** That's not slow, that's a dead tab. (The annotations are also held in a `List<Annotation>` in memory and flattened into one capture's `meta` blob — fine for a prototype, but they need to be entities now.)

The fix is the standard one:

- **Never serve the master to the browser.** Serve an **Opus proxy** — 96k for field, 128k for music. That's a ~64× bandwidth reduction and it moots every 32-float browser-compatibility question at once.
- **Precompute the peaks** server-side. The waveform renders instantly and no audio is decoded to draw it.
- **Range requests** so scrubbing works — ASP.NET gives this away free on static files, and Caddy passes it through.

### The 32-float trap

The H1e records **32-bit float and nothing else** — there is no 16/24-bit mode on the device. The entire point of 32-float is that it doesn't clip: **samples legitimately exceed 0 dBFS.**

Which means the naive pipeline breaks in two places at once. Encode those samples straight to Opus and they distort. Draw peaks from them unscaled and they flat-top at the rail — *your waveform lies to you*, showing a solid block where there's actually dynamics.

So: **measure peak once at ingest, store it, apply corrective gain only to the proxy and the peaks.** The master is never touched.

And do **not** blanket-normalize on ingest, however tempting. Normalizing destroys the relative-level information that makes an archive worth having — a quiet dawn chorus *should* read quiet sitting next to a loud jam. That difference is data. Throwing it away to make the waveforms look nice is the kind of thing you regret in year three.

### One real fork: wavesurfer or peaks.js

`audiowaveform` (BBC's tool, the standard for precomputed peaks) emits **interleaved min/max integers**. wavesurfer v7 wants **per-channel arrays of normalized floats**. These do not interoperate. Nothing warns you.

- **peaks.js** eats `audiowaveform` output natively — same BBC family, built for long-form radio scrubbing, which is genuinely closer to your problem than wavesurfer's usual use case.
- **wavesurfer v7** needs ~10 lines of conversion, but you already have it vendored (`wwwroot/lib/js/wavesurfer.min.js`), the annotate page is already built on it, and its **regions plugin** is exactly the UI this feature needs.

**My leaning: stay on wavesurfer.** The conversion is trivial, the regions plugin is the core interaction, and the 2 GB auto-split means your files cap around 87 minutes rather than the multi-hour monsters peaks.js was built to survive. Rewriting onto peaks.js is a bigger detour than the thing it fixes. Revisit if scrubbing an 87-minute file actually feels bad — measure first.

---

## Three kinds of listening

The sources aren't just different files, they want genuinely different attention:

**Jams (H1e)** — the main event. Everything above: return, settle, voice, novelty. Long, messy, mostly noodling, occasionally transcendent.

**Takes (RME + Mac)** — deliberate. You already decided this one mattered before you hit record. So don't hunt for the good bit; the whole thing is the good bit. What's useful here is *comparison* — take 3 vs take 7 — and linking to the song it belongs to. **Ranking regions inside a deliberate take is mostly a waste of compute.**

**Field (H1e)** — a different problem entirely. Nothing to do with music. You want **events** in an ocean of ambience: the bird, the sudden wind, the water changing, the twelve seconds where a truck ruined everything. Music-structure analysis is the wrong tool; event detection and audio-text search are the right ones. And the audio-text model shines here — *"rain on leaves"*, *"single bird, close"*, *"distant water"* — because that's much closer to what those models were trained on than free improvisation is.

Same door, same schema, three pipelines.

---

## From noodling to song

The quiet payoff, and it costs almost nothing.

You already have `Songs.razor`. You already have chord sheets, ChordPro, transposition, diagrams. You already have `links`.

A region is an entity. A song is an entity. **Link them.**

So a song stops being just a chord sheet and starts carrying its own origin: *the actual eleven seconds, in a room, in March, where the idea arrived.* Not a memory of it. The audio.

And run it backwards too: open a jam from a year ago and see that minute 34 became the bridge of the thing you're playing now. The archive stops being a landfill and becomes a **lineage**.

That, I think, is the actual answer to *"help me in making music."* Not a robot critic scoring your playing. A system that remembers where your songs came from, and can find you the moment again.

---

## The order to build it

Deliberately, the first two phases contain **no machine learning at all** — and they're already a tool worth having. That's not caution, it's structure: if the ML disappoints, you still have a real thing. Ship value before the magic.

**Phase 0 — One file.** Drop a single H1e WAV somewhere. Three undocumented questions fall out in about a minute: does `bext` carry a real `OriginationDate`; are marks a standard `cue ` chunk; is the iXML `FILE_UID` stable. All three change the ingest design. Nothing gets written before this.

**Phase 1 — The Door.** Inbox poller, ffmpeg, hash + dedup, split reassembly, Opus proxy, peaks, `recording` entity. *Result: your audio is in nōto, playable, scrubbable, deduplicated. No AI.*

**Phase 2 — The Hands.** Rewrite `/annotate`: load from the library, precomputed peaks, regions as entities, keyboard-driven. *Result: you can mark and note moments by hand. Still no AI.*

**Phase 3 — The Ear.** `mimi` sidecar: measure, cut, rank. Regions proposed with reasons. *Result: the shortlist.*

**Phase 4 — The Words.** Audio-text embeddings + VAD-gated Whisper. *Result: search your jams in English; find similar moments across years.*

**Phase 5 — The Taste.** k-NN over your keepers. *Result: it starts ranking like you.*

**Phase 6 — The Loom.** Region → song links. *Result: lineage.*

---

## What I'm not sure about

In the spirit of not overselling:

- **The audio-text model choice is unverified.** The research pass got stopped. CLAP is what I know; whether it's still state of the art in 2026, and how well it handles *free improvisation* specifically (as opposed to the tagged, produced music these models train on), is an open question that deserves a real answer before phase 4.
- **The scoring weights are guesses.** Return × settle × voice, weighted somehow. I have opinions about the *ingredients*; the recipe is unknowable until you've rejected a few hundred proposals. This is fine — that's what phase 5 fixes — but phase 3 will feel dumber than you want, and it should be built to be *visibly* dumb rather than confidently wrong.
- **Free improvisation is genuinely hard for MIR.** Nearly every music-structure algorithm was built and evaluated on pop songs with verses and choruses. Your material has neither. Some of this will just work worse than the papers promise.
- **Whisper on CPU in a container may be too slow** to be pleasant, which is the most likely reason we end up moving `mimi` to the host.

None of these threaten phases 1 and 2, which is exactly why they're first.
