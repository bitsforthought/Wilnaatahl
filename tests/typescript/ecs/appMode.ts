import {
  AppMode_Moving,
  AppMode_Viewing,
  type AppMode_$union,
} from "../../../src/generated/Traits/ViewTraits";

/**
 * Restore the return types lost by Fable's nullary-case singleton implementation:
 * https://github.com/fable-compiler/Fable/issues/3867
 * https://github.com/fable-compiler/Fable/pull/4729
 */
export const viewingMode = AppMode_Viewing as () => AppMode_$union;
export const movingMode = AppMode_Moving as () => AppMode_$union;
