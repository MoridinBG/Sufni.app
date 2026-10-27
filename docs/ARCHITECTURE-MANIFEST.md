# Architecture Manifest
## Reactive Composition, Imperative Workflows

An application changes while it runs.

Devices appear and disappear. Recordings arrive from a device, from a file, or from a peer. Users author names, associations, calibrations, and options. Derived results go stale when any of their inputs change. User actions begin workflows that take seconds, span restarts, or finish on another device.

Treat state as the enemy. Hold as little of it as possible. Observe and derive everything that can be observed or derived. Give every fact and every decision one home, and reference that home from everywhere else; never copy it. Keep bookkeeping to the minimum the current requirements need ([§22](#22-stay-minimal-by-default)).

Then admit what remains. The application still has state. Users have intentions. Workflows go through steps and carry something between them. Give that remainder explicit owners, lifetimes, and recovery, so that cause and effect stay visible, no fact has two authorities, and the presentation layer does not become the application.

### The central idea

The application has two complementary modes:

- **Reactive composition** maintains a coherent, observable view of the system from owned facts.
- **Imperative workflows** perform ordered work and publish their results back into that view as new facts.

Neither replaces the other.

```text
facts ──> store ──> query ──> view model ──> view
            ▲                                 │
            │ new facts                       │ intent
            │                                 ▼
        services <──────────── coordinator's workflow
 (device, file, persistence, peer)
```

Facts flow outward to consumers. Intent flows inward to the one owner that can act. The effects of that work return as new facts through the same stores.

Five roles carry this. They are semantic roles, not layers every feature must have.

- **Store** — the one owner of one family of facts. It holds the current snapshots, publishes changes, and answers direct lookups over its own facts. Only coordinators write it.
- **Coordinator** — the owner and single writer of the workflows that change facts. It sequences phases, calls services, folds their results into stores, and translates the outcome for the caller.
- **Query** — a derivation over facts owned elsewhere. It composes, filters, and classifies. It is recomputed from its inputs and is never a second home for them.
- **View model** — the projection of state for one surface and the entry point for intent from that surface. It owns view state only.
- **Service** — the boundary to the world: a device, the file system, the persistence engine, a peer, a clock. It translates wire and storage shapes into domain operations and holds no domain facts. Repositories are persistence services.

Most features need a store, a coordinator, a view model, and the services they call. Add a query when one meaning is composed from more than one store. Add anything else only under [§22](#22-stay-minimal-by-default).

## Commitments

### 1. Give every fact one owner

Each meaningful piece of state has one authoritative owner.

Consumers observe, transform, filter, or combine that state. They do not keep competing mutable copies. When device presence, pairing, configuration, and stored data together decide whether an action is available, derive availability from those facts. Do not synchronize it through independent flags.

When state is wrong, you must be able to answer:

- Who owns it?
- Who may change it?
- What inputs determine it?
- What invalidates it?
- How long does it live?

If any question has several answers, the boundary is not yet clear.

### 2. Derive, do not synchronize

Recompute dependent state from its inputs.

When a device connects, a peer arrives, a configuration changes, or a selection moves, recompute the derived values and replace the objects that were built for the old inputs ([§10](#10-compose-along-lifetimes)). Do not walk the application imperatively, locating every object that might need mutation.

Consumers observe the meaning they need. They do not independently reconcile the underlying signals.

A single meaningful state such as "current", "stale", "unavailable", or "recovering" is usually composed from several lower-level facts. Put that composition in one named query, not in every screen.

### 3. Materialize a derivation only when its cost demands it

Some derivations are too expensive to recompute on every read. Processing a recording into analysis-ready results is the typical case. Such a result may be persisted. It remains a derivation.

A materialized derivation:

- carries the fingerprint of every input that produced it: the source content, the configuration, the algorithm version, the options;
- is current when its fingerprint equals the fingerprint of the current inputs, and stale otherwise. Staleness is observed by comparing fingerprints. It is never a flag that some other code must remember to set;
- is replaceable at any time by recomputing from its inputs. Nothing treats it as the source of truth;
- may travel with the recording it belongs to, so that a device receiving the recording also receives the result. On arrival it is still only a materialization: verify its fingerprint against the local inputs and treat it as stale when they differ.

Keep the derivation itself pure. Give it inputs and get a result and a fingerprint back. Persistence, publication, and cancellation belong to the coordinator that requested the work.

### 4. Do not turn reactive composition into a workflow engine

A dependency changing may invalidate or recompute state. It must not silently encode a multi-step business process.

Ordered operations — import, reprocess, synchronize, pair, capture, migrate, delete with cascades — are explicit imperative workflows with a clear entry point, ordered phases, a cancellation policy, failure behavior, and a final outcome. They live in coordinators.

Reactive composition communicates a workflow's state and propagates its consequences. It does not replace the workflow. Do not chain observers so that one store's change starts work that writes another store, which starts more work. That is a workflow written in a language nobody can read.

### 5. Let views render state and express intent

Presentation is a projection of application state and an input surface for user intent.

A view model may own genuinely visual and ephemeral concerns: focus, selection, zoom, an expanded group, an open menu, the text in a field before it is committed. It does not own business decisions, persistence, device or peer communication, or any work that remains meaningful after the surface disappears.

A button says "begin this intent". It does not contain the operation. The view model forwards the intent to the coordinator that owns it and renders whatever state comes back through the stores.

Navigation and global feedback may react to application state, but those effects are owned by the application shell ([§17](#17-keep-application-wide-concerns-in-the-shell)), not scattered through individual views.

### 6. Give every workflow one writer

A multi-step operation has one component responsible for moving it between phases.

Services, workers, and transports report events or results. They do not independently mutate the workflow record or the stores it affects. One coordinator folds those observations into the authoritative state. It persists a transition only when the workflow must survive a restart ([§15](#15-expect-interruption-in-durable-workflows)).

This single-writer rule matters most when a workflow:

- writes more than one family of facts;
- can be paused, retried, superseded, or cancelled;
- must survive an application restart;
- reports progress;
- compensates after partial failure;
- reconciles desired state against the observed state of a device or a peer.

There is no atomic transaction across a device, the local database, and a peer. Assign authority, ordering, compensation, and reconciliation explicitly.

### 7. Model state by domain meaning

Keep the distinctions that matter to users or to subsequent behavior. Omit the distinctions that are only implementation detail.

Alternatives that exclude each other are one enum or one typed outcome, not several Booleans. A case exists only when it permits different actions or requires different recovery. A phase that only describes the order of statements is not state.

"Busy" is insufficient when waiting for a device and transferring from it permit different actions. It is sufficient when they do not.

Previous data may remain visible while it is being refreshed, as long as it is still valid. Clear it when its scope or authority has changed.

### 8. Classify state by origin, authority, and lifetime

Not all state is the same. Name which kind you are holding before you decide where it lives.

- **Captured facts** — recordings and other data produced by a device or a capture. Immutable once written. Identified by content, not by where they are stored.
- **Authored facts** — names, descriptions, associations, calibrations, options, and other data a user writes. Mutable, with possibly a record of when they last changed.
- **Materialized derivations** — expensive results persisted with the fingerprint of their inputs ([§3](#3-materialize-a-derivation-only-when-its-cost-demands-it)).
- **Replicated facts** — copies of captured or authored facts that arrived from a peer. They enter through a merge rule ([§9](#9-assign-authority-per-kind-of-fact)) and are then ordinary local facts.
- **Presence** — which devices and peers are reachable right now and what they report about themselves. Observed, never persisted. Gone when the observer stops.
- **Workflow checkpoints** — the minimum needed to classify and resume incomplete durable work after a restart.
- **Device-local preferences** — settings that belong to this installation and never travel.
- **View state** — exists only to support the current presentation.

Local data is not authoritative because it is nearby. Data from a peer is not current because the peer is a desktop. Every cache and every replica has an explicit freshness, validity, and reconciliation policy.

Select storage by meaning — sensitivity, durability, structure, size, observability, recovery — not by convenience.

### 9. Assign authority per kind of fact

Devices run disconnected and change independently. No device is authoritative by position. Authority follows the kind of fact.

- A **captured fact** is authoritative wherever it exists. Replicate it verbatim and verify it by content. Never merge it, edit it in place, or regenerate it.
- An **authored fact** has one merge rule, applied identically on every device. The rule decides from the fact's own history — its timestamps and its tombstone — never from which device is applying it. A deletion is not undone by older content. The rule lives in one place.
- A **materialized derivation** is authoritative nowhere. Accept it when its fingerprint matches; recompute it when it does not.
- **Presence** is authoritative only on the device that observes it, and only while it is observed.

Transport is not authority. A device that relays or hosts the exchange applies the same rules as every other participant.

### 10. Compose along lifetimes

The running application is shaped by what currently exists: the installation, a connected device, a paired peer, an open editor, a running stream, a selected recording.

Give each of these a named lifetime. Objects built for one lifetime live exactly as long as it does. When the lifetime ends, dispose the dependent object graph and build a new one for the new inputs. Do not mutate a client configured for one device so that it talks to another. Do not keep a stream alive because a view once asked for it.

Work that must outlive a surface is retained deliberately by an owner with a longer lifetime. Everything else is releasable when no longer observed or owned.

### 11. Reach the world through services at domain boundaries

Devices, files, the persistence engine, and peers are reached through services. Wire formats, protocols, file layouts, and storage schemas stay inside the service that talks to them.

A service translates between the world and domain operations. It does not decide what the application should do with the answer. That decision belongs to the coordinator that called it.

The structures a device or a peer transmits are not the application's domain model because a parser produced them. Translate them at the boundary.

Roles are semantic, not mandatory. A simple operation stays simple. Add a service or a coordinator only when it establishes ownership no existing owner can hold, protects a real boundary, or makes behavior independently testable.

### 12. Give every side effect a lifecycle owner

Subscriptions, timers, listeners, workers, servers, discovery browsers, and background jobs are resources.

The component that starts one owns its lifetime, cancellation, and cleanup. Effects triggered by observed state — navigation, notifications, starting a server, opening a stream — belong at deliberate boundaries with lifetimes broader than one screen. An existing owner with that lifetime is the usual home. A new one needs the justification in [§22](#22-stay-minimal-by-default).

Coordinate shutdown. Active work may delay or reject termination, and cleanup occurs in a defined order. Disconnecting a device or unpairing a peer is a workflow, not a side effect of a view closing.

### 13. Isolate long-running work deliberately

Use concurrency where work would otherwise compete with responsiveness or needs an independent lifecycle: processing a recording, sustained device I/O, a continuous stream, a transfer, a server.

Workers receive immutable job descriptions and communicate through typed commands, events, progress, and results. Their owner holds the public state and the worker's lifetime. Cancellation begins cooperatively and escalates only when the worker fails to settle.

Isolation is not the default for every asynchronous call. Most asynchronous calls are ordinary awaited methods on their owner. This section applies to work that is substantial, sustained, or independently controlled.

### 14. Keep the UI thread for the UI

The UI thread renders and reacts to input. Nothing else runs on it.

Computation, parsing, transfers, and persistence run off the UI thread and receive immutable inputs. Results come back as immutable values. The owner publishes them to its store on the UI thread, in one step, so that consumers see a coherent snapshot and never a half-applied change.

Do not share mutable state across threads. Pass values. Reject a stale result by comparing its generation or fingerprint against the current inputs, not by locking around the consumer.

Batch publications that belong together. A set of related changes arriving in one moment produces one coherent update, not a cascade of partial states.

### 15. Expect interruption in durable workflows

A process can end at any point. A device can be unplugged between any two steps. A peer can disappear in the middle of a transfer.

A workflow that spans an external effect and a local commit — moving a file on a device and recording its import, receiving a payload and acknowledging it, reprocessing every recording, migrating stored data — persists enough to classify its state after a restart. Recovery does not blindly replay the whole operation. It determines which work completed, which phase is resumable, which step needs reconciliation against the device or the peer, and which outcome needs the user.

Work that lives only in memory, such as a capture in progress or an editor's uncommitted edits, ends with the process. Say so in its state model. Do not persist workflow state for it.

Persisted schemas outlive releases. Version, migrate, and validate them at startup, before any owner reads them.

### 16. Model failure as state

Interpret errors at the lowest boundary that can add meaningful context, and log them there once with their cause.

Higher layers translate failures into domain state, typed outcomes, or actionable feedback. They do not log the same event again or leak transport, parser, or framework exceptions into presentation.

Distinguish:

- expected unavailability (no device present, peer not reachable, source not yet received) from exceptional failure;
- recoverable failure (retry, reconnect, recompute) from terminal failure (a corrupt capture, a missing dependency the user must restore);
- operation failure from structural application failure;
- diagnostic detail from safe, actionable user language.

A user-initiated action ends in an outcome the user can understand and, where possible, act on. A result superseded by a newer request is an outcome, not an error.

### 17. Keep application-wide concerns in the shell

Startup, composition, navigation, global feedback, lifecycle, and shutdown are first-class responsibilities of the shell.

Initialization that must precede composition happens once at launch. The resulting values enter the application as typed roots. Deeper components do not reach back into launch arguments, platform globals, or hidden singletons.

Navigation has one owner. Features ask the shell to open, focus, or close a surface. They do not reach into shell controls or into each other's views.

A structural failure prevents the application from presenting a partially valid state.

### 18. Compose platform facts once

Desktop and mobile differ in what they can do and in how much room they have. Both are facts, established at startup and composed like any other.

Express what a device can do as capabilities in domain language: can host peers, can browse a mounted device, can pick files, can give haptic feedback. Code asks whether a capability exists. It does not ask which operating system it is on.

Express how much room there is as a layout profile. Presentation selects by profile. Behavior selects by capability. Neither is inferred from the other.

Platform-specific implementations live at the edge, behind the same service and capability contracts, and are chosen at composition time. Shared code contains no platform branches.

### 19. Shape the codebase by domain ownership

Organize code first by coherent business capability and then by the responsibilities that capability actually needs.

Directories are a map of ownership, not a mandatory pipeline. A small domain may need only a store and a service. A workflow-heavy domain may need a coordinator, several services, and a query or two.

Shared infrastructure remains infrastructure. It must not become an alternative home for domain behavior.

### 20. Design for testing without the world

Every store, coordinator, and query is testable without a view. Every workflow is testable without a device, a file system, a peer, or a wall clock.

This is a design constraint, not a testing convenience. If an owner cannot be exercised without the world, its boundary is in the wrong place or it holds state that belongs elsewhere.

Inject time and dispatch. Make asynchronous completion deterministic. Assert on facts and outcomes — what the store now holds, which typed result came back — not on the sequence of calls that produced them.

### 21. Make the system explain itself

The current state of the application must be explainable from observable facts: what the stores hold, which lifetimes are active, which workflows are in flight and in which phase, what the fingerprints say.

Diagnostics read the same facts the UI reads. Do not add diagnostic-only state, counters that nothing reconciles, or logs that restate what a store already publishes.

Log at boundaries, once, with cause and identity: which device, which recording, which peer, which phase. A log line that cannot be tied to a fact in the system is noise.

### 22. Stay minimal by default

Add structure for a current requirement, not for a possible one.

- A type, interface, stored value, state case, task, observer, cache, generation, or recovery path needs a current consumer or a concrete sequence that requires it.
- Do not defend against cases that no caller, device, peer, or platform event can produce. Check an uncertain platform or device assumption before building code around it.
- A value that other facts determine is derived, not stored and synchronized ([§2](#2-derive-do-not-synchronize)).
- When two designs meet the requirement, choose the one with fewer facts, fewer writers, and fewer ordering dependencies, even when it has more lines.

Simplicity does not trade away correctness. Stale-result rejection, required ordering, and cleanup remain.

## What to resist

Resist:

- view models that orchestrate business workflows;
- one global mutable store containing unrelated domains;
- duplicated flags or stored copies that mirror authoritative or derivable state;
- a "stale" or "dirty" flag where a fingerprint comparison would do;
- machinery for cases that real use cannot produce;
- state cases and phases that no behavior or consumer distinguishes;
- roles, layers, or coordinators added because a diagram lists them;
- observers chained into an implicit workflow language;
- device protocols, file layouts, and peer payloads leaking past their service;
- fire-and-forget operations without ownership or cancellation;
- durable work whose state exists only in memory;
- platform checks where a capability should be asked;
- process-wide globals with implicit initialization order;
- errors logged independently at every layer;
- declaring success because a command completed without verifying the resulting state.

## The test of the architecture

The architecture is working when:

- any important state has an identifiable owner and lifetime;
- a user action has one traceable path into the domain;
- a workflow remains understandable without its initiating screen;
- a device or a peer disconnecting cannot leave objects built for it active;
- interrupted durable work resumes or reconciles safely;
- the authority of every kind of fact is explicit on every device;
- a derived result can be told current or stale from its inputs alone;
- partial failure is represented rather than hidden;
- presentation can be replaced without rewriting business behavior;
- a device protocol, storage engine, or transport can be replaced without redefining the domain;
- the system's current state can be explained from observable facts;
- every type, stored value, and workflow has a current consumer or requirement.

The aspiration is not maximal abstraction. It is a system whose state, intent, effects, ownership, and recovery remain legible as the product grows.
