using System.Runtime.InteropServices;

namespace Nova.Platform.Interop;

internal static class ShellInterop
{
    public static readonly Guid FOLDERID_AppsFolder = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");
    public static readonly Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");
    public static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    public static readonly Guid IID_IEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");
    public static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    public const uint SIGDN_NORMALDISPLAY = 0;
    /// <summary>For AppsFolder items this is the AUMID / launch key.</summary>
    public const uint SIGDN_PARENTRELATIVEPARSING = 0x80018001;

    [Flags]
    public enum SIIGBF
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        MemoryOnly = 0x02,
        IconOnly = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly = 0x10,
        ScaleUp = 0x100,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx, cy;
        public SIZE(int x, int y) { cx = x; cy = y; }
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        [PreserveSig] int GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
        [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumShellItems
    {
        [PreserveSig] int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IShellItem rgelt, out uint pceltFetched);
        [PreserveSig] int Skip(uint celt);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumShellItems ppenum);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    public static string? GetName(IShellItem item, uint sigdn)
    {
        var hr = item.GetDisplayName(sigdn, out var ptr);
        if (hr != 0 || ptr == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(ptr); }
        finally { Marshal.FreeCoTaskMem(ptr); }
    }
}
