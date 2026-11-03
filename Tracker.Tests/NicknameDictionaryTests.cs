using Tracker.Search;

namespace Tracker.Tests;

public class NicknameDictionaryTests
{
    [Fact]
    public void Expand_NicknameWithSurname_ReplacesNickname()
    {
        Assert.Contains("димитър стоянов", NicknameDictionary.Expand("Митко Стоянов"));
    }

    [Fact]
    public void Expand_NoNickname_ReturnsEmpty()
    {
        Assert.Empty(NicknameDictionary.Expand("Иван Петров"));
    }

    [Fact]
    public void Expand_AmbiguousNickname_ReturnsEveryOfficialName()
    {
        var expanded = NicknameDictionary.Expand("Ники");

        Assert.Contains("николай", expanded);
        Assert.Contains("никола", expanded);
    }

    [Fact]
    public void Expand_Latin_IsTransliteratedFirst()
    {
        Assert.Contains("димитър", NicknameDictionary.Expand("MITKO"));
    }

    [Fact]
    public void Expand_FreeText_KeepsOtherWords()
    {
        Assert.Contains("аз съм тодор", NicknameDictionary.Expand("аз съм Тошко"));
    }

    [Theory]
    [InlineData("Ваньо", "иван")]
    [InlineData("Гошо", "георги")]
    [InlineData("Пешо", "петър")]
    [InlineData("Сашо", "александър")]
    [InlineData("Мими", "мария")]
    [InlineData("Деси", "десислава")]
    public void Expand_CommonNicknames(string nickname, string official)
    {
        Assert.Contains(official, NicknameDictionary.Expand(nickname));
    }

    [Fact]
    public void Expand_ManyAmbiguousWords_IsCapped()
    {
        Assert.True(NicknameDictionary.Expand("ники ники ники ники ники").Count
            <= NicknameDictionary.MaxExpansions);
    }
}
