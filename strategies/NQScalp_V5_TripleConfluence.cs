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
    /// Variant 5 — Triple Confluence Scalp
    ///
    /// Concept:
    ///   The most conservative variant. ALL three indicator families must signal
    ///   simultaneously. This produces fewer trades but higher-probability setups.
    ///
    ///   Layer 1 — EMA: Clean stack (13 > 21 > 50) AND price closed above 13 EMA
    ///             (strong trend, not just marginal)
    ///   Layer 2 — MACD: Both the MACD line AND the histogram must be positive/negative.
    ///             Additionally, the signal line must agree (MACD line above signal for longs).
    ///   Layer 3 — RSI: Must have crossed above 50 from below within the last 3 bars (longs)
    ///             confirming fresh bullish momentum, and current RSI between 50-70.
    ///
    /// Entry (LONG):
    ///   Layer 1: Close > EMA(13) > EMA(21) > EMA(50)
    ///   Layer 2: MACD line > 0 AND MACD line > Signal (Avg) AND Histogram > 0
    ///   Layer 3: RSI crossed above 50 within 3 bars AND current RSI 50-70
    ///
    /// Entry (SHORT — mirror):
    ///   Layer 1: Close < EMA(13) < EMA(21) < EMA(50)
    ///   Layer 2: MACD line < 0 AND MACD line < Signal AND Histogram < 0
    ///   Layer 3: RSI crossed below 50 within 3 bars AND RSI 30-50
    ///
    /// Exit:
    ///   Wider profit target (reward for patience), tight stop, trailing stop
    ///   via EMA(13) — if price closes below 13 EMA, bail.
    ///
    /// Timeframe: 1-minute chart.
    /// </summary>
    public class NQScalp_V5_TripleConfluence : Strategy
    {
        #region Parameters
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Profit Target (ticks)", GroupName = "Trade Management", Order = 0)]
        public int ProfitTicks { get; set; } = 28;

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
        public int CooldownBars { get; set; } = 10;

        [NinjaScriptProperty]
        [Display(Name = "Enable Shorts", GroupName = "Trade Management", Order = 4)]
        public bool EnableShorts { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name = "Use EMA(13) Trailing Exit", GroupName = "Trade Management", Order = 5)]
        public bool UseEmaTrailingExit { get; set; } = true;
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
                    Name                    = "NQScalp_V5_TripleConfluence";
                    Description             = "All 3 indicators must confirm: EMA stack + MACD line/signal/hist + RSI 50-cross";
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
        /// Check if RSI crossed above the given level within the last N bars.
        /// </summary>
        private bool RsiCrossedAboveRecently(double level, int withinBars)
        {
            for (int i = 0; i < withinBars; i++)
            {
                if (rsi[i] > level && rsi[i + 1] <= level)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Check if RSI crossed below the given level within the last N bars.
        /// </summary>
        private bool RsiCrossedBelowRecently(double level, int withinBars)
        {
            for (int i = 0; i < withinBars; i++)
            {
                if (rsi[i] < level && rsi[i + 1] >= level)
                    return true;
            }
            return false;
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 60) return;

            // ── Trailing exit: EMA(13) ──
            if (UseEmaTrailingExit)
            {
                if (Position.MarketPosition == MarketPosition.Long && Close[0] < ema13[0])
                {
                    ExitLong("EMA13 Trail", "ConfluenceLong");
                    return;
                }
                if (Position.MarketPosition == MarketPosition.Short && Close[0] > ema13[0])
                {
                    ExitShort("EMA13 Trail", "ConfluenceShort");
                    return;
                }
            }

            // ── Time stop ──
            if (Position.MarketPosition != MarketPosition.Flat
                && BarsSinceEntryExecution() >= MaxBarsInTrade)
            {
                ExitLong("Timeout", "ConfluenceLong");
                ExitShort("Timeout", "ConfluenceShort");
                return;
            }

            if (CurrentBar - lastEntryBar < CooldownBars) return;

            double rsiVal  = rsi[0];
            double macdVal = macd.Value[0];   // MACD line
            double sigVal  = macd.Avg[0];     // signal line
            double histVal = macd.Diff[0];    // histogram

            // ══════════════════════════════════════════════
            //  LAYER 1 — EMA STACK + price above/below 13
            // ══════════════════════════════════════════════
            bool emaLong  = Close[0] > ema13[0] && ema13[0] > ema21[0] && ema21[0] > ema50[0];
            bool emaShort = Close[0] < ema13[0] && ema13[0] < ema21[0] && ema21[0] < ema50[0];

            // ══════════════════════════════════════════════
            //  LAYER 2 — MACD: line + signal + histogram
            // ══════════════════════════════════════════════
            bool macdLong  = macdVal > 0 && macdVal > sigVal && histVal > 0;
            bool macdShort = macdVal < 0 && macdVal < sigVal && histVal < 0;

            // ══════════════════════════════════════════════
            //  LAYER 3 — RSI 50-cross + zone
            // ══════════════════════════════════════════════
            bool rsiLong  = RsiCrossedAboveRecently(50, 3) && rsiVal >= 50 && rsiVal <= 70;
            bool rsiShort = RsiCrossedBelowRecently(50, 3) && rsiVal >= 30 && rsiVal <= 50;

            // ── LONG ──
            if (Position.MarketPosition == MarketPosition.Flat
                && emaLong && macdLong && rsiLong)
            {
                EnterLong(DefaultQuantity, "ConfluenceLong");
                SetProfitTarget("ConfluenceLong", CalculationMode.Ticks, ProfitTicks);
                SetStopLoss("ConfluenceLong", CalculationMode.Ticks, StopTicks, false);
                lastEntryBar = CurrentBar;
            }

            // ── SHORT ──
            if (EnableShorts
                && Position.MarketPosition == MarketPosition.Flat
                && emaShort && macdShort && rsiShort)
            {
                EnterShort(DefaultQuantity, "ConfluenceShort");
                SetProfitTarget("ConfluenceShort", CalculationMode.Ticks, ProfitTicks);
                SetStopLoss("ConfluenceShort", CalculationMode.Ticks, StopTicks, false);
                lastEntryBar = CurrentBar;
            }
        }
    }
}
