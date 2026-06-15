namespace Wilnaatahl.Tests.ECS

open System
open System.Collections
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Relation
open Wilnaatahl.ECS.Trait
open Wilnaatahl.Tests.ECS.TestInfra

#if FABLE_COMPILER
open Wilnaatahl.Tests.ECS.FableTestInfra
#else
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS.Mocks
#endif

[<Collection("ECS")>]
type QueryTests() =
    let wrapper = new TestWorldWrapper()
    let world = wrapper.World
    let IsTagged = tagTrait ()
    let IsFlagged = tagTrait ()
    let IsNagged = tagTrait ()
    let Age = valueTrait {| age = 0 |}
    let Name = valueTrait {| name = "" |}
    let Score = valueTrait {| score = 0.0 |}
    let Active = valueTrait {| active = false |}
    let Owes = valueRelation {| amount = 0.0 |}
    let CousinOf = valueRelation {| degree = 0 |}

    interface IDisposable with
        member _.Dispose() = (wrapper :> IDisposable).Dispose()

    // ------------------------------------------------------------------
    // Portable tests (run on both .NET and Koota)
    // ------------------------------------------------------------------

    [<Fact>]
    member _.``Query with no parameters returns all entities in the World except World entity``() =
        let entity1 = world.Spawn [| Age.Val {| age = 49 |} |]
        let entity2 = world.Spawn [| Name.Val {| name = "Fred" |} |]
        world.Query() |> Set.ofSeq =! set [ entity1; entity2 ]

    [<Fact>]
    member _.``Query With returns only entities having that trait``() =
        let a = world.Spawn [| IsTagged.Tag() |]
        let _ = world.Spawn [| IsFlagged.Tag() |]
        world.Query(With IsTagged) |> Set.ofSeq =! set [ a ]

    [<Fact>]
    member _.``Query Or returns entities having any of the specified traits``() =
        let a = world.Spawn [| IsTagged.Tag() |]
        let b = world.Spawn [| IsTagged.Tag(); IsFlagged.Tag() |]
        let c = world.Spawn [| IsNagged.Tag() |]
        let _ = world.Spawn [| IsFlagged.Tag() |]
        world.Query(Or [| IsTagged; IsNagged |]) |> Set.ofSeq =! set [ a; b; c ]

    [<Fact>]
    member _.``Query With + Or: entity must match With AND at least one Or``() =
        let _ = world.Spawn [| IsTagged.Tag() |]
        let b = world.Spawn [| IsTagged.Tag(); IsFlagged.Tag() |]
        let _ = world.Spawn [| IsFlagged.Tag() |]
        let _ = world.Spawn [||]
        world.Query(With IsTagged, Or [| IsFlagged |]) |> Set.ofSeq =! set [ b ]

    [<Fact>]
    member _.``Query With + Or + Not``() =
        world.Spawn [| IsTagged.Tag() |] |> ignore
        world.Spawn [| IsFlagged.Tag() |] |> ignore
        world.Spawn [| IsTagged.Tag(); IsNagged.Tag() |] |> ignore
        let matchingEntity = world.Spawn [| IsTagged.Tag(); IsFlagged.Tag() |]

        let results =
            world.Query(With IsTagged, Or [| IsFlagged; IsNagged |], Not [| IsNagged |])
            |> Set.ofSeq

        results =! set [ matchingEntity ]

    [<Fact>]
    member _.``Query Not excludes entities with the specified trait``() =
        let a = world.Spawn [| IsTagged.Tag() |]
        let _ = world.Spawn [| IsTagged.Tag(); IsFlagged.Tag() |]
        world.Query(With IsTagged, Not [| IsFlagged |]) |> Set.ofSeq =! set [ a ]

    [<Fact>]
    member _.``Query multiple With traits - entities must have all``() =
        let _ = world.Spawn [| IsTagged.Tag() |]
        let b = world.Spawn [| IsTagged.Tag(); IsFlagged.Tag() |]
        let _ = world.Spawn [| IsFlagged.Tag() |]
        world.Query(With IsTagged, With IsFlagged) |> Set.ofSeq =! set [ b ]

    [<Fact>]
    member _.``QueryFirst returns first matching entity or None``() =
        let matchingEntity = world.Spawn [| IsTagged.Tag() |]
        world.Spawn [||] |> ignore
        world.QueryFirst(With IsTagged) =! Some matchingEntity
        world.QueryFirst(With IsFlagged) =! None

    [<Fact>]
    member _.``QueryTrait returns correct values and entities``() =
        let entity1 = world.Spawn [| Age.Val {| age = 41 |} |]
        let entity2 = world.Spawn [| Age.Val {| age = 32 |} |]
        let results = (world.QueryTrait Age).ToSequence() |> Set.ofSeq
        results =! set [ {| age = 41 |}, entity1; {| age = 32 |}, entity2 ]

    [<Fact>]
    member _.``QueryTraits returns correct pairs of values and entities``() =
        let entity1 =
            world.Spawn [| Age.Val {| age = 41 |}; Name.Val {| name = "Alice" |} |]

        let entity2 = world.Spawn [| Age.Val {| age = 32 |}; Name.Val {| name = "Bob" |} |]
        let results = world.QueryTraits(Age, Name).ToSequence() |> Set.ofSeq

        results
        =! set [
            ({| age = 41 |}, {| name = "Alice" |}), entity1
            ({| age = 32 |}, {| name = "Bob" |}), entity2
        ]

    [<Fact>]
    member _.``QueryTraits3 returns correct triples of values and entities``() =
        let entity1 =
            world.Spawn [|
                Age.Val {| age = 41 |}
                Name.Val {| name = "Alice" |}
                Active.Val {| active = true |}
            |]

        let entity2 =
            world.Spawn [|
                Age.Val {| age = 32 |}
                Name.Val {| name = "Bob" |}
                Active.Val {| active = false |}
            |]

        let results = world.QueryTraits3(Age, Name, Active).ToSequence() |> Set.ofSeq

        results
        =! set [
            ({| age = 41 |}, {| name = "Alice" |}, {| active = true |}), entity1
            ({| age = 32 |}, {| name = "Bob" |}, {| active = false |}), entity2
        ]

    [<Fact>]
    member _.``QueryTraits4 returns correct quadruples of values and entities``() =
        let entity1 =
            world.Spawn [|
                Age.Val {| age = 41 |}
                Name.Val {| name = "Alice" |}
                Active.Val {| active = true |}
                Score.Val {| score = 3.5 |}
            |]

        let entity2 =
            world.Spawn [|
                Age.Val {| age = 32 |}
                Name.Val {| name = "Bob" |}
                Active.Val {| active = false |}
                Score.Val {| score = 2.5 |}
            |]

        let results = world.QueryTraits4(Age, Name, Active, Score).ToSequence() |> Set.ofSeq

        results
        =! set [
            ({| age = 41 |}, {| name = "Alice" |}, {| active = true |}, {| score = 3.5 |}), entity1
            ({| age = 32 |}, {| name = "Bob" |}, {| active = false |}, {| score = 2.5 |}), entity2
        ]

    [<Fact>]
    member _.``QueryFirstTrait returns first entity and value or None``() =
        let matchingEntity = world.Spawn [| Age.Val {| age = 42 |} |]
        world.Spawn [||] |> ignore
        world.QueryFirstTrait Age =! Some(matchingEntity, {| age = 42 |})
        world.QueryFirstTrait Score =! None

    [<Fact>]
    member _.``QueryFirstTarget returns subject, target, and value or None``() =
        let entity1 = world.Spawn [||]
        let entity2 = world.Spawn [||]
        entity1 |> add (Owes => entity2)
        entity1 |> setValue (Owes => entity2) {| amount = 99.0 |}
        world.QueryFirstTarget Owes =! Some(entity1, entity2, {| amount = 99.0 |})

        world.QueryFirstTarget CousinOf =! None

    [<Fact>]
    member _.``QueryResult ForEach iterates all results``() =
        let entity1 = world.Spawn [| Age.Val {| age = 41 |} |]
        let entity2 = world.Spawn [| Age.Val {| age = 32 |} |]
        let results = ResizeArray<_>()
        (world.QueryTrait Age).ForEach(fun (a, e) -> results.Add((a, e)))
        set results =! set [ {| age = 41 |}, entity1; {| age = 32 |}, entity2 ]

    [<Fact>]
    member _.``QueryResult ToSequence returns all results``() =
        let entity1 = world.Spawn [| Age.Val {| age = 41 |} |]
        let entity2 = world.Spawn [| Age.Val {| age = 32 |} |]
        let seqResults = (world.QueryTrait Age).ToSequence() |> Set.ofSeq
        seqResults =! set [ {| age = 41 |}, entity1; {| age = 32 |}, entity2 ]

    [<Fact>]
    member _.``QueryResult UpdateEach can mutate values``() =
        let t = mutableTrait {| X = 0 |} { X = 0 }
        let entity1 = world.Spawn [| t.Val {| X = 1 |} |]
        let entity2 = world.Spawn [| t.Val {| X = 2 |} |]
        (world.QueryTrait t).UpdateEach(fun (v, _) -> if v.X = 1 then v.X <- 10 else v.X <- 20)

        let updated =
            (world.QueryTrait t).ToSequence() |> Seq.map (fun (m, e) -> m.X, e) |> Set.ofSeq

        updated =! set [ 10, entity1; 20, entity2 ]

    [<Fact>]
    member _.``Query UpdateEach visits exactly the matching entities``() =
        let entity1 = world.Spawn [| Age.Val {| age = 1 |}; IsTagged.Tag() |]
        let entity2 = world.Spawn [| Age.Val {| age = 2 |}; IsTagged.Tag() |]
        let _ = world.Spawn [| Age.Val {| age = 3 |} |] // no tag → excluded

        // A void (trait-less) query still iterates with UpdateEach; AlwaysTrack exercises the
        // change-detection path. The callback value is unit; only the visited entities matter.
        let visited = ResizeArray<EntityId>()
        world.Query(With IsTagged).UpdateEachWith AlwaysTrack (fun (_, entity) -> visited.Add entity)

        Set.ofSeq visited =! set [ entity1; entity2 ]

    [<Fact>]
    member _.``A non-exclusive value relation as a query value: ForEach reads, UpdateEach fails fast``() =
        // Owes is non-exclusive, so Koota's multi-trait query state surfaces a target-agnostic array
        // for its slot rather than the per-target value. ForEach reads each value with get and works;
        // UpdateEach fails fast rather than expose the wrong value. Mock and real Koota agree.
        let lender = world.Spawn [||]
        let debtor = world.Spawn [| Age.Val {| age = 7 |} |]
        debtor |> addWith (Owes => lender) {| amount = 99.0 |}

        let read =
            world.QueryTraits(Age, Owes => lender).ToSequence()
            |> Seq.map (fun ((age, owed), entity) -> entity, age.age, owed.amount)
            |> List.ofSeq

        read =! [ debtor, 7, 99.0 ]

        let message =
            captureExceptionMessage (fun () ->
                world.QueryTraits(Age, Owes => lender).UpdateEachWith AlwaysTrack (fun _ -> ()))

        message =! Some relationValueUpdateEachError

    [<Fact>]
    member _.``A relation pair as a single query value with a filter: ForEach reads, UpdateEach fails fast``() =
        // The relation pair is the query's value but an extra filter (With IsTagged) means it is not
        // the sole query trait. A relation's per-target value still can't be surfaced through
        // iteration, so UpdateEach fails fast while ForEach reads it correctly. This pins the
        // single-trait QueryTrait relation-value path (the multi-trait path is covered above).
        let lender = world.Spawn [||]
        let debtor = world.Spawn [| IsTagged.Tag() |]
        debtor |> addWith (Owes => lender) {| amount = 42.0 |}

        let read =
            world.QueryTrait(Owes => lender, With IsTagged).ToSequence()
            |> Seq.map (fun (owed, entity) -> entity, owed.amount)
            |> List.ofSeq

        read =! [ debtor, 42.0 ]

        let message =
            captureExceptionMessage (fun () ->
                world.QueryTrait(Owes => lender, With IsTagged).UpdateEachWith AlwaysTrack (fun _ -> ()))

        message =! Some relationValueUpdateEachError

    // ------------------------------------------------------------------
    // .NET-only tests (use Unquote quotations)
    // ------------------------------------------------------------------

    [<Fact>]
    member _.``QueryResult supports legacy enumerator``() =
        let entity1 = world.Spawn [| Age.Val {| age = 49 |} |]
        let entity2 = world.Spawn [| Name.Val {| name = "Fred" |} |]
        let seqResults = world.Query() :> IEnumerable
        let results = ResizeArray<EntityId>()

        for obj in seqResults do
            results.Add(unbox<EntityId> obj)

        set results =! set [ entity1; entity2 ]
