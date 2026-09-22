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

## Dashboard audit

The dashboard has been checked element by element against the original's four DIALS routines and is
complete for the disc version: the speed indicator (SP), the pitch and roll indicators, the four
energy banks (EN), and the shields (FS and AS), fuel (FU), laser temperature (LT) and cabin
temperature (CT) of the last routine, plus the missile indicators, the compass and the 3D scanner.

The one element that could not be placed — an altitude indicator mentioned in the fourth DIALS
routine — is Apple II only. That makes three features this remake has been asked to account for
which the BBC disc version does not have: the shipyard is Elite-A only, the docking music is
Commodore 64 only, and the altitude indicator is Apple II only.

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
  Lave at 5.6 light years but not Riedquat at 7.2, exactly as in the original. Also done: the
  charts — the short-range chart with its fuel circle, which shows at a glance which systems a tank
  will reach, and the long-range chart of the whole galaxy, both with the crosshairs moved by the
  cursor keys and the selected system's data underneath, and H to jump — and Data on System, which
  shows the selected system's distance, economy, government, tech level, population, productivity,
  radius and coordinates, all from its seeds; the commander's status and inventory; and the galactic
  hyperdrive, which is consumed on use and rotates the galaxy seeds one bit to the left, so eight
  jumps bring you back to where you started.

  **There is no shipyard in the disc version.** An earlier entry in this roadmap listed one, but
  checking the original's docked screens shows they are Buy Cargo (f1), Sell Cargo (f2), Equip Ship,
  the Long-range Chart (f4), the Short-range Chart (f5), Data on System (f6), Market Prices (f7),
  Status and Inventory (f9) — the shipyard is an Elite-A feature, so full disc parity does not need
  one. That entry has been removed.

  Also done: saving and loading the commander. The original writes a 76-byte block to disc ending
  in two checksum bytes; this remake saves the same information — cash, fuel, galaxy, system seeds,
  lasers, equipment, hold and kills — as a small versioned JSON file, validates what it reads rather
  than trusting it, and starts from the saved commander when one is present. CTRL-S saves while
  docked. Still to do: the system *description* generator and the missions.

  A note on the description generator: the phrases ("most famous for its vast rain forests…") live
  in the original's extended token table, TKN1, which runs to over six thousand lines of source, and
  they are assembled by its extended text system — the DETOK printer with its jump tokens, sentence
  casing and the DTW flag tables. That is a subsystem in its own right, comparable in size to the
  ship blueprint extraction, and it wants a round of its own: the right shape is to extend
  EliteDataExtractor to emit the token table and the description fragments as data, then port the
  printer and the seed-driven selection.
