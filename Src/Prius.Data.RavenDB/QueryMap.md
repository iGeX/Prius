# QueryMap Format Specification

`QueryMap` is an `IMap`-based structure used to define RavenDB queries that are compiled into RQL by the `RqlBuilder`. All field paths automatically undergo normalization via `MapPath` (nested properties are transformed into dot-notation like `User.Profile.Name`) and are securely enclosed in single quotes.

## Root Structure
- `From` (string) - Mandatory. The target RavenDB Index or Collection name.
- `Include` (Map) - Paths to include in the session cache (e.g., `{"Orders": {}}`).
- `Where` (Map) - Filtering criteria supporting operators, javascript, and metadata lookups.
- `Spatial` (Map) - Spatial filtering criteria (Circle or WKT boundaries).
- `GroupBy` (Map) - Fields for grouping. Mutually exclusive with `Facets`.
- `Facets` (Map) - Aggregation/facet configurations. Ignored if `GroupBy` is present.
- `OrderBy` (Map) - Sorting criteria wrapper containing `Order` and `Data` blocks.
- `Select` (Map/Value) - Fields to project, server-side `$load` statements, or JavaScript projection maps.
- `Reduce` (Map) - Map-Reduce aggregation functions (`$sum`, `$avg`, `$min`, `$max`). Requires `GroupBy`.
- `TimeSeries` (Map) - TimeSeries configuration (`{"Name": "Temperature"}`).
- `Skip` (int) - Number of items to skip. Default is `0`.
- `Take` (int) - Number of items to take. Default is `1024`.

---

## Where Clauses & Operators

### Direct Field Constraints
If the value is a primitive/direct `MapValue` instead of an `IMap`, it defaults to an equality check (`$eq`).
- **Field-to-Field Comparison** - If a constraint points to an `IMap` containing a `$field` key, it compares against another document field instead of a parameterized value.
- **System Metadata Lookup** - If a field key starts with `@metadata/`, it compiles into native `metadata(this)['@property']` evaluation.

### Operator Expressions
When a field points to an `IMap`, the following operators are evaluated via a `switch-case`:
- `$eq`, `$neq`, `$gt`, `$gte`, `$lt`, `$lte` - Standard parameterized operations using exact `decimal` precision.
- `$null` (bool) - Compiles to `field = null` or `field != null`.
- `$exists` (bool) - Compiles to `exists(field) = true` or `exists(field) = false`.
- `$between` - Map with `$from`, `$to`, and optional bools `$includeFrom` / `$includeTo`.
- `$in`, `$all` - Compiles to RavenDB collection queries `field in ($p0, $p1)` or `field all in ($p0, $p1)`. If the target map is empty, it safely evaluates to `id() == null`.
- `$search` - Map with `$term` and an optional `options` map containing `Operator` (`"AND"` / `"OR"`), `Boost` (decimal), and `Wildcard` (bool).

### Logical Blocks (`$or`, `$and`)
Logical blocks require an explicit evaluation order. They must contain two internal maps: `Order` and `Data`.

### JavaScript Filtering (`$js`)
Executes direct RavenDB server-side JavaScript via `where javascript(...)`.

---

## Spatial Clauses
Evaluated inside the root `Spatial` block. Supports circular and complex polygon constraints:
- `$within` + `Circle` - Circular boundary evaluation.
- `$within` + `Wkt` - Complex geometric Well-Known Text polygon evaluation.

---

## Sorting (`OrderBy`)
Requires `Order` (index mapping) and `Data` (field-to-direction mapping). Supported directions are `"Asc"` (default) and `"Desc"`. Can also evaluate spatial distances via the `$spatialDistance` marker.

---

## Projections, Reduce, and Facets

### 1. Projections (`Select`)
The `Select` root node outputs projection clauses and supports 4 states:
- **Empty / Omitted** - Bypasses token injection.
- **String Primitive** - Compiles as a raw projection fallback.
- **JavaScript Projection Map** - Evaluated via a `$js` key wrapped in braces.
- **Standard Field Projections** - A map of fields where the key is the alias. If the inner map contains a `$load` clause, it executes a server-side document link resolution.

### 2. Map-Reduce (`Reduce`)
Evaluated inside `BuildSelect` if `Reduce` is populated. Each key is the alias, and the inner map specifies the operation (`$sum`, `$avg`, `$min`, `$max`). Requires an active `GroupBy` declaration.

### 3. Aggregations (`Facets`)
Evaluated via the `Facets` root block. Ignored if `GroupBy` is present. Generates native `select facet(...)` statements.
