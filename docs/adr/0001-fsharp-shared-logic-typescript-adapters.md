# F# shared logic and TypeScript browser adapters

Wilnaatahl uses F# for renderer-independent rules, computations, and shared data
structures, compiled through Fable for the browser, while TypeScript adapts DOM,
Three.js, and other browser-library APIs.
This keeps shared logic in one source of truth without proxying browser APIs into
Fable solely to avoid TypeScript.
Locale-dependent date and number formatting stays in TypeScript, while
translatable language strings come from a shared F# locale catalog.

Language choice and ECS ownership are independent: either language can implement
an ECS system, and browser references belong on the relevant ECS entities rather
than in a parallel state registry.

Sources: [architecture constraints](../../AGENTS.md#architecture--data-flow) and
[Names presentation design](../../specs/names-and-detail-overlay.md).
