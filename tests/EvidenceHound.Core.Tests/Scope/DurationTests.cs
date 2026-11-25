using EvidenceHound.Core.Scope;

namespace EvidenceHound.Core.Tests.Scope;

/// <summary>s15-04: recovery objectives and escalation points as durations such as <c>4h</c> or <c>2d</c>, held in seconds by CISO Assistant.</summary>
public class DurationTests
{
    [Theory]
    [InlineData("30m", 1800)]
    [InlineData("4h", 14400)]
    [InlineData("2d", 172800)]
    [InlineData("1w", 604800)]
    public void Parses_hours_days_and_weeks(string text, int seconds)
    {
        Duration.TryParse(text, out var parsed).ShouldBeTrue();
        parsed.Seconds.ShouldBe(seconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("4")]
    [InlineData("h4")]
    [InlineData("0h")]
    [InlineData("-2d")]
    [InlineData("1.5h")]
    public void Rejects_anything_else(string text) => Duration.TryParse(text, out _).ShouldBeFalse();

    [Theory]
    [InlineData(1800, "30m")]
    [InlineData(14400, "4h")]
    [InlineData(90000, "25h")]
    [InlineData(172800, "2d")]
    [InlineData(1209600, "2w")]
    public void Prints_in_the_largest_whole_unit(int seconds, string text) => new Duration(seconds).ToString().ShouldBe(text);
}
