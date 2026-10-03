# GitHub Actions

The repository should use GitHub Actions to run the same FAKE validation graph
used during local development.

The workflow should:

- install the Node.js version recorded in `.nvmrc`;
- install the .NET SDK selected by `global.json`;
- run `npm run init "--" --ci`; and
- run `npm run validate "--" --ci`.

The workflow must not duplicate individual build, test, lint, or coverage steps
already represented by the FAKE target graph.
