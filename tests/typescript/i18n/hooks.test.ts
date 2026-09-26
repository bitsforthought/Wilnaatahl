// @vitest-environment jsdom

import { createElement } from "react";
import { act, cleanup, renderHook } from "@testing-library/react";
import { WorldProvider } from "koota/react";
import { createWorld, World } from "koota";
import { afterEach, describe, expect, test } from "vitest";
import { CurrentLocale } from "../../../src/ecs";
import { Locale } from "../../../src/generated/ViewModel/Localization";
import { EN } from "../../../src/i18n/format";
import { useLocale } from "../../../src/i18n/hooks";

function worldWrapper(world: World) {
  return function WorldWrapper({ children }: { children?: React.ReactNode }) {
    return createElement(WorldProvider, { world, children });
  };
}

describe("useLocale", () => {
  let world: World;

  afterEach(() => {
    cleanup();
    world?.destroy();
  });

  // English is the only semantic locale today. Distinct instances make trait
  // add/set transitions observable until another Locale case exists.
  test("returns the exact locale instance added after the English fallback", () => {
    world = createWorld();
    const addedLocale = new Locale();

    const { result } = renderHook(() => useLocale(), {
      wrapper: worldWrapper(world),
    });

    expect(result.current).toBe(EN);

    act(() => {
      world.add(CurrentLocale(addedLocale));
    });

    expect(result.current).toBe(addedLocale);
  });

  test("tracks replacement locale instances and falls back after removal", () => {
    world = createWorld();
    const initialLocale = new Locale();
    const replacementLocale = new Locale();
    world.add(CurrentLocale(initialLocale));

    const { result } = renderHook(() => useLocale(), {
      wrapper: worldWrapper(world),
    });

    expect(result.current).toBe(initialLocale);

    act(() => {
      world.set(CurrentLocale, replacementLocale);
    });

    expect(result.current).toBe(replacementLocale);

    act(() => {
      world.remove(CurrentLocale);
    });

    expect(result.current).toBe(EN);
  });
});