- **M6 — Audio, polish, parity sweep.** In progress. Two corrections to the earlier list: the
  **docking music is not a disc-version feature** — `startbd` is called only in the C64 build, so the
  Blue Danube is not part of disc parity — and the **energy bomb is now implemented**: TAB sets it
  off, it is a one-shot, it destroys every ship in the local bubble except the space station, and
  its victims count towards the kill tally, which is exactly what the original's main loop does.
  Also done: **docking checks** — the five geometric tests the original's ISDK applies before it
  lets us in (the station must not be hostile, its slot must be facing us within about 26 degrees,
  we must be facing the station, we must be inside the slot's 22 degree cone of approach, and the
  slot must be roughly level), with a collision anywhere else on the station being fatal, as it is
  in the original.

  **The docking computer's controls now settle, but it still cannot dock.** Two fixes have landed
  since it was first written: the controls are no longer full deflection or nothing — the autopilot
  asks for a turn no faster than the aim error warrants and lets go once the ship is turning at that
  rate, reading the roll and pitch rates (whose centre is 128, and which rolling right takes below
  it) — and the approach follows a glide slope, so it can no longer charge past the station. Without
  the glide slope it overshot and left the station behind, which this flight model cannot recover
  from because it has no yaw. What remains is that it arrives slightly off the slot's axis — a
  couple of hundred units wide at a few hundred out, which is just outside the 22 degree cone — and
  collides instead of docking. Centring on the slot axis before closing is the next step, and the
  tests currently pin the settling and the glide slope rather than a docking that does not happen.

  **The original's algorithm is DOCKIT, and this should be ported rather than approximated.** The
  hand-written controller above works but it is an invention, and the project's own rule is that
  maths is ported, not reinvented. DOCKIT lives in the enhanced library and is reached from the key
  logger's manoeuvring code, which sets up a scratch ship looking along +z at our speed and calls
  DOCKIT to work out the moves, then writes the answer into the key logger — so the docking computer
  flies the ship through exactly the same path as the keyboard.

  What DOCKIT does, and the thresholds it uses:

  * `RAT = 2`, `RAT2 = 6` — the counters to set when turning, and the threshold below which pitch
    and roll are not applied at all.
  * `CNT2 = 29` — the angle beyond which a ship slows down to turn.
  * Outside the station's safe zone, or too far for anything accurate, it falls back to GOPL and
    simply heads towards the planet.
  * Otherwise it measures the distance with TA2 and normalises the vector with TAS2, then tests the
    approach with two dot products (TAS4 with the slot axis, TAS3 for the refinement). Pointing the
    wrong way, or less than 35 on the first test, sends it to **PH1: fly to the ideal docking
    position**, which DCS1 works out by stepping out along the slot from the station. Within 157
    units and badly placed it turns away instead (PH2). Pointing the right way it **refines** (PH3),
    rolling and pitching towards the station only while the station is within 12 units of the
    crosshairs.

  **The part this implementation gets wrong, and the reason its autopilot arrives off-axis: PH1
  rolls to match the space station's own roll**, setting a positive undamped roll counter, and PH3
  rolls towards the station. The Coriolis rotates, so the slot's orientation changes as you
  approach, and the docking computer tracks it. A controller that aims at a point and ignores the
  station's rotation — as the one here does — cannot line up with a rotating slot, which is exactly
  the failure observed.

  **DOCKIT's structure has now been ported**, in place of the invented controller: the three
  phases, the ideal docking position eight nose vectors out (DCS1 steps out by two and runs twice,
  and PH1 calls it twice), the roll matching, the thresholds (RAT 2, RAT2 6, 157 units, a dot
  product of 35, a docking speed of 22) and the speed discipline, which now uses the original's cap
  rather than an invented glide slope. It still does not dock, and the reason is now clear:

  **The counter override is in, and the docking computer now docks a well-placed approach.** The
  simulation accepts rotation counters set directly — `SetRotationCounters`, which the original's
  manoeuvring code does with `STA INWK+29` and `STA INWK+30` — and DOCKIT's phases now fly as
  written: with the station straight ahead, the autopilot completes a docking (frame 507 in the
  test harness). That was the blocker identified a round earlier, and it was a change of shape
  rather than more tuning.

  An approach from off to one side still overshoots: it flies past the station instead of turning
  onto the slot. The likely cause is that the Coriolis does not turn in this implementation. In the
  original the station rolls continuously, which is precisely what PH1's roll matching matches — it
  sets a fixed, undamped roll so the ship turns with the station and the slot stays lined up. A
  station that never rolls gives the matching nothing to match, so the ship's roll drifts and the
  approach misses. Giving the station its rotation is the next step.

  **DOCKIT writes the ship's roll and pitch counters directly** — `STA INWK+29` and `STA INWK+30` —
  rather than holding the controls down. The original's docking computer is not a pilot pressing
  keys: it *sets the rotation rates* and lets MVEIT fly the ship. This port returns a
  <see cref="FlightInput"/>, which can only press keys, so it cannot express what DOCKIT actually
  does, and the ship never turns at the rate the algorithm asks for. The shape of the fix is
  therefore to let the simulation accept a counter override for the docking computer, the way the
  original's key logger's manoeuvring code does, and then the phases will fly as written.

  The remaining routines to port are TA2, TAS2, TAS3, TAS4, TAS6, TA151, VCSU1, DCS1 and GOPL; several have
  close relatives already in ShipMath and Tactics (Norm is TAS2, and the counter-setting in Tactics
  is TA151's shape), so this is smaller than it looks. When it lands, the hand-written controller
  should be deleted rather than left beside it.

  The original's docking computer, for reference: Its shape is right: like the
  original, which drives its docking computer through the key logger so that a person and the
  computer fly the ship with the same code, this one produces a FlightInput — the same thing the
  keyboard produces — so the autopilot has no special path into the flight model, and its controls
  have the right sense (a station to the right is corrected by rolling right, one above by pulling
  up). What it does not do is settle: the controls are full deflection or nothing, so the ship
  overshoots and swings past the station instead of closing on it, and it cannot currently dock.
  The fix is to read the rotation rates and ease off as the aim error shrinks, which is what the
  original's key-logger manoeuvres do.

  A modelling fact worth recording, found while writing the autopilot: because this implementation
  turns the universe around the player rather than the player within it, our own axes are the fixed
  ones, so **we always face +z in the world's terms** and steering is a matter of reading the aim's
  world components directly. The docking checks now pass +z as our facing for the same reason;
  passing the player's stored orientation was wrong, since it never changes. Done: the original's ten sound effects with
  their exact SFX bytes, rendered as square waves with the BBC's pitch divider and its four
  envelopes (a decaying note, an upward sweep, noise and a fast tremolo), and triggered by the same
  events the original uses — firing, being hit, a kill, a missile launch, hyperspace and the E.C.M.
  The sounds flush one another, as a single sound chip does. Still to do: the docking music (Blue
  Danube), gamepad rebinding, a settings screen, the full parity sweep and a performance pass.

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
