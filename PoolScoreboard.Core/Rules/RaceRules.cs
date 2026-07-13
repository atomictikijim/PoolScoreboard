using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Core.Rules;

public class RaceRules
{
    private readonly League _league;
    private readonly GameType _gameType;

    public RaceRules(League league, GameType gameType)
    {
        _league = league;
        _gameType = gameType;
    }

    /// <summary>
    /// Calculates the race-to value for a player based on league rules and skill level.
    /// Uses official tournament rulebook values.
    /// For opponent-dependent games (APA/TAP 8-ball), returns the race-to assuming same-skill matchup.
    /// </summary>
    public int GetRaceToValue(Player player)
    {
        return (_league, _gameType, player.SkillLevel) switch
        {
            // APA 9-ball: Official points-to-win (points per rack: balls = 1pt each, 9-ball = 2pts)
            (League.APA, GameType.NineBall, 1) => 14,
            (League.APA, GameType.NineBall, 2) => 19,
            (League.APA, GameType.NineBall, 3) => 25,
            (League.APA, GameType.NineBall, 4) => 31,
            (League.APA, GameType.NineBall, 5) => 38,
            (League.APA, GameType.NineBall, 6) => 46,
            (League.APA, GameType.NineBall, 7) => 55,
            (League.APA, GameType.NineBall, 8) => 65,
            (League.APA, GameType.NineBall, >= 9) => 75,

            // APA 8-ball: Games-to-win (opponent-dependent matrix; shown values are for same-skill matchup)
            // Formula: Lower player races to max(skill-1, 2); Higher player depends on lower's skill level
            // Same-skill: SL2→2, SL3→2, SL4→3, SL5→4, SL6→5, SL7→6, SL8→6, SL9→6
            (League.APA, GameType.EightBall, 1) => 2,
            (League.APA, GameType.EightBall, 2) => 2,
            (League.APA, GameType.EightBall, 3) => 2,
            (League.APA, GameType.EightBall, 4) => 3,
            (League.APA, GameType.EightBall, 5) => 4,
            (League.APA, GameType.EightBall, 6) => 5,
            (League.APA, GameType.EightBall, 7) => 6,
            (League.APA, GameType.EightBall, 8) => 6,
            (League.APA, GameType.EightBall, >= 9) => 6,

            // APA 10-ball: Not officially supported by APA; using 9-Ball values as approximation
            (League.APA, GameType.TenBall, 1) => 14,
            (League.APA, GameType.TenBall, 2) => 19,
            (League.APA, GameType.TenBall, 3) => 25,
            (League.APA, GameType.TenBall, 4) => 31,
            (League.APA, GameType.TenBall, 5) => 38,
            (League.APA, GameType.TenBall, 6) => 46,
            (League.APA, GameType.TenBall, 7) => 55,
            (League.APA, GameType.TenBall, 8) => 65,
            (League.APA, GameType.TenBall, >= 9) => 75,

            // USAPL: Uses Fargo rating converted to skill level; estimated race-to-points
            (League.USAPL, GameType.NineBall, 1) => 4,
            (League.USAPL, GameType.NineBall, 2) => 5,
            (League.USAPL, GameType.NineBall, 3) => 6,
            (League.USAPL, GameType.NineBall, 4) => 7,
            (League.USAPL, GameType.NineBall, 5) => 8,
            (League.USAPL, GameType.NineBall, 6) => 9,
            (League.USAPL, GameType.NineBall, 7) => 10,
            (League.USAPL, GameType.NineBall, 8) => 11,
            (League.USAPL, GameType.NineBall, >= 9) => 12,

            (League.USAPL, GameType.EightBall, 1) => 3,
            (League.USAPL, GameType.EightBall, 2) => 3,
            (League.USAPL, GameType.EightBall, 3) => 4,
            (League.USAPL, GameType.EightBall, 4) => 5,
            (League.USAPL, GameType.EightBall, 5) => 6,
            (League.USAPL, GameType.EightBall, 6) => 7,
            (League.USAPL, GameType.EightBall, 7) => 8,
            (League.USAPL, GameType.EightBall, 8) => 9,
            (League.USAPL, GameType.EightBall, >= 9) => 10,

            (League.USAPL, GameType.TenBall, 1) => 4,
            (League.USAPL, GameType.TenBall, 2) => 5,
            (League.USAPL, GameType.TenBall, 3) => 6,
            (League.USAPL, GameType.TenBall, 4) => 7,
            (League.USAPL, GameType.TenBall, 5) => 8,
            (League.USAPL, GameType.TenBall, 6) => 9,
            (League.USAPL, GameType.TenBall, 7) => 10,
            (League.USAPL, GameType.TenBall, 8) => 11,
            (League.USAPL, GameType.TenBall, >= 9) => 12,

            // BCA: Uses Fargo rating converted to skill level; identical to USAPL
            (League.BCA, GameType.NineBall, 1) => 4,
            (League.BCA, GameType.NineBall, 2) => 5,
            (League.BCA, GameType.NineBall, 3) => 6,
            (League.BCA, GameType.NineBall, 4) => 7,
            (League.BCA, GameType.NineBall, 5) => 8,
            (League.BCA, GameType.NineBall, 6) => 9,
            (League.BCA, GameType.NineBall, 7) => 10,
            (League.BCA, GameType.NineBall, 8) => 11,
            (League.BCA, GameType.NineBall, >= 9) => 12,

            (League.BCA, GameType.EightBall, 1) => 3,
            (League.BCA, GameType.EightBall, 2) => 3,
            (League.BCA, GameType.EightBall, 3) => 4,
            (League.BCA, GameType.EightBall, 4) => 5,
            (League.BCA, GameType.EightBall, 5) => 6,
            (League.BCA, GameType.EightBall, 6) => 7,
            (League.BCA, GameType.EightBall, 7) => 8,
            (League.BCA, GameType.EightBall, 8) => 9,
            (League.BCA, GameType.EightBall, >= 9) => 10,

            (League.BCA, GameType.TenBall, 1) => 4,
            (League.BCA, GameType.TenBall, 2) => 5,
            (League.BCA, GameType.TenBall, 3) => 6,
            (League.BCA, GameType.TenBall, 4) => 7,
            (League.BCA, GameType.TenBall, 5) => 8,
            (League.BCA, GameType.TenBall, 6) => 9,
            (League.BCA, GameType.TenBall, 7) => 10,
            (League.BCA, GameType.TenBall, 8) => 11,
            (League.BCA, GameType.TenBall, >= 9) => 12,

            // TAP: Decimal-based rating system; using skill-level approximation
            (League.TAP, GameType.NineBall, 1) => 12,
            (League.TAP, GameType.NineBall, 2) => 16,
            (League.TAP, GameType.NineBall, 3) => 21,
            (League.TAP, GameType.NineBall, 4) => 27,
            (League.TAP, GameType.NineBall, 5) => 33,
            (League.TAP, GameType.NineBall, 6) => 40,
            (League.TAP, GameType.NineBall, 7) => 48,
            (League.TAP, GameType.NineBall, 8) => 57,
            (League.TAP, GameType.NineBall, >= 9) => 67,

            // TAP 8-ball: Similar to APA 8-ball (opponent-dependent matrix)
            (League.TAP, GameType.EightBall, 1) => 2,
            (League.TAP, GameType.EightBall, 2) => 2,
            (League.TAP, GameType.EightBall, 3) => 2,
            (League.TAP, GameType.EightBall, 4) => 3,
            (League.TAP, GameType.EightBall, 5) => 4,
            (League.TAP, GameType.EightBall, 6) => 5,
            (League.TAP, GameType.EightBall, 7) => 6,
            (League.TAP, GameType.EightBall, 8) => 6,
            (League.TAP, GameType.EightBall, >= 9) => 6,

            (League.TAP, GameType.TenBall, 1) => 12,
            (League.TAP, GameType.TenBall, 2) => 16,
            (League.TAP, GameType.TenBall, 3) => 21,
            (League.TAP, GameType.TenBall, 4) => 27,
            (League.TAP, GameType.TenBall, 5) => 33,
            (League.TAP, GameType.TenBall, 6) => 40,
            (League.TAP, GameType.TenBall, 7) => 48,
            (League.TAP, GameType.TenBall, 8) => 57,
            (League.TAP, GameType.TenBall, >= 9) => 67,

            _ => 5
        };
    }

