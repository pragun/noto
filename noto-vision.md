# Nōto — Vision Document

## How this began

In March 2026, a person sat down with an export of six months of ChatGPT conversations — the ones where they had talked about dreams, inner life, relationships ending, music arriving, the body learning to speak. Over 1,100 assistant messages across four long conversations. The question was simple: *which of these responses actually landed?* Not which were well-written or informative, but which ones named something the person already sensed (and couldn't yet say), or opened a direction they hadn't considered.

What followed was a week-long exploration that built its own tools as it went.

### The extraction

A sliding window approach — 40 messages wide, 20 messages of stride, like a Hanning window in signal processing — moved across each conversation. At each position, an LLM read the surrounding context and identified verbatim quotes that carried genuine resonance. Each was classified as either a **Mirror** (named something already sensed) or a **Catalyst** (opened a new direction). The window overlap ensured nothing was missed at boundaries. 315 segments survived after filtering out technical software discussions.

### The organization

A single prompt sent all 315 quotes with their archetypes to Claude Sonnet, which proposed eight themes:

- **Psyche in Transition** — inner reorganization, threshold-crossing
- **Dreams as Inner Maps** — dream imagery as living language
- **Logos, Eros, and Wholeness** — integrating the modes of the soul
- **Self-Authorship and Inner Compass** — trusting inner signals over external validation
- **Body, Sensation, and Somatic Truth** — embodied knowing
- **Music as Self-Discovery** — sound as vehicle for self-knowledge
- **Belonging, Place, and Relational Field** — finding coherent environments
- **Grief, Tears, and What Was Starved** — reckoning with suppressed longing

A dream catalog was built — 66 named dreams, matched back to their original narrations via fuzzy text search, grouped into night clusters by timestamp proximity. Waking discussion segments (254) were separated from dream segments (61).

### The rendering

Two seed documents emerged:

- A **dream seed** — narrations and the responses that solidified their direction, organized by night
- A **trajectory seed** — the waking life distilled by theme, prefaced by a life arc narrative generated from all user messages grouped by week

Context notes were added: for each resonant segment, a 2-line summary of what the person was working through and how the response fit that moment. Claude Sonnet and Mistral Large were compared; Sonnet's notes were clearly more grounded.

The total cost of the entire pipeline — extraction, theming, cataloging, narration matching, life arc generation, context notes — was under $2.

### What became clear

The seed documents were immediately, startlingly useful. Not as AI artifacts but as *mirrors*. Reading the trajectory seed was like looking at a compressed map of six months of living — only the moments that actually moved something, in order, with context. It was too dense and too honest to have been written by the person themselves, and too specific to have been written by anyone else.

But the documents were static. They captured what had happened. They couldn't grow.

---

## What nōto is

Nōto is a personal archive and synthesis system. It exists to serve one person's ongoing relationship with their inner life — dreams, reflections, observations, creative work, the slow unfolding of meaning across months and years.

### The five things

The entire system is built from five entity types:

| Entity | What it is |
|---|---|
| **Capture** | Raw input — a voice memo, a typed note, a photo. Lands in a stream with a timestamp. No organization required. |
| **Stash** | A link, a fact, a reference. A book title. A trail name. A phone number. Flat, fast, no structure imposed. |
| **Thread** | A named through-line. "The ridge." "Logos vs eros." "Spring at the creek." "Grief." A thread is a tag that carries a timeline and an AI-maintained summary. Captures link to threads; a capture can belong to many threads or none. Some threads are narrow and personal. Some are broad (what the original system called "themes"). They work the same way. |
| **Work** | A composed document — a story, a naturalist log, an essay, a letter-to-self. Something you're writing. A work draws from threads as source material but has its own text, structure, and version history. |
| **AutoSummary** | A generated distillation of a thread or scope, read-only. The system reads what has accumulated and writes a compressed version. What was called "seed documents" in the original pipeline. Updated periodically as new material arrives. |

Everything can link to everything else — capture to capture, thread to stash entry, work to thread. Links are the connective tissue. Threading a capture is just a link. A work citing a source is a link. Finding a surprising connection between two unrelated notes is a link.

### The rhythm

The system supports a natural daily rhythm:

**Capture** happens fast — on a phone, walking, waking from a dream. Voice memo, photo, typed fragment. No decisions required. It just lands in the stream.

**Accumulation** happens without pressure. Things sit in the stream. The system auto-suggests thread assignments ("this mentions the ridge — add to that thread?") but never acts without confirmation.

**Threading** happens when you notice. You scroll through the week's captures, you see a pattern forming, you name it. Or you add to an existing thread. Or you don't. Some captures never get threaded. That's fine.

**Synthesis** happens when you're ready. You open a thread, scroll its timeline across weeks or months, see how something evolved. You open the workspace, inject relevant context, write freely, converse with an AI that knows your arc. Something lands. You mark it as resonant. It feeds back into your material.

**AutoSummaries** grow in the background. The distilled portrait of who you are and what you're working through stays current without effort.

---

## The aesthetic and spiritual need

This tool is not a productivity system. It is not a second brain. It is not about capturing everything or optimizing retrieval.

It is about **tending**. The way you tend a garden, a fire, a fermentation. The daily practice of noticing what's alive and giving it a place to rest.

### The Japanese principle

The aesthetic is drawn from wabi-sabi and the tea ceremony — not as decoration but as discipline. The right amount of space around each element. The right silence between notes. Typography that breathes. Color that doesn't compete with thought.

The palette: charcoal (ink, depth, the ground), matcha green (life, growth, the living edge), parchment (paper, warmth, the scroll that holds), chalk (structure, bone, what endures), slate (the quiet middle, neither dark nor light).

JetBrains Mono throughout — monospace as a commitment to equal weight, no letter more important than another. No rounded corners, no drop shadows, no gradient buttons. The interface should feel like a well-typeset journal, not an application.

### What it serves

The person who uses this system is living through something. They are tracking dreams, working through transitions, composing music, learning to listen to their body, building community, grieving what was lost, becoming something they can't yet name.

They need a place where all of this can coexist without being organized into someone else's categories. Where a voice memo from a morning walk sits next to a dream narration sits next to a link to a book on Craniosacral Therapy sits next to a half-finished essay on what fatherhood might mean.

The tool should never suggest that this material needs to be *resolved*. It needs to be *held*. The threads are not conclusions — they are ongoing investigations. The autosummaries are not reports — they are mirrors. The workspace is not a production line — it is a place to sit with what has gathered and see what wants to be written.

### The AI's role

The AI in this system is not an assistant, a coach, or an analyst. It is a *conversation partner that remembers*. Its value comes from continuity — it has read your arc, your themes, your dreams, your resonant moments. It responds from that context, not from generic knowledge.

The AI should:
- Respond to what you bring, never initiate
- Name patterns you haven't noticed, without insisting on them
- Hold the same material from multiple angles without collapsing it into a single interpretation
- Know when to be quiet

The autosummary is the mechanism that makes this possible at scale. Six months of material compressed into something an AI can hold in a single conversation. The trajectory, the dreams, the life arc — all present, all available, without requiring the user to re-explain themselves each time.

### What success looks like

You open the app on a Saturday morning. You scroll through the week's captures. You notice that three different voice memos — from Monday, Wednesday, and Friday — are circling the same thing. You thread them. You open that thread in the workspace. You write for twenty minutes. The AI responds with something that connects what you wrote this morning to a dream from three months ago. You didn't see that connection. Now you do.

That's the whole thing.

---

## Where the name comes from

Nōto (ノート) is the Japanese word for notebook — borrowed from English "note" but pronounced with the weight and space of Japanese phonology. Two syllables, each given equal duration. A word that carries both the Western impulse to record and the Japanese impulse to attend.
