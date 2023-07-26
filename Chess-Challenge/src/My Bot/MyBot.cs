using System;
using ChessChallenge.API;

public class MyBot : IChessBot
{
    private int Negamax(Board board, int depth, int alpha, int beta)
    {
        if (depth == 0 || board.IsInCheckmate() || board.IsDraw())
        {
            return Evaluate(board);
        }

        var score = int.MinValue;
        foreach (var move in board.GetLegalMoves())
        {
            board.MakeMove(move);
            score = Math.Max(score, -Negamax(board, depth - 1, -beta, -alpha));
            board.UndoMove(move);
            alpha = Math.Max(alpha, score);
            if (alpha >= beta)
            {
                break;
            }
        }

        return score;
    }

    private int PopulationCount(Board board, bool isWhite)
    {
        var count = 0;
        var bitBoard = isWhite ? board.WhitePiecesBitboard : board.BlackPiecesBitboard;
        while (bitBoard != 0)
        {
            count++;
            bitBoard &= bitBoard - 1; // reset LS1B
        }
        return count;
    }

    private int Evaluate(Board board)
    {
        var whiteKings = board.GetPieceList(PieceType.King, true);
        var blackKings = board.GetPieceList(PieceType.King, false);

        var whiteQueens = board.GetPieceList(PieceType.Queen, true);
        var blackQueens = board.GetPieceList(PieceType.Queen, false);

        var whiteRooks = board.GetPieceList(PieceType.Rook, true);
        var blackRooks = board.GetPieceList(PieceType.Rook, false);

        var whiteBishops = board.GetPieceList(PieceType.Bishop, true);
        var blackBishops = board.GetPieceList(PieceType.Bishop, false);

        var whiteKnights = board.GetPieceList(PieceType.Knight, true);
        var blackKnights = board.GetPieceList(PieceType.Knight, false);

        var whitePawns = board.GetPieceList(PieceType.Pawn, true);
        var blackPawns = board.GetPieceList(PieceType.Pawn, false);

        var materialScore = 200 * (whiteKings.Count - blackKings.Count) +
                            9 * (whiteQueens.Count - blackQueens.Count) +
                            5 * (whiteRooks.Count - blackRooks.Count) +
                            3 * ((whiteBishops.Count - blackBishops.Count) + (whiteKnights.Count - blackKnights.Count)) +
                            whitePawns.Count - blackPawns.Count;

        // calculate mobility
        var whiteMobility = PopulationCount(board, true);
        var blackMobility = PopulationCount(board, false);

        var mobilityScore = 1 * (whiteMobility - blackMobility);

        return (materialScore + mobilityScore) * (board.IsWhiteToMove ? 1 : -1);
    }

    public Move Think(Board board, Timer timer)
    {
        var moves = board.GetLegalMoves();
        var bestMove = new Move();
        var bestScore = int.MinValue;
        
        foreach (var move in moves)
        {
            // make move toggles IsWhiteToMove
            board.MakeMove(move);
            var score = -Negamax(board, 3, int.MinValue + 1, int.MaxValue - 1);
            board.UndoMove(move);

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }
        }
        return bestMove;
    }
}
