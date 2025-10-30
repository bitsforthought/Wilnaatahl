import React from "react";
import { Entity } from "koota";
import { useActions, useTrait } from "koota/react";
import { Button, eventActions } from "../ecs";

export function ToolButton({ entity }: { entity: Entity }) {
  const button = useTrait(entity, Button);
  const { handleClick } = useActions(eventActions);
  return (
    <button onClick={handleClick(entity)} disabled={button?.disabled}>
      {button?.label}
    </button>
  );
}
