using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using MoonBot.Core.Security;

namespace MoonBot.Platform.Windows;

// Guarda secretos en el Administrador de credenciales de Windows (Panel de control >
// Administrador de credenciales > Credenciales de Windows, aparecen como "MoonBot/...").
// Windows los cifra con DPAPI, ligado a tu usuario: otro usuario del PC no puede leerlos.
[SupportedOSPlatform("windows")]
public sealed class CredentialManagerSecretStore(string prefix = "MoonBot") : ISecretStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public void Save(string name, string secret)
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = Target(name),
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPtr,
                Persist = CredPersistLocalMachine,
                UserName = Environment.UserName
            };

            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            // El secreto no se queda en memoria más de lo necesario.
            Marshal.Copy(new byte[blob.Length], 0, blobPtr, blob.Length);
            Marshal.FreeHGlobal(blobPtr);
            Array.Clear(blob);
        }
    }

    public string? Read(string name)
    {
        if (!CredRead(Target(name), CredTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound ? null : throw new Win32Exception(error);
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return "";

            return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Delete(string name)
    {
        if (!CredDelete(Target(name), CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
                throw new Win32Exception(error);
        }
    }

    private string Target(string name) => $"{prefix}/{name}";

    // Estructura CREDENTIALW de wincred.h, campo por campo y en el mismo orden.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
