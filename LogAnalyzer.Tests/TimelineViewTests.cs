using LogAnalyzer.Models;
using Xunit;

namespace LogAnalyzer.Tests;

public class TimelineViewTests
{
    private static TimeBucket Hour(int hoursFromOrigin, int info = 1, int warn = 0, int error = 0,
                                   int debug = 0, int other = 0) =>
        new()
        {
            Start = new DateTime(2026, 7, 8, 0, 0, 0).AddHours(hoursFromOrigin),
            Info = info,
            Warn = warn,
            Error = error,
            Debug = debug,
            Other = other,
        };

    private static List<TimeBucket> Hourly(int count) =>
        Enumerable.Range(0, count).Select(i => Hour(i)).ToList();

    [Fact]
    public void An_empty_timeline_yields_no_buckets()
    {
        var view = TimelineView.Downsample(Array.Empty<TimeBucket>(), 180);

        Assert.Empty(view.Buckets);
        Assert.Equal(1, view.HoursPerBucket);
        Assert.Equal("per hour", view.Describe());
    }

    [Fact]
    public void A_timeline_that_already_fits_is_passed_through_untouched()
    {
        var hourly = Hourly(100);
        var view = TimelineView.Downsample(hourly, 180);

        Assert.Same(hourly, view.Buckets);
        Assert.Equal(1, view.HoursPerBucket);
        Assert.Equal("per hour", view.Describe());
    }

    [Theory]
    [InlineData(180, 180, 1)]
    [InlineData(360, 180, 2)]
    [InlineData(720, 180, 4)]      // a month
    [InlineData(8760, 180, 49)]    // a year
    public void Bucket_size_grows_so_the_count_stays_within_the_cap(int hours, int maxBuckets, int expectedHoursPerBucket)
    {
        var view = TimelineView.Downsample(Hourly(hours), maxBuckets);

        Assert.Equal(expectedHoursPerBucket, view.HoursPerBucket);
        Assert.True(view.Buckets.Count <= maxBuckets,
                    $"{view.Buckets.Count} buckets exceeds the cap of {maxBuckets}");
    }

    /// <summary>Downsampling must not lose or invent entries.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(721)]
    [InlineData(8760)]
    public void Totals_survive_downsampling(int hours)
    {
        var hourly = Enumerable.Range(0, hours)
            .Select(i => Hour(i, info: i % 3, warn: i % 5, error: i % 7, debug: i % 2, other: i % 11))
            .ToList();

        var view = TimelineView.Downsample(hourly, 180);

        Assert.Equal(hourly.Sum(b => b.Total), view.Buckets.Sum(b => b.Total));
        Assert.Equal(hourly.Sum(b => b.Info), view.Buckets.Sum(b => b.Info));
        Assert.Equal(hourly.Sum(b => b.Warn), view.Buckets.Sum(b => b.Warn));
        Assert.Equal(hourly.Sum(b => b.Error), view.Buckets.Sum(b => b.Error));
        Assert.Equal(hourly.Sum(b => b.Debug), view.Buckets.Sum(b => b.Debug));
        Assert.Equal(hourly.Sum(b => b.Other), view.Buckets.Sum(b => b.Other));
    }

    [Fact]
    public void Buckets_come_out_in_order_and_evenly_spaced()
    {
        var view = TimelineView.Downsample(Hourly(720), 180);

        var starts = view.Buckets.Select(b => b.Start).ToList();
        Assert.Equal(starts.OrderBy(t => t), starts);
        Assert.All(starts.Zip(starts.Skip(1)),
                   p => Assert.Equal(view.HoursPerBucket, (p.Second - p.First).TotalHours));
    }

    /// <summary>
    /// The timeline only contains hours that have entries, so gaps are normal — buckets must still
    /// be aligned to a fixed grid rather than to the position of an entry in the list.
    /// </summary>
    /// <summary>
    /// The timeline only contains hours that have entries, so gaps are normal. Buckets are aligned
    /// to a fixed grid measured from the first hour, not to positions in the list, so which bucket
    /// an hour lands in does not depend on how many hours before it happened to be empty.
    /// </summary>
    [Fact]
    public void Buckets_are_aligned_to_a_fixed_grid_across_gaps()
    {
        var origin = new DateTime(2026, 7, 8, 0, 0, 0);

        // Hour 0, then a long gap, then hours 499 and 500 — which share a bucket, because with a
        // 3-hour grid bucket 166 covers hours 498-500.
        var view = TimelineView.Downsample(new List<TimeBucket> { Hour(0), Hour(499), Hour(500) }, 180);

        Assert.Equal(3, view.HoursPerBucket);   // ceil(501 / 180)
        Assert.Equal(2, view.Buckets.Count);
        Assert.Equal(origin, view.Buckets[0].Start);
        Assert.Equal(origin.AddHours(498), view.Buckets[1].Start);
        Assert.Equal(1, view.Buckets[0].Total);
        Assert.Equal(2, view.Buckets[1].Total);
    }

    /// <summary>The flip side: two adjacent hours straddling a grid boundary stay separate.</summary>
    [Fact]
    public void Adjacent_hours_either_side_of_a_boundary_stay_in_separate_buckets()
    {
        var origin = new DateTime(2026, 7, 8, 0, 0, 0);
        var view = TimelineView.Downsample(new List<TimeBucket> { Hour(0), Hour(500), Hour(501) }, 180);

        Assert.Equal(3, view.HoursPerBucket);   // ceil(502 / 180)
        Assert.Equal(3, view.Buckets.Count);
        Assert.Equal(new[] { origin, origin.AddHours(498), origin.AddHours(501) },
                     view.Buckets.Select(b => b.Start));
    }

    [Theory]
    [InlineData(1, "per hour")]
    [InlineData(2, "per 2 hours")]
    [InlineData(6, "per 6 hours")]
    [InlineData(24, "per day")]
    [InlineData(48, "per 2 days")]
    public void Bucket_size_is_described_in_readable_units(int hoursPerBucket, string expected) =>
        Assert.Equal(expected, new TimelineView(Array.Empty<TimeBucket>(), hoursPerBucket).Describe());

    [Fact]
    public void Labels_drop_the_time_of_day_once_buckets_span_days()
    {
        var bucket = Hour(0);

        Assert.Equal("07-08 00:00", new TimelineView(Array.Empty<TimeBucket>(), 1).Label(bucket));
        Assert.Equal("07-08", new TimelineView(Array.Empty<TimeBucket>(), 24).Label(bucket));
    }

    [Fact]
    public void The_source_timeline_is_never_mutated()
    {
        var hourly = Hourly(720);
        var before = hourly.Select(b => b.Total).ToList();

        TimelineView.Downsample(hourly, 180);

        Assert.Equal(before, hourly.Select(b => b.Total));
    }
}
