/// Guarded updates to toolbar button traits.
module Wilnaatahl.Systems.Controls

open Wilnaatahl.ECS.Entity
open Wilnaatahl.Traits.ViewTraits

/// Sets a toolbar button's `disabled` state, writing the `Button` trait only when the value
/// actually changes — a trait write notifies change subscribers whether or not the value
/// moved. Fails when the entity carries no `Button`, since writing one there would populate
/// the trait's store for an entity that does not own the trait.
let internal setButtonDisabled disabled buttonEntity =
    match buttonEntity |> get Button with
    | None -> failwith $"Entity {buttonEntity} has no Button trait to disable."
    | Some data when data.disabled = disabled -> ()
    | Some _ -> buttonEntity |> setWith Button (fun data -> {| data with disabled = disabled |})
