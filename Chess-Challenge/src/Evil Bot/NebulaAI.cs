using System;
using System.Linq;
using ChessChallenge.API;

/*
 * So here we go, 8 august, let's start from scratch, let's name this project NebulaAI
 * V1: NegaMax, Q Search, Move ordering, Piece Square Tables and Transposition Tables
 * V2: Null move pruning, History heuristics
 * V3: Reversed futility pruning, futility pruning and from NegaMax to PVS
 * V4: Killer moves, check extensions, razoring, time management
 * V5: Back to more pure forms of pruning
 */
public class NebulaAI : IChessBot
{
    public int Nodes;
    public int QNodes;

    // Transposition Table: keep track of positions that were already calculated and possibly re-use information
    record struct TtEntry(ulong Key, int Score, int Depth, int Flag, Move BestMove);
    TtEntry[] _tt = new TtEntry[0x400000];

    // History Heuristics: keep track on great moves that caused a cutoff to retry them
    // Based on a lookup by color, piece type and target square
    public int[,,] HistoryHeuristics;

    // Killer moves: keep track on great moves that caused a cutoff to retry them
    // Based on a lookup by depth
    Move[] _killers = new Move[256];

    // Globals
    public Timer Timer;
    public Board Board;
    public int TimeLimit;

    // Keep track on the best move
    public Move BestMove;

