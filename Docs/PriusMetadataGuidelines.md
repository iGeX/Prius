# PRIUS: System & Deployment Metadata Guidelines

This document defines the metadata management architecture, rules for system evolution via migrations, development sandboxes, and the deployment protocol.

## 1. Decoupling Metadata Loops (SRP)
System metadata is strictly separated into two independent loops, each managed by its own dedicated manager class:

### A. Business Logic Loop (System Schema)
* **Purpose**: Describes modules, component blueprints, and archetype base architecture.
* **Responsible Class**: `SystemSchemaMetadataManager`.
* **Example Commands**: `RegisterModule`, `DefineComponent`, `MountComponentToArchetype`.
* **Result**: A snapshot of the system's business logic for a specific version (`SystemSchemaVersion`).

### B. Deployment Loop (Deployment / CI/CD)
* **Purpose**: Describes the physical topology, node instances, environment overrides, and change subscriptions.
* **Responsible Class**: `DeploymentMetadataManager`.
* **Example Commands**: `RegisterNodeInstance`, `SetNodeEnvOverride`, `ScaleArchetypeReplicas`.
* **Result**: An active infrastructure deployment map mapped to a specific `SystemSchemaVersion`.

---

## 2. Commands and Metadata Managers
* **Declarative Representation**: Migration commands are stored as homogeneous `IMap` documents, containing `Action` (action name) and `Args` (mutation arguments) fields.
* **Manager-Centric Execution**: No polymorphism for commands. Each manager class features a centralized `Apply` method containing a `switch(action)` statement.
* **Validation**: Before applying changes, the method in the `switch` block must fully validate the input arguments `Args`. Any contract violations must throw an exception, aborting the transaction.

---

## 3. Snapshot Integrity and Validation
* **Snapshot**: The result of sequentially applying the migration log to an empty state.
* **SnapshotHash**: SHA-256 hash of the canonical representation of the snapshot data.
* **ChainHash**: An accumulated hash (hash of the previous state + hash of the current migration) that protects the history from tampering.
* **Validation Protocols**:
  1. *Quick (during administrative actions)*: Verifies the current snapshot contents against its `SnapshotHash`.
  2. *Full Verification (upon suspicion of tampering)*: Manually replays the entire migration log from scratch and performs a byte-by-byte comparison against the stored snapshot.

---

## 4. Development Lifecycle and Sandboxes
* **Draft Plans**: All metadata changes are initially designed as a `VersionPlan` and do not affect the live production system.
* **Isolated Sandboxes**: A developer can spin up a Sandbox (an isolated physical database in RavenDB cloned from the current draft state) in one click.
* **Local Binaries**: Assemblies (DLLs) uploaded by a developer during draft work are only visible within their specific sandbox.
* **Conflict Resolution (Optimistic Concurrency)**: Merging changes from sandboxes into the main draft is protected by RavenDB `Change Vectors` ("first writer wins").
* **Exclusive Commit Phase**: Applying a finalized plan to production requires isolated entry. The write lock is acquired using RavenDB's transactional Compare-Exchange mechanism (`MetadataLock`).
