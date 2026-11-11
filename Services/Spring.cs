namespace DynamicIsland.Services;

/// <summary>
/// Critically-ish damped spring. Gives the island its soft, "liquid" morph:
/// it overshoots a touch and settles, instead of a stiff linear tween.
/// </summary>
public sealed class Spring
{
    public double Value;
    public double Velocity;
    public double Target;
    public double Stiffness = 300;
    public double Damping = 26;

    public Spring(double value)
    {
        Value = Target = value;
    }

    public bool IsSettled => Math.Abs(Target - Value) < 0.15 && Math.Abs(Velocity) < 0.5;

    public void Step(double dt)
    {
        // semi-implicit Euler in small sub-steps for stability
        int n = Math.Max(1, (int)Math.Ceiling(dt / 0.004));
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            double force = Stiffness * (Target - Value) - Damping * Velocity;
            Velocity += force * h;
            Value += Velocity * h;
        }
        if (IsSettled)
        {
            Value = Target;
            Velocity = 0;
        }
    }

    public void Snap(double value)
    {
        Value = Target = value;
        Velocity = 0;
    }
}
