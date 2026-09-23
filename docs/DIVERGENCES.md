# Divergences from the original

Known, deliberate differences between this remake and BBC Micro disc Elite, and the faithful quirks
that we reproduce on purpose. Anything that changes *behaviour* is listed here; presentation choices
are listed too, so the fidelity of the simulation is never in doubt.

## Faithful quirks reproduced on purpose

| Quirk | Where | Why it matters |
|---|---|---|
| `TIS2`, `TIS1` and `DVID96` are coarse (about 7 bits, error up to ~2 units) because they are restoring divisions that scale into units of 96 | `EliteMath.Tis2`, `Tis1`, `Dvid96` | This coarseness shapes the short-range chart's fuel circle and the way vectors are normalised. Replacing it with exact division would change the game's geometry. |
| `TIDY` clears eight of the nine low bytes of the orientation vectors and leaves `sidev_z_lo` alone (the original's `TIL1` loop starts at `INWK+23`, not `INWK+25`) | `ShipMath.Tidy` | An undocumented quirk of the original; reproduced exactly. |
| `MULT1`/`MLTU2` use the complement trick, so multiplying by zero can leave a ±1 artefact | `EliteMath.Mult1`, `Mltu2` | The artefact propagates into positions and rotations. Faithful, and invisible in play. |
| MVEIT part 5's `y` calculation depends on the carry flag `MLTU2` happens to leave behind (the original does not clear it before `ADC`/`SBC`) | `EliteMath.Mltu2` returns its carry; `ShipMovement.RotateLocationByOurPitchAndRoll` threads it | Reproducing the carry keeps the arithmetic identical to the 6502, including its off-by-one behaviour. |
| Face 15 is the "always visible" pseudo-face, and the alloy plate uses it for every vertex and edge while having one real face | `ShipMesh.NoFace`, renderer | The original sets `XX2+15 = 255` so face 15 is never tested. |
| The splinter's face offset points 24 bytes past its own face data, so the original reads the next blueprint's faces | `data/ships.json` (`faces` vs `declaredFaces`) | We ship what the game actually reads (`faces`); `declaredFaces` keeps the source's intent visible. |
| Keyboard auto-recentre snaps the roll or pitch rate straight to the centre when the opposite key is tapped | `FlightControls.Bump2`/`Redu2` | The original's `DJD` behaviour; it is why tapping the other key stops a roll dead. |

## Deliberate choices

| Area | Choice | Rationale |
|---|---|---|
| Projection | The original's focal length (256 pixels for a 192-pixel-high view) is preserved on the vertical axis, so the vertical field of view is exactly the original's 41.1°; a widescreen window shows more to the sides | Ship sizes, combat ranges and docking distances are unchanged vertically, which is what the game's feel depends on. |
| Ships are solid | Faces are filled and flat-shaded with the blueprint's own normals, with back faces culled | The agreed visual direction: "what 1984 Elite imagined it looked like". Silhouettes match the original's hidden-line output because the same normals decide visibility. |
| Detail edges | Not yet drawn; the original's per-edge visibility values will draw the fine detail lines (engine outlines, landing struts) on close ships | Planned for the flight milestone. |
| Pitch angle rounding | `BET1` adds 4 to the magnitude and assumes the carry is set, where the original inherits the carry from earlier instructions | The carry depends on unrelated code paths in the original; the difference is at most 1 unit in an angle of 0-8, and it is documented here rather than guessed at. |
| Colours | Ships are drawn in light hull greys rather than the original's single wireframe colour | Flat shading needs a base colour; the palette is the BBC Micro's. |
| Bullets and updates | The simulation runs at a fixed 50 Hz step, matching the original's frame rate | Keeps the ported maths in its original units. |
| Start screen | The game opens on a title screen with the game's name, our ship turning in front of it, and a menu: start, a saved commander, a new commander, the controls and the music. The original's title screen has no menu at all | The original's title is a name over a starfield, and the disc asks whether to load a commander in a single prompt. A menu is what a modern player looks for before a game begins, and it is where the control settings and the music belong. Its frame — the name, the starfield, the ship — is the original's. |
| Music | The Blue Danube plays on the title screen, looped, and nowhere else | **The BBC disc version has no music.** It has one sound chip, which the flight loop uses for effects, and the waltz is the Commodore 64 version's, which had a SID to spare. It is here because it is the sound people remember from Elite. The flight loop still owns the single sound channel, so the music stops when the game starts. It can be turned off from the start screen's settings, and the choice is saved. |
| Start screen's START goes into flight | Pressing start puts us in space just outside the station rather than on the station's docked screens | The disc's own flow starts a loaded commander docked. `--dock` still does that, and it is the honest place for a port's fidelity; the title screen's job is to get a modern player flying. |

## Open questions

* Whether the flight loop should also reproduce the original's practice of tidying one ship per frame
  (12 ships over 16 frames) — currently `TIDY` runs on the original's schedule via the main loop
  counter, which we track.
