#!/usr/bin/env python3
"""
Download NQ/MNQ historical OHLCV data from 508Bottrader/jsonl
and convert into JSONL fine-tuning entries.
"""

import json
import urllib.request
import math
import random

SYSTEM_PROMPT = (
    "You are an expert in Pine Script v6 for TradingView and NinjaScript for NinjaTrader 8 (NT8), "
    "proficient in creating high-quality, accurate, and optimized scripts for technical analysis "
    "and trading strategies.\n\n"
    "Your primary tasks are:\n"
    "\u2022 Converting summarized trading strategies into precise Pine Script v6 and NinjaScript code.\n"
    "\u2022 Troubleshooting and optimizing existing scripts for both TradingView and NinjaTrader 8.\n\n"
    "Interaction Workflow:\n"
    "1. Read and fully understand the provided summary or existing code of the trading strategy.\n"
    "2. Clearly reiterate or summarize the strategy back to the user for confirmation.\n"
    "3. Explicitly request confirmation or clarification from the user to ensure accuracy.\n"
    "4. Address any errors, omissions, or ambiguities identified by the user before proceeding.\n"
    "5. Only after receiving user confirmation, write accurate Pine Script v6 and NinjaScript code.\n\n"
    "Key Principles:\n"
    "\u2022 Provide precise, concise, and technically accurate Pine Script and NinjaScript examples.\n"
    "\u2022 Follow functional and declarative programming paradigms to minimize complexity.\n"
    "\u2022 Prioritize readability and maintainability; avoid unnecessary nesting or complexity.\n"
    "\u2022 Use meaningful variable names with descriptive intent."
)

# All NQ/MNQ files in the repo
FILES = [
    ("NQ", "03-19"), ("NQ", "03-20"), ("NQ", "03-21"), ("NQ", "03-22"),
    ("NQ", "03-24"), ("NQ", "03-25"), ("NQ", "03-26"),
    ("NQ", "06-19"), ("NQ", "06-20"), ("NQ", "06-21"), ("NQ", "06-22"),
    ("NQ", "06-23"), ("NQ", "06-24"), ("NQ", "06-25"),
    ("NQ", "09-19"), ("NQ", "09-20"), ("NQ", "09-21"), ("NQ", "09-22"),
    ("NQ", "09-23"), ("NQ", "09-24"), ("NQ", "09-25"),
    ("NQ", "12-19"), ("NQ", "12-20"), ("NQ", "12-21"), ("NQ", "12-22"),
    ("NQ", "12-23"), ("NQ", "12-24"), ("NQ", "12-25"),
    ("MNQ", "06-25"),
]

BASE_URL = "https://raw.githubusercontent.com/508Bottrader/jsonl/main/"


def fetch_file(symbol, contract):
    filename = f"{symbol} {contract}.Last.txt"
    url = BASE_URL + urllib.request.quote(filename)
    try:
        with urllib.request.urlopen(url, timeout=30) as r:
            return r.read().decode("utf-8")
    except Exception as e:
        print(f"  SKIP {filename}: {e}")
        return None


def parse_bars(raw):
    bars = []
    for line in raw.strip().splitlines():
        line = line.strip()
        if not line:
            continue
        parts = line.split(";")
        if len(parts) < 6:
            continue
        try:
            dt, o, h, l, c, v = parts[0], float(parts[1]), float(parts[2]), float(parts[3]), float(parts[4]), int(parts[5])
            bars.append({"dt": dt, "o": o, "h": h, "l": l, "c": c, "v": v})
        except ValueError:
            continue
    return bars


def calc_ema(bars, period):
    ema = []
    k = 2.0 / (period + 1)
    for i, b in enumerate(bars):
        if i == 0:
            ema.append(b["c"])
        else:
            ema.append(b["c"] * k + ema[-1] * (1 - k))
    return ema


def calc_sma(values, period):
    result = []
    for i in range(len(values)):
        if i < period - 1:
            result.append(None)
        else:
            result.append(sum(values[i - period + 1:i + 1]) / period)
    return result


