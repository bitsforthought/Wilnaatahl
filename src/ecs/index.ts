import { createActions, Entity, World } from "koota";
import { Matrix4, Quaternion, Vector3 } from "three";
import { ThreeEvent } from "@react-three/fiber";
import { FamilyGraph_FamilyGraph as FamilyGraph } from "../generated/Model";
import { EntityId } from "../generated/ECS/Types";
import * as Events from "../generated/Traits/Events";
import { layoutNodes } from "../generated/Systems/Layout";
import { runSystems as runFableSystems } from "../generated/Systems/Runner";
import { spawnControls, spawnScene } from "../generated/EntityLifeCycle";
import { fromKootaWorld } from "./koota/kootaWrapper";

export function runSystems(input: { world: World; delta: number }) {
  runFableSystems(fromKootaWorld(input.world), input.delta);
}

export const worldActions = createActions((world: World) => {
  const wrappedWorld = fromKootaWorld(world);
  return {
    layoutNodes: (familyGraph: FamilyGraph) => layoutNodes(wrappedWorld, familyGraph),
    spawnControls: () => spawnControls(wrappedWorld),
    spawnScene: (familyGraph: FamilyGraph) => spawnScene(wrappedWorld, familyGraph),
  };
});

export const eventActions = createActions((world: World) => {
  const wrappedWorld = fromKootaWorld(world);
  return {
    handleClick: (entity: Entity & EntityId) => () => Events.handleClick(wrappedWorld, entity),
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
      Events.handleClick(wrappedWorld, entity as Entity & EntityId);
      e.stopPropagation();
    },
    handlePointerMissed: () => Events.handlePointerMissed(wrappedWorld),
  };
});

export { getLinePositions } from "./connectors";
export { useMeshRef, useOverlayVisible } from "./customHooks";
export {
  Button,
  CurrentLocale,
  CurrentMode,
  DragInFlight,
  Elbow,
  Hidden,
  Line,
  Size,
  MeshRef,
  NodeLabel,
  OpenFileRequested,
  PersonRef,
  Position,
  SaveRequested,
  Selected,
} from "./traits";
