using System;
using System.Linq;
using ChessChallenge.API;

public class MyBotv621 : IChessBot
{
    // PeSTO evaluation
    // https://www.chessprogramming.org/PeSTO%27s_Evaluation_Function
    private readonly int[] _piecePhase = { 0, 1, 1, 2, 4, 0 };

    // Pawn, Knight, Bishop, Rook, Queen, King 
    private readonly short[] _pieceValues =
    {
        82, 337, 365, 477, 1025, 10000, // Middlegame
        94, 281, 297, 512, 936, 10000 // Endgame
    };

    // Big table packed with data from premade piece square tables
    // Unpack using PackedEvaluationTables[set, rank] = file
    private readonly decimal[] _packedPestoTables =
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

    private Move _bestMoveRoot = Move.NullMove;
    private readonly Move[,] _killerMoves = new Move[2, 50];

    // side, move from, move to
    private readonly int[,,] _moveHistory = new int[2, 64, 64];

    //14 bytes per entry, likely will align to 16 bytes due to padding (if it aligns to 32, recalculate max TP table size)
    private record struct Transposition(
        ulong ZobristHash,
        int Evaluation,
        sbyte Depth,
        sbyte Flag,
        Move Move);

    private readonly Transposition[] _transpositionTable = new Transposition[1_048_576UL];

    // cache these to save tokens
    private Board _board;
    private Timer _timer;
    private int _maxThinkTime;

    public MyBotv621()
    {
        _unpackedPestoTables = new int[64][];
        _unpackedPestoTables = _packedPestoTables.Select(packedTable =>
        {
            var pieceType = 0;
            return decimal.GetBits(packedTable).Take(3)
                .SelectMany(c => BitConverter.GetBytes(c)
                    .Select(square => (int)((sbyte)square * 1.461) + _pieceValues[pieceType++]))
                .ToArray();
        }).ToArray();
    }

