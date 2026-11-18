# Fixtures and test data

- Use xUnit class fixtures for repetitive setup, with construction in the
  constructor and cleanup through `IDisposable` (`TrackingTests`, `PeopleTests`).
- Use module functions for minimal setup.
- Shared mutable state requires framework/harness necessity and explicit isolation.
- Use stable `TestData.testPeopleAndParents`, not application seed data from
  `Initial.peopleAndParents`.
- Share recurring fixture shapes through `TestData.fs`.
- Reuse helpers such as `TestData.on year month day` instead of reconstructing
  optional dates inline.

Never coin a Gitxsan Name, Wilp, or Pdeeḵ for filler. Names are hereditary
property. Use easily understood English nicknames (`Tinker`, `Sparks`, `The Mayor`,
`Lefty`, `Doc`) and opaque Wilp tokens (`"H"`, `"L"`, `"MM"`). Avoid
Gitxsan-sounding coinages and animal/clan imagery. A real Gitxsan spelling that
is itself the subject of a parser test is not filler.

Wait for the required end state, not a fixed number of frames. Bound the wait
and assert that the condition was reached; see `RunnerTests.runUntilSettled`.
A fixed `for _ in 1..100 do runSystems world 0.1` can silently stop too early
when tuning changes.
