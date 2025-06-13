#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
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
        private readonly HashSet<string> currentTags = new HashSet<string>();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description  = "Draws global HVN/LVN lines calculated by MofRangeVolumeProfile.";
                Name         = "MOF Global Level Lines";
                IsOverlay    = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
            }
        }

        protected override void OnBarUpdate()
        {
            string instrument = Instrument.FullName;
            List<double> hvnList = null;
            List<double> lvnList = null;
            MofRangeVolumeProfile.GlobalHvnLevels.TryGetValue(instrument, out hvnList);
            MofRangeVolumeProfile.GlobalLvnLevels.TryGetValue(instrument, out lvnList);

            UpdateLines(hvnList ?? new List<double>(), "HVN", Brushes.Gold);
            UpdateLines(lvnList ?? new List<double>(), "LVN", Brushes.Lime);
        }

        private void UpdateLines(List<double> levels, string prefix, Brush brush)
        {
            int decimals = (int)Math.Max(0, Math.Round(-Math.Log10(Instrument.MasterInstrument.TickSize)));
            var desiredTags = new HashSet<string>(levels.Select(p => $"MOF_{prefix}_{Math.Round(p, decimals)}"));

            foreach (var tag in currentTags.ToList())
            {
                if (!desiredTags.Contains(tag))
                {
                    RemoveDrawObject(tag);
                    currentTags.Remove(tag);
                }
            }

            foreach (double price in levels)
            {
                string tag = $"MOF_{prefix}_{Math.Round(price, decimals)}";
                if (!currentTags.Contains(tag))
                {
                    var line = Draw.HorizontalLine(this, tag, price, brush);
                    line.IsGlobalDrawingTool = true;
                    line.IsLocked = true;
                    currentTags.Add(tag);
                }
            }
        }
    }
}
