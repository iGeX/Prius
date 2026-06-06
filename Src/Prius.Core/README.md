# Prius.Core

Core framework project containing fundamental data abstractions and serialization mechanisms.

## Base Interfaces

* **IPocoModel**: A marker interface for POCO models. Used for automatic discovery of derived types during polymorphic serialization.

## Maps System (Data Abstraction)

The central part of the project that provides a unified interface for data access.

### Interfaces and Base Types
* **IMap**: The primary synchronous key-value dictionary interface. Supports hierarchical traversal and access.
* **MapValue**: A universal value container structure. It can encapsulate an `IMap`, a primitive scalar (string, long, bool, decimal, DateTimeOffset), or be empty (`Empty`). Supports implicit type conversions.
* **MapPath**: A struct used for traversing hierarchical paths within Maps (e.g., `user/profile/name`). Supports delimiter escaping.

### IMap Implementations
* **DictionaryMap**: An implementation built upon the standard `IDictionary`.
* **ListMap**: An adapter for `IList` where string-based array indexes serve as keys.
* **JsonReaderMap**: A high-performance implementation that parses data directly from JSON (`ReadOnlyMemory<byte>`) utilizing lazy materialization.
* **PocoModelMap**: An adapter providing access to properties of an `IPocoModel` object through the `IMap` interface using cached reflection.
* **StackedMap**: A composite map that overlays multiple `IMap` instances. It allows stacking data layers where the most recently added layer takes precedence.
* **ReadOnlyMap**: A protective wrapper that prevents any mutations to the underlying map.

### Extensions and Utilities
* **MapExtensions**: The primary suite of extension methods for `IMap` and `MapValue` covering deep copying, structural validation, JSON serialization, and path-based operations (`MapPath`).
* **MapTaskExtensions / MapValueTaskExtensions**: Utility extension methods for seamless asynchronous operations using `Task` and `ValueTask` that return maps or map values.
* **MapDebugView**: A proxy class for enhanced visualization of map structures inside the Visual Studio debugger.

## JSON Serialization

* **JsonDefaults**: Static global `JsonSerializerOptions` utilized throughout the ecosystem.
* **PocoModelTypeInfoResolver**: A custom metadata resolver for `System.Text.Json` providing polymorphic type serialization for entities implementing `IPocoModel` via the `$type` discriminator.
