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
- **M1 — Data pipeline.** `EliteDataExtractor`: ship blueprints (31 types) with byte-exact
  verification against `D.MOA`–`D.MOP`, text tokens (`QQ18`/`QQ16`), galaxy seeds (`QQ21`), market
  (`QQ23`) and equipment tables.
- **M2 — Core maths & universe.** Fixed-point maths, galaxy generation (`TT54`/`TT111`/`cpl`),
  system data (`TT25`), economy & prices, token-based text printing. Verified against canonical
  values (galaxy 0: *Tibedied* is system 0, seeds `&5A4A/&0248/&B753`; **Lave** is at (20, 173)
  and is "most famous for its vast rain forests and the Laveian tree grub").
- **M3 — Flight & rendering.** Solid-face 3D renderer with painter's-algorithm depth sorting,
  authentic dashboard/HUD, player flight model (`MVEIT`/`MVS4`/rotation/tidying), sun/planet/station
  objects, system arrival via hyperspace.
- **M4 — Combat & AI.** Lasers, missiles, ECM, energy banks, shields, `TACTICS` ship AI, spawning,
  explosions, escape pods, bounty, rating.
- **M5 — Docked screens & missions.** Launch/docking (manual + docking computer), market, equipment,
  shipyard, short/long-range charts, Data on System, commander save/load, missions (Constrictor,
  Thargoid documents), galactic hyperspace (8 galaxies).
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
