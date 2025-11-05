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

        // NEW: brush pour le texte des barres
        private SharpDX.Direct2D1.Brush barVolumeTextBrushDX;

        private static readonly Dictionary<string, List<double>> globalHvnLevels = new Dictionary<string, List<double>>();
        private static readonly Dictionary<string, List<double>> globalLvnLevels = new Dictionary<string, List<double>>();

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

                // HVN/LVN defaults
                SmoothingWindow = 2;
                NeighborBars = 2;
                MinVolumePctOfPoc = 10;
                MinDistanceTicks = 1;
                MaxLevels = 5;
                ShowHvn = true;
                ShowLvn = true;
                HvnStroke = new Stroke(Brushes.Yellow, DashStyleHelper.Dash, 1);
                LvnStroke = new Stroke(Brushes.LawnGreen, DashStyleHelper.Dash, 1);
                UseGlobalLevels = false;

                // NEW: paramètres d'affichage du texte sur les barres
                ShowBarVolumeText = false;
                BarVolumeTextSize = 10f;       // points
                BarVolumeTextOpacity = 80;     // %
                BarVolumeTextBrush = Brushes.Black;
            }
            else if (State == State.Configure)
            {
                Calculate = Calculate.OnEachTick;
                AddDataSeries((ResolutionMode == MofVolumeProfileResolution.Tick) ? BarsPeriodType.Tick : BarsPeriodType.Minute, Resolution);

                Profiles = new List<MofVolumeProfileData>()
                {
                    new MofVolumeProfileData() { StartBar = 0 }
                };
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

                if (CurrentBar == profile.EndBar) return;
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
            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;
            var volProfileRenderer = new MofVolumeProfileChartRenderer(ChartControl, chartScale, ChartBars, RenderTarget)
            {
                Opacity = Opacity / 100f,
                ValueAreaOpacity = ValueAreaOpacity / 100f,
                WidthPercent = Width / 100f,
                OutlineBrush = outlineBrushDX,
                MaxWidthPixels = Math.Max(0, MaxWidthPixels),
                BackgroundBrush = backgroundBrushDX,

                // NEW: options de texte sur barres
                ShowBarVolumeText = ShowBarVolumeText,
                BarVolumeTextBrush = barVolumeTextBrushDX,
                BarVolumeTextOpacity = BarVolumeTextOpacity / 100f,
                BarVolumeTextSize = BarVolumeTextSize
            };
            totalTextBrushDX = chartControl.Properties.ChartText.ToDxBrush(RenderTarget);
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

                // NEW: chiffres sur chaque barre (volume total par prix)
                if (ShowBarVolumeText)
                {
                    volProfileRenderer.RenderBarValues(profile);
                }

                if (ShowPoc) volProfileRenderer.RenderPoc(profile, PocStroke.BrushDX, PocStroke.Width, PocStroke.StrokeStyle, false, pocHighlightBrushDX);
                if (ShowValueArea) volProfileRenderer.RenderValueArea(profile, ValueAreaStroke.BrushDX, ValueAreaStroke.Width, ValueAreaStroke.StrokeStyle, DisplayTotal);
                if (ShowHvn && profile.HvnLevels.Count > 0)
                    volProfileRenderer.RenderLevels(profile, profile.HvnLevels, HvnStroke.BrushDX, HvnStroke.Width, HvnStroke.StrokeStyle);
                if (ShowLvn && profile.LvnLevels.Count > 0)
                    volProfileRenderer.RenderLevels(profile, profile.LvnLevels, LvnStroke.BrushDX, LvnStroke.Width, LvnStroke.StrokeStyle);
                if (DisplayMode == MofVolumeProfileMode.Delta)
                {
                    volProfileRenderer.RenderDeltaProfile(profile, buyBrushDX, sellBrushDX);
                }
                if (DisplayTotal)
                {
                    volProfileRenderer.RenderTotalVolume(profile, totalTextBrushDX);
                }
            }
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
            if (barVolumeTextBrushDX != null) barVolumeTextBrushDX.Dispose();

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
                hvnHighlightBrushDX = HvnHighlightBrush.ToDxBrush(RenderTarget);
                lvnHighlightBrushDX = LvnHighlightBrush.ToDxBrush(RenderTarget);
                pocHighlightBrushDX = PocHighlightBrush.ToDxBrush(RenderTarget);

                // NEW: DX brush pour le texte
                barVolumeTextBrushDX = BarVolumeTextBrush.ToDxBrush(RenderTarget);
            }
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

        // NEW: Texte sur barres
        [Display(Name = "Show Bar Volume Text", Order = 18, GroupName = "Visual")]
        public bool ShowBarVolumeText { get; set; }

        [Range(1, 100)]
        [Display(Name = "Bar Text Opacity (%)", Order = 19, GroupName = "Visual")]
        public int BarVolumeTextOpacity { get; set; }

        [Display(Name = "Bar Text Size (pt)", Order = 20, GroupName = "Visual")]
        public float BarVolumeTextSize { get; set; }

        [XmlIgnore]
        [Display(Name = "Bar Text Color", Order = 21, GroupName = "Visual")]
        public Brush BarVolumeTextBrush { get; set; }

        [Browsable(false)]
        public string BarVolumeTextBrushSerialize
        {
            get { return Serialize.BrushToString(BarVolumeTextBrush); }
            set { BarVolumeTextBrush = Serialize.StringToBrush(value); }
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

namespace InvestSoft.NinjaScript.VolumeProfile
{
    // *** IMPORTANT *** : les using ci-dessous sont désormais à L’INTÉRIEUR du namespace
    using NinjaTrader.Cbi;
    using NinjaTrader.Gui.Chart;
    using NinjaTrader.NinjaScript;
    using NinjaTrader.NinjaScript.MarketAnalyzerColumns;
    using SharpDX.Direct2D1;
    using SharpDX.DirectWrite;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;

    #region Data
    internal class MofVolumeProfileRow
    {
        public long buy = 0;
        public long sell = 0;
        public long other = 0;
        public long total { get { return buy + sell + other; } }

        public string toString()
        {
            return string.Format("<VolumeProfileRow buy={0} sell={1}>", buy, sell);
        }
    }

    internal class MofVolumeProfileData : ConcurrentDictionary<double, MofVolumeProfileRow>
    {
        public int StartBar { get; set; }
        public int EndBar { get; set; }
        public long MaxVolume { get; set; }
        public long TotalVolume { get; set; }
        public double VAH { get; set; }
        public double VAL { get; set; }
        public double POC { get; set; }

        public List<double> HvnLevels { get; set; } = new List<double>();
        public List<double> LvnLevels { get; set; } = new List<double>();
        public HashSet<double> HvnZones { get; set; } = new HashSet<double>();
        public HashSet<double> LvnZones { get; set; } = new HashSet<double>();

        public MofVolumeProfileRow UpdateRow(double price, long buyVolume, long sellVolume, long otherVolume)
        {
            var row = AddOrUpdate(
                price,
                (double key) => new MofVolumeProfileRow()
                {
                    buy = buyVolume,
                    sell = sellVolume,
                    other = otherVolume
                },
                (double key, MofVolumeProfileRow oldValue) => new MofVolumeProfileRow()
                {
                    buy = buyVolume + oldValue.buy,
                    sell = sellVolume + oldValue.sell,
                    other = otherVolume + oldValue.other
                }
            );
            if (row.total > MaxVolume)
            {
                MaxVolume = row.total;
                POC = price;
            }
            TotalVolume += (buyVolume + sellVolume + otherVolume);
            return row;
        }

        public void CalculateValueArea(float valueAreaPerc)
        {
            if (Count == 0 || POC == 0) return;

            List<double> priceList = Keys.OrderBy(p => p).ToList();
            int SmoothVA = 2;
            long upVol = 0;
            long downVol = 0;
            long valueVol = (long)(TotalVolume * valueAreaPerc);
            long areaVol = this[POC].total;
            int highIdx = priceList.IndexOf(POC);
            int lowIdx = highIdx;

            while (areaVol < valueVol)
            {
                if (upVol == 0)
                {
                    for (int n = 0; (n < SmoothVA && highIdx < priceList.Count - 1); n++)
                    {
                        highIdx++;
                        upVol += this[priceList[highIdx]].total;
                    }
                }

                if (downVol == 0)
                {
                    for (int n = 0; (n < SmoothVA && lowIdx > 0); n++)
                    {
                        lowIdx--;
                        downVol += this[priceList[lowIdx]].total;
                    }
                }

                if (upVol > downVol)
                {
                    areaVol += upVol;
                    upVol = 0;
                }
                else
                {
                    areaVol += downVol;
                    downVol = 0;
                }
            }
            VAH = priceList[highIdx];
            VAL = priceList[lowIdx];
        }

        public MofVolumeProfileRow GetValueOrDefault(double price)
        {
            MofVolumeProfileRow volume;
            if (!TryGetValue(price, out volume))
            {
                volume = new MofVolumeProfileRow();
            }
            return volume;
        }
    }
    #endregion

    #region ChartRenderer
    internal class MofVolumeProfileChartRenderer
    {
        private readonly ChartControl chartControl;
        private readonly ChartScale chartScale;
        private readonly ChartBars chartBars;
        private readonly RenderTarget renderTarget;

        public float Opacity { get; set; }
        public float ValueAreaOpacity { get; set; }
        public float WidthPercent;
        public Brush OutlineBrush { get; set; }
        public float MaxWidthPixels { get; set; }
        public Brush BackgroundBrush { get; set; }

        // NEW: options pour texte sur barres
        public bool ShowBarVolumeText { get; set; }
        public Brush BarVolumeTextBrush { get; set; }   // DX brush
        public float BarVolumeTextOpacity { get; set; } // 0..1
        public float BarVolumeTextSize { get; set; }    // points

        public MofVolumeProfileChartRenderer(
            ChartControl chartControl, ChartScale chartScale, ChartBars chartBars,
            RenderTarget renderTarget
        )
        {
            this.chartControl = chartControl;
            this.chartScale = chartScale;
            this.chartBars = chartBars;
            this.renderTarget = renderTarget;
            WidthPercent = 1;
            MaxWidthPixels = 0;
        }

        internal SharpDX.RectangleF GetBarRect(
            MofVolumeProfileData profile, double price, long volume,
            bool fullwidth = false, bool inWindow = true
        )
        {
            var tickSize = chartControl.Instrument.MasterInstrument.TickSize;
            float ypos = chartScale.GetYByValue(price + tickSize);
            float barHeight = chartScale.GetYByValue(price) - ypos;
            int halfBarDistance = (int)Math.Max(1, chartScale.GetPixelsForDistance(tickSize)) / 2;
            ypos += halfBarDistance;

            int chartBarWidth;
            int startX = (inWindow) ? (
                Math.Max(chartControl.GetXByBarIndex(chartBars, profile.StartBar), chartControl.CanvasLeft)
            ) : chartControl.GetXByBarIndex(chartBars, profile.StartBar);
            int endX = (inWindow) ? (
                Math.Min(chartControl.GetXByBarIndex(chartBars, profile.EndBar), chartControl.CanvasRight)
            ) : chartControl.GetXByBarIndex(chartBars, profile.EndBar);
            if (profile.StartBar > 0)
            {
                chartBarWidth = (
                    chartControl.GetXByBarIndex(chartBars, profile.StartBar) -
                    chartControl.GetXByBarIndex(chartBars, profile.StartBar - 1)
                ) / 2;
            }
            else
            {
                chartBarWidth = chartControl.GetBarPaintWidth(chartBars);
            }
            float xpos = startX;
            int maxWidth = Math.Max(endX - startX, chartBarWidth);
            if (MaxWidthPixels > 0)
                maxWidth = Math.Min(maxWidth, (int)MaxWidthPixels);

            float barWidth = (fullwidth) ? maxWidth : (
                maxWidth * (volume / (float)profile.MaxVolume) * WidthPercent
            );
            if (!fullwidth && MaxWidthPixels > 0)
                barWidth = Math.Min(barWidth, MaxWidthPixels);

            return new SharpDX.RectangleF(xpos, ypos, barWidth, barHeight);
        }

        internal void RenderBackground(MofVolumeProfileData profile)
        {
            if (BackgroundBrush == null || profile.Count == 0)
                return;

            bool hasRect = false;
            float top = 0f;
            float bottom = 0f;
            SharpDX.RectangleF baseRect = new SharpDX.RectangleF();

            foreach (KeyValuePair<double, MofVolumeProfileRow> row in profile)
            {
                var rect = GetBarRect(profile, row.Key, profile.MaxVolume, true);
                if (!hasRect)
                {
                    baseRect = rect;
                    top = rect.Top;
                    bottom = rect.Bottom;
                    hasRect = true;
                }
                else
                {
                    top = Math.Min(top, rect.Top);
                    bottom = Math.Max(bottom, rect.Bottom);
                }
            }

            if (!hasRect || baseRect.Width <= 0 || bottom <= top)
                return;

            var backgroundRect = new SharpDX.RectangleF(baseRect.Left, top, baseRect.Width, bottom - top);
            renderTarget.FillRectangle(backgroundRect, BackgroundBrush);
        }

        internal void RenderProfile(MofVolumeProfileData profile, Brush volumeBrush,
            Brush hvnBrush = null, Brush lvnBrush = null,
            ISet<double> hvnZones = null, ISet<double> lvnZones = null)
        {
            foreach (KeyValuePair<double, MofVolumeProfileRow> row in profile)
            {
                var rect = GetBarRect(profile, row.Key, row.Value.total);
                Brush brush = volumeBrush;
                if (hvnZones != null && hvnZones.Contains(row.Key))
                    brush = hvnBrush ?? volumeBrush;
                else if (lvnZones != null && lvnZones.Contains(row.Key))
                    brush = lvnBrush ?? volumeBrush;

                bool inVa = row.Key >= profile.VAL && row.Key <= profile.VAH;
                brush.Opacity = inVa ? ValueAreaOpacity : Opacity;
                renderTarget.FillRectangle(rect, brush);
                if (OutlineBrush != null)
                {
                    OutlineBrush.Opacity = brush.Opacity;
                    renderTarget.DrawRectangle(rect, OutlineBrush);
                }
            }
        }

        internal void RenderPoc(MofVolumeProfileData profile, Brush lineBrush, float width, StrokeStyle strokeStyle, bool drawText = false, Brush highlightBrush = null)
        {
            var pocRect = GetBarRect(profile, profile.POC, profile.MaxVolume);
            renderTarget.FillRectangle(pocRect, highlightBrush ?? lineBrush);

            pocRect = GetBarRect(profile, profile.POC, profile.MaxVolume, true);
            pocRect.Y += pocRect.Height / 2;
            renderTarget.DrawLine(
                pocRect.TopLeft, pocRect.TopRight,
                lineBrush, width, strokeStyle
            );
            if (drawText)
            {
                RnederText(
                    string.Format("{0}", profile.POC),
                    new SharpDX.Vector2(pocRect.Left, pocRect.Top),
                    lineBrush,
                    pocRect.Width,
                    TextAlignment.Trailing
                );
            }
        }

        internal void RenderValueArea(MofVolumeProfileData profile, Brush brush, float width, StrokeStyle strokeStyle, bool drawText = false)
        {
            if (profile.ContainsKey(profile.VAH))
            {
                var vahRect = GetBarRect(profile, profile.VAH, profile[profile.VAH].total, true);
                vahRect.Y += vahRect.Height / 2;
                renderTarget.DrawLine(vahRect.TopLeft, vahRect.TopRight, brush, width, strokeStyle);
                if (drawText)
                {
                    RnederText(
                        string.Format("{0}", profile.VAH),
                        new SharpDX.Vector2(vahRect.Left, vahRect.Top),
                        brush,
                        vahRect.Width,
                        TextAlignment.Trailing
                    );
                }
            }
            if (profile.ContainsKey(profile.VAL))
            {
                var valRect = GetBarRect(profile, profile.VAL, profile[profile.VAL].total, true);
                valRect.Y += valRect.Height / 2;
                renderTarget.DrawLine(valRect.TopLeft, valRect.TopRight, brush, width, strokeStyle);
                if (drawText)
                {
                    RnederText(
                        string.Format("{0}", profile.VAL),
                        new SharpDX.Vector2(valRect.Left, valRect.Top),
                        brush,
                        valRect.Width,
                        TextAlignment.Trailing
                    );
                }
            }
        }

        internal void RenderBuySellProfile(MofVolumeProfileData profile, Brush buyBrush, Brush sellBrush)
        {
            foreach (KeyValuePair<double, MofVolumeProfileRow> row in profile)
            {
                var buyRect = GetBarRect(profile, row.Key, row.Value.buy);
                var sellRect = GetBarRect(profile, row.Key, row.Value.sell);
                buyRect.X = sellRect.Right;
                if (row.Key >= profile.VAL && row.Key <= profile.VAH)
                {
                    buyBrush.Opacity = ValueAreaOpacity;
                    sellBrush.Opacity = ValueAreaOpacity;
                }
                else
                {
                    buyBrush.Opacity = Opacity;
                    sellBrush.Opacity = Opacity;
                }
                renderTarget.FillRectangle(buyRect, buyBrush);
                renderTarget.FillRectangle(sellRect, sellBrush);
                if (OutlineBrush != null)
                {
                    OutlineBrush.Opacity = buyBrush.Opacity;
                    renderTarget.DrawRectangle(buyRect, OutlineBrush);
                    renderTarget.DrawRectangle(sellRect, OutlineBrush);
                }
            }
        }

        internal void RenderDeltaProfile(MofVolumeProfileData profile, Brush buyBrush, Brush sellBrush)
        {
            foreach (KeyValuePair<double, MofVolumeProfileRow> row in profile)
            {
                var volumeDelta = Math.Abs(row.Value.buy - row.Value.sell);
                var rect = GetBarRect(profile, row.Key, volumeDelta);
                if (row.Key >= profile.VAL && row.Key <= profile.VAH)
                {
                    buyBrush.Opacity = ValueAreaOpacity;
                    sellBrush.Opacity = ValueAreaOpacity;
                }
                else
                {
                    buyBrush.Opacity = Opacity;
                    sellBrush.Opacity = Opacity;
                }
                renderTarget.FillRectangle(rect, (row.Value.buy > row.Value.sell) ? buyBrush : sellBrush);
                if (OutlineBrush != null)
                {
                    OutlineBrush.Opacity = buyBrush.Opacity;
                    renderTarget.DrawRectangle(rect, OutlineBrush);
                }
            }
        }

        internal void RenderLevels(
            MofVolumeProfileData profile,
            IEnumerable<double> levels,
            Brush brush,
            float width,
            StrokeStyle strokeStyle,
            bool drawText = false,
            bool extendRight = false)
        {
            foreach (double price in levels)
            {
                var rect = GetBarRect(profile, price,
                    profile.ContainsKey(price) ? profile[price].total : profile.MaxVolume, true);
                rect.Y += rect.Height / 2;
                var endPoint = extendRight
                    ? new SharpDX.Vector2(chartControl.CanvasRight, rect.Top)
                    : rect.TopRight;
                renderTarget.DrawLine(rect.TopLeft, endPoint, brush, width, strokeStyle);
                if (drawText)
                {
                    RnederText(
                        string.Format("{0}", price),
                        new SharpDX.Vector2(rect.Left, rect.Top),
                        brush,
                        rect.Width,
                        TextAlignment.Trailing
                    );
                }
            }
        }

        internal void RnederText(string text, SharpDX.Vector2 position, Brush brush, float maxWidth, TextAlignment align = TextAlignment.Leading)
        {
            var textLayout = new TextLayout(
                NinjaTrader.Core.Globals.DirectWriteFactory,
                text,
                chartControl.Properties.LabelFont.ToDirectWriteTextFormat(),
                maxWidth,
                30
            );
            textLayout.TextAlignment = align;
            textLayout.WordWrapping = WordWrapping.NoWrap;
            var textWidth = textLayout.Metrics.Width;
            if (textWidth > maxWidth) { textLayout.Dispose(); return; }
            renderTarget.DrawTextLayout(position, textLayout, brush);
            textLayout.Dispose();
        }

        internal void RenderBoldText(string text, SharpDX.Vector2 position, Brush brush, float maxWidth, TextAlignment align = TextAlignment.Leading)
        {
            var textLayout = new TextLayout(
                NinjaTrader.Core.Globals.DirectWriteFactory,
                text,
                chartControl.Properties.LabelFont.ToDirectWriteTextFormat(),
                maxWidth,
                30
            );
            textLayout.TextAlignment = align;
            textLayout.WordWrapping = WordWrapping.NoWrap;
            var textWidth = textLayout.Metrics.Width;
            if (textWidth > maxWidth) { textLayout.Dispose(); return; }
            var offset = new SharpDX.Vector2(position.X + 1, position.Y);
            renderTarget.DrawTextLayout(offset, textLayout, brush);
            renderTarget.DrawTextLayout(position, textLayout, brush);
            textLayout.Dispose();
        }

        internal void RenderTotalVolume(MofVolumeProfileData profile, Brush textBrush)
        {
            var maxPrice = profile.Keys.Max();
            var minPrice = profile.Keys.Min();
            var textFormat = chartControl.Properties.LabelFont.ToDirectWriteTextFormat();
            textFormat.WordWrapping = WordWrapping.NoWrap;
            var textLayout = new TextLayout(
                NinjaTrader.Core.Globals.DirectWriteFactory,
                string.Format("∑ {0} / {1}", profile.TotalVolume, maxPrice - minPrice),
                textFormat,
                300,
                textFormat.FontSize + 4
            );
            var barRect = GetBarRect(profile, minPrice, 0, false);
            RnederText(
                string.Format("∑ {0} / {1}", profile.TotalVolume, maxPrice - minPrice),
                new SharpDX.Vector2(barRect.Left, barRect.Top),
                textBrush,
                barRect.Width,
                TextAlignment.Leading
            );
            textLayout.Dispose();
        }

        // NEW: Rendu du texte de volume sur chaque barre (total par prix)
        internal void RenderBarValues(MofVolumeProfileData profile)
        {
            if (!ShowBarVolumeText || BarVolumeTextBrush == null || profile.Count == 0)
                return;

            foreach (KeyValuePair<double, MofVolumeProfileRow> row in profile)
            {
                var rect = GetBarRect(profile, row.Key, row.Value.total, false);

                if (rect.Width < 12f || rect.Height < 8f)
                    continue;

                string text = row.Value.total.ToString();

                var baseFormat = chartControl.Properties.LabelFont.ToDirectWriteTextFormat();
                var layout = new TextLayout(
                    NinjaTrader.Core.Globals.DirectWriteFactory,
                    text,
                    baseFormat,
                    rect.Width,
                    rect.Height
                );

                try { layout.SetFontSize(BarVolumeTextSize, new TextRange(0, text.Length)); } catch { }

                layout.TextAlignment = TextAlignment.Leading;          // aligné gauche
                layout.ParagraphAlignment = ParagraphAlignment.Center; // centré verticalement
                layout.WordWrapping = WordWrapping.NoWrap;

                if (layout.Metrics.Width > rect.Width - 2f)
                {
                    layout.Dispose();
                    continue;
                }

                BarVolumeTextBrush.Opacity = BarVolumeTextOpacity;
                var pos = new SharpDX.Vector2(rect.Left + 2f, rect.Top + (rect.Height - layout.Metrics.Height) / 2f);
                renderTarget.DrawTextLayout(pos, layout, BarVolumeTextBrush);
                layout.Dispose();
            }
        }
    }
    #endregion

    public enum MofVolumeProfileMode { Standard, BuySell, Delta };
    public enum MofVolumeProfilePeriod { Sessions, Bars };
    public enum MofVolumeProfileResolution { Tick, Minute };
    public enum PlateauSelectionMode { Lowest, Highest, Central };
}
