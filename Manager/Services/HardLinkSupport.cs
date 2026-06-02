using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace TonePrism.Manager.Services
{
    /// <summary>
    /// (#250 PR1) NTFS ハードリンクの作成と、保存先がハードリンク対応かの判定を 1 箇所に隔離する static service。
    /// kernel32 P/Invoke を本クラスに閉じ込め、AssetSnapshotService からはマネージドな API だけ見えるようにする。
    ///
    /// **設計の核心**: アセットスナップショットの重複排除は「新スナップショット ↔ 前スナップショット」間で起きる。
    /// 両方とも backup_dest 上にあるため、判定軸は「dest ボリュームがハードリンク対応 (NTFS) か」だけで、
    /// src と dest が同一ボリュームかは無関係 (src からは常に実コピー、リンクは dest 内 base→新世代でのみ張る)。
    /// </summary>
    internal static class HardLinkSupport
    {
        // ---- P/Invoke ----

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateHardLinkW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLinkNative(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
        private static extern SafeFileHandleWrapper CreateFileNative(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
            uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandleWrapper hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        private sealed class SafeFileHandleWrapper : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
        {
            public SafeFileHandleWrapper() : base(true) { }
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool CloseHandle(IntPtr hObject);
            protected override bool ReleaseHandle() { return CloseHandle(handle); }
        }

        private const uint FILE_READ_ATTRIBUTES = 0x0080;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint FILE_SHARE_DELETE = 0x00000004;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000; // ディレクトリ/各種ファイルを attribute だけで開く

        // ---- public API ----

        /// <summary>
        /// 既存ファイル <paramref name="existingFile"/> への新しいハードリンク <paramref name="linkPath"/> を作成する。
        /// 両 path は同一ボリュームでなければならない (本 PR の用途では両方 backup_dest 上で常に満たす)。
        /// 長パスは呼び出し側で `\\?\` を付与してから渡す (CreateHardLinkW は long path 対応)。
        /// **失敗時は Win32 エラー入りの IOException を投げる** — 呼び出し側はこれを catch して実コピー fallback する契約。
        /// </summary>
        public static void CreateHardLink(string linkPath, string existingFile)
        {
            if (!CreateHardLinkNative(linkPath, existingFile, IntPtr.Zero))
            {
                int err = Marshal.GetLastWin32Error();
                throw new IOException(
                    "CreateHardLink に失敗 (Win32 " + err + "): " + linkPath + " -> " + existingFile,
                    new Win32Exception(err));
            }
        }

        /// <summary>
        /// <paramref name="probeDir"/> が実在するボリューム上でハードリンクを作れるかを、実際に 1 本テストリンクを
        /// 張って消す動的プローブで判定する。FS 名 (NTFS) 文字列に頼らない (SMB 越し等で当てにならないため) で、
        /// 「実際にリンクできるか」を直接確かめる。本メソッドは throw しない (never-throw、失敗は false)。
        /// FAT/exFAT・古い SMB・権限不足等で false を返したら呼び出し側は全実コピー fallback する。
        /// </summary>
        public static bool ProbeHardLinkSupport(string probeDir)
        {
            string src = null;
            string link = null;
            try
            {
                if (string.IsNullOrEmpty(probeDir)) return false;
                Directory.CreateDirectory(probeDir);
                string token = Guid.NewGuid().ToString("N");
                src = Path.Combine(probeDir, ".hlprobe_" + token + ".src");
                link = Path.Combine(probeDir, ".hlprobe_" + token + ".lnk");
                File.WriteAllBytes(src, new byte[] { 0 });
                if (!CreateHardLinkNative(link, src, IntPtr.Zero)) return false;
                return File.Exists(link);
            }
            catch
            {
                return false; // 例外は「非対応」として握り潰す (never-throw)
            }
            finally
            {
                TryDeleteFile(link);
                TryDeleteFile(src);
            }
        }

        /// <summary>
        /// 2 つの path が同一の実体 (同一ボリューム + 同一 file index) を指すか判定する。ハードリンク共有の検証用
        /// (主にテスト)。判定不能時は false を返す (never-throw)。
        /// </summary>
        internal static bool AreSameFile(string pathA, string pathB)
        {
            BY_HANDLE_FILE_INFORMATION a, b;
            if (!TryGetInfo(pathA, out a)) return false;
            if (!TryGetInfo(pathB, out b)) return false;
            return a.VolumeSerialNumber == b.VolumeSerialNumber
                && a.FileIndexHigh == b.FileIndexHigh
                && a.FileIndexLow == b.FileIndexLow;
        }

        private static bool TryGetInfo(string path, out BY_HANDLE_FILE_INFORMATION info)
        {
            info = default(BY_HANDLE_FILE_INFORMATION);
            try
            {
                using (var h = CreateFileNative(path, FILE_READ_ATTRIBUTES,
                    FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, IntPtr.Zero,
                    OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero))
                {
                    if (h.IsInvalid) return false;
                    return GetFileInformationByHandle(h, out info);
                }
            }
            catch
            {
                return false;
            }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { /* プローブ後始末の失敗は無視 */ }
        }
    }
}
