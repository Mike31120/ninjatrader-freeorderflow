// Compat/StrokeShim.cs
// Shim minimal pour builds hors NinjaTrader : fournit NinjaTrader.Gui.Tools.Stroke + DashStyleHelper
// afin d'éviter CS0246 quand la vraie DLL n'est pas présente/résolue.

using System;
using System.Windows.Media;
using SharpDX.Direct2D1;

namespace NinjaTrader.Gui.Tools
{
    // Enum "placeholder" pour rester compatible avec les appels existants.
    // On ne convertit pas ici en StrokeStyle D2D (optionnel).
    public enum DashStyleHelper
    {
        Solid,
        Dash,
        Dot,
        DashDot,
        DashDotDot
    }

    /// <summary>
    /// Shim léger de la classe Stroke de NinjaTrader (seulement ce dont on a besoin).
    /// Contient : Brush (WPF), Width (float), StrokeStyle (D2D), BrushDX (D2D) et un setter RenderTarget.
    /// </summary>
    public class Stroke
    {
        public Brush Brush { get; set; }
        public float Width { get; set; }
        public StrokeStyle StrokeStyle { get; set; }

        // Dans NT, BrushDX est géré par NT; ici on le gère via RenderTarget setter.
        public SharpDX.Direct2D1.Brush BrushDX { get; private set; }

        private RenderTarget renderTarget;

        public Stroke(Brush brush, float width)
        {
            Brush = brush;
            Width = width;
            StrokeStyle = null; // style plein par défaut
        }

        public Stroke(Brush brush, DashStyleHelper dash, float width)
        {
            Brush = brush;
            Width = width;
            // On pourrait créer un StrokeStyle selon "dash" si nécessaire :
            // ici on reste simple (null == trait plein). Tu peux étendre si besoin.
            StrokeStyle = null;
        }

        // NinjaTrader expose "RenderTarget" sur Stroke; on imite ce comportement
        // pour que le code existant (OnRenderTargetChanged) fonctionne.
        public RenderTarget RenderTarget
        {
            get => renderTarget;
            set
            {
                renderTarget = value;
                // (Ré)créer la brosse DX si possible
                DisposeBrushDx();
                if (renderTarget != null && Brush != null)
                {
                    try
                    {
                        // Conversion simple de SolidColorBrush -> DX Brush
                        if (Brush is SolidColorBrush scb)
                        {
                            var color = new SharpDX.Mathematics.Interop.RawColor4(
                                scb.Color.ScR, scb.Color.ScG, scb.Color.ScB, scb.Color.ScA
                            );
                            BrushDX = new SolidColorBrush(renderTarget, color);
                        }
                        else
                        {
                            // fallback : noir
                            BrushDX = new SolidColorBrush(renderTarget, new SharpDX.Mathematics.Interop.RawColor4(0, 0, 0, 1));
                        }
                    }
                    catch
                    {
                        BrushDX = null;
                    }
                }
            }
        }

        private void DisposeBrushDx()
        {
            try { BrushDX?.Dispose(); } catch { /* noop */ }
            BrushDX = null;
        }
    }
}
