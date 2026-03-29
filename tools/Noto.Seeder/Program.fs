open System
open System.IO
open System.Linq
open System.Text.Json
open Microsoft.EntityFrameworkCore
open Noto.Server.Data
open Noto.Server.Services
open Noto.Shared.Models

let connStr = "Host=localhost;Port=5433;Database=noto;Username=noto;Password=noto"

let createService () =
    let optionsBuilder = DbContextOptionsBuilder<NotoDbContext>()
    optionsBuilder.UseNpgsql(connStr, fun o -> o.UseVector() |> ignore) |> ignore
    let db = new NotoDbContext(optionsBuilder.Options)
    db.Database.Migrate()
    EntityService(db)

let originDir =
    let candidates = [
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "origin")
        Path.Combine(Environment.CurrentDirectory, "origin")
        "/Users/pragun/Projects/noto/origin"
    ]
    candidates |> List.tryFind Directory.Exists
    |> Option.defaultValue "/Users/pragun/Projects/noto/origin"

let tryGetStr (elem: JsonElement) (prop: string) =
    let mutable value = Unchecked.defaultof<JsonElement>
    if elem.TryGetProperty(prop, &value) then
        value.GetString()
    else
        null

[<EntryPoint>]
let main _ =
    printfn "Noto Seeder"
    printfn "Origin: %s" originDir
    printfn ""

    let svc = createService ()

    // Check if already seeded
    let existing = svc.Count(null).Result
    if existing > 10 then
        printfn "Database already has %d entities. Skipping seed." existing
        printfn "To re-seed, clear the database first."
        0
    else

    // 1. Import themes as threads
    let themesPath = Path.Combine(originDir, "themes.json")
    if File.Exists(themesPath) then
        printfn "=== Importing themes as threads ==="
        let json = File.ReadAllText(themesPath)
        let themes = JsonDocument.Parse(json)
        let mutable count = 0
        for theme in themes.RootElement.EnumerateArray() do
            let name = theme.GetProperty("name").GetString()
            let desc = theme.GetProperty("description").GetString()
            svc.Create(EntityTypes.Thread, name, desc).Result |> ignore
            count <- count + 1
            printfn "  thread: %s" name
        printfn "  -> %d themes imported as threads" count
        printfn ""

    // 2. Import waking segments as captures + tag to themes
    let wakingPath = Path.Combine(originDir, "waking_segments.json")
    if File.Exists(wakingPath) then
        printfn "=== Importing waking segments ==="
        let json = File.ReadAllText(wakingPath)
        let segs = JsonDocument.Parse(json)
        let mutable count = 0
        let mutable tagged = 0
        for seg in segs.RootElement.EnumerateArray() do
            let quote = seg.GetProperty("quote").GetString()
            let archetype = let v = tryGetStr seg "archetype" in if isNull v then "unknown" else v
            let theme = tryGetStr seg "theme"

            let metaStr =
                if isNull theme then
                    sprintf """{"source":"chatgpt-import","archetype":"%s"}""" archetype
                else
                    sprintf """{"source":"chatgpt-import","archetype":"%s","theme":"%s"}""" archetype (theme.Replace("\"", "\\\""))
            let metaJson = JsonDocument.Parse(metaStr)

            let marker = if archetype = "Mirror" then "\u25C9" else "\u25C8"
            let label = if isNull theme then "unthemed" else theme
            let title = sprintf "%s %s" marker label
            let entity = svc.Create(EntityTypes.Capture, title, quote, metaJson).Result

            if not (isNull theme) then
                let thread = svc.FindThreadByName(theme).Result
                if not (isNull thread) then
                    svc.TagCapture(entity.Id, thread.Id).Result |> ignore
                    tagged <- tagged + 1

            count <- count + 1
            if count % 50 = 0 then printfn "  ... %d segments" count

        printfn "  -> %d waking segments imported, %d tagged to threads" count tagged
        printfn ""

    // 3. Import dream segments
    let dreamPath = Path.Combine(originDir, "dream_segments.json")
    if File.Exists(dreamPath) then
        printfn "=== Importing dream segments ==="
        let json = File.ReadAllText(dreamPath)
        let segs = JsonDocument.Parse(json)
        let mutable count = 0
        for seg in segs.RootElement.EnumerateArray() do
            let quote = seg.GetProperty("quote").GetString()
            let archetype = let v = tryGetStr seg "archetype" in if isNull v then "unknown" else v
            let dream = tryGetStr seg "dream"

            let dreamSafe = if isNull dream then "" else dream.Replace("\"", "\\\"")
            let metaStr =
                if isNull dream then
                    sprintf """{"source":"chatgpt-import","archetype":"%s","kind":"dream"}""" archetype
                else
                    sprintf """{"source":"chatgpt-import","archetype":"%s","kind":"dream","dream":"%s"}""" archetype dreamSafe
            let metaJson = JsonDocument.Parse(metaStr)

            let label = if isNull dream then "dream" else dream
            let title = sprintf "\u263D %s" label
            svc.Create(EntityTypes.Capture, title, quote, metaJson).Result |> ignore
            count <- count + 1

        printfn "  -> %d dream segments imported" count
        printfn ""

    // 4. Import seed documents as works
    let docs = [
        ("trajectory_seed_sonnet.md", "Trajectory Seed (Sonnet)")
        ("dream_seed.md", "Dream Seed")
        ("life_arc.md", "Life Arc")
    ]
    printfn "=== Importing seed documents as works ==="
    for (filename, title) in docs do
        let path = Path.Combine(originDir, filename)
        if File.Exists(path) then
            let content = File.ReadAllText(path)
            let metaJson = JsonDocument.Parse("""{"source":"chatgpt-import","kind":"seed-document"}""")
            svc.Create(EntityTypes.Capture, title, content, metaJson).Result |> ignore
            printfn "  work: %s (%d chars)" title content.Length
    printfn ""

    // Summary
    let total = svc.Count(null).Result
    let captures = svc.Count(EntityTypes.Capture).Result
    let threads = svc.Count(EntityTypes.Thread).Result
    let works = svc.Count(EntityTypes.Capture).Result
    printfn "=== Done ==="
    printfn "  total:    %d" total
    printfn "  captures: %d" captures
    printfn "  threads:  %d" threads
    printfn "  works:    %d" works

    0
