# PRIUS.ENGINE: VirtualBus & Lifecycle Guidelines

This document defines the strict engineering principles for the runtime execution environment, the `Bootstrap` container, and the virtual message bus (`VirtualBus`) within the Prius ecosystem.

## 1. Core Terminology: Components vs. Elements
To ensure architectural precision, you must strictly distinguish between blueprint-level abstractions and live runtime blocks:
* **Elements**: These are the actual, concrete blocks of imperative code mounted directly within the `VirtualBus` tree. Elements are the physical execution units that actively intercept, process, and handle the `Put` and `Get` operations passing through their respective paths.
* **Schematic Element Philosophy**: Elements must represent low-level primitives (comparable to transistors or basic logic gates in electronics, such as `GateElement` with its `$Active` gate). Writing complex business logic inside a single C# Element is prohibited. Complex logic must be achieved via hierarchical composition of components. Keep Elements as simple, generic, and reusable as possible.
* **Components**: These are virtual, declarative entities defined purely at the Blueprint level. A Component represents a hierarchical composition that can aggregate concrete Elements as well as other nested Components. This recursive capability allows building highly complex architectural structures from simpler, reusable parts across multiple levels of abstraction ("schematics" approach). During the compilation and deployment phase, this entire nested hierarchy of Components is completely unwrapped, dissolved, and flattened into a singular, unified hierarchical tree of concrete Elements. A Component has no independent runtime presence inside the active bus.

## 2. Lifecycle and Instance Management (Bootstrap Container)
* **The Runtime Container**: The `Bootstrap` class serves as the actual live runtime container for dynamically transformed archetypes. It maintains the persistent database connection and acts as the top-level orchestrator of the lightweight node.
* **Node Configuration & Resolution Sequence**:
  1. The node is explicitly configured with its target Archetype (and optional identifier/Node ID) via environment variables or command-line parameters.
  2. On startup, `Bootstrap` queries the central RavenDB database containing the system-level metadata snapshot (derived by replaying the migration log).
  3. It searches the CI/CD section of the system snapshot using the configured Archetype:
     * **Personalized**: If a specific list of nodes is defined for the archetype, it retrieves the node's unique configuration (allowing the node to manage personalized RavenDB subscriptions without sharing them with other nodes).
     * **Horizontal**: If horizontal scaling is configured without personalization, it retrieves the generic archetype blueprint.
  4. Based on the retrieved configuration, it downloads the required assembly packages (modules), loads them into a new `AssemblyLoadContext` (ALC), compiles the local blueprint, and spins up the `VirtualBus`.
* **Administrative Commands**: `Bootstrap` intercepts and processes strict administrative lifecycle commands:
  * **Stasis** (`OnTransitionToStasis`): Collapsing the current `AssemblyLoadContext` (ALC) and reverting the node back to its clean startup state.
  * **Active** (`OnTransitionToActive`): Allocating a new `AssemblyLoadContext` (ALC), loading the required package tree and blueprints, instantiating the `VirtualBus`, and initializing the respective modules.
  * **Terminated** (`OnTransitionToTerminated`): Executing a complete and immediate termination of the lightweight node, with no expectation of further instructions.
* **VirtualBus Scope**: The `VirtualBus` is not the container; it is created *inside* the newly allocated ALC during the `Active` phase to govern synchronous and asynchronous execution paths within that specific active archetype.

## 3. Control Flow and The Dispatch Pipeline (Put / Get)
* **Strict Synchronous Execution**: Both `Put` and `Get` operation pipelines inside the `VirtualBus` are strictly synchronous.
* **No Async in Dispatch**: You must not use `await` or block execution threads (`Thread.Sleep`, `.Result`, `.Wait()`) inside any method participating in either the `Put` or `Get` dispatch paths.
* **Downward Data Flow**: Data and queries flow exclusively downward through the tree hierarchy. Implementing event-driven mechanics, callbacks, or upward bubbling notifications (`Notify`) to pass data or signals back to parent nodes is strictly prohibited.
* **No Horizontal Connections**: Data and signals propagate strictly down the tree within a single execution cycle. Cross-branch or horizontal propagation inside the same synchronous cycle is forbidden. Any loop back or sideways trigger must be scheduled as a new execution cycle starting from the root of the tree using `PutAbsolute`.
* **Context Interface Separation**:
  - **Elements (Local Scoping)**: Element developers are restricted to the `IElementContext` interface, exposing only relative `Put` and `Get` operations. This enforces the design convention that each element is the root of its own universe.
  - **Infrastructure (ISystemElementContext)**: System services, tests, and intent processors cast the context to `ISystemElementContext` to access properties like `AbsolutePath`, `CallerSegment`, and the `PutAbsolute` execution method.

## 4. Asynchronous Execution Barriers (Intents)
* **Offloading I/O and Long Tasks**: Any operations involving I/O, network communication, database access, or long-running computations must be deferred outside the synchronous pipeline.
* **Declarative Intentions**: To execute an asynchronous task, you must write a declarative intention into an appropriate asynchronous barrier registry. The execution pipeline must remain decoupled from specific handlers; `DataIntentsRegistry` is merely one example for parent database mutations, and other specialized intent registries will handle diverse external I/O channels.
* **Asynchronous Re-entry Rules**: Dedicated background workers or specialized out-of-band handlers process registered intentions asynchronously and route the results back to the system root strictly via `ISystemElementContext.PutAbsolute`. Under no circumstances may an async barrier/worker call `Put` directly on the context, as doing so violates lock hierarchy and transaction boundaries. All re-entry must initiate a new, clean clock-tick from the root.

## 5. Concurrency, Safety, and Operational Isolation
* **Reentrant Node Locking**: Thread safety across the tree is enforced via standard `lock(object)` monitors mapped to absolute data paths. Recursive reentrant calls (`Put` or `Get` on the exact same node by the same execution thread) are expected and valid.
* **Automated Recursion Protection**: You do not need to worry about infinite recursion or stack overflows when implementing Elements for the bus. The framework features an inherent, automated execution depth guard. You do not need to check for depth limits or know internal constants; focus entirely on the logical behavior of the Element, trusting the runtime to prevent stack exhaustion safely.
* **Control Path Isolation**: To prevent application business payloads from corrupting or overwriting internal node statuses, append the `$` prefix to operational control flags (e.g., `context.Put("$Active", value)`).
