namespace Prius.Data.RavenDB;

using System;
using System.Collections.Generic;
using System.Text;
using Core.Maps;

public static class RqlBuilder
{
    /// <summary>
    /// Компилирует QueryMap в нативную RQL строку и словарь параметров.
    /// </summary>
    public static (string Rql, Dictionary<string, object> Parameters) Build(IMap? queryMap)
    {
        if (queryMap == null || queryMap.IsEmpty)
            return (string.Empty, []);

        var sb = new StringBuilder();
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);

        // 1. Секция FROM
        var fromVal = queryMap.Get("From");
        if (fromVal.IsEmpty)
            return (string.Empty, []);

        sb.Append("from ");
        sb.Append(fromVal.AsString());
        sb.Append(' ');

        // 2. Секция INCLUDE
        var includeMap = queryMap.Get("Include").AsMap();
        if (!includeMap.IsEmpty)
            BuildInclude(sb, includeMap);

        // 3. Секция WHERE
        var whereMap = queryMap.Get("Where").AsMap();
        if (!whereMap.IsEmpty)
        {
            sb.Append("where ");
            BuildWhere(sb, whereMap, parameters);
            sb.Append(' ');
        }

        // 4. Секция GROUP BY
        var groupByMap = queryMap.Get("GroupBy").AsMap();
        if (!groupByMap.IsEmpty)
            BuildGroupBy(sb, groupByMap);

        // 5. Секция ORDER BY
        var orderByMap = queryMap.Get("OrderBy").AsMap();
        if (!orderByMap.IsEmpty)
            BuildOrderBy(sb, orderByMap);

        // 6. Секция SELECT / REDUCE
        var selectVal = queryMap.Get("Select");
        if (!selectVal.IsEmpty)
            BuildSelect(sb, selectVal, queryMap.Get("Reduce").AsMap());

        // 7. Секция PAGINATION (Skip, Take)
        var skipVal = queryMap.Get("Skip");
        var takeVal = queryMap.Get("Take");
        if (!skipVal.IsEmpty || !takeVal.IsEmpty)
        {
            var skip = skipVal.IsEmpty ? 0 : skipVal.AsInt();
            var take = takeVal.IsEmpty ? int.MaxValue : takeVal.AsInt();
            sb.Append("limit ");
            sb.Append(skip);
            sb.Append(", ");
            sb.Append(take);
        }

        return (sb.ToString().TrimEnd(), parameters);
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

            // Обработка логических блоков первого уровня ($or, $and)
            if (key == "$or" || key == "$and")
            {
                BuildLogicalBlock(sb, key, val.AsMap(), parameters);
                continue;
            }

            // Обработка JavaScript фильтрации на сервере
            if (key == "$js")
            {
                sb.Append("javascript(");
                sb.Append(val.AsString());
                sb.Append(')');
                continue;
            }

            // Стандартное поле/путь
            var normalizedKey = NormalizePath(key);
            
            if (!val.IsMap)
            {
                // Простая проверка на равенство: Field = $p0
                AppendConstraint(sb, normalizedKey, "=", val, parameters);
                continue;
            }

            // Вложенная мапа оператора
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

        // Если это сравнение полей документа между собой через оператор $field
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

    private static void BuildGroupBy(StringBuilder sb, IMap groupByMap)
    {
        sb.Append("group by ");
        var first = true;
        foreach (var key in groupByMap.Keys())
        {
            if (!first)
                sb.Append(", ");
            first = false;
            sb.Append(NormalizePath(key));
        }
        sb.Append(' ');
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

        // Проверка на встроенный серверный JavaScript блок в проекции
        if (selectVal.IsMap && !selectVal.AsMap().Get("$js").IsEmpty)
        {
            sb.Append('{');
            sb.Append(selectVal.AsMap().Get("$js").AsString());
            sb.Append('}');
            sb.Append(' ');
            return;
        }

        // Если есть агрегации Reduce — они формируют блок select функций
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

        // Обычный плоский проекционный маппинг полей
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
            {
                sb.Append(segment);
            }
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        // Заменяем слэши на точки для RQL, но сохраняем синтаксис коллекций []
        return path.Replace('/', '.');
    }
}
