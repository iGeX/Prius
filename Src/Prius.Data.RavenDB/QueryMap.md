# QueryMap Format Specification

`QueryMap` is an `IMap`-based structure used to define RavenDB queries that are compiled into RQL by the `RqlBuilder`.

## Root Structure
- `From` (string): The collection name to query (e.g., `"Users"`).
- `Include` (Map): Paths to include in the query (e.g., `{"Orders": {}}`).
- `Where` (Map): Filtering criteria.
- `Spatial` (Map): Spatial filtering criteria.
- `GroupBy` (Map): Fields for grouping (e.g., `{"Company": {}}`).
- `OrderBy` (Map): Sorting criteria.
- `Select` (Map/Value): Fields to project.
- `Reduce` (Map): Aggregation functions.
- `TimeSeries` (Map): TimeSeries configuration (e.g., `{"Name": "Temperature"}`).
- `Skip` (int): Number of items to skip.
- `Take` (int): Number of items to take.

## Where Clauses
Support logical blocks (`$or`, `$and`), JS filtering (`$js`), and standard field operators:
- `$eq`, `$neq`, `$gt`, `$gte`, `$lt`, `$lte`
- `$null`: bool value.
- `$exists`: bool value.
- `$between`: Map with `$from`, `$to`, `$includeFrom`, `$includeTo`.
- `$in`, `$all`: Map of items.
- `$search`: Map with `$term` and `$options`.

## Spatial Clauses
Used within `Spatial` block:
- `$within`: Spatial circle query.
  ```json
  "Spatial": {
    "Field": "Location",
    "$within": { "Circle": { "Radius": 10, "Latitude": 55.7, "Longitude": 37.6 } }
  }
  ```

## TimeSeries Clauses
Used within `TimeSeries` block:
- `Name`: Name of the timeseries.

## Example
```json
{
  "From": "Users",
  "Where": {
    "Age": { "$gt": 21 },
    "Status": "Active"
  },
  "TimeSeries": { "Name": "HeartRate" },
  "Take": 10
}
```
