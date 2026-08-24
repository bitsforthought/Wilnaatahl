namespace Wilnaatahl.ECS

open System
open System.Collections.Generic
#if FABLE_COMPILER
open Fable.Core
#endif

// Here we define types that act as bindings to Koota, an ECS library for TypeScript.
// There interfaces can also be mocked in .NET tests to exercise the F# implementation
// of all our systems.
//
// While in theory these interfaces could map to any TypeScript ECS, their design is
// heavily biased towards Koota's for ease of interop.
//
// NOTE: The heavy use of interfaces is to facilitate interop with TypeScript.
// The Fable compiler won't mangle interface member names, and as much as I'd love
// to use discriminated unions everywhere here, they're pretty ugly in TypeScript.

/// Represents a trait (a.k.a. component or aspect) of a specific type
/// that can be added to or removed from an entity.
type ITrait =
    // NOTE: IsTag is used to distinguish ITagTrait from IValueTrait in TypeScript type tests.
    // We can't use a discriminated union for traits because it would force the
    // type parameter of IValueTrait up to this interface.

    /// True if this is an ITagTrait instance, false otherwise.
    abstract IsTag: bool

/// Represents a trait (a.k.a. component or aspect) that can be added to
/// or removed from an entity and represents a "tag" or "flag" on that entity.
/// It has no value associated with it, but queries can still test for its
/// presence or absence on entities.
type ITagTrait =
    inherit ITrait

/// Represents a trait (a.k.a. component or aspect) that can be added to
/// or removed from an entity and carries values of a specific type.
type IValueTrait<'T> =
    inherit ITrait

// NOTE: In IMutableValueTrait and related operations, separate types are
// used for the "read" and "write" operations purely because F# lacks mutable
// anonymous records AND Koota requires POJOs in order to create struct-of-array
// (SoA) traits, which are more efficient.

/// Represents a trait (a.k.a. component or aspect) that can be added to
/// or removed from an entity and carries mutable values of a specific type.
type IMutableValueTrait<'T, 'TMutable> =
    inherit IValueTrait<'T>

/// Specifies the type of a change tracker.
#if FABLE_COMPILER
[<TypeScriptTaggedUnion("type")>]
#endif
type TrackerType =
    /// Tracks when trait values change on entities.
    | ChangedTracker
    /// Tracks when traits are added to entities.
    | AddedTracker
    /// Tracks when traits are removed from entities (including when they're destroyed).
    | RemovedTracker

/// Used to store tracking state required to track entity changes between runs of a query.
/// Do not instantiate these at global scope or you may get inconsistent query results.
type ITracker =
    abstract Tracker: TrackerType

/// Stores tracking state to track when trait values change on entities.
type IChangedTracker =
    inherit ITracker

/// Stores tracking state to track when traits are added to entities.
type IAddedTracker =
    inherit ITracker

/// Stores tracking state to track when traits are removed from entities (including when they're destroyed).
type IRemovedTracker =
    inherit ITracker

/// Uniquely identifies an entity in the World; Entities may have zero or more traits.
#if FABLE_COMPILER
[<Erase>]
#endif
type EntityId = EntityId of int

/// A relation between entities, optionally carrying a per-(subject, target) value. This non-generic
/// supertype exists so query operators can refer to a relation without knowing its value type.
type IRelation =
    /// True if this relation permits at most one target per subject; false if a subject may relate to many targets.
    abstract IsExclusive: bool

/// A relation between entities parameterised by the "read" and "mutable" types of its per-(subject, target)
/// value. Tag relations carry unit. Values are read and written through the relation accessors on
/// IEntityOperations, keyed by (relation, target); relations themselves are used only as query filters.
type IRelation<'T, 'TMutable> =
    inherit IRelation

/// A relation that carries no value (a Koota storeless relation).
type ITagRelation = IRelation<unit, unit>

