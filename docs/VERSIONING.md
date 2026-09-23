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

A release is a tag; the pipeline does the work. The order matters, because the workflow refuses a tag
that does not agree with the build:

1. Bump `<Version>` in `Directory.Build.props` and commit it.
2. Write the `## [x.y.z]` section in `CHANGELOG.md` and commit it.
3. `dotnet test` locally if you like — the pipeline runs it anyway, before it publishes anything.
4. Tag and push:

```bash
git tag -a v1.0.1 -m "v1.0.1"
git push origin master --follow-tags
```

[`.github/workflows/release.yml`](../.github/workflows/release.yml) then runs four jobs:

| Job | What it refuses to let through |
|---|---|
| `check` | a tag that is not `v<Version>` from `Directory.Build.props`, or a version with no `## [x.y.z]` section in the changelog |
| `test` | a release whose tests are red |
| `package` | a platform whose build does not publish: self-contained `win-x64` and `linux-x64` |
| `release` | — it creates (or updates) the GitHub release, attaches the two archives and a `SHA256SUMS` file, and takes the notes from that changelog section. A tag with a suffix, such as `v1.1.0-rc.1`, is published as a pre-release |

The workflow can also be run by hand with `workflow_dispatch` and a tag name, which rebuilds an
existing release rather than making a new one.

A release should be able to say, of its own build:

* `dotnet test` green, with the count in the changelog;
* the CI's `game-runs` job green, which means the published binary started, drew a frame and reported
  its version;
* a flight smoke test runs (see the README's "Verifying a build").
