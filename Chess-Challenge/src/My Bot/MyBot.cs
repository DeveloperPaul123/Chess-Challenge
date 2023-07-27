using System;
using ChessChallenge.API;

/// <summary>
/// Helper class to perform chess board evaluation
/// </summary>
internal class ChessEvaluator
{
    // Piece values (material values)
    private static readonly int PawnValue = 100;
    private static readonly int KnightValue = 320;
    private static readonly int BishopValue = 330;
    private static readonly int RookValue = 500;
    private static readonly int QueenValue = 900;
    private static readonly int KingValue = 20000;

    // Piece square tables (these tables indicate the positional strength of each pieces
    // This is a packed evaluation table of tables (each ulong is a table for a piece type)
    private readonly ulong[,] _packedEvaluationTables =
    {
        { 58233348458073600, 61037146059233280, 63851895826342400, 66655671952007680 },
        { 63862891026503730, 66665589183147058, 69480338950193202, 226499563094066 },
        { 63862895153701386, 69480338782421002, 5867015520979476, 8670770172137246 },
        { 63862916628537861, 69480338782749957, 8681765288087306, 11485519939245081 },
        { 63872833708024320, 69491333898698752, 8692760404692736, 11496515055522836 },
        { 63884885386256901, 69502350490469883, 5889005753862902, 8703755520970496 },
        { 63636395758376965, 63635334969551882, 21474836490, 1516 },
        { 58006849062751744, 63647386663573504, 63625396431020544, 63614422789579264 }
    };

    private int GetPieceValue(PieceType pieceType)
    {
        switch (pieceType)
        {
            case PieceType.None:
                return 0;
            case PieceType.Pawn:
                return PawnValue;
            case PieceType.Knight:
                return KnightValue;
            case PieceType.Bishop:
                return BishopValue;
            case PieceType.Rook:
                return RookValue;
            case PieceType.Queen:
                return QueenValue;
            case PieceType.King:
                return KingValue;
            default:
                throw new ArgumentOutOfRangeException(nameof(pieceType), pieceType, null);
        }
    }

    private int GetPieceSquareValue(Piece piece)
    {
        var file = piece.Square.File;
        var rank = piece.Square.Rank;

        if (file > 3)
            file = 7 - file;
        
        // mirror for black pieces
        if (piece.IsWhite)
            rank = 7 - rank;
        
        var unpackedData =
            unchecked((sbyte)((_packedEvaluationTables[rank, file] >> 8 * ((int)piece.PieceType - 1)) & 0xFF));
        return piece.IsWhite ? unpackedData : -unpackedData;
    }

    public int EvaluateBoard(Board board)
    {
        if (board.IsRepeatedPosition()) return 0;

        var score = 0;

        for (var pieceType = PieceType.Pawn; pieceType <= PieceType.King; pieceType++)
        {
            var whitePieces = board.GetPieceList(pieceType, true);
            var blackPieces = board.GetPieceList(pieceType, false);

            foreach (var piece in whitePieces)
            {
                score += GetPieceValue(pieceType);
                score += GetPieceSquareValue(piece);
            }

            foreach (var piece in blackPieces)
            {
                score -= GetPieceValue(pieceType);
                score -= GetPieceSquareValue(piece);
            }
        }

        // TODO: handle end game versus middle game
        // You can add more evaluation factors such as piece development, king safety, pawn structure, etc.

        return score;
    }
}

public class MyBot : IChessBot
{
    private ChessEvaluator _chessEvaluator = new();

    private int Negamax(Board board, int depth, int alpha, int beta)
    {
        // discourage draws
        if (board.IsDraw()) return 0;
        
        if (depth == 0)
        {
            return Evaluate(board);
        }

        var score = int.MinValue + 1;
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
        return _chessEvaluator.EvaluateBoard(board) * (board.IsWhiteToMove ? 1 : -1);
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