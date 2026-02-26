using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Variant 4 — RSI + MACD Hidden Divergence Scalp
    ///
    /// Concept:
    ///   Hidden divergence signals trend continuation:
    ///     Bullish hidden div = price makes a higher low, RSI makes a lower low
    ///     Bearish hidden div = price makes a lower high, RSI makes a higher high
    ///   This is a scalping adaptation — we look back over a short window (LookbackBars)
    ///   and confirm with EMA trend + MACD histogram direction.
    ///
    /// Entry (LONG):
    ///   1. EMA 13 > 21 > 50   (uptrend filter)
    ///   2. Bullish hidden divergence detected (higher price low, lower RSI low)
    ///   3. MACD histogram positive or turning positive
    ///   4. RSI currently between 35-60 (reload zone after dip)
    ///
    /// Entry (SHORT):
    ///   1. EMA 13 < 21 < 50   (downtrend filter)
    ///   2. Bearish hidden divergence (lower price high, higher RSI high)
    ///   3. MACD histogram negative or turning negative
    ///   4. RSI 40-65
    ///
    /// Exit: Target/stop ticks, or EMA structure break.
    ///
    /// Timeframe: 1-minute chart.
    /// </summary>
    public class NQScalp_V4_RsiMacdDivergence : Strategy
    {
        #region Parameters
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Profit Target (ticks)", GroupName = "Trade Management", Order = 0)]
        public int ProfitTicks { get; set; } = 24;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop Loss (ticks)", GroupName = "Trade Management", Order = 1)]
        public int StopTicks { get; set; } = 12;

        [NinjaScriptProperty]
        [Range(5, 200)]
        [Display(Name = "Max Bars In Trade", GroupName = "Trade Management", Order = 2)]
        public int MaxBarsInTrade { get; set; } = 35;

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Cooldown Bars", GroupName = "Trade Management", Order = 3)]
        public int CooldownBars { get; set; } = 8;

        [NinjaScriptProperty]
        [Display(Name = "Enable Shorts", GroupName = "Trade Management", Order = 4)]
        public bool EnableShorts { get; set; } = true;

        [NinjaScriptProperty]
        [Range(5, 30)]
        [Display(Name = "Divergence Lookback (bars)", GroupName = "Filters", Order = 0)]
        public int LookbackBars { get; set; } = 10;
        #endregion

        private EMA ema13, ema21, ema50;
        private MACD macd;
        private RSI rsi;
        private int lastEntryBar = -100;

        protected override void OnStateChange()
        {
            switch (State)
            {
                case State.SetDefaults:
                    Name                    = "NQScalp_V4_RsiMacdDivergence";
                    Description             = "Hidden RSI divergence + EMA trend filter + MACD confirmation";
                    Calculate               = Calculate.OnBarClose;
                    EntriesPerDirection     = 1;
                    EntryHandling           = EntryHandling.AllEntries;
                    IsExitOnSessionCloseStrategy = true;
                    ExitOnSessionCloseSeconds    = 30;
                    DefaultQuantity         = 1;
                    break;

                case State.DataLoaded:
                    ema13 = EMA(Close, 13);
                    ema21 = EMA(Close, 21);
                    ema50 = EMA(Close, 50);
                    macd  = MACD(Close, 12, 26, 9);
                    rsi   = RSI(Close, 14, 3);
                    break;
            }
        }

        /// <summary>
        /// Scan back over LookbackBars to find the lowest low in price and RSI.
        /// Bullish hidden divergence: current price low > prior price low, but
        /// current RSI low < prior RSI low.
        /// </summary>
        private bool BullishHiddenDivergence()
        {
            double currPriceLow = Low[0];
            double currRsiLow   = rsi[0];
            double priorPriceLow = double.MaxValue;
            double priorRsiLow   = double.MaxValue;

            // Find the lowest price low and its RSI in the lookback window
            for (int i = 2; i <= LookbackBars; i++)
            {
                if (Low[i] < priorPriceLow)
                {
                    priorPriceLow = Low[i];
                    priorRsiLow   = rsi[i];
                }
            }

            // Also check bar 1 for current swing low
            if (Low[1] < currPriceLow) currPriceLow = Low[1];
            if (rsi[1] < currRsiLow) currRsiLow = rsi[1];

            return currPriceLow > priorPriceLow && currRsiLow < priorRsiLow;
        }

        /// <summary>
        /// Bearish hidden divergence: current price high < prior price high, but
        /// current RSI high > prior RSI high.
        /// </summary>
        private bool BearishHiddenDivergence()
        {
            double currPriceHigh = High[0];
            double currRsiHigh   = rsi[0];
            double priorPriceHigh = double.MinValue;
            double priorRsiHigh   = double.MinValue;

            for (int i = 2; i <= LookbackBars; i++)
            {
                if (High[i] > priorPriceHigh)
                {
                    priorPriceHigh = High[i];
                    priorRsiHigh   = rsi[i];
                }
            }

            if (High[1] > currPriceHigh) currPriceHigh = High[1];
            if (rsi[1] > currRsiHigh) currRsiHigh = rsi[1];

            return currPriceHigh < priorPriceHigh && currRsiHigh > priorRsiHigh;
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 55 + LookbackBars) return;

            // ── Exit: EMA structure break ──
            if (Position.MarketPosition == MarketPosition.Long && ema13[0] < ema21[0])
            {
                ExitLong("EMA Break", "DivLong");
                return;
            }
            if (Position.MarketPosition == MarketPosition.Short && ema13[0] > ema21[0])
            {
                ExitShort("EMA Break", "DivShort");
                return;
            }

            // ── Time stop ──
            if (Position.MarketPosition != MarketPosition.Flat
                && BarsSinceEntryExecution() >= MaxBarsInTrade)
            {
                ExitLong("Timeout", "DivLong");
                ExitShort("Timeout", "DivShort");
                return;
            }

            if (CurrentBar - lastEntryBar < CooldownBars) return;

            double rsiVal = rsi[0];

            // ── LONG: bullish hidden divergence ──
            bool uptrend = ema13[0] > ema21[0] && ema21[0] > ema50[0];

            if (Position.MarketPosition == MarketPosition.Flat
                && uptrend
                && BullishHiddenDivergence()
                && macd.Diff[0] > 0
                && rsiVal >= 35 && rsiVal <= 60)
            {
                EnterLong(DefaultQuantity, "DivLong");
                SetProfitTarget("DivLong", CalculationMode.Ticks, ProfitTicks);
                SetStopLoss("DivLong", CalculationMode.Ticks, StopTicks, false);
                lastEntryBar = CurrentBar;
            }

            // ── SHORT: bearish hidden divergence ──
            if (!EnableShorts) return;

            bool downtrend = ema13[0] < ema21[0] && ema21[0] < ema50[0];

            if (Position.MarketPosition == MarketPosition.Flat
                && downtrend
                && BearishHiddenDivergence()
                && macd.Diff[0] < 0
                && rsiVal >= 40 && rsiVal <= 65)
            {
                EnterShort(DefaultQuantity, "DivShort");
                SetProfitTarget("DivShort", CalculationMode.Ticks, ProfitTicks);
                SetStopLoss("DivShort", CalculationMode.Ticks, StopTicks, false);
                lastEntryBar = CurrentBar;
            }
        }
    }
}
