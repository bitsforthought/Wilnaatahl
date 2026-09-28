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
  }
);
