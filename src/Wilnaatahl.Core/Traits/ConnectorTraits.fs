module Wilnaatahl.Traits.ConnectorTraits

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Relation
open Wilnaatahl.ECS.Trait

/// Pins the Position of one entity to the mid-point of a target Line entity.
let Bisects = tagRelationWith { IsExclusive = true }

/// Represents a line between two entities with Position. Acts as the target of the EndpointOf relation.
let Line = tagTrait ()

/// Marks an entity with Position as being an endpoint of the target Line entity. A Line should have exactly
/// two subject entities referring to it via this relation.
let EndpointOf = tagRelationWith { IsExclusive = true }

/// Relationship from a Bounding Box to an entity with Position that it contains.
let BoundingBoxOn = tagRelation () // TODO: Upgrade Koota so it doesn't kill perf to have one trait per node

/// Relationship from one entity with Position to another entity with Position, where the subject's X co-ordinate
/// will automatically track the target's with the given distance.
let SnapToX = valueRelationWith {| x = 0.0 |} { IsExclusive = true }

/// Relationship from one entity with Position to another entity with Position, where the subject's Y co-ordinate
/// will automatically track the target's with the given distance.
let SnapToY = valueRelationWith {| y = 0.0 |} { IsExclusive = true }

/// Relationship from one entity with Position to another entity with Position, where the subject's Z co-ordinate
/// will automatically track the target's with the given distance.
let SnapToZ = valueRelationWith {| z = 0.0 |} { IsExclusive = true }

/// Relationship from one Line entity to another, where the subject's endpoint Positions will automatically track
/// the target's so that the lines stay parallel. The subject line is offset from the target line by a vector that is
/// the projection of the vertical axis onto the plane that is perpendicular to the target line. To put it another way,
/// it is vertically offset in such a way that it appears co-incident with the target line when viewed from above or below,
/// no matter what the direction of the line is in 3D space. It is not just a simple translation on the Y-axis.
/// The absolute value of the given offset determines the distance between the lines while the sign determines the direction.
/// Negative offsets place the line below, positive offsets above. When the target line is vertical, the subject line will be
/// offset in the x or z direction, depending on which is greater. Note that the line will stay above or below the target line
/// no matter how its endpoints are moved in space.
let Parallels = valueRelationWith {| offset = 0.0 |} { IsExclusive = true }

/// Marks an entity with Position and MeshRef as representing an "elbow" connector, which is rendered as
/// a small sphere the same diameter and colour as lines.
let Elbow = tagTrait ()

/// Marks an entity such that it shouldn't be rendered (i.e. -- React components won't add a MeshRef trait).
let Hidden = tagTrait ()

/// Used to mark connector entities for ease of cleanup later.
let Connector = tagTrait ()
