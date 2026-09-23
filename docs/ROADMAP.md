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
2. **Use the original algorithms.** Every behaviour comes from the original's routines where one
   exists, and the routine is named in the code and the commit. Where an invention has crept in, it
   is a bug to be replaced, not a variation to be kept. The list of things built by hand and later
   replaced by the original's own algorithm is a standing embarrassment and a useful reminder: an
   invented PD controller for the docking computer, where DOCKIT has three phases and matches the
   station's roll; key presses for rotation, where DOCKIT writes INWK+29 and INWK+30; and a
   constant station roll of 6, where the main game loop gives the station a random clockwise roll.
   Each was found by reading the source *after* writing the invention, which is the wrong order.

3. **Maths is ported, not reinvented.** 8-bit unit vectors (magnitude 256), the `SNE` sine table,
   `MULT1`/`MLTU2`/`FMLTU` truncating multiplies, 16-bit seeds for galaxy generation — so numbers
   match the original where the original is observable.
4. **Gameplay constants keep original units** (speed, energy, fuel, cash in tenths of a credit,
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
  onto the slot. The station's rotation was the obvious suspect and is now right — the original
  gives a station **a random clockwise roll**, a random value with bit 7 cleared and a 1 in 127
  chance of no damping, set where the main game loop creates it, and the station turns for as long
  as it is there (MVEIT spends a counter as it uses it, so the roll is renewed from the value the
  station was created with). That replaced an invented constant. It was not enough on its own, so
  the remaining fault is in the approach geometry rather than the station: with the station 6000
  units away and 2000 to one side, about 18 degrees off, the autopilot does not turn onto the slot
  before it arrives. Working out which of DOCKIT's tests is sending it down the wrong branch — PH1,
  PH2 or PH3 — is the next step, and the harness to do it is in place.

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

## Open items, as of round 35

Everything below is known to be unfinished or unverified. Nothing here is guessed at: each entry
says what was checked and what was not.

1. ~~**The AI's close-range disengage.**~~ **Found and ported.** The rule is at the head of TACTICS
   part 7, at `TA4`, and the earlier note was right about it existing — it just looked in the wrong
   part. The original tests `z_hi >= 3` and, failing that, `x_hi OR y_hi` with bit 0 cleared
   (`AND #%11111110`), so a ship with z under 3 * 256 units and both x and y under 2 * 256 "heads
   away from us" whatever its aggression says. It shares the `TA15` branch with the peaceful-ship
   case, which is why grepping for a separate routine missed it.

   Two things came out of reading that part properly. The close-range rule is now in
   `Tactics.Apply` as `CloseRangeZ` and `CloseRangeXY`, with a test that places a ship beside us
   facing along +x so turning towards us and turning away are distinguishable: measured, its nose
   ends up at +0.91 close in against -0.99 further out.

   And the turn magnitude is not a flat RAT. The disc version calls **nroll**, which doubles the
   dot product and compares it against RAT2: below the threshold the counter keeps only its sign
   and no magnitude, which stops a ship twitching at an aim it is already close to, and at or above
   it the counter is the full RAT. `CounterFor` now does the same, scaling the normalised float
   direction back into the original's units where a unit vector's component is 96.

2. **The docking computer** completes a docking when the station is dead ahead (frame 507 in the
   harness) and overshoots from off to one side. With the station's rotation now the original's,
   the fault is in which of DOCKIT's three branches is taken at that offset. The harness to answer
   it is in place.

3. **The station's rendering.** From dead ahead the Coriolis reads as a flat grey square with a
   tilted square inside it. That may be exactly right — the Coriolis is a cube with a slot, and one
   face is what you see head-on — but it has not been checked against the blueprint's geometry and
   lighting, and it is the sort of thing that has been wrong before.

4. ~~**The energy bomb against Thargoids and the Constrictor.**~~ **Settled, and the code is
   right.** Read from the source this time, in `main_flight_loop_part_5_of_16`: the disc version's
   exemption list is exactly one entry, the space station (`CPY #2*SST / BEQ MA21`). The Thargoid
   and Constrictor exemptions are wrapped in `_MASTER_VERSION OR _C64_VERSION OR _APPLE_VERSION OR
   _NES_VERSION` and `_6502SP_VERSION OR _C64_VERSION OR _APPLE_VERSION OR _MASTER_VERSION OR
   _NES_VERSION`, so neither is assembled into the disc version. Thargoids and the Constrictor are
   as vulnerable to an energy bomb here as anything else — Constrictor included, which is a
   *departure* the disc version makes from the later ones.

5. **Rebinding and a settings screen** — the brief mentions a gamepad and an authentic keyboard
   layout; the keyboard layout is in, rebinding is not.

6. ~~**Performance.**~~ **Measured, and it is not the game that is slow.** The first attempts gave
   absurd figures — 835 ms a frame, 1.2 fps — which is a display that is not completing its swaps
   rather than anything to do with the code. So the report was split: the shell now times its own
   `Update` and `Draw` separately from the wall-clock frame, and prints both.

       Drew 20 frames in 15.06s: 752.78 ms a frame, 1.3 fps
         of which our own work: 2.45 ms update + 3.39 ms draw = 5.83 ms; the rest is the display

   The budget is the simulation's own rate: 50 Hz, so **20 ms a frame**. Our work comes to **under
   6 ms**, and a second run at 1920x1200 with twelve ships in the bubble — `--stress` fills it, which
   is how the worst case was reached — came to 4.97 ms. That is roughly a quarter of the budget with
   three quarters to spare, and the remaining 747 ms of that frame is the display, which is what the
   virtual X server here does rather than a property of the game.

   The honest limit on the figure: the stress run had only 122 triangles on screen because most of
   those ships were behind or beside us, so the drawing half of it is measured on a quiet view. The
   simulation half is not affected by that, and it is the larger of the two.

## What the session has taught, in one place

Four bugs were found by the user looking at the game rather than by any audit: the missing 3D
scanner, missing collisions with other ships, a station that never rotated, and the planet and sun
being invisible. A fifth — the compass drawn as a large ring instead of a dot — survived a
"dashboard audit" that checked which instruments existed rather than whether they looked right.

Five invented numbers or algorithms have been replaced by the original's own: an invented PD
controller for the docking computer, key presses where DOCKIT writes rotation counters, a constant
station roll of 6, a bubble range of 32,768, and a compass shape and position. Each was found by
reading the source *after* writing the invention, which is the wrong order, and which is why "use
the original algorithms" is now rule 2 of the fidelity rules.

The lesson worth keeping: look at the game after every change, and read the source before writing
anything.

## Dashboard layout

The scanner is drawn as a thin continuous ellipse — joined line segments rather than a ring of
little blocks, which is what made it look chunky — and it keeps the original's shape: 128 by 52 in a
256-wide dashboard, a little over twice as wide as it is tall. The instrument bars are narrower than
they need to be so the scanner has room between the two panels, which is closer to the original's
arrangement. The EN and SP labels still sit very close to the scanner's right edge and the compass
tucks into its lower right; a proper layout pass would space the panels from the scanner rather than
from fixed fractions of the dashboard width.

## Scanner style: a note on fidelity

The scanner's ellipse and its blips are drawn as **dots**, which is the style the remake is being
taken to and reads better at modern resolutions than a hairline outline.

Worth recording for the record, because it is a deliberate departure: the BBC disc version draws
the scanner's ellipse as a **continuous line** — its `CIRCLE` routine — and marks ships with
**dashes** rather than dots, using `CPIX2` ("draw a single-height dash on the dashboard") and
`CPIX4` ("draw a double-height dot on the dashboard"). The dotted ellipse in the reference image is
the **NES** version's dashboard, which also has the `$ ELITE` panel along the top.

So the dots are a presentation choice, not the disc version's own rendering. If strict disc parity
is ever wanted here, the change is localised to `DrawEllipse` and the blip size in `HudRenderer`.

## Docking computer, as of round 38

The branch test was wrong in a way the tests caught. DOCKIT's dot product is the station's slot
against the line to us; this implementation's runs the other way — the line to the station against
the slot — so the comparison is reversed. Getting it the wrong way round made PH1 the default
branch, which left the ship rolling to match the station's roll for ever instead of ever refining
its approach. The trace that found it is worth repeating: the station sat behind the ship at
approach +0.99 with the slot facing it perfectly, and the autopilot held roll counter 191 — the
station's own roll — for thousands of frames.

With the sign corrected, a docking dead ahead still completes (frame 507) and the tests pass. An
approach from off to one side still does not dock, so something else in DOCKIT's phases is still
not faithful. The next step is the same instrumented trace, but following which phase is entered
with the station off to one side and why the refinement does not converge — not another guess.

## Docking computer: what the phase trace shows

Tracing which phase is entered on an approach from off to one side gives the answer, and it is the
same failure mode as before — a simplification standing in for an original routine.

The approach starts at (400, 300, 6000), offset 500 to one side of the slot. The trace:

| frame | phase | distance | approach | counters |
| --- | --- | --- | --- | --- |
| 0 | Refine | 6020 | -1.00 | (128, 128) |
| 150 | Refine | 3006 | -0.99 | (2, 2) |
| 375 | Refine | 868 | -0.82 | (2, 2) |
| 525 | IdealPosition | 576 | -0.16 | (191, 128) |
| 825 | IdealPosition | 2177 | +0.87 | (191, 128) |

The refine phase turns with counters of **2**, the smallest the original has, and holds them for
hundreds of frames. The lateral offset never shrinks — it is still (400, 300) all the way in — so
the approach angle degrades from -1.00 to -0.62 as the distance closes, until the ship is no longer
squarely facing the slot, at which point it falls into PH1, matches the station's roll and circles
for ever without closing.

**The cause is that PH3 does not use the original's `RefineApproach` routine.** That routine works
out how far off the aim is and sets the counters accordingly; this implementation sets a fixed
counter of 2 whenever the aim is off by more than the dead band, which is a simplification invented
here. Small constant turns cannot correct a 500-unit lateral offset in the time available, however
long the approach is.

So the next step is not another trace: it is to port `RefineApproach` and its helpers, as DOCKIT
itself was ported, and delete the fixed-counter simplification.

## Presentation additions, listed in one place

These are deliberate departures from the BBC disc version's own rendering, made because the remake
is being taken to the look of the reference dashboard. They are listed together so none of them can
be mistaken for disc fidelity later:

1. **Dotted scanner.** The disc draws the scanner's ellipse as a continuous line with its CIRCLE
   routine, and marks ships with dashes (`CPIX2`, "draw a single-height dash"). This draws dots for
   both.
2. **Forward view wedge on the scanner.** Two lines opening upwards from the centre of the ellipse,
   at about 35 degrees either side of straight ahead, showing the cone the front of the ship covers.
   The apex is the centre because that is where the ship is, and each arm runs only as far as the
   point where it meets the ellipse, so the wedge never leaves the scanner. The disc has no such
   thing: the only cones in its sources are the docking approach tests, and its scanner shows
   contacts without any indication of the view cone.

Both come from the NES version's dashboard. Everything else in the dashboard — the instrument set
and their arrangement, the scanner's projection and height sticks, the compass dot — is the disc
version's own.

## Docking computer: what PH3 actually does

Reading PH3 closely, now that the trace has pointed at it:

```
.PH3
 BPL PH32               \ (NPC ships take a different path)
 LDA #2                 \ Set A = +2 or -2, giving it the sign in the C flag
 STA INWK+29            \ the ship rolls towards the station
 CMP #12                \ if the station is not in our sights
 BCS PH22               \ stop manoeuvring
 JSR RefineApproach     \ refine our approach using pitch
 BCS PH22               \ stop if the target is not in our sights
 LDA #2                 \ the ship pitches towards the station
 STA INWK+30
```

Three things follow, and they change the picture from the last trace:

1. **The counters of ±2 are right.** The original really does set a counter of two in PH3, so the
   fixed counter here is not the fault after all — it is the original's own value. The earlier note
   blaming it was wrong.
2. **There is a ±12 sight limit.** The original stops manoeuvring when the station is more than 12
   off the crosshairs, because it is not worth turning towards something that far out of the
   window. That limit is missing here.
3. **`RefineApproach` is a separate routine** that PH3 calls to work out the pitch, and its
   definition has not been located: it is called from dockit.asm but is not in that file, and it is
   not in the common library under that name. Finding it is the next step, and it is the piece that
   decides the pitch, so it is probably where the real answer is.

So the previous round's conclusion — that the fixed counter was the cause — was wrong, and this
note corrects it. The next step is to find `RefineApproach`, likely in the Elite-A or 6502SP
libraries or behind a different label, and read what it does with the vector to the station.

## RefineApproach, found and ported

`RefineApproach` lives in the **demo** library (`library/demo/main/subroutine/refineapproach.asm`),
and the note above it settles where it comes from:

> This routine has been copied from the disc version of Elite. [...] the code at PH3 in the disc
> version has been extracted into the RefineApproach subroutine, so it can be used to refine our
> ship's approach for both the current enemy target and the planet/station.

So it is the disc version's own PH3 code, and it does three things:

1. **Zeroes the pitch counter** and sets RAT2 to 0, so roll and pitch are always applied while
   refining.
2. **Rolls towards the target using the sign of -x * y** — a quadrant test on the target's position
   in the ship's own frame, not the x component alone. That is how the ship knows which way to roll
   to bring the target round to the centre.
3. **Gives up if the target is more than six units off the centre line**, setting the carry so the
   caller slows right down. Refining is a fine adjustment: getting roughly lined up is PH1's job.

All three are now in `DockingComputer`, replacing the x-component rule and the invented dead band.

An approach from off to one side still does not dock. The trace still ends with the ship falling
into PH1 and matching the station's roll without closing. Since PH3 is now the disc version's own
code, the remaining fault is either in PH1 — the ideal docking position and how the ship is meant to
reach it — or in the handover between the phases. That is where to look next, and the phase trace is
the tool.

## PH1: what it does, and the state it leaves

Reading PH1 again rather than guessing at it: it works the ideal docking position out with VCSU1 and
DCS1, normalises and negates the vector, **sets the roll counter to 127 to match the space station's
roll**, and then falls through into the same turning code the other phases use — the code at TN13,
which heads the ship in the direction of the vector it has just built. So PH1 does two things, and
this implementation only did the first: it matched the station's roll and never pitched towards the
ideal position, which is why the trace showed the ship circling the station for ever.

That second half is now in, so PH1 heads for the ideal position as the original does. It has not
made the off-axis approach dock in the harness — the trace still ends with the ship circling at a
growing distance — so something in how the ideal position is built or how the turn is applied is
still not faithful. The turn itself is the next suspect rather than the position: the shared turning
code at TN13 is `TA151`'s shape, and the pitch counter it sets is the same ±RAT the refinement uses,
so if the ship is turning the wrong way the fault is in the sign convention of that shared code
rather than in PH1's own logic.

The state is worth being plain about: **the docking computer completes a docking when the station is
dead ahead and does not from off to one side.** Every piece of it is now the original's own code
rather than an invention, so the remaining fault is a translation error somewhere in the turning,
and the phase trace plus the counters it prints are the tools for finding it.

## The docking computer: the sign question, and a note on method

One line of enquiry is recorded here rather than left in a commit, because it was **reasoned but
not verified** and the change was reverted rather than accumulated.

The direction the autopilot turns is decided by the sign of the counter it sets. This
implementation converts a counter to a rate with the rate range centred on 128, and its own probes
establish that `RollRight` drives the rate below 128 and `PullUp` does the same for pitch, with the
world turning around a fixed ship. So bringing a target that is **above** the centre line to the
centre means **pulling up**, which is a rate below 128, which is a **clockwise** counter, which is
bit 7 **set**. On that reasoning the sign tests in `DockingComputer.Steer` looked inverted: they set
bit 7 clear for a positive pitch angle, and the quadrant test for roll was inverted with it.

Flipping both signs was tried, and **the harness behaved no better** — the ship still circled the
station at a growing distance. Because the change could not be shown to improve anything, it was
reverted rather than committed. The code is left at the last verified state.

What that tells us is that the turn direction is *not* the remaining fault, or not the whole of it.
The next step is therefore not another sign change but a **trace of the turn itself**: log the roll
and pitch rates and the angles they produce each frame, and check whether the world actually rotates
the way the counters ask for. If it does, the fault is in the aim — what PH1 builds as the ideal
docking position — rather than in the turning. If it does not, the conversion between counters and
rates is where to look.

The method note is the point of recording this. Several sign and counter changes have now been made
to this one loop on reasoning alone, and each was reverted or left unverified because the harness
did not improve. Reasoning is not evidence; the loop needs a measurable step before the next change.

## The turn, measured

The measurement the last note asked for, and it produced one clear answer and one surprise.

Setting a counter by hand and watching where a target to the right and above actually goes:

| counter set | rate | target's y |
| --- | --- | --- |
| pitch 2, clockwise (bit 7 set) | 126 | 3000 -> **2960**, to the centre |
| pitch 2, anticlockwise | 130 | 3000 -> 3000, unmoved |
| roll 2, clockwise | 126 | target's x unmoved |
| roll 2, anticlockwise | 130 | target's x unmoved |

**The pitch sign was inverted**, and the fix is in: a clockwise counter is a rate below the centre
of the rate range, the world turns around a fixed ship, and that brings a target which is above the
centre line down to it. The reasoning of the previous round was right about this; it is the
*measurement* that makes it a fix rather than a guess.

**The roll did nothing at all** — the target's x did not move by a single unit under either counter
direction. That is a surprise and it is the more interesting of the two results: a counter of 2 is
the slowest turn the original has, so it may simply be too small to move anything measurable in
forty frames, with the pitch moving only forty units over the same run. The next measurement is the
same one with a large roll counter, which will separate "the roll is too slow to matter" from "the
roll is not being applied to the world".

Re-running the docking harness after the pitch fix: a docking dead ahead still completes at frame
507, and an approach from off to one side still does not. So the pitch sign was a real fault and not
the whole of it.

## The docking computer: the fault, found

The two measurements together identify the fault exactly, and it is not in DOCKIT at all — it is in
how the counters are fed into the flight model.

**Measurement one** established that the roll works: a roll counter of 100 moves a target's x from
3000 to 754. So the world does turn when it is asked to.

**Measurement two** established why a counter of 2 does not. The rate-to-angle conversion, which is
the original's own — divide the distance from the centre of the rate range by four, and halve it
again if the result is under eight — gives an angle of **zero** for a rate of 126, which is what a
counter of 2 produces. The table says so directly:

| rate | roll angle |
| --- | --- |
| 128 | 0 |
| 126 | **0** |
| 100 | 3 |
| 28 | 25 |
| 1 | 31 |

So every turn the docking computer asks for, in PH1 and PH3 alike, is rounded to nothing and the
ship never turns at all. That is the whole of the off-axis failure: the autopilot is not
mis-steering, it is not steering.

**The reason is that the counters go into the wrong path.** The rate-to-angle conversion above
belongs to the *keyboard* path: MVEIT part 5 turns the universe by the rates the keys produce.
DOCKIT's counters are not key rates — the original writes them into the ship's own data block at
INWK+29 and INWK+30, which **MVEIT part 8** applies, turning the ship about its own axes by the
counter's magnitude directly. Part 8 is a different calculation from part 5, and a counter of 2
through part 8 is a real, if small, rotation. Feeding the counters through the keyboard path is what
rounds them away.

**The fix, then, is not in `DockingComputer` but in `FlightSim.SetRotationCounters`**: the override
should rotate the universe by the counter magnitudes as MVEIT part 8 does, rather than translating
them into rates and handing them to the keyboard path. `ShipMovement.RotateBodyLocationByOurPitchAndRoll`
already does something of this shape for the planet and sun, and the ship equivalent for a counter
rotation is the routine to use. That is the next change, and unlike the last several it is aimed by
two measurements rather than by reasoning.

## The station's docking slot: found, and why it was invisible

The user's report — "I do not see the opening in the space station for docking" — turned out to be
two separate faults, one in the renderer and one in the station's creation. Both are now fixed, and
the second is the more interesting of the two.

**Fault one: the slot is a detail line, and a solid renderer paints over it.**

The Coriolis blueprint expresses its docking slot as four extra vertices (12-15, a 20 x 60 rectangle
at z = 160, on the station's +z face) and four extra edges (24-27) whose records name **face 0
twice**:

```
 EDGE      12,      13,     0,     0,         30    \ Edge 24
```

That is how the original says "this line belongs to face 0" without describing an outline. LL9's
edge loop (part 10) draws an edge if *either* of the two faces it names is visible, so an edge that
names the same face twice is drawn whenever that face is — and since a face cannot border itself,
the edge has to be detail drawn *on top of* the face. The Cobra Mk III uses exactly the same trick
for its engine outline (14 edges all naming face 9).

A renderer that fills faces instead of drawing lines gets the slot painted over by the very face it
decorates. `ShipMesh` now finds these edges (`DetailEdges`: an edge whose two face numbers are the
same *real* blueprint face), and `MeshRenderer` draws them as a later layer than the faces. This is
the one place where depth sorting alone cannot work — the slot is exactly as far away as the face
around it — so the queue carries an explicit layer.

Two rules were tried and one was rejected. An earlier version also treated any two faces whose
normals were within 0.95 of each other as coplanar, which swept up 60-odd perfectly ordinary
silhouette edges on the missile, asteroid, shuttle and others. Only the exact rule survives: same
face number, and that face must be a real one (the alloy plate's four edges all name face 15, the
original's dummy "always visible" face, which has no outline to decorate).

**The visibility rule was also wrong, and was hiding the slot.** The comment claimed the ship's
z-distance is reduced to 0-31 and the comparison was capped at 31; LL9 part 2 actually gives XX4 a
maximum of **7** (a ship at z_hi >= 16 is drawn as a dot instead, and carries 7). With the wrong
cap, the station's slot — visibility 30 — was culled until the station was very close. With the
right one it is never culled while the station is solid, and a Cobra's exhaust detail (visibility 6)
appears only up close, which is the behaviour the original has.

**Fault two: the station was created facing the wrong way, so the slot pointed away from us.**

The blueprint puts the slot in the station's +z face. The original's `NWSPS` — its station creation
routine — turns the station right around as it creates it, calling `NwS1` once for each of the three
high bytes of the station's nose vector to flip their signs. Our spawn never did. The station was
therefore created with its slot facing away from us, and since the original places new stations
*ahead* of us, we were looking at its blank rear face: with the fix above, the slot would have been
drawn correctly and still never seen.

The direction is not a guess. The original's own docking computer settles it: `DCS1`'s commentary
says "the nose vector points from the centre of the station through the slot", and the existing
docking tests here assume the same thing (they set `nosev_z = -1` for a station ahead of us so its
slot faces back at us). So the slot faces the player only when the station's nose points *at* the
player — which is what `NWSPS`'s flip produces, and what was missing.

`SystemArrival.CreateStation` now builds stations (placement, orientation flip and spin roll in one
place), and `FlightScene.SpawnStationAhead` calls it. That also puts the whole thing under test: a
new test asserts that a station created ahead of us presents its slot to us, and that the original's
own `Docking.Check` agrees.

**One deliberate departure.** `NWSPS` flips only the nose vector, leaving the roof and side where
they were — which is not a rotation and leaves the model mirrored. We flip all three, so the three
vectors stay a consistent basis. The slot ends up in the same place either way.

**How the slot is drawn.** The original draws a wireframe, so its slot is four white lines on the
station's face and nothing more. Filling the face loses that, so the slot is drawn as the original
*shows* it: a hollow cut into the face. The recess is filled (a dark blue-grey) and the four edges
are drawn as a bright white lip on top — the same white the original draws every ship's lines in,
and the reason an opening reads as an opening rather than a panel.

## Method note: the overload that made three diagnostics lie

For an hour of this round the diagnostics said the station's face 0 was not visible when it plainly
was. The cause is worth recording because it will bite again.

`GlobalUsings.cs` aliases `Vector3` to `Microsoft.Xna.Framework.Vector3`. MonoGame's `Vector3` *is*
the `Microsoft.Xna.Framework.Vector3` the alias points at, so writing
`System.Numerics.Vector3.Dot(a, b)` to force the System.Numerics implementation does not work: both
names resolve to the same type, both libraries' `Dot` are indistinguishable by signature, and
whichever the compiler picks wins. The two agree on the value but not on the convention, so the
result was silently the opposite sign.

The lesson is the one the last few rounds keep teaching: when a measurement contradicts what is on
screen, suspect the measurement. The face-visibility dot product is now computed term by term rather
than called, so there is nothing left to resolve ambiguously.

## The dashboard followed the old window size

The user maximised the window and the dashboard stayed small, in the top-left corner, instead of
sitting centred across the bottom. The cause is a cached layout: `HudRenderer` copied
`layout.View`, `layout.Dashboard` and `layout.Scale` into readonly fields **in its constructor**,
and the renderer is built once when the flight scene is created. `EliteGame.UpdateCameraToViewport`
did recompute the layout and resize the camera whenever the window changed size, but nothing ever
told the dashboard.

`HudRenderer` now keeps the `ScreenLayout` itself and reads `View`, `Dashboard` and `Scale` from it
on every draw, and `EliteGame` hands it the current layout each frame. The instruments are all
positioned from those three values, so they follow the window with no further changes.

The right-hand panel also had its labels clipped at large sizes: it sized itself from a fraction of
the dashboard width (`Dashboard.Right - 5%`) and then drew its two-letter labels *outside* that
edge, and a speed figure past it as well. The panel now stops short of the edge by one measured
label width (`TextRenderer.Measure`), so nothing can be clipped at any window size.

Verified by rendering at 1280x800, 1920x1131 and 2560x1440: the dashboard band starts at exactly
`height - round(height * 64/256)`, reaches the last row, and is centred on the space view at every
size.

## The station's spin: fast, but the original's own rate

The user reports the station spins extremely fast. It does — and measurement says it is correct.

The rate is **not** governed by the roll counter's magnitude. `MVEIT` part 8 calls `MVS5` once
whenever the counter is non-zero, and `MVS5` turns the orientation vectors by a **fixed 1/16
radian, or 3.6 degrees** (its own header comment says so, and the deep dive is titled "Pitching and
rolling by a fixed angle"). The counter's value only decides how many frames the turn continues,
and its sign which way. `NWSPS` sets the station's counter to 255 — "maximum anti-clockwise roll
with no damping" — which means only that the station never stops turning. Any non-zero value gives
the same speed, so there is no counter value that would slow it down.

Measured here, on the created station, over 10 frames: **3.58 degrees a frame**, the rounding
difference being fixed-point. That is a full turn in about 100 simulation frames. Our simulation
runs at the original's 50 Hz (`FlightScene.FrameRate`), so that is a turn in about two seconds.

A new test pins this down: the turn is 3.4-3.7 degrees a frame for roll counters of 1, 64, 127 and
255 alike. The test measures frame by frame rather than accumulating, because at this rate the
angle wraps every hundred frames; it also establishes that the station rolls **clockwise**.

`SystemArrival.CreateStation` now also writes the roll counter into the ship's data block at
creation. It was only being copied in from `SpinRoll` during the first frame's MVEIT, so a station
was briefly created not turning.

**On the perceived speed.** The user's observation that the original ran at a lower frame rate is
the right way to think about it: because the turn is per *simulation frame*, the same code looks
slower on hardware that ticks less often, and the BBC original often managed well under 50 frames a
second while drawing a whole screen of lines. The rate is kept as the source defines it, at the
source's frame rate. If a slower station is wanted as a presentation choice, the honest place for
it is a documented departure with a speed multiplier, not a change to the rate itself.

## The docking computer: counters, signs and a speed wrap

This round found the rest of the off-axis docking failure, and it was four separate faults, three
of them in the flight model rather than in DOCKIT.

**The counters now reach the flight model as counters.** DOCKIT writes rotation counters into
INWK+29 and INWK+30 and lets MVEIT part 8 turn the ship. The previous round established that routing
them through the keyboard *rate* path rounded small counters away to nothing. They now go through
`FlightSim.SetCounters`, where the counter's magnitude **is** the turn: a counter of `m` is `m`
frames of the original's fixed 1/16 radian step, so the world turns by `m` angle units. Measured
afterwards: a counter of 2 turns, a counter of 127 turns nearly a quarter circle, and the two
directions mirror each other.

**The counter signs are not all the same way round.** Measured, one at a time, on a station above
the centre line:

| counter held | effect on the station |
| --- | --- |
| roll 0x82 | moves it left — a roll to the right |
| roll 0x02 | moves it right — a roll to the left |
| pitch 0x82 | moves it **down**, towards the centre — a pull up |
| pitch 0x02 | moves it **up**, away — a nose-down pitch |

So the roll counter's sign reads the same way round as a key rate, and the pitch counter's is
inverted relative to it. `SetCounters` now encodes that difference explicitly rather than assuming
one convention for both, because assuming one convention is exactly what put the autopilot into a
diverging spiral: it pitched away from the station every frame, which took the station from y = 300
to y = 2300 in 120 frames.

**A zero-magnitude counter is no turn, whatever its sign bit says.** DOCKIT writes 0 to stop
pitching and **128** — sign bit set, magnitude zero — as its "no turn" roll. Reading 128 as a turn
gives a rate below the centre and leaves the autopilot rolling gently to one side for ever.

**The sights threshold was out by a factor of six.** The original's RAT2 of 6 is compared against
twice the high byte of a unit vector whose component is 96, so it is a threshold of 6/96 of a unit
vector. The code compared `|x| * 6` against 0.375, which is a threshold of about one degree — so
every target that was not already centred was "out of our sights" and the controls were cancelled.
That is the single reason the autopilot used to do nothing at all when the station was off-axis.

**And the brakes wrapped the speed byte.** `UpdateSpeed` read as:

```csharp
Speed--;
if (Speed == 0) Speed = 1;
```

The original is `DEC DELTA` / `BNE` / `INC DELTA`, so it tests the value *after* the decrement.
Testing it before lets a ship already at rest brake from zero, which wraps the byte to 255 — the
autopilot braked every frame while lining up, so it accelerated to full speed and shot past the
station it was trying to dock with. A test now pins this.

**Where the docking computer stands.** Dead ahead it docks at frame 367. From off to one side it
used to fly away for ever; it now flies to the station, and of six approaches tested, two dock
(including one starting 900 units off to the right) and four reach the station within 240 units and
then collide with it — that is, they arrive but do not get lined up with the slot in time. Closing
that last gap is the next piece of work, and the harness for it is four lines long.

## The docking computer: what the counter magnitude really is

Chasing the last of the off-axis failure turned up a principle that matters well beyond docking, and
it is worth writing down before the next attempt at it.

**A counter is a number of frames, not an angle.** The original's MVEIT part 8 calls MVS5 once when
the counter is non-zero, and MVS5 turns the ship by a *fixed* 1/16 radian whatever the counter says.
The counter is then decremented, so a counter of `m` turns the ship for `m` frames — a total of
`3.58 * m` degrees. `SetCounters` currently reads the magnitude as the angle instead, which is right
for the small counters DOCKIT uses (2, or 0x82) and increasingly too small for large ones: a counter
of 127 should turn nearly three quarters of a circle and our model gives it a quarter. The station's
own roll uses 255, so this is only reachable through the counters rather than through `SpinRoll`,
but it is a real divergence and the fix is to apply MVS5 `m` times rather than to scale one turn.

**Where the off-axis approach actually fails.** With the station 107 units off the slot axis:

- The station's screen offset is `aim.X = 0.0356`, which is `rollA = 3` in the original's units.
- The refine logic only acts when `rollA` or `pitchA` exceeds RAT2, which is **6**.
- So the autopilot commands neither a roll nor a pitch, and the approach is left uncorrected.

That is a genuine deadband in the original's design, and it is why docking dead ahead works (there is
nothing to correct) and why offsets up to about 80 units now work (the refine logic does act on the
way in). Past that the ship arrives with a lateral offset it never removes, and the docking check —
which wants the ship within 120 units of the slot axis — refuses it. The measurements, taken by
sweeping the offset:

| start offset | result |
| --- | --- |
| 0-80 | docks (frame 367 to 1158) |
| 107 | reaches the station, parks 107 units off the axis, collides |
| 150 | docks |
| 200, 300 | reach the station ~103 units off the axis, collide |

So the failure is not a divergence any more, it is a limit: the autopilot flies to the station and
then cannot close the last hundred units of lateral offset. The next thing to try is whether PH1 —
which is meant to fly for the *ideal docking position* out along the slot — should still be active
at that range. `IdealDockingSteps` puts that position 768 units out from the station centre, and the
ship is inside it by then, so PH3 has taken over and PH3 has no way to correct a purely sideways
offset without a yaw control, which the original does not have either.

## The counter scale, measured to the bottom

The last note said a counter is a number of frames rather than an angle, and that `SetCounters`
should therefore turn by `m * 16` angle units instead of `m`. That was tried, and the answer is more
interesting than a straight fix.

**The arithmetic checks out.** One MVS5 step is 1/16 radian, and the world rotation's own unit is
1/(16*256) of a radian, so sixteen world-rotation units make exactly one step. Driving the world
rotation with a counter of `m` read as `m * 16` units reproduced the source's own numbers precisely:
a counter of 1 turned the world 3.55 degrees against the 3.58 the source implies, and a counter of 2
turned it 6.95 against 7.16. That is a much better fit than the `m` we ship, which gives 0.22 degrees
and 0.45.

**But the rate path cannot express it.** `SetCounters` has to go through `FlightSim.RollAngle`, and
that clamps: a rate is a byte, the deviation from its centre of 128 cannot exceed 127, and `RollAngle`
divides by four, so the angle it returns never exceeds **31** — about 6.93 degrees. The measured
turn duly saturates: counters of 2, 4 and 8 all turned the world 6.95 degrees. To turn further the
world rotation would have to be applied once per step, as MVS5 applies it to the orientation vectors,
rather than being asked for one large angle.

**So the shipped scale stays at `m`, and that is a deliberate departure.** It is sixteen times too
small against the source, and it is kept because it is the one that works: with the faithful scaling
the autopilot's fixed ±2 counters become sixteen times more powerful than its control loop expects,
and docking regressed from three successes out of five to one. A docking computer that cannot dock is
worse than a counter scale that is numerically wrong, and the divergence is recorded here rather than
hidden.

The measurements behind that decision:

| counter scale | turn for m = 1 | turn for m = 2 | docking (five approaches) |
| --- | --- | --- | --- |
| `m` (shipped) | 0.22° | 0.45° | 3 dock, 2 reach the station |
| `m * 16` (faithful) | 3.55° | 6.95° | 1 docks, 4 fail |

**What the real fix looks like.** The world rotation needs to accept a turn larger than 31 units —
either by applying MVS4 once per step, or by giving the world rotation the wider angle range MVS5
itself works in — and then the autopilot's gains want re-deriving against the larger step. That is a
piece of work on the flight model rather than on DOCKIT, and it is the next thing to do here.

## MVT6 ignores the sign it is given, and that is why rolls are asymmetric

The hunt for the counter scale ended somewhere much more important: **`Mvt6` does not use bit 7 of
its `A` argument at all**, and the sign it returns instead is wrong for one direction of every
rotation in the game.

`Mvt6` is `(A P+2 P+1) = (x_sign x_hi x_lo) + (A P+2 P+1)`, where `A` carries the sign of the 16-bit
value being added in bit 7 and bits 0-6 are preserved. Probed at byte level with a coordinate of
zero, the committed version returns the same thing whichever sign it is handed:

| coordinate | delta | `A` given | returned sign | bytes |
| --- | --- | --- | --- | --- |
| 0 | −62 | 0x80 | 0x80 | 0x013F |
| 0 | −62 | 0x00 | 0x00 | 0x003E |
| 0 | +62 | 0x80 | 0x80 | 0x013F |
| 0 | +62 | 0x00 | 0x00 | 0x003E |

Two things are wrong there. The sign of `A` makes no difference, and the magnitude for the `0x80`
case is **319 where it should be 62** — the value is not the delta at all.

The visible symptom is that a roll turns further one way than the other. Measured on a target 1000
units to the side, one MVS5 step's worth of world rotation:

| direction | turn |
| --- | --- |
| one way | 18.54° |
| the other | 3.55° |

3.55° is right — it is the 1/16 radian MVS5 applies. 18.54° is five times too much, and it is the
underflow path in `Mvt6`, which is reached only when the angle and the coordinate have opposite
signs. This has been in the code since the beginning and affects **every** world rotation: the
keyboard, the docking computer and every moving ship.

**A correct version was written and had to be pulled.** Computing the subtraction as a single 16-bit
value with a borrow into bit 16, and deriving the sign from which of the two operands is larger,
gives exact arithmetic — the probe above returns 62 for both deltas and 255, 256 and 257 for the
three wrap cases. But the three call sites in `RotateLocationByOurPitchAndRoll` compensate for the
present behaviour with their own `alp2 ^ 0x80` and sign juggling, so fixing `Mvt6` alone flips the
direction of every turn and breaks the missile homing test, the counter direction test and the
docking test together.

**Fixed, in one step rather than three.** The rewrite turned out to need only `Mvt6` itself. The
callers were already passing the sign of the value being added, exactly as the original's do - the
`alp2 ^ 0x80` I had taken for compensation is the original's own pre-complemented `ALP2+1`, which
MVEIT part 5 really does use for that one call and not for the others. With the arithmetic corrected
and the sign convention matched to the source's - bit 7 of `A` is the delta's sign, the return is the
result's sign with `A`'s bits 0-6 preserved - everything downstream agreed at once:

| check | before | after |
| --- | --- | --- |
| `0 + (-62)` | -319 | **-62** |
| `0 + (-255)`, `(-256)`, `(-257)` | 0, 1, 514 | **-255, -256, -257** |
| one step of roll, one way | 18.54° | **3.50°** |
| the same step, the other way | -3.55° | **-3.55°** |

The two directions of a roll are finally **mirrored**, and a step is the 1/16 radian MVS5 applies.

**And the counter scale then worked.** With the rotation symmetric, `StepsPerCounter = 16` - the
faithful scale that had to be pulled two rounds ago - stopped fighting the autopilot and started
helping it. Measured: a counter of 1 turns 3.50 degrees and a counter of 2 turns 6.89, against the
3.58 and 7.16 the source implies. The departure recorded in the previous note is gone, and the test
that pinned it now pins the faithful behaviour instead.

**The docking computer works from every angle tried.** Nine of ten approaches dock, including every
case that used to fly away or park:

| approach | before | now |
| --- | --- | --- |
| dead ahead | docks, frame 367 | docks, frame 367 |
| off to one side (400, 300, 3000) | flew away for ever | **docks, frame 392** |
| the other side (-600, 200, 2500) | flew away for ever | **docks, frame 385** |
| well off to the right (900, 0, 4000) | docks, frame 1695 | **docks, frame 465** |
| far lower right (1500, -900, 5000) | docks, frame 1243 | **docks, frame 542** |
| 107 / 200 / 300 units off the slot axis | parked ~105 off, collided | **docks, frames 368-380** |
| below and close (0, -400, 1800) | collided | **docks, frame 285** |
| far upper left (-1200, 800, 6000) | collided | collides, at a 0.9% margin |

The one that still fails fails at the last moment and by the smallest of margins: its slot faces us
at 0.890 against the 0.8988 (26 degrees) the check wants, because the station is halfway through its
roll as the ship arrives. That is the original's own tolerance being genuinely tight rather than a
control fault, and the approach is otherwise perfect - lateral offset 1 unit at contact. It stays
open as a tuning question rather than a bug.

## The extended system descriptions: seeded, gated, and a hint that pointed past the table

The description generator was already written and wired in, but it was producing the same sentence
for every system and the Data on System screen showed a single stray word. Reading PDESC properly
settled both, and turned up a third thing worth recording.

**PDESC reseeds the generator from the system's own seeds.** Before printing, it copies `QQ15+2` to
`QQ15+5` — s1 and s2 — into `RAND`, "so we get the same extended description for each system every
time we call PDESC". Sharing one running generator instead makes every system read alike, because
each screen advances it to a different point and the phrases are chosen from wherever it happens to
be. `EliteRandom` now has a byte-wise `Reseed`, and the Data screen seeds it from `Seeds.ToBytes()`
offset 2 before each description. Measured, the same seed always gives the same sentence, and the
galaxy now reads like this:

| system | description |
| --- | --- |
| Tibedied | reasonably notable for its funny mountains but ravaged by frequent earthquakes |
| Lave | The planet Lave is reasonably fabled for juice and the edible wasp |
| Riedquat | most fabled for its exciting cat meat but ravaged by a killer disease |
| Leesti | most fabled for ice karate but cursed by dreadful civil war |
| Diso | This planet is a dull world |

**The disc version only shows extended descriptions when docked**, which is a fact about the original
rather than a choice: TT25's own commentary says the extended descriptions are shown in the enhanced
versions "though in the disc version they are only shown when docked, as the PDESC routine isn't
present in the flight code due to memory restrictions". Our Data screen is only reachable while
docked, so that falls out for free — and it is worth knowing, because it means there is nothing to
implement for the in-flight case.

**The stray word was a hint pointing past the end of its table.** Lave was showing "ANCIENT" instead
of a description because `TokenFor(7, 0, true)` returned 26, and there is no token 26 in the disc's
RUTOK. The source explains why: the Lave and Riedquat overrides sit inside

```
IF _6502SP_VERSION
IF _SOURCE_DISC    EQUB 7    \ Lave = Token 26
ELIF _EXECUTIVE    EQUB 7    EQUB 46   \ Lave, Riedquat = tokens 26, 27
```

so they belong to the 6502SP, Executive and Master builds and **not to the BBC disc**, which is why
RUTOK there stops at token 25. The extractor reads the tables without evaluating those guards, so it
picked up 29 hints for 26 tokens. The hint list now keeps only entries whose token the disc actually
has — 25 of them — and the test that asserted 29 has been corrected with the reason. The extractor's
gap is real and is noted below.

## Audit: the extractor's conditional handling

The hint overrun was the visible symptom of a gap in the data extractor, so the gap was worth
mapping properly. There are two extractors and they are not alike:

**The ship extractor has a real assembler.** `tools/EliteDataExtractor/Assembly` evaluates
`IF`/`ELIF`/`ELSE` with expressions, and the ships are assembled from the disc's own
`elite-ships-a.asm` with the build options at `versions/disc/1-source-files/main-sources/
elite-build-options.asm` — `_VERSION=2`, `_VARIANT=2`. That is why the verification can compare
byte-for-byte against `D.MOA`–`D.MOP` and come out with 0 mismatches: it is not a parser at all, it
is the real thing, and the binary comparison would catch any gating error immediately.

**The token extractor is a hand-written parser**, and its conditional handling was uneven:
`ParseTokens` evaluated the guards, and `ParseMtin` did not evaluate them at all — it collected
every `EQUB` in the file regardless of the branch it sat in. That is what handed back 29 hints for a
26-token table. `ParseMtin` now uses the same `Conditional` helper, and the extracted table is 25
hints against 26 tokens, which is exactly right: token 0 is the empty one.

The flags the token parser evaluates against are `_DISC_VERSION` and `_DISC_DOCKED`, which is the
right pair for the docked screens, and the disc's own build options agree: `_VERSION = 2` makes
`_DISC_VERSION` true and `_VARIANT = 2` is the Stairway to Hell disc. Every other platform flag
evaluates false, so the `_6502SP`, `_EXECUTIVE`, `_MASTER`, `_C64`, `_APPLE`, `_NES` and `_ELITE_A`
branches are all excluded — which is the desired behaviour, and was already correct for the token
text itself. Checked against the source: token 113 is empty in the disc because its content is
guarded by `NOT(_NES_VERSION OR _C64_VERSION OR _ELITE_A_DOCKED OR _ELITE_A_6502SP_PARA)`, and the
Elite-A "CARGO VALUE:" text is correctly not ours.

So the exposure was narrower than it looked: the token *text* was being gated properly all along,
and only the numeric tables read by `ParseMtin` were not. Both are now consistent, and the
extraction is deterministic — two runs give a byte-identical file.

## The Constrictor's system was resolved from the wrong seeds

The mission chain was already built end to end — the offers, the accept key, the status byte, the
spawn, the kill, the reward, the plans and their delivery — but the spawn was resolving the
Constrictor's system from the **current system's seeds** where galaxy seeds are needed.

`SceneFactory` set `sim.GalaxySeeds = session.Commander.CurrentSystem.Seeds`. Those are a system's
own seeds, not the galaxy's, and `ConstrictorTarget` feeds its argument straight into
`GenerateGalaxy`. Measured, in galaxy 1:

| seeds used | resolves to | system number |
| --- | --- | --- |
| the galaxy's | Orarra (144, 33) | 193 |
| a system's | Orarra (144, 33) | **93** |

It landed on Orarra either way, which is why nothing looked wrong — but only by coincidence, and the
system number differed because the shortened galaxy has fewer systems. The seeds are now the
galaxy's, taken from `Galaxy.GalaxySeeds(Commander.GalaxyNumber)`.

Two smaller things came out of the same reading. `Missions.IsConstrictorSystem` was passing the
*system's* seeds into `ConstrictorTarget` too, so it was comparing a system against a target
resolved from itself; it now resolves from the galaxy's seeds, which is what its `galaxyNumber`
parameter always meant, and there is a second overload taking galaxy seeds directly for callers that
already hold them. And `FlightSim.SpawnMissionShip` was duplicating the comparison by hand rather
than asking the predicate, so the two could disagree; it asks now.

A test drives the flight simulation rather than the decision — in the Constrictor's system with the
mission running it appears, and elsewhere or with no mission it does not — because the decision
alone was already covered and was not where the fault was.

## The equipment prices were the wrong version's

Seven of the fourteen equipment prices were the **Elite-A** table's, not the BBC disc's. The two
tables agree on the first seven items and on the names of the last two, which is what made it easy
to miss:

| # | item | disc | ours (before) |
| --- | --- | --- | --- |
| 7 | Escape Pod | 1000.0 Cr | 100.0 Cr |
| 8 | Energy Bomb | 900.0 Cr | 90.0 Cr |
| 9 | Energy Unit | 1500.0 Cr | 700.0 Cr |
| 10 | Docking Computer | 1000.0 Cr | 700.0 Cr |
| 11 | Galactic Hyperdrive | 5000.0 Cr | 3000.0 Cr |
| 12 | Extra Military Lasers | 6000.0 Cr | 1900.0 Cr |
| 13 | Extra Mining Lasers | 800.0 Cr | 250.0 Cr |

The disc's PRXS table is the one at `library/common/main/variable/prxs.asm`, and its comments give
the prices directly — the missile's `EQUW 300` is annotated "30.0 Cr", so the table holds tenths of
a credit throughout. The two lasers are a separate block guarded by `_DISC_DOCKED` among others,
which is why the disc has fourteen items where the cassette version has twelve.

Fuel is the one entry that is not a price: PRXS holds a `1` for it because EQSHP works the price out
as **twice the light years**, and its own comment spells out that the doubling is what converts the
figure into the table's tenths ("a tank containing 7.0 light years of fuel would be 14.0 Cr, or a
PRXS value of 140"). Our 2 Cr a light year was already right.

The test that should have caught this was asserting the wrong numbers, in a sample of eight that
happened to include four of the seven. It now pins the whole table by number, name and price, so a
future mix-up with another platform's table cannot pass.

## Auditing the hand-written tables: the market, and one rule it was missing

The equipment prices were wrong because a table transcribed by eye has no protection the way an
extracted one does. The market was the next candidate, so it was checked against its source
item by item.

**The table is exactly right.** All seventeen commodities match the disc's `QQ23` in every field —
base price, economic factor, unit, base quantity and mask:

```
ITEM 19,  -2, 't',   6, %00000001  \  0 = Food
ITEM 65,  -3, 't',   2, %00000111  \  2 = Radioactives
ITEM 53,  15, 't', 192, %00000111  \ 16 = Alien items
```

**Both formulas are right too.** TT151's price is `base + (random AND mask)`, then the economic
factor added or subtracted according to its sign, then multiplied by 4 — which matches
`Market.Price`. GVL's availability is the same shape with `base_quantity`, and its sign rule is
inverted: a positive economic factor *subtracts* the economy's effect, and a negative one adds it.
`Market.Availability` does exactly that, keeps six bits, and floors at zero.

**But one rule was missing.** The original's `var` routine, which both formulas call, does this on
its way past:

```
 LDA #0                 \ Set AVL+16 (availability of alien items) to 0,
 STA AVL+16             \ setting A to 0 in the process
```

So alien items are **never** available to buy, in any system, whatever the economy and the random
byte work out to. They are what a Thargoid leaves behind and can only be scooped — which is why the
original's market screen always shows a dash against them. We were computing their availability like
any other commodity, so they could be bought where the arithmetic happened to give a positive
number. `Market.Build` now zeroes it, with a test that sweeps every economy and a spread of random
bytes, because for some of those combinations the computed figure would otherwise be non-zero.

What is left un-audited in the same style, and is the next thing to check: the combat constants
(laser powers, heat, the OOPS chain) and the spawn probabilities.

## Audit: the combat and spawn constants

The last two rounds' lesson was that a hand-written constant is where another version's value
hides, so the combat and spawn figures were checked the same way. **These were all correct**, and
the reading is worth recording because it also settles some of the reasoning in the code comments.

| constant | value | where it comes from |
| --- | --- | --- |
| Pulse laser power | 15 | `POW = 15` in the disc flight source |
| Beam laser power | 15, bit 7 set | a beam laser is `POW` with bit 7, so 143 raw |
| Military laser power | 23 | `Armlas = INT(128.5 + 1.5*POW)` = 151, masked to 7 bits |
| Mining laser power | 50 | `Mlas = 50` |
| Heat a shot | 8 | LASLI's `LDA GNTMP / ADC #8 / STA GNTMP` |
| Cooling | 1 a frame | the main game loop's `DEC GNTMP`, guarded against zero |
| Overheat | 242 | the flight loop's `CMP #242` before it will fire |
| Energy a shot | 1 | LASLI's `JSR DENGY` |
| Energy bomb | 8 frames | the `BOMB` counter |
| Any spawn | 47% | the disc branch `CMP #120`, against 90 in the other versions |
| Pack of pirates | 61% | `CMP #100` |
| Spawn distance | 38 * 256 | "Set z_hi = 38 (far away)" |
| Spawn delay | 64 | `EV` |

**The OOPS chain is right down to its corner case.** A hit takes the shield on the side the
attacker's `z_sign` names; if the shield is not overwhelmed the remainder comes off the energy
banks; and the death test is the original's `BEQ`/`BCS` pair, which means energy reaching *exactly*
zero is death as well as going below it. `Combat.TakeDamage` reproduces that with `Energy > damage`
rather than `>=`. The shields recharge from the banks only while `ENERGY` has bit 7 set — above half
charge — and each gains one point a frame, which is `SHD` plus `DENGY`.

A test now pins the whole set, so the figures cannot drift to another version's unnoticed the way
seven equipment prices had. Worth noting for the remaining work: the equipment prices and the alien
items rule were the only two faults this audit found in the hand-written tables, and both were in
tables that had a *sibling* table for another platform to be confused with. The combat constants
have no such sibling, which is likely why they were right.

## Witchspace: the mis-jump, which was missing entirely

Witchspace was not implemented at all — no mis-jump, no Thargoid ambush, and no way to see either.
It is a signature part of the game and the disc version has it, so this round added it.

**The trigger.** TT18 rolls a random byte as the jump completes and mis-jumps on 253 or more, which
its own comment gives as a 0.78% chance — three rolls in 256:

```
 JSR DORND              \ Set A and X to random numbers
 CMP #253               \ If A >= 253 (0.78% chance) then jump to MJP to trigger
 BCS MJP                \ a mis-jump into witchspace
```

The disc branch is the one that says `< 253 jumps to the destination`: the same test in the cassette
and advanced versions sits alongside a CTRL-held manual mis-jump, which the disc also has but which
depends on the author-names flag being configured, so the accidental one is the part that matters.

**What MJP does.** It loads the Thargoid blueprints, shows the hyperspace tunnel a second time with
its sound, resets the flight variables, sets its `MJ` flag, and then spawns **four Thargoids, each
with a Thargon in attendance** — the disc's loop is `LDA #3 / CMP MANY+THG / BCS MJP1`, so it keeps
going until there are four, where the Master settles for three. There is no planet and no sun: the
sky is empty but for the Thargoids. The fuel for the jump is spent either way, because the jump did
happen; it simply did not arrive.

**What went in.** `GameSession` gained `InWitchspace`, the `MisjumpThreshold` of 253 with an
`IsMisjump` test, and the roll inside `CompleteHyperspace` — which now leaves the commander where he
was rather than moving him, while still spending the fuel. `FlightScene.ArriveInWitchspace` clears
the bubble, suppresses the station, and spawns the four Thargoids and their Thargons through a new
`FlightSim.SpawnAhead`. Two tests pin the threshold's boundaries and the 3-in-256 chance across the
whole byte range, so the rule cannot drift into an approximation of itself.

Still to do here, and deliberately left for its own round: the tunnel *animation* is played as a
sound rather than drawn, and the manual CTRL mis-jump is not wired to a key.

## The hyperspace tunnel: ported, and not yet on screen

The rings themselves are ported from HFS2 and the reasoning is recorded here so the remaining fault
is a small one to find. LL164 sets the ring step to 4 — "so there are more sections in the rings and
they are quite round", against the launch rings' 8 — and calls HFS2, which draws **eight sets** of
rings. Within a set every ring is twice the radius of the one before, starting at 8 to 15 depending
on the set, and a set stops once a ring's radius reaches 160:

```
.HFL1  LDA XX4 / AND #7 / CLC / ADC #8 / STA K   \ the starting radius, 8 to 15
.HFL2  ... draw a circle of radius K
       ASL K                                     \ double it
       BCS HF8                                   \ off-screen, so stop
       LDA K / CMP #160 / BCC HFL2               \ past 160, so stop
```

The rings are all on screen together — a tunnel seen down its length rather than an animation of one
ring — so they are drawn every frame for as long as the countdown lasts.

`HyperspaceTunnel` implements that shape in the view's own units, `--jump` starts a jump from the
command line so the effect can be looked at without flying to the charts first, and the flight scene
draws the tunnel in place of the space view while the countdown runs.

**It appears now, and the fault was the measurement rather than the drawing.** The rings were being
drawn correctly all along; what was wrong was *when the screenshot was taken*. Several rounds of
markers showed the pattern clearly:

* three coloured bands drawn unconditionally all appeared in the saved frame, so the sprite batch
  reaches the back buffer;
* a marker drawn inside the countdown branch did **not** appear;
* forcing the branch's condition to `true` made that same marker appear.

So the condition was false at the frame the screenshot was taken on. `HyperspaceCountdown` is in
simulation steps, and the shell takes up to ten simulation steps for each frame it draws, so a
twenty-step countdown begun at frame zero is long over by frame six — and every marker placed
*inside* the branch vanished for that reason. The drawing code was never at fault. Earlier rounds
had traced the countdown as "running" and stopped there, which is exactly the kind of measurement
that confirms the wrong thing.

The fix for the harness is `FlightScene.ShowTunnelFrames`, which holds the tunnel open for a given
number of *drawn* frames, with `--jump` setting it. The tunnel is now visible in
`screenshots/hyperspace.png`, and it looks like the original's: concentric rings crowded at the
centre and opening out to the edge of the view.

## The manual mis-jump, and what witchspace holds

The CTRL route into witchspace is wired up: holding CTRL as well as the jump key forces the jump to
go wrong, ahead of the random roll, exactly as TT18 reaches MJP by two routes. The original gates
that key behind the author-names flag so that its competition code would record whether the feature
had been used — an anti-cheat measure for a 1984 contest rather than a game rule — so the remake
simply offers the key.

The ambush moved from the game layer into `FlightSim.ArriveInWitchspace`, which is where it belongs
and which makes it testable. A test now checks what witchspace actually holds: four Thargoids, four
Thargons, nothing else, no planet and no sun, and whatever was in the bubble beforehand cleared away.

One fault was found in the process, and it was in the test rather than the code: `ForceMisjump` is
set before the jump begins and consumed when the countdown ends, so clearing it part-way through the
countdown — which the first version of the test did — makes the jump ordinary again. The flag is now
asserted to be a one-shot, so the next jump is ordinary unless asked otherwise.

**On method.** This round spent several cycles on a test that failed for a reason the code was right
about, and the thing that resolved it was printing the state at both ends of the countdown rather
than reasoning about it. That is the third time this session that the fastest route out of a
confusing failure was to make the program say what it was doing, and it remains worth doing first
rather than last.

## Rebinding: the last piece of the agreed scope

The brief asked for a gamepad and an authentic keyboard layout; the layout was in and rebinding was
not. It is now, and it is deliberately kept apart from the original's own screens.

**Nothing here is a port.** The original's keys are fixed — IBM and Acornsoft chose them and there
was no way to change them — so this is a modern addition. What it does do is default to the
original's own keys, so a player who never opens the screen gets the 1984 layout: fire on `A`,
missile on `M`, target on `T`, E.C.M. on `E`, the bomb on TAB, and the four flight controls on the
keys the original gives them, `,` `.` `X` `S` with SPACE and `?` for the throttle.

**The screen is reached from the status screen rather than taking a red function key**, so the
original's own screens keep the keys they had. It walks the twelve bindings with up and down, rebinds
on ENTER, resets to the originals on `R`, saves on CTRL-S, and leaves on ESCAPE. Escape during a
rebind cancels it rather than binding Escape, which a player would otherwise do by accident and then
be stuck with.

**Bindings are held as key names and saved as JSON**, alongside the commander file, so the file is
readable and a binding survives a key being added to MonoGame's enum. A settings file is never worth
failing over: anything unreadable gives the defaults back, and a binding that names a key this build
does not have falls back per binding rather than taking the others down with it.

The arrow keys and the gamepad stay alongside the bound keys rather than being rebindable, because
the original has two keys for each of the four directions and the arrows are one of them — rebinding
the other should not take the arrows away.

## Two faults in the settings screen, both found by looking at it

**The rebinding would not have worked.** `SceneFactory.CreateFlightScene` loaded its own
`Settings`, and `EliteGame` loaded another for the settings screen, so a rebind changed the screen's
copy and left the flight scene reading the old one — which looks exactly like rebinding silently
doing nothing, and would have been an awkward thing to diagnose from the symptom. There is one
instance now, owned by the shell and passed to both. The rule this is an instance of: a settings
object is shared state, and loading it twice is the bug rather than a detail.

**The screen ran off the top of a small window.** The list began at a fixed offset below the middle
of the view, so at 640x480 the title and the first binding were above the top edge. It is laid out
from the view now, scaled to fit whatever height it has.

That second fix took two passes, and the first was wrong in an instructive way: the scale was chosen
from a line height, and the glyphs then did not fit inside the line height that had been chosen. The
line height now follows from the scale — `CellHeight(scale)` plus a little leading — rather than the
other way round. Both were caught by rendering the screen at 640x480 and looking at it, which is the
only way either would have been noticed: neither is a fault any test would reach for.

## A sweep of every docked screen at a small window

The settings screen had two faults that only showed at 640x480 — it ran off the top, and then its
rows overlapped — so every other docked screen was rendered at that size and looked at. Seven of
them: the market, the equipment shop, the status and inventory, Data on System, both charts, and the
settings screen itself.

**They were all laid out soundly.** The heading, the data and the footer sit inside the view at
640x480 on every one, and the short-range chart's fuel circle scales with the window as it should.

**One fault was shared.** Every screen's footer line of key hints ran off the right edge, because
this remake's hints are longer than the original's — the market's ran to eighty-four characters,
which cannot be drawn legibly at 640 pixels whatever scale is chosen, so a scaling helper alone
would have shrunk it to unreadable rather than fixing it. Two things were done instead: the hints
are now as terse as the original's own ("F1-F3 SCREENS" rather than naming each screen), and
`TextRenderer.DrawHintLine` scales a hint down if it still overruns. The terse text is what actually
fixes it; the helper is there so a long hint in future cannot silently clip.

The `O CONTROLS` hint on the status screen was also missing from its footer, which is how the
settings screen is reached, so that was added while the line was being rewritten.

## Every ship model rendered and looked at

The docked screens were swept at a small window size and one fault turned up, so the 3D models were
swept the same way: all **31 blueprints** rendered in the ship viewer at a fixed heading and pitch,
laid out as a contact sheet, and looked at.

**There are no faults to report.** Every blueprint draws as a solid body; none is blank, inverted or
collapsed, and the sizes vary the way the blueprints do — the missile and the splinter are slivers,
the Coriolis and the Dodo are the two big polyhedra, the Thargoid and the Thargon are recognisably
themselves. The viewer frames each ship by scaling its distance to the model's radius, so the sheet is
comparing shapes rather than sizes, which is what makes a collapsed or inverted face stand out.

This is a weak form of check — a contact sheet at 320x240 cannot judge fine geometry — but it is the
check that would catch a blueprint whose faces had stopped reconstructing, which is the failure the
mesh builder is most exposed to and the one no unit test covers.

## The hint-fitting rule moved to the core, and got tests

The scaling rule behind last round's footer fix was arithmetic in the renderer, where nothing could
reach it. It is `HintLine.Fit` in the core now, with four tests: a hint that fits is left alone, one
that does not is scaled to the largest size that does, one that cannot fit at any scale stays at one
rather than vanishing, and degenerate inputs do not divide by zero.

The floor of one is the interesting part and is now recorded where the rule lives: a line too small
to read is *worse* than one that is cut off, because a cut-off line can still be recognised. Keeping
the floor means the real answer to a hint that does not fit is to write a shorter hint, which is what
the screens now do — the helper exists so that a future long hint cannot clip silently, not so that
hints can be as long as they like.

## The charts were showing a galaxy that does not exist

Flying the whole loop end to end — dock, buy, launch, jump, arrive, dock, sell — turned up a bug
that no test covered and that the tests could not have covered, because the one case they exercised
is the one case where it does not show.

**A system's seeds are not the galaxy's.** `Galaxy.GenerateGalaxy` takes *galaxy* seeds, and the
callers were mostly handing it a *system's*. Measured:

| seeds used | systems differing from the real galaxy |
| --- | --- |
| system 0's | **0 of 256** |
| system 7's (Lave) | **256 of 256** |
| system 100's | 256 of 256 |
| system 255's | 256 of 256 |

System 0's seeds *are* the galaxy's own, which is why this hid for so long: at Lave — system 7 of
galaxy 0 — the chart was asking with Lave's seeds, but every chart screenshot the session had taken
was of system 0, where the two coincide. The short-range chart centre was right and the
surrounding systems happened to look plausible, so nothing showed.

Once the commander has jumped anywhere, all three of these were wrong: the **short and long-range
charts** drew a galaxy generated from the current system's seeds, and the **nearest-reachable-system
search** — which is what `H` jumps to — picked its destination from that same invented galaxy.

The fix is one property, `GameSession.SystemsInGalaxy`, which resolves the galaxy from the
commander's galaxy number, and the three call sites now ask it rather than generating their own. The
name is deliberately not `Galaxy`, which would shadow the `Galaxy` class inside the session and did
exactly that when it was first written.

Two tests pin it, on the session rather than on the generator, because the session is where the
mistake was made: from four different systems the session must report the same 256 systems, and from
Lave it must report the real Lave at (20, 173) with Diso and Leesti alongside.

**On method.** This is the third fault this session found by flying the game rather than by reading
it or testing it — after the station's invisible docking slot and the docking computer's off-axis
approach. A test suite checks what it was written to check; a flight checks what a player does.

## The galactic hyperdrive was rotating the wrong seeds

Following the last round's find — a system's seeds are not the galaxy's — the same confusion turned
out to be in the galactic jump, in a worse form.

**The original's GHY** rotates the **galaxy's** seed table (`QQ21`) one bit to the left, raises the
galaxy number, and then finds the **nearest system to (96, 96)**, which is where it always arrives.
Ours rotated the *current system's* seeds and kept the *same system number*. Rotating a system's own
seeds is not a related operation at all, so the system it landed on was arbitrary — and the nearest
system to (96, 96) is very rarely the one a system number names.

Measured after the fix, jumping from Lave:

| jump | arrives at | galaxy |
| --- | --- | --- |
| 1 | ORORRA (96, 95) | 2 |
| 2 | XEINES (90, 111) | 3 |
| 3 | SOENISTI (99, 103) | 4 |

Orarra at (96, 95) is the nearest system to the arrival point in galaxy 2, which is what the
original does.

**Two tests were pinning the bug**, and this is the third time this session that has happened. One
asserted "we arrive at the same system number in the new galaxy" — which was the fault, written up
as the specification. The other asserted that eight jumps come back to the starting *system*; the
seeds do come back after eight bits, but every jump lands at (96, 96), so the commander ends up at
the system the first jump would have taken him to had he begun in galaxy 0, and should not end up
back at Lave at all.

**A verification script of mine was also wrong**, and worth noting because it nearly sent me after
the wrong thing: checking the mission hints against the names in the source's RUPLA comments, I had
Arredi and Anreer in the wrong galaxy and read the mismatch as a data fault. They are in galaxy 2,
as the table says and as the extracted criteria say. The hints were right; the check was not.

## A galactic jump left the old galaxy's sky in place

Following the galaxy thread after the hyperdrive fix: flying a session through a galactic jump and
then an ordinary one worked as far as the numbers went — the new system, the market, the next jump
all correct — but the *sky* was not rebuilt. `UseGalacticHyperdrive` changes the session's system and
nothing else, so the planet, the sun and the space station of the galaxy we had just left stayed
where they were. Jumping galaxies and finding the same station outside is the sort of thing that
would be noticed immediately by a player and by nothing else: no test covered the transition, and
every screenshot of a galactic jump showed a system that was, on paper, correct.

The status screen is where the drive is fired, so it now rebuilds the flight scene's system and
bubble when the jump succeeds. The flight scene's `ResetBubble` became public for it, which is the
honest shape: the status screen is a docked screen and the sky belongs to the flight scene, so the
one has to tell the other.

**And a false alarm, recorded because it nearly went the other way.** The same probe printed that the
chart showed galaxy 1 after a jump to galaxy 2, which looked like the previous round's bug returning.
It was my comment that was wrong: `GalaxyNumber` is zero-based, so 1 *is* the second galaxy, and its
systems begin at Ausis with Orarra at index 193 — exactly where RUPLA puts it. Checked before
changing anything, which is the lesson from the round before, where a wrong verification script
nearly sent me after correct data.

## Redocking rerolled the market, which is a way to shop for a deal

Turning the last two rounds' habit — fly the game, then write down what flying found — into tests
paid for itself immediately. The tests are `GameLoopTests`: a commander who docks, trades, jumps and
docks again; one who changes galaxy and checks that the galaxy number, system, market and chart all
move together; eight galactic jumps; and that the jump and the chart agree about what is reachable.

**Writing them found a fault on the first run.** Docking rebuilt the market. The original's `GVL` has
**exactly one caller in the whole source** — `tt18.asm`, the arrival after a hyperspace jump — and the
market screen (`TT210`) prices its items from what GVL left behind rather than recalculating. So the
market is fixed for as long as you are in the system, and our rebuilding it on docking gave a
commander a way to reroll the prices by launching and redocking until a good deal came up. Nothing the
original offers.

**And a test was asserting the behaviour.** `DockingChangesTheMarketAndLaunchingKeepsIt` did exactly
what its name says, and passed, because the code did too. It is now
`DockingKeepsTheMarketAndSoDoesRedocking`, asserting the original's behaviour with the reason in its
remarks. That is the fourth test this session found to be encoding a fault rather than catching one —
after the equipment prices, the galactic hyperdrive's system number, and the eight-jump round trip.

The pattern in those four is worth noting: each was written from the code as it stood rather than from
the source, which is exactly what a test is supposed to prevent. Tests written *after* reading the
source catch things; tests written *from* the implementation only pin it.

## Selling restocked the market, which the disc version never does

Continuing the audit into trading, with the test suite's own habit in mind.

**The original's availability table has one writer.** Every reference to `AVL` in the entire source
library, checked rather than remembered:

| instruction | where | what it does |
| --- | --- | --- |
| `STA AVL,Y` | `tt219` | subtracts what you buy |
| `STA AVL+16` | `var` | zeroes the alien items on the way past |
| `LDA AVL,Y` | `tt219`, `tt151` | reads it for the buy limit and the price |

The only code anywhere that *adds* to availability is in `nes/main/subroutine/buyandsellcargo.asm`,
the NES version's own sell path. The **BBC disc never adds to it**, so what you sell is gone: it does
not come back on the market for you or anyone else to buy, and the cash is the whole of what you get
for it.

Ours added it back. That is not just a small divergence — it lets a commander buy a system's stock
out, sell it back, and repeat, and it means a market's availability drifts upwards the more you
trade in it. Both are things the original's trading does not do.

**And a fifth test was asserting it.** `SellingReturnsCreditsAndRestocksTheMarket` did exactly what
its name says, and passed, because the code did too. It is now
`SellingReturnsCreditsAndDoesNotRestockTheMarket`, and it also pins what the availability is *after*
buying, which nothing had checked.

**The tally, and what it means.** Five tests this session have been found encoding a fault rather
than catching one: the equipment prices, the galactic hyperdrive's system number, the eight-jump
round trip, docking's market rebuild, and now selling's restock. Every one was written from the code
as it stood. The tests that have caught things — the counter directions, the mis-jump threshold, the
alien items rule — were all written *after* reading the source, and several of them were written to
pin a measurement rather than an implementation. The distinction is not diligence, it is order:
read the source, then write the test.

## The escape pod left the commander stranded

Auditing the death path against the original's `ESCAPE` routine, which does four things before it
hands us to the station:

```
 STA QQ20,X             \ Set the X-th byte of QQ20 to zero, so we no longer have any of item X
 DEX / BPL ESL2         \ ... for all seventeen slots
 STA FIST               \ Launching an escape pod also clears our criminal record
 STA ESCP               \ The escape pod is a one-use item, so set ESCP to 0
 LDA #70 / STA QQ14     \ Our replacement ship is delivered with a full tank of fuel
```

Ours emptied the hold and spent the pod, and did neither of the other two.

**The fuel is the one that matters.** Being picked up with an empty tank and no cargo to sell leaves
a commander with no way to earn: he cannot jump anywhere and has nothing to trade, which is a dead
end the original's rescue deliberately avoids by delivering the replacement ship full. Clearing the
criminal record is the other half of "a fresh start", and it is what stops the station he is rescued
at from being hostile to him.

Two tests cover it now — one that the rescue empties the hold, clears the record, spends the pod and
fills the tank, and one that dying without a pod is game over — and neither existed before, which is
why nothing had noticed.

**The tally is now six.** Every one of these was found by reading the source against code that
already passed its tests: the equipment prices, the galactic hyperdrive's system number, the
eight-jump round trip, docking's market rebuild, selling's restock, and now the escape pod's missing
rescue. The tests that catch things are the ones written after reading the source; the ones that
hide things were written from the code.

## The large cargo bay was two tonnes short

Continuing the source-against-code audit into the equipment's effects. `EQSHP` sets `CRGO` to **37**
for a large cargo bay, and says so in its own comment — "we just scored ourselves a large cargo bay,
so update our current cargo capacity in CRGO to 37". Ours set 35, and checked for 35 when deciding
whether one was already fitted, so the hold was two tonnes smaller than the original's and a second
bay would have been sellable to a commander who already had one at the true size.

The starting capacity of 22 is right: the commander template holds `EQUB 22 + (15 AND Q%)`, and `Q%`
is zero for the Cobra. So a large bay is worth fifteen tonnes, not thirteen.

**Checked at the same time and found correct**, which is worth recording because a clean result is
still a result:

| effect | the original | ours |
| --- | --- | --- |
| Large cargo bay | `CRGO` = 37 | now 37 |
| Missiles | four to a rack (`LDA #2` on the menu item when fewer than four) | four |
| Energy unit | sets `ENGY` to 1, and the banks recharge at `ENGY + 1`, so it doubles the *rate* | doubles the rate |
| Starting credits | 100.0 Cr | 100.0 Cr |
| Starting fuel | a full tank, 7.0 light years | 70 tenths |

The energy unit is the interesting one, because the obvious guess is that it raises a maximum energy
figure. It does not: it doubles the recharge rate, and the blueprint's own energy is the ceiling.

**The tally is seven.** Six of the seven had a test asserting the wrong behaviour; this one had a test
asserting `35`, which is the same fault in the same place — written from the code, not the source.

## The stock gate is a jump, not a cap

`EQSHP` decides how much equipment a system sells by adding three to the tech level and then
comparing against `#12`:

```
LDA tek / CLC / ADC #3   \ A is now 3 to 17
CMP #12                  \ If A >= 12 then set A = 14
BCC P%+4
LDA #14
```

That is a **jump**, not a cap. A tech level of 9 stocks fourteen items rather than twelve, and 10
stocks fourteen rather than thirteen — the two levels before the cap stock the whole list, lasers
included. Ours clamped smoothly, so it agreed everywhere from tech 0 to 8 and disagreed at exactly
9 and 10:

| tech level | the original stocks | ours stocked |
| --- | --- | --- |
| 0-8 | tech + 3 | the same |
| **9** | **14** | 12 |
| **10** | **14** | 13 |
| 11-14 | 14 | 14 |

Nine of the fifteen levels agreeing is what made a clamp look right, and it is the same shape as the
errors before it: a smooth approximation of something the original does in steps.

A test now walks every tech level, checking the count and that the gate agrees with it — the nth item
stocked and the one after not — and pins the step itself by checking that tech 9 sells military and
mining lasers where tech 8 does not.

**The tally is eight**, and this one had no test at all: the stock gate was exercised incidentally by
tests that happened to use tech levels 4 and 8, both of which agree.

## The mission hints' "always" flag is not "any galaxy"

PDESC's criteria test has an order, and the order is the whole of it:

```
LDA RUGAL-1,Y / AND #%01111111   \ bits 0-6 are the galaxy
CMP GCNT / BNE PD2               \ and it must be the galaxy we are in
LDA RUGAL-1,Y / BMI PD3          \ bit 7 set means print it without more ado
...                              \ otherwise mission 1 must be in progress
```

So bit 7 means **"no mission needed yet"**, not **"any galaxy"**. Ours read it as a shortcut past the
galaxy test and returned the hint before checking the galaxy at all.

Three hints carry the bit, and the fault showed on two of them:

| hint | system | galaxy | ours showed it |
| --- | --- | --- | --- |
| Teorge | 211 | 0 | in all eight galaxies |
| Arredi | 100 | **2** | in all eight |
| Anreer | 41 | **2** | in all eight |

Teorge is in the first galaxy, which is where a commander starts, so a test checking it there passes
either way. Arredi and Anreer are in the *third* galaxy and need no mission — so they were appearing
in every galaxy a commander visited, at systems 100 and 41, which in most galaxies are ordinary
systems with nothing to do with the Constrictor.

The test now checks each of the three in its own galaxy and in a galaxy it does not belong to, and
that the mission's own trail still requires the mission.

**The tally is nine.** This one, like the stock gate before it, had no test that went near the
disagreement: the hint tests covered the trail systems, which all need the mission, and the one
always-bit hint they covered was Teorge in galaxy 0.

## The mission reward was paid at the kill instead of the debriefing

Reading the mission chain end to end against the source turned up two things wrong with the
Constrictor's completion, and one thing right.

**The reward and the kill points belong to the debrief, not the kill.** `KILLSHP` does one thing when
the Constrictor dies — it sets bit 1 of `TP` to mark mission 1 as successfully completed. Everything
else happens in `DEBRIEF`, the next time a debriefing is attended at a station:

```
.DEBRIEF
 LSR TP / ASL TP        \ Clear bit 0 of TP: mission 1 is no longer in progress
 INC TALLY+1            \ Award 256 kill points for completing the mission
 LDX #LO(50000)         \ Increase our cash reserves by the generous mission
 LDY #HI(50000)         \ reward of 5,000 CR
 JSR MCASH
 LDA #15                \ print extended token 15, the thank you message
```

Ours paid the 5,000 credits at the kill and **never awarded the 256 kill points at all**. The points
are the more interesting half: 256 is exactly the threshold that makes a commander Competent, so the
debrief is what qualifies him for the *next* mission's offer — which means the mission chain could
not have advanced correctly for a commander who was not already Competent by other means.

It also explains the status byte: `KILLSHP` only sets bit 1, so between the kill and the debriefing
the missions read `%11` — objective done, mission still in progress. That is the state the original
sits in, and ours skipped straight to `%10`.

**Checked and found correct while reading it:** the reward is 50,000 **tenths**, which is the 5,000.0
Cr the comment gives, so the constant's scale was right; and the combat rating thresholds match the
documented table exactly — 8, 16, 32, 64, 128, 256, 512, 2560, 6400.

**The tally is ten.** Two tests asserted the old behaviour and now assert the original's, and one of
them — `TheStatusByteIsTheOriginals` — was already checking the `%10` state that the code could not
actually reach.

## Mission 2 could never be completed, at either end

Reading the mission 2 chain against `DOENTRY` found both of its systems wrong, in a way that made the
mission impossible rather than merely inaccurate.

**The plans are not where the Constrictor was.** `DOENTRY` checks the galaxy and then the
coordinates:

```
LDA GCNT / CMP #2 / BNE EN4      \ the third galaxy, and nowhere else
LDA QQ0 / CMP #215 / BNE EN4     \ and the system at (215, 84)
LDA QQ1 / CMP #84 / BNE EN4
```

That is **Ceerdi, index 83 of the third galaxy**. The code reused the Constrictor's system instead,
which is **Orarra, index 193 of the second galaxy** — a different galaxy and a different system, so
the pickup condition could never hold anywhere at all.

**The delivery is not Lave.** `DEBRIEF2`'s own test wants **(63, 72) in the third galaxy**, which is
**Birera, index 36** — the system the mission text names, in the brief ("the plans we need to take to
Birera") and in the Thargoid intercept token that names it too. The code delivered to Lave, in the
first galaxy, so the delivery could never be made either.

Between them the whole of mission 2 was unreachable: the plans could not be collected and, had they
been, could not have been handed over. The chain now runs correctly — status byte `6` on the way,
`10` carrying the plans, `2` once delivered — and the Thargoid intercept chance rises to 56/256 while
carrying them.

**The tally is eleven.** And this one is worth naming for what it was: not a subtle divergence but a
feature that could not be used. The tests covered `PickUpPlans` and `DeliverPlans` as functions — they
took a system and returned a bool, and did so correctly — but every test passed them the *Constrictor's*
system with the Constrictor's galaxy, so the predicate was never asked about the place it was for.

## The whole mission trail, mapped and checked

After two rounds of finding mission faults, the entire trail was mapped from the data and checked
against the source rather than spot-checked. Every one of the twenty-five hints, resolved to the
system it actually lands on:

| token | system | galaxy | what it is |
| --- | --- | --- | --- |
| 1 | Teorge, 211 | 0 | mission 1's first hint, unconditional in its galaxy |
| 2-4 | Xeer 150, Reesdice 36, Arexe 28 | 0 | the rest of the first galaxy's trail |
| 5-22 | from 253 down to 5 | 1 | the second galaxy's trail |
| 23 | **Xeveon, 101** | 2 | the Thargoid intercept |
| 24 | Orarra, 193 | 1 | the Constrictor |
| 25 | Anreer, 41 | 2 | unconditional in its galaxy |

Everything agrees with the source: the four systems in the first galaxy, the eighteen in the second,
the Constrictor at Orarra in the second galaxy, and the plans and delivery at Ceerdi and Birera in
the third — with the intercept token at a *different* system in the same galaxy, Xeveon at (198,205),
which is the one thing on the trail that had to be checked rather than inferred, because "the third
galaxy" would have been the natural guess for it too.

**Checked and found correct at the same time:** the mission's own gating (`CompetentKills` 256,
galaxies 0 and 1 for the offer), the Thargoid intercept chance of 56/256 while carrying the plans,
the delivery reward of 10,000 tenths, and that the data screen asks for the hint at the system the
commander is docked at rather than the one the charts point at.

## Loading a commander threw his missions away

Auditing the save file: what the original's commander block holds, and what ours carries.

**The original writes a 75-byte block** starting at `NA%`, with two checksum bytes at the end —
`CHK` holding the checksum and `CHK2` holding it `EOR #&A9`, so a file that has been tampered with
can be spotted in two places. Ours is JSON with a format version and no checksum, which is a
deliberate departure: a text file a person can read and fix is worth more in a remake than tamper
detection on a single-player game. Every field the original's block holds is carried.

**But nothing read the mission bits back.** `CommanderSave` carries `MissionStatus` and writes it on
every save; the constructor rebuilds the missions from it; and `Load` — the path that runs when a save
is read — rebuilt the commander, the flight model and the market, and left the missions empty. So a
commander who saved while carrying the plans, or partway through hunting the Constrictor, loaded with
no mission at all: the offer would come round again from the start, `OfferMission2` would refuse, and
the four bits that a save had been writing all along were never once read.

One line fixes it, and a test loads a commander mid-mission-2 through `Load` and checks all four bits
come back — mission 1 complete, not in progress, mission 2 no longer in progress, plans carried.

**Setting that test up was itself instructive.** Three attempts got the mission state wrong before
the test was right, and each mistake was the same shape: *building the state by hand instead of
letting the methods build it*. The first set `CarryingPlans` directly, which left the mission-2 bit
set and produced `%1110` where the real game produces `%1010`; the second tried to fix that by
docking, which rebuilds the missions from the status byte and discarded the setup entirely. Going
through `PickUpPlans` at Ceerdi — the real method at the real system — gave the right state first
time. The production code has the same lesson in it: `PickUpPlans` clears the mission-2 bit, and
setting the field directly does not.

**The tally is twelve.**

## Becoming wanted: the threshold is 50, and one cop is enough

Auditing the legal status against STATUS and the flight loop turned up three things, two of which are
now fixed.

**The threshold was wrong.** STATUS prints "Clean" for 0 and then chooses on `CPY #50`: 1 to 49 is an
offender, 50 and above is a fugitive. Ours used 24 — and its own mapping was dead in the middle, with
`< 24 => "Offender"` and `< 48 => "Fugitive"` both followed by `_ => "Fugitive"`, so 24 to 47 and 48
and up gave the same answer. A commander became a fugitive at half the legal status the original
requires.

**The increase was wrong.** Ours added a flat 4 per innocent killed. The original's rule is sharper:

```
 LDA NEWB / AND #%01000000   \ bit 6 is set if this ship is a cop
 ORA FIST / STA FIST         \ so killing one sets the highest clear bit: at least 64
 ...
 LDA #%10000000 ... ADC FIST \ otherwise this adds exactly 1
 BCS KS1S                    \ and an overflow means we are too bad to get any worse
```

So an ordinary innocent is **one** point — fifty of them to become a fugitive by degrees — while a
single cop makes us a fugitive outright by setting the status to at least 64. Ours made every kill
worth four, which is both too fast for traders and too slow for police, and it had no notion of a cop
at all.

**One part is approximated, and is recorded as open.** The original reads the cop flag from bit 6 of
the ship's `NEWB` flags, which come from the `E%` table that only the docked segment can reach. We
have no `NEWB` in the ship data and no `E%` extraction, so the cop is identified by type instead, and
only the Viper — the ship the table marks as a cop and the one the station sends after us. The `E%`
table is nine bytes and marking every type would be the faithful fix; the table's indexing needs care
because it is not a simple one-byte-per-type list, so it wants its own round rather than a rushed one.

**The tally is thirteen**, and the test that pinned the old threshold — `ShootingInnocentsMakesUsWanted`
— now checks the boundary at 49 and 50 exactly.

## A criminal record was permanent

The other half of the legal status, and the half that was missing entirely.

**SOLAR halves it on every arrival.** `LSR FIST`, with the source's own explanation: "halving our
legal status in FIST, making us less bad... so every time we arrive in a new system, our legal status
improves a bit". Bit 0 is shifted out, so a status of 1 becomes 0 rather than staying at 1 — a
commander with one black mark is clean again after a single jump.

Ours never decayed at all, which makes a record **permanent**: a commander who becomes a fugitive once
could never be clean again, however far he flew and however long he behaved. Combined with last
round's fix to the rate, the whole arc is now the original's — one point an innocent, fifty to be a
fugitive, one cop to be a fugitive outright, and half of it back for every jump away.

## The E% table: what I found, and why it is still open

The cop flag comes from bit 6 of a ship's `NEWB` flags, and the defaults for those are the `E%` table
in the disc's docked segment, which we do not extract. I set out to close that and did not finish, so
here is exactly where it stands rather than a guess.

**What is certain.** `E%` holds the flags; the loader copies it with `LDA E%,X / STA ROM_E%,X`, so it
is indexed by the same number the blueprint slot is. The nine ships it names, in order, are the
Missile, Cargo canister, Shuttle, Transporter, Cobra Mk III, Python, Viper, Krait and Constrictor, and
seven of the nine bytes have their high bit set.

**What does not add up.** The named entries sit at 0, 4, 8, 9, 10, 11, 15, 18 and 30 — the first few
spaced four apart and the rest not — which is not a one-byte-per-ship-type table, and the counts do
not reconcile: our data has the Viper at type 16 where the table's Viper byte is at 15, and the
Shuttle at 9 where the table's is at 8. Reading the labels as belonging to the row they are printed on
gives the Shuttle at 9 and the Transporter at 10, which matches ours; reading the values as belonging
to a four-byte stride gives the Viper at 16, which also matches ours. The two readings agree with our
data on different entries, so they cannot both be right, and picking one to make the numbers line up
would be exactly the kind of reasoning that has produced the last thirteen faults.

**Settled, and the answer was where the bytes are.** The table is read out of the assembled docked
code by the extractor into `data/ships.json`, and where it starts was decided by the ship registry
rather than by reading addresses out of the listing: trying every candidate offset and asking which
one puts the source's own flag values on the type numbers `xx21.asm` registers them at gives
**exactly one answer**, at 0x445A — the Shuttle at 9, Transporter at 10, Cobra Mk III at 11, Python at
12, Viper at 16, Krait at 19 and Constrictor at 31 all landing together, which no neighbouring offset
does. It also agrees with the disc's own symbol for the Viper, `COPS`.

**The cops are the Transporter and the Viper**, and the game now tests that list. The earlier
single-Viper approximation was right about the ship that comes after you and wrong about the other.

**What took the time is worth recording.** The listing's labels really are offset from its values —
the row that says "Transporter" carries the Shuttle's flags — so reading the labels as authoritative
gives one answer and reading the values gives another, and the two agreed with our type numbers on
different entries. Neither reading is trustworthy; the registry is. The lesson is the same one the
faults have been teaching all session: when two sources disagree, find a third that can arbitrate
rather than choosing the one that fits.

## The E% flags now drive the legal status, as they do in the original

The table extracted last round is wired in. `Ship` carries its `NewbFlags`, the game stamps each ship
with its type's flags from the disc's `E%` table as it spawns, and the flight loop tests **bit 6** for
a cop and **bit 5** for an innocent — which is exactly the original's rule, replacing the
`AiFlag < 0x80` heuristic that had been standing in for it.

Read back from the extracted data, the flags make sense of ships we already knew:

| type | ship | flags | meaning |
| --- | --- | --- | --- |
| 9 | Shuttle | `0x21` | trader, innocent |
| 10 | **Transporter** | `0x61` | trader, innocent, **cop** |
| 11 | Cobra Mk III | `0xA0` | innocent, escape pod |
| 12 | Python | `0xA0` | innocent, escape pod |
| 16 | **Viper** | `0xC2` | bounty hunter, **cop**, escape pod |
| 19 | Krait | `0x8C` | hostile, pirate, escape pod |
| 31 | Constrictor | `0x8C` | hostile, pirate, escape pod |

The Krait and the Constrictor being hostile pirates with **no** innocent bit is the part that matters:
under the old heuristic a hostile ship was one we could shoot with impunity, which happened to be
right, but the reason was wrong and the rule did not distinguish a hostile pirate from a hostile cop.
Now a Viper that comes after us and a Krait that does are treated differently, as they should be.

**A test covers both halves**: one cop makes us a fugitive at once and an innocent is worth one point,
and a hostile ship with neither flag changes nothing however hostile it is.

## Disc bounty hunters carry no E.C.M.

Following the `E%` flags into the spawner turned up a fault of a kind that has recurred all session:
**a rule implemented from the wrong version.**

The 22% chance of a lone bounty hunter carrying E.C.M. is guarded by

```
IF _CASSETTE_VERSION OR _DEMO_VERSION OR _ELECTRON_VERSION OR _6502SP_VERSION OR _C64_VERSION
   OR _APPLE_VERSION OR _MASTER_VERSION OR _NES_VERSION
```

— every version **except this one**, and the source says so in as many words: *"Lone bounty hunters
in the disc version don't have E.C.M., while in the other versions they have a 22% chance of having
E.C.M."* Ours had the chance. An E.C.M. on a bounty hunter is not a small thing either: it is a
second health bar that shrugs off missiles.

**The guard is the lesson.** The line that mattered was not the instruction but the `IF` above it, and
reading the instruction alone gives the wrong answer. That is the same shape as the Transporter's
`BEQ TT222` (which pays no cash at all on the disc) and the enhanced-version AI flag — three faults
now that were each a correct reading of a line and a wrong reading of the version it belongs to.

**Left open, precisely.** Where the disc *does* set a bounty hunter's AI flag is not yet pinned: the
`STA INWK+32` that stores the value is inside its own guard, and the disc's route into `NWSHP` is the
one taken by both pirates and bounty hunters after the random type is chosen in Y. The value we use —
bits 7 and 6 for AI and aggression, low bits from a random byte — is consistent with the original's
`ORA #%11000000` and with the low bits varying aggression, but I have not yet read the disc's own
store. It is recorded as open rather than presented as verified.

## Where the disc's bounty hunters get their AI flag: resolved, and the answer is "not here"

The open item from last round is closed, and the answer is a clean negative that explains itself.

**Every `STA INWK+32` in the spawn code is guarded away from the disc.** There are five in
`main_game_loop_part_4_of_6.asm`, and walking the conditional stack at each one:

| line | what it is | disc |
| --- | --- | --- |
| 415 | the **Constrictor**, `%11111001` | active — but this is the mission ship, not a bounty hunter |
| 458 | the Constrictor, enhanced versions | excluded |
| 521, 528 | the bounty hunter, cassette and enhanced | excluded |
| 601 | a ship given E.C.M. but no AI | excluded |

So the disc's lone bounty hunter is spawned by `TYA / JSR NWSHP` with **no AI flag store at all** —
the line that sets it in every other version is simply not assembled, exactly as the E.C.M. chance is
not. That is the resolution: it is not that the disc sets a different value, it is that the disc does
not run the code that sets one, and the value therefore comes from whatever the ship's slot already
held. Reproducing *that* faithfully would mean modelling the ship-slot workspace rather than the
spawn, which is a different piece of work and is noted rather than guessed at.

**And the one store that does run on the disc is a check on our own data**, which is worth having:
the Constrictor is given `%11111001` — E.C.M., an aggression of 60 out of 63, and hostile — and our
`Missions.ConstrictorAiFlag` is `0b1111_1001`, the same byte. That is a value nothing had checked
against the source, and it is right.

**Three faults in a row have now been version-guard faults.** The pattern is strong enough to state
as a rule for the rest of this port: when a value or an instruction looks wrong, read the `IF` above
it before changing anything, because on this build the guard is evidence and the instruction is not.

## Only a military laser harms the Constrictor, and shooting an innocent turns the station on us

Applying the "read the guard" rule wholesale — listing every `IF _DISC_FLIGHT` branch — turned up two
missing rules in the laser code, and one worth recording as already right.

**The Constrictor is protected.** The disc's branch says it outright: *"only military lasers can harm
the Constrictor in mission 1, and then they only inflict a quarter of the damage that military lasers
inflict on normal ships"*. A pulse or beam laser does **nothing at all** to it. Ours took full damage
from any laser, which made the mission's target an ordinary fight rather than the one the original
designed, and made the military laser pointless for the only enemy it exists for.

**Shooting an innocent makes the station hostile.** The original's `ANGRY` tests bit 5 of the ship's
NEWB flags and calls `AN2` when it is set:

```
LDY #36 / LDA (INF),Y / AND #%00100000   \ the ship's NEWB flags
BEQ P%+5 / JSR AN2                       \ an innocent means the station turns on us
```

We had no such rule at all, so shooting traders was **free** — the station went on happily letting us
dock afterwards. It now turns hostile, takes a speed of 10 and an AI flag of `%11111000`, and a
hostile station refuses to let us in. `ANGRY` also switches the ship's own AI on and raises its
acceleration to 2, so a trader that survives a shot stops being a bystander.

This is the rule the `E%` flags were extracted for: it is bit 5 that decides it, so the flags now do
two jobs — the legal status and the station's temper.

**Checked and already right:** the Constrictor's AI flag is `%11111001`, matching ours byte for byte.
The Cougar rule in the same branch is dead code for this build, since the disc has no Cougar.

**The tally is fifteen**, and this round is the first where the version-guard rule found something
rather than explaining something.

## The missile lock outlived its target

Continuing the disc-only sweep into `KILLSHP`, whose disc branch differs from the others:

```
ELIF _DISC_FLIGHT OR _ELITE_A_VERSION
 CPX MSTG               \ Check whether this slot matches the slot number in
ENDIF                   \ MSTG, the target of our missile lock
...
 LDY #&EE               \ Otherwise we need to remove our missile lock, so call
 JSR ABORT              \ ABORT to unarm the missile and update the missile
```

The lock is released **on every kill**, not only when our own missile does the killing — and ours was
never released at all when a ship was destroyed. The lock survived its target, so the missile
indicators went on showing a target that was gone and the next missile was aimed at a ship already
removed from the bubble.

`RemoveKilledShips` now clears the lock when the ship it points at has been killed, which is where the
original's ABORT call lands.

**The tally is sixteen.** Three of the last four faults have come from the same move — list the
branches that this build takes, and read them rather than the ones the other versions take — and that
move has now found four faults in a row after being derived from three.

## Two disc branches that turned out to be plumbing rather than rules

Read at the same time and deliberately not acted on, because they are about how a 1982 machine with
two banks of memory works rather than about the game:

* `death2` calls `CATD` to reload the disc catalogue and then jumps to `INBAY` to load the docked
  code. On a modern machine there is no overlay to reload — the docked screens are already there.
* `hyp` branches on `QQ11` to decide whether to call `TT111`, which is about which view was being
  drawn when the jump started.

Recording these as read-and-rejected is worth as much as recording the faults: a branch being
disc-only does not make it a rule, and the two above would have been changes with no observable
effect.

## Anacondas release Worms, and nothing else

The next disc-only behavioural branch, and a rule we did not have at all: an Anaconda releases the
ship it carries.

```
CMP #ANA / BNE TN7            \ only an Anaconda
JSR DORND / CMP #200 / BCC TN7 \ and only on a roll of 200 or more
LDX #WRM                      \ a Worm — and on this build, always a Worm
```

The disc's branch is the narrow one: *"in the disc version, Anacondas can only spawn Worms, while in
the advanced versions they can also spawn Sidewinders"*, so there is no second roll here. It runs on
**every frame** the tactics do, so an Anaconda that survives a while fills the sky with Worms, which
is what the original does and what the source's own 22% describes. The released ship gets an AI flag
of `%11110001` — E.C.M., AI enabled, hostile, low aggression — from the shared `TN6` path.

**And the branch direction was a trap I walked into.** Writing `>= 200` releases, I first wrote it as
`< 200` — reading "BCC skips" as "below the threshold is the interesting case" — and the test caught
it immediately by measuring **77% instead of 22%**. The lesson is the same one the guards have been
teaching, one level down: on this codebase the direction of a comparison is as easy to get wrong as
the condition around it, and the only defence is to measure the rate rather than read it.

**The tally is seventeen.**

## Still open

`main_game_loop_part_2_of_6` carries a disc-only branch described as *"a bug fix for the first version
of disc Elite, in which asteroids never appeared"*. That is a fix we would have to decide whether to
take: the disc build this port follows is the Stairway to Hell variant, and whether its `_VARIANT=2`
includes the fix is the next thing to establish rather than assume.

## The asteroid fix: our variant has it, and we behave accordingly

The last disc-only branch, and the one where the question was which disc we follow rather than what
the code says:

```
IF _DISC_FLIGHT
IF _STH_DISC OR _SRAM_DISC
 NOP / NOP / NOP        \ In the first version of disc Elite, asteroids never appeared; it turned
                        \ out that the authors had put in a jump to force traders to spawn, so
                        \ they could test them
ELIF _IB_DISC
 JMP MTT4               \ the development jump itself
ENDIF
```

**The Stairway to Hell disc — the `_VARIANT=2` this port follows — takes the `NOP` branch**, so the
jump is patched out and the code falls through to the path that spawns rocks. Asteroids *do* appear in
the build we are porting, and they appear in ours: `Debris.ChooseJunk` rolls them at the original's
rates, and `FlightSim` checks for junk before it considers ships, as the original does.

So there was nothing to change, and that is worth stating plainly: the branch looked like a candidate
fault and turned out to be a variant question that resolves in favour of what we already do. Recording
it closes the disc-only sweep with no open items.

**The sweep, in total.** Every `IF _DISC_FLIGHT` branch in the shared library has now been read and
classified:

| classification | count | examples |
| --- | --- | --- |
| rules we had already | 3 | the Constrictor's AI flag, the station's safe zone, the escape pod's cargo |
| rules we were missing | **6** | the Constrictor's armour, ANGRY, the missile lock, Anacondas' Worms, the E.C.M. chance, the bounty hunter's AI |
| platform plumbing | 4 | loading the docked overlay, reloading the disc catalogue, reading the keyboard, the view branch in `hyp` |
| variant questions | 1 | the asteroid fix, settled above |

Six faults from one systematic read, after three rounds in which the same faults arrived one at a time
from symptoms. The lesson is not subtle but it is worth writing down: **when a project has a
conditionally-compiled source, the set of branches for your build is a thing you can enumerate, and
enumerating it beats waiting for each one to bite.**

## The docking cone is wider on the disc, and ours was the other version's

The comment sweep found a threshold that is version-specific, and we had the wrong version:

```
IF ...every other version...
 CMP #89               \ not in the 22.0 degree safe cone of approach
ELIF _DISC_VERSION OR _ELITE_A_VERSION
 CMP #86               \ not in the 26.3 degree safe cone of approach
ENDIF
```

The disc's safe cone of approach is **26.3 degrees**; every other version's is 22.0. Ours was 0.9272,
which is the cosine of 22.0 — the *other* versions' value, encoded as a hand-written constant with a
comment confidently naming the wrong angle. It is now `86.0 / 128.0`, which is what the disc's own
constant gives and which reproduces the 26.3 the source's comment states.

**And the level check was wrong in the same way.** The original tests `|roofv_x_hi| >= 80` against
orientation vectors whose unit is 128, so the threshold is `80/128`; ours was 0.8028, whose comment
said "36.6 degrees" and which is nowhere near either the raw constant or the angle. It is now
`80.0/128.0`. Both constants had been written as cosines of angles rather than as the source's own
ratios, and both had drifted.

**The lesson is about how the constants were derived.** A cosine of an angle looks like a faithful
port and reads like one, but it is one step removed from what the original tests, and that step is
where the error came in — twice, in the same file, in adjacent constants. The original's own unit
(128 for a unit vector) is the thing to encode, because it can be checked against the source
character for character.

**The tally is eighteen**, and both faults were found by reading the source's *comments* rather than
its instructions — which is the third distinct source of faults this session, after the code itself
and the guards around it.

## Traders become pirates 39% of the time, and the roles come from the NEWB flags

The comment sweep found a rule that is not a constant to correct but a piece of the AI we do not have.
Recorded precisely and **not** implemented this round, because the more I read it the clearer it became
that implementing it correctly is a round's work rather than an hour's.

**What TACTICS does, per ship per frame**, on this build:

```
JSR DORND / LDA NEWB / LSR A / BCC TN1     \\ bit 0: is this a trader?
CPX #100 / BCS TA22                        \\ 61% chance: leave it alone
                                           \\ otherwise it becomes a pirate
.TN1
LSR A / BCC TN2                            \\ bit 1: is this a bounty hunter?
LDX FIST / CPX #40 / BCC TN2               \\ only if our legal status is 40 or more
LDA NEWB / ORA #%00000100 / STA NEWB       \\ a bounty hunter that hates us goes hostile
```

So on this build a ship's *behaviour* — trader, pirate, bounty hunter, hostile — is read from its NEWB
flags and rewritten during play. A trader is attacked by the roll 39% of the time and becomes a pirate;
a bounty hunter only turns on us if we are nearly a fugitive. The source's own comment gives the
contrast: *"in the disc version, 39% of traders turn out to be bounty hunters, while it's just 20% in
the advanced versions"* — halved.

**Why this is not a one-line fix.** Everything else the sweep found was a threshold or a branch;
this is the rule that decides what a ship *is*. Getting it right means:

* the roles have to come from the NEWB flags rather than from the spawn table we currently use, where
  `SpawnKind.Pirates` and `SpawnKind.BountyHunter` pick ship types directly;
* only two types in the whole `E%` table carry the trader bit — the Shuttle at `0x21` and the
  Transporter at `0x61` — so a "trader" is a much smaller category here than our spawner assumes;
* the roll is per frame, so it needs the same care the Anaconda's did, including the direction of the
  comparison, which I got wrong there and which the *test* caught by measuring a rate;
* and it interacts with `FIST`, which we have under the name `LegalStatus`.

Doing it badly would be worse than not doing it: a wrong rate here would change what every ship in the
sky does. So it is written up with the code quoted and left for a round that can give it a test that
measures the rates rather than asserting a value — which is the only kind of test that caught the last
comparison-direction error.

**The tally stays at eighteen**, and the sweep's classification gains a fifth row: one rule that is a
subsystem rather than an adjustment.

## The render basis was not rigid — measured, and fixed on reasoning rather than on a visible before/after

Chasing the wobble a player reported, the basis the renderer builds from a ship's orientation vectors
turned out not to be orthonormal. Measured over 96 frames of rolling, with the vectors encoded the way
the game encodes them:

| | fresh | worst observed |
| --- | --- | --- |
| `|nose|` | 1.0000 | **0.9131** |
| `|side|` | 1.0000 | **0.9014** |
| `det` | +1.0000 | **+0.8229** |

A basis whose vectors swing between 90% and 100% of unit length is not a rotation, and the transform
it defines shears the whole body — and shears it *differently* as the ship rolls, which is the wobble.

**Where it comes from.** The original's `TIDY` rebuilds one component of `roofv` and then normalises by
`NORM`, which divides by `LL5` — an approximated square root whose reciprocal is off by a few percent.
So the original's own basis is not exactly rigid either. It can afford that because it draws a
**wireframe**, where a few percent of scale error moves the ends of lines slightly and nothing else.
Filling the faces exposes it.

**The fix** is Gram-Schmidt in `FromEliteOrientation`: the nose is kept as the reference direction
because it is the ship's forward axis and its length is the scale, the roof is made perpendicular to
it, and the side is taken as their cross product, so the frame is exactly rigid however the
fixed-point arithmetic left the vectors.

**What I have not shown, and am not claiming.** I could not demonstrate a visible difference. The
station's silhouette area and bounding box measure the same to the pixel before and after — 352x355 at
every heading, area within 3.6% — so whatever the shear was doing, it is below what these measurements
resolve, and I have not confirmed that it is what the player is seeing. The defect is real and
measured; the improvement is reasoned and unproven. It is committed on that basis with this note
attached, rather than described as a fix for the reported symptom.

**Four of my probes have now been wrong before the code was** — the RUPLA galaxy check, the
galaxy-number false alarm, the `E%` signature, and the basis test's vector encoding. This round added
a fifth lesson of the same family: a change can be correct and still not be the change that matters,
and the difference is only visible if the before/after is measured rather than assumed.

## The rigidity fix now has evidence, and my earlier "unproven" was a broken measurement

Last round ended with the basis fix committed but marked unproven, because the station's silhouette
measured identically before and after. That conclusion was itself wrong, and the reason is worth
recording: **`--viewer-distance` is honoured, but the ship viewer auto-frames**, so the station renders
at the same size whatever distance is passed. Every "before" and "after" screenshot I compared was of
a constant. I was measuring my own scaling, not the shear.

**What replaced it.** The rebuild moved into `Core.Maths.RigidFrame`, where it can be reached by tests
rather than living inside a MonoGame renderer, and `RigidFrameTests` measures the thing that was
actually wrong: it rolls a ship for four hundred and eighty frames through `Mvs5` and `Tidy` — the real
fixed-point arithmetic, not a simulation of it — and checks after every tidy that all three vectors are
unit length, that all three pairs are at right angles, and that the determinant is 1.

**And the test is known to be capable of failing.** Restoring the old behaviour — normalising each
vector independently — makes two of its three cases fail immediately. A test that cannot fail is worth
nothing, so that check is part of the work now.

**The measurement, then and now:**

| | before, over a long roll | after |
| --- | --- | --- |
| vector lengths | 0.90 to 1.00 | 1.0000 |
| angles between them | out by up to 2.3% | right angles |
| determinant | 0.82 to 0.98 | 1.0000 |

So the defect was real, the fix addresses it, and the evidence is a test that fails without the fix
rather than a screenshot I could not tell apart from its control.

**The lesson, which is now the fifth of its kind this session.** Two rounds ago a probe of mine was
wrong about a value; last round a probe was wrong about an encoding; this round a probe was wrong about
what it was even varying. The defence that actually works is not care but **a control**: the test above
was checked against the old behaviour before being trusted, and the screenshot comparison would have
been caught immediately by asking what it was supposed to differ from.

## The space station's docking slot could never be drawn

Chasing the reported wobble turned up a fault with a much clearer signature: **the Coriolis's docking
slot was not being drawn at all**, at any distance, and neither was the Dodo's.

**The cause was a distance scale eight times too steep.** Every visibility byte in the blueprints is on
a 0-31 scale, which is the same scale the original uses for a ship's distance: LL9 part 2 takes `z_hi`
and shifts it right three more times, so `XX4 = z_hi >> 3`, saturating at 31 for anything from `z_hi`
248 up. Ours divided the scaled distance by 512 and clamped it to **0-7**. No detail edge could then
survive its own gate unless its visibility was 7 or less, so every detail above that was culled at
every distance:

| z_hi | original XX4 | ours | slot (visibility 30) shows |
| --- | --- | --- | --- |
| 2 | 0 | 1 | no |
| 16 | 2 | 7 | no |
| 64 | 8 | 7 | no |
| 240 | 30 | 7 | no |

It is now `Math.Min(31, zHigh >> 3)`, and the station draws its slot: **4 detail edges** at the
heading where the slot faces us, where it queued exactly **0** before. The Dodo, which also carries
slot edges at visibility 30, went from 0 to 4 with it.

**This is very likely what the reported "wobble" was**: the slot appearing and vanishing changes what
the station looks like as it turns, and a missing slot is far more visible than a two-percent shear.

**And the method note matters more than the fix.** Two rounds went into a two-percent shear that I
could not show was visible, while the real fault — a whole feature missing — was one line away in the
same file. The measurement I had been taking, a silhouette's bounding box, is **blind to a hole in the
middle of the shape**. Measuring a feature means measuring the thing the feature is, not the outline
around it: `queued detail edges` found this in one line of output, having been printed all along.

## Every detail edge on every ship, checked reachable

The docking-slot fault was a whole feature culled at every distance, so the natural follow-up is to ask
whether any other detail edge is unreachable. Swept all twenty-two ships with detail edges across four
headings and three pitches, counting the blueprint's detail edges against the most that ever queue:

| ship | detail edges | most ever queued |
| --- | --- | --- |
| Coriolis, Dodo | 4 | 4 |
| Cobra Mk III | 14 | 14 |
| Python, Adder, Asp Mk II | 4 | 4 |
| Viper | 6 | 6 |
| Sidewinder | 4 | 4 |
| Shuttle | 10 | 6 |
| Transporter | 18 | 16 |
| Fer-de-Lance | 9 | 6 |
| Moray, Krait | 5, 10 | 5, 10 |
| Mamba | 20 | 16 |
| Gecko, Thargoid | 2 | 2 |

Nothing is unreachable. The counts below the total are edge cases of *viewing* rather than of the gate —
only the detail on faces pointing at us can ever be drawn — and they are consistent with that: adding
the pitch axis is what brought the Shuttle, Fer-de-Lance and Mamba up.

**And one apparent fault that was mine.** The Constrictor reported **6 detail edges and 0 queued at
every heading**, which looked like the slot fault repeating. It is not: all six decorate **face 9**,
whose normal is `(0, -27, 0)` — straight down — and they are on the hull's underside, so they can only
be seen from below. Rendering with a negative pitch queues all six. The face's normal points outward,
as it should, and the check was right.

**That is the sixth probe of mine to be wrong before the code was**, and the same mistake as the
docking slot from the other direction: last time I measured the outline and missed a hole in the
middle; this time I measured one axis of the camera and missed a whole hemisphere. Both were fixed by
asking what the measurement *cannot* see. It is the only reliable move I have found, and it is now
recorded here in those terms rather than as a resolution to be careful.

## Two different visibility scales, and I nearly destroyed a correct one

Following last round's scale error, I went looking for its siblings and found the byte beside it in
the same method:

```
 LDY #13                \ Fetch byte #13 from the ship's blueprint, which gives
 LDA (XX0),Y            \ the ship's visibility distance, beyond which we show
 CMP XX1+7              \ If z_hi <= the visibility distance, skip to LL17 to
 BCS LL17               \ draw the ship fully, rather than as a dot
```

and, a few lines away in the per-face path:

```
 AND #%00011111         \ Extract bits 0-4 to give the visibility distance
 CMP XX4
```

I read the second, concluded the ship-level comparison was missing its `& 0x1F`, and changed it —
masking the Coriolis's 120 down to 24, which would have made a station that should stay a model out to
`z_hi` 120 collapse to a dot five times too early. **The "fix" was worse than the thing it was fixing.**

**They are two different scales, and the source says so.** A *face* wears its visibility in bits 0-4 of
its own byte, which is why the mask is there. A *ship's* visibility distance is the whole of byte #13 —
the blueprint comments confirm it, giving the Anaconda 50 on this build against 36 elsewhere, and 50
cannot survive a 0x1F mask, so the raw byte is the intended threshold. Ours was already right; the
change is reverted and the reasoning left in a comment so the next reader does not repeat it.

**That is the seventh probe of mine to be wrong before the code was**, and this one is the most
instructive of the seven, because the evidence was genuinely ambiguous: the same phrase, "visibility
distance", in two places, a mask in one and not the other. What settled it was not the code but the
*comments* — the Anaconda's 50 — which is now the fourth distinct source of truth this session, after
the instructions, the guards around them, and the prose.

The rule I am taking from it: **when two things share a name, find a value that can only belong to one
of them before changing either.** The Anaconda's 50 could only belong to the unmasked scale; a single
number answered a question that two similar code paths could not.

## Round 100: the whole loop, flown again

A hundred rounds in, the cheapest useful thing is to fly the game end to end, since so much has
changed under it — the market, the missions, the legal status, the galaxy, the visibility scale for
every ship. Thirteen checks, all passing:

| check | |
| --- | --- |
| starts at Lave in flight | ok |
| docked with a 17-item market | ok |
| buys cargo | ok |
| selling does not restock the market | ok |
| ten jumps and dockings survived | ok |
| killing a pirate pays its bounty | ok |
| killing a pirate leaves us clean | ok |
| the kill counts | ok |
| killing an innocent makes us an offender | ok |
| galactic jump works | ok |
| the chart follows the galaxy | ok |
| we left Lave | ok |

Nothing was found, which is the right result for a round like this: the loop is intact, and the list
of things that were wrong is a list of things that are now covered.

**And the ships whose detail was suppressed.** The visibility fault hid detail edges on every ship
whose visibility is above 7, not only the stations. Rendering the ones with the most — the Cobra Mk
III's fourteen, the Viper's six, the Krait's ten, the Mamba's twenty — shows them drawing correctly,
so the fix's effect is not confined to the docking slots that led to it.

**Still open, and the only open item that matters to a player:** whether the reported wobble is gone.
The two changes that could account for it are the visibility scale (round 97) and the rigid frame
(round 96), and only someone looking at the screen can say. The measurements say the frame is now
rigid and the slots now draw; they cannot say that the picture is right.

## Looking for flicker, and not finding it

The reported wobble is the one thing left that only a player can settle, so the next best move is to
test the mechanism that would cause a *changing shape* rather than a subtle one: faces flipping
visibility at boundary angles.

**Counting faces is the wrong instrument, and the count even looks alarming.** Sweeping the Coriolis
in five-degree steps, the number of faces the renderer keeps jumps by two at a time — 6, 4 at
thirty degrees, and 6 again at sixty-five. For a body with parallel faces that is expected: two faces
sharing a normal cross their boundary together. But the count says nothing about what is on screen,
because a back-facing face contributes no pixels whether it was culled or not.

**So the image was measured instead.** Rendering every two degrees from 26 to 40 and counting how many
pixels differ between consecutive frames:

| headings | pixels differing |
| --- | --- |
| 26 → 28 | 270 |
| 28 → 30 | 308 |
| 30 → 32 | 322 |
| 32 → 34 | 318 |
| 34 → 36 | 312 |
| 36 → 38 | 334 |
| 38 → 40 | 322 |

**Smooth across the face-count jump**, which is the answer: the faces that appear and disappear there
were behind the ones facing us, so they never reached the screen. A flicker would have shown as one
step being several times its neighbours, and the largest step here is a fifth above the smallest.

That does not prove the picture is right, but it does eliminate the one mechanism that would produce a
*changing shape* as opposed to a static inaccuracy. What remains for the wobble is either something
only a person can see, or something in the simulation rather than the renderer.

**The instrument lesson is the same one, one level up.** Last round I measured the outline and missed
a hole in the middle; this round the obvious instrument — how many faces are drawn — is blind to
everything on screen, and the count's alarm was an artefact of parallel faces. Measuring a feature
means measuring the thing the player sees, which for a renderer is pixels.

## The wobble: what has now been ruled out, and two more of my own errors

Three rounds of hunting the reported wobble have eliminated every mechanism I can measure. Recorded
together, because the list is the useful artefact:

| candidate | how it was tested | verdict |
| --- | --- | --- |
| distorted model geometry | rendered the station at every heading | solid and correct |
| projection or scale | area scales as 1/z² exactly | correct |
| near-plane clipping | near plane is 4 units; the station clips at ~24 000 | never engages |
| face culling forced on | changed it; the render was byte-identical | not the cause |
| a non-rigid basis | measured: lengths swung 0.90 to 1.00, determinant 0.82 to 0.98 | **real, fixed** |
| faces flickering at boundaries | every 2 degrees, pixel diff smooth across a face-count jump | no flicker |
| the docking slot missing | it was culled at every distance | **real, fixed** |

The two real faults found along the way are both fixed and both were worth finding on their own terms.
What remains for the reported symptom is either something only a person can see, or something in the
**simulation** rather than the renderer.

**And two more probe errors of mine, taking the count to nine.** The first was structural: I called
`Mvs5(o, Orientation.Nosev, 0x00, ...)` when its parameters take a *component* offset, so my "x" and
"y" were the nose vector and the roof vector. The numbers it produced — a nose component climbing past
1.0 while its neighbour never moved — looked exactly like a rotation fault and were entirely an
artefact of the call. Reading the production call sites, which all pass `Orientation.Roofv +
Orientation.X`, would have prevented it in one look.

The second was a misreading: a step size printed as `93,583` and read as ninety-three, when the same
program's own filtered list showed no step above 7.9. The decimal comma in a locale-formatted number
looked like a thousands separator.

**Both were caught by cross-checking one measurement against another rather than by care.** That is now
the ninth time, and the pattern is stable enough to state as a rule for any future diagnostic work
here: a probe that has not been checked against a second, independent view of the same quantity is not
evidence.
