using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MoonBot.Platform.Windows;

// Escribe texto en la ventana activa como si se tecleara, carácter por carácter (KEYEVENTF_UNICODE).
// No usa el portapapeles, así que no borra lo que el usuario había copiado.
// Límite conocido: Windows bloquea la escritura en ventanas "como administrador" (UIPI) sin avisar.
[SupportedOSPlatform("windows")]
public static class TextTyper
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventUnicode = 0x0004;
    private const uint KeyEventKeyUp = 0x0002;
    private const int ChunkSize = 200;

    public static void Type(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // Se envía en bloques: cada bloque llega entero, sin mezclarse con teclas del usuario.
        for (var start = 0; start < text.Length; start += ChunkSize)
        {
            var chunk = text.AsSpan(start, Math.Min(ChunkSize, text.Length - start));
            var inputs = new Input[chunk.Length * 2];
            for (var i = 0; i < chunk.Length; i++)
            {
                inputs[2 * i] = Key(chunk[i], KeyEventUnicode);
                inputs[2 * i + 1] = Key(chunk[i], KeyEventUnicode | KeyEventKeyUp);
            }

            var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
            if (sent != inputs.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    // Tamaño de INPUT tal como lo ve .NET. Windows exige 40 bytes en 64 bits y 28 en 32 bits.
    internal static int InputSize => Marshal.SizeOf<Input>();

    private static Input Key(char c, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInput { Scan = c, Flags = flags } }
    };

    // Estructuras de winuser.h. La unión debe tener el tamaño del miembro mayor (MOUSEINPUT).
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
