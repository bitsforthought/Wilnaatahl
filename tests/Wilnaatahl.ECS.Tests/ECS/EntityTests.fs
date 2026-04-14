namespace Wilnaatahl.Tests.ECS

open System
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
type EntityTests() =
    let wrapper = new TestWorldWrapper()
    let world = wrapper.World
    let IsTagged = tagTrait ()
    let Age = valueTrait {| age = 0 |}
    let Score = valueTrait {| age = 0 |} // Same schema as Age for getFirst compatibility
    let NetWorth = valueTrait {| netWorth = 0 |}
#if !FABLE_COMPILER
    // Primitive-schema traits for use inside Unquote quotations
    // (anonymous records can't be used inside quotations).
    let AgeInt = valueTrait 0
    let NetWorthInt = valueTrait 0
#endif

    interface IDisposable with
        member _.Dispose() = (wrapper :> IDisposable).Dispose()

    [<Fact>]
    member _.``Can spawn entity and add/remove tag trait``() =
        let entity = world.Spawn [| IsTagged.Tag() |]
        entity |> has IsTagged =! true
        entity |> remove IsTagged
        entity |> has IsTagged =! false

    [<Fact>]
    member _.``Can set and get value trait on entity``() =
        let entity = world.Spawn [| Age.Val {| age = 42 |} |]
        entity |> get Age =! Some {| age = 42 |}
        entity |> setValue Age {| age = 100 |}
        entity |> get Age =! Some {| age = 100 |}
        entity |> remove Age
        entity |> get Age =! None

    [<Fact>]
    member _.``Can destroy entity and remove all traits``() =
        let entity = world.Spawn [| IsTagged.Tag(); Age.Val {| age = 1 |} |]
        entity |> has IsTagged =! true
        entity |> has Age =! true
        world.Query().ToSequence() |> Seq.map snd |> List.ofSeq =! [ entity ]
        entity |> destroy
        entity |> has IsTagged =! false
        entity |> has Age =! false
        world.Query().ToSequence() |> Seq.isEmpty =! true

    [<Fact>]
    member _.``friendlyId returns local id``() =
        let entity1 = world.Spawn [||]
        let fid1 = entity1 |> friendlyId

        // entity1 is the first entity in its World. If we create another entity in another World, their
        // friendly IDs should match.
        use wrapper2 = new TestWorldWrapper()
        let otherWorld = wrapper2.World

        let entity2 = otherWorld.Spawn [||]
        let fid2 = entity2 |> friendlyId

        fid1 =! fid2

    [<Fact>]
    member _.``getFirst returns first present trait value``() =
        let entity = world.Spawn [| Score.Val {| age = 99 |} |]
        let entity2 = world.Spawn [| Age.Val {| age = 42 |} |]
        let entity3 = world.Spawn [||]

        entity |> getFirst Age Score =! Some {| age = 99 |}
        entity2 |> getFirst Age Score =! Some {| age = 42 |}
        entity3 |> getFirst Age Score =! None

    [<Fact>]
    member _.``setWith updates value trait using function``() =
        let entity = world.Spawn [| Age.Val {| age = 10 |} |]
        entity |> setWith Age (fun v -> {| age = v.age + 5 |})
        entity |> get Age =! Some {| age = 15 |}

    [<Fact>]
    member _.``targetWithValueFor returns target and value if present``() =
        let Owes = valueRelation {| amount = 0 |}
        let entity1 = world.Spawn [||]
        let entity2 = world.Spawn [||]
        entity1 |> add (Owes => entity2)
        // After add, the relation has the schema default value
        entity1 |> targetWithValueFor Owes =! Some(entity2, {| amount = 0 |})

        entity1 |> setValue (Owes => entity2) {| amount = 123 |}
        entity1 |> targetWithValueFor Owes =! Some(entity2, {| amount = 123 |})

        entity1 |> remove (Owes => entity2)
        entity1 |> targetWithValueFor Owes =! None

    [<Fact>]
    member _.``addWith adds and sets value trait``() =
        let entity = world.Spawn [||]
        entity |> addWith Age {| age = 77 |}
        entity |> get Age =! Some {| age = 77 |}

    [<Fact>]
    member _.``setWith works after add because add initializes with default value``() =
        let entity = world.Spawn [| NetWorth.Val {| netWorth = 5 |} |]
        entity |> add Age
        entity |> setWith Age (fun v -> {| age = v.age + 5 |})
        entity |> get Age =! Some {| age = 5 |}

    [<Fact>]
    member _.``setValue throws when trait not added``() =
        let entity = world.Spawn [| NetWorth.Val {| netWorth = 5 |} |]

        let threw =
            try
                entity |> setValue Age {| age = 25 |}
                false
            with _ ->
                true

        threw =! true

    [<Fact>]
    member _.``setWith throws when trait not added``() =
        let entity = world.Spawn [| NetWorth.Val {| netWorth = 5 |} |]

        let threw =
            try
                entity |> setWith Age (fun v -> {| age = v.age + 5 |})
                false
            with _ ->
                true

        threw =! true

// ------------------------------------------------------------------
// .NET-only tests (use Unquote quotations)
// ------------------------------------------------------------------

#if !FABLE_COMPILER
    [<Fact>]
    member _.``entity operation after World is disposed throws exception``() =
        let toDispose = new TestWorld()
        let tempWorld = toDispose :> IWorld
        let entity = tempWorld.Spawn [||]
        entity |> addWith AgeInt 77
        (toDispose :> IDisposable).Dispose()

        raises <@ entity |> setValue AgeInt 49 @>
#endif
