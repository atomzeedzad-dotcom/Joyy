namespace Joyy.Core;

/// <summary>
/// Converts DualSense state to an Xbox 360 report for one profile. Immutable and
/// allocation-free per call, so the input thread can swap instances on profile change.
/// </summary>
public sealed class Mapper
{
    readonly ButtonMap _buttons;

    public Mapper(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        Profile = profile;
        _buttons = ButtonMap.Create(profile.NintendoLayout, profile.ButtonOverrides);
    }

    public Profile Profile { get; }

    public ButtonMap Buttons => _buttons;

    public X360Report Map(in PadState state)
    {
        var p = Profile;

        var (lx, ly) = AnalogMath.ApplyRadialDeadzone(state.LeftX, state.LeftY, p.StickDeadzone, p.StickOuterSaturation);
        var (rx, ry) = AnalogMath.ApplyRadialDeadzone(state.RightX, state.RightY, p.StickDeadzone, p.StickOuterSaturation);

        if (p.InvertLeftX) lx = AnalogMath.Invert(lx);
        if (p.InvertLeftY) ly = AnalogMath.Invert(ly);
        if (p.InvertRightX) rx = AnalogMath.Invert(rx);
        if (p.InvertRightY) ry = AnalogMath.Invert(ry);

        return new X360Report(
            _buttons.Resolve(state.Buttons),
            AnalogMath.ApplyTrigger(state.L2, p.TriggerThreshold, p.TriggerCurve),
            AnalogMath.ApplyTrigger(state.R2, p.TriggerThreshold, p.TriggerCurve),
            lx, ly, rx, ry);
    }
}
