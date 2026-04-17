# Logging Policy

This document defines the logging policy for Sufni.App.

## Goals

- Provide useful diagnostic logging for development and user support.
- Keep the default log stream readable without requiring Debug or
  Verbose output.
- Capture startup, workflow, and failure information early enough to
  diagnose real issues.

## Non-goals

This policy does not introduce:

- log retention
- rolling files
- runtime log-level switching UI
- a redaction or policy engine
- blanket instrumentation of every class
- broad framework diagnostics beyond targeted forwarding
- automated tests for logging

## Core decisions

- Logging backend: static Serilog root logger.
- Per-class usage: `Log.ForContext<T>()` is the default pattern.
- Session file model: one log file per app launch.
- Session filename format: `LOG-yyyyMMdd-HHmmss.log`.
- Release-support log files must not rely on `Debug` events to be
  understandable.
- `Debug` is for development-oriented diagnostics.
- `Verbose` may appear in release log files and may be filtered out
  when a quieter stream is needed.
- Avalonia framework logging is routed into Serilog and filtered to
  `Warning` and above.
- Logging-related automated tests are not added unless explicitly
  requested later. This includes bootstrap, sink configuration, file
  creation, event emission, and log-level behavior.

## Architecture policy

Logging work must follow the application architecture documented in
`docs/architecture/ui.md`.

- Startup and logger configuration belong to shared app startup plus
  platform entry points, not to view models.
- In app code, `Information` logs should usually originate from shell
  workflows or coordinators, because they own user-visible workflow
  boundaries.
- Services should use `Information` level logs much more sparingly than coordinator for
  the same event. If a service adds value, it should usually do so at
  `Verbose`, `Debug`, `Warning`, or `Error`.
- Libraries may reference Serilog directly to produce events, but they
  do not configure sinks.

## Structured logging policy

- Use structured logging with named properties for diagnostic values.
- Prefer concise message templates and move detail into structured
  properties.
- Avoid string interpolation when a value should remain queryable.
- Avoid duplicate logs across layers. The layer that owns the outcome
  should usually own the `Information`, `Warning`, or `Error` log.
- Lower layers may add `Debug` or `Verbose` context when that context
  materially improves diagnosis.

## Level policy

The default rule is simple: if a log does not help reconstruct the
coarse user story or a real failure path, it does not belong at
`Information`.

Intermediate workflow steps that matter, but would make
`Information` too chatty, belong at `Verbose` rather than at
`Information`.

### Information

`Information` is the default user-support narrative. Reading only
`Information` should tell a coherent, coarse story of what the user did
and what happened.

Use `Information` for:

- the start of a user-visible domain action, except pure navigation and
  UI chrome
- the final outcome of the same action
- open and load actions that materially advance the user's task
- external lifecycle milestones meaningful at app level
- one startup summary line

Default `Information` granularity for multi-step workflows is:

- one log when the workflow starts
- one log when the workflow finishes

Important intermediate milestones may still be logged, but they
should usually be `Verbose` rather than `Information`.

Do not use `Information` for:

- routine service calls
- serializer, mapper, or conversion steps
- inner processing stages
- per-item or per-loop chatter
- polling, browse ticks, refresh scans, repeated probes
- broad framework noise
- pure navigation state changes

Examples that fit `Information`:

- opening the import workflow
- selecting a data store and starting file load
- starting sync
- sync completed
- starting session import
- session import completed with summary counts
- starting bike save
- bike save completed

Examples that do not fit `Information`:

- converted a model to JSON
- executed one SQLite query
- parsed one record inside a loop
- processed one telemetry stage inside a larger workflow
- observed one service-discovery callback

User-entered names and titles are not part of the default
`Information` stream. Prefer IDs, counts, sizes, and timings there.

### Warning

`Warning` is for outcomes that matter and should be visible in normal
logs, but do not meet the `Error` threshold.

Use `Warning` for:

- unexpected but recovered situations
- partial completion
- skipped items that matter to the user story
- fallback or degraded paths
- expected non-exception domain outcomes that should be visible to
  support

This includes:

- optimistic conflict outcomes
- delete blocked because an item is in use
- duplicate or already-open conditions
- no-op actions caused by current state

