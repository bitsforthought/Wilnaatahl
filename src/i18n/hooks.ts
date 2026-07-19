import { useTrait, useWorld } from "koota/react";
import { Locale } from "../generated/ViewModel/Localization";
import { CurrentLocale } from "../ecs";
import { EN } from "./format";

/**
 * The active UI locale, read reactively from the world's `CurrentLocale` trait,
 * falling back to English until that trait is written.
 */
export function useLocale(): Locale {
  const world = useWorld();
  return useTrait(world, CurrentLocale) ?? EN;
}