def calc_rsi(bars, period=14):
    rsi = []
    gains = []
    losses = []
    for i in range(len(bars)):
        if i == 0:
            gains.append(0)
            losses.append(0)
            rsi.append(50.0)
            continue
        diff = bars[i]["c"] - bars[i - 1]["c"]
        gains.append(max(diff, 0))
        losses.append(max(-diff, 0))
        if i < period:
            rsi.append(50.0)
        elif i == period:
            avg_g = sum(gains[1:period + 1]) / period
            avg_l = sum(losses[1:period + 1]) / period
            if avg_l == 0:
                rsi.append(100.0)
            else:
                rs = avg_g / avg_l
                rsi.append(100 - 100 / (1 + rs))
        else:
            prev_avg_g = (rsi[-1] if rsi[-1] != 50 else 50)
            # Use Wilder smoothing from stored values - simplified
            avg_g = (gains[-2] * (period - 1) + gains[-1]) / period if len(gains) > period else sum(gains[-period:]) / period
            avg_l = (losses[-2] * (period - 1) + losses[-1]) / period if len(losses) > period else sum(losses[-period:]) / period
            if avg_l == 0:
                rsi.append(100.0)
            else:
                rs = avg_g / avg_l
                rsi.append(100 - 100 / (1 + rs))
    return rsi


def format_table(bars, n=10):
    lines = ["DateTime            | Open      | High      | Low       | Close     | Volume"]
    lines.append("-" * 75)
    for b in bars[:n]:
        lines.append(
            f"{b['dt']:<20}| {b['o']:<10.2f}| {b['h']:<10.2f}| {b['l']:<10.2f}| {b['c']:<10.2f}| {b['v']}"
        )
    return "\n".join(lines)


def session_summary(bars):
    if not bars:
        return {}
    highs = [b["h"] for b in bars]
    lows = [b["l"] for b in bars]
    vols = [b["v"] for b in bars]
    return {
        "open": bars[0]["o"],
        "high": max(highs),
        "low": min(lows),
        "close": bars[-1]["c"],
        "volume": sum(vols),
        "range": max(highs) - min(lows),
        "bars": len(bars),
    }


# ── Entry generators ─────────────────────────────────────────────────────────

def entry_describe_data(symbol, contract, bars):
    sample = bars[:20]
    table = format_table(sample, 20)
    first_dt = bars[0]["dt"]
    last_dt = bars[-1]["dt"]
    ss = session_summary(bars)
    user = (
        f"I have {len(bars)} one-minute OHLCV bars for {symbol} {contract} "
        f"from {first_dt} to {last_dt}. Here are the first 20 bars:\n\n"
        f"```\n{table}\n```\n\n"
        f"Can you describe what this data represents and how NinjaScript reads it?"
    )
    assistant = (
        f"This is **{symbol} {contract}** one-minute bar data exported from NinjaTrader 8 "
        f"in its standard semicolon-delimited format. The dataset covers {len(bars)} bars "
        f"from `{first_dt}` to `{last_dt}`.\n\n"
        f"**Session statistics**\n"
        f"- Open: {ss['open']:.2f}  |  High: {ss['high']:.2f}  |  Low: {ss['low']:.2f}  |  Close: {ss['close']:.2f}\n"
        f"- Total volume: {ss['volume']:,}  |  Range: {ss['range']:.2f} points  |  Bars: {ss['bars']:,}\n\n"
        f"**Column layout** (semicolon delimiter)\n"
        f"```\nYYYYMMDD HHMMSS ; Open ; High ; Low ; Close ; Volume\n```\n\n"
        f"**How NinjaScript reads this data**\n"
        f"When you add {symbol} as an instrument in a NinjaScript `Strategy` or `Indicator`, "
        f"NT8 exposes each column through built-in series:\n"
        f"```csharp\nOpen[0]   // current bar open\nHigh[0]   // current bar high\nLow[0]    // current bar low\nClose[0]  // current bar close (most recent tick price)\nVolume[0] // current bar volume\nTime[0]   // bar timestamp (DateTime)\n```\n"
        f"Index `[0]` is the current bar, `[1]` is one bar back, etc.\n\n"
        f"**Pine Script v6 equivalent**\n"
        f"```pine\n//@version=6\nindicator('{symbol} data reader', overlay=true)\n// Built-in series — same concept\nplot(close, title='Close')\nplot(volume, title='Volume')\n```\n"
        f"TradingView uses `open`, `high`, `low`, `close`, `volume` as global series, "
        f"indexed implicitly by the current bar."
    )
    return make_entry(user, assistant)


