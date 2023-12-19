using System;
using System.Linq;
using ChessChallenge.API;
using Board = ChessChallenge.API.Board;
using Move = ChessChallenge.API.Move;
using Timer = ChessChallenge.API.Timer;

public class MyBotv5 : IChessBot
{
    // PeSTO evaluation
    // https://www.chessprogramming.org/PeSTO%27s_Evaluation_Function
    private readonly int[] _piecePhase = { 0, 1, 1, 2, 4, 0 };

    // None, Pawn, Knight, Bishop, Rook, Queen, King 
    private readonly short[] PieceValues =
    {
        82, 337, 365, 477, 1025, 10000, // Middlegame
        94, 281, 297, 512, 936, 10000 // Endgame
    };

    // Big table packed with data from premade piece square tables
    // Unpack using PackedEvaluationTables[set, rank] = file
    private readonly decimal[] PackedPestoTables =
    {
        63746705523041458768562654720m, 71818693703096985528394040064m, 75532537544690978830456252672m,
        75536154932036771593352371712m, 76774085526445040292133284352m, 3110608541636285947269332480m,
        936945638387574698250991104m, 75531285965747665584902616832m,
        77047302762000299964198997571m, 3730792265775293618620982364m, 3121489077029470166123295018m,
        3747712412930601838683035969m, 3763381335243474116535455791m, 8067176012614548496052660822m,
        4977175895537975520060507415m, 2475894077091727551177487608m,
        2458978764687427073924784380m, 3718684080556872886692423941m, 4959037324412353051075877138m,
        3135972447545098299460234261m, 4371494653131335197311645996m, 9624249097030609585804826662m,
        9301461106541282841985626641m, 2793818196182115168911564530m,
        77683174186957799541255830262m, 4660418590176711545920359433m, 4971145620211324499469864196m,
        5608211711321183125202150414m, 5617883191736004891949734160m, 7150801075091790966455611144m,
        5619082524459738931006868492m, 649197923531967450704711664m,
        75809334407291469990832437230m, 78322691297526401047122740223m, 4348529951871323093202439165m,
        4990460191572192980035045640m, 5597312470813537077508379404m, 4980755617409140165251173636m,
        1890741055734852330174483975m, 76772801025035254361275759599m,
        75502243563200070682362835182m, 78896921543467230670583692029m, 2489164206166677455700101373m,
        4338830174078735659125311481m, 4960199192571758553533648130m, 3420013420025511569771334658m,
        1557077491473974933188251927m, 77376040767919248347203368440m,
        73949978050619586491881614568m, 77043619187199676893167803647m, 1212557245150259869494540530m,
        3081561358716686153294085872m, 3392217589357453836837847030m, 1219782446916489227407330320m,
        78580145051212187267589731866m, 75798434925965430405537592305m,
        68369566912511282590874449920m, 72396532057599326246617936384m, 75186737388538008131054524416m,
        77027917484951889231108827392m, 73655004947793353634062267392m, 76417372019396591550492896512m,
        74568981255592060493492515584m, 70529879645288096380279255040m,
    };

    // unpacked pesto table
    private readonly int[][] _unpackedPestoTables;

    // match types for transposition table
    private const sbyte Exact = 0, LowerBound = -1, UpperBound = 1, Invalid = -2;

#if DEBUG
    private const int MaxDepth = 3;
    private const bool CheckThinkTime = false;
#else
    private const int MaxDepth = 50;
    private const bool CheckThinkTime = true;
#endif

    private Move _bestMoveRoot = Move.NullMove;

    private readonly Move[,] _killerMoves;
    private const int MaxKillerMoves = 2;

    //14 bytes per entry, likely will align to 16 bytes due to padding (if it aligns to 32, recalculate max TP table size)
    private record struct Transposition(
        ulong ZobristHash,
        int Evaluation,
        sbyte Depth,
        sbyte Flag,
        Move Move);

    private const ulong TranspositionTableEntries = (1 << 20);
    private readonly Transposition[] _transpositionTable = new Transposition[TranspositionTableEntries];
    private const int TimeCheckFactor = 30;

    // maximum think time for each move 
    // private const int MaxThinkTime = 10 * 1000;

    public MyBotv5()
    {
        _killerMoves = new Move[MaxKillerMoves, MaxDepth];
        _unpackedPestoTables = new int[64][];
        _unpackedPestoTables = PackedPestoTables.Select(packedTable =>
        {
            int pieceType = 0;
            return decimal.GetBits(packedTable).Take(3)
                .SelectMany(c => BitConverter.GetBytes(c)
                    .Select((byte square) => (int)((sbyte)square * 1.461) + PieceValues[pieceType++]))
                .ToArray();
        }).ToArray();
    }

