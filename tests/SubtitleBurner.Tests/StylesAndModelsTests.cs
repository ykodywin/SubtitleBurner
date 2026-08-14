using SubtitleBurner;
using Xunit;

namespace SubtitleBurner.Tests;

public class SubtitleStylesTests
{
    [Fact]
    public void Resolve_CapCut_HasBoxBehindCurrentWord()
    {
        var preset = SubtitleStyles.Resolve("capcut");

        Assert.Equal("capcut", preset.Name);
        Assert.True(preset.UseBox);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("hormozi")]
    [InlineData("minimal")]
    [InlineData("neon")]
    [InlineData("capcut")]
    public void Resolve_KnownNames_RoundTrip(string name)
    {
        Assert.Equal(name, SubtitleStyles.Resolve(name).Name);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        Assert.Equal("hormozi", SubtitleStyles.Resolve("HORMOZI").Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-such-style")]
    public void Resolve_UnknownOrEmpty_FallsBackToDefault(string? name)
    {
        Assert.Equal("default", SubtitleStyles.Resolve(name).Name);
    }

    [Fact]
    public void Names_ContainsAllSixPresets()
    {
        Assert.Equal(
            new[] { "default", "hormozi", "minimal", "neon", "capcut", "opus" },
            SubtitleStyles.Names);
    }
}

public class ModelTests
{
    [Fact]
    public void SubtitleWord_SpeakerIsOptional()
    {
        var plain = new SubtitleWord(0.0, 0.5, "hello");
        var spoken = new SubtitleWord(0.5, 1.0, "world", "SPEAKER_01");

        Assert.Null(plain.Speaker);
        Assert.Equal("SPEAKER_01", spoken.Speaker);
    }

    [Fact]
    public void SubtitleStyle_Defaults_ArePresetWithNoOverrides()
    {
        var style = new SubtitleStyle();

        Assert.Equal("default", style.Preset);
        Assert.Null(style.FontName);
        Assert.Null(style.FontScale);
        Assert.Null(style.Color);
        Assert.Null(style.Outline);
        Assert.Null(style.Align);
        Assert.Null(style.KeywordHighlight);
        Assert.Null(style.KeywordColor);
        Assert.Null(style.KeywordEmoji);
    }

    [Fact]
    public void SubtitleOverlay_CarriesPngAndTimingWindow()
    {
        var overlay = new SubtitleOverlay("frame1.png", 1.0, 2.5);

        Assert.Equal("frame1.png", overlay.PngPath);
        Assert.Equal(1.0, overlay.Start);
        Assert.Equal(2.5, overlay.End);
    }
}
