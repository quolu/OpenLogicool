using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenLogicool.Host;

/// <summary>中継サーバーへ送る時のパスワードの置き場。設定ファイル・export・診断 bundle には載せない。</summary>
public interface IRemoteViewSecretStore
{
    void Save(string password);

    string? Load();

    void Delete();
}

/// <summary>Windows の資格情報マネージャー（汎用資格情報）へパスワードを保存する。</summary>
public sealed class RemoteViewSecretStore : IRemoteViewSecretStore
{
    public const string TargetName = "OpenLogicool/RemoteView/publish";

    private const uint TypeGeneric = 1;
    private const uint PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    // CRED_MAX_CREDENTIAL_BLOB_SIZE は 2560 byte。UTF-16 で保存するので 1280 文字まで。
    private const int MaxPasswordLength = 1280;

    public void Save(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (password.Length > MaxPasswordLength)
        {
            throw new ArgumentException($"パスワードは{MaxPasswordLength}文字までです。", nameof(password));
        }

        var blob = Encoding.Unicode.GetBytes(password);
        var blobPointer = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPointer, blob.Length);
            var credential = new Credential
            {
                Type = TypeGeneric,
                TargetName = TargetName,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPointer,
                Persist = PersistLocalMachine,
                UserName = "OpenLogicool",
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "資格情報マネージャーへ保存できませんでした。");
            }
        }
        finally
        {
            Array.Clear(blob);
            Marshal.FreeHGlobal(blobPointer);
        }
    }

    public string? Load()
    {
        if (!CredRead(TargetName, TypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound
                ? null
                : throw new Win32Exception(error, "資格情報マネージャーから読めませんでした。");
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return null;
            }

            var blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            try
            {
                return Encoding.Unicode.GetString(blob);
            }
            finally
            {
                Array.Clear(blob);
            }
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Delete()
    {
        if (!CredDelete(TargetName, TypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new Win32Exception(error, "資格情報マネージャーから削除できませんでした。");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string targetName, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string targetName, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern void CredFree(IntPtr buffer);
}
