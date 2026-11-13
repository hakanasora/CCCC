using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;

namespace CCCC
{
    internal sealed class PrankQrCode : FrameworkElement
    {
        internal static string[] ReadModules()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CCCC.Prank.RepositoryQr"))
            {
                if (stream == null) throw new InvalidOperationException("二维码内置资源缺失。");
                using (StreamReader reader = new StreamReader(stream))
                {
                    string[] rows = reader.ReadToEnd().Trim().Split('\n');
                    for (int y = 0; y < rows.Length; y++)
                    {
                        rows[y] = rows[y].Trim();
                        if (rows[y].Length != rows.Length) throw new InvalidDataException("Invalid QR matrix.");
                        foreach (char value in rows[y]) if (value != '0' && value != '1') throw new InvalidDataException("Invalid QR module.");
                    }
                    return rows;
                }
            }
        }
        private readonly string[] modules = ReadModules();
        private double renderedScale;
        internal PrankQrCode()
        {
            SnapsToDevicePixels = true;
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            LayoutUpdated += CheckScale;
            Loaded += delegate { LayoutUpdated -= CheckScale; LayoutUpdated += CheckScale; };
            Unloaded += delegate { LayoutUpdated -= CheckScale; };
        }
        private double DeviceScale()
        {
            Visual root = this;
            DependencyObject ancestor;
            while ((ancestor = VisualTreeHelper.GetParent(root)) is Visual) root = (Visual)ancestor;
            GeneralTransform transform = TransformToAncestor(root);
            Point a = transform.Transform(new Point()), b = transform.Transform(new Point(1, 0));
            PresentationSource source = PresentationSource.FromVisual(this);
            double dpi = source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformToDevice.M11 : 1;
            return Math.Max(.001, Math.Abs(b.X - a.X) * dpi);
        }
        private void CheckScale(object sender, EventArgs args)
        { if (Math.Abs(DeviceScale() - renderedScale) > .0001) InvalidateVisual(); }
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            drawing.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
            double scale = DeviceScale();
            renderedScale = scale;
            // Snap modules to whole physical pixels even inside the adaptive
            // Viewbox. At least four white modules surround the encoded data.
            double module = Math.Max(1, Math.Floor(Math.Min(ActualWidth, ActualHeight) * scale / modules.Length)) / scale;
            double x = Math.Round((ActualWidth - module * modules.Length) * scale / 2) / scale;
            double y = Math.Round((ActualHeight - module * modules.Length) * scale / 2) / scale;
            for (int row = 0; row < modules.Length; row++)
                for (int column = 0; column < modules.Length; column++)
                    if (modules[row][column] == '1') drawing.DrawRectangle(Brushes.Black, null,
                        new Rect(x + column * module, y + row * module, module, module));
        }
    }
}
