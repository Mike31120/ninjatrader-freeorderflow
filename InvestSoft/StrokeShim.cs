#if OUTSIDE_NINJATRADER
// Compat/StrokeShim.cs
// Shim minimal pour builds hors NinjaTrader : NinjaTrader.Gui.Tools.Stroke + DashStyleHelper

// IMPORTANT : on fully-qualifie les types pour éviter les ambiguïtés Brush (WPF vs DX).

namespace NinjaTrader.Gui.Tools
{
    public enum DashStyleHelper
    {
        Solid,
        Dash,
        Dot,
        DashDot,
        DashDotDot
    }

    public class Stroke
    {
        // WPF brush
        public System.Windows.Media.Brush Brush { get; set; }

        public float Width { get; set; }

        // Direct2D stroke style
        public SharpDX.Direct2D1.StrokeStyle StrokeStyle { get; set; }

        // Direct2D brush (créée depuis la Brush WPF)
        public SharpDX.Direct2D1.Brush BrushDX { get; private set; }

        private SharpDX.Direct2D1.RenderTarget renderTarget;

        public Stroke(System.Windows.Media.Brush brush, float width)
        {
            Brush = brush;
            Width = width;
            StrokeStyle = null;
        }

        public Stroke(System.Windows.Media.Brush brush, DashStyleHelper dash, float width)
        {
            Brush = brush;
            Width = width;
            // on garde StrokeStyle = null (trait plein) pour le build hors NT
            StrokeStyle = null;
        }

        public SharpDX.Direct2D1.RenderTarget RenderTarget
        {
            get => renderTarget;
            set
            {
                renderTarget = value;

                try { BrushDX?.Dispose(); } catch { }
                BrushDX = null;

                if (renderTarget != null && Brush is System.Windows.Media.SolidColorBrush scb)
                {
                    var c = new SharpDX.Mathematics.Interop.RawColor4(
                        scb.Color.ScR, scb.Color.ScG, scb.Color.ScB, scb.Color.ScA);
                    BrushDX = new SharpDX.Direct2D1.SolidColorBrush(renderTarget, c);
                }
            }
        }
    }
}
#endif
