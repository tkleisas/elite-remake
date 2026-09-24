# Routine coverage

What the disc version of Elite is made of, and what this port carries. The disc's own
subroutines come from the source library's `common/main` — the flight and docked code the disc
shares with the cassette and other versions — and the disc docked code's own routines, plus the
disc loaders. Each is marked:

| Marking | What it means |
|---|---|
| **Ported** | The port names the routine and carries its behaviour — the flight model, the flight loop's parts, the mission machinery, the screens. |
| **Carried whole** | The port reimplements the routine's work under a modern name or as a shared helper: the 6502's drawing primitives, the fixed-point helpers, the text printer's mechanics. The behaviour exists; the name is the disc's rather than the port's. |
| **Out of scope** | The machine's own machinery: the disc loader's filing-system plumbing and the protection routines, which a .NET port has no equivalent of. |

The library carries 382 routine files across the disc's flight, docked and loader
code. **112 are named in the port** — the measure the README quotes — and the rest are
the disc's own machinery: its drawing and text primitives, its sound and keyboard plumbing, and
the loader. Those are not omissions one by one; they are the machinery the port carries under
modern names. A per-routine pass marking each *carried whole* against its behaviour is the work
this table was measured to make visible.

## Named in the port

* **ABORT** — Unarm missiles and update the dashboard indicators
* **ANGRY** — If this is a space station then make it hostile, or if this is a
* **BEEP** — Make a short, high beep
* **BRIEF** — Start mission 1 and show the mission briefing
* **BRIEF2** — Start mission 2
* **BRIEF3** — Receive the briefing and plans for mission 2
* **BRIS** — Clear the screen, display "INCOMING MESSAGE" and wait for 2
* **BUMP2** — Bump up the value of the pitch or roll dashboard indicator
* **CIRCLE2** — Draw a circle (for the planet or chart)
* **COMPAS** — Update the compass
* **CTRL** — Scan the keyboard to see if CTRL is currently pressed
* **DCS1** — Calculate the vector from the ideal docking position to the ship
* **DEATH** — Display the death screen
* **DEATH2** — Reset most of the game and restart from the title screen
* **DEBRIEF** — Finish mission 1
* **DEBRIEF2** — Finish mission 2
* **DELAY** — Wait for a specified time, in 1/50s of a second
* **DENGY** — Drain some energy from the energy banks
* **DET1** — Show or hide the dashboard (for when we die)
* **DETOK** — Print an extended recursive token from the TKN1 token table
* **DOCKIT** — Apply docking manoeuvres to the ship in INWK
* **DOENTRY** — Dock at the space station, show the ship hangar and work out any
* **DOEXP** — Draw an exploding ship
* **DOKEY** — Scan for the seven primary flight controls
* **DORND** — Generate random numbers
* **ECBLB** — Light up the E.C.M. indicator bulb ("E") on the dashboard
* **ECBLB2** — Start up the E.C.M. (light up the indicator, start the countdown
* **ECMOF** — Switch off the E.C.M. and turn off the dashboard bulb
* **EQSHP** — Show the Equip Ship screen (red key f3)
* **ESCAPE** — Launch our escape pod
* **EXNO2** — Process us making a kill
* **EXNO3** — Make an explosion sound
* **FAROF** — Compare x_hi, y_hi and z_hi with 224
* **FAROF2** — Compare x_hi, y_hi and z_hi with A
* **FLIP** — Reflect the stardust particles in the screen diagonal and redraw
* **FMLTU** — Calculate A = A * Q / 256
* **FRMIS** — Fire a missile from our ship
* **FRS1** — Launch a ship straight ahead of us, below the laser sights
* **GTHG** — Spawn a Thargoid ship and a Thargon companion
* **HALL** — Draw the ships in the ship hangar, then draw the hangar
* **HAS1** — Draw a ship in the ship hangar
* **HFS2** — Draw the launch or hyperspace tunnel
* **HITCH** — Work out if the ship in INWK is in our crosshairs
* **HME2** — Search the galaxy for a system
* **KILLSHP** — Remove a ship from our local bubble of universe
* **LASLI** — Draw the laser lines for when we fire our lasers
* **LAUN** — Make the launch sound and draw the launch tunnel
* **LL118** — Move a point along a line until it is on-screen
* **LL164** — Make the hyperspace sound and draw the hyperspace tunnel
* **LOOK1** — Initialise the space view
* **MAS1** — Add an orientation vector coordinate to an INWK coordinate
* **MAS2** — Calculate a cap on the maximum distance to the planet or sun
* **MAS3** — Calculate A = x_hi^2 + y_hi^2 + z_hi^2 in the K% block
* **MAS4** — Calculate a cap on the maximum distance to a ship
* **MCASH** — Add an amount of cash to the cash pot
* **MESS** — Display an in-flight message
* **MLTU2** — Calculate (A P+1 P) = (A ~P) * Q
* **MU11** — Calculate (A P) = P * X
* **MULT1** — Calculate (A P) = Q * A
* **MULT12** — Calculate (S R) = Q * A
* **MULTU** — Calculate (A P) = P * Q
* **MV40** — Rotate the planet or sun's location in space by the amount of
* **MVEIT** — Move current ship: Redraw on scanner, if it hasn't been destroyed
* **MVS4** — Apply pitch and roll to an orientation vector
* **MVS5** — Apply a 3.6 degree pitch or roll to an orientation vector
* **MVT1** — Calculate (x_sign x_hi x_lo) = (x_sign x_hi x_lo) + (A R)
* **MVT6** — Calculate (A P+2 P+1) = (x_sign x_hi x_lo) + (A P+2 P+1)
* **NORM** — Normalise the three-coordinate vector in XX15
* **NWSHP** — Add a new ship to our local bubble of universe
* **NWSPS** — Add a new space station to our local bubble of universe
* **NWSTARS** — Initialise the stardust field
* **OOPS** — Take some damage
* **PAS1** — Display a rotating ship at space coordinates (0, 112, 256) and
* **PAUSE** — Display a rotating ship, waiting until a key is pressed, then
* **PDESC** — Print the system's extended description or a mission 1 directive
* **PLANET** — Draw the planet or sun
* **PLS1** — Calculate (Y A) = nosev_x / z
* **PLS2** — Draw a half-ellipse
* **PLS5** — Calculate roofv_x / z and roofv_y / z
* **PLUT** — Flip the coordinate axes for the four different views
* **REDU2** — Reduce the value of the pitch or roll dashboard indicator
* **RES2** — Reset a number of flight variables and workspaces
* **RESET** — Reset most variables
* **SCAN** — Display the current ship on the scanner
* **SESCP** — Spawn an escape pod from the current (parent) ship
* **SFRMIS** — Add an enemy missile to our local bubble of universe
* **SFS1** — Spawn a child ship from the current (parent) ship
* **SHPPT** — Draw a distant ship as a point rather than a full wireframe
* **SIGHT** — Draw the laser crosshairs
* **SOLAR** — Set up various aspects of arriving in a new system
* **SPBLB** — Light up the space station indicator ("S") on the dashboard
* **SQUA** — Clear bit 7 of A and calculate (A P) = A * A
* **SQUA2** — Calculate (A P) = A * A
* **STATUS** — Show the Status Mode screen (red key f8)
* **TACTICS** — Apply tactics: Set pitch, roll, and acceleration
* **THERE** — Check whether we are in the Constrictor's system in mission 1
* **TIDY** — Orthonormalise the orientation vectors for a ship
* **TIS1** — Calculate (A ?) = (-X * A + (S R)) / 96
* **TIS2** — Calculate A = A / Q
* **TIS3** — Calculate -(nosev_1 * roofv_1 + nosev_2 * roofv_2) / nosev_3
* **TT110** — Launch from a station or show the front space view
* **TT111** — Set the current system to the nearest system to a point
* **TT151** — Print the name, price and availability of a market item
* **TT18** — Try to initiate a jump into hyperspace
* **TT20** — Twist the selected system's seeds four times
* **TT24** — Calculate system data from the system seeds
* **TT25** — Show the Data on System screen (red key f6)
* **TT26** — Print a character at the text cursor, with support for verified
* **TT27** — Print a text token
* **TT54** — Twist the selected system's seeds
* **WARP** — Perform an in-system jump
* **ZINF** — Reset the INWK workspace and orientation vectors

## Not separately named (270)

These are the disc's drawing, text, sound and loader machinery — PX2 and PXL's pixel
plotters, HLOIN's and BLINE's line rasterisers, the OSWORD sound plumbing, the filed-code
loaders — whose work the port does with modern primitives: a GPU triangle rasteriser, a
System.Text.Json save, MonoGame's sound buffers. The behaviour is carried; the 6502 routines
that did it are not, and were never the port's unit of work.

