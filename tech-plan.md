# Nōto — Technical Implementation Plan

## Stack

- **Blazor Server** (.NET 9) — main web app and API backend
- **Blazor WASM PWA** — mobile capture client (voice, photo, text)
- **PostgreSQL** with pgvector — all structured data, full-text search, semantic search
- **Filesystem** — photos and audio stored as files, paths in postgres
- **Whisper** — server-side voice transcription (OpenAI API initially, local whisper.cpp later)
- **OpenRouter** — all AI features (conversation, autosummaries, auto-tagging, embeddings)
- **Milkdown v7** — headless WYSIWYG markdown editor (MIT, markdown-native, no serialization layer)
- **Bootstrap 5 + custom CSS** — layout utilities only, all visual design is hand-written
- **JetBrains Mono** — monospace throughout

### Data access

All database access is through **EF Core** — no raw SQL. The schema shown below is the target state; it will be created and maintained via EF Core migrations. Raw SQL is only used for the initial `CREATE EXTENSION` statements.

### Why Milkdown for markdown

Evaluated six editors. Milkdown v7 wins because:
- **Markdown is the native format** — no ProseMirror JSON → markdown serialization layer (unlike Tiptap)
- **Headless** — ships zero CSS, you control everything (zen aesthetic preserved)
- **Inline WYSIWYG** — syntax characters disappear as you type (like Typora), not a split-pane code editor
- **Active** — v7.19.1 released March 2026, 141 releases, 71 contributors
- **MIT license**
- Integration via JS interop from Blazor (no wrapper exists, but the vanilla JS API is clean)

Alternatives considered: CodeMirror 6 (has a Blazor NuGet wrapper `BlazorCodeMirror6` but gives a raw-markdown code-editor feel), Tiptap v3 (excellent but requires a community extension for markdown serialization), ink-mde (nice writer aesthetic but smaller community), EasyMDE (legacy CodeMirror 5).

## Data Model

```sql
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- Everything is an entity
CREATE TABLE entities (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    type        text NOT NULL,  -- capture, thread, work, stash, autosummary
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    title       text,
    body        text,
    meta        jsonb DEFAULT '{}',
    embedding   vector(1536)
);

-- All media on disk (photos and audio)
CREATE TABLE attachments (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    entity_id     uuid NOT NULL REFERENCES entities(id) ON DELETE CASCADE,
    kind          text NOT NULL,       -- image, audio
    filename      text,
    storage_path  text NOT NULL,       -- filesystem path (all media)
    mime_type     text,
    created_at    timestamptz NOT NULL DEFAULT now()
);

-- Links between anything
CREATE TABLE links (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    from_id     uuid NOT NULL REFERENCES entities(id) ON DELETE CASCADE,
    to_id       uuid NOT NULL REFERENCES entities(id) ON DELETE CASCADE,
    reason      text,
    created_at  timestamptz NOT NULL DEFAULT now()
);

-- Indexes
CREATE INDEX idx_entities_type_created ON entities (type, created_at DESC);
CREATE INDEX idx_entities_meta ON entities USING GIN (meta);
CREATE INDEX idx_entities_fts ON entities
    USING GIN (to_tsvector('english', coalesce(title,'') || ' ' || coalesce(body,'')));
CREATE INDEX idx_links_from ON links (from_id);
CREATE INDEX idx_links_to ON links (to_id);
-- Vector index added after initial embeddings are populated:
-- CREATE INDEX idx_entities_embedding ON entities USING hnsw (embedding vector_cosine_ops);
```

### Meta field conventions

```jsonc
// capture (voice)
{ "source": "voice", "duration_sec": 92, "status": "transcribed" }

// capture (text)
{ "source": "text" }

// capture (photo)
{ "source": "photo", "caption": "..." }

// stash
{ "url": "https://...", "tags": ["active-inference", "reading"] }

// thread
{ "status": "active" }  // or "quiet"

// autosummary
{ "scope": "thread", "scope_id": "uuid-of-thread" }

// work
{ "kind": "naturalist-log" }  // or "story", "essay", "journal"

// imported from original pipeline
{ "source": "chatgpt-import", "archetype": "Mirror", "theme": "Psyche in Transition" }
```

### Threading via links

A capture belongs to a thread via a link:
```
links: { from_id: <capture>, to_id: <thread>, reason: "tagged" }
```

A work draws from a thread:
```
links: { from_id: <work>, to_id: <thread>, reason: "source" }
```

Any entity links to any entity:
```
links: { from_id: <a>, to_id: <b>, reason: "inspired by" }
```

## Solution Structure

