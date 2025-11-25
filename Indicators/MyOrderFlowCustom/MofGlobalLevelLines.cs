#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators.MyOrderFlowCustom;
using NinjaTrader.NinjaScript.DrawingTools;
using DX = SharpDX;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.MyOrderFlowCustom
{
    /// <summary>
    /// Draws global HVN/LVN lines calculated by MofRangeVolumeProfile,
    /// plus infinite horizontal bands around each level using SharpDX.
    /// </summary>
    public class MofGlobalLevelLines : Indicator
    {
        private readonly HashSet<string> currentHvnTags = new HashSet<string>();
        private readonly HashSet<string> currentLvnTags = new HashSet<string>();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description       = "Draws global HVN/LVN lines calculated by MofRangeVolumeProfile.";
                Name              = "MOF Global Level Lines";
                IsOverlay         = true;
                DisplayInDataBox  = false;
                DrawOnPricePanel  = true;

                // default line styles
                HvnStroke = new Stroke(Brushes.Gold, DashStyleHelper.Solid, 1);
                LvnStroke = new Stroke(Brushes.Lime, DashStyleHelper.Solid, 1);
                ShowHvn   = true;
                ShowLvn   = true;

                BandTicks       = 4;
                HvnBandBrush    = new SolidColorBrush(Colors.Gold);
                HvnBandOpacity  = 40;
                LvnBandBrush    = new SolidColorBrush(Colors.Lime);
                LvnBandOpacity  = 40;
            }
            else if (State == State.Configure)
            {
                // OnEachTick pour rafraîchir les lignes dès que les niveaux changent
                Calculate = Calculate.OnEachTick;
            }
        }

        protected override void OnBarUpdate()
        {
            string instrument = Instrument.FullName;
            List<double> hvnList = null;
            List<double> lvnList = null;

            MofRangeVolumeProfile.GlobalHvnLevels.TryGetValue(instrument, out hvnList);
            MofRangeVolumeProfile.GlobalLvnLevels.TryGetValue(instrument, out lvnList);

            if (ShowHvn)
                UpdateLines(hvnList ?? new List<double>(), "HVN", HvnStroke, currentHvnTags);
            else
                RemoveTags(currentHvnTags);

            if (ShowLvn)
                UpdateLines(lvnList ?? new List<double>(), "LVN", LvnStroke, currentLvnTags);
            else
                RemoveTags(currentLvnTags);
        }

        /// <summary>
        /// Gère la création / suppression des lignes horizontales globales via Draw.HorizontalLine.
        /// </summary>
        private void UpdateLines(List<double> levels, string prefix, Stroke stroke, HashSet<string> lineTagSet)
        {
            if (Instrument == null || Instrument.MasterInstrument == null)
                return;

            int decimals = (int)Math.Max(0, Math.Round(-Math.Log10(Instrument.MasterInstrument.TickSize)));
            var desiredTags = new HashSet<string>(levels.Select(p => $"MOF_{prefix}_{Math.Round(p, decimals)}"));

            // Supprimer les lignes qui ne sont plus dans la liste
            foreach (var tag in lineTagSet.ToList())
            {
                if (!desiredTags.Contains(tag))
                {
                    RemoveDrawObject(tag);
                    lineTagSet.Remove(tag);
                }
            }

            // Créer les nouvelles lignes
            foreach (double price in levels)
            {
                string tag = $"MOF_{prefix}_{Math.Round(price, decimals)}";
                if (lineTagSet.Contains(tag))
                    continue;

                var line = Draw.HorizontalLine(this, tag, price,
                    stroke.Brush, stroke.DashStyleHelper, (int)stroke.Width, true);
                line.IsLocked = true;
                lineTagSet.Add(tag);
            }
        }

        private void RemoveTags(HashSet<string> lineTagSet)
        {
            foreach (var tag in lineTagSet.ToList())
            {
                RemoveDrawObject(tag);
                lineTagSet.Remove(tag);
            }
        }

        /// <summary>
        /// Rendu custom des bandes HVN/LVN en SharpDX : bandes horizontales infinies (full panel width).
        /// </summary>
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (IsInHitTest || Bars == null || Instrument == null || ChartPanel == null)
                return;

            double tickSize = Instrument.MasterInstrument.TickSize;
            if (tickSize <= 0 || BandTicks <= 0)
                return;

            float panelLeft  = ChartPanel.X;
            float panelWidth = ChartPanel.W;
            if (panelWidth <= 0)
                return;

            double offset = tickSize * BandTicks;
            string instrument = Instrument.FullName;

            // Récupérer les listes de niveaux HVN/LVN actuelles
            List<double> hvnList = null;
            List<double> lvnList = null;
            MofRangeVolumeProfile.GlobalHvnLevels.TryGetValue(instrument, out hvnList);
            MofRangeVolumeProfile.GlobalLvnLevels.TryGetValue(instrument, out lvnList);

            // --- HVN bands ---
            if (ShowHvn && hvnList != null && hvnList.Count > 0)
            {
                using (var hvnBandDx = CreateDxBandBrush(HvnBandBrush, HvnBandOpacity))
                {
                    if (hvnBandDx != null)
                    {
                        foreach (double level in hvnList)
                            DrawBand(chartScale, level, offset, panelLeft, panelWidth, hvnBandDx);
                    }
                }
            }

            // --- LVN bands ---
            if (ShowLvn && lvnList != null && lvnList.Count > 0)
            {
                using (var lvnBandDx = CreateDxBandBrush(LvnBandBrush, LvnBandOpacity))
                {
                    if (lvnBandDx != null)
                    {
                        foreach (double level in lvnList)
                            DrawBand(chartScale, level, offset, panelLeft, panelWidth, lvnBandDx);
                    }
                }
            }
        }

        /// <summary>
        /// Crée un pinceau SharpDX à partir d'un Brush WPF, avec une opacité en %.
        /// </summary>
        private SharpDX.Direct2D1.Brush CreateDxBandBrush(Brush wpfBrush, int opacityPercent)
        {
            if (wpfBrush == null || RenderTarget == null)
                return null;

            var dxBrush = wpfBrush.ToDxBrush(RenderTarget);
            dxBrush.Opacity = (float)Math.Max(0.0, Math.Min(1.0, opacityPercent / 100.0));
            return dxBrush;
        }

        /// <summary>
        /// Dessine une bande horizontale "infinie" autour d'un niveau de prix.
        /// </summary>
        private void DrawBand(ChartScale chartScale, double level, double offset,
                              float panelLeft, float panelWidth,
                              SharpDX.Direct2D1.Brush dxBrush)
        {
            double topPrice    = level + offset;
            double bottomPrice = level - offset;

            float yTop    = chartScale.GetYByValue(topPrice);
            float yBottom = chartScale.GetYByValue(bottomPrice);

            float y      = Math.Min(yTop, yBottom);
            float height = Math.Abs(yTop - yBottom);

            if (height <= 0.5f)
                return;

            var rect = new DX.RectangleF(panelLeft, y, panelWidth, height);
            RenderTarget.FillRectangle(rect, dxBrush);
        }

        #region Properties
        [Display(Name = "HVN Line", Order = 1, GroupName = "Lines")]
        public Stroke HvnStroke { get; set; }

        [Display(Name = "LVN Line", Order = 2, GroupName = "Lines")]
        public Stroke LvnStroke { get; set; }

        [Display(Name = "Show HVN", Order = 3, GroupName = "Lines")]
        public bool ShowHvn { get; set; }

        [Display(Name = "Show LVN", Order = 4, GroupName = "Lines")]
        public bool ShowLvn { get; set; }

        [Range(0, 1000)]
        [Display(Name = "Band width (ticks)", Description = "Half-width of band around HVN/LVN", Order = 1, GroupName = "Bands")]
        public int BandTicks { get; set; }

        [XmlIgnore]
        [Display(Name = "HVN Band Color", Order = 2, GroupName = "Bands")]
        public Brush HvnBandBrush { get; set; }

        [Browsable(false)]
        public string HvnBandBrushSerialize
        {
            get { return Serialize.BrushToString(HvnBandBrush); }
            set { HvnBandBrush = Serialize.StringToBrush(value); }
        }

        [Range(0, 100)]
        [Display(Name = "HVN Band Opacity (%)", Order = 3, GroupName = "Bands")]
        public int HvnBandOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "LVN Band Color", Order = 4, GroupName = "Bands")]
        public Brush LvnBandBrush { get; set; }

        [Browsable(false)]
        public string LvnBandBrushSerialize
        {
            get { return Serialize.BrushToString(LvnBandBrush); }
            set { LvnBandBrush = Serialize.StringToBrush(value); }
        }

        [Range(0, 100)]
        [Display(Name = "LVN Band Opacity (%)", Order = 5, GroupName = "Bands")]
        public int LvnBandOpacity { get; set; }
        #endregion
    }
}