Do not use `Warning` for invalid user input or missing required user
choice when that input aborts the requested action. Those are `Error`
under this policy.

### Error

`Error` is for failures where the requested action did not complete as
intended.

Use `Error` for:

- handled exceptions that stop the operation
- global unhandled failures
- remote rejections that abort the action, even when they arrive as a
  normal error response
- missing required user choice or invalid user input when that aborts
  the action

Always include the exception object when there is one.

Expected business-rule outcomes are not automatically `Error`.
Conflicts, in-use delete blocks, duplicates, and similar outcomes stay
at `Warning` unless they also satisfy the `Error` rules above.

### Debug

`Debug` is for developer-oriented context, raw diagnostic values, and
branch detail. It is not the default home for workflow-progress logs.

Use `Debug` for:

- counts, sizes, timings, durations, and versions
- IDs, paths, URLs, and other identifiers when diagnostically useful
- branch choices and workflow context
- selected parameters or options
- richer startup detail beyond the single `Information` summary

User content is allowed at `Debug` only when it materially improves the
diagnostic value and a lower-sensitivity alternative is not good
enough.

Do not use `Debug` just because an event is more detailed than
`Information`. If it is still a meaningful workflow milestone, it
usually belongs at `Verbose`.

### Verbose

`Verbose` is for richer operational detail than `Information` without
turning the normal support narrative into noise. It is not limited to
inner loops.

Use `Verbose` for:

- intermediate workflow phases between the `Information` start and
  finish events
- sub-step boundaries that explain where a long or failure-prone
  workflow currently is
- repeated per-item diagnostics when the sequence still matters to
  support
- phase internals that are too noisy for `Information`
- hot-path library tracing

If a log is more detailed than `Information` but still helps explain
how the workflow progressed in support logs, choose `Verbose`. If it
is mostly raw values, parameters, or branch context, choose `Debug`.

## Content policy

Preferred detail types:

- counts
- sizes
- timings
- IDs
- concise file, device, or endpoint identifiers when they materially
  help diagnosis

User content is not forbidden, but it is not the default. Names,
titles, and other user-entered text should appear only when they add
clear diagnostic value, and usually only at `Debug` or `Error`.

Never log:

- refresh tokens
- access tokens
- secret values from secure storage
- raw telemetry blobs
- full request bodies
- full response bodies

Request and response summaries may log method, route, status, duration,
counts, and sizes when helpful.

## Framework logging policy

- Do not run parallel app and framework logging pipelines.
- Avalonia framework logs are forwarded into Serilog.
- Avalonia framework logs are filtered to `Warning` and above.
- Broad framework `Information` logging is out of scope.

## Review checklist for future changes

Every new log addition should survive this review:

1. Is this log emitted by the layer that owns the outcome?
2. Does its level match this policy?
3. If it is `Information`, would removing it make the coarse user
   story materially harder to reconstruct?
4. If it is `Verbose`, is it meaningful intermediate workflow detail
   or genuinely noisy internal detail?
5. Is it duplicating an existing higher-level event?
6. Does it use structured properties where that matters?
7. Does it avoid secrets and low-value user content?

If the answer to question 3 is no, the log should usually move to
`Debug` or lower.

## Manual validation policy

Logging is validated manually, not through automated tests.

Minimum manual validation for the initial implementation:

1. Launch the app and confirm a new log file is created.
2. Confirm the log file lands in the `logs/` directory beside the
   database.
3. Confirm startup metadata is written once.
4. Perform a few common user actions and verify that the
   `Information` stream tells a coherent story and `Verbose` adds
   useful intermediate progress without becoming noise.
5. Trigger a recoverable issue and verify that a `Warning` entry is
   written.
6. Trigger a handled failure and verify that an `Error` entry includes
   the exception when one exists.
7. Confirm the desktop Welcome screen action opens the expected logs
   directory.
8. Confirm Debug builds include `Debug` events and Release builds do
   not.

## Deferred items

These remain out of scope until explicitly approved:

- DI-based `ILogger<T>` integration
- log retention and pruning
- rolling files
- runtime log-level switching UI
- broader framework diagnostics
- automated logging tests
