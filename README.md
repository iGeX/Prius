# PRIUS: System Architecture & Execution Framework

## AI Agent Execution & Behavior Rules

* **Pre-requisites**: Before generating any code, strictly read and apply the formatting and structural constraints defined in the `./Docs/PriusCodingGuidelines.md` file.
* **Strict Context Isolation**: You are strictly forbidden from independently exploring, indexing, or reading any files within the repository. You must operate exclusively on the exact files provided by the user via @ references in the current session context.
* **Additional File Requests**: If you determine that the task cannot be completed without examining an unprovided file, you must stop execution immediately. Clearly state the specific file needed and explain why it is required, then wait for the user to either provide it via @ reference or explicitly state that it is not needed. If the user explicitly states the file is not needed, you must deductively derive the necessary signatures or semantics from the existing context and examples.
* **Mandatory Planning Phase**: Do not modify files or write code immediately. First, provide a clear, textual architectural plan of the changes, listing the exact files you intend to modify, and wait for explicit user approval ("Approve" or "Proceed").
* **Absolute Exit Condition**: Once the specific task defined in the session is completed and verified, stop execution immediately. Do not attempt to optimize code outside the scope or independently look for further improvements in adjacent modules.

---

## PRIUS: Architectural Essence

### 1. Concept: Declarative System Constructor
Prius is a platform designed for the declarative design, assembly, and orchestration of distributed software systems.
* **Blueprint-Driven Management**: The entire architecture, topology, and connection logic between components are derived completely from metadata.
* **Description Isolation**: System design is fully decoupled from the execution environment. The system description remains static and deterministic, while runtime mechanisms merely interpret these rules in real time.

### 2. Homogeneous Data Environment
The platform completely abandons the use of traditional Data Transfer Objects (DTOs) and custom POCO models.
* **IMap Abstraction**: All entities, signals, configurations, and state structures are homogeneous and operate exclusively through the base primitives `IMap` and `MapValue` (maps or scalars).
* **Unbounded Technical Connectivity**: A single data format allows any base blocks to be placed and connected on a single schema without technical limitations, adapters, or converters. All modules understand this format at a base level; however, end-to-end interface coupling does not guarantee the logical (semantic) viability of the assembled configuration.

### 3. Principle of Dynamic Transformation
Deployment and lifecycle management of applications within the platform are built upon a self-modifying execution environment.
* **Universal Template**: The physical element of deployment on a target node is a lightweight universal application that initially contains no specific business logic.
* **Declarative Upgrade**: Upon startup, this application connects to the central metadata database, determines its archetype, pulls the required packages, and dynamically transforms itself into the specific target application described in the blueprints.
