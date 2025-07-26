using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Indicators;

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// A modified version of the CyborgMasterStrategy that adapts its entries based on trend strength.
    /// When ADX is above a threshold (25 by default), it uses the existing trend‑following logic.
    /// When ADX is below the threshold, it switches to a simple mean‑reversion rule based on
    /// Stochastic RSI crosses out of extreme conditions. This example is for educational purposes only
    /// and should be tested thoroughly in simulation before use.
    /// </summary>
    public class CyborgMasterStrategyModified : Strategy
    {
        [NinjaScriptProperty]
        [Range(0.5, double.MaxValue)]
        public double VolumeMult { get; set; } = 1.0;

        [NinjaScriptProperty]
        public bool EnableShorts { get; set; } = false;

        [NinjaScriptProperty]
        [Range(1, 20)]
        public int CooldownSameDir { get; set; } = 4;

        [NinjaScriptProperty]
        [Range(1, 20)]
        public int CooldownOppDir { get; set; } = 6;

        [NinjaScriptProperty]
        [Range(10, 200)]
        public int MaxBarsInTrade { get; set; } = 50;

        [NinjaScriptProperty]
        public string AtmTemplateName { get; set; } = "CyborgATMTemplate";

        [NinjaScriptProperty]
        public bool EnableAtrFilter { get; set; } = false;

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        public double MinAtr { get; set; } = 0.2;

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        public double MaxAtr { get; set; } = 5.0;

        // ADX threshold used to determine whether to follow the trend or trade mean‑reversion
        private const double AdxThreshold = 25.0;

        private EMA ema21_1, ema50_1, ema200_1;
        private ATR atr14_1;
        private MACD macd_1;
        private DM dm14_1;
        private SMA volSMA21_0;

        // New indicators: ADX and RSI for regime determination and mean‑reversion
        private ADX adx14_1;
        private RSI rsi14_1;

        private int longEntryBar = -1;
        private int shortEntryBar = -1;

        private string atmStrategyId = string.Empty;
        private string orderId = string.Empty;
        private bool isAtmActive = false;

        protected override void OnStateChange()
        {
            switch (State)
            {
                case State.SetDefaults:
                    Name = "CyborgMasterStrategyModified";
                    Calculate = Calculate.OnPriceChange;
                    EntriesPerDirection = 1;
                    EntryHandling = EntryHandling.AllEntries;
                    DefaultQuantity = 1;
                    break;

                case State.Configure:
                    AddDataSeries(BarsPeriodType.Minute, 1);
                    break;

                case State.DataLoaded:
                    ema21_1 = EMA(BarsArray[1], 21);
                    ema50_1 = EMA(BarsArray[1], 50);
                    ema200_1 = EMA(BarsArray[1], 200);
                    atr14_1 = ATR(BarsArray[1], 14);
                    macd_1 = MACD(BarsArray[1], 5, 13, 6);
                    dm14_1 = DM(BarsArray[1], 14);
                    volSMA21_0 = SMA(Volume, 21);
                    // instantiate ADX and RSI on the secondary series
                    adx14_1 = ADX(BarsArray[1], 14);
                    // RSI is used for mean‑reversion instead of StochRSI to avoid casting issues.
                    rsi14_1 = RSI(BarsArray[1], 14, 1);
                    break;
            }
        }

        private bool TrendUp() =>
            ema21_1[0] > ema50_1[0] &&
            ema50_1[0] > ema200_1[0] &&
            macd_1[0] > 0 &&
            dm14_1.DiPlus[0] > 25 &&
            ema21_1[1] > ema50_1[1] &&
            macd_1[1] > 0 &&
            dm14_1.DiPlus[1] > 25;

        private bool TrendDown() =>
            ema21_1[0] < ema50_1[0] &&
            ema50_1[0] < ema200_1[0] &&
            macd_1[0] < 0 &&
            dm14_1.DiMinus[0] > 25 &&
            ema21_1[1] < ema50_1[1] &&
            macd_1[1] < 0 &&
            dm14_1.DiMinus[1] > 25;

        private bool VolumeOkay() =>
            Volume[0] > VolumeMult * volSMA21_0[0];

        private bool CooldownPassed(bool isLong)
        {
            int barsSince = CurrentBar - (isLong ? longEntryBar : shortEntryBar);
            return barsSince > (isLong ? CooldownSameDir : CooldownOppDir);
        }

        private bool AtrOkay()
        {
            if (!EnableAtrFilter)
                return true;

            double atrValue = atr14_1[0];
            return atrValue >= MinAtr && atrValue <= MaxAtr;
        }

        private bool IsPricePushingUp() =>
            Close[0] > Open[0] && High[0] > High[1];

        private bool IsPricePushingDown() =>
            Close[0] < Open[0] && Low[0] < Low[1];

        protected override void OnBarUpdate()
        {
            // Ensure we have enough data on both series and only evaluate on the primary series
            if (BarsInProgress != 0 || CurrentBar < 25 || CurrentBars[1] < 25)
                return;

            // Only process in real-time
            if (State != State.Realtime)
                return;

            // If an ATM strategy is active, poll its status and only continue once flat
            if (isAtmActive)
            {
                MarketPosition atmPos = GetAtmStrategyMarketPosition(atmStrategyId);
                int atmQuantity = GetAtmStrategyPositionQuantity(atmStrategyId);
                if (atmPos == MarketPosition.Flat && atmQuantity == 0)
                {
                    Print("ATM strategy has closed — resetting.");
                    atmStrategyId = string.Empty;
                    orderId = string.Empty;
                    isAtmActive = false;
                }
                else
                {
                    return;
                }
            }

            // Determine regime based on ADX value
            double adxValue = adx14_1[0];
            bool isTrending = adxValue >= AdxThreshold;

            bool longReady;
            bool shortReady;

            if (isTrending)
            {
                // Use existing trend-following logic
                longReady = TrendUp() && VolumeOkay() && AtrOkay() && IsPricePushingUp();
                shortReady = TrendDown() && VolumeOkay() && AtrOkay() && EnableShorts && IsPricePushingDown();
            }
            else
            {
                // Mean-reversion logic: use RSI crosses out of oversold/overbought regions
                // Oversold cross up: RSI crosses above 30 from below
                bool rsiCrossUp = rsi14_1[0] > 30 && rsi14_1[1] <= 30;
                // Overbought cross down: RSI crosses below 70 from above
                bool rsiCrossDown = rsi14_1[0] < 70 && rsi14_1[1] >= 70;
                longReady = rsiCrossUp && VolumeOkay() && AtrOkay();
                shortReady = rsiCrossDown && EnableShorts && VolumeOkay() && AtrOkay();
            }

            Print($"[{Time[0]}]\n" +
                  $"ADX={adxValue:F2} (Trending={(isTrending ? "Yes" : "No")})\n" +
                  $"TrendUp={TrendUp()}, TrendDown={TrendDown()}\n" +
                  $"VolumeOK={VolumeOkay()}, ATR_OK={AtrOkay()} ({atr14_1[0]:F2})\n" +
                  $"RSI={rsi14_1[0]:F2}\n" +
                  $"LongReady={longReady}, ShortReady={shortReady}\n" +
                  $"CooldownPassedLong={CooldownPassed(true)}\n" +
                  $"CooldownPassedShort={CooldownPassed(false)}\n" +
                  $"ATM Active={isAtmActive}");

            if (longReady && CooldownPassed(true))
            {
                atmStrategyId = GetAtmStrategyUniqueId();
                orderId = GetAtmStrategyUniqueId();

                AtmStrategyCreate(Cbi.OrderAction.Buy,
                                  Cbi.OrderType.Market,
                                  0, 0,
                                  TimeInForce.Day,
                                  orderId,
                                  AtmTemplateName,
                                  atmStrategyId,
                                  (errorCode, errorMsg) =>
                                  {
                                      Print($"ATM Buy Callback: {errorCode} | {errorMsg}");
                                      if (errorCode == ErrorCode.NoError)
                                      {
                                          isAtmActive = true;
                                          Print("ATM order submitted successfully. ATM Active=True");
                                      }
                                      else
                                      {
                                          Print("ATM order submission failed:");
                                          Print($"  ErrorCode: {errorCode}");
                                          Print($"  ErrorMessage: {errorMsg}");
                                          atmStrategyId = string.Empty;
                                          orderId = string.Empty;
                                          isAtmActive = false;
                                      }
                                  });

                longEntryBar = CurrentBar;
            }

            if (shortReady && CooldownPassed(false))
            {
                atmStrategyId = GetAtmStrategyUniqueId();
                orderId = GetAtmStrategyUniqueId();

                AtmStrategyCreate(Cbi.OrderAction.SellShort,
                                  Cbi.OrderType.Market,
                                  0, 0,
                                  TimeInForce.Day,
                                  orderId,
                                  AtmTemplateName,
                                  atmStrategyId,
                                  (errorCode, errorMsg) =>
                                  {
                                      Print($"ATM Sell Callback: {errorCode} | {errorMsg}");
                                      if (errorCode == ErrorCode.NoError)
                                      {
                                          isAtmActive = true;
                                          Print("ATM order submitted successfully. ATM Active=True");
                                      }
                                      else
                                      {
                                          Print("ATM order submission failed:");
                                          Print($"  ErrorCode: {errorCode}");
                                          Print($"  ErrorMessage: {errorMsg}");
                                          atmStrategyId = string.Empty;
                                          orderId = string.Empty;
                                          isAtmActive = false;
                                      }
                                  });

                shortEntryBar = CurrentBar;
            }
        }

        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            // This method does not typically fire for ATM-managed positions but is retained as a fallback
            if (isAtmActive && marketPosition == MarketPosition.Flat)
            {
                Print("ATM position confirmed flat — resetting.");
                atmStrategyId = string.Empty;
                orderId = string.Empty;
                isAtmActive = false;
                Print("ATM Active=False");
            }
        }

        protected override void OnExecutionUpdate(Cbi.Execution execution, string executionId, double price, int quantity, Cbi.MarketPosition marketPosition, string orderId, DateTime time)
        {
            // Optional: log fills here if needed
        }
    }
}