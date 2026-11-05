using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class GrafanaExploreLinkTests
{
    // The link the team copies from the Loki logs page (values as Grafana writes them).
    private const string TeamLink =
        "https://teamsystem.grafana.net/a/grafana-lokiexplore-app/explore/team/tse/logs?var-ds=bf9o2vkid4w00c" +
        "&from=now-7d&to=now&var-filters=team%7C%3D%7C__CV%CE%A9__tse,tse&var-filters=service_name%7C%3D%7Cwebapi-fin" +
        "&var-filters=environment%7C%3D%7Cdev&uel_pid=grafana-lokiexplore-app&timezone=browser&var-lineFormat=" +
        "&displayedFields=%5B%22cid%22,%22company%22,%22hostname%22,%22message%22%5D&sortOrder=%22Descending%22";

    // The same team link with a level, a cid field and a line filter applied (values as Grafana writes them).
    private const string FilteredLink =
        "https://teamsystem.grafana.net/a/grafana-lokiexplore-app/explore/team/tse/logs?var-ds=bf9o2vkid4w00c" +
        "&from=now-7d&to=now&var-filters=team%7C%3D%7C__CV%CE%A9__tse,tse" +
        "&var-filters=environment%7C%3D~%7C__CV%CE%A9__prod,prod" +
        "&var-filters=service_name%7C%3D%7C__CV%CE%A9__tse-webapi,tse-webapi" +
        "&var-fields=cid%7C%3D%7C%7B%22parser%22:%22json%22__gfc__%22value%22:%22axoteam%22%7D,axoteam" +
        "&var-levels=detected_level%7C%3D%7Cerror" +
        "&var-lineFilters=caseInsensitive,0%7C__gfp__%3D%7CTeamSystem.AlyCE.PolicyManager";

    [Fact]
    public void Reads_the_level_field_and_line_filters()
    {
        var link = GrafanaExploreLinkParser.TryParse(FilteredLink);

        Assert.Equal("prod", link!.Environment);
        Assert.Equal("tse-webapi", link.ServiceName);
        Assert.Equal(
            new[] { new LokiFieldMatch("detected_level", "error"), new LokiFieldMatch("cid", "axoteam") },
            link.Fields);
        Assert.Equal(new[] { new LokiLineFilter("TeamSystem.AlyCE.PolicyManager", true) }, link.LineFilters);
    }

    [Fact]
    public void Reads_a_cid_field_written_with_the_multi_value_prefix()
    {
        var link = GrafanaExploreLinkParser.TryParse(
            "https://teamsystem.grafana.net/a/grafana-lokiexplore-app/explore/team/tse/logs?var-ds=bf9o2vkid4w00c" +
            "&from=now-6h&to=now&var-fields=cid%7C%3D%7C__CV%CE%A9__%7B%22parser%22:%22mixed%22__gfc__%22value%22:%22geofondazioni%22%7D,geofondazioni" +
            "&var-levels=detected_level%7C%3D%7Cerror");

        Assert.Equal(
            new[] { new LokiFieldMatch("detected_level", "error"), new LokiFieldMatch("cid", "geofondazioni") },
            link!.Fields);
    }

    [Fact]
    public void A_link_without_those_pickers_has_no_extra_filters()
    {
        var link = GrafanaExploreLinkParser.TryParse(TeamLink);

        Assert.Empty(link!.Fields!);
        Assert.Empty(link.LineFilters!);
    }

    [Fact]
    public void Reads_the_team_service_environment_datasource_and_window()
    {
        var link = GrafanaExploreLinkParser.TryParse(TeamLink);

        Assert.NotNull(link);
        Assert.Equal("https://teamsystem.grafana.net", link.BaseUrl);
        Assert.Equal("bf9o2vkid4w00c", link.DatasourceUid);
        Assert.Equal("tse", link.Team);            // "__CVΩ__tse,tse" is one value, not two
        Assert.Equal("webapi-fin", link.ServiceName);
        Assert.Equal("dev", link.Environment);
        Assert.Equal(10080, link.WindowMinutes);   // now-7d
    }

    [Theory]
    [InlineData("now-1h", 60)]
    [InlineData("now-15m", 15)]
    [InlineData("now-2d", 2880)]
    [InlineData("now-1w", 10080)]
    public void Converts_relative_start_times_to_minutes(string from, int minutes)
    {
        var link = GrafanaExploreLinkParser.TryParse($"https://grafana.example/explore?var-ds=uid&from={from}");

        Assert.Equal(minutes, link!.WindowMinutes);
    }

    [Fact]
    public void An_absolute_start_time_gives_no_window()
    {
        var link = GrafanaExploreLinkParser.TryParse(
            "https://grafana.example/explore?var-ds=uid&from=1793873075000&to=1793880000000");

        Assert.Null(link!.WindowMinutes);
    }

    [Fact]
    public void Reads_an_absolute_range_in_epoch_milliseconds()
    {
        var link = GrafanaExploreLinkParser.TryParse(
            "https://grafana.example/explore?var-ds=uid&from=1793873075000&to=1793880000000");

        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1793873075000), link!.From);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1793880000000), link.To);
        Assert.Null(link.WindowMinutes);
    }

    [Fact]
    public void A_relative_range_has_no_absolute_dates()
    {
        var link = GrafanaExploreLinkParser.TryParse("https://grafana.example/explore?var-ds=uid&from=now-7d&to=now");

        Assert.Null(link!.From);
        Assert.Null(link.To);
    }

    [Fact]
    public void A_single_plain_filter_value_is_read_as_is()
    {
        var link = GrafanaExploreLinkParser.TryParse(
            "https://grafana.example/explore?var-filters=environment%7C%3D%7Cprod");

        Assert.Equal("prod", link!.Environment);
        Assert.Equal("", link.Team);
        Assert.Equal("", link.DatasourceUid);
    }

    [Theory]
    [InlineData("not a link")]
    [InlineData("ftp://grafana.example/explore?var-ds=uid")]
    [InlineData("https://grafana.example/dashboards")]
    public void Text_that_is_not_an_explore_link_is_rejected(string text)
    {
        Assert.Null(GrafanaExploreLinkParser.TryParse(text));
    }
}
