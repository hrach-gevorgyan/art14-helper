using Xunit;

namespace Art14Helper.Tests;

/// <summary>Keyboard Auto: bucket choice and hysteresis (no hardware involved).</summary>
public class AutoBacklightTests
{
    private static byte LevelFor(double lux, int current = -1)
        => AutoBacklightService.Buckets[AutoBacklightService.Classify(lux, current)].Level;

    [Theory]
    [InlineData(0, 100)]     // pitch dark: on
    [InlineData(24, 100)]    // dim room: on
    [InlineData(120, 100)]   // lamp-lit: on (fresh decisions take the darker bucket)
    [InlineData(500, 0)]     // normal room light: off
    [InlineData(20000, 0)]   // daylight: off
    public void FreshDecisionPicksBucketForLight(double lux, byte expectedLevel)
        => Assert.Equal(expectedLevel, LevelFor(lux));

    [Fact]
    public void StaysInCurrentBucketInsideOverlap()
    {
        // At 130 lux both "on" (0-160) and "off" (100+) fit: no change either way.
        Assert.Equal(0, AutoBacklightService.Classify(130, current: 0));
        Assert.Equal(1, AutoBacklightService.Classify(130, current: 1));
    }

    [Fact]
    public void LeavesBucketOnlyWhenClearlyOutside()
    {
        // From "on" (0-160): 159 lux stays, 161 turns off.
        Assert.Equal(0, AutoBacklightService.Classify(159, current: 0));
        Assert.Equal(1, AutoBacklightService.Classify(161, current: 0));
        // Back down: must drop below 100 (the off bucket's lower bound) to turn on again.
        Assert.Equal(1, AutoBacklightService.Classify(105, current: 1));
        Assert.Equal(0, AutoBacklightService.Classify(95, current: 1));
    }

    [Fact]
    public void FlickerAroundABorderDoesNotFlap()
    {
        var bucket = AutoBacklightService.Classify(170, current: -1);   // off
        foreach (var lux in new[] { 150.0, 165, 140, 170, 130, 160 })
            bucket = AutoBacklightService.Classify(lux, bucket);
        Assert.Equal(1, bucket); // stayed off: never dropped below 100
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
