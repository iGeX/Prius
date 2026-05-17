# QueryMap Format Specification

`QueryMap` is an `IMap`-based structure used to define RavenDB queries that are compiled into RQL by the `RqlBuilder`. All field paths automatically undergo normalization via `MapPath` (nested properties are transformed into dot-notation like `User.Profile.Name`).

## Root Structure
- `From` (string) - **Mandatory**. The target RavenDB Index or Collection name (generates `from index 'IndexName'`).
- `Include` (Map) - Paths to include in the session cache (e.g., `{"Orders": {}}` -> `include Orders`).
- `Where` (Map) - Filtering criteria (nested logic, operators, or server-side JavaScript).
- `Spatial` (Map) - Spatial filtering criteria.
- `GroupBy` (Map) - Fields for grouping (e.g., `{"Company": {}}` -> `group by Company`). **Mutually exclusive with `Facets`**.
- `Facets` (Map) - Aggregation/facet configurations (generates `select func(field) as alias`). **Ignored if `GroupBy` is present**.
- `OrderBy` (Map) - Sorting criteria wrapper containing `Order` and `Data` blocks.
- `Select` (Map/Value) - Fields to project or client-side JavaScript projection maps.
- `Reduce` (Map) - Map-Reduce aggregation functions (`$sum`, `$avg`, `$min`, `$max`).
- `TimeSeries` (Map) - TimeSeries configuration (`{"Name": "Temperature"}`). Modifies the `from index` clause into `from index '...' timeseries(...)`.
- `Skip` (int) - Number of items to skip (parameterized). Default is `0`.
- `Take` (int) - Number of items to take (parameterized). Default is `1024`.

---

## Where Clauses & Operators

### Direct Field Constraints
If the value is a primitive/direct `MapValue` instead of an `IMap`, it defaults to an equality check (`$eq`).
- **Field-to-Field Comparison** - If a constraint points to an `IMap` containing a `$field` key, it compares against another document field instead of a parameterized value:
```json
{
  "Where": {
    "UpdatedAt": {
      "$field": "CreatedAt"
    }
  }
}
```

### Operator Expressions
When a field points to an `IMap`, the following operators are evaluated via a `switch-case`:
- `$eq`, `$neq`, `$gt`, `$gte`, `$lt`, `$lte` - Standard parameterized operations (`=`, `!=`, `>`, `>=`, `<`, `<=`).
- `$null` (bool) - Compiles to `field = null` (if true) or `field != null` (if false).
- `$exists` (bool) - Compiles to `exists(field) = true` (if true) or `exists(field) = false`.
- `$between` - Map with `$from`, `$to`, and optional bools `$includeFrom` / `$includeTo` (defaulting to true).
```json
{
  "Age": {
    "$between": {
      "$from": 18,
      "$to": 30,
      "$includeFrom": true,
      "$includeTo": true
    }
  }
}
```
- `$in`, `$all` - Compiles to RavenDB collection queries `field in ($p0, $p1)` or `field all in ($p0, $p1)`. The map keys are treated as the array items.
- `$search` - Map with `$term` and an optional `options` map containing `Operator` (`"AND"` / `"OR"`) and `Boost` (double).
```json
{
  "Description": {
    "$search": {
      "$term": "RavenDB",
      "$options": {
        "Operator": "AND",
        "Boost": 2.5
      }
    }
  }
}
```

### Logical Blocks (`$or`, `$and`)
Logical blocks require an explicit evaluation order. They must contain two internal maps: `Order` and `Data`.
```json
{
  "Where": {
    "$or": {
      "Order": {
        "0": "cond1",
        "1": "cond2"
      },
      "Data": {
        "cond1": {
          "Age": {
            "$gt": 21
          }
        },
        "cond2": {
          "Status": "Premium"
        }
      }
    }
  }
}
```

### JavaScript Filtering (`$js`)
Executes direct RavenDB server-side JavaScript:
```json
{
  "Where": {
    "$js": "this.Age > 21 && this.Status == 'Active'"
  }
}
```

---

## Spatial Clauses
Evaluated inside the root `Spatial` block. Currently supports the `$within` operator for circular boundaries:
```json
{
  "Spatial": {
    "Field": "Location",
    "$within": {
      "Circle": {
        "Radius": 10.5,
        "Latitude": 55.7,
        "Longitude": 37.6
      }
    }
  }
}
```

---

## Sorting (`OrderBy`)
Requires `Order` (index mapping) and `Data` (field-to-direction mapping). Supported directions are `"Asc"` (default) and `"Desc"` (case-insensitive).
```json
{
  "OrderBy": {
    "Order": {
      "0": "LastName",
      "1": "Age"
    },
    "Data": {
      "LastName": "Asc",
      "Age": "Desc"
    }
  }
}
```

---

## Projections, Reduce, and Facets

### 1. Projections (`Select`)
The `Select` root node outputs projection clauses and supports 4 states:
- **Empty / Omitted** - Compiles to `select *`.
- **String Primitive** - Compiles as a raw projection fallback (e.g., `select Value`).
- **JavaScript Projection Map** - If an internal map has a `$js` key, it wraps it in curly braces:
```json
{
  "Select": {
    "$js": "Name: this.FirstName + ' ' + this.LastName"
  }
}
```
- **Standard Field Projections** - A map of fields where the key is the *alias*, and the value is either an empty object/primitive or a path string.
```json
{
  "Select": {
    "UserAge": "Age",
    "Status": {}
  }
}
```

### 2. Map-Reduce (`Reduce`)
Evaluated inside `BuildSelect` if `Reduce` is populated. Each key is the alias, and the inner map specifies the operation (`$sum`, `$avg`, `$min`, `$max`).
```json
{
  "Reduce": {
    "TotalPrice": {
      "$sum": "Price"
    },
    "LowestCost": {
      "$min": "Cost"
    }
  }
}
```

### 3. Aggregations (`Facets`)
Evaluated via the `Facets` root block. **Ignored if `GroupBy` is present.** Generates standard RavenDB aggregation outputs.
```json
{
  "Facets": {
    "TotalRevenue": {
      "Function": "sum",
      "Field": "Price"
    },
    "ItemsCount": {
      "Function": "count",
      "Field": "Id"
    }
  }
}
```

---

## Example
```json
{
  "From": "UsersIndex",
  "Where": {
    "Age": {
      "$gt": 21
    },
    "Status": "Active"
  },
  "Facets": {
    "TotalUsers": {
      "Function": "count",
      "Field": "Id"
    }
  },
  "Skip": 0,
  "Take": 50
}
```