# Changelog

All notable changes to this project are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[semantic versioning](https://semver.org/spec/v2.0.0.html) — see [`docs/VERSIONING.md`](docs/VERSIONING.md)
for what the three numbers mean for a faithful port.

## [1.2.0] — 2026-09-24

The dashboard, the sound table and the planet, drawn as the disc draws them: the altitude bar and
the two indicator bulbs, the disc's own cross crosshair, the planet's meridians, the sound table
audited byte for byte, and the gamepad's remaining flight keys.

* **The dashboard is the disc's own.** The altitude bar arrives — part 15's ALTIT, the square root of
  the sum of the squared high bytes less the 37 the SBC takes with it, which also pins the crash test
  to the disc's own boundary — and the two indicator bulbs above the energy banks light as the disc's
  ECBLB and SPBLB light theirs: "E" while the E.C.M. runs, "S" while the station is about. The laser
  crosshair is the disc's own cross, SIGHT's two size-20 crosses leaving the four arms with a gap in
  the middle, drawn only when the view we are looking through has a laser fitted. The planet shows
  PL9 part 2's two meridians through its disc, from its own orientation vectors — which on the disc
  are ZINF's identity, so the planet's look is the cross.
* **The sound table is audited and complete.** All ten of the disc's SFX entries were already
  byte-verified against `variable/sfx.asm` in the tests; the audit wired the last trigger the disc's
  own code makes — the docking dent's EXNO3 pair — and the status table says so.
* The gamepad maps the rest of the flight's keys: the left shoulder locks a missile, X fires one, Y
  fires the E.C.M., Start engages the docking computer, Back cancels it, and the right stick looks
  through the four windows.

## [1.1.0] — 2026-09-24

The mission screens, the death and the docked flow, measured against the disc sources: the flight
loop's bookkeeping swept, cargo scooping made the disc's own pass, mission 2 paid its naval energy
unit, and the briefings, the hangar, the pause and the death sequence staged as the disc stages
them — with a development harness to drive the live game and check all of it.

### Fixed

* **The station appeared the moment you arrived, in every system, again.** The hyperspace arrival
  and the galactic jump still called the flight scene's `ArriveInSystem` without a station distance,
  so its development default of 3,000 units ahead pre-empted the ported part 14 and the station sat
  3,000 units ahead of us for ever. The default is zero, which is the original's rule: the station
  appears when you reach the planet.
* **A ship that had started to explode could still be collided with, and re-killed.** The original's
  part 7 ORs the exploding bit into its distance test, so a wreck is past colliding with and past
  scooping the moment it starts to blow up; the port only skipped the killed bit, so an exploding
  wreck could be rammed and killed a second time, paying a second bounty.
* **The energy bomb's victims were not all paid.** Their kills flowed through one
  destroyed-this-frame flag, which the last victim overwrote: only the last ship's bounty was paid,
  and the scene's compensation counted kills without bounties or legal status. Kills and drops are
  now collected until the game drains them, so every victim of a bomb or a crowded frame is paid
  its own bounty, counted, and read for innocence.
* **Kills from the earlier iterations of a frame were lost.** Up to ten iterations run between two
  drawn frames, and the kill and drop flags were cleared at the end of every iteration; the drain
  fixes that too.
* **A mining laser became a military laser across a save and load.** The reader clamped every saved
  laser into 0–3, and the mining laser is the fourth; it is now validated rather than clamped.
* **Mission 2's reward is the disc's own.** DEBRIEF2 fits the special navy energy unit — ENGY = 2,
  which recharges the banks by 3 a tick instead of 2 — and awards 256 kill points; the port paid
  1,000 credits instead, an invention of its own. The shop refuses to sell an energy unit while any
  is fitted, as the original does, and the energy unit is the original's ENGY byte throughout.
* **Mission 2's offer has the disc's own gates.** DOENTRY offers it only when mission 1 is complete
  and out of progress, the kill tally's high byte is 5 or more (1,280 kills), and we are in the
  third galaxy; the port offered it at any rank in any galaxy.
* **The missile lock survives its own launch and arrival.** RES2 clears the missile target — "Reset
  MSTG, the missile target, to &FF" — at every launch, arrival and mis-jump; a lock that survived
  pointed at a ship that was no longer in the bubble.
* **Dying with an escape pod fitted is game over.** The disc's DEATH is fatal however the ship was
  lost: the pod is launched by its own key while the ship is still there to press it, and DEATH
  never reaches it. The port's death used to consult the pod and rescue the commander, and quietly
  refilled the replacement ship's energy — a rule the disc does not have, in a place the port's own
  comment says the cassette version's RES4 does it.
* **The death sound played, then nothing heard it.** The shell cleared the died flag before the
  flight scene ran, so the scene — whose job is that sound — never saw it. The death is now
  resolved after the scene's update.
* **CTRL-S both saved and sold.** Saving from the market with an item selected also sold one unit;
  CTRL-S is now answered once, in the shell, from every docked screen — as the disc's own docked
  save works from whichever screen is up — and plain S still sells.
* **The compass dot never pointed at the station.** The disc's COMPAS shows the station inside the
  safe zone and the planet otherwise; the port preferred the planet whenever it was in the bubble.
* **The E.C.M.'s invented range is gone.** The port destroyed missiles only within 20,000 units;
  the disc's E.C.M. destroys every missile in the local bubble.
* ESCAPE docked now launches from the station, as the disc's own docked key does, instead of
  quitting; F10 still quits from anywhere, and ESCAPE quits from the title screen and game over.

### Changed

* The energy unit is the original's ENGY byte: none, standard, or navy, where it used to be a
  boolean. Save files written by the previous build read unchanged — a saved `true` reads as the
  standard unit.
* **Cargo scooping is the disc's own pass now.** Part 7 gates every close item on the range — within
  127 units on every axis — on fuel scoops being fitted, and on the item being *below* us, which the
  port ignored: it scooped anything in front within 200 units. The consequences of the real rule all
  follow: a cargo canister holds a random market item drawn at scoop time ("AND #7", food to
  computers) rather than nothing at all; a full hold destroys the canister with the EXNO3 sound
  instead of leaving it hanging; and a canister we cannot scoop is something we *collide* with, at
  the disc's own damage of 128 plus half the energy it has left.
* **A failed approach to the station is the disc's own MA62.** At a speed of 5 or more it is fatal;
  below that it is a dent — the speed stops dead at 1 and 5 points of damage go into the shields.
  An annoyed station's refusal is the same branch, so flying at one politely at a crawl now costs a
  dent where the port used to refuse politely and hurt nobody, and fast used to cost nothing.
* **The docking computer's speed cap is hard now**, as DOKEY has it: "the maximum speed during
  docking is 22", rather than an approach that merely asked to slow down.
* **Pirate packs are drawn with the disc's own dice.** Each pirate's type is the AND of two random
  bytes reduced to the pack's range — "which makes the chances of a smaller number higher" — so the
  Sidewinder leads the pack and the Cobra Mk III (pirate) is its rarest member; the port drew
  uniformly. Packs can also be the disc's rare large one, of up to eight ships, on its 3.1% chance.

### Added

* The disc's own messages and sounds that the flight loop prints and nobody in the port did:
  "FUEL SCOOPS ON" every iteration the sun's scoops are working, "DOCKING COMPUTERS ON" on the disc's
  own cadence (the 15th iteration of every block of 32) while the autopilot flies, the repeating
  short, high beep of an armed missile with a target in the crosshairs, and the beep that token 100
  carries every 32 iterations while the banks are at 50 or below.
* The energy bomb's flash: while it is going off the space screen flashes black and white, which on
  the BBC is a palette trick and here is the same effect drawn.
* **The death sequence is the disc's own D2 loop.** The dashboard hides, "GAME OVER" is printed, and
  the flight loop keeps running for the disc's 5.1 seconds while five bits of debris — a cargo
  canister or an alloy plate on the toss of a coin, half of them exploding, pointed away from the
  wreck at double the speed — drift off it. The game-over screen comes after, as DEATH2 does.
* **The ship hangar.** The disc's DOENTRY shows it while the docked code loads: half the time a group
  from the HATB table (a Shuttle and a Transporter, three canisters, a Transporter and a Cobra Mk
  III, a Viper and a Krait), half the time a solitary ship or an empty bay, every ship spun on the
  deck, with HAS1's own heights off the ground. The port draws the bay's floor as a converging grid,
  which keeps the ships standing in it.
* **The disc's pause.** Backspace (for COPY) stops the flight loop and DELETE starts it again, and
  while it is stopped Q silences the sound, S brings it back, A toggles auto-recentre and CAPS LOCK
  toggles flight damping — the disc's own configuration keys, and the two toggles the simulation
  modelled but nothing could reach.
* The charts' O and F keys: O snaps the crosshairs onto the current system (the disc's ping), and F
  searches the galaxy for a system by name (the disc docked code's HME2), saying "UNKNOWN PLANET" to
  a low beep when there is no such system.
* **The mission briefings are staged.** The disc's five screens are the port's: BRIEF for mission 1's
  offer — which accepts the mission before it prints anything, as the disc's own bit-setting does —
  with the INCOMING MESSAGE banner, the rotating Constrictor (64 iterations of undamped roll and
  pitch, then BRL2's drift to the top of the screen), and token 10's text waiting at the points the
  printer reports; BRIEF2's initial contact, BRIEF3's plans, and the two debriefs' thank-yous. Each
  ends at the Status Mode screen, as BRP does. Finding it fixed a latent bug in the printer: the
  event positions were recorded before the tidy, and the briefing's last event fell past the end of
  the tidied text, which crashed the pagination.
* CTRL-L loads the commander from any docked screen, which is the load half of the disc's docked
  file menu; CTRL-S already saves from all of them.
* **The development harness.** `--dev-server <port>` runs a small HTTP server that drives the live
  game: advance it by drawn frames at a fixed rate, act on it (launch, dock, autopilot, the pause),
  screenshot it, and ask it where things stand — all without restarting it. The README documents it,
  because it is how the docking sequence, the hangar and the death animation are checked now.

## [1.0.0] — 2026-09-23

The first release: BBC Micro disc Elite, rebuilt from its own 6502 sources, flying and trading end to
end, with the mission texts printing word for word.

### Added

* **The universe, from the original's seeds.** All eight galaxies and their 256 systems each, with the
  original's names, coordinates, economies, governments, tech levels, populations and productivity,
  generated by the original's own twist routine. Lave is at (20, 173) and is a Rich Agricultural
  dictatorship, because that is what the seeds say.
* **Flight.** The fixed-point flight model ported from the 6502 sources — `MVS4`, `MVS5`, `MVEIT`,
  `TIDY`, `TACTICS`, the complement-multiplication helpers and their carry quirks — running at the
  disc version's own measured rate of 12.5 iterations a second, with the display interpolating
  between iterations. Pitch, roll, speed, energy and the four views, all on the original's keys.
* **Combat.** Pulse, beam, military and mining lasers with the original's powers and pulse rates; the
  HITCH crosshair test against each blueprint's targetable area; damage, shields, energy banks and
  their separate recharge rules; missiles with lock and homing; the E.C.M.; the energy bomb; the
  escape pod; bounties, the kill tally and legal status.
* **The ships.** All 31 blueprints extracted from the original's own binaries and **verified
  byte-for-byte** against them: 17 ship sets, 527 `XX21` slots, 204 blueprints, 0 mismatches. Rendered
  as flat-shaded solids from the blueprint geometry, with the original's face normals deciding
  visibility, so the silhouettes match the original's hidden-line output.
* **Ship AI and spawning.** The original's spawn probabilities by government type, pirate packs, lone
  bounty hunters, asteroids, boulders, cargo canisters and splinters; the aggression test, steering,
  the aim and range test before firing, and the tactics counters that decide when a ship turns, runs
  or shoots.
* **Docking and launching.** The station's own spin rate and docking slot, the launch and docking
  tunnels, the docking computer (`C`, cancelled with `P`) with its approach phases and its faults
  ported; death at the hands of the station if you get it wrong.
* **The docked screens.** Market (buy and sell), the equipment shop with the original's fourteen items
  gated by tech level, the short-range chart with its fuel circle, the long-range chart, Data on
  System, the commander's status and inventory, and saving and loading.
* **Hyperspace.** The original's distance formula, the fuel it costs, the countdown, witchspace and
  its Thargoids, and the galactic hyperdrive that rotates the galaxy seeds one bit to the left so
  eight jumps bring you back.
* **The missions.** Both disc missions' logic — the Constrictor and the Thargoid plans — with the
  offer rules from `DOENTRY`, the mission hint overrides for the systems the trail runs through, and
  the briefing, contact and debriefing texts printing **word for word** as the original prints them,
  including the captain's name and location hint that depend on the galaxy, the standard tokens the
  texts borrow, and the original's own case rules.
* **The text system.** The extended token printer (`DETOK` and its jump tokens 1–32, the MTIN random
  runs, the two-letter token tables) and the standard token table, with system descriptions generated
  from the seeds as the original generates them — "Lave is most famous for its vast rain forests and
  the Lavian tree grub".
* **The presentation.** A widescreen view keeping the original's vertical field of view, the
  dashboard with its scanner, compass, dials and indicators, the launch/dock/hyperspace tunnels, and
  a start screen with settings and the Blue Danube (a deliberate departure — the BBC disc has no
  music).
* **Sound.** The original's beeper effects synthesized from its own sound-chip parameters: channel,
  amplitude, pitch and duration, with the four envelopes the game's loader sets up.
* **Tools.** `EliteDataExtractor` extracts the blueprints, the token tables, the galaxy seeds and the
  market and equipment tables from the published assembly sources, and verifies the blueprints
  against the original's binaries.

### Known gaps

Recorded here rather than hidden, because the estimate of "how near 100%" should be checkable:

* **Mission presentation.** The briefing texts print and the screen actions they ask for
  (`INCOMING MESSAGE`, show the ship, wait for a key press) are reported by the printer, but the
  flight/docked game layer does not yet stage them, and the briefing is not yet shown on docking.
* **Detail edges.** The original draws fine detail lines on close ships from its per-edge visibility
  values; the remake draws the faces only.
* **Dials.** The dashboard's dial geometry and text placement are close, not the original's
  coordinates.
* **Death sequence** and a handful of rare-state behaviours.
* **Audio** covers the flight effects and the title music; the full list of the original's sound
  table entries has not been audited end to end.

### Notes

* 412 tests, all green (`dotnet test tests/EliteRemake.Core.Tests`, about twenty seconds).
* Save files are JSON rather than the original's 76-byte checksummed block. The information is the
  same; the format is not.
* `docs/DIVERGENCES.md` lists every deliberate difference from the original, and the faithful quirks
  that are reproduced on purpose. `docs/ROADMAP.md` is the full engineering log, by round.

[1.0.0]: https://github.com/tkleisas/elite-remake/releases/tag/v1.0.0
