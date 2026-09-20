// @vitest-environment jsdom

import { createElement } from "react";
import { act, cleanup, renderHook } from "@testing-library/react";
import { WorldProvider } from "koota/react";
import { createWorld } from "koota";
import { afterEach, describe, expect, test } from "vitest";
import { CurrentLocale } from "../../../src/ecs";
import { LocaleModule_parse } from "../../../src/generated/ViewModel/Localization";
import { EN } from "../../../src/i18n/format";
import { useLocale } from "../../../src/i18n/hooks";

type World = ReturnType<typeof createWorld>;

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

  test("falls back to the English locale before the world has a locale", () => {
    world = createWorld();

    const { result } = renderHook(() => useLocale(), {
      wrapper: worldWrapper(world),
    });

    expect(result.current).toBe(EN);
  });

  test("subscribes to world locale changes and returns the current locale", () => {
    world = createWorld();
    const initialLocale = LocaleModule_parse("en-US");
    const nextLocale = LocaleModule_parse("en-US");
    world.add(CurrentLocale(initialLocale));

    const { result } = renderHook(() => useLocale(), {
      wrapper: worldWrapper(world),
    });

    expect(result.current).toBe(initialLocale);

    act(() => {
      world.set(CurrentLocale, nextLocale);
    });

    expect(result.current).toBe(nextLocale);

    act(() => {
      world.remove(CurrentLocale);
    });

    expect(result.current).toBe(EN);
  });
});
