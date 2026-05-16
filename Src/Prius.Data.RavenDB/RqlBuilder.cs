namespace Prius.Data.RavenDB;

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Core.Maps;

public static class RqlBuilder
{
    public static (string Rql, Dictionary<string, object> Parameters) Build(IMap? queryMap)
    {
        if (queryMap == null || queryMap.IsEmpty)
            return (string.Empty, []);

        var sb = new StringBuilder();
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);

        var fromVal = queryMap.Get("From");
        if (fromVal.IsEmpty)
            return (string.Empty, []);

        BuildFrom(sb, fromVal, queryMap.Get("TimeSeries").AsMap());
        BuildInclude(sb, queryMap.Get("Include").AsMap());
        BuildWhereAndSpatial(sb, queryMap, parameters);
        BuildGroupByAndFacets(sb, queryMap);
        BuildOrderBy(sb, queryMap.Get("OrderBy").AsMap());
        BuildSelect(sb, queryMap.Get("Select"), queryMap.Get("Reduce").AsMap());
        BuildLimit(sb, queryMap.Get("Skip"), queryMap.Get("Take"));

        return (sb.ToString().TrimEnd(), parameters);
    }
    
    private static void BuildFrom(StringBuilder sb, MapValue fromVal, IMap tsMap)
    {
        if (tsMap.IsEmpty)
        {
            sb.Append("from index '");
            sb.Append(fromVal.AsString());
            sb.Append("' ");
            return;
        }

        sb.Append("from index '");
        sb.Append(fromVal.AsString());
        sb.Append("' timeseries(");
        sb.Append(tsMap.Get("Name").AsString());
        sb.Append(") ");
    }
    
    private static void BuildWhereAndSpatial(StringBuilder sb, IMap queryMap, Dictionary<string, object> parameters)
    {
        var whereMap = queryMap.Get("Where").AsMap();
        var spatialMap = queryMap.Get("Spatial").AsMap();
        
        var hasWhere = !whereMap.IsEmpty;
        var hasSpatial = !spatialMap.IsEmpty;

        if (!hasWhere && !hasSpatial)
            return;

        sb.Append("where ");
        
        if (hasWhere)
            BuildWhere(sb, whereMap, parameters);

        if (hasSpatial)
        {
            if (hasWhere)
                sb.Append(" and ");
            BuildSpatial(sb, spatialMap);
        }
        sb.Append(' ');
    }

    private static void BuildSpatial(StringBuilder sb, IMap spatialMap)
    {
        var field = NormalizePath(spatialMap.Get("Field").AsString());
        var op = spatialMap.Keys().FirstOrDefault(k => k.StartsWith('$'));
        
        if (op != "$within")
            return;

        var circle = spatialMap.Get("$within").AsMap().Get("Circle").AsMap();
        var lat = circle.Get("Latitude").AsValue<double>();
        var lng = circle.Get("Longitude").AsValue<double>();
        var radius = circle.Get("Radius").AsValue<double>();

        sb.Append($"spatial.within({field}, spatial.circle({lat}, {lng}, {radius}))");
    }

    private static void BuildGroupByAndFacets(StringBuilder sb, IMap queryMap)
    {
        var groupBy = queryMap.Get("GroupBy").AsMap();
        var facets = queryMap.Get("Facets").AsMap();

        if (!groupBy.IsEmpty)
        {
            sb.Append("group by ");
            var first = true;
            foreach (var key in groupBy.Keys())
            {
                if (!first)
                    sb.Append(", ");
                first = false;
                sb.Append(NormalizePath(key));
            }
            sb.Append(' ');
            return;
        }

        if (!facets.IsEmpty)
        {
            // Placeholder for facets
        }
    }

    private static void BuildLimit(StringBuilder sb, MapValue skipVal, MapValue takeVal)
    {
        if (skipVal.IsEmpty && takeVal.IsEmpty)
            return;

        var skip = skipVal.IsEmpty ? 0 : skipVal.AsInt();
        var take = takeVal.IsEmpty ? int.MaxValue : takeVal.AsInt();
        sb.Append($"limit {skip}, {take}");
    }

    private static void BuildInclude(StringBuilder sb, IMap includeMap)
    {
        foreach (var key in includeMap.Keys())
        {
            sb.Append("include ");
            sb.Append(NormalizePath(key));
            sb.Append(' ');
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
            var val = whereMap.Get(key);

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
        var orderMap = blockMap.Get("Order").AsMap();
        var dataMap = blockMap.Get("Data").AsMap();
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
            var condKey = orderMap.Get(i.ToIndexString()).AsString();
            var condMap = dataMap.Get(condKey).AsMap();
            
            BuildWhere(sb, condMap, parameters);
        }
        sb.Append(')');
    }

    private static void BuildOperatorExpression(StringBuilder sb, string field, IMap opMap, Dictionary<string, object> parameters)
    {
        foreach (var opKey in opMap.Keys())
        {
            var val = opMap.Get(opKey);

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
                    var fromVal = val.AsMap().Get("$from");
                    var toVal = val.AsMap().Get("$to");
                    var incFrom = val.AsMap().Get("$includeFrom").IsEmpty || val.AsMap().Get("$includeFrom").AsValue<bool>();
                    var incTo = val.AsMap().Get("$includeTo").IsEmpty || val.AsMap().Get("$includeTo").AsValue<bool>();

                    AppendConstraint(sb, field, incFrom ? ">=" : ">", fromVal, parameters);
                    sb.Append(" and ");
                    AppendConstraint(sb, field, incTo ? "<=" : "<", toVal, parameters);
                    break;
                case "$in":
                case "$all":
                    sb.Append(field);
                    sb.Append(opKey == "$in" ? " in (" : " all in (");
                    var first = true;
                    foreach (var itemKey in val.AsMap().Keys())
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
                    var term = val.AsMap().Get("$term").AsString();
                    var options = val.AsMap().Get("$options").AsMap();
                    var searchOp = options.Get("Operator").IsEmpty ? "OR" : options.Get("Operator").AsString();
                    var boost = options.Get("Boost");

                    var pName1 = "p" + parameters.Count.ToIndexString();
                    parameters.Add(pName1, term);

                    sb.Append($"search({field}, ${pName1}");
                    if (searchOp == "AND")
                        sb.Append(", AND");
                    sb.Append(')');

                    if (!boost.IsEmpty)
                    {
                        sb.Append(" boost ");
                        sb.Append(boost.AsValue<double>().ToString(System.Globalization.CultureInfo.InvariantCulture));
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

        if (val.IsMap && !val.AsMap().Get("$field").IsEmpty)
        {
            sb.Append(NormalizePath(val.AsMap().Get("$field").AsString()));
            return;
        }

        var pName = "p" + parameters.Count.ToIndexString();
        parameters.Add(pName, val.AsValue() ?? DBNull.Value);
        sb.Append('$');
        sb.Append(pName);
    }

    private static void BuildOrderBy(StringBuilder sb, IMap orderByMap)
    {
        var orderMap = orderByMap.Get("Order").AsMap();
        var dataMap = orderByMap.Get("Data").AsMap();
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
            var fieldKey = orderMap.Get(i.ToIndexString()).AsString();
            var direction = dataMap.Get(fieldKey).AsString();
            
            sb.Append(NormalizePath(fieldKey));
            if (string.Equals(direction, "Desc", StringComparison.OrdinalIgnoreCase))
                sb.Append(" desc");
        }
        sb.Append(' ');
    }

    private static void BuildSelect(StringBuilder sb, MapValue selectVal, IMap reduceMap)
    {
        sb.Append("select ");

        if (selectVal.IsMap && !selectVal.AsMap().Get("$js").IsEmpty)
        {
            sb.Append('{');
            sb.Append(selectVal.AsMap().Get("$js").AsString());
            sb.Append('}');
            sb.Append(' ');
            return;
        }

        if (!reduceMap.IsEmpty)
        {
            var first = true;
            foreach (var key in reduceMap.Keys())
            {
                if (!first)
                    sb.Append(", ");
                first = false;
                var funcMap = reduceMap.Get(key).AsMap();
                BuildReduceFunction(sb, key, funcMap);
            }
            sb.Append(' ');
            return;
        }

        if (selectVal.IsMap)
        {
            var first = true;
            foreach (var key in selectVal.AsMap().Keys())
            {
                if (!first)
                    sb.Append(", ");
                first = false;
                sb.Append(NormalizePath(key));
            }
            sb.Append(' ');
        }
    }

    private static void BuildReduceFunction(StringBuilder sb, string alias, IMap funcMap)
    {
        foreach (var opKey in funcMap.Keys())
        {
            var field = NormalizePath(funcMap.Get(opKey).AsString());
            var segment = opKey switch
            {
                "$sum" => $"sum({field}) as {alias}",
                "$avg" => $"avg({field}) as {alias}",
                "$min" => $"min({field}) as {alias}",
                "$max" => $"max({field}) as {alias}",
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
        return path.Replace('/', '.');
    }
}
