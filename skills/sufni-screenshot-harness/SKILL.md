---
name: sufni-screenshot-harness
description: Use for Sufni.App UI work when building, adjusting, or reviewing Avalonia views and visual layout. Requires using the Sufni.Screenshots harness with temporary or focused screenshot scenarios, running screenshot tests, and inspecting generated PNGs before calling UI changes verified.
---

# Sufni Screenshot Harness

Use this skill for Sufni.App UI changes where visual correctness matters: spacing, alignment, tab content, graph surfaces, view composition, responsive desktop/mobile variants, or any change the user asks to verify with screenshots.

## Workflow

1. Read the relevant production XAML/code first. Do not guess control names, paths, or binding shape.
2. Read `docs/SCREENSHOTS.md` if you need harness details.
3. Add or update a focused screenshot scenario in `Sufni.Screenshots/`.
   - Prefer a temporary scenario class for verification-only work.
   - Keep the scenario narrow: one view, one state, one output PNG per visual claim.
   - Mount a real view in an Avalonia `Window`, call `Show()`, flush the dispatcher twice, capture with `CaptureRenderedFrame()`, save under `screenshots/<scenario>/`.
4. Run screenshot tests serially. Do not run screenshot builds in parallel with other `dotnet test` commands; shared build outputs can lock `Sufni.App.pdb`.
5. Inspect the generated PNG. Use image viewing if available; otherwise report the exact PNG path and use measurable layout checks where possible.
6. If the screenshot scenario is only a temporary verification aid, remove it before finishing unless the user asked to keep it as reusable coverage.
7. Run `git diff --check`. Run focused headless view tests when the change touches existing view contracts.

## Commands

Run the focused scenario:

```bash
dotnet test Sufni.Screenshots --filter <ScenarioClassOrStepName>
```

Run the whole screenshot project:

```bash
dotnet test Sufni.Screenshots
```

Generated PNGs land under:

```text
Sufni.Screenshots/bin/Debug/net10.0/screenshots/<scenario>/
```

## Scenario Rules

- Keep generated PNG files out of the repo unless the user explicitly asks to store them.
- Prefer deterministic fixture data created in the scenario or small local helpers.
- Use production views and view models when practical; use a minimal stub only when the view binds to an interface.
- Match the target viewport to the user’s issue. For desktop statistics/graphs, use a wide desktop window so tab content and graph alignment are visible.
- Inspect the actual visual target, not just successful test output. A passing screenshot test only proves the render completed.

## Finish Criteria

Before claiming a UI fix is verified, state:

- screenshot command run and result;
- generated PNG path inspected;
- what the screenshot showed about the requested visual behavior;
- any focused view tests or `git diff --check` run.
