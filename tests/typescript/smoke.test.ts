// Harness smoke test. Its purpose is not behavioural coverage of `styles.ts` but
// proof that the Vitest harness resolves and evaluates a real module under
// `src/` through the Vite pipeline — TypeScript compilation, ESM resolution and
// the `react` type import all included. Later commits add the actual unit tests;
// this one fails loudly if the runner itself is misconfigured.

import { describe, expect, it } from "vitest";
import { dismissButtonStyle } from "../../src/react-components/styles";

describe("test harness", () => {
  it("resolves and evaluates a module under src/", () => {
    expect(dismissButtonStyle).toBeDefined();
  });

  it("renders the dismiss control as a bare interactive glyph", () => {
    // Chrome, not a button: no background or border of its own, but it must
    // still read as clickable.
    expect(dismissButtonStyle.background).toBe("transparent");
    expect(dismissButtonStyle.border).toBe("none");
    expect(dismissButtonStyle.cursor).toBe("pointer");
  });
});
