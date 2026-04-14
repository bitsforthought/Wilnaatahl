namespace Wilnaatahl.Tests.ECS

open System
open Wilnaatahl.ECS
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
