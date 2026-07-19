module Wilnaatahl.Traits.PeopleTraits

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Relation
open Wilnaatahl.ECS.Trait
open Wilnaatahl.Model
open Wilnaatahl.ViewModel

/// Used for entities that represent "tree nodes", i.e. people in the family tree.
let PersonRef = refTrait (fun () -> Person.Empty)

/// A never-rendered placeholder for the NodeKeyRef factory default. Its PersonId is
/// deliberately invalid (negative) so that, were the default ever to leak into a layout
/// lookup, it would fail loudly rather than silently colliding with a real node's key.
let private emptyNodeKey = MemberNode(PersonId(-1))

/// The identity of a tree node within its Wilp's layout. Every tree node carries this trait
/// alongside its PersonRef: a rendered-Wilp member surfaces as one MemberNode, while a
/// from-outside spouse surfaces as one PartnerNode per marriage, so a spouse married to
/// several Wilp members renders as several distinct nodes.
let NodeKeyRef = refTrait (fun () -> emptyNodeKey)

/// Indicates which Wilp a tree node is rendered in. A tree node is exactly an entity that
/// carries PersonRef, NodeKeyRef, and this relation together: any one of the three implies
/// the other two.
let RenderedIn = tagRelationWith { IsExclusive = true }

/// Identifies an entity that represents a rendered Wilp, which is a special BoundingBox that contains, directly or
/// indirectly, all tree nodes representing wilp members.
let RenderedWilp = valueTrait {| wilpName = "" |}

/// The precomputed `NodeLabelView` rendered on this tree node.
let NodeLabel = refTrait (fun () -> NodeLabelView.Empty)
