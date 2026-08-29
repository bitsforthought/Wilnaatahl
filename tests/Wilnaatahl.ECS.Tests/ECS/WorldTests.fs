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
type WorldTests() =
    let wrapper = new TestWorldWrapper()
    let world = wrapper.World
    let IsTagged = tagTrait ()
    let Age = valueTrait {| age = 0 |}
    let Tally = refTrait (fun () -> ResizeArray [ 7 ])

    interface IDisposable with
        member _.Dispose() = (wrapper :> IDisposable).Dispose()

    [<Fact>]
    member _.``Can add and remove trait on world entity``() =
        world.Add IsTagged
        world.Has IsTagged =! true
        world.Remove IsTagged
        world.Has IsTagged =! false

    [<Fact>]
    member _.``Can set and get value trait on world entity``() =
        world.Add Age
        world.Set Age {| age = 42 |}
        world.Get Age =! Some {| age = 42 |}
        world.Remove Age
        world.Get Age =! None
        world.AddWith Age {| age = 99 |}
        world.Get Age =! Some {| age = 99 |}

    /// A refTrait uses Koota's whole-value (AoS) storage, and world-level operations enter each
    /// backend through different API paths than entity operations, so this combination needs
    /// direct conformance coverage.
    [<Fact>]
    member _.``Can add and set ref trait on world entity``() =
        world.Add Tally
        world.Get Tally |> Option.map List.ofSeq =! Some [ 7 ]

        world.Set Tally (ResizeArray [ 1; 2 ])
        world.Get Tally |> Option.map List.ofSeq =! Some [ 1; 2 ]

        world.Remove Tally
        world.Get Tally |> Option.map List.ofSeq =! None

        world.AddWith Tally (ResizeArray [ 3 ])
        world.Get Tally |> Option.map List.ofSeq =! Some [ 3 ]

    [<Fact>]
    member _.``Can remove all traits in the world``() =
        world.Add IsTagged
        let entity1 = world.Spawn [| Age.Val {| age = 27 |}; IsTagged.Tag() |]
        let entity2 = world.Spawn [| Age.Val {| age = 44 |}; IsTagged.Tag() |]
        world.RemoveAll IsTagged
        world.Has IsTagged =! false
        world.Query(With IsTagged).ToSequence() |> Seq.isEmpty =! true

        // Other traits are unaffected
        world.QueryTrait(Age).ToSequence() |> Set.ofSeq
        =! set [ {| age = 27 |}, entity1; {| age = 44 |}, entity2 ]

    [<Fact>]
    member _.``Bare and relation-only entities are live members of the world``() =
        // Real Koota tracks live entities independent of their traits: an entity created with no
        // traits (or whose only data is a relation) is still returned by an unfiltered Query() and by
        // a Not-only query, and stays live until destroyed. The mock must agree so conformance tests
        // are portable.
        let Likes = tagRelation ()

        let bare = world.Spawn [||]
        let target = world.Spawn [||]
        let relationOnly = world.Spawn [||]
        relationOnly |> addRelation Likes target
        let tagged = world.Spawn [| IsTagged.Tag() |]

        let allEntities = world.Query().ToSequence() |> Seq.map snd |> Set.ofSeq
        // The world's membership is EXACTLY the spawned entities — every spawn is a live member
        // regardless of whether it carries any trait, and nothing else is surfaced.
        allEntities =! set [ bare; target; relationOnly; tagged ]

        // A relation-only subject is reachable through the relation filters.
        world.Query(Related(Likes, target)).ToSequence() |> Seq.map snd |> Set.ofSeq
        =! set [ relationOnly ]

        world.Query(RelatedToAny Likes).ToSequence() |> Seq.map snd |> Set.ofSeq
        =! set [ relationOnly ]

        // Not narrows out the tagged entity and keeps exactly the bare and relation-only ones.
        let untagged =
            world.Query(Not [| IsTagged |]).ToSequence() |> Seq.map snd |> Set.ofSeq

        untagged =! set [ bare; target; relationOnly ]

        // Destroying a bare entity removes it from the world; removing a trait does not.
        bare |> destroy
        tagged |> remove IsTagged
        let afterChanges = world.Query().ToSequence() |> Seq.map snd |> Set.ofSeq
        afterChanges =! set [ target; relationOnly; tagged ]

// ------------------------------------------------------------------
// .NET-only tests (use Unquote quotations or TestECS internals)
// ------------------------------------------------------------------

#if !FABLE_COMPILER
    [<Fact>]
    member _.``Default trait factory throws NotImplementedException``() =
        let factory = TestSupport.defaultTraitFactory

        let threw (f: unit -> _) =
            try
                f () |> ignore
                false
            with :? NotImplementedException ->
                true

        threw (fun () -> factory.CreateAdded()) =! true
        threw (fun () -> factory.CreateChanged()) =! true
        threw (fun () -> factory.CreateRemoved()) =! true
        threw (fun () -> factory.Relation { IsExclusive = false }) =! true
        threw (fun () -> factory.RelationWith({ IsExclusive = false }, 0, 0)) =! true
        threw (fun () -> factory.TagTrait()) =! true
        threw (fun () -> factory.TraitWith 0 0) =! true
        threw (fun () -> factory.TraitWithRef(fun () -> 0)) =! true

    [<Fact>]
    member _.``Default entity operations throws NotImplementedException``() =
        let ops = TestSupport.defaultEntityOperations
        let entity = EntityId 0
        let trait' = tagTrait ()
        let vt = valueTrait {| x = 0 |}
        let rel = tagRelation ()

        let threw (f: unit -> _) =
            try
                f () |> ignore
                false
            with :? NotImplementedException ->
                true

        threw (fun () -> ops.Add trait' entity) =! true
        threw (fun () -> ops.Destroy entity) =! true
        threw (fun () -> ops.Get vt entity) =! true
        threw (fun () -> ops.Has trait' entity) =! true
        threw (fun () -> ops.FriendlyId entity) =! true
        threw (fun () -> ops.Remove trait' entity) =! true
        threw (fun () -> ops.Set vt {| x = 1 |} entity) =! true
        threw (fun () -> ops.SetWith vt id entity) =! true
        threw (fun () -> ops.AddRelation rel entity entity) =! true
        threw (fun () -> ops.RemoveRelation rel entity entity) =! true
        threw (fun () -> ops.HasRelation rel entity entity) =! true
        threw (fun () -> ops.GetRelationValue rel entity entity) =! true
        threw (fun () -> ops.SetRelationValue rel entity () entity) =! true
        threw (fun () -> ops.TargetFor rel entity) =! true
        threw (fun () -> ops.TargetsFor rel entity) =! true

    [<Fact>]
    member _.``Creating too many worlds throws exception``() =
        let worlds: Option<TestWorld>[] = Array.create TestECS.maxWorlds None

        try
            // Somewhere in the loop, we'll run out of Worlds, although it isn't clear where due to the fact
            // that each test class allocates its own World and they all run in parallel.
            raises
                <@
                    for i = 0 to worlds.Length - 1 do
                        worlds[i] <- Some(new TestWorld())
                @>
        finally
            for i = 0 to worlds.Length - 1 do
                match worlds[i] with
                | Some world -> (world :> IDisposable).Dispose()
                | None -> ()
#endif
