import { RefObject, useLayoutEffect, useRef } from "react";
import { Mesh } from "three";
import { Entity } from "koota";
import { MeshRef } from "./traits";

// Custom React hook to dynamically attach a Mesh to an Entity via the MeshRef trait.
export function useMeshRef(entity: Entity): RefObject<Mesh | null> {
  const ref = useRef<Mesh>(null);

  useLayoutEffect(() => {
    if (!ref.current) {
      return;
    }

    entity.add(MeshRef(ref.current));
    return () => {
      entity.remove(MeshRef);
    };
  }, [entity]);

  return ref;
}
