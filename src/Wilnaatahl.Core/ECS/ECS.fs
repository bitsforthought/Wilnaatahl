namespace Wilnaatahl.ECS

open System
#if FABLE_COMPILER
open Fable.Core

/// Exposes Koota functionality via wrappers defined in F# and implemented in TypeScript.
module private KootaWrapper =
    [<Import("createTraitFactory", "../../ecs/koota/kootaWrapper.ts")>]
    let createTraitFactory: unit -> ITraitFactory = nativeOnly

    [<Import("createEntityOperations", "../../ecs/koota/kootaWrapper.ts")>]
    let createEntityOperations: unit -> IEntityOperations = nativeOnly

#else

/// Default implementations that throw, ensuring tests install a proper mock or wrapper.
module internal TestSupport =
    /// A default implementation of ITraitProvider that always throws.
    /// This ensures that unit tests exercising the ECS don't forget to install a mock.
    let defaultTraitFactory =
        { new ITraitFactory with
            member _.CreateAdded() = raise (NotImplementedException())
            member _.CreateChanged() = raise (NotImplementedException())
            member _.CreateRemoved() = raise (NotImplementedException())
            member _.Relation _ = raise (NotImplementedException())
            member _.RelationWith(_, _, _) = raise (NotImplementedException())
            member _.TagTrait() = raise (NotImplementedException())

            member _.TraitWith<'T, 'TMutable> _ _ : IMutableValueTrait<'T, 'TMutable> =
                raise (NotImplementedException())

            member _.TraitWithRef<'T> _ : IMutableValueTrait<'T, 'T> = raise (NotImplementedException())
        }

    /// A default implementation of IEntityOperations that always throws.
    /// This ensures that unit tests exercising the ECS don't forget to install a mock.
    let defaultEntityOperations =
        { new IEntityOperations with
            member _.Add _ _ = raise (NotImplementedException())
            member _.Destroy _ = raise (NotImplementedException())
            member _.Get _ _ = raise (NotImplementedException())
            member _.Has _ _ = raise (NotImplementedException())
            member _.FriendlyId _ = raise (NotImplementedException())
            member _.Remove _ _ = raise (NotImplementedException())
            member _.Set _ _ _ = raise (NotImplementedException())
            member _.SetWith _ _ _ = raise (NotImplementedException())
            member _.TargetFor _ _ = raise (NotImplementedException())
            member _.TargetsFor _ _ = raise (NotImplementedException())
        }

#endif

/// Provides global access to ECS functionality. When compiling for .NET, it provides hooks for unit
/// tests to substitute Fable implementations with .NET ones.
type Globals private () =
    static member val Instance = Globals()

    // MAINTENANCE NOTE: We want global dependencies to be immutable and instantiated on the TypeScript
    // side for the app itself, but mutable and settable by unit tests on the .NET side.

    /// Provides functionality to create new traits, relations, and change trackers.
    member val Traits: ITraitFactory =
#if FABLE_COMPILER
        KootaWrapper.createTraitFactory () with get
#else
        TestSupport.defaultTraitFactory with get, set
#endif

    /// Provides access to operations on entities in the world.
    member val Entities: IEntityOperations =
#if FABLE_COMPILER
        KootaWrapper.createEntityOperations () with get
#else
        TestSupport.defaultEntityOperations with get, set
#endif

