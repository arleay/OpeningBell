# Prop firms, accounts and work (PROP_SPEC)

Extends `PROJECT_SPEC.md` (§6 computer, economy) and `WORLD_SPEC.md`. Goal: trading as it really works for most
retail futures traders today. You don't trade your own savings; you pay a prop firm for an evaluation, pass it,
trade the firm's funded account and take payouts under the firm's rules. And you start broke, with a job.

Names are fictional (WORLD_SPEC §94: no real brands). **Rules are copied from real firms** (researched Sept 2026):
Ridgeback Funding follows Topstep's model, Profit Harbor follows Take Profit Trader's.

---

## 1. Principles

- Rules are data (`PropProgram`), enforced by one engine (`PropRules`). Pure C# in `OpeningBell.Trading`
  (`noEngineReferences`), money in `decimal`, time from the market clock, "days" are trading days.
- Every prop account is a real `Account` + `OrderManager` pair on the same market. Fills, commissions, margin,
  brackets and the chart work exactly as they do now.
- What the player pays (evaluations, activation, resets) comes out of the **bank**. Payouts go into the **bank**.
- The personal brokerage (PennyBridge, your own money) stays, starting at $0.

## 2. The firms

### 2.1 Ridgeback Funding (`ridgebackfunding.com`, Topstep model)

**Trading Challenge** (evaluation), billed monthly until passed or cancelled.

| Size | Price/mo | Profit target | Max Loss Limit (EOD trailing) | Max contracts |
|---|---|---|---|---|
| 50K | $49 | $3,000 | $2,000 | 5 |
| 100K | $99 | $6,000 | $3,000 | 10 |
| 150K | $149 | $9,000 | $4,500 | 15 |

- Consistency: the best day may be at most **50%** of total profit when the target is reached.
- Minimum **2** trading days. No daily loss limit.
- Pass → **Express Funded Account**, activation **$149**.

**Express Funded Account**
- Same Max Loss Limit, EOD trailing, locks at the starting balance.
- Scaling plan (contracts allowed by profit): 50K: 2 until +$1,500, 3 until +$2,000, then 5.
  100K: 3 / 6 / 10 at +$1,500 / +$3,000. 150K: 3 / 6 / 10 / 15 at +$1,500 / +$3,000 / +$4,500.
- Payouts: after **5 winning days of $150+** since the last payout. Up to **50%** of the profit balance,
  cap **$5,000** per request. Split **90/10**. Paid to the bank the next weekday. Winning days reset.
- Up to 5 funded accounts at once.

### 2.2 Profit Harbor (`profitharbor.com`, Take Profit Trader model)

**Test** (evaluation), billed monthly.

| Size | Price/mo | Profit target (6%) | Drawdown (EOD trailing) | Max contracts |
|---|---|---|---|---|
| 25K | $150 | $1,500 | $1,500 | 3 |
| 50K | $170 | $3,000 | $2,000 | 6 |
| 75K | $245 | $4,500 | $2,500 | 9 |
| 100K | $330 | $6,000 | $3,000 | 12 |
| 150K | $360 | $9,000 | $4,500 | 15 |

- Consistency **50%**, minimum **5** trading days, no daily loss limit. Reset **$99**.
- Pass → **PRO**, activation **$130**.

**PRO** (daily payouts)
- Drawdown becomes **intraday trailing**: the high-water mark includes open profit, updated every tick. Locks at
  the starting balance.
- Payouts **any day**, once the balance has cleared the **buffer** (start + drawdown, e.g. $52,000 on a 50K).
  Only profit above the buffer can be withdrawn. Split **80/20**. $50 fee on requests of $250 or less.
- Must trade at least one day per calendar week.

### 2.3 Rules common to both

- **Drawdown breach** (equity at or below the threshold, checked every tick): positions flattened, orders
  cancelled, account failed. Evaluations can be reset for the fee; funded accounts are closed.
- **EOD trailing**: threshold = highest end-of-day balance − drawdown, never above the starting balance.
- **Max contracts**: an order that would take the net position past the limit is rejected.
- **Flat by 15:55**: open positions are closed and new orders refused until the next session. No overnight holds.
- A **trading day** counts when the account has at least one fill that day.
- Commissions: $5 round trip per contract (the current broker schedule).
- Account IDs look like the real thing: `profitharborpro2015151`, `profitharbortest2015152`, `RB50K-448210`.

## 3. Terminal: accounts and copy trading

- The trading app's top bar shows the **active account**: ID, firm, phase (Challenge / Funded / Test / PRO),
  balance, distance to the drawdown, and status.
- A **dropdown** lists every account (personal + prop). Click one to make it active.
- Each account in the dropdown has a **check box**. Checked accounts are **followers**: any order placed on the
  active account is mirrored 1:1 to every checked, tradeable follower (group trading). Cancels, price changes
  and position closes are mirrored too. A follower's own rules still apply; a refused copy is noted.

## 4. Firm websites

Each firm's site in the browser: plans and rules table, **buy** (from the bank, confirm), **dashboard** per
account (target progress, drawdown threshold and distance, trading days, consistency, winning days / buffer,
payout eligibility), and actions: **activate funded**, **request payout**, **reset**, **cancel subscription**.

## 5. Starting broke and working

- New game: personal brokerage $0, bank $0. Existing saves keep their money.
- **Sal's Pizza** (existing unit) hires you: talk to Sal. Clock in at the time clock during opening hours,
  clock out there (or by leaving).
- Job: counter server. Customers come in and order at the counter ("Two slices, pepperoni"). Take the order
  off the warmer, bring it to the counter, [E] to hand it over, then ring it up on the register. Customers have
  patience; slow service costs the tip, wrong items get sent back.
- Pay **$15/h** plus tips (more when fast), paid into the bank at clock-out with a shift summary.
- Rush hours at lunch and dinner.

## 6. Phases

- **PF1** Prop engine: programs, accounts, rule checks, EOD/intraday trailing, consistency, scaling,
  flat-by-close, pass/fail, payouts. EditMode tests.
- **PF2** Multi-account terminal: active account, dropdown, copy trading. Save/load of all accounts.
- **PF3** Firm websites: buy, subscriptions, activation, resets, payouts to the bank.
- **PF4** $0 start and the Sal's Pizza job.

Later (not now): Profit Harbor PRO+ (90/10, live), payout denials/reviews, news-trading restrictions.
