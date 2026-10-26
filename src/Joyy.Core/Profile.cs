namespace Joyy.Core;

/// <summary>User-tunable settings. Immutable; build a new <see cref="Mapper"/> when it changes.</summary>
public sealed record Profile
{
    public string Name { get; init; } = "Default";

    /// <summary>Foreground process name (without .exe) this profile applies to; null for the global profile.</summary>
    public string? ProcessName { get; init; }

    /// <summary>Radial stick deadzone as a fraction of full deflection (0 .. 0.9).</summary>
    public double StickDeadzone { get; init; } = 0.08;

    /// <summary>Deflection at which output reaches 100% (deadzone .. 1].</summary>
    public double StickOuterSaturation { get; init; } = 1.0;

    /// <summary>Trigger travel ignored at rest, as a fraction (0 .. 0.9). Absorbs idle drift.</summary>
    public double TriggerThreshold { get; init; } = 0.02;

    /// <summary>Exponent applied to trigger travel after the threshold; 1 = linear (0.1 .. 10).</summary>
    public double TriggerCurve { get; init; } = 1.0;

    public bool InvertLeftX { get; init; }
    public bool InvertLeftY { get; init; }
    public bool InvertRightX { get; init; }
    public bool InvertRightY { get; init; }

    /// <summary>Multiplier for rumble forwarded to the DualSense (0 .. 2).</summary>
    public double RumbleScale { get; init; } = 1.0;

    /// <summary>Swap A/B and X/Y (Cross = B, Circle = A, Square = Y, Triangle = X).</summary>
    public bool NintendoLayout { get; init; }

    public IReadOnlyDictionary<PhysicalButton, X360Buttons> ButtonOverrides { get; init; } =
        new Dictionary<PhysicalButton, X360Buttons>();

    /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> if any value is outside its documented range.</summary>
    public void Validate()
    {
        Check(StickDeadzone >= 0 && StickDeadzone <= 0.9, nameof(StickDeadzone), StickDeadzone);
        Check(StickOuterSaturation > StickDeadzone && StickOuterSaturation <= 1.0,
            nameof(StickOuterSaturation), StickOuterSaturation);
        Check(TriggerThreshold >= 0 && TriggerThreshold <= 0.9, nameof(TriggerThreshold), TriggerThreshold);
        Check(TriggerCurve >= 0.1 && TriggerCurve <= 10, nameof(TriggerCurve), TriggerCurve);
        Check(RumbleScale >= 0 && RumbleScale <= 2, nameof(RumbleScale), RumbleScale);
    }

    static void Check(bool ok, string name, double value)
    {
        // Comparisons above are false for NaN, so NaN is rejected too.
        if (!ok)
            throw new ArgumentOutOfRangeException(name, value, $"Profile value {name} is out of range.");
    }
}
