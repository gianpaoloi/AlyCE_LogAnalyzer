using LogAnalyzer.Models;
using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

/// <summary>
/// The multi-select filter properties have to survive being set to null.
/// <para>
/// Radzen's clear (×) writes <c>default(TValue)</c> back through <c>@bind-Value</c>, and for
/// <c>IEnumerable&lt;string&gt;</c> that is null rather than an empty set. Explorer enumerated it
/// while building its filter and Live called <c>Contains</c> on it while rendering, so removing a
/// filter threw on the renderer and ended the Blazor circuit — after which nothing on the page
/// responded at all.
/// </para>
/// </summary>
public class SessionStateTests
{
    /// <summary>Every property a clearable multi-select dropdown is bound to, by page.</summary>
    private static readonly string[] Filters =
    {
        "ExplorerLevels", "ExplorerEnvironments", "ExplorerCompanies", "ExplorerColumns",
        "LiveLevels", "LiveEnvironments", "LiveCompanies", "LiveColumns",
    };

    public static TheoryData<string> MultiSelectFilters()
    {
        var data = new TheoryData<string>();
        foreach (var name in Filters) data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(MultiSelectFilters))]
    public void Clearing_a_filter_leaves_an_empty_set_rather_than_null(string property)
    {
        var state = new SessionState();
        var accessor = typeof(SessionState).GetProperty(property);
        Assert.NotNull(accessor);

        accessor.SetValue(state, null);

        var value = (IEnumerable<string>?)accessor.GetValue(state);
        Assert.NotNull(value);
        Assert.Empty(value);
    }

    /// <summary>
    /// Guards the list above: a new clearable multi-select added to a page has to be covered here
    /// too, and the count is the only thing a test can notice.
    /// </summary>
    [Fact]
    public void Every_string_sequence_property_is_covered_by_the_null_theory()
    {
        var sequences = typeof(SessionState).GetProperties()
            .Where(p => p.PropertyType == typeof(IEnumerable<string>))
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal);

        var covered = Filters.OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(covered, sequences);
    }

    [Fact]
    public void A_cleared_filter_is_still_usable_as_a_sequence()
    {
        // What Explorer.BuildFilter does, and what used to throw.
        var state = new SessionState { ExplorerLevels = null! };
        var filter = new LogFilter();

        foreach (var level in state.ExplorerLevels) filter.Levels.Add(level);

        Assert.Empty(filter.Levels);
    }

    [Fact]
    public void The_optional_columns_start_at_the_defaults()
    {
        Assert.Equal(LogColumns.DefaultKeys, new SessionState().ExplorerColumns);
        Assert.Equal(LogColumns.DefaultKeys, new SessionState().LiveColumns);
    }

    [Fact]
    public void A_real_selection_is_kept_as_given()
    {
        var chosen = new List<string> { "ERROR", "WARN" };

        var state = new SessionState { ExplorerLevels = chosen };

        Assert.Same(chosen, state.ExplorerLevels);
    }
}
