# Testing Guide

## Purpose

This guide defines how tests in `Sufni.App.Tests` should be written.
The goal is reliable regression protection around meaningful behavior, not blind coverage growth.

## Core Standard

- A test targets one unit under test.
- A unit test exercises that unit through its public interface.
- Assertions should be about externally visible behavior: returned results, public state, public commands, published notifications, and observable side effects on collaborators.
- Do not target private helpers, internal sequencing, or implementation details directly. They can still inform which public-interface cases should be covered.
- Keep one primary reason for failure per test.

In this repository, that means the SUT is usually one view model, one coordinator, one service, one view, or one utility with real logic.

## Input Selection

Tests should use two kinds of inputs:

- expected or known inputs that represent valid, normal use of the SUT
- plausible unexpected inputs that could happen in real use

Plausible unexpected inputs are things like:

- `null` or missing optional data
- invalid files or malformed serialized content
- stale versions during optimistic concurrency
- canceled workflows
- empty collections
- superseded background completions

Do not bombard the SUT with random junk just to create more cases. Prefer inputs that can realistically occur in production and that can expose a meaningful branch or regression.

## Stack And Defaults

- Use xUnit.
- Use NSubstitute for collaborators.
- Use `Avalonia.Headless` when the code touches Avalonia objects, UI-thread-bound state, or actual views.
- Prefer `Fact` for non-UI tests and `AvaloniaFact` only when it is actually required.

## What To Assert

- Assert on result kinds, state transitions, store writes, command behavior, and other observable effects of the public interface.
- Do not assert on exception text or user-facing message wording.
- Do not assert on implementation-specific call ordering unless call order is itself part of the contract.
- Avoid tests that merely restate a property assignment or trivial record construction.

## Test Doubles And Fixtures

- Do not construct real runtime dependencies inside a unit test unless the dependency is a dedicated test fixture.
- Collaborators passed into the SUT constructor should be substitutes or explicit test fixtures.
- Prefer small shared fixtures and helper objects when they make tests easier to read.
- Small local adapters are acceptable when they are specific to one test class, but repeated helper patterns should be promoted into shared test infrastructure instead of being copy-pasted.

## Shared Helpers

- Check `Sufni.App.Tests/Infrastructure/` before creating a new local helper.
- Reuse shared builders, snapshot factories, app/test-environment helpers, and synchronization helpers when they fit the test.
- If the same helper pattern starts appearing in multiple test classes, move it into shared test infrastructure.
- Keep shared helpers small and mechanical. They should reduce repetition, not hide the behavior the test is asserting.

## Async Test Control

- Use `TestSynchronizationContextScope` when the SUT or one of its collaborators posts callbacks or continuations through `SynchronizationContext` and the test needs those callbacks to run deterministically before assertions.
- Use `TaskCompletionSource<T>` when the test needs to hold async work pending so it can assert intermediate states such as busy, cancellation, unload behavior, or superseded results before completion.
- Prefer explicit control over async completion to timing-based waits or delays.
- When the SUT exposes a safe override seam and the test needs lightweight observability, a small tracking subclass is preferable to reflection.

## Desktop And Mobile Branches

- When behavior branches on desktop versus mobile mode, cover both relevant branches.
- Use `TestApp.SetIsDesktop(true)` and `TestApp.SetIsDesktop(false)` to select the platform mode in headless tests.
- Keep the assertion focused on the behavioral difference caused by the platform branch, not on the toggle itself.

## Layer Ownership

### View Model Tests

The SUT is the view model's public surface.

They should cover:

- the state the view model exposes to the screen
- derived state and state transitions it is responsible for
- command availability and command outcomes
- validation, notifications, and other screen-facing status it owns
- observable interaction with collaborators
- lifecycle, refresh, and data-loading behavior when the view model owns it
- collection, selection, and state-coherence behavior when those are part of its responsibility
- cancellation, background completion, and stale-result behavior when applicable

They should not cover:

- infrastructure file parsing
- database persistence details
- visual layout or control-template behavior already owned by a view test

### Coordinator Tests

The SUT is the coordinator's workflow contract.

They should cover:

- branching between `Saved`, `Conflict`, `Failed`, `Canceled`, and similar result shapes
- database calls and store writes
- shell navigation or open/focus behavior
- orchestration between services, stores, and dialogs
- progress-event emission and consumption when the workflow owns it
- background-runner usage when the coordinator owns the off-thread work

They should not cover:

- internal view model property updates
- infrastructure parsing details already owned by a service test

### Service Tests

The SUT is the service method contract, not the libraries it calls.

They should cover:

