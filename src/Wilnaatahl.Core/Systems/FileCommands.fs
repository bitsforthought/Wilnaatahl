module Wilnaatahl.Systems.FileCommands

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Trait
open Wilnaatahl.Traits.Intents
open Wilnaatahl.Traits.ViewTraits

/// World signal raised when the open-file button is clicked. It is deliberately not
/// cleared during frame cleanup, so it persists until consumed and removed; a single
/// click therefore yields a single request that outlives the frame it was raised in.
let OpenFileRequested = tagTrait ()

/// World signal raised when the save button is clicked. Like OpenFileRequested, it
/// persists until consumed and removed rather than being cleared each frame.
let SaveRequested = tagTrait ()

/// Spawns the open-file and save toolbar buttons at consecutive sort orders.
let spawnFileControls (sortOrder, world: IWorld) =
    world.Spawn(
        Button.Val {| sortOrder = sortOrder; label = "Open file…"; disabled = false |},
        EmitsIntent.Val [ OpenFile ]
    )
    |> ignore

    world.Spawn(Button.Val {| sortOrder = sortOrder + 1; label = "Save"; disabled = false |}, EmitsIntent.Val [ Save ])
    |> ignore

    sortOrder + 2, world

/// Maps this frame's `OpenFile`/`Save` intents to the matching world request signal. Each is
/// checked independently, so raising one request never depends on the other.
let internal handleFileCommands intents (world: IWorld) =
    if intents |> List.contains OpenFile then
        world.Add OpenFileRequested

    if intents |> List.contains Save then
        world.Add SaveRequested

    world
