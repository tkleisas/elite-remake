# EliteDataExtractor

Extracts the ship blueprint data of **BBC Micro disc Elite** (Stairway to Hell variant, `_VERSION=2`,
`_VARIANT=2`) from Mark Moxon's commented 6502 source library and verifies it against the assembled
original binaries.

It is a dependency-free .NET 10 console app (BCL only: `System.Text.Json`, `System.Text.RegularExpressions`).

```text
Usage: EliteDataExtractor [all|extract|verify] [options]

  all       extract, write the JSON, then verify (default command)
  extract   extract and write the JSON only
  verify    decode the reference binaries and compare them with the extracted data

  --source <path>   source library (default: $ELITE_SOURCE_LIBRARY, else
                    /home/tkleisas/Projects/elite-source-code-library)
  --out <path>      output directory, or a path ending in .json (default: data)
  --quiet, -q       suppress progress output
  --help, -h        show help
```

Typical use from the repository root:

```bash
dotnet run --project tools/EliteDataExtractor            # all: extract + write data/ships.json + verify
dotnet run --project tools/EliteDataExtractor -- verify  # verification only (does not write the JSON)
```

Exit codes: `0` success, `1` verification mismatch, `2` usage error, `3` extraction error.

## What is extracted

* **31 ship blueprints** reachable in the disc flight build: the 30 ships referenced by the sixteen
  `D.MOA`–`D.MOP` blueprint files, plus the missile (which lives in `MISSILE.bin` at `&7F00`).
* **17 ship sets**: the sixteen flight `XX21` lookup tables and the docked/hangar `XX21` table.
* **204 ship type registrations**, with the four-character blueprint symbols from the `XX21` comments
  (`MSL`, `SST`, `ESC`, `PLT`, `OIL`, `AST`, `SPL`, `SHU`, `CYL`, `ANA`, `COPS`, `SH3`, `KRA`, `ADA`,
  `WRM`, `CYL2`, `ASP`, `THG`, `TGL`, `CON`).
* For each blueprint: the header stats, every vertex, edge and face, and the decoded face/vertex
  numbers exactly as the original stores them.

`data/ships.json` is deterministic: fixed property order, no timestamps, stable ordering (ships by
their lowest registered ship type, then by assembler label). Re-running produces a byte-identical
file.

## How it works

