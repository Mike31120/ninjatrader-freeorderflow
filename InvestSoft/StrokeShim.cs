#if OUTSIDE_NINJATRADER
// Compat/StrokeShim.cs
// Shim minimal pour builds hors NinjaTrader : NinjaTrader.Gui.Tools.Stroke + DashStyleHelper

using System.Windows.Media;
using SharpDX.Direct2D1;

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
        public Brush Brush { get; set; }
        public float Width { get; set; }
        public StrokeStyle StrokeStyle { get; set; }

        public SharpDX.Direct2D1.Brush BrushDX { get; private set; }

        private RenderTarget renderTarget;

        public Stroke(Brush brush, float width)
        {
            Brush = brush;
            Width = width;
            StrokeStyle = null;
        }

        public Stroke(Brush brush, DashStyleHelper dash, float width)
        {
            Brush = brush;
            Width = width;
            // On laisse StrokeStyle = null (trait plein) pour la build hors NT
            StrokeStyle = null;
        }

        public RenderTarget RenderTarget
        {
            get => renderTarget;
            set
            {
                renderTarget = value;
                try { BrushDX?.Dispose(); } catch { }
                BrushDX = null;

                if (renderTarget != null && Brush is SolidColorBrush scb)
                {
                    var c = new SharpDX.Mathematics.Interop.RawColor4(
                        scb.Color.ScR, scb.Color.ScG, scb.Color.ScB, scb.Color.ScA);
                    BrushDX = new SolidColorBrush(renderTarget, c);
                }
            }
        }
    }
}
#endif
