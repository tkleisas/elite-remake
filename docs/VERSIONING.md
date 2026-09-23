# Versioning

This project uses [semantic versioning](https://semver.org/spec/v2.0.0.html): `MAJOR.MINOR.PATCH`.

The number is written in exactly one place — `<Version>` in [`Directory.Build.props`](../Directory.Build.props) —
and [`GameVersion`](../src/EliteRemake.Core/GameVersion.cs) reads it back off the assembly at run
time, so the title screen and the build cannot disagree. Every release is a git tag `vMAJOR.MINOR.PATCH`
on `master`, and every tag has an entry in [`CHANGELOG.md`](../CHANGELOG.md).

## What the numbers mean here

A remake is not a library, so "breaking change" needs translating. For this project:

| Part | Bumped when | Examples |
|---|---|---|
| **MAJOR** | The port is complete against the disc version, or the shape of the game changes for a player (a save file that no longer loads, a screen reorganised so muscle memory breaks) | `1.0.0` — the first release: the whole flight and docked loop, playable end to end. `2.0.0` would be the port declared complete against the original's 447 subroutines |
| **MINOR** | Something the original has and we did not: a mechanic, a screen, a mission, a sound, a piece of the interface | the mission briefing screens, the ship's catalogue of close-range detail lines, the death sequence |
| **PATCH** | A fix to something we already claimed to do, with no new behaviour: a wrong constant, a rule running on the wrong clock, a rendering fault | the recharge rate running eight times too fast, the docking range tested as a distance instead of per axis, the black triangle on the start screen's ship |

Two consequences worth stating plainly:

* **Fidelity fixes are PATCHes, not MINORs**, even when they change how the game plays. They are not
  new features; they are the port becoming *more* like the original, which is the whole point.
  A player who liked the wrong behaviour is out of luck, and the changelog says so.
* **Deliberate divergences are not bugs to fix later.** They are listed in
  [`DIVERGENCES.md`](DIVERGENCES.md) with the reason, and changing one of them is a MINOR (or a
  MAJOR if it changes saved state), because it changes what the project claims to be.

## Where the version appears

* The title screen, bottom left, as `v1.0.0`.
* The title screen's status line (`Title: version 1.0.0, ...`), which the screenshot harness prints
  so an automated run can say which build it exercised.
* The assembly informational version, so `EliteRemake.Game.dll` and any bug report carry it too.

## Releases

```
# the version has already been bumped in Directory.Build.props, tests are green, changelog written
git tag -a v1.0.0 -m "v1.0.0"
git push origin master --follow-tags
```

A release should be able to say, of its own build:

* `dotnet test` green, with the count in the changelog;
* the screenshot harness runs (`dotnet run --project src/EliteRemake.Game -- --title --screenshot ...`);
* a flight smoke test runs (see the README's "Verifying a build").
