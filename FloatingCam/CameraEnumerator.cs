using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FloatingCam;

/// <summary>
/// Enumera as webcams via DirectShow. A ordem retornada casa com o índice
/// usado por OpenCvSharp.VideoCapture(index, VideoCaptureAPIs.DSHOW).
/// </summary>
public static class CameraEnumerator
{
    public record Camera(int Index, string Name);

    public static List<Camera> List()
    {
        var result = new List<Camera>();

        var devEnumType = Type.GetTypeFromCLSID(new Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86"))!;
        var devEnum = (ICreateDevEnum)Activator.CreateInstance(devEnumType)!;

        var videoInputCategory = new Guid("860BB310-5D01-11d0-BD3B-00A0C911CE86");
        int hr = devEnum.CreateClassEnumerator(ref videoInputCategory, out IEnumMoniker? enumMoniker, 0);
        if (hr != 0 || enumMoniker is null)
        {
            Marshal.ReleaseComObject(devEnum);
            return result; // nenhuma câmera
        }

        var monikers = new IMoniker[1];
        int index = 0;
        while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
        {
            var moniker = monikers[0];
            if (moniker is null) { index++; continue; }

            string name = ReadFriendlyName(moniker) ?? $"Câmera {index}";
            result.Add(new Camera(index, name));
            Marshal.ReleaseComObject(moniker);
            index++;
        }

        Marshal.ReleaseComObject(enumMoniker);
        Marshal.ReleaseComObject(devEnum);
        return result;
    }

    private static string? ReadFriendlyName(IMoniker moniker)
    {
        IPropertyBag? bag = null;
        try
        {
            var bagId = typeof(IPropertyBag).GUID;
            moniker.BindToStorage(IntPtr.Zero, IntPtr.Zero, ref bagId, out object bagObj);
            bag = bagObj as IPropertyBag;
            if (bag is not null && bag.Read("FriendlyName", out object value, IntPtr.Zero) == 0 && value is string s)
                return s;
        }
        catch
        {
            // moniker sem FriendlyName legível — usa nome genérico no chamador
        }
        finally
        {
            if (bag is not null)
                Marshal.ReleaseComObject(bag);
        }
        return null;
    }

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator(ref Guid pType, out IEnumMoniker? ppEnumMoniker, int dwFlags);
    }

    [ComImport, Guid("00000102-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumMoniker
    {
        [PreserveSig]
        int Next(int celt,
            [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Interface, SizeParamIndex = 0)] IMoniker[] rgelt,
            IntPtr pceltFetched);
        [PreserveSig]
        int Skip(int celt);
        [PreserveSig]
        int Reset();
        [PreserveSig]
        int Clone(out IEnumMoniker ppenum);
    }

    [ComImport, Guid("0000000f-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMoniker
    {
        // Apenas BindToStorage é necessário; os demais slots da vtable são preenchidos para manter o layout.
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load(IntPtr pStm);
        void Save(IntPtr pStm, [MarshalAs(UnmanagedType.Bool)] bool fClearDirty);
        void GetSizeMax(out long pcbSize);
        void BindToObject(IntPtr pbc, IntPtr pmkToLeft, [In] ref Guid riidResult, [MarshalAs(UnmanagedType.IUnknown)] out object ppvResult);
        void BindToStorage(IntPtr pbc, IntPtr pmkToLeft, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObj);
        // Demais métodos omitidos — não usados.
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig]
        int Read([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, out object pVar, IntPtr pErrorLog);
        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object pVar);
    }
}
