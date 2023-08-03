using System;
using System.Linq;
using ChessChallenge.API;

public class MyBotv2 : IChessBot
{
    // PeSTO evaluation
    // https://www.chessprogramming.org/PeSTO%27s_Evaluation_Function
    private readonly int[] _pieceValues =
    {
        0, 82, 337, 365, 477, 1025, 20000,
        0, 94, 281, 297, 512, 936, 20000
    };

    private readonly int[] _piecePhase = { 0, 0, 1, 1, 2, 4, 0 };

    // This is all the pesto value tables compressed into a single array
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

    // used for move ordering: https://www.chessprogramming.org/MVV-LVA
    // most valuable victim - least valuable attacker note that the order is based on the enumeration
    // order for PieceType as it is used as an index
    // Piece type values: 0-None, 1-Pawn, 2-Knight, 3-Bishop, 4-Rook, 5-Queen, 6-King 
    // mvv -> queen, rook, bishop, knight, pawn (king is not considered)
    private readonly int[] _mvvValues = { 0, 10, 20, 30, 40, 50, 0 };

    // lva -> pawn, knight, bishop, rook, queen, king
    private readonly int[] _lvaValues = { 0, 5, 4, 3, 2, 1, 0 };

    // match types for transposition table
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

    // 4.7 million entries, likely consuming about 151 MB
    private const ulong TranspositionTableMask = 0x7FFFFF;
    private readonly Transposition[] _transpositionTable = new Transposition[TranspositionTableMask + 1];

    // maximum think time for each move 
    // private const int MaxThinkTime = 10 * 1000;

    /// <summary>
    /// Negamax search with alpha-beta pruning and transposition table
    /// </summary>
    /// <param name="board"></param>
    /// <param name="depth"></param>
    /// <param name="alpha"></param>
    /// <param name="beta"></param>
    /// <returns></returns>
    private int Search(Board board, int depth, int alpha, int beta)
    {
        if (depth <= 0) return Quiesce(board, alpha, beta);

        ref var transposition = ref _transpositionTable[board.ZobristKey & TranspositionTableMask];
        if (transposition.ZobristHash == board.ZobristKey && transposition.Flag != Invalid &&
            transposition.Depth >= depth)
        {
            switch (transposition.Flag)
            {
                case Exact:
                    return transposition.Evaluation;
                case LowerBound:
                    alpha = Math.Max(alpha, transposition.Evaluation);
                    break;
                case UpperBound:
                    beta = Math.Min(beta, transposition.Evaluation);
                    break;
            }

            if (alpha >= beta) return transposition.Evaluation;
        }

        // discourage draws
        if (board.IsDraw())
            return -10;
        // really hate to lose
        if (board.IsInCheckmate()) return int.MinValue + board.PlyCount;

        var startingAlpha = alpha;
        var moves = board.GetLegalMoves();
        OrderMoves(ref moves, board);

        var bestScore = int.MinValue + 1;
        var bestMove = moves[0];
        foreach (var move in moves)
        {
            board.MakeMove(move);
            var eval = -Search(board, depth - 1, -beta, -alpha);
            board.UndoMove(move);
            if (bestScore < eval)
            {
                bestScore = eval;
                bestMove = move;
            }

            alpha = Math.Max(alpha, bestScore);
            if (alpha >= beta) break;
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
        var priority = 0;
        var tp = _transpositionTable[board.ZobristKey & TranspositionTableMask];
        if (tp.Move == move && tp.ZobristHash == board.ZobristKey) priority += 1000;
        if (move.IsCapture) priority = _mvvValues[(int)move.CapturePieceType] + _lvaValues[(int)move.MovePieceType];
        return priority;
    }

    private void OrderMoves(ref Move[] moves, Board board)
    {
        var orderedMoves = moves.Select(m => new Tuple<Move, int>(m, GetMovePriority(m, board))).ToList();
        orderedMoves.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        for (var i = 0; i < moves.Length; i++) moves[i] = orderedMoves[i].Item1;
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

        foreach (var m in moves)
        {
            board.MakeMove(m);
            var evaluation = -Quiesce(board, -beta, -alpha);
            board.UndoMove(m);

            alpha = Math.Max(evaluation, alpha);
            if (alpha >= beta) break;
        }

        return alpha;
    }

    // private static bool ShouldExecuteNextDepth(Timer timer, int maxThinkTime)
    // {
    //     var currentThinkTime = timer.MillisecondsElapsedThisTurn;
    //     return ((maxThinkTime - currentThinkTime) > currentThinkTime * 3);
    // }

    private int GetPestoValue(int index) =>
        (int)(((CompressedPestoValues[index / 10] >> (6 * (index % 10))) & 63) - 20) * 8;

    public int Evaluate(Board board)
    {
        int mg = 0, eg = 0, phase = 0;

        for (sbyte b = 0; b <= 1; b++)
        {
            // evaluate white first
            var isWhite = b == 0;
            for (var piece = PieceType.Pawn; piece <= PieceType.King; piece++)
            {
                var mask = board.GetPieceBitboard(piece, isWhite);
                while (mask != 0)
                {
                    phase += _piecePhase[(int)piece];
                    var index = 128 * ((int)piece - 1) + BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^
                                (isWhite ? 56 : 0);
                    // subtract 1 when getting pesto value because pesto values are 1-indexed
                    mg += GetPestoValue(index) + _pieceValues[(int)piece];
                    eg += GetPestoValue(index + 64) + _pieceValues[(int)piece + 6];
                }
            }

            mg = -mg;
            eg = -eg;
        }

        return ((mg * phase + eg * (24 - phase)) / 24) * (board.IsWhiteToMove ? 1 : -1);
    }

    // private int Evaluate(Board board) => _chessEvaluator.Evaluate(board) * (board.IsWhiteToMove ? 1 : -1);

    public Move Think(Board board, Timer timer)
    {
        var bestMove = _transpositionTable[board.ZobristKey & TranspositionTableMask];

        for (sbyte depth = 1;
             depth <= MaxDepth;
             depth++)
        {
            Search(board, MaxDepth, int.MinValue + 1, int.MaxValue - 1);
            bestMove = _transpositionTable[board.ZobristKey & TranspositionTableMask];

            var currentThinkTime = timer.MillisecondsElapsedThisTurn;
            var shouldExecuteNextDepth = ((10 * 1000) - currentThinkTime) > (currentThinkTime * 3);

            if (!shouldExecuteNextDepth) break;
        }

        return bestMove.Move;
    }
}