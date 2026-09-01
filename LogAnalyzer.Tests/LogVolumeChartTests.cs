using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using LogAnalyzer.Components.Shared;
using Xunit;

namespace LogAnalyzer.Tests;

/// <summary>
/// The chart writes its geometry into inline CSS, so the numbers in it have to be formatted
/// invariantly. Under a comma-decimal culture the current culture produced <c>height:1,9%</c>,
/// which no browser accepts: the declaration was dropped, the bar segment fell back to its
/// <c>min-height: 1px</c>, and only segments whose percentage came out a whole number were drawn to
/// scale. A 1 590-entry bucket rendered 3px tall next to a 343-entry one at 20px — which read as a
/// broken y axis, when the axis was the only part that was right.
/// </summary>
public class LogVolumeChartTests
{
    // Reached by reflection rather than by widening the component's surface for a test. The null
    // check matters: a rename has to fail here loudly instead of quietly testing nothing.
    private static readonly MethodInfo PctMethod =
        typeof(LogVolumeChart).GetMethod("Pct", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "LogVolumeChart.Pct was renamed or removed; this test guards its formatting.");

    private static string Pct(double fraction) =>
        (string)PctMethod.Invoke(null, new object[] { fraction })!;

    /// <summary>A CSS percentage: digits, optionally a dot and more digits, then '%'.</summary>
    private static readonly Regex CssPercentage = new(@"^\d+(\.\d+)?%$", RegexOptions.Compiled);

    /// <summary>
    /// Fractions that do not land on a whole percentage — the ones that used to break. The last is
    /// a vertical gridline's offset with 144 buckets, which is why the x axis labels collapsed onto
    /// the left edge.
    /// </summary>
    public static TheoryData<string, double> CultureAndFraction()
    {
        var data = new TheoryData<string, double>();
        foreach (var culture in new[] { "it-IT", "de-DE", "fr-FR", "es-ES", "en-US", "" })
            foreach (var fraction in new[] { 38d / 2000, 25d / 2000, 1301d / 2000, 0.5 / 144, 1d / 3 })
                data.Add(culture, fraction);
        return data;
    }

    [Theory]
    [MemberData(nameof(CultureAndFraction))]
    public void A_percentage_is_valid_css_whatever_the_current_culture(string culture, double fraction)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture.Length == 0
                ? CultureInfo.InvariantCulture
                : new CultureInfo(culture);

            var css = Pct(fraction);

            Assert.DoesNotContain(",", css);
            Assert.Matches(CssPercentage, css);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("it-IT")]
    [InlineData("en-US")]
    public void The_percentage_is_the_same_string_in_every_culture(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("1.9%", Pct(38d / 2000));
            Assert.Equal("65.05%", Pct(1301d / 2000));
            Assert.Equal("14%", Pct(280d / 2000));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>A bar can never be drawn taller than the plot, nor at a negative height.</summary>
    [Theory]
    [InlineData(-0.5, "0%")]
    [InlineData(0, "0%")]
    [InlineData(1, "100%")]
    [InlineData(1.5, "100%")]
    public void The_percentage_is_clamped_to_the_plot(double fraction, string expected) =>
        Assert.Equal(expected, Pct(fraction));

    /// <summary>
    /// The five series the bars stack, sanity-checked against the level strings the parser emits —
    /// a level that maps to no series would be counted as "other" and silently mis-coloured.
    /// </summary>
    [Theory]
    [InlineData("ERROR")]
    [InlineData("WARN")]
    [InlineData("WARNING")]
    [InlineData("INFO")]
    [InlineData("DEBUG")]
    public void Every_known_level_maps_to_a_series(string level) =>
        Assert.False(string.IsNullOrEmpty(LogVolumeChart.SeriesKey(level)));
}
