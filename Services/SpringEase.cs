using System.Windows;
using System.Windows.Media.Animation;

namespace DynamicIsland.Services;

/// <summary>
/// iOS-style spring curve for storyboard animations: quick start, a small overshoot, soft settle.
/// </summary>
public sealed class SpringEase : EasingFunctionBase
{
    /// <summary>0..1, lower = bouncier (iOS default feel ≈ 0.7).</summary>
    public double DampingRatio { get; set; } = 0.72;

    public SpringEase()
    {
        EasingMode = EasingMode.EaseIn; // EaseInCore is the whole curve
    }

    protected override double EaseInCore(double t)
    {
        const double w = 11; // natural frequency over the animation's normalised time
        double z = Math.Clamp(DampingRatio, 0.05, 0.99);
        double wd = w * Math.Sqrt(1 - z * z);
        double v = 1 - Math.Exp(-z * w * t) * (Math.Cos(wd * t) + z * w / wd * Math.Sin(wd * t));
        return t >= 1 ? 1 : v;
    }

    protected override Freezable CreateInstanceCore() => new SpringEase();
}
