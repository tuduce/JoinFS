namespace JoinFS.Tests;

// --whazzupdelay sets how often whazzup.txt is written. A value that is not a finite number must never reach the timer: NaN or
// Infinity would make "ElapsedTime > updateTime" false for good and silently stop the feed.
public class WhazzupDelayTests
{
    [Theory]
    [InlineData("1", 1.0f)]
    [InlineData("2.5", 2.5f)]
    [InlineData("0.1", 0.1f)]
    [InlineData("3600", 3600.0f)]
    public void A_number_in_range_is_taken_as_it_is(string text, float expected)
    {
        Assert.True(Whazzup.TryParseDelay(text, out float seconds));
        Assert.Equal(expected, seconds);
    }

    [Theory]
    [InlineData("0", 0.1f)]
    [InlineData("0.01", 0.1f)]
    [InlineData("-5", 0.1f)]
    [InlineData("1000000000", 3600.0f)]
    [InlineData("7200", 3600.0f)]
    public void A_number_out_of_range_is_clamped(string text, float expected)
    {
        Assert.True(Whazzup.TryParseDelay(text, out float seconds));
        Assert.Equal(expected, seconds);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void Text_that_is_not_a_finite_number_is_rejected(string text)
    {
        Assert.False(Whazzup.TryParseDelay(text, out _));
    }

    [Fact]
    public void The_decimal_point_is_a_dot_whatever_the_culture()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.True(Whazzup.TryParseDelay("1.5", out float seconds));
            Assert.Equal(1.5f, seconds);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }
}
