using Supermonitor.Core.Settings;

namespace Supermonitor.Core.Dashboard;

public enum ThresholdLevel
{
    Normal,
    Warning,
    Critical,
}

public static class ThresholdEvaluator
{
    public static ThresholdLevel Evaluate(float value, MetricThreshold? threshold, bool lowerIsWorse = false)
    {
        if (threshold is null || !float.IsFinite(value))
        {
            return ThresholdLevel.Normal;
        }

        // Com lowerIsWorse, Warning >= Critical e o valor cai até cruzá-los.
        if (lowerIsWorse)
        {
            if (value <= threshold.Critical)
            {
                return ThresholdLevel.Critical;
            }

            return value <= threshold.Warning ? ThresholdLevel.Warning : ThresholdLevel.Normal;
        }

        if (value >= threshold.Critical)
        {
            return ThresholdLevel.Critical;
        }

        return value >= threshold.Warning ? ThresholdLevel.Warning : ThresholdLevel.Normal;
    }
}