/// Provides convenience functions for working with relations.
module Relation =

    /// Creates a new tag relation with the given configuration.
    let tagRelationWith config = Globals.Instance.Traits.Relation config

    /// Creates a new tag relation with the default configuration.
    let tagRelation () = tagRelationWith RelationConfig.Default

    /// Creates a new relation with a mutable value and the given configuration.
    let mutableRelationWith store mutableStore config =
        Globals.Instance.Traits.RelationWith(config, store, mutableStore)

    /// Creates a new relation with a mutable value and the default configuration.
    let mutableRelation store mutableStore =
        mutableRelationWith store mutableStore RelationConfig.Default

    /// Creates a new relation with an immutable value and the given configuration.
    let valueRelationWith store config = mutableRelationWith store store config

    /// Creates a new relation with an immutable value and the default configuration.
    let valueRelation store =
        valueRelationWith store RelationConfig.Default

    /// Returns the trait that represents the relation with the given entity as target. The returned
    /// trait can be used to set a value for the relation if it's a value trait, to add the relation
    /// with the given target to a subject entity, or anything else a trait can be used for.
    let inline (=>) (rel: IRelation<'TTrait>) targetEntity = rel.WithTarget targetEntity

/// Provides convenience functions for working with entities and traits.
module Entity =
    open Relation

    /// Adds the given trait to the entity.
    let add someTrait entity =
        entity |> Globals.Instance.Entities.Add someTrait

    /// Destroys the given entity.
    let destroy entity =
        entity |> Globals.Instance.Entities.Destroy

    /// Returns a debugging-friendly ID for the given entity, stripped of any internal prefixes.
    let friendlyId entity =
        entity |> Globals.Instance.Entities.FriendlyId

    /// Gets the value of the given trait from the entity, if present.
    let get valueTrait entity =
        entity |> Globals.Instance.Entities.Get valueTrait

    /// Gets the value of the first of the two given traits that is present on the entity, if any.
    let getFirst valueTrait1 valueTrait2 entity =
        match entity |> get valueTrait1 with
        | Some value1 -> Some value1
        | None -> entity |> get valueTrait2

    /// Returns true if the entity has the given trait.
    let has someTrait entity =
        entity |> Globals.Instance.Entities.Has someTrait

    /// Removes the given trait from the entity.
    let remove someTrait entity =
        entity |> Globals.Instance.Entities.Remove someTrait

    /// Sets the value of the given trait on the entity.
    let setValue valueTrait value entity =
        entity |> Globals.Instance.Entities.Set valueTrait value

    /// Sets the value of the given trait on the entity using the provided
    /// function to update the existing value.
    let setWith valueTrait f entity =
        entity |> Globals.Instance.Entities.SetWith valueTrait f

    /// Returns the target entity for the given relation on the entity, if present.
    let targetFor relation entity =
        entity |> Globals.Instance.Entities.TargetFor relation

    /// Returns the target entity for the given relation on the entity along with the relation value, if present.
    let targetWithValueFor relation entity =
        match entity |> targetFor relation with
        | Some target ->
            match entity |> get (relation => target) with
            | Some value -> Some(target, value)
            | None ->
                // Value traits are initialized with their schema default on add, so get should
                // always return Some if the relation exists. This would indicate a bug.
                failwith $"Relation value not found on entity {entity} despite relation existing."
        | None -> None // Entity may not have the relation, which is a legit case.

    /// Returns all target entities for the given relation on the entity.
    let targetsFor relation entity =
        entity |> Globals.Instance.Entities.TargetsFor relation

    /// Adds the given trait to the entity and sets its value in a single operation.
    let addWith valueTrait value entity =
        entity |> add valueTrait
        entity |> setValue valueTrait value

/// Provides convenience functions for working with change trackers.
module Tracking =

    /// Convenience operator to create a QueryOperator from a change tracker.
    let inline (<=>) (tracker: ITracker) traits =
        match tracker.Tracker with
        | AddedTracker -> Added(traits, tracker :?> IAddedTracker)
        | ChangedTracker -> Changed(traits, tracker :?> IChangedTracker)
        | RemovedTracker -> Removed(traits, tracker :?> IRemovedTracker)

    /// Creates a new change tracker that tracks added traits.
    let createAdded () = Globals.Instance.Traits.CreateAdded()

    /// Creates a new change tracker that tracks changed traits.
    let createChanged () = Globals.Instance.Traits.CreateChanged()

    /// Creates a new change tracker that tracks removed traits.
    let createRemoved () = Globals.Instance.Traits.CreateRemoved()

/// Provides convenience functions for working with traits.
module Trait =

    /// Creates a new tag trait.
    let tagTrait () = Globals.Instance.Traits.TagTrait()

    /// Creates a new trait with a mutable value.
    let mutableTrait data mutableData =
        Globals.Instance.Traits.TraitWith data mutableData

    /// Creates a new trait with an immutable value.
    let valueTrait data = mutableTrait data data

    /// Creates a new trait with a reference type value.
    let refTrait dataFactory =
        Globals.Instance.Traits.TraitWithRef dataFactory

/// Provides convenience extensions for ECS types.
module Extensions =
    open Entity

    type ITagTrait with
        /// Creates a TraitSpec instance for the given tag trait.
        member this.Tag() = Tag this

    type IValueTrait<'T> with
        /// Creates a TraitSpec instance for the given value trait and value.
        member this.Val(value: 'T) = Val(this, value :> obj)

    type IQueryResult<'T, 'TMutable> with
        /// Returns a sequence over the results of the query, including trait values. Note that this fully
        /// materializes all query results.
        member this.ToSequence() =
            let results = ResizeArray<'T * EntityId>()
            this.ForEach <| fun x -> results.Add x
            results :> seq<'T * EntityId>

        /// Invokes the given callback on every value/entity pair of the query results. The values are of the
        /// "mutable" type, not the "read" type, so the intent is that the callback can mutate the values
        /// passed to it. Change detection happens only if the query has a change tracking modifier.
        member this.UpdateEach callback = this.UpdateEachWith AutoTrack callback

    type IWorld with
        /// Adds the given trait to the world and sets its value in a single operation.
        member this.AddWith valueTrait value =
            this.Add valueTrait
            this.Set valueTrait value

        /// Queries for the first entity matching the given criteria that has the given value trait
        /// and returns the entity along with the trait value.
        member this.QueryFirstTrait
            (someTrait: IValueTrait<'T>, [<ParamArray>] where: QueryOperator[])
            : (EntityId * 'T) option =
            match this.QueryFirst [| With someTrait; yield! where |] with
            | Some entity ->
                match entity |> get someTrait with
                | Some value -> Some(entity, value)
                | None ->
                    // The trait could only be missing if it was removed in between QueryFirst and get,
                    // which would be a concurrency bug in the app.
                    failwith $"Required trait not found on entity {entity}."
            | None -> None // Legit case where no entities meet the criteria.

        /// Queries for the first entity matching the given criteria that has the given relation (i.e. acts as the
        /// subject of the relation) and returns the subject entity, target entity, and the trait value.
        member this.QueryFirstTarget<'T, 'TTrait when 'TTrait :> IValueTrait<'T>>
            (relation: IRelation<'TTrait>, [<ParamArray>] where: QueryOperator[])
            =
            match this.QueryFirst [| With(relation.Wildcard()); yield! where |] with
            | Some entity ->
                match entity |> targetWithValueFor relation with
                | Some(target, value) -> Some(entity, target, value)
                | None ->
                    // targetWithValueFor should always return Some if the entity matched the
                    // wildcard query, since value traits are initialized on add. This would
                    // indicate a bug.
                    failwith $"Relation target/value not found on entity {entity} despite matching wildcard query."
            | None -> None // Legit case where no entities have the relation.

        /// Removes all instances of the given trait from all entities in the world.
        member this.RemoveAll someTrait =
            for entity in this.Query(With someTrait) do
                entity |> remove someTrait

            // Handle the World separately (Koota excludes the World entity from queries).
            this.Remove someTrait
