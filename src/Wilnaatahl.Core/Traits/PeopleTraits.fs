module Wilnaatahl.Traits.PeopleTraits

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Relation
open Wilnaatahl.ECS.Trait
open Wilnaatahl.Model

/// Used for entities that represent "tree nodes", i.e. people in the family tree.
let PersonRef = refTrait (fun () -> Person.Empty)

/// Indicates which Wilp a tree node is rendered in. If an entity has this relation, it must also have a PersonRef,
/// and vice-versa.
let RenderedIn = tagRelationWith { IsExclusive = true }

/// Identifies an entity that represents a rendered Wilp, which is a special BoundingBox that contains, directly or
/// indirectly, all tree nodes representing wilp members.
let Wilp = valueTrait {| wilpName = "" |}