```
noto/
    noto.sln
    src/
        Noto.Shared/              -- models, DTOs, interfaces
            Models/
                Entity.cs
                Attachment.cs
                Link.cs
            Dto/
                CaptureCreateDto.cs
                EntitySummaryDto.cs
                SearchResultDto.cs
            Interfaces/
                IEntityService.cs
                ISearchService.cs
                IStorageService.cs

        Noto.Server/              -- Blazor Server + API
            Program.cs
            appsettings.json
            Data/
                NotoDbContext.cs
            Services/
                EntityService.cs
                ThreadService.cs
                LinkService.cs
                SearchService.cs
                EmbeddingService.cs
                EmbeddingWorker.cs       -- BackgroundService
                PhotoStorageService.cs
                AudioStorageService.cs
                TranscriptionService.cs
                TranscriptionWorker.cs   -- BackgroundService
                OpenRouterClient.cs
                AiConversationService.cs
                AutoSummaryService.cs
                AutoSummaryWorker.cs     -- BackgroundService
                AutoTagService.cs
            Api/
                CaptureEndpoints.cs      -- minimal API for PWA
                AttachmentEndpoints.cs   -- serves photos/audio over HTTP
                SyncEndpoints.cs
            Components/
                Layout/
                    MainLayout.razor
                    SlimLayout.razor     -- workspace/thread icon-only sidebar
                    NavSidebar.razor
                Pages/
                    Dashboard.razor
                    CaptureDetail.razor
                    ThreadList.razor
                    ThreadView.razor
                    Stash.razor
                    Workspace.razor
                    WorkList.razor
                    WorkEditor.razor
                Shared/
                    NoteCard.razor
                    QuickCapture.razor
                    ThreadChip.razor
                    ThreadPicker.razor
                    LinkPanel.razor
                    SearchOverlay.razor
                    AutoSummaryCard.razor
                    TagSuggestionBar.razor
                    MarkdownEditor.razor     -- Milkdown wrapper via JS interop
                Workspace/
                    SeedPanel.razor
                    Composer.razor
                    DetailPanel.razor
                    AiResponseBlock.razor
                    PromptBar.razor
            wwwroot/
                css/noto.css
                js/shortcuts.js
                js/milkdown-interop.js   -- Milkdown init/get/set/destroy

        Noto.Pwa/                 -- Blazor WASM PWA
            Program.cs
            Pages/
                Capture.razor
            Components/
                VoiceRecorder.razor
                PhotoCapture.razor
                TextCapture.razor
            Services/
                NotoApiClient.cs
            wwwroot/
                manifest.json
                service-worker.js
                css/pwa.css
                js/mediaRecorder.js

    tools/
        Noto.Cli/                 -- F# CLI for testing and ops
            Noto.Cli.fsproj
            Program.fs
            Commands/
                CaptureCommands.fs
                ThreadCommands.fs
                StashCommands.fs
                LinkCommands.fs
                SearchCommands.fs
                ImportCommands.fs
                SummaryCommands.fs
            CliArgs.fs             -- Argu argument definitions

        Noto.Seeder/              -- one-time import of origin/ data
            Program.fs
            Noto.Seeder.fsproj

    sql/
        001_init.sql
        002_vector_index.sql

    mockups/                      -- HTML mockups (reference)
        mockup_dashboard.html
        mockup_workspace.html
        mockup_thread.html
```

## CLI Design (Noto.Cli)

The CLI shares the same services and DbContext as the server. It talks directly to postgres, not through HTTP.

```
noto capture "woke up with the ridge dream again"
noto capture --voice ~/recording.wav
noto capture --photo ~/IMG_1234.jpg --caption "hawk at the creek"

noto list                              # recent captures
noto list --type voice                 # filter by type
noto list --thread "the ridge"         # filter by thread
noto list --unthreaded                 # orphan captures

noto show <id>                         # full detail of any entity
noto edit <id> --title "new title"     # update fields
noto delete <id>                       # with confirmation

noto thread create "spring at the creek"
noto thread list                       # all threads with status
noto thread list --active              # active only
noto thread show "the ridge"           # timeline of captures

noto tag <capture-id> "the ridge"      # link capture to thread
noto untag <capture-id> "the ridge"

noto stash "Active Inference paper" --url "https://..." --tag reading
noto stash list
noto stash search "inference"

noto link <id-a> <id-b> "inspired by"
noto links <id>                        # show all links for entity

noto search "hands knew before the mind"          # full-text
noto search --semantic "body intelligence music"   # vector search

noto import                            # run seeder from origin/
noto summary generate "the ridge"      # generate autosummary for thread
noto summary show "the ridge"          # display current autosummary
```

