import React from "react";
import { Entity } from "koota";
import { useQuery, useTrait } from "koota/react";
import { ToolButton } from "./ToolButton";
import { Button } from "../ecs";

function sortByButtonOrder(a: Entity, b: Entity) {
  const aOrder = useTrait(a, Button)?.sortOrder ?? 0;
  const bOrder = useTrait(b, Button)?.sortOrder ?? 0;
  return aOrder - bOrder;
}

export default function Toolbar() {
  const buttonEntities = useQuery(Button);
  return (
    <div style={{ margin: "8px", display: "flex", gap: "8px" }}>
      {buttonEntities.sort(sortByButtonOrder).map((entity: Entity) => (
        <ToolButton entity={entity} key={entity.id()} />
      ))}
    </div>
  );
}
