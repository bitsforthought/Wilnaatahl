# Wilnaatahl

Wilnaatahl describes the genealogical relationships of Gitx̱san huwilp members.
This glossary defines the shared vocabulary of its genealogical model, scene,
ECS, and development tooling.

## Language

### Huwilp and affiliation

**Wilp / Huwilp**:
A matrilineal House; Wilp is singular and Huwilp is plural.
_Avoid_: Wilps

**Pdeek**:
A clan to which a Wilp belongs; each Wilp belongs to exactly one Pdeek.
The four Pdeek are Lax̱ Gibuu (Wolf), Lax̱ Skiik (Eagle), G̱aneda (Frog), and
Gisḵ'aast (Fireweed).

**Kinship**:
What is recorded about a person's matrilineal House affiliation: a specific Wilp,
a known Pdeek with the specific Wilp unknown, or no structured affiliation recorded.
Missing information does not mean that the person has no Wilp.

**Birth Wilp**:
The Wilp a person was born into, which may differ from their recorded current
House affiliation.

### People and relationships

**Person**:
An individual represented in a genealogical record.

**Couple**:
Two people in a partnered relationship, including marriage, common-law
partnership, or another form of union.
A Couple may have no recorded children.

### Names

**Gitx̱san Name**:
A heritable Gitx̱san name that outlives its bearer and is handed down within a
Wilp; referred to as a Name in this project.

**Name holding**:
A person's past or present holding of a Gitx̱san Name.
A person may hold several Names, and a Name may be held by different people
across generations.

**Colonial name**:
A person's recorded name from outside the Gitx̱san legal system, such as from a
birth certificate issued by a colonial government. These are distinct from their
Gitx̱san Names.

### Family graph and trees

**Family graph**:
The collection of recorded people, Couples, parent-child relationships, and their
projections into Wilp forests.

**Wilp tree**:
A single-rooted genealogical projection for a Wilp, including any recorded
partnerships of the root member and their descendant subtrees.

**Wilp forest**:
The collection of Wilp trees for one Wilp, allowing disconnected recorded lineages
to have separate roots.

**Family**:
A grouping within a Wilp tree centred on one Wilp member, their recorded
partnerships, and the descendant trees associated with those partnerships.

### Scene and layout

**Scene**:
The visual representation of the family graph, with contextual person nodes and
the connectors between them.

**Node**:
A displayed occurrence of a Person in a Wilp's tree representation.
A Person can have more than one Node, so scene identity is not person identity.

**Member node**:
A node representing a Person as a member of the Wilp being displayed.

**Partner node**:
A Couple-specific node for a Person not recorded as a member of the displayed
Wilp.
The same Person can have separate partner nodes for different Couples.

**Couple connector**:
The paired lines joining the displayed nodes of a Couple, whether or not they
have recorded children.
_Avoid_: spouse bar

**Elbow**:
A junction point in the connectors between a Couple and their children, including
the shared branch junction and individual child junctions.

**Layout box**:
A nestable spatial grouping that gives a node or group of nodes its extent and
parent-child connector alignment.
Nested boxes describe relative placement within larger groupings.

**Target position**:
The intended destination of a scene entity, distinct from its current position
while movement is animated.

**Projected node**:
A node's screen-space footprint and viewing depth, used to reason about label
placement and occlusion.

### ECS foundations

**Entity**:
An ECS identity carrying application state, such as a displayed node, control,
or background target; distinct from the identity of a Person.

**Trait**:
A kind of data or marker attached to an entity or the shared world.
This is Koota's term for a component; value traits carry data, while tags record
presence alone.

**System**:
A unit of application behaviour operating on ECS state, organized around one
behaviour rather than one kind of entity.

**World**:
The shared ECS state containing the application's entities and world-level traits,
accessible to both F# and TypeScript.

### Interaction and history

**App mode**:
The current interaction context: Viewing for inspecting genealogical records, or
Moving for arranging nodes.

**Input event**:
A queued record of an interaction, such as a click, drag start, drag movement, or
drag end.

**Intent**:
An interpreted request associated with a click target, such as changing mode,
toggling selection, opening a file, or undoing a change.
It is the requested action, not the raw input event or a recorded undoable change.

**Move**:
A recorded position change for one scene entity, with before and after positions.

**Command**:
A non-empty group of Moves treated as one undoable change.

### Development tooling

**Owned skill**:
A skill governed by the repository's source-formatting, refactoring, and
skill-metadata validation policy, regardless of its origin.

**Vendored skill**:
An upstream-managed skill explicitly excluded from that source and skill-metadata
policy so it can be retained as supplied.
The policy calls this category vendor; upstream identifies the supplying project
rather than a separate ownership category.

**Source rewriting**:
Automatic refactoring and formatting of maintained repository source, distinct
from check-only reporting of required changes.

**Local policy**:
The build policy that enables source rewriting and automatic coverage-baseline
increases for coverage improvements without regressions.

**CI policy**:
The build policy that checks source compliance and existing coverage baselines
without rewriting maintained source or increasing baselines.
Build and test outputs may still be generated.

**Coverage baseline**:
The recorded minimum line and branch coverage for the F# and TypeScript suites.

**Coverage ratchet**:
A policy that rejects coverage regressions and raises recorded minima when
coverage improves.
Improvement in one tracked metric cannot compensate for regression in another.

**Refactor advisory**:
A refactoring finding without an automatic fix that still blocks the strict
refactor gate when it is within the configured scope.

**First-party generated source**:
Fable output compiled from this repository's F# source, distinct from the emitted
third-party runtime and the separately generated runtime bridge.

**Fable runtime bridge**:
The stable import surface for handwritten TypeScript to consume the Fable runtime
without depending on its versioned output-directory name.
