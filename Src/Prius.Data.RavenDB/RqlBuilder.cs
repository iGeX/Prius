// ReSharper disable PossibleMultipleEnumeration
namespace Prius.Data.RavenDB;

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Core.Maps;

public static class RqlBuilder
{
    private const int DefaultLimit = 1024;

    public static (string Rql, Dictionary<string, object> Parameters) Build(IMap? queryMap)
    {
        if (queryMap == null || queryMap.IsEmpty)
            return (string.Empty, []);

        var sb = new StringBuilder();
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);

        var fromVal = queryMap["From"];
        if (fromVal.IsEmpty)
            return (string.Empty, []);

        BuildFrom(sb, fromVal, queryMap["TimeSeries"].AsMap());
        BuildWhereAndSpatial(sb, queryMap, parameters);
        BuildOrderBy(sb, queryMap["OrderBy"].AsMap(), queryMap["Spatial"].AsMap());
        
        BuildInclude(sb, queryMap["Include"].AsMap(), queryMap["Highlight"].AsMap());
        
        BuildSelect(
            sb, 
            queryMap["Select"], 
            queryMap["Reduce"].AsMap(), 
            queryMap["Facets"].AsMap(), 
            queryMap["GroupBy"].AsMap()
        );
        
        BuildLimit(sb, queryMap["Skip"], queryMap["Take"], parameters);