**CLI is F#** with [Argu](https://github.com/fsprojects/Argu) for argument parsing — discriminated unions map naturally to subcommands. The CLI project references `Noto.Shared` (C#) and `NotoDbContext` — F# consumes C# libraries seamlessly. All DB access through the same EF Core services as the server.

## Phase 1: Foundation + CLI

**Goal:** Database, data layer, text capture CRUD, dashboard shell, and a working CLI.

### Steps

1. Create solution and project scaffolding
2. Write `001_init.sql`, apply to local postgres
3. Implement `NotoDbContext` with EF Core + pgvector mapping
4. Implement `EntityService` (CRUD, stream query with pagination)
5. Implement `Noto.Cli` with `capture`, `list`, `show`, `edit`, `delete` commands
6. Verify full round-trip: CLI create → postgres → CLI list
7. Port `mockup_dashboard.html` CSS into `noto.css`
8. Implement `MainLayout.razor` + `NavSidebar.razor` (charcoal sidebar)
9. Implement `Dashboard.razor` with `NoteCard.razor` rendering stream from DB
10. Implement `QuickCapture.razor` modal for text capture from browser

**After Phase 1:** You can create text captures from CLI or browser, see them in the zen-styled dashboard, and query them from the command line.

## Phase 2: Threads, Links, Stash, Import

**Goal:** The organizational layer + import of existing data.

### Steps

1. Implement `ThreadService` — create thread, get thread captures (join through links), tag/untag
2. Implement `LinkService` — create/delete links, get links for entity
3. Add CLI commands: `thread`, `tag`, `untag`, `link`, `stash`
4. Implement `ThreadList.razor` and `ThreadView.razor` (port from mockup)
5. Implement `ThreadChip.razor` and `ThreadPicker.razor` components
6. Implement `Stash.razor` page + `StashQuickAdd.razor`
7. Implement `LinkPanel.razor` — shown on any entity detail view
8. Build `Noto.Seeder`:
   - Import `themes.json` entries as thread entities
   - Import `waking_segments.json` + `dream_segments.json` as capture entities with archetype/theme in meta
   - Link imported captures to matching thread entities
   - Import `trajectory_seed_sonnet.md`, `dream_seed.md`, `life_arc.md` as work entities
   - Import `dream_catalog.json` entries as thread entities (one per dream)
9. Run seeder, verify via CLI and browser

**After Phase 2:** All 315 original resonant segments are in the system, organized by theme. Threads are browsable. Stash works. Entities link to each other.

## Phase 3: Mobile Capture (PWA)

**Goal:** Voice, photo, and text capture from phone.

### Steps

1. Add minimal API endpoints to server: `POST /api/captures/{text,voice,photo}`, `GET /api/threads`
2. Implement `PhotoStorageService` — saves to `{MediaRoot}/photos/{yyyy-MM}/{guid}.ext`, creates attachment record
3. Implement `AudioStorageService` — saves to `{MediaRoot}/audio/{yyyy-MM}/{guid}.webm`, creates attachment record
4. Implement `AttachmentEndpoints` — `GET /api/attachments/{id}` serves file bytes with correct content-type
5. Implement `TranscriptionService` — calls OpenAI Whisper API
6. Implement `TranscriptionWorker` — background service that processes pending voice captures
7. Create `Noto.Pwa` project with `VoiceRecorder.razor` (MediaRecorder JS interop), `PhotoCapture.razor`, `TextCapture.razor`
8. Configure PWA manifest and service worker for offline queueing
9. Test: record voice on phone → arrives in stream → transcribed within seconds

**After Phase 3:** Full capture pipeline from phone. Voice, photos, text all flow into the dashboard.

## Phase 4: Search

**Goal:** Full-text and semantic search with cmd+K overlay.

### Steps

1. Implement `SearchService.SearchFullText` using `plainto_tsquery` / `ts_rank`
2. Implement `EmbeddingService` — calls `text-embedding-3-small` via OpenRouter/OpenAI
3. Implement `EmbeddingWorker` — background service that embeds entities where `embedding IS NULL`
4. After initial population, create HNSW vector index
5. Implement `SearchService.SearchSemantic` — embed query, `ORDER BY embedding <=> $1`
6. Implement `SearchService.SearchHybrid` — combine FTS rank + vector distance
7. Implement `SearchOverlay.razor` — cmd+K modal, toggle text/semantic, results as note cards
8. Add `search` CLI commands (text and semantic)
9. Add keyboard shortcut JS interop

**After Phase 4:** Press cmd+K, type what you vaguely remember, find it. Both keyword and meaning-based search.

## Phase 5: Workspace + AI Conversation

**Goal:** The synthesis engine — writing with AI context.

### Steps

