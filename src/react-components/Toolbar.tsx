import React from "react";
import { Entity, Not } from "koota";
import { useQuery } from "koota/react";
import { ToolButton } from "./ToolButton";
import { Button, Hidden } from "../ecs";

// Read the sortOrder imperatively rather than via useTrait. Hooks must be
// called the same number of times on every render, so they cannot be invoked
// inside a comparator (the entity count varies). The Toolbar is re-rendered
// when entities are added or removed via useQuery(Button) below; sortOrder is
// effectively static after spawn, so missing the subscription doesn't matter.
function sortByButtonOrder(a: Entity, b: Entity) {
  const aOrder = a.get(Button)?.sortOrder ?? 0;
  const bOrder = b.get(Button)?.sortOrder ?? 0;
  return aOrder - bOrder;
}

export default function Toolbar() {
  // Exclude Hidden buttons: the F# side hides the Move-mode-only buttons (undo,
  // redo and select-mode, via the Hidden trait) while in View mode. Because the
  // query filters on trait membership, adding/removing Hidden re-runs it, so
  // buttons appear and disappear as the mode toggles.
  const buttonEntities = useQuery(Button, Not(Hidden));

  // Hide the toolbar until its control entities exist, so a freshly-created
  // World does not flash an empty toolbar before spawnControls runs.
  if (buttonEntities.length === 0) return null;

  return (
    <div style={{ margin: "8px", display: "flex", gap: "8px" }}>
      {[...buttonEntities].sort(sortByButtonOrder).map((entity: Entity) => (
        <ToolButton entity={entity} key={entity.id()} />
      ))}
    </div>
  );
}
