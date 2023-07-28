using System;
using System.Collections.Generic;
using ChessChallenge.API;

/// <summary>
/// Helper class to perform chess board evaluation
/// </summary>
internal class ChessEvaluator
{
    // Piece values (material values) - followed the order of the PieceType enum
    private readonly int[] _pieceValues = { 0, 100, 300, 320, 500, 900, 20000 };

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

    private int PieceMobility(Piece piece, Board board)
    {
        var bitBoard = board.GetPieceBitboard(piece.PieceType, piece.IsWhite);
        var count = 0;
        while (bitBoard != 0)
        {
            count++;
            bitBoard &= bitBoard - 1; // reset LS1B
        }

        return count;
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
                score += _pieceValues[(int)pieceType];
                score += GetPieceSquareValue(piece);
                score += PieceMobility(piece, board);
            }

            foreach (var piece in blackPieces)
            {
                score -= _pieceValues[(int)pieceType];
                score -= GetPieceSquareValue(piece);
                score -= PieceMobility(piece, board);
            }
        }

        // TODO: Take into account piece structure
        // TODO: Take into account king safety
        // TODO: Take into account piece board control

        return score;
    }
}

public class MyBot : IChessBot
{
    private readonly ChessEvaluator _chessEvaluator = new();

    // used for move ordering: https://www.chessprogramming.org/MVV-LVA
    // most valuable victim - least valuable attacker note that the order is based on the enumeration
    // order for PieceType as it is used as an index
    // Piece type values: 0-None, 1-Pawn, 2-Knight, 3-Bishop, 4-Rook, 5-Queen, 6-King 
    // mvv -> queen, rook, bishop, knight, pawn (king is not considered)
    private readonly int[] _mvvValues = { 0, 10, 20, 30, 40, 50, 0 };

    // lva -> pawn, knight, bishop, rook, queen, king
    private readonly int[] _lvaValues = { 0, 5, 4, 3, 2, 1, 0 };
    private const sbyte Exact = 0, Lowerbound = -1, Upperbound = 1, Invalid = -2;
#if DEBUG
    private static int _maxDepth = 3;
#else
    private static int _maxDepth = 5;
#endif

//14 bytes per entry, likely will align to 16 bytes due to padding (if it aligns to 32, recalculate max TP table size)
    private struct Transposition
    {
        public Transposition(ulong zobristHash, int score, sbyte depth)
        {
            ZobristHash = zobristHash;
            Evaluation = score;
            Depth = depth;
            Flag = Invalid;
            Move = Move.NullMove;
        }

        public ulong ZobristHash = 0;
        public int Evaluation = 0;
        public sbyte Depth = 0;
        public sbyte Flag = Invalid;
        public Move Move;
    };

    private static ulong _transpositionTableMask = 0x7FFFFF; //4.7 million entries, likely consuming about 151 MB
    private Transposition[] _transpositionTable = new Transposition[_transpositionTableMask + 1];
    private int _maxThinkTime = 5 * 1000; //5 seconds

    //To access
    ref Transposition Lookup(ulong zobristHash)
    {
        return ref _transpositionTable[zobristHash & _transpositionTableMask];
    }

    private int Negamax(Board board, int depth, int alpha, int beta)
    {
        if (depth <= 0)
        {
            return Quiesce(board, alpha, beta, board.IsWhiteToMove);
        }

        ref Transposition transposition = ref _transpositionTable[board.ZobristKey & _transpositionTableMask];
        if (transposition.ZobristHash == board.ZobristKey && transposition.Flag != Invalid &&
            transposition.Depth >= depth)
        {
            if (transposition.Flag == Exact) return transposition.Evaluation;
            else if (transposition.Flag == Lowerbound) alpha = Math.Max(alpha, transposition.Evaluation);
            else if (transposition.Flag == Upperbound) beta = Math.Min(beta, transposition.Evaluation);
            if (alpha >= beta) return transposition.Evaluation;
        }

        // discourage draws
        if (board.IsDraw())
            return -10;
        // really hate to lose
        if (board.IsInCheckmate()) return int.MinValue + board.PlyCount;

        int startingAlpha = alpha;
        var moves = board.GetLegalMoves();
        OrderMoves(ref moves, board);

        var bestScore = int.MinValue + 1;
        var bestMove = moves[0];
        foreach (var move in moves)
        {
            board.MakeMove(move);
            var eval = -Negamax(board, depth - 1, -beta, -alpha);
            board.UndoMove(move);
            if (bestScore < eval)
            {
                bestScore = eval;
                bestMove = move;
            }

            alpha = Math.Max(alpha, bestScore);
            if (alpha >= beta)
            {
                break;
            }
        }

        transposition.Evaluation = bestScore;
        transposition.ZobristHash = board.ZobristKey;
        if (bestScore < startingAlpha) transposition.Flag = Upperbound;
        else if (bestScore >= beta) transposition.Flag = Lowerbound;
        else transposition.Flag = Exact;
        transposition.Depth = (sbyte)depth;
        transposition.Move = bestMove;

        return bestScore;
    }

