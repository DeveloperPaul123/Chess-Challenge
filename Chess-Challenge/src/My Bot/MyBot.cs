using System;
using System.Collections.Generic;
using ChessChallenge.API;

public abstract class IBoardEvaluator
{
    public abstract int Evaluate(Board board);
}

internal class PestoEvaluation : IBoardEvaluator
{
    // PEsTo eval
    // https://www.chessprogramming.org/PeSTO%27s_Evaluation_Function
    private readonly int[] _pieceValues = { 100, 310, 330, 500, 1000, 10000 };
    private readonly int[] _piecePhase = { 0, 1, 1, 2, 4, 0 };

    private static readonly ulong[] CompressedPestoValues =
    {
        657614902731556116, 420894446315227099, 384592972471695068, 312245244820264086, 364876803783607569,
        366006824779723922, 366006826859316500, 786039115310605588, 421220596516513823, 366011295806342421,
        366006826859316436, 366006896669578452, 162218943720801556, 440575073001255824, 657087419459913430,
        402634039558223453, 347425219986941203, 365698755348489557, 311382605788951956, 147850316371514514,
        329107007234708689, 402598430990222677, 402611905376114006, 329415149680141460, 257053881053295759,
        291134268204721362, 492947507967247313, 367159395376767958, 384021229732455700, 384307098409076181,
        402035762391246293, 328847661003244824, 365712019230110867, 366002427738801364, 384307168185238804,
        347996828560606484, 329692156834174227, 365439338182165780, 386018218798040211, 456959123538409047,
        347157285952386452, 365711880701965780, 365997890021704981, 221896035722130452, 384289231362147538,
        384307167128540502, 366006826859320596, 366006826876093716, 366002360093332756, 366006824694793492,
        347992428333053139, 457508666683233428, 329723156783776785, 329401687190893908, 366002356855326100,
        366288301819245844, 329978030930875600, 420621693221156179, 422042614449657239, 384602117564867863,
        419505151144195476, 366274972473194070, 329406075454444949, 275354286769374224, 366855645423297932,
        329991151972070674, 311105941360174354, 256772197720318995, 365993560693875923, 258219435335676691,
        383730812414424149, 384601907111998612, 401758895947998613, 420612834953622999, 402607438610388375,
        329978099633296596, 67159620133902
    };

    private int GetPestoValue(int index)
    {
        return (int)(((CompressedPestoValues[index / 10] >> (6 * (index % 10))) & 63) - 20) * 8;
    }

    public override int Evaluate(Board board)
    {
        var mg = 0;
        var eg = 0;
        var phase = 0;

        for (sbyte b = 0; b <= 1; b++)
        {
            // evaluate white first
            var isWhite = b == 0;
            for (var piece = PieceType.Pawn; piece <= PieceType.King; piece++)
            {
                var mask = board.GetPieceBitboard(piece, isWhite);
                while (mask != 0)
                {
                    phase += _piecePhase[(int)piece - 1];
                    var index = 128 * ((int)piece - 1) + BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^
                                (isWhite ? 56 : 0);
                    mg += GetPestoValue(index) + _pieceValues[(int)piece - 1];
                    eg += GetPestoValue(index + 64) + _pieceValues[(int)piece - 1];
                }
            }

            mg = -mg;
            eg = -eg;
        }

        return (mg * phase + eg * (24 - phase)) / 24;
        // return (mg * phase + eg * (24 - phase)) / 24 * (board.IsWhiteToMove ? 1 : -1);
    }
}

internal class SimpleEvaluation : IBoardEvaluator
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

    public override int Evaluate(Board board)
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
    private readonly IBoardEvaluator _chessEvaluator;

    // used for move ordering: https://www.chessprogramming.org/MVV-LVA
    // most valuable victim - least valuable attacker note that the order is based on the enumeration
    // order for PieceType as it is used as an index
    // Piece type values: 0-None, 1-Pawn, 2-Knight, 3-Bishop, 4-Rook, 5-Queen, 6-King 
    // mvv -> queen, rook, bishop, knight, pawn (king is not considered)
    private readonly int[] _mvvValues = { 0, 10, 20, 30, 40, 50, 0 };

    // lva -> pawn, knight, bishop, rook, queen, king
    private readonly int[] _lvaValues = { 0, 5, 4, 3, 2, 1, 0 };
    private const sbyte Exact = 0, LowerBound = -1, UpperBound = 1, Invalid = -2;
