using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MoonBot.Platform.Windows;

// Consulta el estado de teclas concretas. A propósito NO usa un "hook" de teclado global:
// un hook recibe todas las pulsaciones (también contraseñas). Aquí solo se pregunta
// "¿está Ctrl derecho abajo?" y "¿hay alguna otra tecla abajo?" (sí/no, nunca cuál).
[SupportedOSPlatform("windows")]
public static class KeyState
{
    public const int RightControl = 0xA3;
    private const int Control = 0x11;

    public static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public static bool AnyOtherKeyDown(int exceptKey)
    {
        // 0x08 en adelante: teclado (se ignoran los botones del ratón, 0x01-0x06).
        for (var vk = 0x08; vk <= 0xFE; vk++)
        {
            if (vk == exceptKey || vk == Control)
                continue;
            if (IsDown(vk))
                return true;
        }
        return false;
    }

    public static bool AnyModifierDown()
        => IsDown(0x10) || IsDown(0x11) || IsDown(0x12) || IsDown(0x5B) || IsDown(0x5C); // Shift, Ctrl, Alt, Win

    // ¿La ventana activa es de este mismo proceso (MoonBot)?
    public static bool ForegroundIsThisProcess()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    public static IntPtr ForegroundWindow() => GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
