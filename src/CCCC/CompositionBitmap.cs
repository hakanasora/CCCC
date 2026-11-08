using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Windows.Graphics.DirectX;
using Windows.UI.Composition;

namespace CCCC
{
    internal sealed class CompositionBitmap : IDisposable
    {
        [ComImport, Guid("25297D5C-3AD4-4C9C-B5CF-E36A38512330"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICompositorInterop
        {
            void CreateCompositionSurfaceForHandle(IntPtr handle, out IntPtr surface);
            void CreateCompositionSurfaceForSwapChain(IntPtr swapChain, out IntPtr surface);
            void CreateGraphicsDevice(IntPtr device, out CompositionGraphicsDevice graphicsDevice);
        }
        [ComImport, Guid("FD04E6E3-FE0C-4C3C-AB19-A07601A576EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISurfaceInterop
        {
            void BeginDraw(IntPtr rect, ref Guid iid, out IntPtr context, out PointI offset);
            void EndDraw();
        }
        [StructLayout(LayoutKind.Sequential)] private struct PointI { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct SizeU { internal uint Width, Height; }
        [StructLayout(LayoutKind.Sequential)] private struct BitmapProperties { internal uint Format, Alpha; internal float DpiX, DpiY; }
        [StructLayout(LayoutKind.Sequential)] private struct Matrix { internal float M11, M12, M21, M22, X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RectF { internal float Left, Top, Right, Bottom; }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateBitmap(IntPtr self, SizeU size, [In] byte[] pixels, uint pitch, ref BitmapProperties properties, out IntPtr bitmap);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void DrawBitmap(IntPtr self, IntPtr bitmap, ref RectF destination, float opacity, int interpolation, IntPtr source);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetTransform(IntPtr self, ref Matrix matrix);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Clear(IntPtr self, IntPtr color);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetDpi(IntPtr self, float x, float y);
        [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(IntPtr adapter, int driver, IntPtr software, uint flags, IntPtr levels, uint levelCount, uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr context);
        [DllImport("d2d1.dll")] private static extern int D2D1CreateDevice(IntPtr dxgiDevice, IntPtr properties, out IntPtr device);
        private IntPtr device;
        private CompositionGraphicsDevice graphics;
        private CompositionDrawingSurface surface;
        private readonly CompositionSurfaceBrush brush;
        private BackgroundFrame current;
        private bool disposed;
        internal CompositionSurfaceBrush Brush { get { return brush; } }

        internal CompositionBitmap(Compositor compositor)
        {
            IntPtr d3d = IntPtr.Zero, context = IntPtr.Zero, dxgi = IntPtr.Zero;
            try
            {
                int level;
                Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out d3d, out level, out context));
                Guid iid = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3d, ref iid, out dxgi));
                Marshal.ThrowExceptionForHR(D2D1CreateDevice(dxgi, IntPtr.Zero, out device));
                ((ICompositorInterop)(object)compositor).CreateGraphicsDevice(device, out graphics);
                brush = compositor.CreateSurfaceBrush();
                brush.Stretch = CompositionStretch.Fill;
            }
            catch { Dispose(); throw; }
            finally
            {
                if (context != IntPtr.Zero) Marshal.Release(context);
                if (dxgi != IntPtr.Zero) Marshal.Release(dxgi);
                if (d3d != IntPtr.Zero) Marshal.Release(d3d);
            }
        }

        private static T Method<T>(IntPtr value, int index) where T : class
        { return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), index * IntPtr.Size), typeof(T)); }

        internal void SetFrame(BackgroundFrame frame)
        {
            if (disposed) throw new ObjectDisposedException("CompositionBitmap");
            if (Object.ReferenceEquals(current, frame)) return;
            if (frame.Bitmap.Format != PixelFormats.Pbgra32 || frame.Width != frame.Bitmap.PixelWidth || frame.Height != frame.Bitmap.PixelHeight)
                throw new ArgumentException("Composition background must contain exact Pbgra32 pixels.", "frame");
            int stride = checked(frame.Width * 4);
            byte[] pixels = new byte[checked(stride * frame.Height)];
            frame.Bitmap.CopyPixels(pixels, stride, 0);
            CompositionDrawingSurface next = graphics.CreateDrawingSurface(new Windows.Foundation.Size(frame.Width, frame.Height),
                DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);
            try
            {
                ISurfaceInterop interop = (ISurfaceInterop)(object)next;
                Guid iid = new Guid("E8F7FE7A-191C-466D-AD95-975678BDA998");
                IntPtr context = IntPtr.Zero, bitmap = IntPtr.Zero;
                PointI offset;
                interop.BeginDraw(IntPtr.Zero, ref iid, out context, out offset);
                try
                {
                    // ID2D1RenderTarget slots from d2d1.h, including IUnknown and ID2D1Resource.
                    Method<SetDpi>(context, 51)(context, 96, 96);
                    Matrix transform = new Matrix { M11 = 1, M22 = 1, X = offset.X, Y = offset.Y };
                    Method<SetTransform>(context, 30)(context, ref transform);
                    Method<Clear>(context, 47)(context, IntPtr.Zero);
                    BitmapProperties properties = new BitmapProperties { Format = 87, Alpha = 1, DpiX = 96, DpiY = 96 };
                    Marshal.ThrowExceptionForHR(Method<CreateBitmap>(context, 4)(context,
                        new SizeU { Width = (uint)frame.Width, Height = (uint)frame.Height }, pixels, (uint)stride, ref properties, out bitmap));
                    RectF destination = new RectF { Right = frame.Width, Bottom = frame.Height };
                    Method<DrawBitmap>(context, 26)(context, bitmap, ref destination, 1, 0, IntPtr.Zero);
                }
                finally
                {
                    try { interop.EndDraw(); }
                    finally
                    {
                        if (bitmap != IntPtr.Zero) Marshal.Release(bitmap);
                        if (context != IntPtr.Zero) Marshal.Release(context);
                    }
                }
                brush.Surface = next;
                CompositionDrawingSurface old = surface;
                surface = next; next = null; current = frame;
                if (old != null) old.Dispose();
            }
            finally { if (next != null) next.Dispose(); }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; current = null;
            try { if (brush != null) brush.Surface = null; }
            finally
            {
                try { if (brush != null) brush.Dispose(); }
                finally
                {
                    try { if (surface != null) surface.Dispose(); }
                    finally
                    {
                        surface = null;
                        try { if (graphics != null) graphics.Dispose(); }
                        finally
                        {
                            graphics = null;
                            if (device != IntPtr.Zero) { Marshal.Release(device); device = IntPtr.Zero; }
                        }
                    }
                }
            }
        }
    }
}
