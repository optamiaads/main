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
    /// Variant 2 — MACD Zero-Line Cross Scalp
    ///
    /// Concept:
    ///   The MACD line crossing the zero line signals a shift in medium-term momentum.
    ///   Filter with EMA trend direction (price above 21 EMA which is above 50 EMA)
    ///   and RSI not at extremes to avoid traps.
    ///
    /// Entry:
    ///   LONG  — MACD crosses above 0 (bar 0 > 0, bar 1 <= 0)
    ///           + Close > EMA(21) > EMA(50)
    ///           + RSI > 45 and RSI < 75
    ///   SHORT — MACD crosses below 0 (bar 0 < 0, bar 1 >= 0)
    ///           + Close < EMA(21) < EMA(50)
    ///           + RSI < 55 and RSI > 25
    ///
    /// Exit:
    ///   Profit target / Stop loss in ticks, or MACD reverses back across zero.
    ///
    /// Timeframe: 1-minute chart.
    /// </summary>
    public class NQScalp_V2_MacdZeroCross : Strategy
    {
        #region Parameters
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Profit Target (ticks)", GroupName = "Trade Management", Order = 0)]
        public int ProfitTicks { get; set; } = 20;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop Loss (ticks)", GroupName = "Trade Management", Order = 1)]
        public int StopTicks { get; set; } = 12;

        [NinjaScriptProperty]
        [Range(5, 200)]
        [Display(Name = "Max Bars In Trade", GroupName = "Trade Management", Order = 2)]
        public int MaxBarsInTrade { get; set; } = 40;

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Cooldown Bars", GroupName = "Trade Management", Order = 3)]
        public int CooldownBars { get; set; } = 8;

        [NinjaScriptProperty]
        [Display(Name = "Enable Shorts", GroupName = "Trade Management", Order = 4)]
        public bool EnableShorts { get; set; } = true;

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MACD Fast", GroupName = "Indicators", Order = 0)]
        public int MacdFast { get; set; } = 12;

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "MACD Slow", GroupName = "Indicators", Order = 1)]
        public int MacdSlow { get; set; } = 26;

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MACD Smooth", GroupName = "Indicators", Order = 2)]
        public int MacdSmooth { get; set; } = 9;
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
                    Name                    = "NQScalp_V2_MacdZeroCross";
                    Description             = "MACD zero-line crossover + EMA trend filter + RSI guard";
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
                    macd  = MACD(Close, MacdFast, MacdSlow, MacdSmooth);
                    rsi   = RSI(Close, 14, 3);
                    break;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 55) return;

            // ── Discretionary exit: MACD reverses back through zero ──
            if (Position.MarketPosition == MarketPosition.Long && macd.Value[0] < 0)
            {
                ExitLong("MACD Rev", "ZeroCrossLong");
                return;
            }
            if (Position.MarketPosition == MarketPosition.Short && macd.Value[0] > 0)
            {
                ExitShort("MACD Rev", "ZeroCrossShort");
                return;
            }

            // ── Time stop ──
            if (Position.MarketPosition != MarketPosition.Flat
                && BarsSinceEntryExecution() >= MaxBarsInTrade)
            {
                ExitLong("Timeout", "ZeroCrossLong");
                ExitShort("Timeout", "ZeroCrossShort");
                return;
            }

            if (CurrentBar - lastEntryBar < CooldownBars) return;

            // ── MACD zero-line crossover detection ──
            bool macdCrossUp   = macd.Value[0] > 0 && macd.Value[1] <= 0;
            bool macdCrossDown = macd.Value[0] < 0 && macd.Value[1] >= 0;

            // ── EMA trend bias ──
            bool emaLongBias  = Close[0] > ema21[0] && ema21[0] > ema50[0];
            bool emaShortBias = Close[0] < ema21[0] && ema21[0] < ema50[0];

            double rsiVal = rsi[0];

            // ── LONG ──
            if (Position.MarketPosition == MarketPosition.Flat
                && macdCrossUp && emaLongBias
                && rsiVal > 45 && rsiVal < 75)
            {
                EnterLong(DefaultQuantity, "ZeroCrossLong");
                SetProfitTarget("ZeroCrossLong", CalculationMode.Ticks, ProfitTicks);
                SetStopLoss("ZeroCrossLong", CalculationMode.Ticks, StopTicks, false);
                lastEntryBar = CurrentBar;
            }

            // ── SHORT ──
            if (EnableShorts
                && Position.MarketPosition == MarketPosition.Flat
                && macdCrossDown && emaShortBias
                && rsiVal > 25 && rsiVal < 55)
            {
                EnterShort(DefaultQuantity, "ZeroCrossShort");
                SetProfitTarget("ZeroCrossShort", CalculationMode.Ticks, ProfitTicks);
                SetStopLoss("ZeroCrossShort", CalculationMode.Ticks, StopTicks, false);
                lastEntryBar = CurrentBar;
            }
        }
    }
}
