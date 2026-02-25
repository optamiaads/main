# CLAUDE.md — AI Assistant Guide

This file documents the structure, conventions, and workflows for AI assistants (Claude, Codex, ChatGPT, etc.) working in this repository.

---

## Repository Purpose

This is an **AI-assisted trading strategy development workspace** focused on:

1. **NinjaScript (NinjaTrader 8)** — C#-based indicators and automated trading strategies for the NT8 platform.
2. **Pine Script v6 (TradingView)** — Indicator and strategy scripts for TradingView charts.
3. **LLM Fine-Tuning Data** — Curated JSONL conversation datasets for training/fine-tuning language models on trading strategy code generation and debugging.

The repository is designed as a collaborative workspace between human traders and LLM tools to develop, debug, and optimize trading strategies.

---

## Repository Structure

```
/
├── *.txt                              # NinjaScript C# source files (indicator & strategy code)
│   ├── ADX.txt                        # Average Directional Index indicator
│   ├── DM.txt                         # Directional Movement indicator
│   ├── DMI.txt                        # Directional Movement Index indicator
│   ├── EMA.txt                        # Exponential Moving Average indicator
│   ├── MACD.txt                       # MACD indicator
│   ├── RSI.txt                        # Relative Strength Index indicator
│   ├── SMA.txt                        # Simple Moving Average indicator
│   ├── VMA.txt                        # Variable Moving Average indicator
│   ├── VOL.txt                        # Volume indicator
│   ├── SampleAtmStrategy .txt         # ATM strategy usage example
│   ├── SampleMACrossOver .txt         # Moving average crossover strategy example
│   ├── SampleMultiInstrument .txt     # Multi-instrument strategy example
│   ├── SampleMultiTimeFrame.txt       # Multi-timeframe strategy example
│   └── VolumeColorVsSMA .txt          # Volume vs SMA coloring strategy
├── cyborg_master_strategy_modified (2).cs  # Custom CyborgMasterStrategy (trend + mean-reversion)
├── training_clean.jsonl               # 54 LLM fine-tuning examples (training set)
├── validation_clean.jsonl             # 6 LLM fine-tuning examples (validation set)
├── merged_20.jsonl                    # 20 merged conversation examples
├── merged_20_valid.jsonl              # 20 merged validation examples
├── merged_20_valid_v2.jsonl           # 20 merged validation examples (v2)
└── README.md                          # Project overview and LLM collaboration guide
```

> **Note:** Files with `.txt` extension contain full C# NinjaScript source code — they are plain-text code files, not documentation.

---

## File Conventions

### NinjaScript Source Files (`.txt` and `.cs`)

- **Language:** C# targeting .NET Framework, compiled and executed inside NinjaTrader 8.
- **Namespace:** All indicators live in `NinjaTrader.NinjaScript.Indicators`. All strategies live in `NinjaTrader.NinjaScript.Strategies`. **Never change these namespaces.**
- **Standard `using` block:** Every NinjaScript file includes a `#region Using declarations` block at the top with the full set of standard NT8 `using` statements.
- **`OnStateChange()`:** The main lifecycle method. Handle `State.SetDefaults`, `State.Configure`, and `State.DataLoaded` stages.
- **`OnBarUpdate()`:** The per-bar calculation method. Always guard with `if (CurrentBar < BarsRequiredToTrade) return;` or equivalent.
- **Properties:** Exposed parameters use `[NinjaScriptProperty]` and `[Range(...)]` attributes with `[Display(...)]` for UI labeling.
- **Auto-generated region:** Files contain a `#region NinjaScript generated code. Neither change nor remove.` section at the bottom — do not modify this.
- **Indicator caching:** NT8 uses a caching pattern for indicators accessed by strategies. The auto-generated region handles this.
- **`Series<double>`:** Internal data series are declared as fields and initialized in `State.DataLoaded`.

### Strategy-Specific Patterns

- **ATM Strategies:** Use `AtmStrategyCreate()` with a callback lambda for async order handling. Track state with `atmStrategyId`, `orderId`, and an `isAtmActive` boolean.
- **Multi-series:** Add secondary bar series in `State.Configure` with `AddDataSeries(...)`. Access via `BarsArray[1]`, `BarsArray[2]`, etc. Guard with `if (BarsInProgress != 0) return;` to process only the primary series.
- **Realtime-only logic:** Use `if (State != State.Realtime) return;` to prevent historical backfill from triggering live orders.
- **Cooldown pattern:** Track `longEntryBar` / `shortEntryBar` = `CurrentBar` on entry; gate re-entry with bar-count comparisons.
- **Logging:** Use `Print(...)` for debug output to the NT8 Output window. Use interpolated strings (`$"..."`) for formatting.

### JSONL Fine-Tuning Data

