#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
{
    public class FootprintSignalTickReplay : Indicator
    {
        private class RowData
        {
            public long Buy;
            public long Sell;
        }

        private Dictionary<double, RowData> barData;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Detects absorption using tick replay and plots arrows on the chart.";
                Name = "FootprintSignalTickReplay";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = true;

                ImbalanceRatio = 2.0;
                MinVolumeFilter = 50;
                StackedLength = 3;
                ArrowOffset = 2;

                BuyArrowBrush = Brushes.Lime;
                SellArrowBrush = Brushes.Red;

                AddPlot(Brushes.Transparent, "Signal");
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                barData = new Dictionary<double, RowData>();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 1)
            {
                double price = Closes[1][0];
                double ask = BarsArray[1].GetAsk(CurrentBar);
                double bid = BarsArray[1].GetBid(CurrentBar);
                long volume = (long)Volumes[1][0];
                long buyVolume = price >= ask ? volume : 0;
                long sellVolume = price <= bid ? volume : 0;

                double key = Math.Round(price / TickSize) * TickSize;
                if (!barData.TryGetValue(key, out RowData row))
                {
                    row = new RowData();
                    barData[key] = row;
                }
                row.Buy += buyVolume;
                row.Sell += sellVolume;
            }
            else
            {
                if (IsFirstTickOfBar && CurrentBar > 0)
                {
                    int signal = AnalyzeBar(barData, Closes[0][1]);
                    if (signal > 0)
                    {
                        double price = Lows[0][1] - ArrowOffset * TickSize;
                        Draw.ArrowUp(this, "absBuy" + CurrentBar, false, 1, price, BuyArrowBrush);
                        Values[0][1] = 1;
                    }
                    else if (signal < 0)
                    {
                        double price = Highs[0][1] + ArrowOffset * TickSize;
                        Draw.ArrowDown(this, "absSell" + CurrentBar, false, 1, price, SellArrowBrush);
                        Values[0][1] = -1;
                    }
                    else
                    {
                        Values[0][1] = 0;
                    }
                    barData = new Dictionary<double, RowData>();
                }
            }
        }

        private int AnalyzeBar(Dictionary<double, RowData> data, double close)
        {
            HashSet<double> askAbs = new HashSet<double>();
            HashSet<double> bidAbs = new HashSet<double>();

            foreach (var kvp in data)
            {
                double price = kvp.Key;
                RowData row = kvp.Value;
                data.TryGetValue(price + TickSize, out RowData above);
                data.TryGetValue(price - TickSize, out RowData below);

                long belowSell = below != null ? below.Sell : 0;
                long aboveBuy = above != null ? above.Buy : 0;

                if (row.Buy >= belowSell * ImbalanceRatio && row.Buy >= MinVolumeFilter && close < price)
                    askAbs.Add(price);
                if (row.Sell >= aboveBuy * ImbalanceRatio && row.Sell >= MinVolumeFilter && close > price)
                    bidAbs.Add(price);
            }

            bool askStacked = HasStacked(askAbs, true);
            bool bidStacked = HasStacked(bidAbs, false);

            if (askAbs.Count > 0 || askStacked)
                return 1;
            if (bidAbs.Count > 0 || bidStacked)
                return -1;
            return 0;
        }

        private bool HasStacked(HashSet<double> levels, bool descending)
        {
            if (levels.Count == 0)
                return false;
            var list = descending ? levels.OrderByDescending(p => p).ToList() : levels.OrderBy(p => p).ToList();
            int count = 1;
            for (int i = 1; i < list.Count; i++)
            {
                if (Math.Abs(list[i - 1] - list[i] - TickSize) < TickSize * 0.1)
                {
                    count++;
                    if (count >= StackedLength)
                        return true;
                }
                else
                {
                    count = 1;
                }
            }
            return false;
        }

        #region Properties
        [Display(Name = "Imbalance Ratio", Order = 1, GroupName = "Parameters")]
        public double ImbalanceRatio { get; set; }

        [Display(Name = "Min Volume", Order = 2, GroupName = "Parameters")]
        public long MinVolumeFilter { get; set; }

        [Display(Name = "Stacked Length", Order = 3, GroupName = "Parameters")]
        public int StackedLength { get; set; }

        [Display(Name = "Arrow Offset", Order = 4, GroupName = "Visual")]
        public int ArrowOffset { get; set; }

        [XmlIgnore]
        [Display(Name = "Buy Arrow Color", Order = 5, GroupName = "Visual")]
        public Brush BuyArrowBrush { get; set; }

        [Browsable(false)]
        public string BuyArrowBrushSerialize
        {
            get { return Serialize.BrushToString(BuyArrowBrush); }
            set { BuyArrowBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Sell Arrow Color", Order = 6, GroupName = "Visual")]
        public Brush SellArrowBrush { get; set; }

        [Browsable(false)]
        public string SellArrowBrushSerialize
        {
            get { return Serialize.BrushToString(SellArrowBrush); }
            set { SellArrowBrush = Serialize.StringToBrush(value); }
        }
        #endregion
    }
}
