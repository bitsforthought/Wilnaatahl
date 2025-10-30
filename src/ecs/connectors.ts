import { Vector3 } from "three";
import { Entity, World } from "koota";
import { EntityId } from "../generated/ECS/Types";
import { getPositions as wrappedGetLinePositions } from "../generated/Entities/Line";
import { fromKootaWorld } from "./koota/kootaWrapper";

export function getLinePositions(world: World, line: Entity & EntityId) {
  const wrappedWorld = fromKootaWorld(world);
  const [v1, v2] = wrappedGetLinePositions(wrappedWorld, line);
  return [new Vector3(v1.x, v1.y, v1.z), new Vector3(v2.x, v2.y, v2.z)];
}