    public int Pvs(int depth, int plyFromRoot, int alpha, int beta, bool canNullMove)
    {
        // Reuse search variables
        bool notRoot = plyFromRoot++ > 0,
            canPrune = false,
            isPv = beta - alpha > 1,
            inCheck = Board.IsInCheck();
        int alphaStart = alpha,
            bestEval = -100_000,
            movesSearched = 0;

        // Check for repetition since TT doesn't know that and we don't want draws when we can win
        if (notRoot && Board.IsRepeatedPosition()) return 0;

        // Try to find the board position in the tt
        ulong zobristKey = Board.ZobristKey;
        TtEntry ttEntry = _tt[zobristKey % 0x400000];

        // When we find the transposition check if we can use it to narrow our alpha beta bounds
        if (notRoot && ttEntry.Key == zobristKey && ttEntry.Depth >= depth)
        {
            // 1 = lower bound; 2 = exact; 3 = upper bound
            switch (ttEntry.Flag)
            {
                case 1:
                    alpha = Math.Max(alpha, ttEntry.Score);
                    break;                
                case 2:
                    return ttEntry.Score;
                case 3:
                    beta = Math.Min(beta, ttEntry.Score);
                    break;
            }
            
            // Beta cutoff, move is too good, opposing player has a better option (beta) and won't play this subtree
            if (beta <= alpha) return ttEntry.Score;
        }
        
        // Check extensions
        if (inCheck)
            depth++;
        
        // Search quiescence position to prevent horizon effect
        bool inQSearch = depth < 1;
        if (inQSearch)
        {
            bestEval = Evaluate();
            alpha = Math.Max(alpha, bestEval);
            if (beta <= bestEval) return bestEval;
        }
        // No pruning in QSearch and not when there is a check (unstable situation)
        else if (!isPv && !inCheck)
        {
            int staticEval = Evaluate();

            // Reverse futility pruning: if our position is much better than beta and even if we start losing material 
            // every depth from now and we'd still be above beta, cut it off.
            // Use symbolic value of 100 centi pawn per ply
            if (depth < 4 && beta <= staticEval - 100 * depth)
                return staticEval;

            // Null move pruning on pre-frontier nodes and higher
            if (depth > 1)
            {
                Board.ForceSkipTurn();
                // depth - (1 + R(eduction)), using the classic 2 for reduction
                int nullMoveEval = -Pvs(depth - 3, plyFromRoot, -beta, 1 - beta, false);
                Board.UndoSkipTurn();
                // Prune branch when the side who got a free move can't even improve
                if (beta <= nullMoveEval) return nullMoveEval;
            }

            // Futility pruning: if our position is so bad that even if we improve a lot
            // We can't improve alpha, so we'll give up on this branch
            // It's the pure form, only depth 1, classic minor piece value
            if (depth == 1)
                canPrune = staticEval + 300 <= alpha;

            // Classic razoring in pre-pre-frontier node by x margin, who knows
            if (depth == 3 && staticEval + 500 <= alpha)
                depth--;
        }

        // Move Ordering
        var moves = Board.GetLegalMoves(inQSearch).OrderByDescending(
            move =>
                move == ttEntry.BestMove ? 9_000_000 :
                move.IsCapture ? 1_000_000 * (int)move.CapturePieceType - (int)move.MovePieceType :
                move.IsPromotion ? 1_000_000 :
                _killers[plyFromRoot] == move ? 900_000 :
                HistoryHeuristics[plyFromRoot & 1, (int)move.MovePieceType, move.TargetSquare.Index]
        ).ToArray();

        Move bestMove = default;
        foreach (Move move in moves)
        {
            // On certain nodes (tactical nodes), static eval, even with a wide margin, isn't safe enough to exclude
            bool tactical = !move.IsCapture && !move.IsPromotion;

            // Only futility prune on non tactical nodes and when we've fully searched 1 line to prevent pruning everything
            if (canPrune && !tactical && movesSearched > 0) continue;

            if (depth > 0) Nodes++;
            else QNodes++;

            Board.MakeMove(move);

            // Principle Variation Search: search our first moves fully with normal bounds
            // After we're using a small window search (performant) to know if it has potential
            // Also later moves (movesSearched > 5) will have less depth (late move reduction)
            bool isFullSearch = inQSearch || movesSearched++ == 0;
            int lmr = !isFullSearch && !tactical && !inCheck && movesSearched > 5 ? 1 : 0;
            int score = -Pvs(depth - 1 - lmr, plyFromRoot, isFullSearch || lmr > 0 ? -beta : -alpha - 1, -alpha, canNullMove);
            
            // If the branch has potential, if it can improve alpha, we'll need to fully search it for exact score
            if (!isFullSearch && score > alpha && (isPv || lmr > 0))
                score = -Pvs(depth - 1, plyFromRoot, -beta, -alpha, canNullMove);
            Board.UndoMove(move);
            
            if (score > bestEval)
            {
                if (!notRoot) BestMove = move;
                bestMove = move;
                bestEval = score;
                alpha = Math.Max(alpha, bestEval);
                
                // Beta cutoff, move is too good, opposing player has a better option (beta) and won't play this subtree
                if (beta <= alpha)
                {
                    if (!move.IsCapture)
                    {
                        HistoryHeuristics[plyFromRoot & 1, (int)move.MovePieceType, move.TargetSquare.Index] += depth * depth;
                        _killers[plyFromRoot] = move;
                    }
                    break;
                }
            }

            // Out of time break out of the loop
            if (Timer.MillisecondsElapsedThisTurn > TimeLimit) return 100_000;
        }

        // Performant way to check for stalemate and checkmate
        if (!inQSearch && moves.Length == 0) return inCheck ? plyFromRoot - 100_000 : 0;

        _tt[zobristKey % 0x400000] = new TtEntry(zobristKey,
            bestEval,
            depth,
            bestEval <= alphaStart ? 3 : bestEval >= beta ? 1 : 2,
            bestMove);

        return bestEval;
    }

    public Move Think(Board board, Timer timer)
    {
        Nodes = 0;
        QNodes = 0;

        Timer = timer;
        Board = board;

        // Base is 1500 ms, or less
        TimeLimit = Math.Min(timer.MillisecondsRemaining / 30, 1500);

        // Initialise HH every time we're trying to find a move with Iterative Deepening
        HistoryHeuristics = new int[2, 7, 64];

        // Reset to prevent lingering previous moves
        BestMove = default;

        // Iterative deepening
        for (int depth = 1; depth < 50; depth++)
        {
            Pvs(depth, 0, -100000, 100000, true);

            if (Timer.MillisecondsElapsedThisTurn * 2 > TimeLimit) break;
        }

        return BestMove;
    }

    // 
    // PST Packer and Un-packer - credits to Tyrant
    readonly int[] _phaseWeight = { 0, 1, 1, 2, 4, 0 };

