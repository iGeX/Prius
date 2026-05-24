namespace Prius.Data.RavenDB.Tests;

using Xunit;
using Core.Maps;

public class RqlBuilderTests
{
    [Fact]
    public void Should_Build_Standard_Query_With_Quoted_Fields_And_Parameters()
    {
        // Arrange
        var queryMap = JsonReaderMap.From($$"""
        {
            "From": "Users",
            "Where": {
                "Status": "Active",
                "Profile-Data/Age": { "$gt": 21 }
            },
            "Take": 50
        }
        """);

        // Act
        var (rql, parameters) = RqlBuilder.Build(queryMap);

        // Assert
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
        var queryMap = JsonReaderMap.From($$"""
        {
            "From": "Orders",
            "Where": {
                "$or": {
                    "Order": { "0": "c1", "1": "c2" },
                    "Data": {
                        "c1": { "Total": { "$between": { "$from": 10, "$to": 100 } } },
                        "c2": { "IsSpecial": true }
                    }
                }
            }
        }
        """);

        // Act
        var (rql, parameters) = RqlBuilder.Build(queryMap);

        // Assert
        Assert.Equal("from index 'Orders' where ('Total' >= $p0 and 'Total' <= $p1 or 'IsSpecial' = $p2) limit $p3, $p4", rql);
        Assert.Equal(10L, parameters["p0"]);
        Assert.Equal(100L, parameters["p1"]);
        Assert.True((bool)parameters["p2"]);
    }

    [Fact]
    public void Should_Build_Valid_Facet_Syntax_Without_Aggregation_Duplication()
    {
        // Arrange
        var queryMap = JsonReaderMap.From($$"""
        {
            "From": "Products",
            "Facets": {
                "CategoryCount": {
                    "Function": "count", 
                    "Field": "Category-Id"
                }
            }
        }
        """);

        // Act
        var (rql, _) = RqlBuilder.Build(queryMap);

        // Assert
        Assert.Equal("from index 'Products' select facet('Category-Id') limit $p0, $p1", rql);
    }

    [Fact]
    public void Should_Build_Search_Operator_With_And_And_Boost()
    {
        // Arrange
        var queryMap = JsonReaderMap.From($$"""
        {
            "From": "Products",
            "Where": {
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
        }
        """);

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
        var queryMap = JsonReaderMap.From($$"""
        {
            "From": "Orders",
            "Where": {
                "UpdatedAt": {
                    "$eq": { "$field": "CreatedAt" }
                }
            }
        }
        """);

        // Act
        var (rql, parameters) = RqlBuilder.Build(queryMap);

        // Assert
        Assert.Equal("from index 'Orders' where 'UpdatedAt' = 'CreatedAt' limit $p0, $p1", rql);
        Assert.False(parameters.ContainsKey("p2"));
    }
    
    [Fact]
    public void Should_Handle_Empty_In_Operator_Safely()
    {
        // Arrange
        var queryMap = JsonReaderMap.From($$"""
        {
            "From": "Users",
            "Where": {
                "Id": {
                    "$in": {}
                }
            }
        }
        """);

        // Act
        var (rql, _) = RqlBuilder.Build(queryMap);

        // Assert
        Assert.Equal("from index 'Users' where id() == null limit $p0, $p1", rql);
    }
    
    [Fact]
    public void Should_Build_Native_JavaScript_Filters_And_Projections()
    {
        // Arrange
        var queryMap = JsonReaderMap.From($$"""
        {
            "From": "Employees",
            "Where": {
                "$js": "this.Age > 30"
            },
            "Select": {
                "$js": "FullName: this.FirstName + ' ' + this.LastName"
            }
        }
        """);

        // Act
        var (rql, _) = RqlBuilder.Build(queryMap);

        // Assert
        Assert.Equal("from index 'Employees' where javascript(this.Age > 30) select {FullName: this.FirstName + ' ' + this.LastName} limit $p0, $p1", rql);
    }
    
    [Fact]
    public void Should_Escape_Quotes_In_TimeSeries_Name()
    {
        // Arrange
        var queryMap = JsonReaderMap.From($$"""
        {
            "From": "Metrics",
            "TimeSeries": {
                "Name": "User's-HeartRate"
            }
        }
        """);

        // Act
        var (rql, _) = RqlBuilder.Build(queryMap);

        // Assert
        Assert.Equal("from index 'Metrics' timeseries('User''s-HeartRate') limit $p0, $p1", rql);
    }
    
    [Fact]
    public void Should_Build_Metadata_Filters_And_Wildcard_Search()
    {
        // Arrange
        var queryMap = JsonReaderMap.From($$"""
        {
            "From": "Documents",
            "Where": {
                "@metadata/last-modified": { "$gt": "2026-01-01" },
                "Title": {
                    "$search": {
                        "$term": "Raven",
                        "$options": { "Wildcard": true }
                    }
                }
            }
        }
        """);

        // Act
        var (rql, parameters) = RqlBuilder.Build(queryMap);

        // Assert
        Assert.Equal("from index 'Documents' where metadata(this)['last-modified'] > $p0 and search('Title', $p1) limit $p2, $p3", rql);
        Assert.Equal("2026-01-01", parameters["p0"]);
        Assert.Equal("Raven*", parameters["p1"]);
    }

