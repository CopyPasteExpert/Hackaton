using Tracker.Search;

namespace Tracker.Tests;

public class NameNormalizerTests
{
    [Theory]
    [InlineData("Ivan Petrov", "иван петров")]
    [InlineData("Zhivko", "живко")]
    [InlineData("Shtilianov", "щилианов")]
    [InlineData("Yana Tsoneva", "яна цонева")]
    [InlineData("Chavdar Shishkov", "чавдар шишков")]
    [InlineData("Yuliya", "юлия")]
    [InlineData("  Иван,   Петров! ", "иван петров")]
    [InlineData("Мария-Елена", "мария-елена")]
    [InlineData("ИВАН ПЕТРОВ", "иван петров")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Normalize_ReturnsExpected(string input, string expected)
    {
        Assert.Equal(expected, NameNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_MixedScripts()
    {
        Assert.Equal("иван петров", NameNormalizer.Normalize("Ivan Петров"));
    }
}
