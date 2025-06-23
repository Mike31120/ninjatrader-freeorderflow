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
        private SharpDX.Direct2D1.Brush askHighlightBrushDX;
        private SharpDX.Direct2D1.Brush bidHighlightBrushDX;

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
                MinVolumeFilter = 50;
                AbsorptionVolume = 100;
                StackedLength = 3;

                BuyBrush = Brushes.DarkCyan;
                SellBrush = Brushes.MediumVioletRed;
                ImbalanceUpBrush = Brushes.Goldenrod;
                ImbalanceDownBrush = Brushes.DodgerBlue;
                AbsorptionBrush = Brushes.Yellow;
                AskHighlightBrush = Brushes.Green;
                BidHighlightBrush = Brushes.Red;
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
                    AnalyzeBar(profile, Highs[0][1], Lows[0][1], Closes[0][1]);
                    profile = new MofFootprintBarData() { StartBar = CurrentBar, EndBar = CurrentBar };
                    Profiles.Add(profile);
                }
                else
                {
                    profile.EndBar = CurrentBar;
                }
            }
        }

        private void AnalyzeBar(MofFootprintBarData data, double high, double low, double close)
        {
            foreach (var kvp in data)
            {
                var price = kvp.Key;
                var row = kvp.Value;
                var above = data.GetValueOrDefault(price + TickSize);
                var below = data.GetValueOrDefault(price - TickSize);
                if (row.buy >= below.sell * ImbalanceRatio && row.buy >= MinVolumeFilter)
                {
                    data.BidImbalances.Add(price);
                    if (close < price)
                        data.AskAbsorptions.Add(price);
                }
                if (row.sell >= above.buy * ImbalanceRatio && row.sell >= MinVolumeFilter)
                {
                    data.AskImbalances.Add(price);
                    if (close > price)
                        data.BidAbsorptions.Add(price);
                }
            }
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
                foreach (double price in profile.AskAbsorptions)
                {
                    var rect = renderer.GetBarRect(profile, price, profile[price].total, false);
                    RenderTarget.DrawRectangle(rect, bidHighlightBrushDX, 1);
                }

                foreach (double price in profile.BidAbsorptions)
                {
                    var rect = renderer.GetBarRect(profile, price, profile[price].total, false);
                    RenderTarget.DrawRectangle(rect, askHighlightBrushDX, 1);
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
                    float half = fullRect.Width / 2f;
                    var sellPos = new SharpDX.Vector2(fullRect.Left, fullRect.Top);
                    var buyPos = new SharpDX.Vector2(fullRect.Left + half, fullRect.Top);
                    bool askStrong = profile.AskImbalances.Contains(kvp.Key);
                    bool bidStrong = profile.BidImbalances.Contains(kvp.Key);
                    var sellBrush = askStrong ? bidHighlightBrushDX : textBrushDX;
                    var buyBrush = bidStrong ? askHighlightBrushDX : textBrushDX;
                    if (askStrong)
                        renderer.RenderBoldText(kvp.Value.sell.ToString(), sellPos, sellBrush, half, TextAlignment.Center);
                    else
                        renderer.RnederText(kvp.Value.sell.ToString(), sellPos, sellBrush, half, TextAlignment.Center);
                    if (bidStrong)
                        renderer.RenderBoldText(kvp.Value.buy.ToString(), buyPos, buyBrush, half, TextAlignment.Center);
                    else
                        renderer.RnederText(kvp.Value.buy.ToString(), buyPos, buyBrush, half, TextAlignment.Center);
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
            if (askHighlightBrushDX != null) askHighlightBrushDX.Dispose();
            if (bidHighlightBrushDX != null) bidHighlightBrushDX.Dispose();
            if (RenderTarget != null)
            {
                buyBrushDX = BuyBrush.ToDxBrush(RenderTarget);
                sellBrushDX = SellBrush.ToDxBrush(RenderTarget);
                imbalanceUpBrushDX = ImbalanceUpBrush.ToDxBrush(RenderTarget);
                imbalanceDownBrushDX = ImbalanceDownBrush.ToDxBrush(RenderTarget);
                absorptionBrushDX = AbsorptionBrush.ToDxBrush(RenderTarget);
                textBrushDX = ChartControl.Properties.ChartText.ToDxBrush(RenderTarget);
                askHighlightBrushDX = AskHighlightBrush.ToDxBrush(RenderTarget);
                bidHighlightBrushDX = BidHighlightBrush.ToDxBrush(RenderTarget);
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

        [XmlIgnore]
        [Display(Name = "Ask Highlight", Order = 6, GroupName = "Visual")]
        public Brush AskHighlightBrush { get; set; }

        [Browsable(false)]
        public string AskHighlightBrushSerialize
        {
            get { return Serialize.BrushToString(AskHighlightBrush); }
            set { AskHighlightBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bid Highlight", Order = 7, GroupName = "Visual")]
        public Brush BidHighlightBrush { get; set; }

        [Browsable(false)]
        public string BidHighlightBrushSerialize
        {
            get { return Serialize.BrushToString(BidHighlightBrush); }
            set { BidHighlightBrush = Serialize.StringToBrush(value); }
        }

        [Display(Name = "Imbalance Ratio", Order = 1, GroupName = "Setup")]
        public double ImbalanceRatio { get; set; }

        [Display(Name = "Min Volume Filter", Order = 2, GroupName = "Setup")]
        public long MinVolumeFilter { get; set; }

        [Display(Name = "Absorption Volume", Order = 3, GroupName = "Setup")]
        public long AbsorptionVolume { get; set; }

        [Display(Name = "Stacked Length", Order = 4, GroupName = "Setup")]
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
