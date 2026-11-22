using Xunit;

namespace InventoryMovementTracker.Core.Tests;

public sealed class ScoringTests
{
    // p = 1.0 is clipped to 0.999 first. Clipped: 0.8/1, 0.3/0, 0.999/1.
    // Brier   = (0.2^2 + 0.3^2 + 0.001^2) / 3 = 0.130001 / 3
    // LogLoss = -(ln 0.8 + ln 0.7 + ln 0.999) / 3 = (0.2231435513 + 0.3566749439 + 0.0010005003) / 3
    private static readonly (double P, int Y)[] Three = [(0.8, 1), (0.3, 0), (1.0, 1)];

    [Fact]
    public void Brier_clips_before_scoring() =>
        Assert.Equal(0.130001 / 3, Scoring.Brier(Three), 9);

    [Fact]
    public void LogLoss_clips_before_scoring() =>
        Assert.Equal(0.5808189955 / 3, Scoring.LogLoss(Three), 9);

    [Fact]
    public void Reliability_puts_one_in_last_bin_and_judges_from_ten()
    {
        // Bin 2: nine pairs at 0.25, three of them observed -> not judged. Bin 9: p = 1.0 alone.
        var pairs = Enumerable.Range(0, 9).Select(i => (0.25, i < 3 ? 1 : 0)).Append((1.0, 1)).ToArray();

        var bins = Scoring.Reliability(pairs);

        Assert.Equal(10, bins.Count);
        Assert.Equal(new ReliabilityBin(9, 0.25, 1.0 / 3, false), bins[2]);
        Assert.Equal(new ReliabilityBin(1, 0.999, 1.0, false), bins[9]);
        Assert.Equal(new ReliabilityBin(0, 0, 0, false), bins[0]);
    }

    [Fact]
    public void Reliability_judges_a_bin_of_ten()
    {
        var bins = Scoring.Reliability(Enumerable.Repeat((0.55, 1), 10).ToArray());

        Assert.True(bins[5].Judged);
    }

    [Fact]
    public void Bootstrap_of_constant_difference_is_a_point()
    {
        var b = new[] { 0.2, 0.5, 0.1, 0.9 };
        var a = b.Select(x => x + 0.1).ToArray();

        var (low, high) = Scoring.PairedBootstrap(a, b);

        Assert.Equal(0.1, low, 9);
        Assert.Equal(0.1, high, 9);
    }

    [Fact]
    public void Bootstrap_of_identical_losses_is_zero()
    {
        var a = new[] { 0.2, 0.5, 0.1, 0.9 };

        var (low, high) = Scoring.PairedBootstrap(a, a);

        Assert.Equal((0.0, 0.0), (low, high));
    }

    [Fact]
    public void Bootstrap_is_reproducible_and_brackets_the_mean()
    {
        var a = new[] { 0.9, 0.1, 0.4, 0.8, 0.3, 0.7 };
        var b = new[] { 0.2, 0.3, 0.5, 0.1, 0.6, 0.2 };

        var first = Scoring.PairedBootstrap(a, b, 1000);

        Assert.Equal(first, Scoring.PairedBootstrap(a, b, 1000));
        Assert.True(first.Low < 0.25 && 0.25 < first.High); // mean difference is exactly 0.25
    }

    [Fact]
    public void Holdout_respects_time_order_even_when_input_is_shuffled()
    {
        var t0 = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var shuffled = new[] { 7, 2, 9, 0, 5, 3, 8, 1, 6, 4 };

        var (train, test) = Scoring.Holdout(shuffled, i => t0.AddDays(i), 0.8);

        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], train);
        Assert.Equal([8, 9], test);
    }
}
