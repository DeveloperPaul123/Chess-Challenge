using System;
using System.Collections;
using System.Linq;
using ChessChallenge.API;
using Board = ChessChallenge.API.Board;
using Move = ChessChallenge.API.Move;
using Timer = ChessChallenge.API.Timer;

public class MyBot : IChessBot
{
    // PeSTO evaluation
    // https://www.chessprogramming.org/PeSTO%27s_Evaluation_Function
    private readonly int[] _piecePhase = { 0, 1, 1, 2, 4, 0 };

    // Pawn, Knight, Bishop, Rook, Queen, King 
    private readonly short[] PieceValues =
    {
        82, 337, 365, 477, 1025, 9468, // Middlegame
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

    private readonly Move[,] _killerMoves = new Move[MaxKillerMoves, MaxDepth];
    private const int MaxKillerMoves = 2;

    // side, move from, move to
    private readonly int[,,] _moveHistory = new int[MaxDepth, 64, 64];

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

    public MyBot()
    {
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

    // TODO: Remove to save tokens
    private const int MvvLvaFactor = 1000;
    private const int TranspositionTableSortValue = 1000000;
    private const int KillerValue = 1000;

    private int GetMovePriority(Move move, Board board, int ply)
    {
        return
            _transpositionTable[board.ZobristKey % TranspositionTableEntries].Move == move ? TranspositionTableSortValue :
            move.IsCapture ? MvvLvaFactor * (int)move.CapturePieceType - (int)move.MovePieceType :
            _killerMoves[0, ply] == move || _killerMoves[1, ply] == move ? KillerValue :
            _moveHistory[ply, move.StartSquare.Index, move.TargetSquare.Index];
    }

    /// Negamax search with alpha-beta pruning and transposition table
    private int Search(Board board, Timer timer, int depth, int ply, int alpha, int beta)
    {
        var quiesceSearch = depth <= 0;
        var notRoot = ply > 0;
        var bestScore = int.MinValue + 1;
        if (notRoot && board.IsRepeatedPosition()) return 0;

        var transposition = _transpositionTable[board.ZobristKey % TranspositionTableEntries];
        if (notRoot && transposition.ZobristHash == board.ZobristKey && transposition.Depth >= depth && (
                transposition.Flag == Exact
                || transposition.Flag == LowerBound && transposition.Evaluation >= beta
                || transposition.Evaluation == UpperBound && transposition.Evaluation <= alpha
            )) return transposition.Evaluation;

        var evaluation = Evaluate(board);

        if (quiesceSearch)
        {
            bestScore = evaluation;
            if (bestScore >= beta) return bestScore;
            alpha = Math.Max(alpha, bestScore);
        }

        var moves = board.GetLegalMoves(quiesceSearch).OrderByDescending(
            move => GetMovePriority(move, board, ply)).ToArray();

        var bestMove = Move.NullMove;
        var startingAlpha = alpha;

        foreach (var move in moves)
        {
            if (CheckThinkTime && timer.MillisecondsElapsedThisTurn >= timer.MillisecondsRemaining / TimeCheckFactor) return 30000;

            board.MakeMove(move);
            var eval = -Search(board, timer, depth - 1, ply + 1, -beta, -alpha);
            board.UndoMove(move);
            if (eval > bestScore)
            {
                if (!move.IsCapture && !quiesceSearch)
                    // add it to history
                    _moveHistory[ply, move.StartSquare.Index,
                        move.TargetSquare.Index] += depth * depth;

                bestScore = eval;
                bestMove = move;
                // update move at root
                if (ply == 0) _bestMoveRoot = move;

                // update alpha and check for beta cutoff
                alpha = Math.Max(alpha, eval);
                if (alpha >= beta)
                {
                    if (!quiesceSearch && !bestMove.IsCapture)
                    {
                        // assign killer move in the case of a beta cutoff
                        // make sure we have no duplicates
                        if (bestMove != _killerMoves[0, ply])
                        {
                            // shift moves down
                            _killerMoves[1, ply] = _killerMoves[0, ply];
                            _killerMoves[0, ply] = bestMove;
                        }
                    }

                    break;

                }

            }
        }

        // check for terminal position
        if (!quiesceSearch && moves.Length == 0) return board.IsInCheck() ? -100000 + ply : 0;

        // after finding the best move, store it in the transposition table
        // note we use the original alpha
        var bound = bestScore >= beta ? LowerBound : bestScore > startingAlpha ? Exact : UpperBound;

        _transpositionTable[board.ZobristKey % TranspositionTableEntries] = new Transposition(board.ZobristKey,
            bestScore, (sbyte)depth, bound, bestMove);

        return bestScore;
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

        // clear killer moves
        Array.Clear(_killerMoves, 0, _killerMoves.Length);
        // clear history
        Array.Clear(_moveHistory, 0, _moveHistory.Length);

        // https://www.chessprogramming.org/Iterative_Deepening
        for (sbyte depth = 1;
             depth <= MaxDepth;
             depth++)
        {
            int score = Search(board, timer, depth, 0, int.MinValue + 1, int.MaxValue - 1);

            // check if we're out of time
            if (CheckThinkTime && timer.MillisecondsElapsedThisTurn >= timer.MillisecondsRemaining / TimeCheckFactor) break;
        }

        return _bestMoveRoot.IsNull ? board.GetLegalMoves().First() : _bestMoveRoot;
    }
}