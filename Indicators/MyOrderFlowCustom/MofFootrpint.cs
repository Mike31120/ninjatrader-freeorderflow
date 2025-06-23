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
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using InvestSoft.NinjaScript.VolumeProfile;
using SharpDX.DirectWrite;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.MyOrderFlowCustom
{
    public class MofFootrpint : Indicator
    {
        private List<MofFootprintBarData> Profiles;
        private SharpDX.Direct2D1.Brush buyBrushDX;
        private SharpDX.Direct2D1.Brush sellBrushDX;
        private SharpDX.Direct2D1.Brush imbalanceUpBrushDX;
        private SharpDX.Direct2D1.Brush imbalanceDownBrushDX;
        private SharpDX.Direct2D1.Brush absorptionBrushDX;
        private SharpDX.Direct2D1.Brush textBrushDX;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"My Order Flow Custom Footprint";
                Name = "MofFootrpint";
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;

                ImbalanceRatio = 3.0;
                AbsorptionVolume = 100;
                StackedLength = 3;

                BuyBrush = Brushes.DarkCyan;
                SellBrush = Brushes.MediumVioletRed;
                ImbalanceUpBrush = Brushes.Goldenrod;
                ImbalanceDownBrush = Brushes.DodgerBlue;
                AbsorptionBrush = Brushes.Yellow;
            }
            else if (State == State.Configure)
            {
                Calculate = Calculate.OnEachTick;
                AddDataSeries(BarsPeriodType.Tick, 1);
                Profiles = new List<MofFootprintBarData>()
                {
                    new MofFootprintBarData(){ StartBar = 0, EndBar = 0 }
                };
            }
            else if (State == State.Historical)
            {
                SetZOrder(-1);
            }
        }

        protected override void OnBarUpdate()
        {
            var profile = Profiles.Last();
            if (BarsInProgress == 1)
            {
                double ask = BarsArray[1].GetAsk(CurrentBar);
                double bid = BarsArray[1].GetBid(CurrentBar);
                long volume = (long)Volumes[1][0];
                long buyVolume = (Closes[1][0] >= ask) ? volume : 0;
                long sellVolume = (Closes[1][0] <= bid) ? volume : 0;
                long otherVolume = (buyVolume == 0 && sellVolume == 0) ? volume : 0;
                profile.UpdateRow(Closes[1][0], buyVolume, sellVolume, otherVolume);
            }
            else
            {
                if (IsFirstTickOfBar && CurrentBar > 0)
                {
                    AnalyzeBar(profile, Highs[0][1], Lows[0][1]);
                    profile = new MofFootprintBarData() { StartBar = CurrentBar, EndBar = CurrentBar };
                    Profiles.Add(profile);
                }
                else
                {
                    profile.EndBar = CurrentBar;
                }
            }
        }

        private void AnalyzeBar(MofFootprintBarData data, double high, double low)
        {
            foreach (var kvp in data)
            {
                var row = kvp.Value;
                if (row.buy >= row.sell * ImbalanceRatio)
                    data.BidImbalances.Add(kvp.Key);
                else if (row.sell >= row.buy * ImbalanceRatio)
                    data.AskImbalances.Add(kvp.Key);
            }
            if (data.ContainsKey(high) && data[high].sell >= AbsorptionVolume)
                data.AskAbsorptions.Add(high);
            if (data.ContainsKey(low) && data[low].buy >= AbsorptionVolume)
                data.BidAbsorptions.Add(low);

            DetectStackedAbsorption(data);
        }

        private void DetectStackedAbsorption(MofFootprintBarData data)
        {
            var askList = data.AskAbsorptions.OrderByDescending(p => p).ToList();
            int count = 1;
            for (int i = 1; i < askList.Count; i++)
            {
                if (Math.Abs(askList[i - 1] - askList[i] - TickSize) < TickSize * 0.1)
                {
                    count++;
                    if (count >= StackedLength)
                        data.StackedAskAbsorptions.UnionWith(askList.GetRange(i - count + 1, count));
                }
                else
                {
                    count = 1;
                }
            }
            var bidList = data.BidAbsorptions.OrderBy(p => p).ToList();
            count = 1;
            for (int i = 1; i < bidList.Count; i++)
            {
                if (Math.Abs(bidList[i] - bidList[i - 1] - TickSize) < TickSize * 0.1)
                {
                    count++;
                    if (count >= StackedLength)
                        data.StackedBidAbsorptions.UnionWith(bidList.GetRange(i - count + 1, count));
                }
                else
                {
                    count = 1;
                }
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;
            var renderer = new MofVolumeProfileChartRenderer(chartControl, chartScale, ChartBars, RenderTarget)
            {
                Opacity = 0.4f,
                ValueAreaOpacity = 0.4f,
                WidthPercent = 1f
            };
            foreach (var profile in Profiles)
            {
                if (profile.MaxVolume == 0) continue;
                if (profile.StartBar > ChartBars.ToIndex || profile.EndBar < ChartBars.FromIndex)
                    continue;
                renderer.RenderBuySellProfile(profile, buyBrushDX, sellBrushDX);
                foreach (double price in profile.AskImbalances)
                {
                    var rect = renderer.GetBarRect(profile, price, profile[price].total, false);
                    RenderTarget.DrawRectangle(rect, imbalanceDownBrushDX, 1);
                }
                foreach (double price in profile.BidImbalances)
                {
                    var rect = renderer.GetBarRect(profile, price, profile[price].total, false);
                    RenderTarget.DrawRectangle(rect, imbalanceUpBrushDX, 1);
                }
                foreach (double price in profile.StackedAskAbsorptions)
                {
                    var rect = renderer.GetBarRect(profile, price, profile[price].total, false);
                    RenderTarget.DrawRectangle(rect, absorptionBrushDX, 2);
                }
                foreach (double price in profile.StackedBidAbsorptions)
                {
                    var rect = renderer.GetBarRect(profile, price, profile[price].total, false);
                    RenderTarget.DrawRectangle(rect, absorptionBrushDX, 2);
                }
                foreach (var kvp in profile.OrderByDescending(p => p.Key))
                {
                    var fullRect = renderer.GetBarRect(profile, kvp.Key, profile.MaxVolume, true);
                    renderer.RnederText(
                        string.Format("{0} X {1}", kvp.Value.sell, kvp.Value.buy),
                        new SharpDX.Vector2(fullRect.Left, fullRect.Top),
                        textBrushDX,
                        fullRect.Width,
                        TextAlignment.Center
                    );
                }
            }
        }

        public override void OnRenderTargetChanged()
        {
            if (buyBrushDX != null) buyBrushDX.Dispose();
            if (sellBrushDX != null) sellBrushDX.Dispose();
            if (imbalanceUpBrushDX != null) imbalanceUpBrushDX.Dispose();
            if (imbalanceDownBrushDX != null) imbalanceDownBrushDX.Dispose();
            if (absorptionBrushDX != null) absorptionBrushDX.Dispose();
            if (textBrushDX != null) textBrushDX.Dispose();
            if (RenderTarget != null)
            {
                buyBrushDX = BuyBrush.ToDxBrush(RenderTarget);
                sellBrushDX = SellBrush.ToDxBrush(RenderTarget);
                imbalanceUpBrushDX = ImbalanceUpBrush.ToDxBrush(RenderTarget);
                imbalanceDownBrushDX = ImbalanceDownBrush.ToDxBrush(RenderTarget);
                absorptionBrushDX = AbsorptionBrush.ToDxBrush(RenderTarget);
                textBrushDX = ChartControl.Properties.ChartText.ToDxBrush(RenderTarget);
            }
        }

        #region Properties
        [XmlIgnore]
        [Display(Name = "Buy Color", Order = 1, GroupName = "Visual")]
        public Brush BuyBrush { get; set; }

        [Browsable(false)]
        public string BuyBrushSerialize
        {
            get { return Serialize.BrushToString(BuyBrush); }
            set { BuyBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Sell Color", Order = 2, GroupName = "Visual")]
        public Brush SellBrush { get; set; }

        [Browsable(false)]
        public string SellBrushSerialize
        {
            get { return Serialize.BrushToString(SellBrush); }
            set { SellBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Imbalance Up Color", Order = 3, GroupName = "Visual")]
        public Brush ImbalanceUpBrush { get; set; }

        [Browsable(false)]
        public string ImbalanceUpBrushSerialize
        {
            get { return Serialize.BrushToString(ImbalanceUpBrush); }
            set { ImbalanceUpBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Imbalance Down Color", Order = 4, GroupName = "Visual")]
        public Brush ImbalanceDownBrush { get; set; }

        [Browsable(false)]
        public string ImbalanceDownBrushSerialize
        {
            get { return Serialize.BrushToString(ImbalanceDownBrush); }
            set { ImbalanceDownBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Absorption Color", Order = 5, GroupName = "Visual")]
        public Brush AbsorptionBrush { get; set; }

        [Browsable(false)]
        public string AbsorptionBrushSerialize
        {
            get { return Serialize.BrushToString(AbsorptionBrush); }
            set { AbsorptionBrush = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Imbalance Ratio", Order = 1, GroupName = "Setup")]
        public double ImbalanceRatio { get; set; }

        [Display(Name = "Absorption Volume", Order = 2, GroupName = "Setup")]
        public long AbsorptionVolume { get; set; }

        [Display(Name = "Stacked Length", Order = 3, GroupName = "Setup")]
        public int StackedLength { get; set; }
        #endregion
    }
}

namespace InvestSoft.NinjaScript.VolumeProfile
{
    internal class MofFootprintBarData : MofVolumeProfileData
    {
        public HashSet<double> AskImbalances = new HashSet<double>();
        public HashSet<double> BidImbalances = new HashSet<double>();
        public HashSet<double> AskAbsorptions = new HashSet<double>();
        public HashSet<double> BidAbsorptions = new HashSet<double>();
        public HashSet<double> StackedAskAbsorptions = new HashSet<double>();
        public HashSet<double> StackedBidAbsorptions = new HashSet<double>();
    }
}