- file import and export behavior
- serialization and deserialization boundaries
- bitmap or stream decoding behavior
- duplicate detection or one-shot discovery logic
- cancellation propagation when the service owns background work
- translation from low-level exceptions into domain result shapes when that translation is part of the contract

They should not cover:

- trivial pass-through wrappers with no branching or transformation

### View Tests

The SUT is the view's externally visible binding behavior.

They should cover:

- enabled and disabled state
- visibility and content switching driven by bound state
- desktop versus mobile view parity when the XAML differs
- resource requirements and headless wiring when a view depends on them

They should not cover:

- complex workflow logic that is already better expressed in a view model test
- pixel-perfect styling details unless there is a concrete regression risk

### Model And Utility Tests

Model and utility tests are appropriate only when the unit has real logic.

Good candidates:

- custom serialization or deserialization
- parsing and normalization
- calculation helpers with non-obvious edge cases

Poor candidates:

- plain records
- simple result unions with no behavior
- getters and setters with no logic

## Choosing What To Test

The default expectation is that meaningful behavior is covered.
The target is high coverage of the unit's real behavior.
The reason to skip a test should be that the code is genuinely trivial, not that the failure seems unimportant.

Prioritize coverage for:

- normal successful behavior of the public interface
- failure and error behavior of the public interface
- validation, conflict, cancellation, and recovery branches where they exist
- cases that can leave stale, partial, or incoherent state behind
- behavior that is complex enough that inspection alone is not a strong guarantee
- areas that have regressed before or are easy to regress

Code that usually does not need direct tests includes:

- simple assignments
- constants
- trivial getters and setters
- obvious pass-through code with no branching, translation, or meaningful state change

## Optimistic Concurrency

Editor and coordinator tests should make the optimistic-concurrency mechanism explicit.

- Editors carry `BaselineUpdated`, initialized from the loaded snapshot's `Updated` value.
- Coordinators compare that baseline against the current stored snapshot version on save.
- If the values diverge, the save should be rejected with the appropriate conflict path instead of silently overwriting newer data.
- Tests should cover both the matching-version case and the stale-version case.

This mechanism is shared across editor save flows and should be treated as a standard contract, not an incidental implementation detail.

## Patterns To Avoid

- Over-mocking where plain input data would be clearer.
- Assertions that duplicate the implementation line-by-line.
- Artificial tests that exist only to reach a private helper path instead of exercising a real public behavior.
- Large multi-assertion tests that validate several unrelated behaviors at once.
- Hard-coding fragile message text or exception text.

## Cancellation And Result Coherence

This repo has enough background and replaceable workflows that cancellation behavior is part of the contract.

Tests should cover:

- canceled work not surfacing as a failure
- stale completions not overwriting newer state
- busy flags clearing only for the active workflow
- accepted conflict reloads or resets triggering the correct fresh work

If a workflow can be superseded, at least one test should prove that an older completion cannot win after newer work has started.

## Known And Unexpected Cases

For each meaningful public behavior, think in pairs:

- the expected case the unit is meant to handle
- the plausible unexpected case that the unit still needs to handle correctly

Examples:

- import succeeds for valid bike JSON, and returns `InvalidFile` for malformed JSON
- save succeeds with the current version, and returns `Conflict` for a stale version
- analysis returns computed data for a valid linkage, and `Unavailable` for missing linkage
- a background workflow applies its own result, and an older superseded workflow does not overwrite newer state

This gives good branch coverage without degenerating into random-input testing.

## Naming

Test names should describe setup and expected behavior.

Preferred style:

- `SaveAsync_ReturnsConflict_WhenUpdatedVersionChanged`
- `RemovingJoint_DetachesPropertyHandler_FromRemovedInstance`
- `ImportSessionsView_DisablesEditors_WhileImportRuns`

Avoid vague names such as:

- `TestSave`
- `EditorWorks`
- `HandlesCase1`

## Practical Workflow

When changing production code:

1. Decide which single unit owns the behavior.
2. Add or update tests in that layer first when practical.
3. Add cross-layer tests only when there is clear value, not as routine duplication.
4. Run focused tests while iterating.
5. Run the affected test project before considering the change complete.

## Default Checklist

Before finishing a change, ask:

- Did I test one unit through its public interface?
- Did I cover the expected case and a plausible unexpected case where that adds value?
- Did I cover the failure or conflict branch if it is meaningful?
- Did I cover cancellation or stale-result behavior if background work is involved?
- Did I avoid asserting on wording that the UI may legitimately change later?
- Did I keep the test data and setup readable?

If the answer is yes across that set, the test is usually shaped correctly for this repository.
