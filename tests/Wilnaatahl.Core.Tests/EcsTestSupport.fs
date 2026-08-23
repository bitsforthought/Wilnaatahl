module Wilnaatahl.Tests.EcsTestSupport

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Mocks
open Wilnaatahl.Entities
open Wilnaatahl.Traits.History
open Wilnaatahl.Traits.ViewTraits

/// A disposable wrapper around TestWorld that installs the TestECS mock
/// and provides access to the IWorld interface.
type EcsWorld() =
    do TestECS.install ()
    let testWorld = new TestWorld()
    let world = testWorld :> IWorld

    member _.World = world

    interface System.IDisposable with
        member _.Dispose() =
            (testWorld :> System.IDisposable).Dispose()

/// The label on a toolbar button. Fails when the entity carries no `Button`.
let buttonLabel entity = (entity |> get Button).Value.label

/// Whether a toolbar button is disabled. Fails when the entity carries no `Button`.
let isButtonDisabled entity = (entity |> get Button).Value.disabled

/// The toolbar button carrying the given label. Fails when no button has it.
let buttonWithLabel label (world: IWorld) =
    world.Query(With Button)
    |> Seq.tryFind (fun entity -> buttonLabel entity = label)
    |> Option.defaultWith (fun () -> failwith $"No button labelled {label}.")

/// One node's part in a change that carried it from one x to another, leaving y and z at zero.
let internal moveAlongX node fromX toX = {
    Entity = node
    Before = Line3.pos fromX 0.0 0.0
    After = Line3.pos toX 0.0 0.0
}