    // Pawn, Knight, Bishop, Rook, Queen, King 
    private readonly short[] _pieceValues =
    {
        82, 337, 365, 477, 1025, 20000,
        94, 281, 297, 512, 936, 20000
    };

    private readonly decimal[] _packedPst =
    {
        63746705523041458768562654720m, 71818693703096985528394040064m, 75532537544690978830456252672m,
        75536154932036771593352371712m, 76774085526445040292133284352m, 3110608541636285947269332480m,
        936945638387574698250991104m, 75531285965747665584902616832m, 77047302762000299964198997571m,
        3730792265775293618620982364m, 3121489077029470166123295018m, 3747712412930601838683035969m,
        3763381335243474116535455791m, 8067176012614548496052660822m, 4977175895537975520060507415m,
        2475894077091727551177487608m, 2458978764687427073924784380m, 3718684080556872886692423941m,
        4959037324412353051075877138m, 3135972447545098299460234261m, 4371494653131335197311645996m,
        9624249097030609585804826662m, 9301461106541282841985626641m, 2793818196182115168911564530m,
        77683174186957799541255830262m, 4660418590176711545920359433m, 4971145620211324499469864196m,
        5608211711321183125202150414m, 5617883191736004891949734160m, 7150801075091790966455611144m,
        5619082524459738931006868492m, 649197923531967450704711664m, 75809334407291469990832437230m,
        78322691297526401047122740223m, 4348529951871323093202439165m, 4990460191572192980035045640m,
        5597312470813537077508379404m, 4980755617409140165251173636m, 1890741055734852330174483975m,
        76772801025035254361275759599m, 75502243563200070682362835182m, 78896921543467230670583692029m,
        2489164206166677455700101373m, 4338830174078735659125311481m, 4960199192571758553533648130m,
        3420013420025511569771334658m, 1557077491473974933188251927m, 77376040767919248347203368440m,
        73949978050619586491881614568m, 77043619187199676893167803647m, 1212557245150259869494540530m,
        3081561358716686153294085872m, 3392217589357453836837847030m, 1219782446916489227407330320m,
        78580145051212187267589731866m, 75798434925965430405537592305m, 68369566912511282590874449920m,
        72396532057599326246617936384m, 75186737388538008131054524416m, 77027917484951889231108827392m,
        73655004947793353634062267392m, 76417372019396591550492896512m, 74568981255592060493492515584m,
        70529879645288096380279255040m,
    };

    private readonly int[][] _pst;

    private int Evaluate()
    {
        int mg = 0, eg = 0, phase = 0, sideToMove = 2;
        for (; --sideToMove >= 0;)
        {
            for (int piece = -1; ++piece < 6;)
            for (ulong mask = Board.GetPieceBitboard((PieceType)piece + 1, sideToMove > 0); mask != 0;)
            {
                // A number between 0 to 63 that indicates which square the piece is on, flip for black
                int squareIndex = BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^ 56 * sideToMove;

                // Piece values are baked into the pst (see constructor of the bot)
                mg += _pst[squareIndex][piece];
                eg += _pst[squareIndex][piece + 6];

                // The less pieces, the more we bend towards our endgame strategy
                phase += _phaseWeight[piece];
            }

            // Flip score for optimised token count (always white perspective due to double flip)
            // Eg. White eval = 2300 -> flip -> -2300 -> black eval = 2000 -> -300 -> flip -> 300 
            mg = -mg;
            eg = -eg;
        }

        // Tapered evaluation since our goals towards endgame shifts
        return (mg * phase + eg * (24 - phase)) / 24 * (Board.IsWhiteToMove ? 1 : -1);
    }

    // Constructor and wizardry to unpack the bitmap piece square tables and bake the piece values into the values
    public NebulaAI()
    {
        _pst = _packedPst.Select(packedTable =>
        {
            int pieceType = 0;
            return decimal.GetBits(packedTable).Take(3)
                .SelectMany(bit => BitConverter.GetBytes(bit)
                    .Select(square => (int)((sbyte)square * 1.461) + _pieceValues[pieceType++]))
                .ToArray();
        }).ToArray();
    }
}