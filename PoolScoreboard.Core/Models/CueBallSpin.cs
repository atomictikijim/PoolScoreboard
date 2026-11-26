namespace PoolScoreboard.Core.Models;

public class CueBallSpin
{
    public double X { get; set; }
    public double Y { get; set; }

    public CueBallSpin Copy() => new() { X = X, Y = Y };
}
