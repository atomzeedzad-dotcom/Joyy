using System.Diagnostics;
using Joyy.Core;
using Joyy.Platform;

namespace Joyy.Cli;

/// <summary>
/// End-to-end check of everything after SDL: scripted DualSense states go through the Mapper and
/// the real ViGEmBus driver, and are read back through XInput exactly as a game reads them.
/// Also checks rumble from XInput back to the (scripted) DualSense.
/// </summary>
internal static class SelfTest
{
    sealed class ScriptedPad : IPhysicalPad
    {
        public PadState State;
        public (ushort Low, ushort High) Rumble;
        public PadInfo? Device => null;
#pragma warning disable CS0067
        public event Action<PadInfo>? Connected;
        public event Action<PadInfo>? Disconnected;
        public event Action<string>? Warning;
#pragma warning restore CS0067
        public bool Poll(out PadState state) { state = State; return true; }
        public void SetRumble(ushort low, ushort high) => Rumble = (low, high);
        public void Dispose() { }
    }

    public static int Run(bool withSdl = false)
    {
        // Diagnostic: bring SDL up in this process too (as `run` does) without using it for input.
        using var sdl = withSdl ? new SdlPad() : null;
        if (sdl is not null)
        {
            sdl.Poll(out _);
            Console.WriteLine($"SDL {SdlPad.NativeVersion} initialized in-process; DualSense open: {sdl.Device is not null}");
            sdlPump = () => sdl.Poll(out _);
        }

        var mapper = new Mapper(new Profile());
        var pad = new ScriptedPad();
        int failures = 0;

        var before = XInputProbe.ConnectedSlots().ToHashSet();
        Console.WriteLine($"XInput slots in use before test: {(before.Count == 0 ? "none" : string.Join(", ", before.Select(s => s + 1)))}");

        using var bridge = new Bridge(pad, () => new ViGEmPad(), mapper);
        bridge.Tick();
        if (bridge.Status != BridgeStatus.Running)
        {
            Console.WriteLine("FAIL  could not create the virtual Xbox 360 controller. Is ViGEmBus installed?");
            return 1;
        }

        int slot = WaitForSlot(bridge, before);
        if (slot < 0)
        {
            Console.WriteLine("FAIL  virtual controller did not appear in XInput.");
            return 1;
        }
        Console.WriteLine($"Virtual controller is XInput slot {slot + 1}.");
        Console.WriteLine();

        void Check(string name, PadState state)
        {
            pad.State = state;
            var expected = mapper.Map(state);
            X360Report? seen = null;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 500)
            {
                bridge.Tick();
                sdlPump?.Invoke();
                seen = XInputProbe.Read(slot);
                if (seen == expected)
                    break;
                Thread.Sleep(2);
            }
            bool ok = seen == expected;
            if (!ok)
                failures++;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name,-28} expected {Describe(expected)}" +
                (ok ? "" : $"\n      {"",-28} got      {(seen is null ? "nothing" : Describe(seen.Value))}"));
        }

        // First check on a freshly plugged pad: at rest, the game must see a centered controller.
        Check("Fresh pad at rest", PadState.Neutral);

        foreach (var b in Enum.GetValues<PhysicalButton>())
            Check($"{b} -> {mapper.Buttons[b]}", PadState.Neutral with { Buttons = PhysicalButtons.Bit(b) });

        Check("Cross + Circle + R1 + Up", PadState.Neutral with
        {
            Buttons = PhysicalButtons.Of(PhysicalButton.Cross, PhysicalButton.Circle, PhysicalButton.R1, PhysicalButton.DpadUp),
        });
        Check("L2 full -> LT", PadState.Neutral with { L2 = 255 });
        Check("R2 half -> RT", PadState.Neutral with { R2 = 128 });
        Check("Left stick up-right max", PadState.Neutral with { LeftX = short.MaxValue, LeftY = short.MaxValue });
        Check("Left stick left min", PadState.Neutral with { LeftX = short.MinValue });
        Check("Right stick down min", PadState.Neutral with { RightY = short.MinValue });
        Check("Right stick small drift", PadState.Neutral with { RightX = 900, RightY = -400 });
        Check("Neutral", PadState.Neutral);

        // Rumble: a game calls XInputSetState; ViGEm reports it; Bridge forwards it to the DualSense.
        bool RumbleArrives(ushort left, ushort right, ushort expectLow, ushort expectHigh)
        {
            XInputProbe.SetRumble(slot, left, right);
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 1000)
            {
                bridge.Tick();
                if (pad.Rumble == (expectLow, expectHigh))
                    return true;
                Thread.Sleep(5);
            }
            return false;
        }

        // XInput motor speeds are 0..65535; ViGEm passes the high byte; we scale back by 257.
        bool rumbleOn = RumbleArrives(40000, 20000, AnalogMath.RumbleToSdl(40000 >> 8, 1.0), AnalogMath.RumbleToSdl(20000 >> 8, 1.0));
        if (!rumbleOn) failures++;
        Console.WriteLine($"{(rumbleOn ? "PASS" : "FAIL")}  {"Rumble on reaches DualSense",-28} low={pad.Rumble.Low} high={pad.Rumble.High}");
        bool rumbleOff = RumbleArrives(0, 0, 0, 0);
        if (!rumbleOff) failures++;
        Console.WriteLine($"{(rumbleOff ? "PASS" : "FAIL")}  {"Rumble off reaches DualSense",-28} low={pad.Rumble.Low} high={pad.Rumble.High}");

        bridge.Dispose();
        Thread.Sleep(300);
        bool unplugged = XInputProbe.Read(slot) is null;
        if (!unplugged) failures++;
        Console.WriteLine($"{(unplugged ? "PASS" : "FAIL")}  {"Virtual pad unplugged on exit",-28}");

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        return failures == 0 ? 0 : 1;
    }

    static Action? sdlPump;

    static int WaitForSlot(Bridge bridge, HashSet<int> before)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 3000)
        {
            bridge.Tick();
            sdlPump?.Invoke();
            if (bridge.VirtualPad?.UserIndex is int i and >= 0 and < 4)
                return i;
            var fresh = XInputProbe.ConnectedSlots().FirstOrDefault(s => !before.Contains(s), -1);
            if (fresh >= 0)
                return fresh;
            Thread.Sleep(20);
        }
        return -1;
    }

    static string Describe(X360Report r) =>
        $"[{(r.Buttons == X360Buttons.None ? "-" : r.Buttons.ToString().Replace(", ", "+"))}] " +
        $"LT {r.LeftTrigger} RT {r.RightTrigger} L({r.ThumbLX},{r.ThumbLY}) R({r.ThumbRX},{r.ThumbRY})";
}
