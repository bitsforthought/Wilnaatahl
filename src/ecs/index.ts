import { createActions, createWorld, Entity, World } from "koota";
import { Matrix4, Quaternion, Vector3 } from "three";
import { ThreeEvent } from "@react-three/fiber";
import { FamilyGraph_FamilyGraph as FamilyGraph } from "../generated/Model";
import { EntityId } from "../generated/ECS/Types";
import * as Events from "../generated/Traits/Events";
import { layoutNodes } from "../generated/Systems/Layout";
import { runSystems as runFableSystems } from "../generated/Systems/Runner";
import { destroyScene, spawnScene, spawnControls } from "../generated/EntityLifeCycle";
import { fromKootaWorld } from "./koota/kootaWrapper";
import { ClickEvent } from "./traits";

export const world = createWorld();

spawnControls(fromKootaWorld(world));

export function runSystems(input: { world: World; delta: number }) {
  runFableSystems(fromKootaWorld(input.world), input.delta);
}

export const worldActions = createActions((world: World) => {
  const wrappedWorld = fromKootaWorld(world);
  return {
    destroyScene: () => destroyScene(wrappedWorld),
    layoutNodes: (familyGraph: FamilyGraph) => layoutNodes(wrappedWorld, familyGraph),
    spawnScene: (familyGraph: FamilyGraph) => spawnScene(wrappedWorld, familyGraph),
  };
});

export const eventActions = createActions((world: World) => {
  const wrappedWorld = fromKootaWorld(world);
  return {
    handleClick: (entity: Entity & EntityId) => () => Events.handleClick(entity),
    handleDrag: (localMatrix: Matrix4) => {
      const local = new Vector3();
      localMatrix.decompose(local, new Quaternion(), new Vector3());
      Events.handleDrag(wrappedWorld, local.x, local.y, local.z);
    },
    handleDragEnd: () => Events.handleDragEnd(wrappedWorld),
    handleDragStart: () => {
      // We ignore the origin from DragControls since it always seems to be (0, 0, 0).
      Events.handleDragStart(wrappedWorld);
    },
    handleMeshClick: (entity: Entity) => (e: ThreeEvent<MouseEvent>) => {
      entity.add(ClickEvent);
      e.stopPropagation();
    },
    handlePointerDown: (entity: Entity & EntityId) => () => Events.handlePointerDown(entity),
    handlePointerMissed: () => Events.handlePointerMissed(wrappedWorld),
  };
});

export { getLinePositions } from "./connectors";
export { useMeshRef } from "./customHooks";
export {
  Button,
  Dragging,
  Elbow,
  Hidden,
  Line,
  Size,
  MeshRef,
  PersonRef,
  Position,
  Selected,
} from "./traits";
