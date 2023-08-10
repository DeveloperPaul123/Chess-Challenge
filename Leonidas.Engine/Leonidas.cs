using ChessChallenge.API;
using Leonidas.Engine.Evaluation;
using Timer = ChessChallenge.API.Timer;

namespace Leonidas.Engine;

public class Leonidas : IChessBot
{
    private IBoardEvaluator _boardEvaluator;

    public Leonidas(IBoardEvaluator boardEvaluator)
    {
        _boardEvaluator = boardEvaluator;
    }

    private int Search(Board board, Timer timer, int depth, int ply, int alpha, int beta)
    {
        return 0;
    }

    public Move Think(Board board, Timer timer)
    {
        throw new NotImplementedException();
    }
}