    [Fact]
    public void Should_Build_Spatial_Wkt_And_Distance_Sorting()
    {
        // Arrange
        var queryMap = JsonReaderMap.From($$"""
        {
            "From": "Stores",
            "Spatial": {
                "Field": "Coordinates",
                "$within": {
                    "Wkt": "POLYGON((0 0, 0 10, 10 10, 10 0, 0 0))",
                    "Circle": {
                        "Latitude": 55.7,
                        "Longitude": 37.6
                    }
                }
            },
            "OrderBy": {
                "Order": { "0": "$spatialDistance" },
                "Data": { "$spatialDistance": "Asc" }
            }
        }
        """);

        // Act
        var (rql, parameters) = RqlBuilder.Build(queryMap);

        // Assert
        Assert.Equal("from index 'Stores' where spatial.within('Coordinates', spatial.wkt($p0)) order by spatial.distance('Coordinates', spatial.point(55.7, 37.6)) limit $p1, $p2", rql);
        Assert.Equal("POLYGON((0 0, 0 10, 10 10, 10 0, 0 0))", parameters["p0"]);
    }

    [Fact]
    public void Should_Build_Server_Side_Select_Load()
    {
        // Arrange
        var queryMap = JsonReaderMap.From($$"""
        {
            "From": "Orders",
            "Select": {
                "CompanyName": {
                    "$load": {
                        "Field": "CompanyId",
                        "Path": "Name"
                    }
                }
            }
        }
        """);

        // Act
        var (rql, _) = RqlBuilder.Build(queryMap);

        // Assert
        Assert.Equal("from index 'Orders' select load('CompanyId').'Name' as 'CompanyName' limit $p0, $p1", rql);
    }
    
    [Fact]
    public void Should_Build_Server_Side_Select_Load_And_Highlighting()
    {
        // Arrange
        var queryMap = JsonReaderMap.From($$"""
        {
            "From": "Orders",
            "Select": {
                "CompanyName": {
                    "$load": {
                        "Field": "CompanyId",
                        "Path": "Name"
                    }
                }
            },
            "Highlight": { "Field": "Notes" }
        }
        """);

        // Act
        var (rql, _) = RqlBuilder.Build(queryMap);

        // Assert
        Assert.Equal("from index 'Orders' include highlight('Notes', 128, 5) select load('CompanyId').'Name' as 'CompanyName' limit $p0, $p1", rql);
    }
    
    [Fact]
    public void Should_Build_Query_With_Standard_Includes_Enclosed_In_Quotes()
    {
        // Arrange
        var queryMap = JsonReaderMap.From($$"""
        {
            "From": "Orders",
            "Where": { "Status": "Shipped" },
            "Include": { "CompanyId": {} }
        }
        """);

        // Act
        var (rql, parameters) = RqlBuilder.Build(queryMap);

        // Assert
        Assert.Equal("from index 'Orders' where 'Status' = $p0 include 'CompanyId' limit $p1, $p2", rql);
        Assert.Equal("Shipped", parameters["p0"]);
    }
    
    [Fact]
    public void Should_Build_Metadata_Lookups_And_Exists_And_Null_Operators()
    {
        // Arrange
        var queryMap = JsonReaderMap.From($$"""
        {
            "From": "Users",
            "Where": {
                "@metadata/last-modified": { "$gt": "2026-01-01" },
                "DeletedAt": { "$null": true },
                "ActivationCode": { "$exists": false }
            }
        }
        """);

        // Act
        var (rql, parameters) = RqlBuilder.Build(queryMap);

        // Assert
        Assert.Equal("from index 'Users' where metadata(this)['last-modified'] > $p0 and 'DeletedAt' = null and exists('ActivationCode') = false limit $p1, $p2", rql);
        Assert.Equal("2026-01-01", parameters["p0"]);
    }

    [Fact]
    public void Should_Build_In_And_All_Operators_With_Arrays()
    {
        // Arrange
        var queryMap = JsonReaderMap.From($$"""
        {
            "From": "Products",
            "Where": {
                "Status": {
                    "$in": { "Active": true, "Pending": true }
                },
                "Tags": {
                    "$all": { "Premium": true, "Featured": true }
                }
            }
        }
        """);

        // Act
        var (rql, parameters) = RqlBuilder.Build(queryMap);

        // Assert
        Assert.Equal("from index 'Products' where 'Status' in ($p0, $p1) and 'Tags' all in ($p2, $p3) limit $p4, $p5", rql);
        Assert.True(parameters.ContainsKey("p0"));
        Assert.True(parameters.ContainsKey("p1"));
        Assert.True(parameters.ContainsKey("p2"));
        Assert.True(parameters.ContainsKey("p3"));
    }

    [Fact]
    public void Should_Build_Search_With_Automatic_Wildcard_Suffix()
    {
        // Arrange
        var queryMap = JsonReaderMap.From($$"""
        {
            "From": "Docs",
            "Where": {
                "Title": {
                    "$search": {
                        "$term": "Raven",
                        "$options": { "Wildcard": true }
                    }
                }
            }
        }
        """);

        // Act
        var (rql, parameters) = RqlBuilder.Build(queryMap);

        // Assert
        Assert.Equal("from index 'Docs' where search('Title', $p0) limit $p1, $p2", rql);
        Assert.Equal("Raven*", parameters["p0"]);
    }

}