/// Represents a query criteria consisting of traits, relations, or combinations thereof.
#if FABLE_COMPILER
[<TypeScriptTaggedUnion("type")>]
#endif
type QueryOperator =
    /// Matches entities with the given trait.
    | With of ITrait
    /// Matches entities that do not have any of the given traits.
    | Not of ITrait[]
    /// Matches entities that have any of the given traits.
    | Or of ITrait[]
    /// Matches entities that have had all of the given traits change value since the last time the same tracker was used.
    | Changed of ITrait[] * IChangedTracker
    /// Matches entities that have had all of the given traits added since the last time the same tracker was used.
    | Added of ITrait[] * IAddedTracker
    /// Matches entities that have had all of the given traits removed since the last time the same tracker was used,
    /// including entities that have been destroyed.
    | Removed of ITrait[] * IRemovedTracker
    /// Matches subjects related to the given target via the given relation.
    | Related of IRelation * EntityId
    /// Matches subjects related to any target via the given relation (wildcard).
    | RelatedToAny of IRelation

/// An initializer for a new entity: it either adds a trait to the entity (a tag trait is its own
/// instance; a value trait carries a value) or relates the entity, as the subject, to a target via a
/// relation. The relation and its value are type-erased, exactly as the trait value is.
type SpawnSpec = // NOTE: We can't use TypeScriptTaggedUnion here as it breaks the generated TypeScript code for functions returning a SpawnSpec.
    private // Private to force type-safe construction via the extension members in the Extensions module.
    | Tag of ITagTrait
    | Val of (ITrait * obj) // Need to erase the types here for the sake of Spawn's signature.
    // Relate the new entity to the target with no explicit value (the relation's schema default).
    | Rel of (IRelation * EntityId)
    // Relate the new entity to the target carrying the given (type-erased) value.
    | ValRel of (IRelation * EntityId * obj)

    // It's static to make the name generated by Fable more palatable on the TypeScript side.
    static member Map fTag fValue fRel fValRel config =
        match config with
        | Tag t -> fTag t
        | Val v -> fValue v
        | Rel r -> fRel r
        | ValRel r -> fValRel r

/// Represent operations on entities in the World. For all entity operations, if an invalid entity ID
/// is provided, the operation will throw an exception.
type IEntityOperations =
    /// Adds the given trait to the given entity. If it already exists, this has no effect.
    abstract Add: someTrait: ITrait -> entity: EntityId -> unit

    /// Destroys the given entity and all its trait instances (if any).
    abstract Destroy: entity: EntityId -> unit

    /// Gets the value for the given trait for the given entity, or None if the entity doesn't have that trait.
    abstract Get: valueTrait: IValueTrait<'T> -> entity: EntityId -> 'T option

    /// Return true if the given entity has the given trait, false otherwise. If this returns false, Get will return None.
    abstract Has: someTrait: ITrait -> entity: EntityId -> bool

    /// Returns the ID of the entity stripped of its world and generation components; Useful for debugging.
    abstract FriendlyId: entity: EntityId -> int

    /// Removes the given trait from the given entity, if it exists. Otherwise, it has no effect.
    abstract Remove: someTrait: ITrait -> entity: EntityId -> unit

    /// Sets the value of the given trait on the given entity to the given value. Throws an exception if the given trait
    /// hasn't been added to the entity.
    abstract Set: valueTrait: IValueTrait<'T> -> value: 'T -> entity: EntityId -> unit

    /// Sets the value of the given trait on the given entity by updating the existing value using a mapping function.
    /// Throws an exception if the given trait hasn't been added to the entity.
    abstract SetWith: valueTrait: IValueTrait<'T> -> update: ('T -> 'T) -> entity: EntityId -> unit

    /// Adds the given relation with the given target to the given entity. If it already exists, this has no effect.
    /// For an exclusive relation, any existing target for the same relation on the entity is removed first.
    abstract AddRelation: relation: IRelation<'T, 'TMutable> -> target: EntityId -> entity: EntityId -> unit

    /// Removes the given relation with the given target from the given entity, if it exists. Otherwise, no effect.
    abstract RemoveRelation: relation: IRelation<'T, 'TMutable> -> target: EntityId -> entity: EntityId -> unit

    /// Returns true if the given entity has the given relation with the given target, false otherwise.
    abstract HasRelation: relation: IRelation<'T, 'TMutable> -> target: EntityId -> entity: EntityId -> bool

    /// Gets the value of the given relation with the given target for the given entity, or None if the entity
    /// is not related to that target via that relation.
    abstract GetRelationValue: relation: IRelation<'T, 'TMutable> -> target: EntityId -> entity: EntityId -> 'T option

    /// Sets the value of the given relation with the given target on the given entity. Throws an exception if the
    /// entity is not related to that target via that relation.
    abstract SetRelationValue:
        relation: IRelation<'T, 'TMutable> -> target: EntityId -> value: 'T -> entity: EntityId -> unit

    /// Returns the first entity that is the target of the given relation for the given entity, or None if no target exists
    /// or the entity doesn't have the given relation.
    abstract TargetFor: relation: IRelation<'T, 'TMutable> -> entity: EntityId -> EntityId option

    /// Returns the all entities that are targets of the given relation for the given entity, if any. Returns an empty
    /// array if no target exists or the entity doesn't have the given relation.
    abstract TargetsFor: relation: IRelation<'T, 'TMutable> -> entity: EntityId -> EntityId[]