#if DEBUG
    private const int MaxDepth = 3;
#else
    private const int MaxDepth = 6;
#endif

//14 bytes per entry, likely will align to 16 bytes due to padding (if it aligns to 32, recalculate max TP table size)
    private struct Transposition
    {
        public ulong ZobristHash = 0;
        public int Evaluation = 0;
        public sbyte Depth = 0;
        public sbyte Flag = Invalid;
        public Move Move = default;

        public Transposition()
        {
        }
    };

    private const ulong TranspositionTableMask = 0x7FFFFF; //4.7 million entries, likely consuming about 151 MB
    private readonly Transposition[] _transpositionTable = new Transposition[TranspositionTableMask + 1];

    public MyBot(IBoardEvaluator chessEvaluator)
    {
        _chessEvaluator = chessEvaluator;
    }

    private const int MaxThinkTime = 10 * 1000; //5 seconds

    private int Negamax(Board board, int depth, int alpha, int beta)
    {
        if (depth <= 0)
        {
            return Quiesce(board, alpha, beta);
        }

        ref Transposition transposition = ref _transpositionTable[board.ZobristKey & TranspositionTableMask];
        if (transposition.ZobristHash == board.ZobristKey && transposition.Flag != Invalid &&
            transposition.Depth >= depth)
        {
            if (transposition.Flag == Exact) return transposition.Evaluation;
            else if (transposition.Flag == LowerBound) alpha = Math.Max(alpha, transposition.Evaluation);
            else if (transposition.Flag == UpperBound) beta = Math.Min(beta, transposition.Evaluation);
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
        if (bestScore < startingAlpha) transposition.Flag = UpperBound;
        else if (bestScore >= beta) transposition.Flag = LowerBound;
        else transposition.Flag = Exact;
        transposition.Depth = (sbyte)depth;
        transposition.Move = bestMove;

        return bestScore;
    }

    private int GetMovePriority(Move move, Board board)
    {
        int priority = 0;
        Transposition tp = _transpositionTable[board.ZobristKey & TranspositionTableMask];
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

    private int Quiesce(Board board, int alpha, int beta)
    {
        Move[] moves;
        if (board.IsInCheck()) moves = board.GetLegalMoves();
        else
        {
            moves = board.GetLegalMoves(true);
            if (board.IsInCheckmate()) return int.MinValue + board.PlyCount;
            if (moves.Length == 0) return Evaluate(board);
        }

        alpha = Math.Max(Evaluate(board), alpha);
        if (alpha >= beta) return beta;

        OrderMoves(ref moves, board);

        foreach (Move m in moves)
        {
            board.MakeMove(m);
            int evaluation = -Quiesce(board, -beta, -alpha);
            board.UndoMove(m);

            alpha = Math.Max(evaluation, alpha);
            if (alpha >= beta) break;
        }

        return alpha;
    }

    private static bool ShouldExecuteNextDepth(Timer timer, int maxThinkTime)
    {
        var currentThinkTime = timer.MillisecondsElapsedThisTurn;
        return ((maxThinkTime - currentThinkTime) > currentThinkTime * 3);
    }

    private int Evaluate(Board board)
    {
        return _chessEvaluator.Evaluate(board) * (board.IsWhiteToMove ? 1 : -1);
    }

    public Move Think(Board board, Timer timer)
    {
        var bestMove = _transpositionTable[board.ZobristKey & TranspositionTableMask];

        for (sbyte depth = 1;
             depth <= MaxDepth;
             depth++)
        {
            Negamax(board, MaxDepth, int.MinValue + 1, int.MaxValue - 1);
            bestMove = _transpositionTable[board.ZobristKey & TranspositionTableMask];

            if (!ShouldExecuteNextDepth(timer, MaxThinkTime)) break;
        }

        return bestMove.Move;
    }
}