1. Implement `OpenRouterClient` — chat completions with SSE streaming
2. Implement `AiConversationService` — builds system prompt from injected seeds + thread context, manages conversation state
3. Port `mockup_workspace.html` into Blazor:
   - `SeedPanel.razor` — browse captures by thread, select, inject
   - `Composer.razor` — title + body + context bar + AI response area
   - `DetailPanel.razor` — selected segment detail
   - `PromptBar.razor` — input with send + tool buttons
   - `AiResponseBlock.razor` — streamed response with actions
4. Integrate Milkdown editor for work creation/editing:
   - Install Milkdown via npm, bundle into wwwroot
   - Create `wwwroot/js/milkdown-interop.js` — init, get/set markdown, destroy
   - Create `MarkdownEditor.razor` — Blazor wrapper component using JS interop
   - Style `.milkdown` with JetBrains Mono and zen palette CSS
   - Wire editor content to entity body (markdown string, native format)
5. "Absorb into notes" action — saves AI response as a new capture linked to the conversation
6. "Mark as resonant" action — tags a response for potential inclusion in autosummaries

**After Phase 5:** The full synthesis loop works. Write with your history as context, converse with an AI that knows your arc.

## Phase 6: AutoSummaries, Auto-Tagging, Polish

**Goal:** Background AI features + final polish.

### Steps

1. Implement `AutoSummaryService` — fetches thread captures, sends to OpenRouter, creates/updates autosummary entity
2. Implement `AutoSummaryWorker` — periodic check for stale summaries
3. Implement `AutoTagService` — sends new capture + existing thread list to OpenRouter, returns suggestions
4. Implement `TagSuggestionBar.razor` — shown on untagged captures
5. Add `summary` CLI commands (generate, show)
6. Responsive sidebar (full ↔ slim icon bar)
7. Keyboard shortcuts (cmd+K search, cmd+N capture, cmd+Enter send)
8. Transition animations matching mockup hover states
9. Final CSS pass — ensure all pages match mockup aesthetic exactly

**After Phase 6:** The complete system. Captures get auto-tag suggestions. Threads have living summaries. Everything is searchable by keyword or meaning. The workspace supports sustained creative work.

## NuGet Packages

| Package | Project | Purpose |
|---|---|---|
| `Npgsql.EntityFrameworkCore.PostgreSQL` 9.x | Server | EF Core + PostgreSQL |
| `Pgvector.EntityFrameworkCore` | Server | vector column mapping |
| `Microsoft.EntityFrameworkCore.Design` | Server | migrations tooling |
| `Argu` 6.x | CLI (F#) | argument parsing via discriminated unions |
| `FSharp.Core` | CLI, Seeder | F# runtime |
| `Microsoft.AspNetCore.Components.WebAssembly` | PWA | Blazor WASM |

| `Milkdown` (npm) | Server (wwwroot) | markdown editor, loaded via JS interop |

No AI SDK packages needed — OpenRouter is standard REST, use `HttpClient` directly.

## Configuration

```jsonc
// appsettings.json
{
    "ConnectionStrings": {
        "Noto": "Host=localhost;Database=noto;Username=pragun"
    },
    "Storage": {
        "MediaRoot": "/Users/pragun/noto-media"  // photos/ and audio/ subdirs
    },
    "OpenRouter": {
        "ApiKey": "",                              // from env or secrets
        "ChatModel": "anthropic/claude-sonnet-4-6",
        "EmbeddingModel": "openai/text-embedding-3-small"
    },
    "Whisper": {
        "Provider": "openai",                      // or "local"
        "ApiKey": ""
    }
}
```

## Key Design Principles

1. **One table for everything.** Entities table + type field. No schema migration when adding a new kind of thing.
2. **Links for all relationships.** Threading, citations, zettelkasten connections — all the same table.
3. **JSONB for flexibility.** Type-specific fields live in `meta`. Queryable without schema changes.
4. **Background workers for heavy lifting.** Transcription, embedding, summarization happen async. The user never waits.
5. **CLI parity.** Every data operation available from the command line. Testing without a browser. Scripting for bulk operations. Import and export.
6. **CSS from mockups, not from a library.** The mockups define the design system. Extract, don't recreate.
7. **EF Core for all data access.** No raw SQL. Schema changes via migrations. The vector and FTS queries use EF Core's raw SQL interpolation (`FromSqlInterpolated`) only where LINQ can't express them.
8. **All media on disk.** Photos and audio live under `{MediaRoot}/` with paths in postgres. Simple, backupable, serveable over HTTP without postgres in the hot path.
9. **F# for tooling.** CLI and seeder are F# — Argu for argument parsing, pattern matching for command dispatch. C# for the Blazor server. Both share the same EF Core data layer.
