using EliteRemake.Core.Sim;
using EliteRemake.Data;
using Xunit;

namespace EliteRemake.Core.Tests;

/// <summary>
/// The mission texts, which the original prints when we dock: the two briefings, the Navy's first
/// contact and the two debriefings.
/// </summary>
/// <remarks>
/// The expected text is the original's own, taken from the screens the game displays rather than
/// from what the token table looks like it should say. That distinction matters, because the tables
/// store their text in capitals and the printer's case rules turn those into the mixed case on
/// screen: "CAPTAIN" prints as "Captain", a word after a full stop keeps its capital, and a comma is
/// not a word break, so the briefing really does read "Greetings Commander Jameson, i am Captain
/// Curruthers" with a lower case i after the comma.
/// </remarks>
public class MissionTextTests
{
    private static string Briefing(int token, int galaxy = 0, string commander = "JAMESON") =>
        DescriptionData.MissionText(token, "LAVE", commander, galaxy, new EliteRandom(12345));

    [Fact]
    public void TheFirstBriefingIsTheOriginalsText()
    {
        string text = Briefing(DescriptionData.MissionTokens.MissionOneBriefing);

        Assert.Contains(
            "Greetings Commander JAMESON, I am Captain Curruthers of Her Majesty's Space Navy and "
            + "I beg a moment of your valuable time.",
            text);
        Assert.Contains("We would like you to do a little job for us.", text);
        Assert.Contains(
            "The ship you see here is a new model, the Constrictor, equiped with a top secret new "
            + "shield generator.",
            text);
        Assert.Contains("Unfortunately it's been stolen.", text);
        Assert.Contains(
            "It went missing from our ship yard on Xeer five months ago and was last seen at Reesdice.",
            text);
        Assert.Contains(
            "Your mission, should you decide to accept it, is to seek and destroy this ship.",
            text);
        Assert.Contains(
            "You are cautioned that only Military Lasers will penetrate the new shields and that "
            + "the Constrictor is fitted with an E.C.M.System.",
            text);
        Assert.Contains("Good Luck, Commander.", text);
        Assert.Contains("MESSAGE ENDS", text);
    }

    [Fact]
    public void TheCaptainAndTheLocationHintFollowTheGalaxy()
    {
        // The original picks the captain and the hint from the galaxy the briefing is given in:
        // jump token 27 prints token 217 plus the galaxy, jump token 28 prints token 220 plus it
        string first = Briefing(DescriptionData.MissionTokens.MissionOneBriefing, galaxy: 0);
        string second = Briefing(DescriptionData.MissionTokens.MissionOneBriefing, galaxy: 1);

        Assert.Contains("Captain Curruthers", first);
        Assert.Contains("was last seen at Reesdice", first);

        Assert.Contains("Captain Fosdyke Smythe", second);
        Assert.Contains("is believed to have jumped to this galaxy", second);
    }

    [Fact]
    public void TheBorrowedStandardTokensReadCorrectly()
    {
        // Jump token 6 switches the printer to the standard token table and jump token 5 switches
        // it back, which is how the briefings borrow phrases: token 10 is "... ONLY {6}MILITARY
        // LASER{5}S WILL PENETRATE ...", so the borrowed phrase and the letter that finishes it
        // come from different tables
        Assert.Contains("Military Lasers", Briefing(DescriptionData.MissionTokens.MissionOneBriefing));
        Assert.Contains("E.C.M.System", Briefing(DescriptionData.MissionTokens.MissionOneBriefing));

        // The debriefing borrows another phrase the same way
        string second = Briefing(DescriptionData.MissionTokens.MissionTwoDebriefing);
        Assert.Contains("please accept this Navy Energy Unit as payment", second);
    }

    [Fact]
    public void TheSecondMissionIsBriefedAndDebriefed()
    {
        string contact = Briefing(DescriptionData.MissionTokens.MissionTwoContact);
        Assert.Contains("We have need of your services again.", contact);
        Assert.Contains("go to Ceerdi you will be briefed", contact);

        string briefing = Briefing(DescriptionData.MissionTokens.MissionTwoBriefing);
        Assert.Contains("I am Agent Blake of Naval Intellegence.", briefing);
        Assert.Contains("our base on Birera", briefing);

        string debriefing = Briefing(DescriptionData.MissionTokens.MissionTwoDebriefing);
        Assert.Contains("Well done Commander.", debriefing);
        Assert.Contains("We did not expect the Thargoids to find out about you.", debriefing);
    }

