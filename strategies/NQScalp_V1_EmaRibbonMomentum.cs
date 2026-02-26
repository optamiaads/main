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
    /// Variant 1 — EMA Ribbon Momentum Scalp
    ///
    /// Concept:
    ///   Trade in the direction of a clean EMA stack (13 > 21 > 50 for longs,
    ///   13 < 21 < 50 for shorts). MACD histogram must be positive and increasing
    ///   (momentum accelerating). RSI must be in the "momentum sweet-spot" (40-70
    ///   for longs, 30-60 for shorts) to avoid chasing overbought/oversold extremes.
    ///
    /// Entry:
    ///   LONG  — EMA 13 > 21 > 50 + MACD Hist > 0 and rising + RSI 40-70
    ///   SHORT — EMA 13 < 21 < 50 + MACD Hist < 0 and falling + RSI 30-60
    ///
    /// Exit:
    ///   Profit target = user-defined ticks (default 16 = 4 pts NQ)
    ///   Stop loss     = user-defined ticks (default 12 = 3 pts NQ)
    ///   Timeout       = MaxBarsInTrade bars without hitting target/stop
    ///
    /// Timeframe: 1-minute chart (primary), entry signals evaluated every bar close.
    /// </summary>
    public class NQScalp_V1_EmaRibbonMomentum : Strategy
    {
        #region Parameters
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Profit Target (ticks)", GroupName = "Trade Management", Order = 0)]
        public int ProfitTicks { get; set; } = 16;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop Loss (ticks)", GroupName = "Trade Management", Order = 1)]
        public int StopTicks { get; set; } = 12;

        [NinjaScriptProperty]
        [Range(5, 200)]
        [Display(Name = "Max Bars In Trade", GroupName = "Trade Management", Order = 2)]
        public int MaxBarsInTrade { get; set; } = 30;

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Cooldown Bars", GroupName = "Trade Management", Order = 3)]
        public int CooldownBars { get; set; } = 5;

        [NinjaScriptProperty]
        [Display(Name = "Enable Shorts", GroupName = "Trade Management", Order = 4)]
        public bool EnableShorts { get; set; } = true;

        [NinjaScriptProperty]
        [Range(30, 80)]
        [Display(Name = "RSI Long Upper Limit", GroupName = "Filters", Order = 0)]
        public int RsiLongUpper { get; set; } = 70;

        [NinjaScriptProperty]
        [Range(20, 60)]
        [Display(Name = "RSI Long Lower Limit", GroupName = "Filters", Order = 1)]
        public int RsiLongLower { get; set; } = 40;
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
                    Name                    = "NQScalp_V1_EmaRibbonMomentum";
                    Description             = "EMA ribbon stack + MACD histogram acceleration + RSI momentum zone";
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

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 55) return;

            // ── Exit: time-based stop ──
            if (Position.MarketPosition != MarketPosition.Flat
                && BarsSinceEntryExecution() >= MaxBarsInTrade)
            {
                ExitLong("Timeout", "RibbonLong");
                ExitShort("Timeout", "RibbonShort");
                return;
            }

            // ── Cooldown ──
            if (CurrentBar - lastEntryBar < CooldownBars) return;

            // ── Indicators ──
            bool emaStackUp   = ema13[0] > ema21[0] && ema21[0] > ema50[0];
            bool emaStackDown = ema13[0] < ema21[0] && ema21[0] < ema50[0];

            double hist0 = macd.Diff[0];
            double hist1 = macd.Diff[1];
            bool   histRising  = hist0 > 0 && hist0 > hist1;
            bool   histFalling = hist0 < 0 && hist0 < hist1;

            double rsiVal = rsi[0];
            bool   rsiLongOk  = rsiVal >= RsiLongLower && rsiVal <= RsiLongUpper;
            bool   rsiShortOk = rsiVal >= (100 - RsiLongUpper) && rsiVal <= (100 - RsiLongLower);

            // ── LONG ──
            if (Position.MarketPosition == MarketPosition.Flat
                && emaStackUp && histRising && rsiLongOk)
            {
                EnterLong(DefaultQuantity, "RibbonLong");
                SetProfitTarget("RibbonLong", CalculationMode.Ticks, ProfitTicks);
                SetStopLoss("RibbonLong", CalculationMode.Ticks, StopTicks, false);
                lastEntryBar = CurrentBar;
            }

            // ── SHORT ──
            if (EnableShorts
                && Position.MarketPosition == MarketPosition.Flat
                && emaStackDown && histFalling && rsiShortOk)
            {
                EnterShort(DefaultQuantity, "RibbonShort");
                SetProfitTarget("RibbonShort", CalculationMode.Ticks, ProfitTicks);
                SetStopLoss("RibbonShort", CalculationMode.Ticks, StopTicks, false);
                lastEntryBar = CurrentBar;
            }
        }
    }
}
