open System
open System.Linq
open Argu
open Microsoft.EntityFrameworkCore
open Noto.Server.Data
open Noto.Server.Services
open Noto.Shared.Models

type CaptureArgs =
    | [<MainCommand; ExactlyOnce>] Body of string
    | [<AltCommandLine("-t")>] Title of string
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Body _ -> "capture body text"
            | Title _ -> "optional title"

type ListArgs =
    | [<AltCommandLine("-t")>] Type of string
    | [<AltCommandLine("-n")>] Limit of int
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Type _ -> "filter by entity type"
            | Limit _ -> "max results (default 20)"

type ShowArgs =
    | [<MainCommand; ExactlyOnce>] Id of string
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Id _ -> "entity id (prefix match)"

type EditArgs =
    | [<MainCommand; ExactlyOnce>] Id of string
    | [<AltCommandLine("-t")>] Title of string
    | [<AltCommandLine("-b")>] Body of string
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Id _ -> "entity id"
            | Title _ -> "new title"
            | Body _ -> "new body"

type DeleteArgs =
    | [<MainCommand; ExactlyOnce>] Id of string
    | [<AltCommandLine("-y")>] Yes
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Id _ -> "entity id"
            | Yes -> "skip confirmation"

type ThreadCreateArgs =
    | [<MainCommand; ExactlyOnce>] Name of string
    | [<AltCommandLine("-d")>] Description of string
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Name _ -> "thread name"
            | Description _ -> "thread description"

type TagArgs =
    | [<MainCommand>] Ids of string list
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Ids _ -> "<capture-id> <thread-name-or-id>"

type SearchArgs =
    | [<MainCommand; ExactlyOnce>] Query of string
    | [<AltCommandLine("-n")>] Limit of int
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Query _ -> "search query"
            | Limit _ -> "max results"

type NotoArgs =
    | [<CliPrefix(CliPrefix.None)>] Capture of ParseResults<CaptureArgs>
    | [<CliPrefix(CliPrefix.None)>] List of ParseResults<ListArgs>
    | [<CliPrefix(CliPrefix.None)>] Show of ParseResults<ShowArgs>
    | [<CliPrefix(CliPrefix.None)>] Edit of ParseResults<EditArgs>
    | [<CliPrefix(CliPrefix.None)>] Delete of ParseResults<DeleteArgs>
    | [<CliPrefix(CliPrefix.None)>] Thread of ParseResults<ThreadCreateArgs>
    | [<CliPrefix(CliPrefix.None)>] Tag of ParseResults<TagArgs>
    | [<CliPrefix(CliPrefix.None)>] Search of ParseResults<SearchArgs>
    | [<CliPrefix(CliPrefix.None)>] Count
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Capture _ -> "create a new capture"
            | List _ -> "list recent entities"
            | Show _ -> "show entity detail"
            | Edit _ -> "edit an entity"
            | Delete _ -> "delete an entity"
            | Thread _ -> "create a new thread"
            | Tag _ -> "tag a capture to a thread"
            | Search _ -> "full-text search"
            | Count -> "count entities"

let connStr =
    "Host=localhost;Port=5433;Database=noto;Username=noto;Password=noto"

let createDb () =
    let optionsBuilder = DbContextOptionsBuilder<NotoDbContext>()
    optionsBuilder.UseNpgsql(connStr, fun o -> o.UseVector() |> ignore) |> ignore
    new NotoDbContext(optionsBuilder.Options)

let createService () =
    let db = createDb ()
    db.Database.Migrate()
    EntityService(db)

let printEntity (e: Entity) =
    let title = if isNull e.Title then "(no title)" else e.Title
    let bodyText = if isNull e.Body then "" else e.Body
    let body = if bodyText.Length > 80 then bodyText.[..77] + "..." else bodyText
    let time = e.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
    printfn "  %s  %-10s  %s  %s" (e.Id.ToString().[..7]) e.Type time title
    if body <> "" then printfn "    %s" body

let findEntity (svc: EntityService) (idPrefix: string) =
    let db = createDb ()
    let all = db.Entities.ToList()
    let matches = all |> Seq.filter (fun e -> e.Id.ToString().StartsWith(idPrefix, StringComparison.OrdinalIgnoreCase)) |> Seq.toList
    match matches with
    | [one] -> Some one
    | [] ->
        printfn "no entity matching '%s'" idPrefix
        None
    | many ->
        printfn "ambiguous id '%s' — %d matches:" idPrefix many.Length
        many |> List.iter printEntity
        None

let findThread (svc: EntityService) (nameOrId: string) =
    let db = createDb ()
    let threads = db.Entities.Where(fun e -> e.Type = EntityTypes.Thread).ToList()
    let byName = threads |> Seq.tryFind (fun e -> e.Title <> null && e.Title.Equals(nameOrId, StringComparison.OrdinalIgnoreCase))
    match byName with
    | Some t -> Some t
    | None ->
        let byId = threads |> Seq.filter (fun e -> e.Id.ToString().StartsWith(nameOrId, StringComparison.OrdinalIgnoreCase)) |> Seq.toList
        match byId with
        | [one] -> Some one
        | _ ->
            printfn "no thread matching '%s'" nameOrId
            None

