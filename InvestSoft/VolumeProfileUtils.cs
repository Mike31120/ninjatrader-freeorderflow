#region Using declarations
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
#endregion

namespace InvestSoft.NinjaScript.VolumeProfile
{
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
            // caculate POC
            if (row.total > MaxVolume)
            {
                MaxVolume = row.total;
                POC = price;
            }
            // calculate total volume for use in VAL and VAH
            TotalVolume += (buyVolume + sellVolume + otherVolume);
            return row;
        }

        public void CalculateValueArea(float valueAreaPerc)
        {
            if (Count == 0 || POC == 0) return;

            // Calculate the total trading volume
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
        }

        internal SharpDX.RectangleF GetBarRect(
            MofVolumeProfileData profile, double price, long volume,
            bool fullwidth = false, bool inWindow = true
        )
        {
            // bar height and Y
            var tickSize = chartControl.Instrument.MasterInstrument.TickSize;
            float ypos = chartScale.GetYByValue(price + tickSize);
            float barHeight = chartScale.GetYByValue(price) - ypos;
            // center bar on price tick
            int halfBarDistance = (int)Math.Max(1, chartScale.GetPixelsForDistance(tickSize)) / 2; //pixels
            ypos += halfBarDistance;
            // bar width and X
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
            float barWidth = (fullwidth) ? maxWidth : (
                maxWidth * (volume / (float)profile.MaxVolume) * WidthPercent
            );
            return new SharpDX.RectangleF(xpos, ypos, barWidth, barHeight);
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

                if (row.Key >= profile.VAL && row.Key <= profile.VAH)
                {
                    brush.Opacity = ValueAreaOpacity;
                    renderTarget.FillRectangle(rect, brush);
                }
                else
                {
                    brush.Opacity = Opacity;
                    renderTarget.FillRectangle(rect, brush);
                }
            }
        }

        internal void RenderPoc(MofVolumeProfileData profile, Brush brush, float width, StrokeStyle strokeStyle, bool drawText = false)
        {
            var pocRect = GetBarRect(profile, profile.POC, profile.MaxVolume);
            renderTarget.FillRectangle(pocRect, brush);

            pocRect = GetBarRect(profile, profile.POC, profile.MaxVolume, true);
            pocRect.Y += pocRect.Height / 2;
            renderTarget.DrawLine(
                pocRect.TopLeft, pocRect.TopRight,
                brush, width, strokeStyle
            );
            if (drawText)
            {
                RnederText(
                    string.Format("{0}", profile.POC),
                    new SharpDX.Vector2(pocRect.Left, pocRect.Top),
                    brush,
                    pocRect.Width,
                    TextAlignment.Trailing
                );
            }
        }

        internal void RenderValueArea(MofVolumeProfileData profile, Brush brush, float width, StrokeStyle strokeStyle, bool drawText = false)
        {
            // draw VAH
            if (profile.ContainsKey(profile.VAH))
            {
                var vahRect = GetBarRect(profile, profile.VAH, profile[profile.VAH].total, true);
                vahRect.Y += vahRect.Height / 2;
                renderTarget.DrawLine(
                    vahRect.TopLeft, vahRect.TopRight,
                    brush, width, strokeStyle
                );
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
            // draw VAL
            if (profile.ContainsKey(profile.VAL))
            {
                var valRect = GetBarRect(profile, profile.VAL, profile[profile.VAL].total, true);
                valRect.Y += valRect.Height / 2;
                renderTarget.DrawLine(
                    valRect.TopLeft, valRect.TopRight,
                    brush, width, strokeStyle
                );
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
                renderTarget.FillRectangle(
                    rect, (row.Value.buy > row.Value.sell) ? buyBrush : sellBrush
                );
            }
        }

        internal void RenderLevels(MofVolumeProfileData profile, IEnumerable<double> levels,
            Brush brush, float width, StrokeStyle strokeStyle, bool drawText = false)
        {
            foreach (double price in levels)
            {
                var rect = GetBarRect(profile, price,
                    profile.ContainsKey(price) ? profile[price].total : profile.MaxVolume, true);
                rect.Y += rect.Height / 2;
                renderTarget.DrawLine(rect.TopLeft, rect.TopRight, brush, width, strokeStyle);
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
            if (textWidth > maxWidth) return;
            renderTarget.DrawTextLayout(position, textLayout, brush);
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
            if (textWidth > maxWidth) return;
            var offset = new SharpDX.Vector2(position.X + 1, position.Y);
            renderTarget.DrawTextLayout(offset, textLayout, brush);
            renderTarget.DrawTextLayout(position, textLayout, brush);
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
        }
    }
    #endregion

    public enum MofVolumeProfileMode { Standard, BuySell, Delta };
    public enum MofVolumeProfilePeriod { Sessions, Bars };
    public enum MofVolumeProfileResolution { Tick, Minute };
    public enum PlateauSelectionMode { Lowest, Highest, Central };
}
