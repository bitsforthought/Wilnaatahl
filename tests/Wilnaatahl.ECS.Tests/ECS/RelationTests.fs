namespace Wilnaatahl.Tests.ECS

open System
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
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
