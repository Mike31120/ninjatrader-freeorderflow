#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
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
                UpdateLines(hvnList ?? new List<double>(), "HVN", HvnStroke, currentHvnTags);
            if (ShowLvn)
                UpdateLines(lvnList ?? new List<double>(), "LVN", LvnStroke, currentLvnTags);
        }

        private void UpdateLines(List<double> levels, string prefix, Stroke stroke, HashSet<string> tagSet)
        {
            int decimals = (int)Math.Max(0, Math.Round(-Math.Log10(Instrument.MasterInstrument.TickSize)));
            var desiredTags = new HashSet<string>(levels.Select(p => $"MOF_{prefix}_{Math.Round(p, decimals)}"));

            foreach (var tag in tagSet.ToList())
            {
                if (!desiredTags.Contains(tag))
                {
                    RemoveDrawObject(tag);
                    tagSet.Remove(tag);
                }
            }

            foreach (double price in levels)
            {
                string tag = $"MOF_{prefix}_{Math.Round(price, decimals)}";
                if (!tagSet.Contains(tag))
                {
                    var line = Draw.HorizontalLine(this, tag, price, stroke.Brush,
                        stroke.DashStyleHelper, (int)stroke.Width, true);
                    line.IsLocked = true;
                    tagSet.Add(tag);
                }
            }
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
        #endregion
    }
}
