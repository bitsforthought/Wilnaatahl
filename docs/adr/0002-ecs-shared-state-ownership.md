# ECS ownership of shared mutable state

Wilnaatahl uses the ECS as the default home for shared mutable application state,
with mutable working values kept local to a call or owned by an explicit
instance.
Module-level mutable scratch objects and caches hide data flow and prevent
reentrancy, so speculative allocation savings do not justify sharing them or
moving temporary measurements into ECS traits.
Likewise, trivial derived values are computed where consumed rather than mirrored
into traits, because a cached copy introduces a second source of truth that can
disagree with its inputs.

The F# default does not require duplicating state already available to TypeScript:
both languages query the same world.
Genuine domain computations and derivations that several consumers would repeat
or that are measurably expensive stay in F#.

Source: [state ownership and derivation constraints](../../AGENTS.md#architecture--data-flow).
