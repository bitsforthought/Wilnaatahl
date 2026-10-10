import js from "@eslint/js";
import { defineConfig } from "eslint/config";
import tseslint from "typescript-eslint";

export default defineConfig(
  {
    ignores: ["src/generated/**", "tests/Wilnaatahl.ECS.Tests/out/**", "dist/**"],
  },
  {
    files: ["src/**/*.{ts,tsx}", "tests/typescript/**/*.{ts,tsx}", "{vite,vitest}.config.ts"],
    extends: [js.configs.recommended, tseslint.configs.recommendedTypeChecked],
    languageOptions: {
      parserOptions: {
        projectService: { allowDefaultProject: ["vite.config.ts"] },
        tsconfigRootDir: import.meta.dirname,
      },
    },
    rules: {
      "no-restricted-imports": [
        "error",
        {
          patterns: [
            {
              regex: "fable_modules/fable-library-ts\\.",
              message:
                "Import the Fable runtime from src/generated/fable-library/ so that Fable upgrades don't break the path.",
            },
          ],
        },
      ],
    },
  }
);
