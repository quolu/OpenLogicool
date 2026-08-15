using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace OpenLogicool.CaptureProbe;

// Windows.Graphics.Capture は GraphicsCaptureItem の picker なし生成 (CreateForMonitor /
// CreateForWindow) と IDirect3DDevice⇔ID3D11Device 相互変換を WinRT projection の外、
// 素の COM interop で行う必要がある。ここではその配線だけを持つ。

[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);

    IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
}

[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    IntPtr GetInterface([In] ref Guid iid);
}

internal static class WgcInterop
{
    private static readonly Guid GraphicsCaptureItemIid = Guid.Parse("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    public static GraphicsCaptureItem CreateItemForMonitor(IntPtr hmonitor)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var itemIid = GraphicsCaptureItemIid;
        var itemPtr = interop.CreateForMonitor(hmonitor, ref itemIid);
        try
        {
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
        }
        finally
        {
            Marshal.Release(itemPtr);
        }
    }

    public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var itemIid = GraphicsCaptureItemIid;
        var itemPtr = interop.CreateForWindow(hwnd, ref itemIid);
        try
        {
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
        }
        finally
        {
            Marshal.Release(itemPtr);
        }
    }

    public static IDirect3DDevice CreateDirect3DDeviceFromDxgiDevice(IntPtr dxgiDevicePtr)
    {
        var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevicePtr, out var deviceAbi);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(deviceAbi);
        }
        finally
        {
            Marshal.Release(deviceAbi);
        }
    }
}
