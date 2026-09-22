# Elite Remake — Roadmap

A modern remake of **BBC Micro Model B disc Elite** (Ian Bell & David Braben, Acornsoft 1984)
in C# / MonoGame on .NET 10.

Source of truth for the original game:
`/home/tkleisas/Projects/elite-source-code-library` (Mark Moxon's commented disassembly library).
Primary target variant: **`versions/disc`, variant `sth` (Stairway to Hell)**, with
`library/disc/**` + `library/common/**` + `library/enhanced/**` ship blueprints.

## Product decisions (agreed with the user)

| Area | Decision |
|---|---|
| Fidelity | Faithful port: identical universe (same seeds → same systems, names, descriptions, prices), original mechanics, original fixed-point flight model. |
| Visuals | Flat-shaded **solid** 3D from the original blueprint geometry, per-face lighting from the original face normals, authentic palette, modern resolution. |
| Assets | Data extracted from the BBC sources by our own tool; art/audio generated or authored by us (no original bitmaps/samples shipped). |
| Scope | Full disc-version parity, delivered in milestones; flyable core first. |
| Platform | Desktop (Windows/Linux/macOS), MonoGame DesktopGL, keyboard (BBC key layout) + gamepad, widescreen with authentic HUD. |
| Audio | Synthesized recreation of the original beeper SFX (laser, explosion, ECM, missile, docking computer, Blue Danube). |
| Architecture | `EliteRemake.Core` (engine-free deterministic sim) + `EliteRemake.Data` (generated tables) + `EliteRemake.Game` (MonoGame) + `tools/EliteDataExtractor` + xUnit tests. |
| Workflow | Long autonomous rounds, git commit per milestone, report per milestone. |

## Fidelity rules

1. **Data is extracted, never retyped.** Ship blueprints, market/equipment tables, text tokens and
   galaxy seeds come from the asm library through `tools/EliteDataExtractor`, and extraction is
   verified against the reference binaries in `versions/disc/3-assembled-output/*.bin`.
2. **Maths is ported, not reinvented.** 8-bit unit vectors (magnitude 256), the `SNE` sine table,
   `MULT1`/`MLTU2`/`FMLTU` truncating multiplies, 16-bit seeds for galaxy generation — so numbers
   match the original where the original is observable.
3. **Gameplay constants keep original units** (speed, energy, fuel, cash in tenths of a credit,
   distances in the original's internal units) with conversions only at the presentation layer.
4. Where the original is silent (rendering, audio playback, input mapping) we modernise freely.

## Milestones

- **M0 — Toolchain & scaffold.** Solution, projects, tests, screenshot harness, git. ✅
- **M1 — Data pipeline.** ✅ `EliteDataExtractor` extracts all 31 ship blueprints and verifies them
  **byte-for-byte** against the original's own binaries: 17 ship sets (`D.MOA`–`D.MOP` plus the
  docked hangar block inside `T.CODE`), 527 XX21 slots, 204 blueprints, 0 mismatches. Text tokens,
  galaxy seeds, market and equipment tables come next.
- **M2 — Core maths & universe.** In progress. Done: fixed-point maths ✅; the procedural galaxy ✅;
  the market and the commander's starting state ✅; lasers, combat and the ship AI ✅
  (`TT54` twist, `cpl` name generation from the two-letter tokens, `TT24` system data, `TT111`
  closest-system search, `GHY` galactic jumps), verified against the original's own universe —
  **TIBEDIED** is system 0 of galaxy 0 from seeds `&5A4A/&0248/&B753`, and **LAVE** comes out at
  exactly (20, 173) as a Rich Agricultural dictatorship with a radius of 4116 km. Still to do: the
  system description generator (`TT25`, "most famous for its vast rain forests..."), market and
  equipment tables (`QQ23`), and the token-based text printer.
- **M3 — Flight & rendering.** In progress. Done: solid-face 3D renderer with painter's-algorithm
  depth sorting and the original's visibility rules; view camera matching the original's projection;
  ported flight maths (`MVS4`/`MVS5`/`TIDY`/`MVEIT` parts 1/3/5/6/7); flight controls with keyboard
  auto-recentre; the flight scene itself, running at the original's fixed 50 Hz with the universe
  rotating around us; a first-pass dashboard (crosshair, shields, fuel, temperatures, missiles,
  energy banks, speed, roll and pitch indicators, compass). Still to do: the sun and planet objects,
  the authentic dial coordinates from `dials_part_*`, the bitmap font and dashboard text, detail
  edge lines for close ships, and hyperspace arrival.
- **M4 — Combat & AI.** In progress. Done: the laser powers (pulse 15, beam 15 with bit 7 set,
  military 23, mining 50), the pulse-rate rule from LASCT, heating at 8 a shot and cooling at 1 a
  frame with overheat at 242, the energy cost per shot, the HITCH crosshair test against each
  blueprint's targetable area, damage and destruction with an explosion, and the laser beams drawn
  from the bottom corners to the crosshairs exactly as the original draws them; and the ship AI, so
  ships fight back: the TACTICS aggression test against the AI flag, steering towards or away from
  us with the original's RAT of 3 and RAT2 of 4, an aim and range test before opening fire, and
  damage to our ship. Implementing the AI also meant porting MVEIT part 8, the routine that turns a
  ship about its own axes — without it the AI's steering had nothing to drive. Also done: the
  original's damage model, with the fore and aft shields absorbing hits before the energy banks
  (OOPS), the shields recharging from the banks only above half charge (SHD), the banks recharging at
  ENGY + 1 a frame, the energy-low warning, and death — resolved by an escape pod if one is fitted.
  Also done: ship spawning, with the disc version's probabilities (a 47% chance of anything at all,
  anarchy systems always busy and corporate ones quiet, then 61% pirates flying one of the eight
  pack-hunter types and otherwise a lone bounty hunter flying one of four from the Cobra Mk III
  pirate to the Fer-de-lance), spawned ships placed 9728 units ahead with a random heading and
  dressed from their blueprints, and distant ships leaving the bubble so the twelve slots do not
  silt up. Also done: asteroids, boulders, cargo canisters and splinters, with the original's 13%
  spawn chance and three-bit junk limit, its tumbling rocks, the laser-type split (2% canisters,
  50% boulders, 48% asteroids), mining lasers that shatter asteroids into one to three scoopable
  splinters and boulders into one half the time, ordinary lasers that leave a cargo canister, and
  scooping with fuel scoops. Also done: missiles (lock with T, fire with M, homing that steers with
  the same counters ships use, 250 damage on a hit and 80 for one that goes off nearby), the E.C.M.
  countermeasure with its energy cost, bounty payouts from the blueprints with the kill tally and
  the legal status that shooting innocents costs, and the missile lock indicator. Still to do: enemy
  missile launches against us, energy bombs, and the full death sequence.
