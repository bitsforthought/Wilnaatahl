# Fixtures and test data

Use xUnit class fixtures for repetitive setup, with construction in the
constructor and cleanup through `IDisposable` (`TrackingTests`, `PeopleTests`).
Module functions are appropriate for minimal setup. Shared mutable state still
requires framework/harness necessity and explicit isolation.

- Use stable `TestData.testPeopleAndParents`, not application seed data from
  `Initial.peopleAndParents`.
- Share recurring fixture shapes through `TestData.fs`.
- Parameterize the varying value (`dragNodeTo node x`) instead of closing over
  one entity.
- Reuse helpers such as `TestData.on year month day` instead of reconstructing
  optional dates inline.

Wait for the required end state, not a fixed number of frames. Bound the wait
and assert that the condition was reached; see `RunnerTests.runUntilSettled`.
A fixed `for _ in 1..100 do runSystems world 0.1` can silently stop too early
when tuning changes.
