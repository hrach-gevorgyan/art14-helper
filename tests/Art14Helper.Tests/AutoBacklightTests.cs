using Xunit;

namespace Art14Helper.Tests;

/// <summary>Keyboard Auto: bucket choice and hysteresis (no hardware involved).</summary>
public class AutoBacklightTests
{
    private static byte LevelFor(double lux, int current = -1)
        => AutoBacklightService.Buckets[AutoBacklightService.Classify(lux, current)].Level;

    [Theory]
    [InlineData(0, 100)]     // pitch dark
    [InlineData(12, 100)]    // dark room
    [InlineData(40, 60)]     // dim
    [InlineData(120, 30)]    // lamp-lit
    [InlineData(500, 0)]     // normal room light
    [InlineData(20000, 0)]   // daylight
    public void FreshDecisionPicksBucketForLight(double lux, byte expectedLevel)
        => Assert.Equal(expectedLevel, LevelFor(lux));

    [Fact]
    public void StaysInCurrentBucketInsideOverlap()
    {
        // At 12 lux both "dark" (0-15) and "dim" (8-60) fit: no change either way.
        Assert.Equal(0, AutoBacklightService.Classify(12, current: 0));
        Assert.Equal(1, AutoBacklightService.Classify(12, current: 1));
    }

    [Fact]
    public void LeavesBucketOnlyWhenClearlyOutside()
    {
        // From "dim" (8-60): 59 lux stays, 61 moves to "lamp-lit".
        Assert.Equal(1, AutoBacklightService.Classify(59, current: 1));
        Assert.Equal(2, AutoBacklightService.Classify(61, current: 1));
        // Back down: must drop below 30 (lamp-lit lower bound) to return.
        Assert.Equal(2, AutoBacklightService.Classify(35, current: 2));
        Assert.Equal(1, AutoBacklightService.Classify(25, current: 2));
    }

    [Fact]
    public void FlickerAroundABorderDoesNotFlap()
    {
        var bucket = AutoBacklightService.Classify(14, current: -1);
        foreach (var lux in new[] { 16.0, 14, 17, 13, 18, 12 })
            bucket = AutoBacklightService.Classify(lux, bucket);
        Assert.Equal(1, bucket); // settled in "dim", not bouncing back to "dark"
    }

    [Fact]
    public void BucketsOverlapAndGetDimmerAsLightRises()
    {
        var buckets = AutoBacklightService.Buckets;
        for (var i = 1; i < buckets.Length; i++)
        {
            Assert.True(buckets[i].MinLux < buckets[i - 1].MaxLux, $"bucket {i} must overlap bucket {i - 1}");
            Assert.True(buckets[i].Level < buckets[i - 1].Level, $"bucket {i} must be dimmer than bucket {i - 1}");
        }
    }
}