- **M5 — Docked screens & missions.** In progress. Done: the commander data block with the
  original's default commander, the trade table and its price and availability formulas (GVL/TT151),
  the market screen with buying and selling, and the equipment shop — the original's fourteen items
  with their prices, gated by tech level the way EQSHP does it (three plus the tech level, capped at
  fourteen), with fuel sold by the light year at 2 credits each and each purchase applying its
  effect. Also done: hyperspace — the distance formula is the original's, four times the eight-bit
  square root of the sum of the squared coordinate differences, so a full tank reaches Leesti from
  Lave at 5.6 light years but not Riedquat at 7.2, exactly as in the original. Still to do: the
  chart screens to choose a destination, the shipyard, Data on System and its description generator,
  save/load, missions, and the galactic hyperdrive.
- **M6 — Audio, polish, parity sweep.** Synthesized beeper SFX + docking music, gamepad/rebinding,
  settings, full feature sweep against the original, performance pass.

## Verification strategy

- **Unit tests** for ported maths and procedural generation, pinned to canonical values.
- **Byte-exact data validation** against the assembled original binaries (`D.MOA`–`D.MOP`, `D.CODE`).
- **Visual verification**: the game supports `--screenshot <path> [--frames n]` headless capture so
  rendered frames can be inspected during development (works under `xvfb-run` or the live display).
- **Behavioural parity notes**: any known divergence from the original is recorded in
  `docs/DIVERGENCES.md`.

## Repository map

```
src/EliteRemake.Core/      engine-free simulation + ported maths
src/EliteRemake.Data/      generated tables (committed) + loaders
src/EliteRemake.Game/      MonoGame presentation, input, audio
tools/EliteDataExtractor/  asm/binary → data converter with verification
tests/EliteRemake.Core.Tests/
docs/                      roadmap, data pipeline, divergences
```