    private int Search(int depth, int ply, int alpha, int beta, bool allowNullMove = true)
    {
        // declare these all at once to save tokens
        bool quiesceSearch = depth <= 0,
            notRoot = ply > 0,
            isInCheck = _board.IsInCheck(),
            isPrincipleVariation = beta - alpha > 1,
            canPrune = false;

        var bestScore = -9999999;

        if (notRoot && _board.IsRepeatedPosition()) return 0;

        var (zobristHash, score, ttDepth, flag, _) = _transpositionTable[_board.ZobristKey % 1_048_576UL];
        if (zobristHash == _board.ZobristKey && notRoot &&
            ttDepth >= depth)
        {
            if (flag == LowerBound)
                alpha = Math.Max(alpha, score);
            else if (flag == UpperBound)
                beta = Math.Min(beta, score);

            if (alpha >= beta || flag == Exact)
                return score;
        }

        if (quiesceSearch)
        {
            bestScore = Evaluate();
            alpha = Math.Max(alpha, bestScore);
            if (alpha >= beta) return bestScore;
        }
        // no pruning in q-search
        // null move pruning only when allowed and we're not in check
        else if (!isInCheck && !isPrincipleVariation)
        {
            // reverse futility pruning
            var staticEval = Evaluate();
            if (staticEval - _pieceValues[0] * depth >= beta) return staticEval;

            if (allowNullMove)
            {
                _board.TrySkipTurn();
                // depth reduction factor used for null move pruning, commented out for tokens
                // private const int DepthReductionFactor = 3;
                var nullMoveScore = -Search(depth - 1 - 3, ply + 1, -beta, -beta + 1,
                    false);
                _board.UndoSkipTurn();

                // beta cutoff
                if (nullMoveScore >= beta)
                    return beta;
            }

            // check for futility pruning conditions, use depth * pawn value
            canPrune = depth <= 4 && staticEval + _pieceValues[0] * depth <= alpha;
            
        }

        // TODO: Can we make this buffer smaller?
        Span<Move> moves = stackalloc Move[128];
        // use non-alloc version for the speeeeeeddddddd
        _board.GetLegalMovesNonAlloc(ref moves, quiesceSearch);
        // create buffer based on span size, note that GetLegalMovesNonAlloc changes the length of the span
        // based on the number of moves available.
        var moveScores = new int[moves.Length];
        // assign scores
        for (var i = 0; i < moves.Length; i++)
        {
            var move = moves[i];
            // negate all scores so we don't have to reverse the move list later
            moveScores[i] = _transpositionTable[_board.ZobristKey % 1_048_576UL].Move == move ? -9_000_000 :
                move.IsCapture ? -1 * (1_000_000 * (int)move.CapturePieceType - (int)move.MovePieceType) :
                move.IsPromotion ? -10_000 :
                _killerMoves[0, ply] == move || _killerMoves[1, ply] == move ? -900_000 :
                -1 * _moveHistory[ply & 1, move.StartSquare.Index, move.TargetSquare.Index];
        }

        // sort moves using the negative scores as keys
        moveScores.AsSpan().Sort(moves);

        var bestMove = Move.NullMove;
        int startingAlpha = alpha,
            movesSearched = 0;

        // search function alias for the next iteration, trying to save some tokens
        int NextSearch(int newAlpha, int newBeta, int depthReduction = 1) =>
            -Search(depth - depthReduction, ply + 1, newAlpha, newBeta);

        foreach (var move in moves)
        {
            // futility pruning
            var isTacticalMove = move.IsCapture || move.IsPromotion || isInCheck;
            if (!isTacticalMove && !isPrincipleVariation && canPrune && movesSearched > 0) continue;

            _board.MakeMove(move);

            int eval;
            // first child searches with normal window, otherwise do a null window search
            if (movesSearched++ == 0 || quiesceSearch)
                eval = NextSearch(-beta, -alpha);
            // LMR conditions
            else if (movesSearched >= (isPrincipleVariation ? 7 : 3) && depth >= 3 &&
                     !isTacticalMove)
            {
                // null window search
                eval = NextSearch(-(alpha + 1), -alpha, 2);
            }
            else eval = alpha + 1;

            // check result to see if we need to do a full re-search
            // if we fail high, we re-search
            if (eval > alpha &&
                alpha < (eval = NextSearch(-(alpha + 1), -alpha)) && eval < beta)
                eval = NextSearch(-beta, -eval);

            _board.UndoMove(move);

            if (eval > bestScore)
            {
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
                        // add it to history
                        _moveHistory[ply & 1, move.StartSquare.Index,
                            move.TargetSquare.Index] += depth * depth;

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

            // check if time expired
            if (_timer.MillisecondsElapsedThisTurn >= _maxThinkTime)
                return 100_000;
        }

        // check for terminal position            
        if (!quiesceSearch && moves.Length == 0) return _board.IsInCheck() ? -100_000 + ply : 0;

        // after finding the best move, store it in the transposition table
        // note we use the original alpha
        _transpositionTable[_board.ZobristKey % 1_048_576UL] = new
        (
            _board.ZobristKey,
            bestScore,
            (sbyte)depth,
            bestScore >= beta ? LowerBound : bestScore > startingAlpha ? Exact : UpperBound,
            bestMove
        );

        return bestScore;
    }

    private int Evaluate()
    {
        int middleGame = 0, endGame = 0, phase = 0, sideToMove = 2, piece, square;

        for (; --sideToMove >= 0; middleGame = -middleGame, endGame = -endGame)
        {
            // evaluate white first
            for (piece = -1; ++piece < 6;)
            {
                for (ulong mask = _board.GetPieceBitboard((PieceType)piece + 1, sideToMove > 0); mask != 0;)
                {
                    phase += _piecePhase[piece];
                    square = BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^ 56 * sideToMove;
                    middleGame += _unpackedPestoTables[square][piece];
                    endGame += _unpackedPestoTables[square][piece + 6];
                }
            }
        }

        return (middleGame * phase + endGame * (24 - phase)) / 24 * (_board.IsWhiteToMove ? 1 : -1);
    }

    public Move Think(Board board, Timer timer)
    {
        _board = board;
        _timer = timer;
        _maxThinkTime = timer.MillisecondsRemaining / 25;

        _bestMoveRoot = Move.NullMove;

        // clear killer moves
        Array.Clear(_killerMoves, 0, _killerMoves.Length);
        // clear history
        Array.Clear(_moveHistory, 0, _moveHistory.Length);

        // https://www.chessprogramming.org/Iterative_Deepening
        for (sbyte depth = 1;
             depth <= 50;
             depth++)
        {
            Search(depth, 0, -9999999, 9999999);

            // check if we're out of time
            if (timer.MillisecondsElapsedThisTurn >= _maxThinkTime) break;
        }

        return _bestMoveRoot.IsNull ? board.GetLegalMoves().First() : _bestMoveRoot;
    }
}