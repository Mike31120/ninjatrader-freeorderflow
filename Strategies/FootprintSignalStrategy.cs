#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// This namespace holds strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies
{
    public class FootprintSignalStrategy : Strategy
    {
        private FootprintSignalTickReplay signal;

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Imbalance Ratio", Order = 1, GroupName = "Parameters")]
        public double ImbalanceRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Min Volume", Order = 2, GroupName = "Parameters")]
        public long MinVolumeFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stacked Length", Order = 3, GroupName = "Parameters")]
        public int StackedLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Arrow Offset", Order = 4, GroupName = "Parameters")]
        public int ArrowOffset { get; set; }
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Enter trades based on FootprintSignalTickReplay indicator.";
                Name = "FootprintSignalStrategy";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsOverlay = false;

                ImbalanceRatio = 2.0;
                MinVolumeFilter = 50;
                StackedLength = 3;
                ArrowOffset = 2;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                signal = FootprintSignalTickReplay();
                signal.ImbalanceRatio = ImbalanceRatio;
                signal.MinVolumeFilter = MinVolumeFilter;
                signal.StackedLength = StackedLength;
                signal.ArrowOffset = ArrowOffset;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < 1 || BarsInProgress != 0)
                return;

            if (signal[1] > 0 && Position.MarketPosition != MarketPosition.Long)
                EnterLong("Long");
            else if (signal[1] < 0 && Position.MarketPosition != MarketPosition.Short)
                EnterShort("Short");

            if (signal[0] < 0 && Position.MarketPosition == MarketPosition.Long)
                ExitLong("ExitLong", "Long");
            else if (signal[0] > 0 && Position.MarketPosition == MarketPosition.Short)
                ExitShort("ExitShort", "Short");
        }
    }
}