[<EntryPoint>]
let main argv =
    let parser = ArgumentParser.Create<NotoArgs>(programName = "noto")

    try
        let results = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)
        let svc = createService ()

        match results.GetAllResults() with
        | [Capture args] ->
            let body = args.GetResult CaptureArgs.Body
            let title = args.TryGetResult CaptureArgs.Title
            let entity = svc.Create(EntityTypes.Capture, (title |> Option.toObj), body, null).Result
            printfn "created %s" (entity.Id.ToString())
            printEntity entity

        | [List args] ->
            let entityType = args.TryGetResult ListArgs.Type |> Option.toObj
            let limit = args.TryGetResult ListArgs.Limit |> Option.defaultValue 20
            let entities = svc.GetStream(0, limit, entityType).Result
            printfn "%d entities:" entities.Count
            entities |> Seq.iter printEntity

        | [Show args] ->
            let idPrefix = args.GetResult ShowArgs.Id
            match findEntity svc idPrefix with
            | Some e ->
                let full = svc.GetById(e.Id).Result
                if full <> null then
                    printfn "id:      %A" full.Id
                    printfn "type:    %s" full.Type
                    printfn "title:   %s" (if isNull full.Title then "(none)" else full.Title)
                    printfn "created: %s" (full.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))
                    printfn "updated: %s" (full.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))
                    printfn ""
                    if not (isNull full.Body) then
                        printfn "%s" full.Body
                    if full.LinksFrom.Count > 0 then
                        printfn "\nlinks from:"
                        full.LinksFrom |> Seq.iter (fun l ->
                            printfn "  -> %s (%s) %s" (l.ToId.ToString().[..7]) (if isNull l.Reason then "link" else l.Reason) (if isNull l.To.Title then "" else l.To.Title))
                    if full.LinksTo.Count > 0 then
                        printfn "\nlinks to:"
                        full.LinksTo |> Seq.iter (fun l ->
                            printfn "  <- %s (%s) %s" (l.FromId.ToString().[..7]) (if isNull l.Reason then "link" else l.Reason) (if isNull l.From.Title then "" else l.From.Title))
            | None -> ()

        | [Edit args] ->
            let idPrefix = args.GetResult EditArgs.Id
            let title = args.TryGetResult EditArgs.Title
            let body = args.TryGetResult EditArgs.Body
            match findEntity svc idPrefix with
            | Some e ->
                let updated = svc.Update(e.Id, (title |> Option.toObj), (body |> Option.toObj), null).Result
                if updated <> null then
                    printfn "updated"
                    printEntity updated
            | None -> ()

        | [Delete args] ->
            let idPrefix = args.GetResult DeleteArgs.Id
            let force = args.Contains DeleteArgs.Yes
            match findEntity svc idPrefix with
            | Some e ->
                if not force then
                    printfn "delete this entity?"
                    printEntity e
                    printf "  type 'yes' to confirm: "
                    let answer = Console.ReadLine()
                    if answer <> "yes" then
                        printfn "cancelled"
                    else
                        svc.Delete(e.Id).Result |> ignore
                        printfn "deleted"
                else
                    svc.Delete(e.Id).Result |> ignore
                    printfn "deleted %s" (e.Id.ToString().[..7])
            | None -> ()

        | [Thread args] ->
            let name = args.GetResult ThreadCreateArgs.Name
            let desc = args.TryGetResult ThreadCreateArgs.Description |> Option.toObj
            let entity = svc.Create(EntityTypes.Thread, name, desc, null).Result
            printfn "thread created: %s (%s)" name (entity.Id.ToString())

        | [Tag args] ->
            let ids = args.GetResult TagArgs.Ids
            if ids.Length < 2 then
                printfn "usage: noto tag <capture-id> <thread-name-or-id>"
            else
                let capturePrefix = ids.[0]
                let threadRef = ids.[1]
                match findEntity svc capturePrefix, findThread svc threadRef with
                | Some capture, Some thread ->
                    svc.TagCapture(capture.Id, thread.Id).Result |> ignore
                    printfn "tagged %s -> %s" (capture.Id.ToString().[..7]) (if isNull thread.Title then thread.Id.ToString().[..7] else thread.Title)
                | _ -> ()

        | [Search args] ->
            let query = args.GetResult SearchArgs.Query
            let limit = args.TryGetResult SearchArgs.Limit |> Option.defaultValue 20
            let results' = svc.SearchText(query, limit).Result
            printfn "%d results for '%s':" results'.Count query
            results' |> Seq.iter printEntity

        | [Count] ->
            let total = svc.Count(null).Result
            let captures = svc.Count(EntityTypes.Capture).Result
            let threads = svc.Count(EntityTypes.Thread).Result
            let stash = svc.Count(EntityTypes.Stash).Result
            let works = svc.Count(EntityTypes.Capture).Result
            printfn "total:    %d" total
            printfn "captures: %d" captures
            printfn "threads:  %d" threads
            printfn "stash:    %d" stash
            printfn "works:    %d" works

        | _ ->
            printfn "%s" (parser.PrintUsage())

        0
    with
    | :? ArguParseException as ex ->
        printfn "%s" ex.Message
        1
    | ex ->
        printfn "error: %s" ex.Message
        1
