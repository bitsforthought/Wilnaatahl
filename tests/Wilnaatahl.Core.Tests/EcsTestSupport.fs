module Wilnaatahl.Tests.EcsTestSupport

open System
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Mocks
open Wilnaatahl.ECS.Trait
open Wilnaatahl.Entities
open Wilnaatahl.Traits.History
open Wilnaatahl.Traits.Intents
open Wilnaatahl.Traits.ViewTraits

type EcsWorld() =
    do TestECS.install ()
    let testWorld = new TestWorld()
    let world = testWorld :> IWorld

    member _.World = world

    interface System.IDisposable with
        member _.Dispose() =
            (testWorld :> System.IDisposable).Dispose()

type internal AddTrackingWorld(inner: IWorld) =
    let mutable addCalls = 0

    member _.AddCalls = addCalls

    interface IWorld with
        member _.Add someTrait =
            addCalls <- addCalls + 1
            inner.Add someTrait

        member _.Get(valueTrait: IValueTrait<'T>) = inner.Get valueTrait
        member _.Has someTrait = inner.Has someTrait
        member _.Query([<ParamArray>] where: QueryOperator[]) = inner.Query(where)
        member _.QueryTrait(someTrait, [<ParamArray>] where: QueryOperator[]) = inner.QueryTrait(someTrait, where)

        member _.QueryTraits(firstTrait, secondTrait, [<ParamArray>] where: QueryOperator[]) =
            inner.QueryTraits(firstTrait, secondTrait, where)

        member _.QueryTraits3(firstTrait, secondTrait, thirdTrait, [<ParamArray>] where: QueryOperator[]) =
            inner.QueryTraits3(firstTrait, secondTrait, thirdTrait, where)

        member _.QueryTraits4(firstTrait, secondTrait, thirdTrait, fourthTrait, [<ParamArray>] where: QueryOperator[]) =
            inner.QueryTraits4(firstTrait, secondTrait, thirdTrait, fourthTrait, where)

        member _.QueryFirst([<ParamArray>] where: QueryOperator[]) = inner.QueryFirst(where)
        member _.Remove someTrait = inner.Remove someTrait
        member _.Set (valueTrait: IValueTrait<'T>) (value: 'T) = inner.Set valueTrait value
        member _.Spawn([<ParamArray>] specs: SpawnSpec[]) = inner.Spawn(specs)

/// The label on a toolbar button. Fails when the entity carries no `Button`.
let buttonLabel entity = (entity |> get Button).Value.label

let internal enterMode mode (world: IWorld) =
    if world.Has CurrentMode then
        world.Set CurrentMode mode
    else
        world.AddWith CurrentMode mode

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

/// Spawns the background singleton with its clear-selection declaration.
let internal spawnBackground (world: IWorld) =
    world.Spawn(Background.Tag(), EmitsIntent.Val [ ClearSelection ]) |> ignore

let internal runWithIntents system (world: IWorld) = system (world |> derivedIntents) world