def entry_ema_calculation(symbol, contract, bars):
    period = random.choice([9, 14, 21, 50])
    ema_vals = calc_ema(bars, period)
    # Pick 5 bars near the middle
    mid = max(period, len(bars) // 2)
    sample_bars = bars[mid:mid + 5]
    sample_ema = ema_vals[mid:mid + 5]

    table_lines = ["DateTime            | Close     | EMA(" + str(period) + ")"]
    table_lines.append("-" * 45)
    for b, e in zip(sample_bars, sample_ema):
        table_lines.append(f"{b['dt']:<20}| {b['c']:<10.2f}| {e:.4f}")
    table = "\n".join(table_lines)

    user = (
        f"Using {symbol} {contract} one-minute data, calculate EMA({period}) "
        f"and show how it looks on a few bars starting around bar {mid}. "
        f"Then write NinjaScript and Pine Script code to plot it."
    )
    assistant = (
        f"**EMA({period}) on {symbol} {contract} — sample bars**\n\n"
        f"```\n{table}\n```\n"
        f"Formula: `EMA[0] = Close[0] \u00d7 \u03b1 + EMA[1] \u00d7 (1 \u2212 \u03b1)`  where `\u03b1 = 2 / ({period} + 1) = {2/(period+1):.4f}`\n\n"
        f"**NinjaScript**\n"
        f"```csharp\npublic class EMA{period} : Indicator\n{{\n"
        f"    private double alpha, beta;\n\n"
        f"    protected override void OnStateChange()\n"
        f"    {{\n"
        f"        if (State == State.SetDefaults)\n"
        f"        {{\n"
        f"            Name      = \"EMA{period}\";\n"
        f"            IsOverlay = true;\n"
        f"            AddPlot(Brushes.DodgerBlue, \"EMA{period}\");\n"
        f"        }}\n"
        f"        else if (State == State.Configure)\n"
        f"        {{\n"
        f"            alpha = 2.0 / ({period} + 1);   // {2/(period+1):.6f}\n"
        f"            beta  = 1 - alpha;\n"
        f"        }}\n"
        f"    }}\n\n"
        f"    protected override void OnBarUpdate()\n"
        f"    {{\n"
        f"        Value[0] = CurrentBar == 0\n"
        f"            ? Input[0]\n"
        f"            : Input[0] * alpha + Value[1] * beta;\n"
        f"    }}\n"
        f"}}\n```\n\n"
        f"**Pine Script v6**\n"
        f"```pine\n//@version=6\nindicator('EMA {period}', overlay=true)\nplot(ta.ema(close, {period}), color=color.blue, title='EMA {period}')\n```"
    )
    return make_entry(user, assistant)


def entry_rsi_question(symbol, contract, bars):
    period = 14
    rsi_vals = calc_rsi(bars, period)
    # Find overbought/oversold bars
    ob_bars = [(bars[i], rsi_vals[i]) for i in range(period, len(bars)) if rsi_vals[i] >= 70]
    os_bars = [(bars[i], rsi_vals[i]) for i in range(period, len(bars)) if rsi_vals[i] <= 30]
    ob_count = len(ob_bars)
    os_count = len(os_bars)

    user = (
        f"On {symbol} {contract} one-minute data ({len(bars)} bars total), "
        f"I want to find RSI({period}) overbought (>70) and oversold (<30) signals. "
        f"How many such bars are there, and how do I code this alert in NinjaScript?"
    )
    assistant = (
        f"**RSI({period}) signal count on {symbol} {contract}**\n\n"
        f"- Overbought (\u2265 70): **{ob_count} bars**\n"
        f"- Oversold (\u2264 30): **{os_count} bars**\n\n"
        f"**NinjaScript alert code**\n"
        f"```csharp\nprotected override void OnBarUpdate()\n{{\n"
        f"    if (CurrentBar < {period}) return;\n\n"
        f"    double rsi = RSI(Close, {period}, 3)[0];\n\n"
        f"    if (rsi >= 70 && CrossAbove(RSI(Close, {period}, 3), 70, 1))\n"
        f"        Alert(\"OB\", Priority.High, \"RSI overbought on \" + Instrument.FullName,\n"
        f"              NinjaTrader.NinjaScript.AlertWindow.SoundFileName, 10, Brushes.Red, Brushes.White);\n\n"
        f"    if (rsi <= 30 && CrossBelow(RSI(Close, {period}, 3), 30, 1))\n"
        f"        Alert(\"OS\", Priority.High, \"RSI oversold on \" + Instrument.FullName,\n"
        f"              NinjaTrader.NinjaScript.AlertWindow.SoundFileName, 10, Brushes.Green, Brushes.White);\n"
        f"}}\n```\n\n"
        f"**Pine Script v6 equivalent**\n"
        f"```pine\n//@version=6\nstrategy('RSI Signals', overlay=false)\nrsiVal = ta.rsi(close, {period})\nplot(rsiVal, 'RSI', color=color.purple)\nhline(70, 'OB', color=color.red)\nhline(30, 'OS', color=color.green)\nalertcondition(ta.crossover(rsiVal, 70),  title='OB cross', message='RSI overbought')\nalertcondition(ta.crossunder(rsiVal, 30), title='OS cross', message='RSI oversold')\n```"
    )
    return make_entry(user, assistant)


def entry_session_stats(symbol, contract, bars):
    # Group by date
    days = {}
    for b in bars:
        day = b["dt"][:8]
        days.setdefault(day, []).append(b)

    day_summaries = []
    for day, dbars in sorted(days.items())[:5]:
        ss = session_summary(dbars)
        day_summaries.append((day, ss))

    table_lines = ["Date     | Open      | High      | Low       | Close     | Range    | Volume"]
    table_lines.append("-" * 75)
    for day, ss in day_summaries:
        table_lines.append(
            f"{day}  | {ss['open']:<10.2f}| {ss['high']:<10.2f}| {ss['low']:<10.2f}"
            f"| {ss['close']:<10.2f}| {ss['range']:<9.2f}| {ss['volume']:,}"
        )
    table = "\n".join(table_lines)

    user = (
        f"For {symbol} {contract}, calculate daily session stats (open, high, low, close, range, volume) "
        f"across the first 5 trading days. Then write NinjaScript to compute these same stats in real time."
    )
    assistant = (
        f"**{symbol} {contract} — first 5 trading days**\n\n"
        f"```\n{table}\n```\n\n"
        f"**NinjaScript — real-time daily stats**\n"
        f"```csharp\npublic class DailyStats : Indicator\n{{\n"
        f"    private double dayOpen, dayHigh, dayLow;\n\n"
        f"    protected override void OnStateChange()\n"
        f"    {{\n"
        f"        if (State == State.SetDefaults)\n"
        f"        {{\n"
        f"            Name      = \"DailyStats\";\n"
        f"            IsOverlay = true;\n"
        f"            AddPlot(Brushes.Blue,  \"DayHigh\");\n"
        f"            AddPlot(Brushes.Red,   \"DayLow\");\n"
        f"            AddPlot(Brushes.White, \"DayOpen\");\n"
        f"        }}\n"
        f"        Calculate = Calculate.OnBarClose;\n"
        f"    }}\n\n"
        f"    protected override void OnBarUpdate()\n"
        f"    {{\n"
        f"        if (Bars.IsFirstBarOfSession)\n"
        f"        {{\n"
        f"            dayOpen = Open[0];\n"
        f"            dayHigh = High[0];\n"
        f"            dayLow  = Low[0];\n"
        f"        }}\n"
        f"        else\n"
        f"        {{\n"
        f"            dayHigh = Math.Max(dayHigh, High[0]);\n"
        f"            dayLow  = Math.Min(dayLow,  Low[0]);\n"
        f"        }}\n"
        f"        Values[0][0] = dayHigh;\n"
        f"        Values[1][0] = dayLow;\n"
        f"        Values[2][0] = dayOpen;\n"
        f"    }}\n"
        f"}}\n```\n\n"
        f"**Pine Script v6**\n"
        f"```pine\n//@version=6\nindicator('Daily Stats', overlay=true)\nvar float dayHigh = na\nvar float dayLow  = na\nvar float dayOpen = na\nif timeframe.change('D') or na(dayOpen)\n    dayOpen := open\n    dayHigh := high\n    dayLow  := low\nelse\n    dayHigh := math.max(dayHigh, high)\n    dayLow  := math.min(dayLow, low)\nplot(dayHigh, 'Day High', color=color.blue,  linewidth=2)\nplot(dayLow,  'Day Low',  color=color.red,   linewidth=2)\nplot(dayOpen, 'Day Open', color=color.white, linewidth=1)\n```"
    )
    return make_entry(user, assistant)


def entry_atr(symbol, contract, bars):
    period = 14
    tr_vals = []
    for i in range(len(bars)):
        if i == 0:
            tr_vals.append(bars[i]["h"] - bars[i]["l"])
        else:
            tr = max(
                bars[i]["h"] - bars[i]["l"],
                abs(bars[i]["h"] - bars[i - 1]["c"]),
                abs(bars[i]["l"] - bars[i - 1]["c"]),
            )
            tr_vals.append(tr)
    atr_vals = []
    for i in range(len(tr_vals)):
        if i < period:
            atr_vals.append(sum(tr_vals[:i + 1]) / (i + 1))
        else:
            atr_vals.append((atr_vals[-1] * (period - 1) + tr_vals[i]) / period)

    last_atr = atr_vals[-1]
    last_close = bars[-1]["c"]

    user = (
        f"Calculate ATR({period}) for {symbol} {contract} using Wilder's smoothing. "
        f"The last close was {last_close:.2f}. "
        f"What is the current ATR, and how do I use it in NinjaScript for position sizing?"
    )
    assistant = (
        f"**ATR({period}) for {symbol} {contract}**\n\n"
        f"Using Wilder's smoothing: `ATR[0] = (ATR[1] \u00d7 ({period}\u22121) + TR[0]) / {period}`\n\n"
        f"- Last Close: **{last_close:.2f}**\n"
        f"- Current ATR({period}): **{last_atr:.4f} points**\n"
        f"- ATR as % of price: **{last_atr/last_close*100:.3f}%**\n\n"
        f"**NinjaScript — ATR-based position sizing**\n"
        f"```csharp\nprotected override void OnBarUpdate()\n{{\n"
        f"    if (CurrentBar < {period}) return;\n\n"
        f"    double atr      = ATR(Close, {period})[0];  // current ATR\n"
        f"    double riskPts  = atr * 2;                  // 2-ATR stop distance\n"
        f"    double riskAmt  = 500;                       // $ risk per trade\n"
        f"    double tickVal  = Instrument.MasterInstrument.PointValue\n"
        f"                      * TickSize;               // $ per tick\n"
        f"    double ticksRisk = riskPts / TickSize;\n"
        f"    int    qty      = (int)(riskAmt / (ticksRisk * tickVal));\n"
        f"    qty = Math.Max(1, qty);\n\n"
        f"    if (CrossAbove(Close, EMA(Close, 20), 1))\n"
        f"        EnterLong(qty, \"ATR Long\");\n"
        f"}}\n```\n\n"
        f"**Pine Script v6**\n"
        f"```pine\n//@version=6\nstrategy('ATR Sizing', overlay=true)\natrVal  = ta.atr({period})\nriskPts = atrVal * 2\n// TradingView uses contracts; sizing shown as reference\nqty     = math.round(500 / (riskPts * syminfo.pointvalue))\nif ta.crossover(close, ta.ema(close, 20))\n    strategy.entry('Long', strategy.long, qty=math.max(1, qty))\n```"
    )
    return make_entry(user, assistant)


def make_entry(user, assistant):
    return {
        "messages": [
            {"role": "system", "content": SYSTEM_PROMPT},
            {"role": "user", "content": user},
            {"role": "assistant", "content": assistant},
        ]
    }


def main():
    random.seed(42)
    out_path = "/home/user/main/nq_mnq_historical.jsonl"
    entries = []

    for symbol, contract in FILES:
        print(f"Processing {symbol} {contract} ...", end=" ", flush=True)
        raw = fetch_file(symbol, contract)
        if raw is None:
            continue
        bars = parse_bars(raw)
        if len(bars) < 30:
            print(f"too few bars ({len(bars)}), skipping")
            continue
        print(f"{len(bars)} bars")

        entries.append(entry_describe_data(symbol, contract, bars))
        entries.append(entry_ema_calculation(symbol, contract, bars))
        entries.append(entry_rsi_question(symbol, contract, bars))
        entries.append(entry_session_stats(symbol, contract, bars))
        entries.append(entry_atr(symbol, contract, bars))

    print(f"\nWriting {len(entries)} entries to {out_path}")
    with open(out_path, "w", encoding="utf-8") as f:
        for e in entries:
            f.write(json.dumps(e, ensure_ascii=False) + "\n")

    print("Done.")


if __name__ == "__main__":
    main()
