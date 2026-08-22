module Wilnaatahl.Tests.Traits.SpaceTraitsTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Entities
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Tests.EcsTestSupport

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    /// A node on its way to a target has already been given its next resting place, so that
    /// target is where it has settled — not whichever frame of the animation it is showing.
    [<Fact>]
    member _.``settledPosition returns the target of a node that is animating``() =
        let node =
            world.Spawn(
                Position.Val {| x = 1.0; y = 2.0; z = 3.0 |},
                TargetPosition.Val {| x = 7.0; y = 8.0; z = 9.0 |}
            )

        node |> settledPosition =! Line3.pos 7.0 8.0 9.0

    [<Fact>]
    member _.``settledPosition returns the position of a node that has stopped``() =
        let node = world.Spawn(Position.Val {| x = 1.0; y = 2.0; z = 3.0 |})

        node |> settledPosition =! Line3.pos 1.0 2.0 3.0

    /// Everything with a place in the scene has a Position, so an entity with neither trait is a
    /// setup error rather than a case to fall back from.
    [<Fact>]
    member _.``settledPosition throws for an entity with no position``() =
        let entity = world.Spawn()

        let ex = Assert.ThrowsAny<exn>(fun () -> entity |> settledPosition |> ignore)

        ex.Message =! $"Entity {entity} has no TargetPosition or Position."