    [Fact]
    public void TheFirstDebriefingCongratulatesUs()
    {
        string text = Briefing(DescriptionData.MissionTokens.MissionOneDebriefing);

        Assert.Contains("Congratulations Commander!", text);
        Assert.Contains("there will always be a place for you in Her Majesty's Space Navy.", text);
        Assert.Contains("And maybe sooner than you think...", text);
    }

    [Fact]
    public void TheCommanderNameIsPrintedAsItWasSaved()
    {
        // The original's name routine hands each character straight to the printer, so the name
        // keeps whatever case it was saved with rather than being tidied up
        Assert.Contains("Commander MARK,", Briefing(DescriptionData.MissionTokens.MissionOneBriefing, commander: "MARK"));
        Assert.Contains("Commander jameson,", Briefing(DescriptionData.MissionTokens.MissionOneBriefing, commander: "jameson"));
    }

    [Fact]
    public void TheBriefingAsksForTheScreensTheOriginalShows()
    {
        // Jump tokens 22, 24 and 25 are not words: they ask the display to show the ship and wait
        // for a key press, to wait for a key press, and to clear the screen for the INCOMING
        // MESSAGE banner. The printer reports them so the briefing screen can stage them
        (string text, IReadOnlyList<EliteRemake.Core.Text.TokenEvent> events) =
            DescriptionData.MissionTextWithEvents(
                DescriptionData.MissionTokens.MissionOneBriefing,
                "LAVE",
                "JAMESON",
                0,
                new EliteRandom(1));

        Assert.Contains("MESSAGE ENDS", text);

        // Token 10 shows the ship twice: once as the Constrictor is introduced and once while it
        // explains where the ship went. The INCOMING MESSAGE banner is not part of it — the
        // original's briefing routine prints token 216 for that before it prints this
        Assert.Equal(
            [EliteRemake.Core.Text.TokenAction.ShowShip, EliteRemake.Core.Text.TokenAction.ShowShip],
            events.Select(e => e.Action));
        // The positions are where in the text each action falls, in order
        Assert.Equal(events.Select(e => e.Position).Order(), events.Select(e => e.Position));

        // The other four mission texts open on the incoming message banner and end waiting for a
        // key press, which is how the original shows them
        foreach (int token in new[]
                 {
                     DescriptionData.MissionTokens.MissionTwoContact,
                     DescriptionData.MissionTokens.MissionOneDebriefing,
                     DescriptionData.MissionTokens.MissionTwoBriefing,
                     DescriptionData.MissionTokens.MissionTwoDebriefing,
                 })
        {
            (_, IReadOnlyList<EliteRemake.Core.Text.TokenEvent> missionEvents) =
                DescriptionData.MissionTextWithEvents(token, "LAVE", "JAMESON", 0, new EliteRandom(1));

            Assert.Equal(EliteRemake.Core.Text.TokenAction.IncomingMessage, missionEvents[0].Action);
            Assert.Contains(missionEvents, e => e.Action == EliteRemake.Core.Text.TokenAction.WaitForKey);
        }
    }
}

/// <summary>The system adjective, which jump token 17 builds out of the system's name.</summary>
public class SystemAdjectiveTests
{
    [Fact]
    public void TheAdjectiveDropsATrailingVowelAndAddsIan()
    {
        // The original's token 17 removes the last letter if it is a vowel and prints token 153
        // ("IAN"), so LAVE gives LAVIAN and REESDICE gives REESDICIAN
        for (uint seed = 1; seed <= 40; seed++)
        {
            string description = DescriptionData.Describe("LAVE", new EliteRandom(seed));
            Assert.DoesNotContain("LAVEIAN", description);
            Assert.DoesNotContain("Laveian", description);
        }

        // A description that uses the adjective says Lavian, with the capital the original gives it
        bool seenAdjective = false;
        for (uint seed = 1; seed <= 400 && !seenAdjective; seed++)
        {
            seenAdjective = DescriptionData.Describe("LAVE", new EliteRandom(seed)).Contains("Lavian");
        }

        Assert.True(seenAdjective, "some descriptions name the Lavian something or other");
    }
}
