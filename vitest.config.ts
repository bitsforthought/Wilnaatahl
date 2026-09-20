// Vitest configuration for the TypeScript unit tests.
//
// Deliberately separate from vite.config.ts so the app build carries no test
// configuration. The `react` plugin is mirrored from the app config so tests can
// import components should that ever be needed.
//
// The coverage denominator is `src/**/*.ts` only: `src/generated/**` is Fable
// output (never hand-edited, and its logic is already covered by the F# suite),
// and `.tsx` files are excluded because they are required to hold no logic —
// see the "no logic in .tsx" convention in AGENTS.md. Setting `coverage.include`
// is what makes untested files count against the denominator; Vitest has no
// `coverage.all` option.

import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  test: {
    // Most suites are pure logic or Three.js/Koota object manipulation, neither
    // of which needs a DOM. The few that do opt in per-file with a
    // `// @vitest-environment jsdom` docblock.
    environment: "node",
    include: ["tests/typescript/**/*.test.ts"],
    coverage: {
      provider: "v8",
      reportsDirectory: "coveragereport-ts",
      // Cobertura so the same ReportGenerator step the F# suite uses can turn it
      // into a Summary.json of identical shape, letting CheckCoverage.fsx read
      // both suites through one parser.
      reporter: ["text", "cobertura"],
      include: ["src/**/*.ts"],
      exclude: ["src/generated/**", "src/**/*.tsx", "src/vite-env.d.ts"],
      // The suite imports Fable-generated TypeScript, which carries source maps
      // back to its own inputs. Without this, excluded files reappear in the
      // report after remapping.
      excludeAfterRemap: true,
      reportOnFailure: true,
    },
  },
});
