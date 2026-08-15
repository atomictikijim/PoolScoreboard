using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Overlay.Models;

public static class CueBallStateMapper
{
    public static CueBallStateDto ToDto(GameState state) => new()
    {
        X = state.CueBallSpin?.X,
        Y = state.CueBallSpin?.Y
    };
}
