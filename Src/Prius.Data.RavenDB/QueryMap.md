# QueryMap Format Specification

`QueryMap` is a declarative, `IMap`-based structure used to define RavenDB queries. The `RqlBuilder` compiler translates this map into syntactically clean RQL (Raven Query Language), while the intent processor handles response materialization, including deterministic composite ID generation and parallel Lucene metadata extraction.

All field paths automatically undergo normalization via `MapPath` (nested properties separated by slashes, e.g., `User/Profile/Name`, are transformed into dot-notation like `'User'.'Profile'.'Name'`) and are securely enclosed in single quotes to safeguard against injections and special characters.

---

## 1. Root Nodes Structure

- **`From`** (string) — Mandatory node. Specifies the name of the static RavenDB index. According to architectural guidelines, the system always generates a `from index '...'` token, strictly prohibiting the use of dynamic auto-indexes in production.
- **`Where`** (Map) — Filtering criteria for documents. Supports standard comparison operators, nested logical blocks, and raw JavaScript.
- **`Spatial`** (Map) — Criteria for geospatial searches (Circle or WKT boundaries).
- **`GroupBy`** (Map) — Meta-contract specifying the grouping fields. It is not rendered into the RQL string (since all indexes are static and aggregation happens asynchronously on the server), but it is used by the intent processor to deterministically assemble composite IDs.
- **`Facets`** (Map) — Aggregation and facet configurations. Ignored by the processor if the `GroupBy` node is populated.
- **`OrderBy`** (Map) — Sorting criteria container containing ordered arrays of fields and directions.
- **`Select`** (Map/Value) — Projection contract (custom fields, server-side document link resolution via `$load`, or JS projection maps).
- **`Reduce`** (Map) — Meta-declaration for Map-Reduce functions (`$sum`, `$avg`, `$min`, `$max`). Requires an active `GroupBy` node; otherwise, the compiler throws an `InvalidOperationException`.
- **`TimeSeries`** (Map) — Configuration for retrieving time series data.
- **`Highlight`** (Map) — Full-text search result highlighting configurations. Translates into a native `include highlight(...)` token.
- **`Skip`** (int) — Paging offset. Defaults to `0`.
- **`Take`** (int) — Paging limit. Defaults to `1024`.

---

## 2. Filtering Node (`Where`)

### Direct Field Constraints
If a property value is a primitive (`MapValue`) instead of a nested `IMap`, the compiler defaults to an equality check (`=`).
- **Field-to-Field Comparison** — If a nested map contains a `$field` marker, the value is treated as the name of another document property. It is normalized with single quotes and injected without parameterization (e.g., `'UpdatedAt' = 'CreatedAt'`).
- **System Metadata Lookup** — Property keys starting with the `@metadata/` prefix are compiled into a native server-side lookup: `metadata(this)['property_name']`.

### Operator Expressions
- `$eq`, `$neq`, `$gt`, `$gte`, `$lt`, `$lte` — Standard comparison operations. Floating-point numbers are processed with strict `decimal` precision.
- `$null` (bool) — Compiles to `field = null` or `field != null`.
- `$exists` (bool) — Compiles to `exists(field) = true` or `exists(field) = false`.
- `$between` — Range-based queries. Contains `$from` and `$to` nodes, alongside optional boolean flags `$includeFrom` and `$includeTo`.
- `$in`, `$all` — Collection membership checks (compiles to `in ($p0, $p1)` or `all in ($p2, $p3)` respectively). If an empty map is passed, the compiler generates a safe, false condition `id() == null` to prevent runtime query crashes on the server.
- `$search` — Full-text Lucene search. Accepts a `$term` and an optional `$options` map containing `Operator` (`"AND"`/`"OR"`), a fractional `Boost` via InvariantCulture, and a `Wildcard` flag. If `Wildcard: true`, an asterisk (`*`) is automatically appended to the end of the term.

### Logical Blocks (`$or`, `$and`)
To enforce explicit operator precedence, logical blocks require separation into two inner nodes: `Order` (execution indices) and `Data` (the target condition maps).

### JavaScript Filtering (`$js`)
Executes direct RavenDB server-side JavaScript filtering via the `where javascript(...)` construct.

---

## 3. Geospatial Search (`Spatial`) and Sorting

Evaluated inside the root `Spatial` block. Coordinates and radius values are hardcoded directly into the RQL query string as invariant literals, completely eliminating index tracking errors within the parameter dictionary.
- **`Circle`** — Circular boundary evaluation. According to the RavenDB RQL specification, arguments for the `spatial.circle` function must be rendered strictly in the following order: **`(radius, latitude, longitude)`**. The radius is measured in kilometers.
- **`Wkt`** — Complex geometric Well-Known Text polygons. Parameterized using the `$p` token.

### Spatial Distance Sorting
If the **`$spatialDistance`** marker is declared inside the `OrderBy` block, the compiler automatically generates a server-side distance calculation sorting clause relative to the target point specified in the `Circle` configuration:
`order by spatial.distance('Field', spatial.point(Lat, Lng))`

---

## 4. Intent Processor Response Materialization Specifics

The intent processor bypasses the driver's high-level C# LINQ abstractions, interacting directly with the network payload via the low-level **`query.GetQueryResultAsync(token)`** call. This ensures parallel data streams are extracted seamlessly from the HTTP response package.

### Idempotent Id Materialization (Map-Reduce)
Virtual projection entries produced by static Map-Reduce indexes do not contain a system `@id` property in their metadata, as they are computed on the fly and do not exist as distinct documents in the store. However, they contain the grouping keys used for aggregation. The processor derives a composite identifier by consulting the `GroupBy` meta-contract from the original `QueryMap`:
1. Grouping keys are extracted from the `GroupBy` map with a strict alphabetical sort enforced via **`Keys(true)`**.
2. Corresponding values are retrieved from the document body and concatenated using a forward slash (`/`).
3. This sorted sequence guarantees absolute ID idempotency (e.g., `prod/1/category/42`) regardless of the property declaration order inside the incoming JSON request.

### Parallel Includes and Highlights Gathering
- **`Includes`** — Referenced documents are pulled directly from the `queryResult.Includes` network payload node using their physical keys and populated under a dedicated `"Includes"` root node in the final output.
- **`Highlights`** — Text fragments highlighted by Lucene are extracted from the parallel `queryResult.Highlightings` dictionary. In the output map, they are isolated under a distinct `"Highlights"` root node, mapped by document ID and the original text field name.