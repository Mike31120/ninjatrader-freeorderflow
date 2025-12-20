```csharp
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using InvestSoft.NinjaScript.VolumeProfile;
using DX = SharpDX;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.MyOrderFlowCustom
{
    public class MofVolumeProfile : Indicator
    {
        private List<MofVolumeProfileData> Profiles;
        private int LastBar;

        private SharpDX.Direct2D1.Brush volumeBrushDX;
        private SharpDX.Direct2D1.Brush buyBrushDX;
        private SharpDX.Direct2D1.Brush sellBrushDX;
        private SharpDX.Direct2D1.Brush outlineBrushDX;
        private SharpDX.Direct2D1.Brush backgroundBrushDX;
        private SharpDX.Direct2D1.Brush hvnHighlightBrushDX;
        private SharpDX.Direct2D1.Brush lvnHighlightBrushDX;
        private SharpDX.Direct2D1.Brush pocHighlightBrushDX;
        private SharpDX.Direct2D1.Brush totalTextBrushDX;
        private SharpDX.Direct2D1.Brush volumeTextBrushDX;

        private static readonly Dictionary<string, List<double>> globalHvnLevels = new Dictionary<string, List<double>>();
        private static readonly Dictionary<string, List<double>> globalLvnLevels = new Dictionary<string, List<double>>();

        // --- ZigZag (for crossing segments) ---
        // IMPORTANT: inside an Indicator, "ZigZag" can also refer to the plot/series member name
        // => use fully-qualified class name + Indicators.Indicator.ZigZag(...) call form.
        private NinjaTrader.NinjaScript.Indicators.ZigZag zz;

        #region OnStateChange
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"My Order Flow Custom Volume Profile";
                Name = "Volume Profile";
                IsChartOnly = true;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;

                // Setup
                DisplayMode = MofVolumeProfileMode.Standard;
                ResolutionMode = MofVolumeProfileResolution.Tick;
                Resolution = 1;
                ValueArea = 70;
                DisplayTotal = true;

                // Visual
                Width = 60;
                MaxWidthPixels = 120;
                Opacity = 40;
                ValueAreaOpacity = 80;
                ShowPoc = true;
                ShowValueArea = true;
                VolumeBrush = Brushes.CornflowerBlue;
                BuyBrush = Brushes.DarkCyan;
                SellBrush = Brushes.MediumVioletRed;
                OutlineBrush = Brushes.Black;
                ProfileBackgroundBrush = Brushes.Transparent;
                HvnHighlightBrush = Brushes.Yellow;
                LvnHighlightBrush = Brushes.LawnGreen;
                PocHighlightBrush = Brushes.Goldenrod;
                PocStroke = new Stroke(Brushes.Goldenrod, 1);
                ValueAreaStroke = new Stroke(Brushes.CornflowerBlue, DashStyleHelper.Dash, 1);
                ShowVolumeText = false;
                VolumeTextSize = 12;
                VolumeTextOpacity = 100;
                VolumeTextBrush = Brushes.White;

                // HVN/LVN defaults
                SmoothingWindow = 2;
                NeighborBars = 2;
                MinVolumePctOfPoc = 10;
                MinDistanceTicks = 1;
                MaxLevels = 5;
                ShowHvn = true;
                ShowLvn = true; // bands + zigzag-cross treat only HVN, LVN display can remain optional
                HvnStroke = new Stroke(Brushes.Yellow, DashStyleHelper.Dash, 1);
                LvnStroke = new Stroke(Brushes.LawnGreen, DashStyleHelper.Dash, 1);
                UseGlobalLevels = false;

                // Bands (HVN only)
                BandTicks = 4;
                HvnBandBrush = new SolidColorBrush(Colors.Gold);
                HvnBandOpacity = 40;

                // Band boundaries
                ShowBandBoundaries = true;
                BandBoundaryStroke = new Stroke(Brushes.Gray, DashStyleHelper.Dash, 1);

                // --- ZigZag parameters ---
                ShowZigZagCrossSegments = true;
                ZigZagDeviationType = DeviationType.Points;
                ZigZagDeviationValue = 0.5;
                ZigZagUseHighLow = false;

                ZigZagCrossUpStroke = new Stroke(Brushes.LimeGreen, DashStyleHelper.Solid, 2);
                ZigZagCrossDownStroke = new Stroke(Brushes.Red, DashStyleHelper.Solid, 2);
            }
            else if (State == State.Configure)
            {
                Calculate = Calculate.OnEachTick;

                // Add lower timeframe data series
                AddDataSeries((ResolutionMode == MofVolumeProfileResolution.Tick) ? BarsPeriodType.Tick : BarsPeriodType.Minute, Resolution);

                // Init volume profiles list
                Profiles = new List<MofVolumeProfileData>()
                {
                    new MofVolumeProfileData() { StartBar = 0 }
                };
            }
            else if (State == State.DataLoaded)
            {
                // FIX: call the indicator factory method via base class "Indicator" explicitly to avoid
                // the CS1955 "Non-invocable member 'ZigZag' cannot be used like a method."
                zz = NinjaTrader.NinjaScript.Indicators.Indicator.ZigZag(this.Input, ZigZagDeviationType, ZigZagDeviationValue, ZigZagUseHighLow);
            }
            else if (State == State.Historical)
            {
                SetZOrder(-1);
            }
        }
        #endregion

        #region Calculations
        protected override void OnBarUpdate()
        {
            var profile = Profiles.Last();

            if (BarsInProgress == 1)
            {
                long buyVolume, sellVolume, otherVolume;

                if (ResolutionMode == MofVolumeProfileResolution.Tick && Resolution == 1)
                {
                    // 1 tick uses bid and ask price
                    var ask = BarsArray[1].GetAsk(CurrentBar);
                    var bid = BarsArray[1].GetBid(CurrentBar);

                    buyVolume = (Closes[1][0] >= ask) ? (long)Volumes[1][0] : 0;
                    sellVolume = (Closes[1][0] <= bid) ? (long)Volumes[1][0] : 0;
                    otherVolume = (Closes[1][0] < ask && Closes[1][0] > bid) ? (long)Volumes[1][0] : 0;
                }
                else
                {
                    buyVolume = Closes[1][0] > Opens[1][0] ? (long)Volumes[1][0] : 0;
                    sellVolume = Closes[1][0] < Opens[1][0] ? (long)Volumes[1][0] : 0;
                    otherVolume = 0;
                }

                profile.UpdateRow(Closes[1][0], buyVolume, sellVolume, otherVolume);
            }
            else // BarsInProgress == 0
            {
                if (State == State.Realtime || IsFirstTickOfBar)
                {
                    profile.CalculateValueArea(ValueArea / 100f);
                    DetectLevels(profile);

                    if (UseGlobalLevels)
                    {
                        globalHvnLevels[Instrument.FullName] = new List<double>(profile.HvnLevels);
                        globalLvnLevels[Instrument.FullName] = new List<double>(profile.LvnLevels);
                    }
                }

                // update profile end bar
                if (CurrentBar != profile.EndBar)
                    profile.EndBar = CurrentBar;

                if (
                    IsFirstTickOfBar &&
                    (Period == MofVolumeProfilePeriod.Bars ||
                     (Period == MofVolumeProfilePeriod.Sessions && Bars.IsFirstBarOfSession))
                )
                {
                    if (State != State.Realtime)
                    {
                        profile.CalculateValueArea(ValueArea / 100f);
                        DetectLevels(profile);

                        if (UseGlobalLevels)
                        {
                            globalHvnLevels[Instrument.FullName] = new List<double>(profile.HvnLevels);
                            globalLvnLevels[Instrument.FullName] = new List<double>(profile.LvnLevels);
                        }
                    }

                    Profiles.Add(new MofVolumeProfileData() { StartBar = CurrentBar, EndBar = CurrentBar });
                }
            }
        }

        private void DetectLevels(MofVolumeProfileData prof)
        {
            prof.HvnLevels.Clear();
            prof.LvnLevels.Clear();
            prof.HvnZones.Clear();
            prof.LvnZones.Clear();
            var prices = prof.Keys.OrderBy(p => p).ToList();
            if (prices.Count == 0) return;
            var vols = prices.Select(p => (double)prof[p].total).ToList();

            int w = Math.Max(1, SmoothingWindow);
            List<double> smooth = new List<double>(prices.Count);
            for (int i = 0; i < prices.Count; i++)
            {
                int s = Math.Max(0, i - w);
                int e = Math.Min(prices.Count - 1, i + w);
                double sum = 0;
                for (int j = s; j <= e; j++) sum += vols[j];
                smooth.Add(sum / (e - s + 1));
            }

            int n = Math.Max(1, NeighborBars);
            double minVol = prof.ContainsKey(prof.POC) ? prof[prof.POC].total * (MinVolumePctOfPoc / 100.0) : 0;
            double tick = Instrument.MasterInstrument.TickSize;

            const double EPS = 1e-8;
            for (int i = 0; i < prices.Count; )
            {
                int start = i;
                int end = i;
                while (end + 1 < prices.Count && Math.Abs(smooth[end + 1] - smooth[end]) < EPS)
                    end++;

                double v = smooth[i];
                bool higher = true;
                bool lower = true;
                for (int k = 1; k <= n; k++)
                {
                    if (start - k >= 0)
                    {
                        if (smooth[start - k] >= v) higher = false;
                        if (smooth[start - k] <= v) lower = false;
                    }
                    if (end + k < prices.Count)
                    {
                        if (smooth[end + k] >= v) higher = false;
                        if (smooth[end + k] <= v) lower = false;
                    }
                }

                if (higher && v >= minVol)
                {
                    for (int j = start; j <= end; j++)
                        prof.HvnZones.Add(prices[j]);

                    int idx = GetPlateauIndex(start, end, vols, true, PlateauSelectionMode.Central);
                    int extremeIdx = idx;
                    double extremeVol = vols[idx];
                    for (int j = 1; j <= n; j++)
                    {
                        int left = idx - j;
                        if (left >= 0 && vols[left] > extremeVol)
                        {
                            extremeVol = vols[left];
                            extremeIdx = left;
                        }
                        int right = idx + j;
                        if (right < vols.Count && vols[right] > extremeVol)
                        {
                            extremeVol = vols[right];
                            extremeIdx = right;
                        }
                    }

                    double price = prices[extremeIdx];
                    if (prof.HvnLevels.All(p => Math.Abs(p - price) > tick * MinDistanceTicks))
                        prof.HvnLevels.Add(price);
                }

                if (lower)
                {
                    for (int j = start; j <= end; j++)
                        prof.LvnZones.Add(prices[j]);

                    int idx = GetPlateauIndex(start, end, vols, false, PlateauSelectionMode.Central);
                    int extremeIdx = idx;
                    double extremeVol = vols[idx];
                    for (int j = 1; j <= n; j++)
                    {
                        int left = idx - j;
                        if (left >= 0 && vols[left] < extremeVol)
                        {
                            extremeVol = vols[left];
                            extremeIdx = left;
                        }
                        int right = idx + j;
                        if (right < vols.Count && vols[right] < extremeVol)
                        {
                            extremeVol = vols[right];
                            extremeIdx = right;
                        }
                    }

                    double price = prices[extremeIdx];
                    if (prof.LvnLevels.All(p => Math.Abs(p - price) > tick * MinDistanceTicks))
                        prof.LvnLevels.Add(price);
                }

                i = end + 1;
            }

            prof.HvnLevels = prof.HvnLevels.OrderByDescending(p => prof[p].total).Take(MaxLevels).ToList();
            prof.LvnLevels = prof.LvnLevels.OrderBy(p => prof[p].total).Take(MaxLevels).ToList();
        }

        private int GetPlateauIndex(int start, int end, List<double> vols, bool chooseMax, PlateauSelectionMode mode)
        {
            double extreme = vols[start];
            List<int> indices = new List<int> { start };
            for (int i = start + 1; i <= end; i++)
            {
                double vv = vols[i];
                if (chooseMax ? vv > extreme : vv < extreme)
                {
                    extreme = vv;
                    indices.Clear();
                    indices.Add(i);
                }
                else if (vv == extreme)
                {
                    indices.Add(i);
                }
            }

            switch (mode)
            {
                case PlateauSelectionMode.Highest:
                    return indices[indices.Count - 1];
                case PlateauSelectionMode.Central:
                    return indices[indices.Count / 2];
                default:
                    return indices[0];
            }
        }
        #endregion

        #region Rendering
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (RenderTarget == null)
                return;

            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;

            var volProfileRenderer = new MofVolumeProfileChartRenderer(ChartControl, chartScale, ChartBars, RenderTarget)
            {
                Opacity = Opacity / 100f,
                ValueAreaOpacity = ValueAreaOpacity / 100f,
                WidthPercent = Width / 100f,
                OutlineBrush = outlineBrushDX,
                MaxWidthPixels = Math.Max(0, MaxWidthPixels),
                BackgroundBrush = backgroundBrushDX
            };

            totalTextBrushDX = chartControl.Properties.ChartText.ToDxBrush(RenderTarget);
            if (volumeTextBrushDX != null)
                volumeTextBrushDX.Opacity = VolumeTextOpacity / 100f;

            foreach (var profile in Profiles)
            {
                if (
                    profile.MaxVolume == 0 ||
                    (profile.StartBar < ChartBars.FromIndex && profile.EndBar < ChartBars.FromIndex) ||
                    (profile.StartBar > ChartBars.ToIndex && profile.EndBar > ChartBars.ToIndex)
                ) continue;

                volProfileRenderer.RenderBackground(profile);

                if (DisplayMode == MofVolumeProfileMode.BuySell)
                {
                    volProfileRenderer.RenderBuySellProfile(profile, buyBrushDX, sellBrushDX);
                }
                else
                {
                    volProfileRenderer.RenderProfile(
                        profile,
                        volumeBrushDX,
                        hvnHighlightBrushDX,
                        lvnHighlightBrushDX,
                        new HashSet<double>(profile.HvnLevels),
                        new HashSet<double>(profile.LvnLevels)
                    );
                }

                if (ShowPoc)
                    volProfileRenderer.RenderPoc(profile, PocStroke.BrushDX, PocStroke.Width, PocStroke.StrokeStyle, false, pocHighlightBrushDX);

                if (ShowValueArea)
                    volProfileRenderer.RenderValueArea(profile, ValueAreaStroke.BrushDX, ValueAreaStroke.Width, ValueAreaStroke.StrokeStyle, DisplayTotal);

                if (ShowHvn && profile.HvnLevels.Count > 0)
                    volProfileRenderer.RenderLevels(profile, profile.HvnLevels, HvnStroke.BrushDX, HvnStroke.Width, HvnStroke.StrokeStyle);

                if (ShowLvn && profile.LvnLevels.Count > 0)
                    volProfileRenderer.RenderLevels(profile, profile.LvnLevels, LvnStroke.BrushDX, LvnStroke.Width, LvnStroke.StrokeStyle);

                if (DisplayMode == MofVolumeProfileMode.Delta)
                    volProfileRenderer.RenderDeltaProfile(profile, buyBrushDX, sellBrushDX);

                if (DisplayTotal)
                    volProfileRenderer.RenderTotalVolume(profile, totalTextBrushDX);

                if (ShowVolumeText && volumeTextBrushDX != null)
                    volProfileRenderer.RenderVolumeValues(profile, volumeTextBrushDX, VolumeTextSize);
            }

            // --- HVN Bands ONLY + boundaries ---
            if (IsInHitTest || Bars == null || Instrument == null || ChartPanel == null)
                return;

            double tickSize = Instrument.MasterInstrument.TickSize;
            if (tickSize <= 0 || BandTicks <= 0)
                return;

            float panelLeft = ChartPanel.X;
            float panelWidth = ChartPanel.W;
            if (panelWidth <= 0)
                return;

            double offset = tickSize * BandTicks;

            // aggregate visible HVN levels ONLY
            var hvnSet = new HashSet<double>();

            foreach (var profile in Profiles)
            {
                if (
                    profile.MaxVolume == 0 ||
                    (profile.StartBar < ChartBars.FromIndex && profile.EndBar < ChartBars.FromIndex) ||
                    (profile.StartBar > ChartBars.ToIndex && profile.EndBar > ChartBars.ToIndex)
                )
                    continue;

                if (ShowHvn && profile.HvnLevels != null)
                    foreach (var lvl in profile.HvnLevels)
                        hvnSet.Add(lvl);
            }

            // HVN band fill + boundary lines
            if (ShowHvn && hvnSet.Count > 0)
            {
                using (var hvnBandDx = CreateDxBandBrush(HvnBandBrush, HvnBandOpacity))
                {
                    if (hvnBandDx != null)
                    {
                        foreach (double level in hvnSet)
                        {
                            DrawBand(chartScale, level, offset, panelLeft, panelWidth, hvnBandDx);

                            if (ShowBandBoundaries && BandBoundaryStroke != null && BandBoundaryStroke.BrushDX != null)
                                DrawBandBoundaries(chartScale, level, offset, panelLeft, panelWidth, BandBoundaryStroke);
                        }
                    }
                }
            }

            // ZigZag segments crossing HVN bands (colored)
            if (ShowZigZagCrossSegments && zz != null && hvnSet.Count > 0)
                RenderZigZagCrossings(chartControl, chartScale, hvnSet, offset);
        }

        private void RenderZigZagCrossings(ChartControl chartControl, ChartScale chartScale, HashSet<double> hvnSet, double offset)
        {
            if (zz == null || zz.Value == null || ChartBars == null || Bars == null)
                return;

            // make sure ZZ is up-to-date
            zz.Update();

            int from = ChartBars.FromIndex;
            int to = ChartBars.ToIndex;

            int lastIdx = -1;
            double lastVal = 0;

            int scanFrom = Math.Max(0, from - 50);
            int scanTo = Math.Min(Bars.Count - 1 - (Calculate == Calculate.OnBarClose ? 1 : 0), to + 50);

            for (int idx = scanFrom; idx <= scanTo; idx++)
            {
                if (!zz.Value.IsValidDataPointAt(idx))
                    continue;

                double value = zz.Value.GetValueAt(idx);

                if (lastIdx >= 0)
                {
                    bool crossesAny = false;

                    foreach (double level in hvnSet)
                    {
                        double lower = level - offset;
                        double upper = level + offset;

                        if (SegmentIntersectsBand(lastVal, value, lower, upper))
                        {
                            crossesAny = true;
                            break;
                        }
                    }

                    if (crossesAny)
                    {
                        bool isUp = value > lastVal;
                        Stroke st = isUp ? ZigZagCrossUpStroke : ZigZagCrossDownStroke;

                        if (st != null && st.BrushDX != null)
                        {
                            float x0 = GetX(chartControl, lastIdx);
                            float y0 = chartScale.GetYByValue(lastVal);
                            float x1 = GetX(chartControl, idx);
                            float y1 = chartScale.GetYByValue(value);

                            RenderTarget.DrawLine(
                                new DX.Vector2(x0, y0),
                                new DX.Vector2(x1, y1),
                                st.BrushDX,
                                st.Width,
                                st.StrokeStyle
                            );
                        }
                    }
                }

                lastIdx = idx;
                lastVal = value;
            }
        }

        private bool SegmentIntersectsBand(double p0, double p1, double lower, double upper)
        {
            double segMin = Math.Min(p0, p1);
            double segMax = Math.Max(p0, p1);
            return segMin <= upper && segMax >= lower;
        }

        private float GetX(ChartControl chartControl, int barIdx)
        {
            if (chartControl.BarSpacingType == BarSpacingType.TimeBased ||
                (chartControl.BarSpacingType == BarSpacingType.EquidistantMulti && barIdx >= ChartBars.Count))
                return chartControl.GetXByTime(ChartBars.GetTimeByBarIdx(chartControl, barIdx));

            return chartControl.GetXByBarIndex(ChartBars, barIdx);
        }

        private void DrawBandBoundaries(ChartScale chartScale, double level, double offset,
                                        float panelLeft, float panelWidth,
                                        Stroke stroke)
        {
            if (stroke == null || stroke.BrushDX == null)
                return;

            double topPrice = level + offset;
            double bottomPrice = level - offset;

            float yTop = chartScale.GetYByValue(topPrice);
            float yBottom = chartScale.GetYByValue(bottomPrice);

            RenderTarget.DrawLine(
                new DX.Vector2(panelLeft, yTop),
                new DX.Vector2(panelLeft + panelWidth, yTop),
                stroke.BrushDX,
                stroke.Width,
                stroke.StrokeStyle
            );

            RenderTarget.DrawLine(
                new DX.Vector2(panelLeft, yBottom),
                new DX.Vector2(panelLeft + panelWidth, yBottom),
                stroke.BrushDX,
                stroke.Width,
                stroke.StrokeStyle
            );
        }

        public override void OnRenderTargetChanged()
        {
            if (volumeBrushDX != null) volumeBrushDX.Dispose();
            if (buyBrushDX != null) buyBrushDX.Dispose();
            if (sellBrushDX != null) sellBrushDX.Dispose();
            if (outlineBrushDX != null) outlineBrushDX.Dispose();
            if (backgroundBrushDX != null) backgroundBrushDX.Dispose();
            if (hvnHighlightBrushDX != null) hvnHighlightBrushDX.Dispose();
            if (lvnHighlightBrushDX != null) lvnHighlightBrushDX.Dispose();
            if (pocHighlightBrushDX != null) pocHighlightBrushDX.Dispose();
            if (volumeTextBrushDX != null) volumeTextBrushDX.Dispose();

            if (RenderTarget != null)
            {
                volumeBrushDX = VolumeBrush.ToDxBrush(RenderTarget);
                buyBrushDX = BuyBrush.ToDxBrush(RenderTarget);
                sellBrushDX = SellBrush.ToDxBrush(RenderTarget);
                outlineBrushDX = OutlineBrush.ToDxBrush(RenderTarget);
                backgroundBrushDX = ProfileBackgroundBrush != null
                    ? ProfileBackgroundBrush.ToDxBrush(RenderTarget)
                    : null;

                PocStroke.RenderTarget = RenderTarget;
                ValueAreaStroke.RenderTarget = RenderTarget;
                HvnStroke.RenderTarget = RenderTarget;
                LvnStroke.RenderTarget = RenderTarget;

                if (BandBoundaryStroke != null) BandBoundaryStroke.RenderTarget = RenderTarget;
                if (ZigZagCrossUpStroke != null) ZigZagCrossUpStroke.RenderTarget = RenderTarget;
                if (ZigZagCrossDownStroke != null) ZigZagCrossDownStroke.RenderTarget = RenderTarget;

                hvnHighlightBrushDX = HvnHighlightBrush.ToDxBrush(RenderTarget);
                lvnHighlightBrushDX = LvnHighlightBrush.ToDxBrush(RenderTarget);
                pocHighlightBrushDX = PocHighlightBrush.ToDxBrush(RenderTarget);

                volumeTextBrushDX = VolumeTextBrush != null
                    ? VolumeTextBrush.ToDxBrush(RenderTarget)
                    : null;
            }
        }
        #endregion

        #region Band helpers (SharpDX)
        private SharpDX.Direct2D1.Brush CreateDxBandBrush(Brush wpfBrush, int opacityPercent)
        {
            if (wpfBrush == null || RenderTarget == null)
                return null;

            var dxBrush = wpfBrush.ToDxBrush(RenderTarget);
            dxBrush.Opacity = (float)Math.Max(0.0, Math.Min(1.0, opacityPercent / 100.0));
            return dxBrush;
        }

        private void DrawBand(ChartScale chartScale, double level, double offset,
                              float panelLeft, float panelWidth,
                              SharpDX.Direct2D1.Brush dxBrush)
        {
            double topPrice = level + offset;
            double bottomPrice = level - offset;

            float yTop = chartScale.GetYByValue(topPrice);
            float yBottom = chartScale.GetYByValue(bottomPrice);

            float y = Math.Min(yTop, yBottom);
            float height = Math.Abs(yTop - yBottom);

            if (height <= 0.5f)
                return;

            var rect = new DX.RectangleF(panelLeft, y, panelWidth, height);
            RenderTarget.FillRectangle(rect, dxBrush);
        }
        #endregion

        #region Properties
        // Setup
        [Display(Name = "Display mode", Description = "Profile mode to render", Order = 1, GroupName = "Setup")]
        public MofVolumeProfileMode DisplayMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profile Period", Description = "Calculate profile from region", Order = 1, GroupName = "Setup")]
        public MofVolumeProfilePeriod Period { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Resolution Mode", Description = "Calculate profile from region", Order = 2, GroupName = "Setup")]
        public MofVolumeProfileResolution ResolutionMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Resolution", Description = "Calculate profile from region", Order = 3, GroupName = "Setup")]
        public int Resolution { get; set; }

        [Range(10, 90)]
        [Display(Name = "Value Area (%)", Description = "Value area percentage", Order = 7, GroupName = "Setup")]
        public float ValueArea { get; set; }

        [Display(Name = "Display Total Volume", Order = 8, GroupName = "Setup")]
        public bool DisplayTotal { get; set; }

        // Visual
        [Display(Name = "Profile width (%)", Description = "Width of bars relative to range", Order = 1, GroupName = "Visual")]
        public int Width { get; set; }

        [Display(Name = "Max profile width (px)", Description = "Maximum width in pixels for the profile", Order = 2, GroupName = "Visual")]
        public int MaxWidthPixels { get; set; }

        [Range(1, 100)]
        [Display(Name = "Profile opacity (%)", Description = "Opacity of bars out value area", Order = 3, GroupName = "Visual")]
        public int Opacity { get; set; }

        [Range(1, 100)]
        [Display(Name = "Value area opacity (%)", Description = "Opacity of bars in value area", Order = 4, GroupName = "Visual")]
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

        [Display(Name = "Show volume text", Order = 20, GroupName = "Visual")]
        public bool ShowVolumeText { get; set; }

        [Range(6, 48)]
        [Display(Name = "Volume text size", Order = 21, GroupName = "Visual")]
        public int VolumeTextSize { get; set; }

        [Range(0, 100)]
        [Display(Name = "Volume text opacity (%)", Order = 22, GroupName = "Visual")]
        public int VolumeTextOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "Volume text brush", Order = 23, GroupName = "Visual")]
        public Brush VolumeTextBrush { get; set; }

        [Browsable(false)]
        public string VolumeTextBrushSerialize
        {
            get { return Serialize.BrushToString(VolumeTextBrush); }
            set { VolumeTextBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Color for outline", Order = 13, GroupName = "Visual")]
        public Brush OutlineBrush { get; set; }

        [Browsable(false)]
        public string OutlineBrushSerialize
        {
            get { return Serialize.BrushToString(OutlineBrush); }
            set { OutlineBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Profile background", Order = 14, GroupName = "Visual")]
        public Brush ProfileBackgroundBrush { get; set; }

        [Browsable(false)]
        public string ProfileBackgroundBrushSerialize
        {
            get { return Serialize.BrushToString(ProfileBackgroundBrush); }
            set { ProfileBackgroundBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "HVN Highlight", Order = 15, GroupName = "Visual")]
        public Brush HvnHighlightBrush { get; set; }

        [Browsable(false)]
        public string HvnHighlightBrushSerialize
        {
            get { return Serialize.BrushToString(HvnHighlightBrush); }
            set { HvnHighlightBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "LVN Highlight", Order = 16, GroupName = "Visual")]
        public Brush LvnHighlightBrush { get; set; }

        [Browsable(false)]
        public string LvnHighlightBrushSerialize
        {
            get { return Serialize.BrushToString(LvnHighlightBrush); }
            set { LvnHighlightBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "POC Highlight", Order = 17, GroupName = "Visual")]
        public Brush PocHighlightBrush { get; set; }

        [Browsable(false)]
        public string PocHighlightBrushSerialize
        {
            get { return Serialize.BrushToString(PocHighlightBrush); }
            set { PocHighlightBrush = Serialize.StringToBrush(value); }
        }

        // Lines
        [Display(Name = "POC", Order = 8, GroupName = "Lines")]
        public Stroke PocStroke { get; set; }

        [Display(Name = "Value Area", Order = 9, GroupName = "Lines")]
        public Stroke ValueAreaStroke { get; set; }

        [Display(Name = "HVN", Order = 10, GroupName = "Lines")]
        public Stroke HvnStroke { get; set; }

        [Display(Name = "LVN", Order = 11, GroupName = "Lines")]
        public Stroke LvnStroke { get; set; }

        [Display(Name = "Show HVN", Order = 12, GroupName = "Lines")]
        public bool ShowHvn { get; set; }

        [Display(Name = "Show LVN", Order = 13, GroupName = "Lines")]
        public bool ShowLvn { get; set; }

        [Range(1, 20)]
        [Display(Name = "Smoothing Window", Order = 1, GroupName = "Levels")]
        public int SmoothingWindow { get; set; }

        [Range(1, 20)]
        [Display(Name = "Neighbor Bars", Order = 2, GroupName = "Levels")]
        public int NeighborBars { get; set; }

        [Range(0, 100)]
        [Display(Name = "Min Vol % of POC", Order = 3, GroupName = "Levels")]
        public int MinVolumePctOfPoc { get; set; }

        [Display(Name = "Min Distance (ticks)", Order = 5, GroupName = "Levels")]
        public int MinDistanceTicks { get; set; }

        [Display(Name = "Max Levels", Order = 6, GroupName = "Levels")]
        public int MaxLevels { get; set; }

        [Display(Name = "Use Global Levels", Order = 7, GroupName = "Levels")]
        public bool UseGlobalLevels { get; set; }

        // Bands (HVN)
        [Range(0, 1000)]
        [Display(Name = "Band width (ticks)", Description = "Half-width of band around HVN", Order = 1, GroupName = "Bands")]
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

        [Display(Name = "Show Band Boundaries", Order = 4, GroupName = "Bands")]
        public bool ShowBandBoundaries { get; set; }

        [Display(Name = "Band Boundary Stroke", Description = "Stroke for the upper/lower boundary lines of each HVN band", Order = 5, GroupName = "Bands")]
        public Stroke BandBoundaryStroke { get; set; }

        // ZigZag cross segments
        [Display(Name = "Show ZigZag Cross Segments", Order = 1, GroupName = "ZigZag")]
        public bool ShowZigZagCrossSegments { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ZigZag DeviationType", Order = 2, GroupName = "ZigZag")]
        public DeviationType ZigZagDeviationType { get; set; }

        [Range(0, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "ZigZag DeviationValue", Order = 3, GroupName = "ZigZag")]
        public double ZigZagDeviationValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ZigZag UseHighLow", Order = 4, GroupName = "ZigZag")]
        public bool ZigZagUseHighLow { get; set; }

        [Display(Name = "Cross Up Stroke (green)", Description = "Stroke for ZigZag segment crossing HVN band (upward)", Order = 5, GroupName = "ZigZag")]
        public Stroke ZigZagCrossUpStroke { get; set; }

        [Display(Name = "Cross Down Stroke (red)", Description = "Stroke for ZigZag segment crossing HVN band (downward)", Order = 6, GroupName = "ZigZag")]
        public Stroke ZigZagCrossDownStroke { get; set; }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MyOrderFlowCustom.MofVolumeProfile[] cacheMofVolumeProfile;
		public MyOrderFlowCustom.MofVolumeProfile MofVolumeProfile(MofVolumeProfilePeriod period, MofVolumeProfileResolution resolutionMode, int resolution)
		{
			return MofVolumeProfile(Input, period, resolutionMode, resolution);
		}

		public MyOrderFlowCustom.MofVolumeProfile MofVolumeProfile(ISeries<double> input, MofVolumeProfilePeriod period, MofVolumeProfileResolution resolutionMode, int resolution)
		{
			if (cacheMofVolumeProfile != null)
				for (int idx = 0; idx < cacheMofVolumeProfile.Length; idx++)
					if (cacheMofVolumeProfile[idx] != null && cacheMofVolumeProfile[idx].Period == period && cacheMofVolumeProfile[idx].ResolutionMode == resolutionMode && cacheMofVolumeProfile[idx].Resolution == resolution && cacheMofVolumeProfile[idx].EqualsInput(input))
						return cacheMofVolumeProfile[idx];
			return CacheIndicator<MyOrderFlowCustom.MofVolumeProfile>(new MyOrderFlowCustom.MofVolumeProfile(){ Period = period, ResolutionMode = resolutionMode, Resolution = resolution }, input, ref cacheMofVolumeProfile);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MyOrderFlowCustom.MofVolumeProfile MofVolumeProfile(MofVolumeProfilePeriod period, MofVolumeProfileResolution resolutionMode, int resolution)
		{
			return indicator.MofVolumeProfile(Input, period, resolutionMode, resolution);
		}

		public Indicators.MyOrderFlowCustom.MofVolumeProfile MofVolumeProfile(ISeries<double> input , MofVolumeProfilePeriod period, MofVolumeProfileResolution resolutionMode, int resolution)
		{
			return indicator.MofVolumeProfile(input, period, resolutionMode, resolution);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MyOrderFlowCustom.MofVolumeProfile MofVolumeProfile(MofVolumeProfilePeriod period, MofVolumeProfileResolution resolutionMode, int resolution)
		{
			return indicator.MofVolumeProfile(Input, period, resolutionMode, resolution);
		}

		public Indicators.MyOrderFlowCustom.MofVolumeProfile MofVolumeProfile(ISeries<double> input , MofVolumeProfilePeriod period, MofVolumeProfileResolution resolutionMode, int resolution)
		{
			return indicator.MofVolumeProfile(input, period, resolutionMode, resolution);
		}
	}
}

#endregion
```
