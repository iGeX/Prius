# PRIUS.CORE: Core Data Architecture & Best Practices

This library defines the fundamental data primitives and types for the entire Prius ecosystem. It enforces a completely homogeneous data environment across all modules and applications.

## 1. The Principle of Data Homogeneity
The core architecture completely eliminates traditional Data Transfer Objects (DTOs) and custom POCO models. Every piece of data, state, or configuration within the platform exists exclusively within two unified abstractions:

* **IMap**: Represents a hierarchical, tree-like data structure (a dictionary/map node).
* **MapValue**: A strict variant type that can exclusively encapsulate one of the following underlying types: Empty, IMap, string, long, bool, decimal, or DateTimeOffset.

By relying strictly on these primitives, any component in the ecosystem can read, write, or pipe data from any other component without requiring custom mappers, translation layers, or adapters.

## 2. Truthy Semantics (Logic Evaluation)
Conditional branching and boolean evaluation of any data node must strictly rely on the behavior of the `MapValue.AsBool()` method. The evaluation follows fixed invariants:

* **TRUE**: Any non-empty map node, any non-zero numeric value, any non-empty string variable (except the literal text "false").
* **FALSE**: The Empty state, a numeric 0, an empty string "", or the explicit string value "false".

## 3. Allocationless Path Mechanics
Data traversal and addressing across hierarchical maps are highly optimized for execution efficiency:

* **No String Allocations**: Parsing, slicing, and matching structural paths must never trigger allocations in the managed heap.
* **Span-Based Routing**: All path operations utilize `ref struct MapPath` operating directly over `ReadOnlySpan<char>`.

## 4. Map Initialization and Composition
* **Test Provisioning**: For unit testing and creating mock data configurations, never build maps procedurally. The strictly preferred method is using raw multiline string literals passed into `JsonReaderMap.From()`. This maintains clear visual alignment with the target JSON structure.

  Example configuration pattern:
  
  var queryMap = JsonReaderMap.From("""
  {
      "From": "Users/ByNotes",
      "Where": {
          "Notes": {
              "$search": { "$term": "RavenDB" }
          }
      },
      "Highlight": { "Field": "Notes" }
  }
  """);

* **Fluent Building**: For runtime map construction, utilize the fluent `.With()` extension methods. It allows chaining key-value pairs, nested sub-maps, or merging multiple maps into one without manual looping.

## 5. Safe Deep Tree Traversal
* **Hierarchical Mutation**: Avoid manual nested checks when accessing deep keys. Use `DeepGet()` and `DeepPut()` to read or write structures using a `MapPath`.
* **Automatic Tree Growth**: When calling `DeepPut()`, the runtime automatically provisions intermediate missing path segments as `DictionaryMap.New`. You do not need to initialize parent nodes manually before executing a deep write.

## 6. Pattern Matching and Type Conversion
* **Functional Branching**: Never perform manual casting or unsafe type checks on a `MapValue`. Always use the `.Match()` or `.Switch()` extension methods to cleanly handle Empty states, nested IMaps, or primitive scalars via lambdas.
* **Polymorphic Upcasting**: Use the `.AsMapValue()` extension to convert standard .NET types (including `IDictionary`, `IEnumerable`, and `IPocoModel`) into a fully compliant ecosystem element.