- **Format:** OpenAI chat completions format — each line is a JSON object with a single `"messages"` key.
- **Message roles:** `system`, `user`, `assistant`.
- **System prompt:** Each example uses a consistent expert system prompt that defines the AI as a Pine Script v6 / NinjaScript NT8 expert with a multi-step interaction workflow.
- **Content:** Conversations cover NT8 documentation explanations, strategy code generation, debugging, and Pine Script conversion.
- **File naming convention:**
  - `training_clean.jsonl` — primary training set (54 examples)
  - `validation_clean.jsonl` — primary validation set (6 examples)
  - `merged_20*.jsonl` — smaller 20-example merged datasets for experimentation

---

## Key Technical Concepts

### Indicators Used in Strategies

| Indicator | Default Period | Access Pattern |
|-----------|---------------|----------------|
| EMA | 21, 50, 200 | `EMA(BarsArray[1], period)` |
| MACD | Fast=5, Slow=13, Smooth=6 | `MACD(BarsArray[1], 5, 13, 6)` |
| ADX | 14 | `ADX(BarsArray[1], 14)` |
| RSI | 14 | `RSI(BarsArray[1], 14, 1)` |
| DM | 14 | `DM(BarsArray[1], 14)` — exposes `.DiPlus[0]` and `.DiMinus[0]` |
| ATR | 14 | `ATR(BarsArray[1], 14)` |
| SMA (Volume) | 21 | `SMA(Volume, 21)` |

### CyborgMasterStrategy Logic (cyborg_master_strategy_modified (2).cs)

The main custom strategy implements a **regime-adaptive** approach:

- **ADX >= 25 (trending regime):** Uses trend-following signals (EMA alignment, MACD > 0, DI+ > 25, price pushing).
- **ADX < 25 (ranging regime):** Uses mean-reversion signals (RSI crossing above 30 from oversold, or below 70 from overbought).
- Both regimes apply volume filter (`VolumeOkay`), ATR filter (`AtrOkay`, optional), and cooldown gates.
- Orders are placed via `AtmStrategyCreate()` using an ATM template named `"CyborgATMTemplate"`.

---

## Development Workflow

### Adding or Modifying NinjaScript

1. Edit the relevant `.txt` or `.cs` file in this repository.
2. Copy the source code into NinjaTrader 8 via **Tools > Edit NinjaScript** or the NinjaScript Editor.
3. Compile inside NT8 — errors appear in the NT8 Output window.
4. Test in **Strategy Analyzer** (backtesting) or **Simulation mode** before live deployment.
5. Do **not** run live/realtime strategies without thorough simulation testing.

### Working with Fine-Tuning Data

- Each JSONL record must be one valid JSON object per line (no pretty-printing across lines).
- Maintain the consistent system prompt across all records for training coherence.
- Add new examples to `training_clean.jsonl` or `validation_clean.jsonl`.
- Keep the train/validation split roughly 90/10.
- Validate JSONL with: `python3 -c "import json; [json.loads(l) for l in open('training_clean.jsonl')]"`

### NinjaScript Compilation Constraints

- NT8 uses a specific C# subset — avoid features unavailable in .NET Framework 4.8.
- `dynamic`, some LINQ expressions, and async/await are restricted or unavailable in NinjaScript.
- Never use `Thread.Sleep()` or blocking calls inside `OnBarUpdate()`.
- `Print()` is the only safe logging mechanism during bar updates.

---

## Code Quality Standards

- **No unnecessary complexity:** Keep indicator logic flat and readable; avoid deep nesting.
- **Variable naming:** Use descriptive names (`ema21_1` = EMA 21-period on BarsArray[1]; `dm14_1` = DM 14-period on BarsArray[1]).
- **Guard clauses first:** Place all early-return guards at the top of `OnBarUpdate()`.
- **Separation of concerns:** Extract entry condition logic into private boolean methods (e.g., `TrendUp()`, `TrendDown()`, `VolumeOkay()`).
- **No magic numbers:** Define thresholds as named constants or properties (e.g., `private const double AdxThreshold = 25.0`).
- **ATM lifecycle:** Always reset `atmStrategyId`, `orderId`, and `isAtmActive` together when a position closes.

---

## Important Warnings

- **For educational and simulation purposes only.** All strategy code in this repository is intended for learning and backtesting, not live trading without independent verification.
- **Do not modify the auto-generated `#region NinjaScript generated code` blocks** — they are regenerated by NT8.
- **Do not change the namespace declarations** — NT8 requires `NinjaTrader.NinjaScript.Indicators` and `NinjaTrader.NinjaScript.Strategies`.
- **The `.txt` files are code, not documentation** — treat them as C# source files when editing.
- **ATM templates must exist in NT8** — `AtmStrategyCreate()` calls reference templates by name (e.g., `"CyborgATMTemplate"`, `"AtmStrategyTemplate"`); these must be created in the NT8 platform independently.

---

## References

- [NinjaTrader 8 Help Guide](https://ninjatrader.com/support/helpGuides/nt8/)
- [NinjaTrader Developer Docs](https://developer.ninjatrader.com/docs/desktop/)
- [TradingView Pine Script v6 Docs](https://www.tradingview.com/pine-script-docs/welcome)
- [OpenAI Fine-tuning Format](https://platform.openai.com/docs/guides/fine-tuning)
