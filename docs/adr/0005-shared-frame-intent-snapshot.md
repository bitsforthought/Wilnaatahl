# A shared intent snapshot for each frame

Click order matters when node selections and mode changes occur in the same
frame.
Wilnaatahl derives one ordered intent list from queued clicks and their targets'
declarations before running systems, then shares that immutable snapshot with
every intent-consuming system.
Deriving intents separately within each system would let declaration changes
made by an earlier system reinterpret the same clicks; applying each system's
whole pass as the meaning of the input would also substitute system order for
click order.
The shared list lets each behaviour fold the same click-ordered sequence without
depending on another system's interpretation of it.

This snapshot fixes the interpretation of click-target declarations, not all
input state: clicks separately capture the mode active when raised, and dragging
consumes raw drag events in queue order rather than resolving them to intents.

Sources: [frame orchestration](../../src/Wilnaatahl.Core/Systems/Runner.fs),
[intent derivation](../../src/Wilnaatahl.Core/Traits/Intents.fs),
[full-frame click-order tests](../../tests/Wilnaatahl.Core.Tests/Systems/RunnerTests.fs),
and [snapshot-boundary tests](../../tests/Wilnaatahl.Core.Tests/Traits/IntentsTests.fs).
