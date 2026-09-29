# AOBuddy20: review and decisions

Written 2026-09-29 from a review of AOBuddy20 at b7972e2 against BOTREWORK.md (the brains design, in AOBuddy10) and RESTRUCTURE.md. Each item gives the finding, Algorithman's answer, and where it stands. Rules: [rules.md](rules.md).

Why the rewrite exists: AOBuddy10's Main.cs (1664 lines) is a tangle of per-tick special cases. The rewrite is the clean-up, not a refactor of AOBuddy10. The goal is an async bot run by brains: packet routing, a priority gate, and profession brains.

## Decided

| # | Topic | Decision |
|---|---|---|
| 1 | Several accounts in one process | **One account per process** for now. The SDK's `Client` is static: CreateDomain writes Credentials, CharacterName, Dimension, Logger and the chat client into statics (ClientDomain.cs:38-52). An instance wrapper may come later. |
| 7 | Combat needing to move | **Combat never calls travel.** Movement is kept out of the ControlArbiter. When two movers want the body, the higher priority wins: its move replaces any unsent lower one. |
| 9 | Threading | **To do.** Async continuations must not touch SDK state from thread-pool threads. Run them on the update loop, or marshal onto it (AOBuddy10 hit this: RESTRUCTURE R0.1). |
| 10 | Router order | **Handlers get a sort order.** Also still to do from the design: an observer must get every packet even when an actor ends the sequence. |
| 11 | Handler signatures differ (N3 gets AOMessage, system gets SystemMessage, chat gets ChatMessage) | **Not a problem.** AOSharp does the same. |
| 12 | Router is not thread-safe | **Not a problem.** Registration finishes before the network session starts. The brain is set once right after login (or from config before connecting) and never changes. |
| 16 | Stale `.gitmodules` (AOtomation.Messaging) | **Remove the entry.** The messages already come in through AOSharp. |
| 20 | Attack rules | **Target choice belongs to the brains.** Example: a second, hard mob joins. Finish the easy one, or switch to the one that can kill you? The wire-proven part stays: never re-send Attack to the *same* target, because that restarts the swing timer. Switching targets is fine. |

## In progress / to do

| # | Topic | Status |
|---|---|---|
| 2 | BotLoop is never created or started (not in DI; LoadPlugin needs a parameterless ctor; `CurrentTask` is readonly `Nothing`) | Early stage, being wired |
| 3 | PacketRouter is never created and `Init()` is never called; `WirePackets` is empty | Being wired |
| 4 | `BotLoop.DeserializePacket` builds a new MessageSerializer per packet and throws the result away. The SDK already raises `Client.MessageReceived`. | Being fixed |
| 13 | No heartbeat hook (`IHeartbeatHandler` in the design, nothing ticks) | To do |
| 17 | Async methods with no `await` (CS1998 once filled in) | To do |
| 19 | No BotApi (/status, /nav, /command), so the copied monitor and the MCP have nothing to talk to | Later, once the bot runs |
| 15 | SDK fork is from about 2026-09-24 | Waiting until the unneeded code (AOSharp.Common.Unmanaged) is stripped. Then port AOBuddy10's 2026-09-29 work, listed below. |

## ControlArbiter: open

- **#5 Lost wakeups.** Each waiter creates its own TaskCompletionSource and stores it in the single `_resume` field, so the last waiter overwrites the others. `ReleaseControl` completes only that last one.
  - Example: a mission step and a travel loop are both parked. On release only travel resumes, and the mission step waits forever.
  - Fix: all waiters share one TCS. Create it only when none exists; complete it and clear it on release.
- **#6 No ownership.**
  - `TakeControl(Emergency)` then `TakeControl(Combat)` lowers the level to Combat.
  - `ReleaseControl()` from anyone drops the level to None, even if that caller never took control.
  - Algorithman considered a stack, but thought the state machine behind it would be hard. It doesn't need one:
    - Keep a count per priority level. The active level is the highest level with a count above zero.
    - `Release(p)` removes only its own entry.
    - Then take Combat, take Emergency, release Emergency returns to Combat.
- **#8 Interrupting a running step.** `RunStepAsync` checks the gate once, before the step starts.
  - Algorithman: long steps poll anyway. TravelTo checks its position every pass.
  - Agreed, as long as every such loop calls the yield on each pass, the way BOTREWORK's TravelTo example does. That is a rule for every long step.

## #14: SDK Item.cs, partly in

The param1 matching fix (AOBuddy10 84ed33b) is in AOBuddy20 (c118c79). The missing-key guards are not. AOBuddy20 Item.cs still has:

- line 63: `highTemplate.Criteria[criteria.Key]`
- about line 102: `highTemplate.Modifiers[modifier.Key]` and `highMod[stat.Key]`

AOBuddy10's Item.cs keeps the low template's values when the high template lacks the action or stat. Its comment (Algorithman, 2026-09-28) records one such item dropping the whole FullCharacter, with no login as the result.

## #18: restart mid-run

AOBuddy10 resumes a mission run after a watchdog restart. Algorithman: that is "bad learned behavior". It will be redone and tied into Awareness, which is buggy at the moment.

## SDK work to port from AOBuddy10 (2026-09-29, commits 8460c64..2b6fed4)

The port is mechanical: take AOBuddy10's SDK files, then re-apply the Message → AOMessage rename. Verify with the census tool (0 throws, 0 bytes left over).

**Message layouts**
- Every message layout is ported from OmniCell's library. Result: 0 throws and 0 bytes left over across all bot recordings and 50 retail captures.
- 41 new message classes.
- Old field names are kept as `[Obsolete]` aliases.

**Character and world data**
- FullCharacterReader: team, raid, pets, buffs and research goals (the trained perks). Verified 865 of 865.
- NanoEffectFormats.
- Chests: QL comes from stat 701, and positions only for ground chests.
- Doors: open state, direction, rooms, and lock difficulty.
- Quests: `AnnounceAsNew`.

**Combat and heals**
- `Blows`: a record of every hit, miss and engage over the last 60 s.
- `UseVerdict`: the GenericCmd echo of our own use (1 = accepted, 2 = refused).
- `SpecialUnavailable`: the seconds left on a lock.
- HealthDamage always sets Health.
- SetNanoDuration now names the caster.

**Connection**
- Pong stamps use our own clock, as the retail client does.

## Settled behaviour (wire-proven in AOBuddy10) to carry into the brains

- Attack is sent once per target. Re-sending it to the same target restarts the swing. Never send StopAttack or StopFight to heal, or when a fight ends.
- Only one system moves the body per frame. Movement is outside the arbiter, and the higher priority wins (#7).
- Stand-up (action 87) is a sit/stand toggle. Decide it once, from the movement mode at login (stat 173 never updates). Firing it blind sits a standing character.
- SetPos self-corrections: apply those of 10 m or more; ignore smaller ones while moving. The retail client ignores corrections under 10 m 86% of the time and takes those of 10 m or more 64% of the time.
- Run speed: velocity = 4.82 + 0.003615 × Run Speed (stat 156), capped at 15.5.
- Heal-out: walk out only when losing, judged from the blow record against server Health. Go at the last safe moment.
- Heal items:
  - Requirements are read from the item's criteria. GreaterThan is strict.
  - A recharger's gate is Treatment OR First Aid.
  - Never fall back to the "lowest QL".
  - Reuse waits for SpecialAvailable.
- Outside, on the way to a mission door, he never fights. He only runs.
- BuffMessage means a buff **ended**: 345 of 426 arrive exactly as the SetNanoDuration runs out.
