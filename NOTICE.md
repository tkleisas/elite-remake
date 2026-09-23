# Notices

The **code** in this repository is MIT licensed — see [`LICENSE`](LICENSE). The notices below are
about everything the code is *about*, and about the work it was built on.

## Elite

Elite was written by **Ian Bell** and **David Braben** and is copyright © Acornsoft 1984. "Elite" is a
trade mark of the copyright holders.

This is an independent remake. It is not affiliated with, authorised by or endorsed by Ian Bell,
David Braben or the current rights holders, and it is not a distribution of the game.

It ships **no original assets**:

* the ship geometry, the universe, the system and market data and the game's text are *extracted* at
  build time by this repository's own tool from the publicly published assembly sources (see below),
  and are committed here as JSON;
* the artwork is generated at run time from that geometry and from the BBC Micro's own palette;
* the sound effects are synthesized from the original's own sound-chip parameters — the pitch,
  amplitude, duration and envelope bytes in its sound table — rather than sampled from the game.

## Mark Moxon's Elite source code library

The port was written against the annotated disassembly at
<https://github.com/markmoxon/elite-source-code-library>, without which none of this would have been
possible: it is where the routines, the tables and — just as importantly — the commentary on what a
routine is *for* come from. `EliteDataExtractor` reads that library when asked to regenerate
`data/*.json`, but the library itself is not part of this repository and is not redistributed with
it.

The deep dives at <https://www.bbcelite.com> were the reference for how the original's routines
behave, and the archived screenshots of the original game were the check on what its text actually
looks like on screen.

## MonoGame

This project uses [MonoGame](https://monogame.net/) (MonoGame.Framework.DesktopGL), which is licensed
under the Microsoft Public License.
