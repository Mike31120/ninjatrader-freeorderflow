#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.MyOrderFlowCustom
{
        public class MofVWAP : Indicator
	{
		private Series<double> cumVol;
		private Series<double> cumPV;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"My Order Flow Custom VWAP";
				Name										= "VWAP";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= true;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event.
				//See Help Guide for additional information.
				IsSuspendedWhileInactive					= true;
				SlopeLookbackBars						= 10;
				BullishSlopeThresholdDegrees			= 5.0;
				BearishSlopeThresholdDegrees			= 5.0;
				BullishBrush							= Brushes.LimeGreen;
				BearishBrush							= Brushes.Red;
				FlatBrush							= Brushes.Orange;
				AddPlot(FlatBrush, "VWAP");
			}
			else if (State == State.DataLoaded)
			{
				cumVol = new Series<double>(this);
				cumPV = new Series<double>(this);
			} else if (State == State.Historical) {
				// Displays a message if the bartype is not intraday
				if (!Bars.BarsType.IsIntraday)
				{
					Draw.TextFixed(this, "NinjaScriptInfo", "VwapAR Indicator only supports Intraday charts", TextPosition.BottomRight);
					Log("VwapAR only supports Intraday charts", LogLevel.Error);
				}
			}
		}

		protected override void OnBarUpdate()
		{
			if(Bars.IsFirstBarOfSession)
			{
				if(CurrentBar > 0) Values[0].Reset(1);
				cumVol[1] = 0;
				cumPV[1] = 0;
			}

			cumPV[0] = cumPV[1] + (Typical[0] * Volume[0]);
			cumVol[0] = cumVol[1] + Volume[0];

			// plot VWAP value
			Values[0][0] = cumPV[0] / (cumVol[0] == 0 ? 1 : cumVol[0]);

			UpdateVwapBrush();
		}

		private void UpdateVwapBrush()
		{
			if (CurrentBar < SlopeLookbackBars)
			{
				PlotBrushes[0][0] = FlatBrush;
				return;
			}

			double delta = Values[0][0] - Values[0][SlopeLookbackBars];
			double slopePerBar = delta / SlopeLookbackBars;
			double slopeDegrees = Math.Atan(slopePerBar) * 180.0 / Math.PI;

			if (slopeDegrees >= BullishSlopeThresholdDegrees)
			{
				PlotBrushes[0][0] = BullishBrush;
			}
			else if (slopeDegrees <= -BearishSlopeThresholdDegrees)
			{
				PlotBrushes[0][0] = BearishBrush;
			}
			else
			{
				PlotBrushes[0][0] = FlatBrush;
			}
		}

		#region Properties
		[Range(1, int.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Slope Lookback Bars", GroupName = "Slope", Order = 0)]
		public int SlopeLookbackBars { get; set; }

		[Range(0.0, double.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Bullish Threshold (Degrees)", GroupName = "Slope", Order = 1)]
		public double BullishSlopeThresholdDegrees { get; set; }

		[Range(0.0, double.MaxValue)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Bearish Threshold (Degrees)", GroupName = "Slope", Order = 2)]
		public double BearishSlopeThresholdDegrees { get; set; }

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Bullish Color", GroupName = "Visual", Order = 0)]
		public Brush BullishBrush { get; set; }

		[Browsable(false)]
		public string BullishBrushSerializable
		{
			get { return Serialize.BrushToString(BullishBrush); }
			set { BullishBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Bearish Color", GroupName = "Visual", Order = 1)]
		public Brush BearishBrush { get; set; }

		[Browsable(false)]
		public string BearishBrushSerializable
		{
			get { return Serialize.BrushToString(BearishBrush); }
			set { BearishBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Flat Color", GroupName = "Visual", Order = 2)]
		public Brush FlatBrush { get; set; }

		[Browsable(false)]
		public string FlatBrushSerializable
		{
			get { return Serialize.BrushToString(FlatBrush); }
			set { FlatBrush = Serialize.StringToBrush(value); }
		}
		#endregion
	}
}
