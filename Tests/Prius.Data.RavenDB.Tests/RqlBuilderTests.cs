namespace Prius.Data.RavenDB.Tests;

using Xunit;
using System;
using Core.Maps;

public class RqlBuilderTests
{
    [Fact]
    public void Should_Build_Standard_Query_With_Quoted_Fields_And_Parameters()
    {
        // Arrange
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Users")),
            ("Where", DictionaryMap.New.With(
                ("Status", new MapValue("Active")),
                // Возвращаем плоский ключ. NormalizePath сам разобьет его по точке!
                ("Profile-Data/Age", DictionaryMap.New.With(("$gt", new MapValue(21L))).AsMapValue()) // Не забываем 21L
            ).AsMapValue()),
            ("Take", new MapValue(50))
        );

        // Act
        var (rql, parameters) = RqlBuilder.Build(queryMap);

        // Assert
        // Теперь без лишнего "select *" в конце
        Assert.Equal("from index 'Users' where 'Status' = $p0 and 'Profile-Data'.'Age' > $p1 limit $p2, $p3", rql);
        
        Assert.Equal("Active", parameters["p0"]);
        Assert.Equal(21L, parameters["p1"]);
        Assert.Equal(0, parameters["p2"]);  
        Assert.Equal(50, parameters["p3"]); 
    }

    [Fact]
    public void Should_Build_Logical_Blocks_And_Between_Operators()
    {
        // Arrange
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Orders")),
            ("Where", DictionaryMap.New.With(
                ("$or", DictionaryMap.New.With(
                    ("Order", DictionaryMap.New.With(("0", new MapValue("c1")), ("1", new MapValue("c2"))).AsMapValue()),
                    ("Data", DictionaryMap.New.With(
                        ("c1", DictionaryMap.New.With(("Total", DictionaryMap.New.With(("$between", DictionaryMap.New.With(("$from", new MapValue(10)), ("$to", new MapValue(100))).AsMapValue())).AsMapValue())).AsMapValue()),
                        ("c2", DictionaryMap.New.With(("IsSpecial", new MapValue(true))).AsMapValue())
                    ).AsMapValue())
                ).AsMapValue())
            ).AsMapValue())
        );

        // Act
        var (rql, parameters) = RqlBuilder.Build(queryMap);

        // Assert
        // Убран лишний "select *" перед limit
        Assert.Equal("from index 'Orders' where ('Total' >= $p0 and 'Total' <= $p1 or 'IsSpecial' = $p2) limit $p3, $p4", rql);
        Assert.Equal(10L, parameters["p0"]);
        Assert.Equal(100L, parameters["p1"]);
        Assert.True((bool)parameters["p2"]);
    }

    [Fact]
    public void Should_Build_Valid_Facet_Syntax_Without_Aggregation_Duplication()
    {
        // Arrange
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Products")),
            ("Facets", DictionaryMap.New.With(
                ("CategoryCount", DictionaryMap.New.With(
                    ("Function", new MapValue("count")), 
                    ("Field", new MapValue("Category-Id"))
                ).AsMapValue())
            ).AsMapValue())
        );

        // Act
        var (rql, _) = RqlBuilder.Build(queryMap);

        // Assert
        // Должен сгенерироваться ровно один "select facet(...)"
        Assert.Equal("from index 'Products' select facet('Category-Id') limit $p0, $p1", rql);
    }

    [Fact]
    public void Should_Throw_Exception_When_Reduce_Is_Used_Without_GroupBy()
    {
        // Arrange
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Sales")),
            ("Reduce", DictionaryMap.New.With(
                ("Revenue", DictionaryMap.New.With(("$sum", new MapValue("Price"))).AsMapValue())
            ).AsMapValue())
        );

        // Act & Assert
        // Проверяем нашу защиту уровня компиляции, которую мы добавили в BuildSelect
        var exception = Assert.Throws<InvalidOperationException>(() => RqlBuilder.Build(queryMap));
        Assert.Contains("Map-Reduce aggregations (Reduce) require a GroupBy clause", exception.Message);
    }
    
    [Fact]
    public void Should_Build_Search_Operator_With_And_And_Boost()
    {
        // Arrange
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Products")),
            ("Where", DictionaryMap.New.With(
                ("Description", DictionaryMap.New.With(
                    ("$search", DictionaryMap.New.With(
                        ("$term", new MapValue("RavenDB")),
                        ("$options", DictionaryMap.New.With(
                            ("Operator", new MapValue("AND")),
                            ("Boost", new MapValue(2.5M))
                        ).AsMapValue())
                    ).AsMapValue())
                ).AsMapValue())
            ).AsMapValue())
        );

        // Act
        var (rql, parameters) = RqlBuilder.Build(queryMap);

        // Assert
        Assert.Equal("from index 'Products' where search('Description', $p0, AND) boost 2.5 limit $p1, $p2", rql);
        Assert.Equal("RavenDB", parameters["p0"]);
    }
    
    [Fact]
    public void Should_Build_Field_To_Field_Comparison()
    {
        // Arrange
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Orders")),
            ("Where", DictionaryMap.New.With(
                ("UpdatedAt", DictionaryMap.New.With(
                    // Указываем оператор сравнения, а внутри него — модификатор поля
                    ("$eq", DictionaryMap.New.With(("$field", new MapValue("CreatedAt"))).AsMapValue())
                ).AsMapValue())
            ).AsMapValue())
        );

        // Act
        var (rql, parameters) = RqlBuilder.Build(queryMap);

        // Assert
        // Теперь выражение 'UpdatedAt' = 'CreatedAt' соберется идеально
        Assert.Equal("from index 'Orders' where 'UpdatedAt' = 'CreatedAt' limit $p0, $p1", rql);
        Assert.False(parameters.ContainsKey("p2")); // Убеждаемся, что лишних параметров нет
    }
    
    [Fact]
    public void Should_Handle_Empty_In_Operator_Safely()
    {
        // Arrange
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Users")),
            ("Where", DictionaryMap.New.With(
                ("Id", DictionaryMap.New.With(
                    ("$in", DictionaryMap.New.AsMapValue()) // Пустая мапа внутри $in
                ).AsMapValue())
            ).AsMapValue())
        );

        // Act
        var (rql, _) = RqlBuilder.Build(queryMap);

        // Assert
        // Вместо падения или 'Id' in () генерируется безопасное ложное условие
        Assert.Equal("from index 'Users' where id() == null limit $p0, $p1", rql);
    }
    
    [Fact]
    public void Should_Build_Native_JavaScript_Filters_And_Projections()
    {
        // Arrange
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Employees")),
            ("Where", DictionaryMap.New.With(
                ("$js", new MapValue("this.Age > 30"))
            ).AsMapValue()),
            ("Select", DictionaryMap.New.With(
                ("$js", new MapValue("FullName: this.FirstName + ' ' + this.LastName"))
            ).AsMapValue())
        );

        // Act
        var (rql, _) = RqlBuilder.Build(queryMap);

        // Assert
        Assert.Equal("from index 'Employees' where javascript(this.Age > 30) select {FullName: this.FirstName + ' ' + this.LastName} limit $p0, $p1", rql);
    }
    
    [Fact]
    public void Should_Escape_Quotes_In_TimeSeries_Name()
    {
        // Arrange
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Metrics")),
            ("TimeSeries", DictionaryMap.New.With(
                ("Name", new MapValue("User's-HeartRate"))
            ).AsMapValue())
        );

        // Act
        var (rql, _) = RqlBuilder.Build(queryMap);

        // Assert
        Assert.Equal("from index 'Metrics' timeseries('User''s-HeartRate') limit $p0, $p1", rql);
    }
}
