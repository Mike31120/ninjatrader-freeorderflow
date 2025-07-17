//
// Copyright (C) 2025, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Core;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class BigTradersProfile : Indicator
    {
        #region Properties
        internal class RejectInfoItem
        {
            public int buy;
            public int sell;
            public double buyVolume;
            public double sellVolume;
            public readonly List<double> buyDeltaVolumes = new();
            public readonly List<double> sellDeltaVolumes = new();

            public string BuyDeltaList => string.Join(" | ", buyDeltaVolumes.ConvertAll(d => d.ToString("0")));
            public string SellDeltaList => string.Join(" | ", sellDeltaVolumes.ConvertAll(d => d.ToString("0")));
            public double Delta => buyVolume - sellVolume;
        }

        private double alpha = 50;
        private int rejectionTicks = 2;
        private int rejectionResetTicks = 10;
        private readonly int barSpacing = 1;
		private int peakThreshold = 5;
        private DateTime cacheSessionEnd = Globals.MinDate;
        private DateTime currentDate = Globals.MinDate;
        private readonly List<int> newSessionBarIdx = new();
        private DateTime sessionDateTmp = Globals.MinDate;
        private SessionIterator sessionIterator;
        private int startIndexOf;
        private SessionIterator storedSession;
        private double lastPrice = double.NaN;
        private double extremePrice = double.NaN; // start price of current move
        private int currentTrend; // -1 down, 1 up
        private bool rejectionRecorded;
        private double moveBuyVolume;
        private double moveSellVolume;

        private readonly List<Dictionary<double, RejectInfoItem>> sortedDicList = new();
        private Dictionary<double, RejectInfoItem> cacheDictionary = new();
        private TimeSpan startTime = new TimeSpan(15, 30, 0);
        private bool showQuantities;
        private Brush highlightBuyBrush = Brushes.Gold;
        private Brush highlightSellBrush = Brushes.Gold;
        private double lastBuyRejectionPrice = double.NaN;
        private double lastSellRejectionPrice = double.NaN;
        private bool histogramOnTop;
        private Brush quantityTextBrush = Brushes.White;
        private bool quantityTextBold;
        private bool showDeltaVolumes;

        // --- volume delta background ---
        private double volumeDeltaMax = 100;
        private Brush positiveDeltaBrush = Brushes.Green;
        private Brush negativeDeltaBrush = Brushes.Red;
        private double volumeDeltaOpacity = 30;

        private double askPrice;
        private double bidPrice;

                private class RejectionDot
                {
                    public int BarIndex;
                    public double Price;
                    public Color Color;
                    public float Opacity;
                    public double Delta;   // <-- Ajouté ici
                }

                private class DottedLine
                {
                    public int StartBar;
                    public int LastBar;
                    public double Price;
                    public bool IsBuy;
                    public bool Active = true;
					public DateTime SessionDate; // <-- AJOUT
                }


        private readonly List<RejectionDot> rejectionDots = new();
        private readonly List<DottedLine> dottedLines = new();
		
		private int consecutiveRejectionCount = 2; // N
		private Brush highlightBoxBrush = Brushes.Yellow; // Couleur entourage
				
		private Brush consecutiveRejectionBrush = Brushes.Gold;
		private Brush buyConsecutiveLineBrush = Brushes.Gold;
		private Brush sellConsecutiveLineBrush = Brushes.Gold;
		private DashStyleHelper buyConsecutiveLineDashStyle = DashStyleHelper.Dash;
		private DashStyleHelper sellConsecutiveLineDashStyle = DashStyleHelper.Dash;
		private double buyConsecutiveLineThickness = 2;
		private double sellConsecutiveLineThickness = 2;

        // Track last consecutive rejection information
        private double buyConsecutivePrice = double.NaN;
        private double sellConsecutivePrice = double.NaN;
        private int buyConsecutiveStreak;
        private int sellConsecutiveStreak;

        // --- colored rejection history ---
        private int coloredRejectionCount = 0;
        private Brush coloredBuyBrush = Brushes.DodgerBlue;
        private Brush coloredSellBrush = Brushes.Crimson;
        private readonly List<double> buyRejectionHistory = new();
        private readonly List<double> sellRejectionHistory = new();
		
		private bool usePeakColors = true;
		
		private bool showMaxBuyRejectionLine = true;
		private bool showMaxSellRejectionLine = true;
		
		private Brush maxBuyRejectionLineBrush = Brushes.Lime;
		private Brush maxSellRejectionLineBrush = Brushes.Red;
		
		private double maxBuyRejectionLineThickness = 2;
		private double maxSellRejectionLineThickness = 2;
		
		private DashStyleHelper maxBuyRejectionLineDashStyle = DashStyleHelper.Solid;
		private DashStyleHelper maxSellRejectionLineDashStyle = DashStyleHelper.Solid;
				
		private bool showArrowSignals = true;
		
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Big traders rejection profile indicator";
                Name = "Big Traders Profile";
                Calculate = Calculate.OnEachTick;
                IsChartOnly = true;
                IsOverlay = true;
                DrawOnPricePanel = false;
                BuyBrush = Brushes.DodgerBlue;
                SellBrush = Brushes.Crimson;
                BuyPeakBrush = Brushes.LimeGreen;
                SellPeakBrush = Brushes.Orange;
                HistogramWidth = 100;
                StartTime = new TimeSpan(15,30,0);
                ShowQuantities = false;
                ShowDeltaVolumes = false;
                HighlightBuyBrush = Brushes.Gold;
                HighlightSellBrush = Brushes.Gold;
                QuantityTextBrush = Brushes.White;
                QuantityTextBold = false;
                HistogramOnTop = true;
                VolumeDeltaMax = 100;
                PositiveDeltaBrush = Brushes.Green;
                NegativeDeltaBrush = Brushes.Red;
                VolumeDeltaOpacity = 30;
                ColoredRejectionCount = 5;
                ColoredBuyBrush = Brushes.DodgerBlue;
                ColoredSellBrush = Brushes.Crimson;
				UsePeakColors = true;

            }
            else if (State == State.Configure)
            {
                ZOrder = 1000000;
            }
            else if (State == State.DataLoaded)
            {
                storedSession = new SessionIterator(Bars);
            }
            else if (State == State.Historical && Calculate != Calculate.OnEachTick)
            {
                Draw.TextFixed(this, "NinjaScriptInfo", string.Format(Custom.Resource.NinjaScriptOnBarCloseError, Name), TextPosition.BottomRight);
            }
        }

        protected override void OnBarUpdate()
        {
            if (!IsFirstTickOfBar || CurrentBar < 1)
                return;

            int closedBar = CurrentBar - 1;
            double closeValue = Close[1];

			foreach (var line in dottedLines)
			{
			    if (!line.Active)
			        continue;
			
			    if (closedBar <= line.StartBar)
			        continue;
			
			    // *** AJOUT ICI : couper la ligne si on change de session ***
			    DateTime lineSession = line.SessionDate;
			    DateTime barSession = GetSessionDateForBar(closedBar);
			
			    if (lineSession != barSession)
			    {
			        line.Active = false;
			        continue;
			    }
			
			    // ... le reste du code ...
			    if (line.IsBuy)
			    {
			        if (closeValue < line.Price)
			        {
			            line.Active = false;
			            continue;
			        }
			    }
			    else
			    {
			        if (closeValue > line.Price)
			        {
			            line.Active = false;
			            continue;
			        }
			    }
			
			    if (closedBar > line.LastBar)
			    {
			        int barsAgo = CurrentBar - closedBar;
			        Draw.Dot(
			            this,
			            $"DL{line.StartBar}_{closedBar}",
			            false,
			            barsAgo,
			            line.Price,
			            line.IsBuy ? Brushes.LimeGreen : Brushes.Crimson
			        );
			        line.LastBar = closedBar;
			    }
			}
			
			dottedLines.RemoveAll(l => !l.Active);


        }	

        private DateTime GetLastBarSessionDate(DateTime time)
        {
            if (time <= cacheSessionEnd)
                return sessionDateTmp;

            if (!Bars.BarsType.IsIntraday)
                return sessionDateTmp;

            storedSession.GetNextSession(time, true);

            cacheSessionEnd = storedSession.ActualSessionEnd;
            sessionDateTmp = TimeZoneInfo.ConvertTime(cacheSessionEnd.AddSeconds(-1), Globals.GeneralOptions.TimeZoneInfo, Bars.TradingHours.TimeZoneInfo);

            if (newSessionBarIdx.Count == 0 || (newSessionBarIdx.Count > 0 && CurrentBar > newSessionBarIdx[newSessionBarIdx.Count - 1]))
                newSessionBarIdx.Add(CurrentBar);

            return sessionDateTmp;
        }

