module Wilnaatahl.Tests.Systems.ControlsTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Tracking
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Systems.Controls
open Wilnaatahl.Tests.EcsTestSupport

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    /// The guard's whole purpose: a value that hasn't moved must not reach the trait store
    /// (Koota notifies change subscribers without diffing), while a real change must.
    [<Fact>]
    member _.``setButtonDisabled writes only when the disabled state differs``() =
        let button =
            world.Spawn(Button.Val {| sortOrder = 0; label = "Undo"; disabled = false |})

        let buttonWrites = createChanged ()
        // Establish the tracker's baseline so it afterwards reports only real writes.
        world.Query(buttonWrites <=> [| Button |]) |> Seq.length |> ignore

        button |> setButtonDisabled false
        (world.Query(buttonWrites <=> [| Button |]) |> Seq.length) =! 0
        (button |> get Button).Value.disabled =! false

        button |> setButtonDisabled true
        world.Query(buttonWrites <=> [| Button |]) |> Seq.exactlyOne =! button
        (button |> get Button).Value.disabled =! true

    /// Writing `Button` on an entity that doesn't carry it would populate the trait's
    /// backing store without the entity ever owning the trait, so the helper refuses
    /// rather than leaving that inconsistency behind.
    [<Fact>]
    member _.``setButtonDisabled fails on an entity with no Button``() =
        let notAButton = world.Spawn(Position.Val {| x = 0.0; y = 0.0; z = 0.0 |})

        let ex = Assert.ThrowsAny<exn>(fun () -> notAButton |> setButtonDisabled true)

        ex.Message =! $"Entity {notAButton} has no Button trait to disable."
