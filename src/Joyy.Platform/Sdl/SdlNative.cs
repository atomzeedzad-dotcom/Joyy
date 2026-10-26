using System.Runtime.InteropServices;

namespace Joyy.Platform.Sdl;

/// <summary>
/// Minimal SDL2 bindings for the GameController API. Only what Joyy uses; signatures follow
/// SDL 2.24+ headers (SDL_GameControllerPath needs 2.24, DualSense Edge needs 2.26).
/// </summary>
internal static partial class SdlNative
{
    const string Lib = "SDL2";

    public const uint INIT_JOYSTICK = 0x00000200;
    public const uint INIT_GAMECONTROLLER = 0x00002000;
    public const uint INIT_EVENTS = 0x00004000;

    public const uint CONTROLLERDEVICEADDED = 0x653;   // which = device index
    public const uint CONTROLLERDEVICEREMOVED = 0x654; // which = instance id

    // SDL_GameControllerButton
    public const int BUTTON_A = 0, BUTTON_B = 1, BUTTON_X = 2, BUTTON_Y = 3;
    public const int BUTTON_BACK = 4, BUTTON_GUIDE = 5, BUTTON_START = 6;
    public const int BUTTON_LEFTSTICK = 7, BUTTON_RIGHTSTICK = 8;
    public const int BUTTON_LEFTSHOULDER = 9, BUTTON_RIGHTSHOULDER = 10;
    public const int BUTTON_DPAD_UP = 11, BUTTON_DPAD_DOWN = 12, BUTTON_DPAD_LEFT = 13, BUTTON_DPAD_RIGHT = 14;
    public const int BUTTON_MISC1 = 15; // DualSense microphone (Mute) button
    public const int BUTTON_TOUCHPAD = 20;

    // SDL_GameControllerAxis
    public const int AXIS_LEFTX = 0, AXIS_LEFTY = 1, AXIS_RIGHTX = 2, AXIS_RIGHTY = 3;
    public const int AXIS_TRIGGERLEFT = 4, AXIS_TRIGGERRIGHT = 5;

    /// <summary>SDL_Event is a 56-byte union; we only read the common header and the device index/id.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 56)]
    public struct Event
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public int Which;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Version
    {
        public byte Major, Minor, Patch;
    }

    [LibraryImport(Lib, EntryPoint = "SDL_Init")]
    public static partial int Init(uint flags);

    [LibraryImport(Lib, EntryPoint = "SDL_Quit")]
    public static partial void Quit();

    [LibraryImport(Lib, EntryPoint = "SDL_GetVersion")]
    public static partial void GetVersion(out Version version);

    [LibraryImport(Lib, EntryPoint = "SDL_SetHint", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetHint(string name, string value);

    [LibraryImport(Lib, EntryPoint = "SDL_GetError")]
    private static partial nint GetErrorPtr();

    [LibraryImport(Lib, EntryPoint = "SDL_PollEvent")]
    public static partial int PollEvent(out Event e);

    [LibraryImport(Lib, EntryPoint = "SDL_NumJoysticks")]
    public static partial int NumJoysticks();

    [LibraryImport(Lib, EntryPoint = "SDL_IsGameController")]
    public static partial int IsGameController(int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "SDL_JoystickGetDeviceVendor")]
    public static partial ushort JoystickGetDeviceVendor(int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "SDL_JoystickGetDeviceProduct")]
    public static partial ushort JoystickGetDeviceProduct(int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "SDL_GameControllerOpen")]
    public static partial nint GameControllerOpen(int deviceIndex);

    [LibraryImport(Lib, EntryPoint = "SDL_GameControllerClose")]
    public static partial void GameControllerClose(nint controller);

    [LibraryImport(Lib, EntryPoint = "SDL_GameControllerGetButton")]
    public static partial byte GameControllerGetButton(nint controller, int button);

    [LibraryImport(Lib, EntryPoint = "SDL_GameControllerGetAxis")]
    public static partial short GameControllerGetAxis(nint controller, int axis);

    [LibraryImport(Lib, EntryPoint = "SDL_GameControllerRumble")]
    public static partial int GameControllerRumble(nint controller, ushort lowFrequency, ushort highFrequency, uint durationMs);

    [LibraryImport(Lib, EntryPoint = "SDL_GameControllerGetVendor")]
    public static partial ushort GameControllerGetVendor(nint controller);

    [LibraryImport(Lib, EntryPoint = "SDL_GameControllerGetProduct")]
    public static partial ushort GameControllerGetProduct(nint controller);

    [LibraryImport(Lib, EntryPoint = "SDL_GameControllerGetJoystick")]
    public static partial nint GameControllerGetJoystick(nint controller);

    [LibraryImport(Lib, EntryPoint = "SDL_JoystickInstanceID")]
    public static partial int JoystickInstanceID(nint joystick);

    [LibraryImport(Lib, EntryPoint = "SDL_GameControllerName")]
    private static partial nint GameControllerNamePtr(nint controller);

    [LibraryImport(Lib, EntryPoint = "SDL_GameControllerPath")]
    private static partial nint GameControllerPathPtr(nint controller);

    [LibraryImport(Lib, EntryPoint = "SDL_GameControllerGetSerial")]
    private static partial nint GameControllerGetSerialPtr(nint controller);

    // SDL owns these strings; copy them, never free them.
    public static string GetError() => Marshal.PtrToStringUTF8(GetErrorPtr()) ?? "";
    public static string? GameControllerName(nint c) => Marshal.PtrToStringUTF8(GameControllerNamePtr(c));
    public static string? GameControllerPath(nint c) => Marshal.PtrToStringUTF8(GameControllerPathPtr(c));
    public static string? GameControllerGetSerial(nint c) => Marshal.PtrToStringUTF8(GameControllerGetSerialPtr(c));
}