    /// <summary>
    /// Returns true if the race-to value is opponent-dependent (varies by opponent skill level).
    /// </summary>
    public bool IsRaceToOpponentDependent => _gameType == GameType.EightBall && (_league == League.APA || _league == League.TAP);

    /// <summary>
    /// Calculates the race-to value for a player against a specific opponent skill level.
    /// For opponent-dependent games (APA/TAP 8-Ball), this gives the accurate race-to.
    /// Uses official APA 8-Ball Games Must Win matrix rules.
    /// </summary>
    public int GetRaceToValueAgainstOpponent(int playerSkillLevel, int opponentSkillLevel)
    {
        if (!IsRaceToOpponentDependent)
            return GetRaceToValue(new Player { SkillLevel = playerSkillLevel, League = _league });

        int lower = Math.Min(playerSkillLevel, opponentSkillLevel);
        int higher = Math.Max(playerSkillLevel, opponentSkillLevel);

        // Calculate lower player's race-to
        int lowerPlayerRace;
        if (lower == 2)
        {
            lowerPlayerRace = 2;
        }
        else if (higher == 7 && lower >= 5)
        {
            // Special case: when playing against SL7, SL5+ drop by 2
            lowerPlayerRace = lower - 2;
        }
        else
        {
            lowerPlayerRace = Math.Max(lower - 1, 2);
        }

        // Calculate higher player's race-to
        int higherPlayerRace;
        if (lower == 2)
        {
            higherPlayerRace = higher;  // Lower is 2, higher races to their own level
        }
        else if (lower == 3)
        {
            higherPlayerRace = higher - 1;  // Lower is 3, higher races to their level - 1
        }
        else if (higher >= 6)
        {
            // Higher skill 6+, lower skill 4+: cap higher player at 5
            higherPlayerRace = Math.Min(higher - 1, 5);
        }
        else
        {
            // Lower skill 4+, higher skill 4-5: standard reduction
            higherPlayerRace = higher - 1;
        }

        return playerSkillLevel == lower ? lowerPlayerRace : higherPlayerRace;
    }

    /// <summary>
    /// Determines if a player has won based on their score and the opponent's skill level.
    /// </summary>
    public bool IsPlayerWinner(Player player, int playerScore, int opponentScore)
    {
        int raceToValue = GetRaceToValue(player);
        return playerScore >= raceToValue;
    }
}
