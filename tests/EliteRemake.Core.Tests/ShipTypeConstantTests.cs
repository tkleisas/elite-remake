using EliteRemake.Core.Sim;
using EliteRemake.Data.Ships;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// Every hardcoded ship type in the core names the ship the blueprints say it does.
/// </summary>
/// <remarks>
/// The core cannot depend on the data files, so it names the ship types it cares about as constants —
/// and a constant that drifts from the data is invisible: it type-checks, it reads correctly, and it
/// silently refers to a different ship. That is what happened to the Thargon, which was 20, the
/// Adder's number, so Thargons could not be scooped at all. This checks all of them at once, which is
/// cheaper than noticing the one that is wrong by flying into it.
/// </remarks>
public class ShipTypeConstantTests
{
    [Fact]
    public void EverNamedShipTypeMatchesTheBlueprint()
    {
        (string Name, int Value, string Ship)[] checks =
        [
            ("Combat.SpaceStationType", Combat.SpaceStationType, "coriolis"),
            ("Combat.ConstrictorType", Combat.ConstrictorType, "constrictor"),
            ("Missions.ConstrictorType", Missions.ConstrictorType, "constrictor"),
            ("Missions.ThargoidType", Missions.ThargoidType, "thargoid"),
            ("Missiles.MissileType", Missiles.MissileType, "missile"),
            ("Tactics.AnacondaType", Tactics.AnacondaType, "anaconda"),
            ("Tactics.WormType", Tactics.WormType, "worm"),
            ("GameSession.CopType", GameSession.CopType, "viper"),
            ("FlightSim.ThargoidType", FlightSim.ThargoidType, "thargoid"),
            ("FlightSim.ThargonType", FlightSim.ThargonType, "thargon"),
            ("Debris.Canister", Debris.Canister, "canister"),
            ("Debris.Boulder", Debris.Boulder, "boulder"),
            ("Debris.Asteroid", Debris.Asteroid, "asteroid"),
            ("Debris.Splinter", Debris.Splinter, "splinter"),
            ("Debris.EscapePod", Debris.EscapePod, "escape-pod"),
            ("Debris.Thargon", Debris.Thargon, "thargon"),
            ("Spawner.PackHunterBase", Spawner.PackHunterBase, "sidewinder"),
            ("Spawner.BountyHunterBase", Spawner.BountyHunterBase, "cobra-mk-3-p"),
        ];

        foreach ((string name, int value, string ship) in checks)
        {
            ShipBlueprint? blueprint = ShipData.ById(ship);
            Assert.NotNull(blueprint);
            Assert.Equal(blueprint!.Types[0], value);
            _ = name;
        }
    }
}