1. **A miniature BeebAsm assembler** (`Assembly/`) assembles each original source file exactly as the
   disc build sees it. It supports `MACRO`/`ENDMACRO` (with arguments and `IF` inside macro bodies),
   `IF`/`ELIF`/`ELSE`/`ENDIF` (including `NOT(...)`, `AND`/`OR`/`EOR`, comparisons and nested blocks
   that are never evaluated when skipped), `EQUB`/`EQUW`/`EQUD`/`EQUS`, `SKIP`, `ORG`, `GUARD`,
   `INCLUDE`, labels and symbol assignment. Comments start at `\` (also inside banner blocks, so a
   line is only a comment if its first non-whitespace character is `\`). Expressions support decimal,
   `&`hex, `%`binary, `ABS()`, `LO()`, `HI()`, `NOT()`, `+ - * / % << >> AND OR EOR` and `^` as a
   power operator.
2. **Two passes.** Pass one records label addresses; pass two evaluates every expression with the
   complete symbol table. Conditionals only ever depend on the build-option flags, which is enough
   for these sources; undefined symbols are an error if an evaluated expression needs them.
3. **Blueprint decoding.** Vertices are read from 20 bytes after the blueprint label, edges and faces
   from the signed 16-bit offsets in header bytes #3/#16 and #4/#17 (so shared data such as the
   Thargon's cargo canister edges is handled). The sign byte contains the sign bits of x, y and z plus
   the 5-bit visibility; vertex face bytes pack face1/face2 and face3/face4 into nibbles; edge bytes
   pack face1/face2 and store both vertex numbers multiplied by four.

## How verification works

`verify` never trusts the assembler for the binary side. For each ship set it:

1. **Compares the whole assembled image with the reference binary byte for byte.** For the sixteen
   flight files that is `versions/disc/3-assembled-output/D.MO?.bin` from base `&5600`; for the docked
   set it is the ship blueprint block at `&5600` inside `T.CODE.unprot.bin` (file offset `&441D`).
   Bytes past the end of the assembled image must be zero, which is how the `SAVE CODE%, CODE%+&0A00`
   padding is checked.
2. **Compares the `XX21` pointer table** slot by slot (31 two-byte entries per set).
3. **Decodes every ship independently from the binary**, using only the pointer table, the 20-byte
   header layout and the offsets in the header (the decoder in `Verification/` is a separate
   implementation from the one used for the assembly side), and compares every header stat, vertex,
   edge and face with the asm-derived data.
4. The missile's `XX21` pointer is `&7F00`, which is outside every flight file, so its blueprint is
   decoded from `MISSILE.bin` at offset 0 and compared there.

The report is printed per file and per ship. The command exits non-zero if anything mismatches.

## Findings and deliberate choices

These are the places where the extractor's behaviour is worth knowing about. Everything below is
verified against the reference binaries.

1. **The `XX21` tables have 31 entries, not 32.** The flight tables cover ship types 1–31 (the first
   blueprint starts at offset `&5D` = 62 bytes of table + 31 bytes of `E%` flags). **Ship type 15 is
   not registered in any table** in the disc version, so 31 ships are registered under 30 types.
2. **Header byte #19 is `(laserPower << 3) | missiles`, not `laserPower | missiles << 4`.** LL9 reads
   bits 3–7 for the laser power and `TACTICS`/`NWSHP` read bits 0–2 for the missile count, and the
   source comments agree (`%00101111` is "Laser power = 5, Missiles = 7"). Both decoded fields and the
   raw byte are emitted.
3. **Header byte #0 packs two values.** The low nibble is the maximum number of canisters dropped on
   demise; the high nibble plus one is the market item number awarded when the ship is scooped (0 if
   it is not scooped as an item). Both `maxCanisters` and `scoopMarketItem` are emitted alongside the
   raw `canisterByte`.
4. **The docked table *does* have its own blueprints.** `library/disc/docked/variable/xx21.asm` only
   contains the table, but `elite-source-docked.asm` includes full hangar blueprints for eight ships
   (`SHIP_CANISTER`, `SHIP_SHUTTLE`, `SHIP_TRANSPORTER`, `SHIP_COBRA_MK_3`, `SHIP_PYTHON`,
   `SHIP_VIPER`, `SHIP_KRAIT`, `SHIP_CONSTRICTOR`) assembled at `&5600` inside `T.CODE`. Five of them
   differ from their flight versions — the Shuttle, Transporter, Python, Krait and Constrictor (the
   hangar Constrictor is a much tougher ship: energy 200, speed 55, no scoop item) — and are emitted
   per ship as `dockedVariant`. The `docked` entry in `shipSets` still contributes only type mappings,
   as the brief assumed, but its blueprints are extracted and byte-verified too.
5. **The splinter's face offset is wrong in the original.** `ship_splinter.asm` sets the low byte of
   the faces offset to `LO(SHIP_SPLINTER_FACES - SHIP_SPLINTER) + 24`, so the game reads faces from
   offset 68 rather than 44 — i.e. 24 bytes past the splinter's own face data and into whatever
   blueprint follows it (in `D.MOC` that is the start of the Cobra Mk III). The extracted `faces` are
   the bytes the original actually reads, and they differ between `D.MO?` files; the source-declared
   FACE data is emitted separately as `declaredFaces` for a remake that wants a correctly shaded
   splinter.
6. **Face 15 is an "always visible" pseudo-face, not an index.** LL9 sets the face visibility table
   entry for face 15 to 255 so a vertex can be pinned as visible. Vertices in thirteen ships use it
   (normal), and the **alloy plate** uses it for *all* of its vertices *and edges* even though it has
   only one real face. The unit test that requires every edge face index to be inside the face list
   therefore exempts `plate` and pins its behaviour in a separate test.
7. **The missile is not in the `D.MO?` files.** Every `XX21` table points slot 1 at the absolute
   address `&7F00`, where the loader places the contents of `MISSILE.bin`. Its blueprint is verified
   against that file.
8. **`GUARD` is treated as an upper bound that must not be crossed upwards.** The ship files assemble
   at `&5600` with `GUARD &6000` (screen memory); `elite-missile.asm` assembles at `&7F00`, above its
   `GUARD &6000`, and is not affected.
9. **Undefined flags.** `_RELEASED`, `_SOURCE_DISC` and `_BUG_FIX` are never defined by the disc
   build options; they only appear inside `IF _ELITE_A_VERSION` blocks, which are never evaluated for
   the disc variant. The assembler only errors on an unknown symbol if an expression that is actually
   evaluated needs it.
10. **Friendly names come from the `Summary:` comment** with the "Ship blueprint for a/an " prefix
    removed and the first letter capitalised, so `Ship blueprint for an asteroid` becomes
    `Asteroid`. The one alias form, `Dodecahedron ("Dodo") space station`, becomes
    `Dodo (space station)`. The names used by the `XX21` tables are kept separately in `typeNames`
    (`Fer-de-Lance` from the summary vs `Fer-de-lance` from the table, `Thargoid mothership` vs
    `Thargoid`, and so on).
11. **Shared blueprint data.** The Thargon reuses the cargo canister's edges and the splinter reuses
    the escape pod's edges, so their header offsets are negative and their values depend on the file
    layout. The decoded edges/faces are identical across files; `edgesFrom`/`facesFrom` name the label
    the data came from. The canonical JSON entry for a blueprint always comes from the first ship file
    (in `D.MOA`–`D.MOP` order) that defines it.

## JSON schema

```jsonc
{
  "schemaVersion": 1,
  "generator": "EliteDataExtractor",
  "source": { "library", "version", "variant", "buildVersion", "buildVariant",
              "flags": { "_DISC_VERSION": true, ... } },
  "counts": { "ships", "registeredTypes", "shipSets", "shipSetEntries",
              "vertices", "edges", "faces" },
  "shipSets": [
    { "id": "D.MOA", "asmFile", "binary", "baseAddress", "slotCount",
      "entries": [ { "type": 1, "symbol": "MSL", "typeName": "Missile",
                     "label": "SHIP_MISSILE", "ship": "missile" }, ... ] }
  ],
  "ships": [
    {
      "id": "cobra-mk-3", "name": "Cobra Mk III",
      "summary": "Ship blueprint for a Cobra Mk III",
      "label": "SHIP_COBRA_MK_3", "asmFile": "...", "binary": "D.MOB.bin",
      "types": [11], "symbols": ["CYL"], "typeNames": ["Cobra Mk III"],
      "shipSets": ["D.MOB", "D.MOC", ...],
      "edgesFrom": "SHIP_COBRA_MK_3_EDGES", "facesFrom": "SHIP_COBRA_MK_3_FACES",
      "declaredFaces": [ ... ],          // only when it differs from "faces" (splinter)
      "dockedVariant": { "header", "vertices", "edges", "faces" },  // only when it differs
      "header": {
        "maxCanisters", "scoopMarketItem", "canisterByte", "targetableArea",
        "edgesOffset", "facesOffset", "maxEdges", "gunVertex", "explosionCount",
        "vertexCount", "edgeCount", "faceCount", "bounty", "visibilityDistance",
        "maxEnergy", "maxSpeed", "normalScale", "laserPower", "missiles",
        "laserMissileByte"
      },
      "vertices": [ { "x", "y", "z", "faces": [f1, f2, f3, f4], "visibility" } ],
      "edges":    [ { "vertex1", "vertex2", "faces": [f1, f2], "visibility" } ],
      "faces":    [ { "x", "y", "z", "visibility" } ],
      "notes": [ ... ]                    // omitted when empty
    }
  ],
  "notes": [ ... ]
}
```

The packed header bytes keep their original meanings: `maxEdges` is `1 + 4 * maxVisibleEdges`,
`gunVertex` is the vertex index times 4, `explosionCount` is `6 + 4 * nodes`, `normalScale` is the
exponent *n* in `2^n`, `targetableArea` is a square (e.g. `160 * 160`), and `bounty` is in tenths of
a credit. `edgesOffset`/`facesOffset` are file-layout values and are informational; the decoded
`edges`/`faces` arrays are the data to use.

The C# models in `src/EliteRemake.Data/Ships/` mirror this JSON and are loaded from the embedded
resource `EliteRemake.Data.data.ships.json` (`ShipData.All`, `ShipData.ByType(int)`,
`ShipData.ById(string)`, `ShipData.AllForType(int)`, `ShipData.ShipSets`).

## Source library files used

| Purpose | File |
|---|---|
| Build options | `versions/disc/1-source-files/main-sources/elite-build-options.asm` |
| Flight ship sets | `versions/disc/1-source-files/main-sources/elite-ships-a.asm` … `-p.asm` |
| Docked ship set | `library/disc/docked/variable/xx21.asm` |
| Docked ship block | `versions/disc/1-source-files/main-sources/elite-source-docked.asm` (preamble + includes) |
| Missile blueprint | `versions/disc/1-source-files/main-sources/elite-missile.asm` |
| Macros | `library/common/main/macro/{vertex,edge,face}.asm` |
| Blueprints | `library/common/main/variable/ship_*.asm`, `library/enhanced/main/variable/ship_*.asm` |
| Verification binaries | `versions/disc/3-assembled-output/{D.MOA..D.MOP,MISSILE,T.CODE.unprot}.bin` |
