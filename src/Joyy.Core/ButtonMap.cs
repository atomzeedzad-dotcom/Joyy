using System.Collections.ObjectModel;
using System.Numerics;

namespace Joyy.Core;

/// <summary>
/// Resolved physical-button to X360-button table. Built once per profile; lookups do not allocate.
/// </summary>
public sealed class ButtonMap
{
    /// <summary>Position-based default: the button in the same place on an Xbox pad.</summary>
    public static readonly IReadOnlyDictionary<PhysicalButton, X360Buttons> Default =
        new ReadOnlyDictionary<PhysicalButton, X360Buttons>(new Dictionary<PhysicalButton, X360Buttons>
        {
            [PhysicalButton.Cross] = X360Buttons.A,
            [PhysicalButton.Circle] = X360Buttons.B,
            [PhysicalButton.Square] = X360Buttons.X,
            [PhysicalButton.Triangle] = X360Buttons.Y,
            [PhysicalButton.L1] = X360Buttons.LeftShoulder,
            [PhysicalButton.R1] = X360Buttons.RightShoulder,
            [PhysicalButton.L3] = X360Buttons.LeftThumb,
            [PhysicalButton.R3] = X360Buttons.RightThumb,
            [PhysicalButton.Options] = X360Buttons.Start,
            [PhysicalButton.Create] = X360Buttons.Back,
            [PhysicalButton.PS] = X360Buttons.Guide,
            [PhysicalButton.Touchpad] = X360Buttons.Back,
            [PhysicalButton.Mute] = X360Buttons.None,
            [PhysicalButton.DpadUp] = X360Buttons.DpadUp,
            [PhysicalButton.DpadDown] = X360Buttons.DpadDown,
            [PhysicalButton.DpadLeft] = X360Buttons.DpadLeft,
            [PhysicalButton.DpadRight] = X360Buttons.DpadRight,
        });

    readonly X360Buttons[] _targets;

    ButtonMap(X360Buttons[] targets) => _targets = targets;

    /// <summary>
    /// Builds the table: default map, then the Nintendo A/B + X/Y swap if requested,
    /// then per-button overrides (overrides are taken literally and are not swapped).
    /// </summary>
    public static ButtonMap Create(bool nintendoLayout = false,
        IReadOnlyDictionary<PhysicalButton, X360Buttons>? overrides = null)
    {
        var targets = new X360Buttons[PhysicalButtons.Count];
        foreach (var (physical, target) in Default)
            targets[(int)physical] = nintendoLayout ? SwapNintendo(target) : target;

        if (overrides is not null)
        {
            foreach (var (physical, target) in overrides)
            {
                if ((int)physical >= targets.Length)
                    throw new ArgumentOutOfRangeException(nameof(overrides), physical, "Unknown physical button.");
                targets[(int)physical] = target;
            }
        }

        return new ButtonMap(targets);
    }

    public X360Buttons this[PhysicalButton button] => _targets[(int)button];

    /// <summary>ORs together the targets of every pressed physical button.</summary>
    public X360Buttons Resolve(uint physicalButtons)
    {
        X360Buttons result = X360Buttons.None;
        while (physicalButtons != 0)
        {
            int index = BitOperations.TrailingZeroCount(physicalButtons);
            if (index < _targets.Length)
                result |= _targets[index];
            physicalButtons &= physicalButtons - 1;
        }
        return result;
    }

    static X360Buttons SwapNintendo(X360Buttons target) => target switch
    {
        X360Buttons.A => X360Buttons.B,
        X360Buttons.B => X360Buttons.A,
        X360Buttons.X => X360Buttons.Y,
        X360Buttons.Y => X360Buttons.X,
        _ => target,
    };
}
