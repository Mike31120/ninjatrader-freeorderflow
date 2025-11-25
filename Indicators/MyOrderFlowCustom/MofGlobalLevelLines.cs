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
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.MyOrderFlowCustom
{
    /// <summary>
    /// Example indicator that creates global horizontal lines from the levels
    /// detected by <see cref="MofRangeVolumeProfile"/>.
    /// </summary>
    public class MofGlobalLevelLines : Indicator
    {
        private readonly HashSet<string> currentHvnTags = new HashSet<string>();
        private readonly HashSet<string> currentLvnTags = new HashSet<string>();
        private readonly HashSet<string> currentHvnBandTags = new HashSet<string>();
        private readonly HashSet<string> currentLvnBandTags = new HashSet<string>();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description  = "Draws global HVN/LVN lines calculated by MofRangeVolumeProfile.";
                Name         = "MOF Global Level Lines";
                IsOverlay    = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;

                // Default calculation mode only updates once per bar which can
                // cause noticeable lag when new levels are detected. Use
                // OnEachTick so the indicator refreshes as soon as possible.
            
                // default line styles
                HvnStroke = new Stroke(Brushes.Gold, DashStyleHelper.Solid, 1);
                LvnStroke = new Stroke(Brushes.Lime, DashStyleHelper.Solid, 1);
                ShowHvn = true;
                ShowLvn = true;

                BandTicks = 4;
                HvnBandBrush = new SolidColorBrush(Colors.Gold);
                HvnBandOpacity = 40;
                LvnBandBrush = new SolidColorBrush(Colors.Lime);
                LvnBandOpacity = 40;
            }
            else if (State == State.Configure)
            {
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
                UpdateLines(hvnList ?? new List<double>(), "HVN", HvnStroke, currentHvnTags, currentHvnBandTags, HvnBandBrush, HvnBandOpacity);
            else
                RemoveTags(currentHvnTags, currentHvnBandTags);
            if (ShowLvn)
                UpdateLines(lvnList ?? new List<double>(), "LVN", LvnStroke, currentLvnTags, currentLvnBandTags, LvnBandBrush, LvnBandOpacity);
            else
                RemoveTags(currentLvnTags, currentLvnBandTags);
        }

        private void UpdateLines(List<double> levels, string prefix, Stroke stroke, HashSet<string> lineTagSet, HashSet<string> bandTagSet, Brush bandBrush, int bandOpacity)
        {
            int decimals = (int)Math.Max(0, Math.Round(-Math.Log10(Instrument.MasterInstrument.TickSize)));
            var desiredTags = new HashSet<string>(levels.Select(p => $"MOF_{prefix}_{Math.Round(p, decimals)}"));
            var desiredBandTags = new HashSet<string>(levels.Select(p => $"MOF_{prefix}_BAND_{Math.Round(p, decimals)}"));

            // Drawing methods must use brushes that are safe for cross-thread access. Clone and freeze
            // once per update to avoid "The calling thread cannot access this object" errors.
            var strokeBrush = FreezeBrush(stroke?.Brush);

            foreach (var tag in lineTagSet.ToList())
            {
                if (!desiredTags.Contains(tag))
                {
                    RemoveDrawObject(tag);
                    lineTagSet.Remove(tag);
                }
            }

            foreach (var tag in bandTagSet.ToList())
            {
                if (!desiredBandTags.Contains(tag))
                {
                    RemoveDrawObject(tag);
                    bandTagSet.Remove(tag);
                }
            }

            foreach (double price in levels)
            {
                string tag = $"MOF_{prefix}_{Math.Round(price, decimals)}";
                string bandTag = $"MOF_{prefix}_BAND_{Math.Round(price, decimals)}";
                if (!lineTagSet.Contains(tag))
                {
                    var line = Draw.HorizontalLine(this, tag, price, strokeBrush,
                        stroke.DashStyleHelper, (int)stroke.Width, true);
                    line.IsLocked = true;
                    lineTagSet.Add(tag);
                }

                if (!bandTagSet.Contains(bandTag))
                {
                    double offset = Instrument.MasterInstrument.TickSize * BandTicks;
                    var areaBrush = CreateBandBrush(bandBrush, bandOpacity);
                    var rectangle = Draw.Rectangle(this, bandTag, false, CurrentBar, price + offset, 0, price - offset,
                        null, areaBrush, 1);
                    rectangle.IsLocked = true;
                    bandTagSet.Add(bandTag);
                }
            }
        }

        private void RemoveTags(HashSet<string> lineTagSet, HashSet<string> bandTagSet)
        {
            foreach (var tag in lineTagSet.ToList())
            {
                RemoveDrawObject(tag);
                lineTagSet.Remove(tag);
            }

            foreach (var tag in bandTagSet.ToList())
            {
                RemoveDrawObject(tag);
                bandTagSet.Remove(tag);
            }
        }

        private Brush CreateBandBrush(Brush baseBrush, int opacity)
        {
            if (baseBrush == null)
                return Brushes.Transparent;

            var clone = baseBrush.Clone();
            clone.Opacity = Math.Max(0, Math.Min(100, opacity)) / 100d;
            clone.Freeze();
            return clone;
        }

        private Brush FreezeBrush(Brush source)
        {
            if (source == null)
                return Brushes.Transparent;

            var clone = source.Clone();
            clone.Freeze();
            return clone;
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
