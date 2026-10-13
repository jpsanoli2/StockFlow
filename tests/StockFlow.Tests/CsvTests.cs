using StockFlow.Web.Services;

namespace StockFlow.Tests;
public class CsvTests
{
    [Theory] [InlineData("=SUM(A1)")] [InlineData("+cmd")] [InlineData("-cmd")] [InlineData(" @formula")]
    public void Formula_is_neutralized(string input) => Assert.StartsWith("\"'", CsvExporter.Cell(input));
    [Fact] public void Quotes_are_escaped() => Assert.Equal("\"a\"\"b;c\"", CsvExporter.Cell("a\"b;c"));
}
