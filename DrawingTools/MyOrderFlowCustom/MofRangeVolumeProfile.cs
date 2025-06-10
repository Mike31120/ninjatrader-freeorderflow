#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;
using System.Xml.Serialization;
using Brush = System.Windows.Media.Brush;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using InvestSoft.NinjaScript.VolumeProfile;
#endregion

//This namespace holds Drawing tools in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.DrawingTools
{
    public class MofRangeVolumeProfile : Rectangle
    {
        #region Icon
        public override object Icon
        {
            get
            {
                Grid icon = new Grid { Height = 16, Width = 16, UseLayoutRounding = true };
                RenderOptions.SetEdgeMode(icon, EdgeMode.Aliased);
                icon.Children.Add(new Path
                {
                    Stroke = Application.Current.TryFindResource("MenuBorderBrush") as Brush,
                    StrokeThickness = 1,
                    Data = Geometry.Parse("M 0 1 H 10 V 3 H 0 M 0 5 H 13 V 7 H 0 M 0 9 H 8 V 11 H 0 M 0 13 H 4 V 15 H 0 M 0 0 V 16 M 16 0 V 16")
                });
                return icon;
            }
        }
        #endregion

        private ChartAnchor firstAnchor;
        private ChartAnchor lastAnchor;
        private double MaxPrice;
        private double MinPrice;
        private int StartBar = -1;
        private int EndBar = -1;
        private BarsRequest BarsRequest;
        private MofVolumeProfileData profile;
        private SharpDX.Direct2D1.Brush volumeBrushDX;
        private SharpDX.Direct2D1.Brush buyBrushDX;
        private SharpDX.Direct2D1.Brush sellBrushDX;
        private SharpDX.Direct2D1.Brush textBrushDX;
        private ChartBars ChartBars { get { return AttachedTo.ChartObject as ChartBars; } }
        private bool autoUpdateEndTime;

        #region OnStateChange
        protected override void OnStateChange()
        {
            base.OnStateChange();
            if (State == State.SetDefaults)
            {
                Name = "Fixed Range Volume Profile (My Order Flow Custom)";
                Description = @"My Order Flow Custom fixed range volume profile";
                AreaOpacity = 5;
                AreaBrush = Brushes.Silver;
                OutlineStroke = new Stroke(Brushes.Gray, DashStyleHelper.Dash, 1, 50);

                // Setup
                ResolutionMode = MofVolumeProfileResolution.Tick;
                Resolution = 1;
                ValueArea = 70;
                DisplayTotal = false;
                AutoUpdateEndTime = false;

                // Visual
                Width = 60;
                Opacity = 40;
                ValueAreaOpacity = 80;
                ShowPoc = true;
                ShowValueArea = true;
                VolumeBrush = Brushes.CornflowerBlue;
                BuyBrush = Brushes.DarkCyan;
                SellBrush = Brushes.MediumVioletRed;
                PocStroke = new Stroke(Brushes.Goldenrod, 1);
                ValueAreaStroke = new Stroke(Brushes.CornflowerBlue, DashStyleHelper.Dash, 1);
            }
            else if (State == State.Configure)
            {
                ZOrderType = DrawingToolZOrder.AlwaysDrawnFirst;
                ZOrder = -1;
            }
        }
        #endregion

        #region Calculation
        private void CaculateVolumeProfile()
        {
            Bars chartBars = (AttachedTo.ChartObject as ChartBars).Bars;
            // Create a temporary profile to hold the new calculation. The
            // existing profile will remain displayed until this calculation
            // is finished and the new profile replaces it.
            MofVolumeProfileData newProfile = new MofVolumeProfileData()
            {
                StartBar = StartBar,
                EndBar = EndBar
            };

            if (BarsRequest != null)
            {
                BarsRequest = null;
            }
            BarsRequest = new BarsRequest(
                chartBars.Instrument,
                firstAnchor.Time,
                lastAnchor.Time
            )
            {
                BarsPeriod = new BarsPeriod()
                {
                    BarsPeriodType = BarsPeriodType.Tick,
                    Value = 1
                }
            };
            BarsRequest.Request((request, errorCode, errorMessage) =>
            {
                if (request != BarsRequest || State >= State.Terminated) return;
                if (errorCode != Cbi.ErrorCode.NoError)
                {
                    request.Dispose();
                    request = null;
                    return;
                }
                // calculate volume profile from bars into the temporary profile
                for (int i = 0; i < request.Bars.Count; i++)
                {
                    if (
                        request.Bars.BarsSeries.GetTime(i) < firstAnchor.Time ||
                        request.Bars.BarsSeries.GetTime(i) > lastAnchor.Time
                    ) continue;
                    double ask = request.Bars.BarsSeries.GetAsk(i);
                    double bid = request.Bars.BarsSeries.GetBid(i);
                    double close = request.Bars.BarsSeries.GetClose(i);
                    long volume = request.Bars.BarsSeries.GetVolume(i);

                    long buyVolume = (close >= ask) ? volume : 0;
                    long sellVolume = (close <= bid) ? volume : 0;

                    newProfile.UpdateRow(close, buyVolume, sellVolume, 0);
                }
                newProfile.CalculateValueArea(ValueArea / 100f);
                // Replace the displayed profile only after calculation completes
                profile = newProfile;
                ForceRefresh();
            });
        }

        private void CalcAnchorPrice()
        {
            MaxPrice = ChartBars.Bars.GetHigh(StartBar);
            MinPrice = ChartBars.Bars.GetLow(StartBar);

            for (int i = StartBar + 1; i <= EndBar; i++)
            {
                MaxPrice = Math.Max(ChartBars.Bars.GetHigh(i), MaxPrice);
                MinPrice = Math.Min(ChartBars.Bars.GetLow(i), MinPrice);
            }

            EndAnchor.SlotIndex = EndBar;
            StartAnchor.Price = MaxPrice;
            EndAnchor.Price = MinPrice;
        }
        #endregion

        #region Rendering
        public override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (StartAnchor.SlotIndex < 0 || EndAnchor.SlotIndex < 0) return;

            if (AutoUpdateEndTime && DrawingState == DrawingState.Normal && ChartBars != null && ChartBars.Count > 0)
            {
                EndAnchor.SlotIndex = ChartBars.Count - 1;
                EndAnchor.Time = ChartBars.Bars.GetTime(ChartBars.Count - 1);
            }

            if (DrawingState == DrawingState.Normal)
            {
                // set real anchor sequence
                if (StartAnchor.SlotIndex > EndAnchor.SlotIndex)
                {
                    firstAnchor = EndAnchor;
                    lastAnchor = StartAnchor;
                }
                else
                {
                    firstAnchor = StartAnchor;
                    lastAnchor = EndAnchor;
                }
                // check anchor changed
                if (StartBar != firstAnchor.SlotIndex || EndBar != lastAnchor.SlotIndex)
                {
                    StartBar = (int) firstAnchor.SlotIndex;
                    EndBar = (int) lastAnchor.SlotIndex;
                    if (EndBar >= ChartBars.Count - 1) EndBar = ChartBars.Count - 1;
                    CalcAnchorPrice();
                    CaculateVolumeProfile();
                }
            }
            base.OnRender(chartControl, chartScale);
            textBrushDX = chartControl.Properties.ChartText.ToDxBrush(RenderTarget);
            var volProfileRenderer = new MofVolumeProfileChartRenderer(chartControl, chartScale, ChartBars, RenderTarget)
            {
                Opacity = Opacity / 100f,
                ValueAreaOpacity = ValueAreaOpacity / 100f,
                WidthPercent = Width / 100f
            };
            if (profile != null && profile.TotalVolume > 0)
            {
                if (DisplayMode == MofVolumeProfileMode.BuySell)
                {
                    volProfileRenderer.RenderBuySellProfile(profile, buyBrushDX, sellBrushDX);
                }
                else
                {
                    volProfileRenderer.RenderProfile(profile, volumeBrushDX);
                }
                if (ShowPoc) volProfileRenderer.RenderPoc(profile, PocStroke.BrushDX, PocStroke.Width, PocStroke.StrokeStyle);
                if (ShowValueArea) volProfileRenderer.RenderValueArea(profile, ValueAreaStroke.BrushDX, ValueAreaStroke.Width, ValueAreaStroke.StrokeStyle);
                if (DisplayMode == MofVolumeProfileMode.Delta)
                {
                    volProfileRenderer.RenderDeltaProfile(profile, buyBrushDX, sellBrushDX);
                }
                if (DisplayTotal)
                {
                    volProfileRenderer.RenderTotalVolume(profile, textBrushDX);
                }
            }

        }

        public override void OnRenderTargetChanged()
        {
            if (volumeBrushDX != null) volumeBrushDX.Dispose();
            if (buyBrushDX != null) buyBrushDX.Dispose();
            if (sellBrushDX != null) sellBrushDX.Dispose();
            if (RenderTarget != null)
            {
                volumeBrushDX = VolumeBrush.ToDxBrush(RenderTarget);
                buyBrushDX = BuyBrush.ToDxBrush(RenderTarget);
                sellBrushDX = SellBrush.ToDxBrush(RenderTarget);
                PocStroke.RenderTarget = RenderTarget;
                ValueAreaStroke.RenderTarget = RenderTarget;
            }
        }

        private Rect GetAnchorsRect(ChartControl chartControl, ChartScale chartScale)
        {
            if (StartAnchor == null || EndAnchor == null)
                return new Rect();

            ChartPanel chartPanel = chartControl.ChartPanels[chartScale.PanelIndex];
            Point startPoint = StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
            Point endPoint = EndAnchor.GetPoint(chartControl, chartPanel, chartScale);

            // rect doesnt handle negative width/height so we need to determine and wind it up ourselves
            // make sure to always use smallest left/top anchor for start
            double left = Math.Min(endPoint.X, startPoint.X);
            double top = Math.Min(endPoint.Y, startPoint.Y);
            double width = Math.Abs(endPoint.X - startPoint.X);
            double height = Math.Abs(endPoint.Y - startPoint.Y);
            return new Rect(left, top, width, height);
        }
        #endregion

        #region Properties
        [Display(Name = "Display mode", Description = "Profile mode to render", Order = 1, GroupName = "Setup")]
        public MofVolumeProfileMode DisplayMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Resolution Mode", Description = "Calculate profile from region", Order = 2, GroupName = "Setup")]
        public MofVolumeProfileResolution ResolutionMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Resolution", Description = "Calculate profile from region", Order = 3, GroupName = "Setup")]
        public int Resolution { get; set; }

        [Range(10, 90)]
        [Display(Name = "Value Area (%)", Description = "Value area percentage", Order = 7, GroupName = "Setup")]
        public int ValueArea { get; set; }

        [Display(Name = "Display Total Volume", Order = 8, GroupName = "Setup")]
        public bool DisplayTotal { get; set; }

        [Display(Name = "Auto Update End Time", Order = 9, GroupName = "Setup")]
        public bool AutoUpdateEndTime
        {
            get { return autoUpdateEndTime; }
            set { autoUpdateEndTime = value; }
        }

        [Display(Name = "Profile width (%)", Description = "Width of bars relative to range", Order = 1, GroupName = "Visual")]
        public int Width { get; set; }

        [Range(1, 100)]
        [Display(Name = "Profile opacity (%)", Description = "Opacity of bars out value area", Order = 2, GroupName = "Visual")]
        public int Opacity { get; set; }

        [Range(1, 100)]
        [Display(Name = "Value area opacity (%)", Description = "Opacity of bars in value area", Order = 2, GroupName = "Visual")]
        public int ValueAreaOpacity { get; set; }

        [Display(Name = "Show POC", Description = "Show PoC line", Order = 5, GroupName = "Setup")]
        public bool ShowPoc { get; set; }

        [Display(Name = "Show Value Area", Description = "Show value area high and low lines", Order = 6, GroupName = "Setup")]
        public bool ShowValueArea { get; set; }

        [XmlIgnore]
        [Display(Name = "Color for profile", Order = 10, GroupName = "Visual")]
        public Brush VolumeBrush { get; set; }

        [Browsable(false)]
        public string VolumeBrushSerialize
        {
            get { return Serialize.BrushToString(VolumeBrush); }
            set { VolumeBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Color for buy", Order = 11, GroupName = "Visual")]
        public Brush BuyBrush { get; set; }

        [Browsable(false)]
        public string BuyBrushSerialize
        {
            get { return Serialize.BrushToString(BuyBrush); }
            set { BuyBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Color for sell", Order = 12, GroupName = "Visual")]
        public Brush SellBrush { get; set; }

        [Browsable(false)]
        public string SellBrushSerialize
        {
            get { return Serialize.BrushToString(SellBrush); }
            set { SellBrush = Serialize.StringToBrush(value); }
        }

        // Lines
        [Display(Name = "POC", Order = 8, GroupName = "Lines")]
        public Stroke PocStroke { get; set; }

        [Display(Name = "Value Area", Order = 9, GroupName = "Lines")]
        public Stroke ValueAreaStroke { get; set; }
        #endregion
    }
}