    private int GetMovePriority(Move move, Board board)
    {
        int priority = 0;
        Transposition tp = _transpositionTable[board.ZobristKey & _transpositionTableMask];
        if (tp.Move == move && tp.ZobristHash == board.ZobristKey) priority += 1000;
        if (move.IsCapture) priority = _mvvValues[(int)move.CapturePieceType] + _lvaValues[(int)move.MovePieceType];
        return priority;
    }

    private void OrderMoves(ref Move[] moves, Board board)
    {
        List<Tuple<Move, int>> orderedMoves = new();
        foreach (Move m in moves) orderedMoves.Add(new Tuple<Move, int>(m, GetMovePriority(m, board)));
        orderedMoves.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        for (int i = 0; i < moves.Length; i++) moves[i] = orderedMoves[i].Item1;
    }

    int Quiesce(Board board, int alpha, int beta, bool isWhite)
    {
        Move[] moves;
        if (board.IsInCheck()) moves = board.GetLegalMoves();
        else
        {
            moves = board.GetLegalMoves(true);
            if (board.IsInCheckmate()) return int.MinValue + board.PlyCount;
            if (moves.Length == 0) return Evaluate(board);
        }

        Transposition transposition = _transpositionTable[board.ZobristKey & _transpositionTableMask];
        if (transposition.ZobristHash == board.ZobristKey && transposition.Flag != Invalid && transposition.Depth >= 0)
        {
            if (transposition.Flag == Exact) return transposition.Evaluation;
            else if (transposition.Flag == Lowerbound) alpha = Math.Max(alpha, transposition.Evaluation);
            else if (transposition.Flag == Upperbound) beta = Math.Min(beta, transposition.Evaluation);
            if (alpha >= beta) return transposition.Evaluation;
        }

        alpha = Math.Max(Evaluate(board), alpha);
        if (alpha >= beta) return beta;

        OrderMoves(ref moves, board);

        foreach (Move m in moves)
        {
            board.MakeMove(m);
            int evaluation = -Quiesce(board, -beta, -alpha, !isWhite);
            board.UndoMove(m);

            alpha = Math.Max(evaluation, alpha);
            if (alpha >= beta) break;
        }

        return alpha;
    }

    private bool ShouldExecuteNextDepth(Timer timer, int maxThinkTime)
    {
        int currentThinkTime = timer.MillisecondsElapsedThisTurn;
        return ((maxThinkTime - currentThinkTime) > currentThinkTime * 3);
    }

    private int Evaluate(Board board)
    {
        return _chessEvaluator.EvaluateBoard(board) * (board.IsWhiteToMove ? 1 : -1);
    }

    public Move Think(Board board, Timer timer)
    {
        var bestMove = _transpositionTable[board.ZobristKey & _transpositionTableMask];
        var bestScore = int.MinValue;

        for (sbyte depth = 1;
             depth <= _maxDepth;
             depth++)
        {
            Negamax(board, _maxDepth, int.MinValue + 1, int.MaxValue - 1);
            bestMove = _transpositionTable[board.ZobristKey & _transpositionTableMask];

            if (!ShouldExecuteNextDepth(timer, _maxThinkTime)) break;
        }

        return bestMove.Move;
    }
}