using System.Text.RegularExpressions;
using MoonBot.Core.Localization;

namespace MoonBot.Core.Tests;

public class LocalizerTests
{
    public static TheoryData<string> AllLanguages()
    {
        var data = new TheoryData<string>();
        foreach (var language in Localizer.Languages)
            data.Add(language.Code);
        return data;
    }

    [Fact]
    public void DefaultLanguage_IsEnglish()
    {
        Assert.Equal("en", Localizer.Languages[0].Code);
        Assert.Equal("en", Localizer.For("xx-unknown").Language.Code);
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryLanguage_TranslatesEveryEnglishKey(string code)
    {
        var english = Localizer.For("en");
        var other = Localizer.For(code);

        var missing = english.Keys.Where(k => !other.HasOwn(k)).ToList();

        Assert.True(missing.Count == 0, $"{code} is missing: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryLanguage_KeepsTheSamePlaceholders(string code)
    {
        var english = Localizer.For("en");
        var other = Localizer.For(code);

        foreach (var key in english.Keys)
        {
            Assert.True(Placeholders(english[key]).SetEquals(Placeholders(other[key])),
                $"{code}/{key} has different {{n}} placeholders");
        }
    }

    [Fact]
    public void UnknownKey_ReturnsTheKeyItself()
    {
        Assert.Equal("no.such.key", Localizer.For("es")["no.such.key"]);
    }

    [Fact]
    public void Format_FillsPlaceholders()
    {
        Assert.Equal("Hi, Ana. How can I help you today?", Localizer.For("en").Format("moon.greeting", "Ana"));
    }

    private static HashSet<string> Placeholders(string text)
        => Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).ToHashSet();
}