        return (sb.ToString().TrimEnd(), parameters);
    }
    
    private static void BuildFrom(StringBuilder sb, MapValue fromVal, IMap tsMap)
    {
        var escapedFrom = fromVal.AsString().Replace("'", "''");

        if (tsMap.IsEmpty)
        {
            sb.Append("from index '");
            sb.Append(escapedFrom);
            sb.Append("' ");
            return;
        }

        var escapedTsName = tsMap["Name"].AsString().Replace("'", "''");

        sb.Append("from index '");
        sb.Append(escapedFrom);
        sb.Append("' timeseries('");
        sb.Append(escapedTsName);
        sb.Append("') ");
    }
    
    private static void BuildWhereAndSpatial(StringBuilder sb, IMap queryMap, Dictionary<string, object> parameters)
    {
        var whereMap = queryMap["Where"].AsMap();
        var spatialMap = queryMap["Spatial"].AsMap();
        
        var hasWhere = !whereMap.IsEmpty;
        var hasSpatial = !spatialMap.IsEmpty;

        if (!hasWhere && !hasSpatial)
            return;

        var hasValidSpatial = false;
        if (hasSpatial)
        {
            var withinMap = spatialMap["$within"].AsMap();
            hasValidSpatial = !withinMap["Wkt"].IsEmpty || !withinMap["Circle"].IsEmpty;
        }

        if (!hasWhere && !hasValidSpatial)
            return;

        sb.Append("where ");
        
        if (hasWhere)
            BuildWhere(sb, whereMap, parameters);

        if (hasValidSpatial)
        {
            if (hasWhere)
                sb.Append(" and ");
            BuildSpatial(sb, spatialMap, parameters);
        }
        sb.Append(' ');
    }

    private static void BuildSpatial(StringBuilder sb, IMap spatialMap, Dictionary<string, object> parameters)
    {
        var field = NormalizePath(spatialMap["Field"].AsString());
        var op = spatialMap.Keys().FirstOrDefault(k => k.StartsWith('$'));
        
        if (op != "$within")
            return;

        var withinMap = spatialMap["$within"].AsMap();
        var circle = withinMap["Circle"].AsMap();
        var wkt = withinMap["Wkt"];

        if (!wkt.IsEmpty)
        {
            var pWkt = "p" + parameters.Count.ToIndexString();
            parameters.Add(pWkt, wkt.AsString());
            sb.Append($"spatial.within({field}, spatial.wkt(${pWkt}))");
            return;
        }

        var lat = circle["Latitude"].AsValue<decimal>().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var lng = circle["Longitude"].AsValue<decimal>().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var rad = circle["Radius"].AsValue<decimal>().ToString(System.Globalization.CultureInfo.InvariantCulture);

        sb.Append($"spatial.within({field}, spatial.circle({rad}, {lat}, {lng}))");
    }

    private static void BuildLimit(StringBuilder sb, MapValue skipVal, MapValue takeVal, Dictionary<string, object> parameters)
    {
        var skip = skipVal.IsEmpty ? 0 : skipVal.AsInt();
        var take = takeVal.IsEmpty ? DefaultLimit : takeVal.AsInt();
        
        var pSkip = "p" + parameters.Count.ToIndexString();
        parameters.Add(pSkip, skip);
        
        var pTake = "p" + parameters.Count.ToIndexString();
        parameters.Add(pTake, take);

        sb.Append($"limit ${pSkip}, ${pTake}");
    }

    private static void BuildInclude(StringBuilder sb, IMap includeMap, IMap highlightMap)
    {
        foreach (var key in includeMap.Keys())
        {
            sb.Append("include ");
            sb.Append(NormalizePath(key));
            sb.Append(' ');
        }

        if (!highlightMap.IsEmpty)
        {
            var originalField = highlightMap["Field"].AsString();
            var field = NormalizePath(originalField);
            sb.Append($"include highlight({field}, 128, 5) ");
        }
    }

    private static void BuildWhere(StringBuilder sb, IMap whereMap, Dictionary<string, object> parameters)
    {
        var first = true;
        foreach (var key in whereMap.Keys())
        {
            if (!first)
                sb.Append(" and ");

            first = false;
            var val = whereMap[key];

            if (key == "$or" || key == "$and")
            {
                BuildLogicalBlock(sb, key, val.AsMap(), parameters);
                continue;
            }

            if (key == "$js")
            {
                sb.Append("javascript(");
                sb.Append(val.AsString());
                sb.Append(')');
                continue;
            }

            var normalizedKey = NormalizePath(key);
            
            if (!val.IsMap)
            {
                AppendConstraint(sb, normalizedKey, "=", val, parameters);
                continue;
            }

            BuildOperatorExpression(sb, normalizedKey, val.AsMap(), parameters);
        }
    }

    private static void BuildLogicalBlock(StringBuilder sb, string op, IMap blockMap, Dictionary<string, object> parameters)
    {
        var orderMap = blockMap["Order"].AsMap();
        var dataMap = blockMap["Data"].AsMap();
        if (orderMap.IsEmpty || dataMap.IsEmpty)
            return;

        sb.Append('(');
        var rqlOp = op == "$or" ? " or " : " and ";
        var first = true;
        var count = orderMap.Keys().Count();

        for (var i = 0; i < count; i++)
        {
            if (!first)
                sb.Append(rqlOp);

            first = false;
            var condKey = orderMap[i.ToIndexString()].AsString();
            var condMap = dataMap[condKey].AsMap();
            
            BuildWhere(sb, condMap, parameters);
        }
        sb.Append(')');
    }

    private static void BuildOperatorExpression(StringBuilder sb, string field, IMap opMap, Dictionary<string, object> parameters)
    {
        foreach (var opKey in opMap.Keys())
        {
            var val = opMap[opKey] ;

            switch (opKey)
            {
                case "$eq": AppendConstraint(sb, field, "=", val, parameters); break;
                case "$neq": AppendConstraint(sb, field, "!=", val, parameters); break;
                case "$gt": AppendConstraint(sb, field, ">", val, parameters); break;
                case "$gte": AppendConstraint(sb, field, ">=", val, parameters); break;
                case "$lt": AppendConstraint(sb, field, "<", val, parameters); break;
                case "$lte": AppendConstraint(sb, field, "<=", val, parameters); break;
                case "$null":
                    sb.Append(field);
                    sb.Append(val.AsValue<bool>() ? " = null" : " != null");
                    break;
                case "$exists":
                    sb.Append("exists(");
                    sb.Append(field);
                    sb.Append(val.AsValue<bool>() ? ") = true" : ") = false");
                    break;
                case "$between":
                    var fromVal = val["$from"];
                    var toVal = val["$to"];
                    var incFrom = val["$includeFrom"].IsEmpty || val["$includeFrom"].AsValue<bool>();
                    var incTo = val["$includeTo"].IsEmpty || val["$includeTo"].AsValue<bool>();

                    AppendConstraint(sb, field, incFrom ? ">=" : ">", fromVal, parameters);
                    sb.Append(" and ");
                    AppendConstraint(sb, field, incTo ? "<=" : "<", toVal, parameters);
                    break;
                case "$in":
                case "$all":
                    var keys = val.AsMap().Keys();
                    if (!keys.Any())
                    {
                        sb.Append("id() == null");
                        break;
                    }

                    sb.Append(field);
                    sb.Append(opKey == "$in" ? " in (" : " all in (");
                    var first = true;
                    foreach (var itemKey in keys)
                    {
                        if (!first)
                            sb.Append(", ");
                        first = false;
                        var pName = "p" + parameters.Count.ToIndexString();
                        parameters.Add(pName, itemKey);
                        sb.Append('$');
                        sb.Append(pName);
                    }
                    sb.Append(')');
                    break;
                case "$search":
                    var term = val["$term"].AsString();
                    var options = val["$options"].AsMap();
                    var searchOp = options["Operator"].IsEmpty ? "OR" : options["Operator"].AsString();
                    var boost = options["Boost"];
                    var wildcard = !options["Wildcard"].IsEmpty && options["Wildcard"].AsValue<bool>();

                    if (wildcard && !term.EndsWith('*'))
                        term += "*";

                    var pName1 = "p" + parameters.Count.ToIndexString();
                    parameters.Add(pName1, term);

                    sb.Append($"search({field}, ${pName1}");
                    if (searchOp == "AND")
                        sb.Append(", AND");
                    sb.Append(')');

                    if (!boost.IsEmpty)
                    {
                        sb.Append(" boost ");
                        var boostValue = boost.AsValue<decimal>();
                        sb.Append(boostValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                    break;
            }
        }
    }

    private static void AppendConstraint(StringBuilder sb, string field, string op, MapValue val, Dictionary<string, object> parameters)
    {
        sb.Append(field);
        sb.Append(' ');
        sb.Append(op);
        sb.Append(' ');

        if (val.IsMap && !val["$field"].IsEmpty)
        {
            sb.Append(NormalizePath(val["$field"].AsString()));
            return;
        }

        var pName = "p" + parameters.Count.ToIndexString();
        parameters.Add(pName, val.AsValue() ?? DBNull.Value);
        sb.Append('$');
        sb.Append(pName);
    }

    private static void BuildOrderBy(StringBuilder sb, IMap orderByMap, IMap spatialMap)
    {
        var orderMap = orderByMap["Order"].AsMap();
        var dataMap = orderByMap["Data"].AsMap();
        if (orderMap.IsEmpty || dataMap.IsEmpty)
            return;

        sb.Append("order by ");
        var first = true;
        var count = orderMap.Keys().Count();

        for (var i = 0; i < count; i++)
        {
            if (!first)
                sb.Append(", ");
            first = false;
            var fieldKey = orderMap[i.ToIndexString()].AsString();
            var direction = dataMap[fieldKey].AsString();
            
            if (fieldKey == "$spatialDistance")
            {
                var field = NormalizePath(spatialMap["Field"].AsString());
                var circle = spatialMap["$within"]["Circle"].AsMap();
                sb.Append($"spatial.distance({field}, spatial.point({circle["Latitude"].AsValue<decimal>().ToString(System.Globalization.CultureInfo.InvariantCulture)}, {circle["Longitude"].AsValue<decimal>().ToString(System.Globalization.CultureInfo.InvariantCulture)}))");
            }
            else
                sb.Append(NormalizePath(fieldKey));

            if (string.Equals(direction, "Desc", StringComparison.OrdinalIgnoreCase))
                sb.Append(" desc");
        }
        sb.Append(' ');
    }

    private static void BuildSelect(StringBuilder sb, MapValue selectVal, IMap reduceMap, IMap facetsMap, IMap groupByMap)
    {
        if (selectVal.IsEmpty && reduceMap.IsEmpty && facetsMap.IsEmpty)
            return;

        if (groupByMap.IsEmpty && !facetsMap.IsEmpty)
        {
            sb.Append("select ");
            var first = true;
            foreach (var key in facetsMap.Keys())
            {
                if (!first) sb.Append(", ");
                first = false;

                var facetMap = facetsMap[key].AsMap();
                var field = NormalizePath(facetMap["Field"].AsString());
                
                if (string.IsNullOrEmpty(field))
                    field = NormalizePath(key).Replace("Count", "").Replace("Sum", "");

                sb.Append($"facet({field})");
            }
            sb.Append(' ');
            return; 
        }

        sb.Append("select ");

        if (selectVal.IsMap && !selectVal["$js"].IsEmpty)
        {
            sb.Append('{');
            sb.Append(selectVal["$js"].AsString());
            sb.Append('}');
            sb.Append(' ');
            return;
        }

        if (!reduceMap.IsEmpty)
        {
            if (groupByMap.IsEmpty) throw new InvalidOperationException("Map-Reduce aggregations (Reduce) require a GroupBy clause in QueryMap.");

            var first = true;
            foreach (var key in reduceMap.Keys())
            {
                if (!first) sb.Append(", ");
                first = false;
                
                var funcMap = reduceMap[key].AsMap();
                BuildReduceFunction(sb, key, funcMap);
            }
            sb.Append(' ');
            return;
        }

        if (selectVal.IsEmpty)
        {
            sb.Append("* ");
            return;
        }

        if (!selectVal.IsMap)
        {
            sb.Append(selectVal.AsString()).Append(' ');
            return;
        }
        
        var map = selectVal.AsMap();
        var first1 = true;
        foreach (var key in map.Keys())
        {
            if (!first1) sb.Append(", ");
            first1 = false;

            var value = map[key];
            var escapedAlias = $"'{key.Replace("'", "''")}'";

            if (value.IsMap && !value["$load"].IsEmpty)
            {
                var loadMap = value["$load"].AsMap();
                var targetField = NormalizePath(loadMap["Field"].AsString());
                var pathInTarget = NormalizePath(loadMap["Path"].AsString());
                sb.Append($"load({targetField}).{pathInTarget} as {escapedAlias}");
            }
            else
            {
                sb.Append(NormalizePath(!value.IsString ? key : value.AsString()))
                  .Append(" as ")
                  .Append(escapedAlias);
            }
        }
        sb.Append(' ');
    }

    private static void BuildReduceFunction(StringBuilder sb, string alias, IMap funcMap)
    {
        var escapedAlias = $"'{alias.Replace("'", "''")}'";

        foreach (var opKey in funcMap.Keys())
        {
            var field = NormalizePath(funcMap[opKey].AsString());
            var segment = opKey switch
            {
                "$sum" => $"sum({field}) as {escapedAlias}",
                "$avg" => $"avg({field}) as {escapedAlias}",
                "$min" => $"min({field}) as {escapedAlias}",
                "$max" => $"max({field}) as {escapedAlias}",
                _ => null
            };
            
            if (segment != null)
                sb.Append(segment);
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        if (path.StartsWith("@metadata/"))
        {
            var metaProp = path.Substring("@metadata/".Length).Replace("'", "''");
            return $"metadata(this)['{metaProp}']";
        }

        var mapPath = new MapPath(path);
        var sb = new StringBuilder();
        
        var head = mapPath.Head;
        while (!string.IsNullOrEmpty(head))
        {
            if (sb.Length > 0) 
                sb.Append('.');
                
            sb.Append('\'');
            sb.Append(head.Replace("'", "''"));
            sb.Append('\'');

            mapPath = mapPath.Tail;
            head = mapPath.Head;
        }
        
        return sb.ToString();
    }
}
