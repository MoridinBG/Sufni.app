# View Testing Guide

## Purpose

This guide defines how view tests in `Sufni.App.Tests` should be written.
The goal is reliable regression protection around externally visible view behavior,
especially for Avalonia views running under headless tests.

[TESTING.md](TESTING.md) defines the repository-wide testing standard.
This document narrows that standard for views specifically.

## Table Of Contents

- [Purpose](#purpose)
- [Core Standard](#core-standard)
- [What View Tests Own](#what-view-tests-own)
- [Choosing The Right Unit](#choosing-the-right-unit)
- [Headless Baseline](#headless-baseline)
- [Resources, Styles, And DataTemplates](#resources-styles-and-datatemplates)
- [Data Contexts And Fixtures](#data-contexts-and-fixtures)
- [Control Discovery](#control-discovery)
- [What To Assert](#what-to-assert)
- [Interaction Strategy](#interaction-strategy)
- [Async And Lifecycle Behavior](#async-and-lifecycle-behavior)
- [Platform Variants](#platform-variants)
- [Headless Pitfalls](#headless-pitfalls)
- [Patterns To Avoid](#patterns-to-avoid)
- [Naming](#naming)
- [Practical Workflow](#practical-workflow)
- [Default Checklist](#default-checklist)

## Core Standard

- A view test targets one view or one reusable subview.
- A view test exercises the view through its mounted public surface: rendered controls, bindings, commands, visual or logical presence, and visible state.
- Assertions should be about behavior a user could observe through the UI surface: displayed values, selected items, visible or hidden content, enabled or disabled affordances, command wiring, and content-template resolution.
- Do not target private helper methods, internal XAML layout mechanics, or implementation-specific control nesting unless that structure is itself the contract being tested.
- Keep one primary reason for failure per test.

In this repository, the SUT is often one reusable control, one editor view, one desktop or mobile variant, or another view with real binding behavior.

## What View Tests Own

The SUT is the view's externally visible binding behavior.

View tests should cover:

- bound text, numeric values, selected items, and other rendered values
- enabled and disabled state when that state is part of the visible contract
- visibility and content switching driven by bound state
- command wiring and control interactions that belong to the view surface
- resource, style, and template wiring required for the view to render in headless tests
- desktop versus mobile parity when the XAML differs
- structural composition when the view chooses between reusable subviews or templates

View tests should not cover:

- business workflow logic that belongs to the view model or coordinator
- persistence, parsing, or service behavior
- pixel-perfect styling details unless there is a concrete regression risk
- internal sequencing that is not observable from the mounted view

## Choosing The Right Unit

Prefer the smallest view unit that owns the behavior.

- Reusable subviews should usually get isolated tests with a lightweight data context.
- Composed views should test their own bindings, structure, and content switching without re-testing child controls that already have their own coverage.
- When desktop and mobile views differ in XAML, test both relevant variants and focus on the behavioral difference the user can observe.

This keeps failures local and avoids duplicating the same assertions across layers.

## Headless Baseline

- Use `Avalonia.Headless` for tests that touch real views, Avalonia controls, UI-thread-bound state, or templated content.
- Prefer `[AvaloniaFact]` for mounted view tests.
- Mount the view inside a real `Window`, call `Show()`, and flush the dispatcher before asserting.
- Close the host window and flush the dispatcher again during cleanup.

This repository's `TestApp` does not load `App.axaml` by default.
That means view tests must provide any app-level resources, styles, or templates that the view depends on.

## Resources, Styles, And DataTemplates

Headless failures are often setup failures rather than product bugs.
Before writing assertions, make the rendering environment match what the view expects.

- Seed required app resources into `Application.Current.Resources` before mounting the view.
- Register required typed templates where production code resolves them, usually `Application.Current.DataTemplates` or a control-local `DataTemplates` collection.
- If the view depends on a style file that `TestApp` does not load, register that style explicitly in the test.
- Keep resource and template setup idempotent so multiple test classes can reuse the same helper safely.

If a view fails to render because a `Sufni*` resource or a typed template is missing, fix the test environment first.

## Data Contexts And Fixtures

Use the simplest real data context that can drive the behavior under test.

- For reusable control tests, prefer a tiny stub or test view model over a full feature view model.
- For composed views, use the real view model when the view depends on real binding behavior, real commands, or view-model-managed content switching.
- Substitute constructor collaborators that are not part of the view contract.
- Do not over-mock framework-driven data sources. If a view model expects a live `ObservableCollection`, DynamicData stream, or other changing source, back it with a real collection or cache so the binding pipeline behaves as it does in production.
- When several tests need the same setup, create a small local harness first. Promote it into `Sufni.App.Tests/Infrastructure/` only when the pattern is reused across multiple test classes.

The fixture should make the view easy to mount and the assertions easy to read. It should not hide the behavior being tested.

## Control Discovery

Prefer stable names over fragile traversal.

- Add `x:Name` to controls that tests need to inspect or drive.
- Use `FindControl<T>("Name")` for direct interaction with specific controls.
- Use visual or logical tree traversal only for structural assertions such as presence or absence of a subview, or when testing templated content that has no stable named root.
- Avoid locating controls by type plus position when a name would make the test more explicit.

Named controls make tests resilient to layout changes and remove ambiguity when a view contains multiple controls of the same type.

## What To Assert

Assert on behavior the mounted view actually exposes.

Good assertion surfaces include:

- a `TextBox.Text`, `NumericUpDown.Value`, or `ComboBox.SelectedItem` reflecting bound state
- a collection control showing the expected items from a bound source
- a `ContentControl` rendering the correct subview when content is present and remaining empty when content is `null`
- a command-bound button exposing the expected `Command` and `CommandParameter`
- a reusable subview appearing in one variant and not another when that difference is intentional
- view visibility changing when the bound error, loading, or empty state changes

Prefer expected and plausible unexpected cases in pairs, for example:

- a view renders the typed content view when the bound content exists, and renders nothing when the bound content is `null`
- a selector shows the bound selected item when the source is populated, and clears selection when the underlying bound value is missing
- a desktop variant includes an inline action while the mobile variant intentionally delegates that action to a shared bottom button line

## Interaction Strategy

Choose the assertion surface that is most reliable for the behavior you are testing.

- If the SUT is code-behind handling on the control itself, raising the actual Avalonia event is appropriate.
- If the SUT is command binding, first assert that the correct command is bound. Then execute that command through the bound button or command object if synthetic click events are unreliable in headless mode.
- After changing bound state, selected values, or command availability, flush the dispatcher before asserting the resulting UI state.
- When testing an intermediate busy or loading state, hold the async work pending with `TaskCompletionSource<T>` and assert the mounted view while the work is still active.

The point is to prove the public view contract, not to insist on one interaction mechanism when headless Avalonia exposes a more stable one.

## Async And Lifecycle Behavior

Mounted view tests often need explicit dispatcher control.

- Flush the dispatcher after showing the host window.
- Flush after property changes that should update bindings.
- Flush after `NotifyCanExecuteChanged()` or other command-state changes.
- Flush after opening or closing hosted content if your assertion depends on layout or template application.

Do not assume a host-window close will always reproduce every lifecycle callback exactly as a live app would.
If unloaded behavior is the contract under test, verify that the specific lifecycle path is actually exercised in headless mode before depending on it.

## Platform Variants

When a feature has separate desktop and mobile views, treat the XAML split as part of the public surface.

- Cover both variants when the structure differs.
- Assert the behavior that differs, not just that the files are different.
- Prefer one or two focused tests per divergence: different shared subview usage, different button placement, different template content, or different content composition.

The value is in catching regressions where one variant drifts from the intended UI contract.

## Headless Pitfalls

The following patterns are easy to get wrong in Avalonia headless tests:

- Do not assume `Button.IsEnabled` is always the most reliable assertion for command availability in headless tests. If the contract is command wiring or `CanExecute`, asserting on `Button.Command` and `Command.CanExecute(...)` is often more stable unless `IsEnabled` propagation has been verified for that control.
- Do not assume synthetic `RaiseEvent(Button.ClickEvent)` will always execute command-bound buttons the same way it executes code-behind handlers. Use it for real control event handling, not as the only proof of command binding.
- Do not assume flyouts, popups, ComboBox dropdowns, or selected-item presenters will be realized in the same way they are in a full app host. When necessary, assert the bound template or content structure directly, or render the real template inside the test tree.
- Do not rely on detached flyout content having a usable parent namescope. If named lookup fails there, inspect the content tree directly.
- Do not mistake missing resources or templates for product regressions. Headless tests often fail because the app-level setup was not reproduced.

These are test-environment constraints. Design the test around them rather than fighting them.

**NEVER** make design concessions in the actual app code to accommodate test limitations!

## Patterns To Avoid

- Re-testing view model logic that already belongs in a view-model test.
- Asserting exact visual-tree shape when the user-visible contract is simpler.
- Looking up controls by index or incidental nesting.
- Over-mocking collections, templates, or other framework behavior that should be exercised for real.
- Opening popups or dropdowns just to prove structure that can be verified through the bound template more directly.
- Large tests that validate several unrelated bindings or interactions at once.

## Naming

Test names should state the mounted view behavior and the condition that drives it.

Preferred style:

- `SetupEditorView_ForkSensorConfigContentControl_Empty_WhenNull`
- `EditableTitle_EditButton_TogglesEditingState`
- `ImportSessionsView_DisablesEditors_WhileImportRuns`

Avoid vague names such as:

- `ViewWorks`
- `TestButtons`
- `HandlesCase1`

## Practical Workflow

When adding or changing a view:

1. Decide whether the behavior belongs to the view or to the view model.
2. Add stable control names if the test will need direct access.
3. Reuse or add small helpers for mounting, dispatcher flushes, resources, templates, and simple view-model stubs.
4. Test reusable subviews in isolation first when they have their own behavior.
5. Test composed views for their own bindings, platform-specific structure, and content switching.
6. Run the focused headless tests while iterating.
7. Run the affected test project before considering the change complete.

## Default Checklist

Before finishing a view change, ask:

- Did I test one mounted view through its public surface?
- Did I cover the expected state and a plausible unexpected state where that adds value?
- Did I provide the resources, styles, and templates the view needs in headless mode?
- Did I prefer named controls over fragile tree traversal?
- Did I flush the dispatcher after the interactions that matter?
- Did I assert command or binding behavior at the most reliable headless surface?
- Did I cover both platform variants if the XAML differs?
- Did I avoid duplicating view-model logic or styling details that are not the view test's responsibility?

If the answer is yes across that set, the test is usually shaped correctly for this repository.
