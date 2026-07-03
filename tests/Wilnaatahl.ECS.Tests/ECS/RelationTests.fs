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

    interface IDisposable with
        member _.Dispose() = (wrapper :> IDisposable).Dispose()

    [<Fact>]
    member _.``A tag relation can be added, queried, and removed``() =
        let entity1 = world.Spawn [||]
        let entity2 = world.Spawn [||]

        entity1 |> hasRelation FriendsWith entity2 =! false
        entity1 |> addRelation FriendsWith entity2
        entity1 |> hasRelation FriendsWith entity2 =! true
        entity1 |> targetFor FriendsWith =! Some entity2
        entity1 |> targetsFor FriendsWith =! [| entity2 |]

        entity1 |> removeRelation FriendsWith entity2
        entity1 |> hasRelation FriendsWith entity2 =! false
        entity1 |> targetFor FriendsWith =! None
        entity1 |> targetsFor FriendsWith =! [||]

    [<Fact>]
    member _.``Adding a value relation initializes it with the schema default``() =
        let entity1 = world.Spawn [||]
        let entity2 = world.Spawn [||]

        entity1 |> getRelationValue Owes entity2 =! None
        entity1 |> addRelation Owes entity2
        // addRelation seeds the schema default so a subsequent read always succeeds.
        entity1 |> getRelationValue Owes entity2 =! Some {| amount = 0 |}

    [<Fact>]
    member _.``A value relation's value can be set and updated``() =
        let entity1 = world.Spawn [||]
        let entity2 = world.Spawn [||]

        entity1 |> addRelationWith Owes entity2 {| amount = 123 |}
        entity1 |> getRelationValue Owes entity2 =! Some {| amount = 123 |}

        entity1 |> setRelationValue Owes entity2 {| amount = 7 |}
        entity1 |> getRelationValue Owes entity2 =! Some {| amount = 7 |}

        entity1 |> removeRelation Owes entity2
        entity1 |> getRelationValue Owes entity2 =! None
        entity1 |> targetFor Owes =! None

    [<Fact>]
    member _.``A Related query returns each subject of a target and its per-target value``() =
        // The membership read pattern (used by the UndoRedo and Movement systems): filter to a
        // relation's subjects for a given target with Query(Related(rel, target)), then read each
        // subject's value inside the callback with getRelationValue.
        let lender = world.Spawn [||]
        let otherLender = world.Spawn [||]
        let debtor1 = world.Spawn [||]
        let debtor2 = world.Spawn [||]

        debtor1 |> addRelationWith Owes lender {| amount = 10 |}
        debtor2 |> addRelationWith Owes lender {| amount = 20 |}
        // A debt to a different lender must not leak into the query for `lender`.
        debtor1 |> addRelationWith Owes otherLender {| amount = 99 |}

        let owedToLender =
            world.Query(Related(Owes, lender)).ToSequence()
            |> Seq.map (fun (_, entity) ->
                let owed = entity |> getRelationValue Owes lender |> Option.get
                entity, owed.amount)
            |> Map.ofSeq

        owedToLender =! Map.ofList [ debtor1, 10; debtor2, 20 ]

    [<Fact>]
    member _.``A RelatedToAny query returns all subjects of a relation regardless of target``() =
        let target1 = world.Spawn [||]
        let target2 = world.Spawn [||]
        let a = world.Spawn [||]
        let b = world.Spawn [||]
        let c = world.Spawn [||]

        a |> addRelation FriendsWith target1
        b |> addRelation FriendsWith target1
        c |> addRelation FriendsWith target2

        let subjects =
            world.Query(RelatedToAny FriendsWith).ToSequence() |> Seq.map snd |> Set.ofSeq

        subjects =! set [ a; b; c ]

    [<Fact>]
    member _.``An exclusive relation keeps only the most recently added target``() =
        let ChildOf = tagRelationWith { IsExclusive = true }
        let MarriedTo = valueRelationWith {| yearsMarried = 0 |} { IsExclusive = true }

        let entity1 = world.Spawn [||]
        let entity2 = world.Spawn [||]
        let entity3 = world.Spawn [||]

        entity1 |> addRelation ChildOf entity2
        entity1 |> targetsFor ChildOf =! [| entity2 |]
        entity1 |> addRelation ChildOf entity3
        // The earlier target is dropped, not accumulated.
        entity1 |> targetsFor ChildOf =! [| entity3 |]
        entity1 |> hasRelation ChildOf entity2 =! false

        entity1 |> addRelationWith MarriedTo entity2 {| yearsMarried = 5 |}
        entity1 |> addRelationWith MarriedTo entity3 {| yearsMarried = 8 |}
        entity1 |> targetsFor MarriedTo =! [| entity3 |]
        entity1 |> getRelationValue MarriedTo entity3 =! Some {| yearsMarried = 8 |}
        entity1 |> getRelationValue MarriedTo entity2 =! None

    [<Fact>]
    member _.``A non-exclusive relation keeps every target with independent values``() =
        // The BoundingBoxOn pattern: one subject relates to many targets through a single relation.
        let subject = world.Spawn [||]
        let a = world.Spawn [||]
        let b = world.Spawn [||]
        let c = world.Spawn [||]

        subject |> addRelation FriendsWith a
        subject |> addRelation FriendsWith b
        subject |> addRelation FriendsWith c
        subject |> targetsFor FriendsWith |> Set.ofArray =! set [ a; b; c ]

        // Removing one target leaves the others intact.
        subject |> removeRelation FriendsWith b
        subject |> hasRelation FriendsWith b =! false
        subject |> targetsFor FriendsWith |> Set.ofArray =! set [ a; c ]

        // Per-target data is independent across targets of the same relation.
        subject |> addRelationWith Owes a {| amount = 10 |}
        subject |> addRelationWith Owes c {| amount = 20 |}
        subject |> getRelationValue Owes a =! Some {| amount = 10 |}
        subject |> getRelationValue Owes c =! Some {| amount = 20 |}
        subject |> targetsFor Owes |> Set.ofArray =! set [ a; c ]

    [<Fact>]
    member _.``Destroying a subject removes its relations``() =
        let subject = world.Spawn [||]
        let target = world.Spawn [||]

        subject |> addRelation FriendsWith target
        subject |> destroy

        // The relation is gone from the (now dead) subject's side and from any query.
        subject |> hasRelation FriendsWith target =! false
        world.Query(Related(FriendsWith, target)).ToSequence() |> Seq.isEmpty =! true

    [<Fact>]
    member _.``Destroying a target removes relations pointing to it``() =
        let subject = world.Spawn [||]
        let target = world.Spawn [||]
        let otherTarget = world.Spawn [||]

        subject |> addRelation FriendsWith target
        subject |> addRelation FriendsWith otherTarget
        target |> destroy

        // Only the relation to the destroyed target is cleaned up; the other survives.
        subject |> hasRelation FriendsWith target =! false
        subject |> hasRelation FriendsWith otherTarget =! true
        subject |> targetsFor FriendsWith =! [| otherTarget |]

    [<Fact>]
    member _.``Re-adding an exclusive relation to the same target preserves its value``() =
        // AddRelation is documented as a no-op when the (subject, target) pair already exists, so
        // re-adding an exclusive relation to the SAME target must not reset the value to the schema
        // default. Only a DIFFERENT target displaces the existing one (verified against real Koota).
        let MarriedTo = valueRelationWith {| yearsMarried = 0 |} { IsExclusive = true }

        let entity1 = world.Spawn [||]
        let entity2 = world.Spawn [||]

        entity1 |> addRelationWith MarriedTo entity2 {| yearsMarried = 8 |}
        entity1 |> getRelationValue MarriedTo entity2 =! Some {| yearsMarried = 8 |}

        // Re-adding the same target is a no-op: the value survives.
        entity1 |> addRelation MarriedTo entity2
        entity1 |> getRelationValue MarriedTo entity2 =! Some {| yearsMarried = 8 |}
        entity1 |> targetsFor MarriedTo =! [| entity2 |]

    [<Fact>]
    member _.``IsExclusive reflects the relation configuration``() =
        let exclusive = tagRelationWith { IsExclusive = true }
        FriendsWith.IsExclusive =! false
        Owes.IsExclusive =! false
        exclusive.IsExclusive =! true

    [<Fact>]
    member _.``setRelationValue on an absent relation throws on both backends``() =
        // The documented contract (Types.fs SetRelationValue / ECS.fs setRelationValue) is that
        // setting a value for a relation the subject does not have throws. This must hold on BOTH
        // the .NET mock and real Koota: Koota's raw set() would otherwise phantom-write into the
        // relation store, so the wrapper guards with has() before set(). Both backends throw a
        // message that starts with the shared relationNotPresentError prefix.
        let entity1 = world.Spawn [||]
        let entity2 = world.Spawn [||]

        let message =
            captureExceptionMessage (fun () -> entity1 |> setRelationValue Owes entity2 {| amount = 1 |})

        match message with
        | Some text -> text.StartsWith relationNotPresentError =! true
        | None -> failwith "Expected setRelationValue on an absent relation to throw."