    /// <summary>
    /// Negamax search with alpha-beta pruning and transposition table
    /// </summary>
    /// <param name="board"></param>
    /// <param name="timer"></param>
    /// <param name="depth"></param>
    /// <param name="ply">Current ply (number of moves)</param>
    /// <param name="alpha"></param>
    /// <param name="beta"></param>
    /// <returns></returns>
    private int Search(Board board, Timer timer, int depth, int ply, int alpha, int beta)
    {
        var quiesceSearch = depth <= 0;
        var notRoot = ply > 0;
        var bestScore = int.MinValue + 10000;
        if (notRoot && board.IsRepeatedPosition()) return 0;

        var transposition = _transpositionTable[board.ZobristKey % TranspositionTableEntries];
        if (notRoot && transposition.ZobristHash == board.ZobristKey && transposition.Depth >= depth && (
                transposition.Flag == Exact
                || transposition.Flag == LowerBound && transposition.Evaluation >= beta
                || transposition.Evaluation == UpperBound && transposition.Evaluation <= alpha)) return transposition.Evaluation;

        var evaluation = Evaluate(board);

        if (quiesceSearch)
        {
            bestScore = evaluation;
            if (bestScore >= beta) return bestScore;
            alpha = Math.Max(alpha, bestScore);
        }

        var moves = board.GetLegalMoves(quiesceSearch);

        // check for terminal position
        if (!quiesceSearch && moves.Length == 0) return board.IsInCheck() ? -30000 + ply : 0;

        OrderMoves(ref moves, board, ply);

        var bestMove = Move.NullMove;
        var startingAlpha = alpha;

        foreach (var move in moves)
        {
            if (CheckThinkTime && timer.MillisecondsElapsedThisTurn >= timer.MillisecondsRemaining / TimeCheckFactor) return 30000;

            board.MakeMove(move);
            var eval = -Search(board, timer, depth - 1, ply + 1, -beta, -alpha);
            board.UndoMove(move);
            if (eval <= bestScore) continue;

            // update our best score, move
            bestScore = eval;
            bestMove = move;
            // update move at root
            if (ply == 0) _bestMoveRoot = move;

            // update alpha and check for beta cutoff
            alpha = Math.Max(alpha, eval);
            if (alpha >= beta) break;
        }

        if (!quiesceSearch)
        {
            // after finding the best move, store it in the transposition table
            // note we use the original alpha
            var bound = bestScore >= beta ? LowerBound : bestScore > startingAlpha ? Exact : UpperBound;
            // assign killer move in the case of a beta cutoff
            if (bestScore >= beta && !bestMove.IsCapture)
            {
                if (bestMove != _killerMoves[0, ply])
                    // shift moves down
                    (_killerMoves[0, ply], _killerMoves[1, ply]) = (bestMove, _killerMoves[0, ply]);

            }
            _transpositionTable[board.ZobristKey % TranspositionTableEntries] = new Transposition(board.ZobristKey,
                bestScore, (sbyte)depth, bound, bestMove);

        }

        return bestScore;
    }

    private const int MvvLvaOffset = 5000;
    private const int MvvLvaFactor = 1000;
    private const int TranspositionTableSortValue = 1000000;
    private const int KillerValue = 100;

    private int GetMovePriority(Move move, Board board, int ply)
    {
        var priority = MvvLvaOffset;
        var tp = _transpositionTable[board.ZobristKey % TranspositionTableEntries];
        if (tp.Move == move) priority += TranspositionTableSortValue;
        // MVV - LVA move ordering
        // - https://www.chessprogramming.org/MVV-LVA
        // - https://rustic-chess.org/search/ordering/mvv_lva.html
        // The more valuable the captured piece is, and the less valuable the attacker is,
        // the stronger the capture will be, and thus it will be ordered higher in the move list
        // max score could be 1000 * 6 - 1 = 5999
        else if (move.IsCapture) priority += MvvLvaFactor * (int)move.CapturePieceType - (int)move.MovePieceType;
        else
        {
            for (var i = 0; i < MaxKillerMoves; i++)
            {
                if (_killerMoves[i, ply] != move) continue;

                priority += i * KillerValue;
                break;
            }
        }

        return priority;
    }

    private void OrderMoves(ref Move[] moves, Board board, int ply)
    {
        var moveScores = new int[moves.Length];
        for (var i = 0; i < moves.Length; ++i) moveScores[i] = GetMovePriority(moves[i], board, ply);
        Array.Sort(moveScores, moves);
        Array.Reverse(moves);
    }

    public int Evaluate(Board board)
    {
        int mg = 0, eg = 0, phase = 0;

        for (sbyte b = 0; b <= 1; b++)
        {
            // evaluate white first
            var isWhite = b == 0;
            for (var piece = PieceType.Pawn; piece <= PieceType.King; piece++)
            {
                var bitboard = board.GetPieceBitboard(piece, isWhite);
                while (bitboard != 0)
                {
                    var sq = BitboardHelper.ClearAndGetIndexOfLSB(ref bitboard) ^ (isWhite ? 56 : 0);
                    mg += _unpackedPestoTables[sq][(int)piece - 1];
                    // endgame value is in the same array, but offset by 6
                    // instead of doing piece -1 + 6, we can just do piece + 5
                    eg += _unpackedPestoTables[sq][(int)piece + 5];
                    phase += _piecePhase[(int)piece - 1];
                }
            }

            mg = -mg;
            eg = -eg;
        }

        return (mg * phase + eg * (24 - phase)) / 24 * (board.IsWhiteToMove ? 1 : -1);
    }

    public Move Think(Board board, Timer timer)
    {
        _bestMoveRoot = Move.NullMove;
        // https://www.chessprogramming.org/Iterative_Deepening
        for (sbyte depth = 1;
             depth <= MaxDepth;
             depth++)
        {
            Search(board, timer, depth, 0, int.MinValue + 1, int.MaxValue - 1);

            // check if we're out of time
            if (CheckThinkTime && timer.MillisecondsElapsedThisTurn >= timer.MillisecondsRemaining / TimeCheckFactor) break;
        }

        return _bestMoveRoot.IsNull ? board.GetLegalMoves().First() : _bestMoveRoot;
    }
}