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
    /// </summary>
    public int GetRaceToValue(Player player)
    {
        return (_league, _gameType, player.SkillLevel) switch
        {
            // APA 9-ball rules
            (League.APA, GameType.NineBall, >= 7) => 9,
            (League.APA, GameType.NineBall, >= 4) => 7,
            (League.APA, GameType.NineBall, _) => 5,

            // APA 8-ball rules
            (League.APA, GameType.EightBall, >= 7) => 8,
            (League.APA, GameType.EightBall, >= 4) => 6,
            (League.APA, GameType.EightBall, _) => 4,

            // APA 10-ball rules
            (League.APA, GameType.TenBall, >= 7) => 10,
            (League.APA, GameType.TenBall, >= 4) => 8,
            (League.APA, GameType.TenBall, _) => 6,

            // USAPL 9-ball rules
            (League.USAPL, GameType.NineBall, >= 6) => 9,
            (League.USAPL, GameType.NineBall, >= 4) => 7,
            (League.USAPL, GameType.NineBall, _) => 5,

            // USAPL 8-ball rules
            (League.USAPL, GameType.EightBall, >= 6) => 8,
            (League.USAPL, GameType.EightBall, >= 4) => 6,
            (League.USAPL, GameType.EightBall, _) => 4,

            // USAPL 10-ball rules
            (League.USAPL, GameType.TenBall, >= 6) => 10,
            (League.USAPL, GameType.TenBall, >= 4) => 8,
            (League.USAPL, GameType.TenBall, _) => 6,

            // BCA 9-ball rules
            (League.BCA, GameType.NineBall, >= 6) => 9,
            (League.BCA, GameType.NineBall, >= 4) => 7,
            (League.BCA, GameType.NineBall, _) => 5,

            // BCA 8-ball rules
            (League.BCA, GameType.EightBall, >= 6) => 8,
            (League.BCA, GameType.EightBall, >= 4) => 6,
            (League.BCA, GameType.EightBall, _) => 4,

            // BCA 10-ball rules
            (League.BCA, GameType.TenBall, >= 6) => 10,
            (League.BCA, GameType.TenBall, >= 4) => 8,
            (League.BCA, GameType.TenBall, _) => 6,

            // TAP 9-ball rules
            (League.TAP, GameType.NineBall, >= 6) => 9,
            (League.TAP, GameType.NineBall, >= 4) => 7,
            (League.TAP, GameType.NineBall, _) => 5,

            // TAP 8-ball rules
            (League.TAP, GameType.EightBall, >= 6) => 8,
            (League.TAP, GameType.EightBall, >= 4) => 6,
            (League.TAP, GameType.EightBall, _) => 4,

            // TAP 10-ball rules
            (League.TAP, GameType.TenBall, >= 6) => 10,
            (League.TAP, GameType.TenBall, >= 4) => 8,
            (League.TAP, GameType.TenBall, _) => 6,

            _ => 5
        };
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