/// Specifies whether UpdateEach will apply change detection. The default is Auto.
#if FABLE_COMPILER
[<TypeScriptTaggedUnion("type")>]
#endif
type ChangeDetectionOption =
    /// Change tracking happens if the corresponding query has a Changed modifier.
    | AutoTrack
    /// Change tracking is disabled.
    | NeverTrack
    /// Change tracking is enabled.
    | AlwaysTrack

/// Represents the result of a query. It holds the entities the query matched when it ran, so it
/// can be enumerated more than once. Trait values are not held with them: each one is read when
/// it is handed to a callback, so an entity must still carry the traits the query asked for.
type IQueryResult<'T, 'TMutable> =
    inherit IEnumerable<EntityId>

    /// Invokes the given callback on every value/entity pair of the query results. The values are of the
    /// "read" type, not the "mutable" type.
    abstract ForEach: callback: ('T * EntityId -> unit) -> unit

    /// Invokes the given callback on every value/entity pair of the query results. The values are of the
    /// "mutable" type, not the "read" type, so the intent is that the callback can mutate the values
    /// passed to it.
    abstract UpdateEachWith: changeOption: ChangeDetectionOption -> callback: ('TMutable * EntityId -> unit) -> unit

/// Allows specifying options for the behaviour of relations.
type RelationConfig = {
    IsExclusive: bool
} with

    /// Default relation configuration. Relations are non-exclusive by default.
    static member Default = { IsExclusive = false }

/// Provides functionality to create new traits, relations, and change trackers.
type ITraitFactory =
    // NOTE: We can't use overloadeed method names for tag vs. value relations because TS needs return types
    // of overloaded methods to be sub-types of each other. I don't want to use an erased union because
    // that seems less safe than a tagged union. This is the least bad compromise.

    /// Creates a new Added tracker.
    abstract CreateAdded: unit -> IAddedTracker

    /// Creates a new Changed tracker.
    abstract CreateChanged: unit -> IChangedTracker

    /// Creates a new Removed tracker.
    abstract CreateRemoved: unit -> IRemovedTracker

    /// Defines a new tag relation (carrying no value) with the given configuration.
    abstract Relation: config: RelationConfig -> ITagRelation

    /// Defines a new relation of value type with the given configuration and value stores.
    /// The given values are used for type inference only and are not stored anywhere.
    abstract RelationWith: config: RelationConfig * store: 'T * mutableStore: 'TMutable -> IRelation<'T, 'TMutable>
    // NOTE: We're avoiding overloading here too to make things easier on the TypeScript side.

    /// Defines a new tag trait.
    abstract TagTrait: unit -> ITagTrait

    /// Defines a new value trait. The given values are used for type inference only and are not stored anywhere.
    abstract TraitWith: value: 'T -> mutableValue: 'TMutable -> IMutableValueTrait<'T, 'TMutable>

    /// Defines a new value trait whose values are built by the given factory. An entity given this
    /// trait without a value gets one from a fresh call to the factory.
    abstract TraitWithRef: valueFactory: (unit -> 'T) -> IMutableValueTrait<'T, 'T>

/// Encapsulates all state of the world (traits indexed by entities) in one place
/// for centralized access.
type IWorld =
    /// Adds the given trait to the world.
    abstract Add: someTrait: ITrait -> unit

    /// Gets the value of the given trait from the world, or None if the trait hasn't been added to the world.
    abstract Get: valueTrait: IValueTrait<'T> -> 'T option

    /// Returns true if the world has the given trait, false otherwise. If this returns false, Get will return None.
    abstract Has: someTrait: ITrait -> bool

    // NOTE: It's less than ideal to not overload Query, but unfortunately it's next to impossible to correctly
    // implement overloaded methods on the TypeScript side when they involve variable argument lists, so
    // it's better to avoid it.

    /// Queries for all entities in the world matching the given query criteria. The results won't have any
    /// trait values, only entity IDs.
    abstract Query: [<ParamArray>] where: QueryOperator[] -> IQueryResult<unit, unit>

    /// Queries for all entities in the world matching the given value trait and additional query criteria.
    /// The results will have values for the given value trait as well as entity IDs.
    abstract QueryTrait:
        someTrait: (IMutableValueTrait<'T, 'TMutable>) * [<ParamArray>] where: QueryOperator[] ->
            IQueryResult<'T, 'TMutable>

    /// Queries for all entities in the world matching the two given value traits and additional query criteria.
    /// The results will have values for the given value traits as well as entity IDs.
    abstract QueryTraits:
        firstTrait: IMutableValueTrait<'T, 'TMutable> *
        secondTrait: IMutableValueTrait<'U, 'UMutable> *
        [<ParamArray>] where: QueryOperator[] ->
            IQueryResult<'T * 'U, 'TMutable * 'UMutable>

    /// Queries for all entities in the world matching the three given value traits and additional query criteria.
    /// The results will have values for the given value traits as well as entity IDs.
    abstract QueryTraits3:
        firstTrait: IMutableValueTrait<'T, 'TMutable> *
        secondTrait: IMutableValueTrait<'U, 'UMutable> *
        thirdTrait: IMutableValueTrait<'V, 'VMutable> *
        [<ParamArray>] where: QueryOperator[] ->
            IQueryResult<'T * 'U * 'V, 'TMutable * 'UMutable * 'VMutable>

    /// Queries for all entities in the world matching the four given value traits and additional query criteria.
    /// The results will have values for the given value traits as well as entity IDs.
    abstract QueryTraits4:
        firstTrait: IMutableValueTrait<'T, 'TMutable> *
        secondTrait: IMutableValueTrait<'U, 'UMutable> *
        thirdTrait: IMutableValueTrait<'V, 'VMutable> *
        fourthTrait: IMutableValueTrait<'W, 'WMutable> *
        [<ParamArray>] where: QueryOperator[] ->
            IQueryResult<'T * 'U * 'V * 'W, 'TMutable * 'UMutable * 'VMutable * 'WMutable>

    /// Queries for the first entity in the world matching the given query criteria. Returns the entity ID if
    /// a match is found; None otherwise.
    abstract QueryFirst: [<ParamArray>] where: QueryOperator[] -> EntityId option

    /// Removes the given trait from the world, if it exists. Otherwise, it has no effect.
    abstract Remove: someTrait: ITrait -> unit

    /// Sets the value of the given trait on the world to the given value. Throws an exception if the given trait
    /// hasn't been added to the world.
    abstract Set: valueTrait: IValueTrait<'T> -> value: 'T -> unit

    /// Spawns a new entity in the world initialized with the given spawn specs (traits to add and/or relations
    /// to establish with the new entity as subject) and returns its ID. Entity IDs are unique across all world
    /// instances and remain so even as entities are removed and spawned. This is because they contain the world
    /// ID and a "generation ID" that prevents Spawn from returning the same EntityId value when the underlying
    /// entity "slot" is recycled.
    abstract Spawn: [<ParamArray>] specs: SpawnSpec[] -> EntityId