protected override void OnMarketData(MarketDataEventArgs e)
{
    if (Bars.Count <= 0)
        return;


    if (!Bars.IsTickReplay)
    {
        if (e.MarketDataType == MarketDataType.Ask)
        {
            askPrice = e.Price;
            return;
        }
        if (e.MarketDataType == MarketDataType.Bid)
        {
            bidPrice = e.Price;
            return;
        }

        if (e.MarketDataType != MarketDataType.Last || ChartControl == null || askPrice == 0 || bidPrice == 0)
            return;
    }
    else if (e.MarketDataType != MarketDataType.Last)
        return;

    DateTime tickTime = e.Time;
    DateTime sessionDate = tickTime.Date;
    if (tickTime.TimeOfDay < startTime)
        sessionDate = sessionDate.AddDays(-1);

        if (sessionDate != currentDate && tickTime.TimeOfDay >= startTime)
        {
            cacheDictionary = new Dictionary<double, RejectInfoItem>();
            sortedDicList.Add(cacheDictionary);
            lastPrice = double.NaN;
            extremePrice = double.NaN;
            currentTrend = 0;
            rejectionRecorded = false;
            lastBuyRejectionPrice = double.NaN;
            lastSellRejectionPrice = double.NaN;
            buyConsecutivePrice = double.NaN;
            sellConsecutivePrice = double.NaN;
            buyConsecutiveStreak = 0;
            sellConsecutiveStreak = 0;
            rejectionDots.Clear();
            buyRejectionHistory.Clear();
            sellRejectionHistory.Clear();
            if (newSessionBarIdx.Count == 0 || CurrentBar > newSessionBarIdx[newSessionBarIdx.Count - 1])
                newSessionBarIdx.Add(CurrentBar);
        }

    currentDate = sessionDate;

    if (tickTime.TimeOfDay < startTime)
        return;

    double price = e.Price;
    double tickSize = Bars.Instrument.MasterInstrument.TickSize;
    double volume = e.Volume;

    bool isBuyMarket = false;
    bool isSellMarket = false;
    if (Bars.IsTickReplay)
    {
        isBuyMarket = price >= e.Ask;
        isSellMarket = price <= e.Bid;
    }
    else
    {
        isBuyMarket = price >= askPrice;
        isSellMarket = price <= bidPrice;
    }

    if (!cacheDictionary.ContainsKey(price))
        cacheDictionary.Add(price, new RejectInfoItem());

    RejectInfoItem volumeItem = cacheDictionary[price];
    if (isBuyMarket)
    {
        volumeItem.buyVolume += volume;
        moveBuyVolume += volume;
    }
    else if (isSellMarket)
    {
        volumeItem.sellVolume += volume;
        moveSellVolume += volume;
    }

    double threshold = rejectionResetTicks * tickSize;

    // ---------------------------------------
    // Correction : reset au niveau de chaque rejet individuel
	// ---------------------------------------
	// Nouvelle logique : reset dans les deux sens
	foreach (var kv in cacheDictionary)
	{
	    double priceLevel = kv.Key;
	
	    // Reset BUY si trop loin du niveau de rejet
	    if (kv.Value.buy > 0 && Math.Abs(price - priceLevel) >= threshold)
	    {
	        kv.Value.buy = 0;
	        kv.Value.buyDeltaVolumes.Clear();
	    }
	
	    // Reset SELL si trop loin du niveau de rejet
	    if (kv.Value.sell > 0 && Math.Abs(price - priceLevel) >= threshold)
	    {
	        kv.Value.sell = 0;
	        kv.Value.sellDeltaVolumes.Clear();
	    }
	}
	// ---------------------------------------

    // ---------------------------------------

    if (double.IsNaN(lastPrice))
    {
        lastPrice = price;
        extremePrice = price;
        currentTrend = 0;
        rejectionRecorded = false;
        moveBuyVolume = 0;
        moveSellVolume = 0;
        return;
    }

    int direction = price > lastPrice ? 1 : (price < lastPrice ? -1 : 0);

    if (direction == 1)
    {
        if (currentTrend != 1)
        {
            currentTrend = 1;
            extremePrice = lastPrice;
            rejectionRecorded = false;
            moveBuyVolume = 0;
            moveSellVolume = 0;
        }

        if (!rejectionRecorded && (price - extremePrice) / tickSize >= rejectionTicks)
        {
            double rejectionPrice = extremePrice;
            if (!cacheDictionary.ContainsKey(rejectionPrice))
                cacheDictionary.Add(rejectionPrice, new RejectInfoItem());

            RejectInfoItem item = cacheDictionary[rejectionPrice];
            item.buy++;
            double delta = moveBuyVolume - moveSellVolume;
            item.buyDeltaVolumes.Add(delta);
            ApplyDeltaDot(CurrentBar, rejectionPrice, delta, true);
            moveBuyVolume = 0;
            moveSellVolume = 0;

            double prevPrice = lastBuyRejectionPrice;
            lastBuyRejectionPrice = rejectionPrice;
            if (!double.IsNaN(prevPrice) && Math.Abs(rejectionPrice - prevPrice) <= tickSize / 2.0)
                buyConsecutiveStreak++;
            else
                buyConsecutiveStreak = 1;
            if (buyConsecutiveStreak >= ConsecutiveRejectionCount)
                buyConsecutivePrice = rejectionPrice;
            else
                buyConsecutivePrice = double.NaN;

            rejectionRecorded = true;

            if (coloredRejectionCount > 0)
            {
                buyRejectionHistory.Insert(0, rejectionPrice);
                if (buyRejectionHistory.Count > coloredRejectionCount)
                    buyRejectionHistory.RemoveAt(buyRejectionHistory.Count - 1);
            }

			if (ShowArrowSignals && buyRejectionHistory.Count >= 2 && sellRejectionHistory.Count >= 2)
			{
			    double maxSell = Math.Max(sellRejectionHistory[0], sellRejectionHistory[1]);
			    if (buyRejectionHistory[0] >= maxSell && buyRejectionHistory[1] >= maxSell)
			    {
			        Draw.ArrowUp(this,
			            $"BTPBuySignal{CurrentBar}",
			            false,
			            0,
			            rejectionPrice - tickSize,
			            Brushes.LimeGreen);
			    }
			}

        }
    }
    else if (direction == -1)
    {
        if (currentTrend != -1)
        {
            currentTrend = -1;
            extremePrice = lastPrice;
            rejectionRecorded = false;
            moveBuyVolume = 0;
            moveSellVolume = 0;
        }

        if (!rejectionRecorded && (extremePrice - price) / tickSize >= rejectionTicks)
        {
            double rejectionPrice = extremePrice;
            if (!cacheDictionary.ContainsKey(rejectionPrice))
                cacheDictionary.Add(rejectionPrice, new RejectInfoItem());

            RejectInfoItem item = cacheDictionary[rejectionPrice];
            item.sell++;
            double delta = moveBuyVolume - moveSellVolume;
            item.sellDeltaVolumes.Add(delta);
            ApplyDeltaDot(CurrentBar, rejectionPrice, delta, false);
            moveBuyVolume = 0;
            moveSellVolume = 0;

            double prevPrice = lastSellRejectionPrice;
            lastSellRejectionPrice = rejectionPrice;
            if (!double.IsNaN(prevPrice) && Math.Abs(rejectionPrice - prevPrice) <= tickSize / 2.0)
                sellConsecutiveStreak++;
            else
                sellConsecutiveStreak = 1;
            if (sellConsecutiveStreak >= ConsecutiveRejectionCount)
                sellConsecutivePrice = rejectionPrice;
            else
                sellConsecutivePrice = double.NaN;

            rejectionRecorded = true;

            if (coloredRejectionCount > 0)
            {
                sellRejectionHistory.Insert(0, rejectionPrice);
                if (sellRejectionHistory.Count > coloredRejectionCount)
                    sellRejectionHistory.RemoveAt(sellRejectionHistory.Count - 1);
            }

			if (ShowArrowSignals && sellRejectionHistory.Count >= 2 && buyRejectionHistory.Count >= 2)
			{
			    double minBuy = Math.Min(buyRejectionHistory[0], buyRejectionHistory[1]);
			    if (sellRejectionHistory[0] <= minBuy && sellRejectionHistory[1] <= minBuy)
			    {
			        Draw.ArrowDown(this,
			            $"BTPSellSignal{CurrentBar}",
			            false,
			            0,
			            rejectionPrice + tickSize,
			            Brushes.Red);
			    }
			}

        }
    }

    lastPrice = price;
}

protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
{
    if (Bars == null || Bars.Instrument == null || IsInHitTest)
        return;

    int firstBarIdxToPaint = -1;
    double tickSize = Bars.Instrument.MasterInstrument.TickSize;
    double rejectMax = 0;

    float padding = 10f;
    float panelLeft = ChartPanel.X;
    float panelRight = ChartPanel.X + ChartPanel.W;

    // ----------- Détection des index de début de session
    for (int i = newSessionBarIdx.Count - 1; i > 0; i--)
    {
        if (newSessionBarIdx[i] <= ChartBars.ToIndex)
        {
            startIndexOf = i;
            firstBarIdxToPaint = newSessionBarIdx[i];
            break;
        }
    }
    if (sortedDicList.Count < 1 && cacheDictionary.Keys.Count > 0)
        sortedDicList.Add(cacheDictionary);

    // ----------- Préparation profileList
    var profileList = new List<(double price, int buy, int sell, string buyDeltaList, string sellDeltaList)>();
    foreach (var kv in sortedDicList[startIndexOf])
        profileList.Add((kv.Key, kv.Value.buy, kv.Value.sell, kv.Value.BuyDeltaList, kv.Value.SellDeltaList));

    double boxedBuyPrice = buyConsecutivePrice;
    double boxedSellPrice = sellConsecutivePrice;

    // ----------- Recherche du prix max rejet BUY/SELL
    int maxBuyCount = 0, maxSellCount = 0;
    List<double> maxBuyPrices = new();
    List<double> maxSellPrices = new();

    foreach (var p in profileList)
    {
        if (p.buy > maxBuyCount)
        {
            maxBuyCount = p.buy;
            maxBuyPrices.Clear();
            maxBuyPrices.Add(p.price);
        }
        else if (p.buy == maxBuyCount && p.buy > 0)
        {
            maxBuyPrices.Add(p.price);
        }
        if (p.sell > maxSellCount)
        {
            maxSellCount = p.sell;
            maxSellPrices.Clear();
            maxSellPrices.Add(p.price);
        }
        else if (p.sell == maxSellCount && p.sell > 0)
        {
            maxSellPrices.Add(p.price);
        }
    }

    double? maxBuyRejectionPrice = (maxBuyPrices.Count == 1) ? maxBuyPrices[0] : (double?)null;
    double? maxSellRejectionPrice = (maxSellPrices.Count == 1) ? maxSellPrices[0] : (double?)null;

    // ----------- Tracer les lignes MAX le plus tôt possible (background !)
    if (ShowMaxBuyRejectionLine && maxBuyRejectionPrice.HasValue)
    {
        float y = chartScale.GetYByValue(maxBuyRejectionPrice.Value);
        using (var lineBrush = MaxBuyRejectionLineBrush.ToDxBrush(RenderTarget))
        using (var strokeStyle = new SharpDX.Direct2D1.StrokeStyle(
            RenderTarget.Factory,
            new SharpDX.Direct2D1.StrokeStyleProperties()
            {
                DashStyle = (SharpDX.Direct2D1.DashStyle)MaxBuyRejectionLineDashStyle
            }))
        {
            RenderTarget.DrawLine(
                new SharpDX.Vector2(panelLeft, y),
                new SharpDX.Vector2(panelRight, y),
                lineBrush,
                (float)MaxBuyRejectionLineThickness,
                strokeStyle
            );
        }
    }
    if (ShowMaxSellRejectionLine && maxSellRejectionPrice.HasValue)
    {
        float y = chartScale.GetYByValue(maxSellRejectionPrice.Value);
        using (var lineBrush = MaxSellRejectionLineBrush.ToDxBrush(RenderTarget))
        using (var strokeStyle = new SharpDX.Direct2D1.StrokeStyle(
            RenderTarget.Factory,
            new SharpDX.Direct2D1.StrokeStyleProperties()
            {
                DashStyle = (SharpDX.Direct2D1.DashStyle)MaxSellRejectionLineDashStyle
            }))
        {
            RenderTarget.DrawLine(
                new SharpDX.Vector2(panelLeft, y),
                new SharpDX.Vector2(panelRight, y),
                lineBrush,
                (float)MaxSellRejectionLineThickness,
                strokeStyle
            );
        }
    }

    // ----------- Calcul du maximum pour normaliser l'histogramme
    foreach (Dictionary<double, RejectInfoItem> tmpDict in sortedDicList)
    {
        foreach (KeyValuePair<double, RejectInfoItem> keyValue in tmpDict)
        {
            double price = keyValue.Key;
            if (Bars.BarsType.IsIntraday && (price > chartScale.MaxValue || price < chartScale.MinValue))
                continue;
            RejectInfoItem rii = keyValue.Value;
            rejectMax = Math.Max(rejectMax, Math.Max(rii.buy, rii.sell));
        }
    }
    if (rejectMax.ApproxCompare(0) == 0)
        return;

    // ----------- Préparation des brushes
    var buyBrushDx = BuyBrush.ToDxBrush(RenderTarget);      buyBrushDx.Opacity = (float)(alpha / 100.0);
    var sellBrushDx = SellBrush.ToDxBrush(RenderTarget);    sellBrushDx.Opacity = (float)(alpha / 100.0);
    var buyPeakBrushDx = BuyPeakBrush.ToDxBrush(RenderTarget);  buyPeakBrushDx.Opacity = (float)(alpha / 100.0);
    var sellPeakBrushDx = SellPeakBrush.ToDxBrush(RenderTarget);    sellPeakBrushDx.Opacity = (float)(alpha / 100.0);
    var highlightBuyBrushDx = HighlightBuyBrush.ToDxBrush(RenderTarget);  highlightBuyBrushDx.Opacity = (float)(alpha / 100.0);
    var highlightSellBrushDx = HighlightSellBrush.ToDxBrush(RenderTarget);    highlightSellBrushDx.Opacity = (float)(alpha / 100.0);
    SharpDX.Direct2D1.Brush buyLineBrushDx = BuyConsecutiveLineBrush.ToDxBrush(RenderTarget);
    SharpDX.Direct2D1.Brush sellLineBrushDx = SellConsecutiveLineBrush.ToDxBrush(RenderTarget);

    SharpDX.DirectWrite.TextFormat textFormatLeft = null, textFormatRight = null;
    SharpDX.Direct2D1.Brush textBrushDx = null;
    if (ShowQuantities)
    {
        var weight = quantityTextBold ? SharpDX.DirectWrite.FontWeight.Bold : SharpDX.DirectWrite.FontWeight.Normal;
        textFormatLeft = new SharpDX.DirectWrite.TextFormat(Globals.DirectWriteFactory, "Arial", weight, SharpDX.DirectWrite.FontStyle.Normal, SharpDX.DirectWrite.FontStretch.Normal, 12f)
        {
            TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading,
            ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center
        };
        textFormatRight = new SharpDX.DirectWrite.TextFormat(Globals.DirectWriteFactory, "Arial", weight, SharpDX.DirectWrite.FontStyle.Normal, SharpDX.DirectWrite.FontStretch.Normal, 12f)
        {
            TextAlignment = SharpDX.DirectWrite.TextAlignment.Trailing,
            ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center
        };
        textBrushDx = QuantityTextBrush.ToDxBrush(RenderTarget);
        textBrushDx.Opacity = (float)(alpha / 100.0);
    }

    // ----------- Boucle de dessin des histogrammes/barres
    for (int i = 0; i < profileList.Count; i++)
    {
        double price = profileList[i].price;
        int buyVal = profileList[i].buy;
        int sellVal = profileList[i].sell;
        string buyDeltaList = profileList[i].buyDeltaList;
        string sellDeltaList = profileList[i].sellDeltaList;

        bool buyPeak = buyVal >= PeakThreshold;
        bool sellPeak = sellVal >= PeakThreshold;

        double priceLower = price - tickSize / 2;
        float yLower = chartScale.GetYByValue(priceLower);
        float yUpper = chartScale.GetYByValue(priceLower + tickSize);
        float height = Math.Max(1, Math.Abs(yUpper - yLower) - barSpacing);
        int barWidthBuy = (int)(HistogramWidth * (buyVal / rejectMax));
        int barWidthSell = (int)(HistogramWidth * (sellVal / rejectMax));

        var buyBrushTmp = (buyPeak && UsePeakColors) ? buyPeakBrushDx : buyBrushDx;
        var sellBrushTmp = (sellPeak && UsePeakColors) ? sellPeakBrushDx : sellBrushDx;

        float buyX = ChartPanel.X;
        float sellX = ChartPanel.X + ChartPanel.W - barWidthSell;

        bool isLastBuy = Math.Abs(price - lastBuyRejectionPrice) <= tickSize / 2.0;
        bool isLastSell = Math.Abs(price - lastSellRejectionPrice) <= tickSize / 2.0;
        if (isLastBuy) buyBrushTmp = highlightBuyBrushDx;
        if (isLastSell) sellBrushTmp = highlightSellBrushDx;

        // Couleur spéciale si N consécutifs
        bool isConsecutiveBuy = (Math.Abs(price - boxedBuyPrice) < tickSize / 2.0 && buyVal >= ConsecutiveRejectionCount);
        bool isConsecutiveSell = (Math.Abs(price - boxedSellPrice) < tickSize / 2.0 && sellVal >= ConsecutiveRejectionCount);
        SharpDX.Direct2D1.Brush buyConsecutiveBrushDx = null;
        SharpDX.Direct2D1.Brush sellConsecutiveBrushDx = null;
        if (isConsecutiveBuy)
        {
            buyConsecutiveBrushDx = ConsecutiveRejectionBrush.ToDxBrush(RenderTarget);
            buyConsecutiveBrushDx.Opacity = 1.0f;
            buyBrushTmp = buyConsecutiveBrushDx;
        }
        if (isConsecutiveSell)
        {
            sellConsecutiveBrushDx = ConsecutiveRejectionBrush.ToDxBrush(RenderTarget);
            sellConsecutiveBrushDx.Opacity = 1.0f;
            sellBrushTmp = sellConsecutiveBrushDx;
        }
        // Coloration des derniers rejets
        SharpDX.Direct2D1.Brush buyHistoryBrushDx = null;
        SharpDX.Direct2D1.Brush sellHistoryBrushDx = null;
        if (coloredRejectionCount > 0)
        {
            int idx = -1;
            for (int j = 0; j < buyRejectionHistory.Count; j++)
                if (Math.Abs(buyRejectionHistory[j] - price) <= tickSize / 2.0)
                { idx = j; break; }
            if (idx >= 0 && idx < coloredRejectionCount)
            {
                buyHistoryBrushDx = ColoredBuyBrush.ToDxBrush(RenderTarget);
                buyHistoryBrushDx.Opacity = (float)((coloredRejectionCount - idx) / (double)coloredRejectionCount * alpha / 100.0);
                buyBrushTmp = buyHistoryBrushDx;
            }
            idx = -1;
            for (int j = 0; j < sellRejectionHistory.Count; j++)
                if (Math.Abs(sellRejectionHistory[j] - price) <= tickSize / 2.0)
                { idx = j; break; }
            if (idx >= 0 && idx < coloredRejectionCount)
            {
                sellHistoryBrushDx = ColoredSellBrush.ToDxBrush(RenderTarget);
                sellHistoryBrushDx.Opacity = (float)((coloredRejectionCount - idx) / (double)coloredRejectionCount * alpha / 100.0);
                sellBrushTmp = sellHistoryBrushDx;
            }
        }
        // Ligne horizontale à chaque prix détecté (consecutive)
        if (isConsecutiveBuy)
        {
            using (var strokeStyle = new SharpDX.Direct2D1.StrokeStyle(
                RenderTarget.Factory,
                new SharpDX.Direct2D1.StrokeStyleProperties()
                {
                    DashStyle = (SharpDX.Direct2D1.DashStyle)BuyConsecutiveLineDashStyle
                }))
            {
                RenderTarget.DrawLine(
                    new SharpDX.Vector2(panelLeft, (yLower + yUpper) / 2f),
                    new SharpDX.Vector2(panelRight, (yLower + yUpper) / 2f),
                    buyLineBrushDx,
                    (float)BuyConsecutiveLineThickness,
                    strokeStyle
                );
            }
        }
        if (isConsecutiveSell)
        {
            using (var strokeStyle = new SharpDX.Direct2D1.StrokeStyle(
                RenderTarget.Factory,
                new SharpDX.Direct2D1.StrokeStyleProperties()
                {
                    DashStyle = (SharpDX.Direct2D1.DashStyle)SellConsecutiveLineDashStyle
                }))
            {
                RenderTarget.DrawLine(
                    new SharpDX.Vector2(panelLeft, (yLower + yUpper) / 2f),
                    new SharpDX.Vector2(panelRight, (yLower + yUpper) / 2f),
                    sellLineBrushDx,
                    (float)SellConsecutiveLineThickness,
                    strokeStyle
                );
            }
        }
        // Dessin des barres/histogrammes (recouvrent les lignes max)
        RenderTarget.FillRectangle(new SharpDX.RectangleF(buyX, yUpper, barWidthBuy, height), buyBrushTmp);
        RenderTarget.FillRectangle(new SharpDX.RectangleF(sellX, yUpper, barWidthSell, height), sellBrushTmp);

        // Affichage des quantités
        if (ShowQuantities && textFormatLeft != null && textFormatRight != null)
        {
            if (buyVal > 0)
            {
                string txtBuy = ShowDeltaVolumes ? string.Format("{0} : {1}", buyVal, buyDeltaList) : buyVal.ToString();
                RenderTarget.DrawText(
                    txtBuy,
                    textFormatLeft,
                    new SharpDX.RectangleF(ChartPanel.X + padding, yUpper, HistogramWidth - padding, height),
                    textBrushDx
                );
            }
            if (sellVal > 0)
            {
                string txtSell = ShowDeltaVolumes ? string.Format("{0} : {1}", sellVal, sellDeltaList) : sellVal.ToString();
                RenderTarget.DrawText(
                    txtSell,
                    textFormatRight,
                    new SharpDX.RectangleF(ChartPanel.X + ChartPanel.W - HistogramWidth, yUpper, HistogramWidth - padding, height),
                    textBrushDx
                );
            }
        }
        // Libération
        buyConsecutiveBrushDx?.Dispose();
        sellConsecutiveBrushDx?.Dispose();
        buyHistoryBrushDx?.Dispose();
        sellHistoryBrushDx?.Dispose();
    }

    // ----------- Draw rejection dots (au dessus)
    foreach (var dot in rejectionDots)
    {
        if (dot.BarIndex < ChartBars.FromIndex || dot.BarIndex > ChartBars.ToIndex)
            continue;
        float x = chartControl.GetXByBarIndex(ChartBars, dot.BarIndex);
        if (x < ChartPanel.X || x > ChartPanel.X + ChartPanel.W)
            continue;
        float y = chartScale.GetYByValue(dot.Price);
        float dotRadius = Math.Min(4f + (float)Math.Abs(dot.Delta) / 100f, 20f);
        var fillBrush = new SolidColorBrush(dot.Color) { Opacity = dot.Opacity };
        var dxFillBrush = fillBrush.ToDxBrush(RenderTarget);
        var ellipse = new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(x, y), dotRadius, dotRadius);
        RenderTarget.FillEllipse(ellipse, dxFillBrush);
        var outlineBrush = Brushes.White.ToDxBrush(RenderTarget);
        RenderTarget.DrawEllipse(ellipse, outlineBrush, 1f);
    }

    // ----------- Dispose
    buyLineBrushDx.Dispose();
    sellLineBrushDx.Dispose();
    buyBrushDx.Dispose();
    sellBrushDx.Dispose();
    buyPeakBrushDx.Dispose();
    sellPeakBrushDx.Dispose();
    highlightBuyBrushDx.Dispose();
    highlightSellBrushDx.Dispose();
    if (textBrushDx != null)
    {
        textBrushDx.Dispose();
        textFormatLeft?.Dispose();
        textFormatRight?.Dispose();
    }
}


