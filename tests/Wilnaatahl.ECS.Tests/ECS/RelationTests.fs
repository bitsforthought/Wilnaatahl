namespace Wilnaatahl.Tests.ECS

open System
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Relation
open Wilnaatahl.Tests.ECS.TestInfra

#if FABLE_COMPILER
open Wilnaatahl.Tests.ECS.FableTestInfra
#else
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS.Mocks
#endif

[<Collection("ECS")>]
type RelationTests() =
    let wrapper = new TestWorldWrapper()
    let world = wrapper.World
    let FriendsWith = tagRelation ()
    let Owes = valueRelation {| amount = 0 |}
    let SeparatedBy = mutableRelation {| X = 0 |} { X = 0 }

    interface IDisposable with
        member _.Dispose() = (wrapper :> IDisposable).Dispose()

    [<Fact>]
    member _.``Can create and use tag relation``() =
        let entity1 = world.Spawn [||]
        let entity2 = world.Spawn [||]
        entity1 |> add (FriendsWith => entity2)
        entity1 |> has (FriendsWith => entity2) =! true
        entity1 |> targetFor FriendsWith =! Some entity2
        entity1 |> targetsFor FriendsWith =! [| entity2 |]
        entity1 |> remove (FriendsWith => entity2)
        entity1 |> has (FriendsWith => entity2) =! false
        entity1 |> targetFor FriendsWith =! None
        entity1 |> targetsFor FriendsWith =! [||]

    [<Fact>]
    member _.``Can create and use value relation``() =
        let entity1 = world.Spawn [||]
        let entity2 = world.Spawn [||]
        entity1 |> add (Owes => entity2)
        entity1 |> setValue (Owes => entity2) {| amount = 123 |}
        entity1 |> get (Owes => entity2) =! Some {| amount = 123 |}
        entity1 |> targetFor Owes =! Some entity2
        entity1 |> targetsFor Owes =! [| entity2 |]
        entity1 |> remove (Owes => entity2)
        entity1 |> get (Owes => entity2) =! None
        entity1 |> targetFor Owes =! None
        entity1 |> targetsFor Owes =! [||]

    [<Fact>]
    member _.``QueryTrait over a value-relation pair reads its store value``() =
        // Regression: querying with a relation pair as the sole read trait must surface the pair's
        // store value to ForEach. The Movement system relies on this via QueryTrait(Parallels => line).
        let lender = world.Spawn [||]
        let debtor1 = world.Spawn [||]
        let debtor2 = world.Spawn [||]
        debtor1 |> addWith (Owes => lender) {| amount = 10 |}
        debtor2 |> addWith (Owes => lender) {| amount = 20 |}

        let owedByEntity =
            world.QueryTrait(Owes => lender).ToSequence()
            |> Seq.map (fun (owed, entity) -> entity, owed.amount)
            |> Map.ofSeq

        owedByEntity =! Map.ofList [ debtor1, 10; debtor2, 20 ]

    [<Fact>]
    member _.``Spawn with a value-relation pair sets the relation and its value``() =
        // Regression: spawning an entity with a value-relation pair carrying a value (e.g. the
        // Dragging system's Spawn((Dragging => node).Val origin)) must add the relation and store
        // the value. Koota 0.6.x rejects a [pair, value] tuple, so the value goes in as relation params.
        let lender = world.Spawn [||]
        let debtor = world.Spawn((Owes => lender).Val {| amount = 5 |})
        debtor |> has (Owes => lender) =! true
        debtor |> targetFor Owes =! Some lender
        debtor |> get (Owes => lender) =! Some {| amount = 5 |}

    [<Fact>]
    member _.``Only tag relations and wildcard traits have IsTag set to true``() =
        FriendsWith.IsTag =! true
        Owes.IsTag =! false
        SeparatedBy.IsTag =! false

        FriendsWith.Wildcard().IsTag =! true
        Owes.Wildcard().IsTag =! true
        SeparatedBy.Wildcard().IsTag =! true

    [<Fact>]
    member _.``Exclusive relations can only have one target at a time``() =
        let ChildOf = tagRelationWith { IsExclusive = true }
        let OlderThan = valueRelationWith {| years = 0 |} { IsExclusive = true }
        let FollowsAt = mutableRelationWith {| X = 0 |} { X = 0 } { IsExclusive = true }

        let entity1 = world.Spawn [||]
        let entity2 = world.Spawn [||]
        let entity3 = world.Spawn [||]

        entity1 |> add (ChildOf => entity2)
        entity1 |> targetFor ChildOf =! Some entity2
        entity1 |> add (ChildOf => entity3)
        entity1 |> targetsFor ChildOf =! [| entity3 |]

        entity1 |> add (OlderThan => entity2)
        entity1 |> targetFor OlderThan =! Some entity2
        entity1 |> add (OlderThan => entity3)
        entity1 |> targetsFor OlderThan =! [| entity3 |]

        entity1 |> add (FollowsAt => entity2)
        entity1 |> targetFor FollowsAt =! Some entity2
        entity1 |> add (FollowsAt => entity3)
        entity1 |> targetsFor FollowsAt =! [| entity3 |]

    [<Fact>]
    member _.``Non-exclusive relations can have many targets``() =
        // This is the BoundingBoxOn pattern: one subject relates to many targets through a single
        // relation. Koota 0.6.x stores all targets as data on one trait, so this scales without a
        // trait-per-target explosion.
        let Contains = tagRelation ()
        let WeighedAgainst = valueRelation {| grams = 0 |}

        let box = world.Spawn [||]
        let a = world.Spawn [||]
        let b = world.Spawn [||]
        let c = world.Spawn [||]

        box |> add (Contains => a)
        box |> add (Contains => b)
        box |> add (Contains => c)

        box |> has (Contains => a) =! true
        box |> has (Contains => b) =! true
        box |> has (Contains => c) =! true
        box |> targetsFor Contains |> Set.ofArray =! set [ a; b; c ]

        // Removing one target leaves the others intact.
        box |> remove (Contains => b)
        box |> has (Contains => b) =! false
        box |> targetsFor Contains |> Set.ofArray =! set [ a; c ]

        // Per-target data is independent across targets of the same relation.
        box |> add (WeighedAgainst => a)
        box |> add (WeighedAgainst => c)
        box |> setValue (WeighedAgainst => a) {| grams = 10 |}
        box |> setValue (WeighedAgainst => c) {| grams = 20 |}
        box |> get (WeighedAgainst => a) =! Some {| grams = 10 |}
        box |> get (WeighedAgainst => c) =! Some {| grams = 20 |}
        box |> targetsFor WeighedAgainst |> Set.ofArray =! set [ a; c ]
