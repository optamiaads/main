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
    /// Variant 3 — EMA Pullback Scalp
    ///
    /// Concept:
    ///   In a trending market (EMA 13 > 21 > 50), price often pulls back to the
    ///   21 EMA before resuming. This strategy waits for price to touch or pierce
    ///   the 21 EMA, then enters when MACD histogram turns positive again and
    ///   RSI bounces off the 40-50 "reload zone."
    ///
    /// Entry (LONG):
    ///   1. EMA 13 > EMA 21 > EMA 50   (uptrend)
    ///   2. Low[0] <= EMA(21) or Low[1] <= EMA(21)   (pullback touched 21 EMA)
    ///   3. Close[0] > EMA(21)   (price reclaimed 21 EMA)
    ///   4. MACD histogram > 0   (momentum flipped positive)
    ///   5. RSI between 40-65   (not overbought, coming off reload zone)
    ///
    /// Entry (SHORT — mirror):
    ///   1. EMA 13 < 21 < 50
    ///   2. High touched/pierced 21 EMA, then Close back below
    ///   3. MACD histogram < 0, RSI 35-60
    ///
    /// Exit:
    ///   Profit target, stop loss, or EMA 13 crosses below EMA 21 (trend break).
    ///
    /// Timeframe: 1-minute chart.
    /// </summary>
    public class NQScalp_V3_EmaPullback : Strategy
    {
        #region Parameters
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Profit Target (ticks)", GroupName = "Trade Management", Order = 0)]
        public int ProfitTicks { get; set; } = 20;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop Loss (ticks)", GroupName = "Trade Management", Order = 1)]
        public int StopTicks { get; set; } = 10;

        [NinjaScriptProperty]
        [Range(5, 200)]
        [Display(Name = "Max Bars In Trade", GroupName = "Trade Management", Order = 2)]
        public int MaxBarsInTrade { get; set; } = 25;

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Cooldown Bars", GroupName = "Trade Management", Order = 3)]
        public int CooldownBars { get; set; } = 6;

        [NinjaScriptProperty]
        [Display(Name = "Enable Shorts", GroupName = "Trade Management", Order = 4)]
        public bool EnableShorts { get; set; } = true;

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Pullback Tolerance (pts)", GroupName = "Filters", Order = 0)]
        public double PullbackTolerance { get; set; } = 1.0;
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
                    Name                    = "NQScalp_V3_EmaPullback";
                    Description             = "EMA pullback to 21 + MACD flip + RSI reload zone";
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

            // ── Exit: trend structure break ──
            if (Position.MarketPosition == MarketPosition.Long && ema13[0] < ema21[0])
            {
                ExitLong("EMA Break", "PullbackLong");
                return;
            }
            if (Position.MarketPosition == MarketPosition.Short && ema13[0] > ema21[0])
            {
                ExitShort("EMA Break", "PullbackShort");
                return;
            }

            // ── Time stop ──
            if (Position.MarketPosition != MarketPosition.Flat
                && BarsSinceEntryExecution() >= MaxBarsInTrade)
            {
                ExitLong("Timeout", "PullbackLong");
                ExitShort("Timeout", "PullbackShort");
                return;
            }

            if (CurrentBar - lastEntryBar < CooldownBars) return;

            double rsiVal = rsi[0];

            // ── LONG PULLBACK ──
            bool uptrend      = ema13[0] > ema21[0] && ema21[0] > ema50[0];
            bool touchedEma21 = Low[0] <= ema21[0] + PullbackTolerance
                             || Low[1] <= ema21[1] + PullbackTolerance;
            bool reclaimed    = Close[0] > ema21[0];
            bool macdLongOk   = macd.Diff[0] > 0;
            bool rsiLongOk    = rsiVal >= 40 && rsiVal <= 65;

            if (Position.MarketPosition == MarketPosition.Flat
                && uptrend && touchedEma21 && reclaimed && macdLongOk && rsiLongOk)
            {
                EnterLong(DefaultQuantity, "PullbackLong");
                SetProfitTarget("PullbackLong", CalculationMode.Ticks, ProfitTicks);
                SetStopLoss("PullbackLong", CalculationMode.Ticks, StopTicks, false);
                lastEntryBar = CurrentBar;
            }

            // ── SHORT PULLBACK ──
            if (!EnableShorts) return;

            bool downtrend       = ema13[0] < ema21[0] && ema21[0] < ema50[0];
            bool touchedEma21Dn  = High[0] >= ema21[0] - PullbackTolerance
                                || High[1] >= ema21[1] - PullbackTolerance;
            bool reclaimedDn     = Close[0] < ema21[0];
            bool macdShortOk     = macd.Diff[0] < 0;
            bool rsiShortOk      = rsiVal >= 35 && rsiVal <= 60;

            if (Position.MarketPosition == MarketPosition.Flat
                && downtrend && touchedEma21Dn && reclaimedDn && macdShortOk && rsiShortOk)
            {
                EnterShort(DefaultQuantity, "PullbackShort");
                SetProfitTarget("PullbackShort", CalculationMode.Ticks, ProfitTicks);
                SetStopLoss("PullbackShort", CalculationMode.Ticks, StopTicks, false);
                lastEntryBar = CurrentBar;
            }
        }
    }
}