private void ApplyDeltaDot(int barIndex, double price, double delta, bool isBuy)
{
    Color color;
    float opacity = (float)(volumeDeltaOpacity / 100.0);

    if (delta >= volumeDeltaMax)
        color = ((SolidColorBrush)positiveDeltaBrush).Color;
    else if (delta <= -volumeDeltaMax)
        color = ((SolidColorBrush)negativeDeltaBrush).Color;
    else
        return;

        rejectionDots.Add(new RejectionDot
        {
            BarIndex = barIndex,
            Price = price,
            Color = color,
            Opacity = opacity,
            Delta = delta      // <-- Ajout ici
        });

        dottedLines.Add(new DottedLine
        {
            StartBar = barIndex,
            LastBar = barIndex,
            Price = price,
            IsBuy = isBuy,
            Active = true,
			SessionDate = currentDate
        });

}

private DateTime GetSessionDateForBar(int barIdx)
{
    if (Bars == null || barIdx < 0 || barIdx >= Bars.Count)
        return DateTime.MinValue;

    DateTime barTime = Bars.GetTime(barIdx);
    DateTime sessionDate = barTime.Date;
    if (barTime.TimeOfDay < startTime)
        sessionDate = sessionDate.AddDays(-1);
    return sessionDate;
}



        #region Properties
        [Range(0, double.MaxValue)]
        [Display(ResourceType = typeof(Custom.Resource), Name = "Opacity", Order = 0, GroupName = "NinjaScriptParameters")]
        public double Opacity
        {
            get => alpha;
            set => alpha = Math.Max(1, value);
        }

        [Display(ResourceType = typeof(Custom.Resource), Name = "RejectionTicks", Order = 1, GroupName = "NinjaScriptParameters")]
        public int RejectionTicks
        {
            get => rejectionTicks;
            set => rejectionTicks = Math.Max(1, value);
        }

        [Display(ResourceType = typeof(Custom.Resource), Name = "RejectionResetTicks", Order = 2, GroupName = "NinjaScriptParameters")]
        public int RejectionResetTicks
        {
            get => rejectionResetTicks;
            set => rejectionResetTicks = Math.Max(1, value);
        }
		
		[Range(1, int.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "PeakThreshold", Order = 15, GroupName = "NinjaScriptParameters")]
		public int PeakThreshold
		{
		    get => peakThreshold;
		    set => peakThreshold = Math.Max(1, value);
		}

        [XmlIgnore]
        [Display(ResourceType = typeof(Custom.Resource), Name = "BuyColor", Order = 3, GroupName = "NinjaScriptParameters")]
        public Brush BuyBrush { get; set; }

        [Browsable(false)]
        public string BuyBrushSerialize
        {
            get => Serialize.BrushToString(BuyBrush);
            set => BuyBrush = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [Display(ResourceType = typeof(Custom.Resource), Name = "SellColor", Order = 4, GroupName = "NinjaScriptParameters")]
        public Brush SellBrush { get; set; }

        [Browsable(false)]
        public string SellBrushSerialize
        {
            get => Serialize.BrushToString(SellBrush);
            set => SellBrush = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [Display(ResourceType = typeof(Custom.Resource), Name = "BuyPeakColor", Order = 5, GroupName = "NinjaScriptParameters")]
        public Brush BuyPeakBrush { get; set; }

        [Browsable(false)]
        public string BuyPeakBrushSerialize
        {
            get => Serialize.BrushToString(BuyPeakBrush);
            set => BuyPeakBrush = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [Display(ResourceType = typeof(Custom.Resource), Name = "SellPeakColor", Order = 6, GroupName = "NinjaScriptParameters")]
        public Brush SellPeakBrush { get; set; }

        [Browsable(false)]
        public string SellPeakBrushSerialize
        {
            get => Serialize.BrushToString(SellPeakBrush);
            set => SellPeakBrush = Serialize.StringToBrush(value);
        }

        [Range(0, int.MaxValue)]
        [Display(ResourceType = typeof(Custom.Resource), Name = "HistogramWidth", Order = 7, GroupName = "NinjaScriptParameters")]
        public int HistogramWidth { get; set; }

        [Display(ResourceType = typeof(Custom.Resource), Name = "StartTime", Order = 8, GroupName = "NinjaScriptParameters")]
        [XmlIgnore]
        public TimeSpan StartTime
        {
            get => startTime;
            set => startTime = value;
        }

        [Browsable(false)]
        public string StartTimeSerialize
        {
            get => startTime.ToString();
            set => startTime = TimeSpan.Parse(value);
        }

        [Display(ResourceType = typeof(Custom.Resource), Name = "ShowQuantities", Order = 9, GroupName = "NinjaScriptParameters")]
        public bool ShowQuantities
        {
            get => showQuantities;
            set => showQuantities = value;
        }

        [Display(Name = "ShowDeltaVolumes", Order = 9, GroupName = "NinjaScriptParameters")]
        public bool ShowDeltaVolumes
        {
            get => showDeltaVolumes;
            set => showDeltaVolumes = value;
        }

        [XmlIgnore]
        [Display(ResourceType = typeof(Custom.Resource), Name = "HighlightBuyColor", Order = 10, GroupName = "NinjaScriptParameters")]
        public Brush HighlightBuyBrush
        {
            get => highlightBuyBrush;
            set => highlightBuyBrush = value;
        }

        [Browsable(false)]
        public string HighlightBuyBrushSerialize
        {
            get => Serialize.BrushToString(HighlightBuyBrush);
            set => HighlightBuyBrush = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [Display(ResourceType = typeof(Custom.Resource), Name = "HighlightSellColor", Order = 11, GroupName = "NinjaScriptParameters")]
        public Brush HighlightSellBrush
        {
            get => highlightSellBrush;
            set => highlightSellBrush = value;
        }

        [Browsable(false)]
        public string HighlightSellBrushSerialize
        {
            get => Serialize.BrushToString(HighlightSellBrush);
            set => HighlightSellBrush = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [Display(ResourceType = typeof(Custom.Resource), Name = "QuantityTextColor", Order = 12, GroupName = "NinjaScriptParameters")]
        public Brush QuantityTextBrush
        {
            get => quantityTextBrush;
            set => quantityTextBrush = value;
        }

        [Browsable(false)]
        public string QuantityTextBrushSerialize
        {
            get => Serialize.BrushToString(QuantityTextBrush);
            set => QuantityTextBrush = Serialize.StringToBrush(value);
        }

        [Display(ResourceType = typeof(Custom.Resource), Name = "QuantityTextBold", Order = 13, GroupName = "NinjaScriptParameters")]
        public bool QuantityTextBold
        {
            get => quantityTextBold;
            set => quantityTextBold = value;
        }

        [Display(ResourceType = typeof(Custom.Resource), Name = "HistogramOnTop", Order = 14, GroupName = "NinjaScriptParameters")]
		
                public bool HistogramOnTop
                {
                    get => histogramOnTop;
                    set
                    {
                        if (histogramOnTop == value)
                            return;
                        histogramOnTop = value;
                        if (State == State.Realtime || State == State.Historical)
                        {
                            ZOrder = histogramOnTop ? 10000 : -1; // Utilise une valeur plus haute si besoin
                            ChartPanel?.Dispatcher?.InvokeAsync(() =>
                            {
                                ChartPanel.InvalidateVisual();
                            });
                        }
                    }
                }

                [Range(0, int.MaxValue)]
                [Display(Name = "ColoredRejectionCount", GroupName = "NinjaScriptParameters", Order = 16)]
                public int ColoredRejectionCount
                {
                    get => coloredRejectionCount;
                    set => coloredRejectionCount = Math.Max(0, value);
                }

                [XmlIgnore]
                [Display(Name = "ColoredBuyBrush", GroupName = "NinjaScriptParameters", Order = 17)]
                public Brush ColoredBuyBrush
                {
                    get => coloredBuyBrush;
                    set => coloredBuyBrush = value;
                }
                [Browsable(false)]
                public string ColoredBuyBrushSerialize
                {
                    get => Serialize.BrushToString(coloredBuyBrush);
                    set => coloredBuyBrush = Serialize.StringToBrush(value);
                }

                [XmlIgnore]
                [Display(Name = "ColoredSellBrush", GroupName = "NinjaScriptParameters", Order = 18)]
                public Brush ColoredSellBrush
                {
                    get => coloredSellBrush;
                    set => coloredSellBrush = value;
                }
                [Browsable(false)]
                public string ColoredSellBrushSerialize
                {
                    get => Serialize.BrushToString(coloredSellBrush);
                    set => coloredSellBrush = Serialize.StringToBrush(value);
                }
		
		[Range(1, int.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "ConsecutiveRejectionCount", Order = 20, GroupName = "NinjaScriptParameters")]
		public int ConsecutiveRejectionCount
		{
		    get => consecutiveRejectionCount;
		    set => consecutiveRejectionCount = Math.Max(1, value);
		}
		
			
		// Paramètres utilisateurs pour la couleur/épaisseur/style de la ligne
		[XmlIgnore]
		[Display(Name = "ConsecutiveRejectionBrush", GroupName = "NinjaScriptParameters", Order = 22)]
		public Brush ConsecutiveRejectionBrush
		{
		    get => consecutiveRejectionBrush;
		    set => consecutiveRejectionBrush = value;
		}
		[Browsable(false)]
		public string ConsecutiveRejectionBrushSerialize
		{
		    get => Serialize.BrushToString(consecutiveRejectionBrush);
		    set => consecutiveRejectionBrush = Serialize.StringToBrush(value);
		}
		
		[XmlIgnore]
		[Display(Name = "BuyConsecutiveLineBrush", GroupName = "NinjaScriptParameters", Order = 23)]
		public Brush BuyConsecutiveLineBrush
		{
		    get => buyConsecutiveLineBrush;
		    set => buyConsecutiveLineBrush = value;
		}
		[Browsable(false)]
		public string BuyConsecutiveLineBrushSerialize
		{
		    get => Serialize.BrushToString(buyConsecutiveLineBrush);
		    set => buyConsecutiveLineBrush = Serialize.StringToBrush(value);
		}
		
		[XmlIgnore]
		[Display(Name = "SellConsecutiveLineBrush", GroupName = "NinjaScriptParameters", Order = 24)]
		public Brush SellConsecutiveLineBrush
		{
		    get => sellConsecutiveLineBrush;
		    set => sellConsecutiveLineBrush = value;
		}
		[Browsable(false)]
		public string SellConsecutiveLineBrushSerialize
		{
		    get => Serialize.BrushToString(sellConsecutiveLineBrush);
		    set => sellConsecutiveLineBrush = Serialize.StringToBrush(value);
		}
		
		[Range(1, double.MaxValue)]
		[Display(Name = "BuyConsecutiveLineThickness", GroupName = "NinjaScriptParameters", Order = 25)]
		public double BuyConsecutiveLineThickness
		{
		    get => buyConsecutiveLineThickness;
		    set => buyConsecutiveLineThickness = value;
		}
		
		[Range(1, double.MaxValue)]
		[Display(Name = "SellConsecutiveLineThickness", GroupName = "NinjaScriptParameters", Order = 26)]
		public double SellConsecutiveLineThickness
		{
		    get => sellConsecutiveLineThickness;
		    set => sellConsecutiveLineThickness = value;
		}
		
		[Display(Name = "BuyConsecutiveLineDashStyle", GroupName = "NinjaScriptParameters", Order = 27)]
		public DashStyleHelper BuyConsecutiveLineDashStyle
		{
		    get => buyConsecutiveLineDashStyle;
		    set => buyConsecutiveLineDashStyle = value;
		}
		
		[Display(Name = "SellConsecutiveLineDashStyle", GroupName = "NinjaScriptParameters", Order = 28)]
                public DashStyleHelper SellConsecutiveLineDashStyle
                {
                    get => sellConsecutiveLineDashStyle;
                    set => sellConsecutiveLineDashStyle = value;
                }

                [Range(1, double.MaxValue)]
                [Display(Name = "VolumeDeltaMax", GroupName = "NinjaScriptParameters", Order = 29)]
                public double VolumeDeltaMax
                {
                    get => volumeDeltaMax;
                    set => volumeDeltaMax = Math.Max(1, value);
                }

                [XmlIgnore]
                [Display(Name = "PositiveDeltaBrush", GroupName = "NinjaScriptParameters", Order = 30)]
                public Brush PositiveDeltaBrush
                {
                    get => positiveDeltaBrush;
                    set => positiveDeltaBrush = value;
                }
                [Browsable(false)]
                public string PositiveDeltaBrushSerialize
                {
                    get => Serialize.BrushToString(positiveDeltaBrush);
                    set => positiveDeltaBrush = Serialize.StringToBrush(value);
                }

                [XmlIgnore]
                [Display(Name = "NegativeDeltaBrush", GroupName = "NinjaScriptParameters", Order = 31)]
                public Brush NegativeDeltaBrush
                {
                    get => negativeDeltaBrush;
                    set => negativeDeltaBrush = value;
                }
                [Browsable(false)]
                public string NegativeDeltaBrushSerialize
                {
                    get => Serialize.BrushToString(negativeDeltaBrush);
                    set => negativeDeltaBrush = Serialize.StringToBrush(value);
                }

                [Range(0, double.MaxValue)]
                [Display(Name = "VolumeDeltaOpacity", GroupName = "NinjaScriptParameters", Order = 32)]
                public double VolumeDeltaOpacity
                {
                    get => volumeDeltaOpacity;
                    set => volumeDeltaOpacity = Math.Max(0, value);
                }
				
				
				[Display(Name = "Activer Peak Colors", Order = 40, GroupName = "NinjaScriptParameters")]
				public bool UsePeakColors
				{
				    get => usePeakColors;
				    set => usePeakColors = value;
				}
				
				
				
				[Display(Name = "Afficher ligne max BUY", GroupName = "NinjaScriptParameters", Order = 50)]
				public bool ShowMaxBuyRejectionLine
				{
				    get => showMaxBuyRejectionLine;
				    set => showMaxBuyRejectionLine = value;
				}
				
				[Display(Name = "Afficher ligne max SELL", GroupName = "NinjaScriptParameters", Order = 51)]
				public bool ShowMaxSellRejectionLine
				{
				    get => showMaxSellRejectionLine;
				    set => showMaxSellRejectionLine = value;
				}
				
				[XmlIgnore]
				[Display(Name = "Couleur ligne max BUY", GroupName = "NinjaScriptParameters", Order = 52)]
				public Brush MaxBuyRejectionLineBrush
				{
				    get => maxBuyRejectionLineBrush;
				    set => maxBuyRejectionLineBrush = value;
				}
				[Browsable(false)]
				public string MaxBuyRejectionLineBrushSerialize
				{
				    get => Serialize.BrushToString(maxBuyRejectionLineBrush);
				    set => maxBuyRejectionLineBrush = Serialize.StringToBrush(value);
				}
				
				[XmlIgnore]
				[Display(Name = "Couleur ligne max SELL", GroupName = "NinjaScriptParameters", Order = 53)]
				public Brush MaxSellRejectionLineBrush
				{
				    get => maxSellRejectionLineBrush;
				    set => maxSellRejectionLineBrush = value;
				}
				[Browsable(false)]
				public string MaxSellRejectionLineBrushSerialize
				{
				    get => Serialize.BrushToString(maxSellRejectionLineBrush);
				    set => maxSellRejectionLineBrush = Serialize.StringToBrush(value);
				}
				
				[Range(1, double.MaxValue)]
				[Display(Name = "Épaisseur ligne max BUY", GroupName = "NinjaScriptParameters", Order = 54)]
				public double MaxBuyRejectionLineThickness
				{
				    get => maxBuyRejectionLineThickness;
				    set => maxBuyRejectionLineThickness = value;
				}
				
				[Range(1, double.MaxValue)]
				[Display(Name = "Épaisseur ligne max SELL", GroupName = "NinjaScriptParameters", Order = 55)]
				public double MaxSellRejectionLineThickness
				{
				    get => maxSellRejectionLineThickness;
				    set => maxSellRejectionLineThickness = value;
				}
				
				[Display(Name = "Style ligne max BUY", GroupName = "NinjaScriptParameters", Order = 56)]
				public DashStyleHelper MaxBuyRejectionLineDashStyle
				{
				    get => maxBuyRejectionLineDashStyle;
				    set => maxBuyRejectionLineDashStyle = value;
				}
				
				[Display(Name = "Style ligne max SELL", GroupName = "NinjaScriptParameters", Order = 57)]
				public DashStyleHelper MaxSellRejectionLineDashStyle
				{
				    get => maxSellRejectionLineDashStyle;
				    set => maxSellRejectionLineDashStyle = value;
				}
				
				[Display(Name = "Afficher signaux flèche", GroupName = "NinjaScriptParameters", Order = 60)]
				public bool ShowArrowSignals
				{
				    get => showArrowSignals;
				    set => showArrowSignals = value;
}

        private SessionIterator SessionIterator => sessionIterator ??= new SessionIterator(Bars);
        #endregion	
	
	
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BigTradersProfile[] cacheBigTradersProfile;
		public BigTradersProfile BigTradersProfile()
		{
			return BigTradersProfile(Input);
		}

		public BigTradersProfile BigTradersProfile(ISeries<double> input)
		{
			if (cacheBigTradersProfile != null)
				for (int idx = 0; idx < cacheBigTradersProfile.Length; idx++)
					if (cacheBigTradersProfile[idx] != null &&  cacheBigTradersProfile[idx].EqualsInput(input))
						return cacheBigTradersProfile[idx];
			return CacheIndicator<BigTradersProfile>(new BigTradersProfile(), input, ref cacheBigTradersProfile);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BigTradersProfile BigTradersProfile()
		{
			return indicator.BigTradersProfile(Input);
		}

		public Indicators.BigTradersProfile BigTradersProfile(ISeries<double> input )
		{
			return indicator.BigTradersProfile(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BigTradersProfile BigTradersProfile()
		{
			return indicator.BigTradersProfile(Input);
		}

		public Indicators.BigTradersProfile BigTradersProfile(ISeries<double> input )
		{
			return indicator.BigTradersProfile(input);
		}
	}
}

#endregion
