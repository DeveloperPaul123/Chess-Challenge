//#define X

using ChessChallenge.API;
using System;
using System.Linq;

/*
 * So here we go, 8 august, let's start from scratch, let's name this project NebulaAI
 * V1: NegaMax, Q Search, Move ordering, Piece Square Tables and Transposition Tables
 * V2: Null move pruning, History heuristics
 * V3: Reversed futility pruning, futility pruning and from NegaMax to PVS
 * V4: Killer moves, check extensions, razoring, time management
 * V5: Late move reduction, token savings
 * V6: Span<Move> for moves collection, allowNull to prevent double pruning, more effective alpha beta update and beta cutoff, token optimalisation
 */

public class NebulaAI2 : IChessBot
{
#if X
    public int Nodes;
    public int QNodes;
#endif

    // Transposition Table: keep track of positions that were already calculated and possibly re-use information
    // Token optimised: Key, Score, Depth, Flag, Move
    private readonly (ulong, int, int, int, Move)[] _tt = new (ulong, int, int, int, Move)[0x400000];

    // Score each legal move for move ordering
    public readonly int[] MoveScores = new int[218];

    // The next move (at root depth) to play based on our search
    public Move RootMove;

    // Time limit defined here so we re-use it for unpacking pst
    private int _timeLimit;

    // Piece values for tapered eval
    private readonly short[] _pieceValues =
    {
        82, 337, 365, 477, 1025, 0,
        94, 281, 297, 512, 936, 0
    };

    // The unpacked piece square lookup table with the piece values baked in
    private readonly int[][] _pst;

    // Think gets called in order to decide the move (our) bot wants to play
    public Move Think(Board board, Timer timer)
    {
#if X
        Nodes = 0;
        QNodes = 0;
#endif

        // History Heuristics: keep track on great moves that caused a cutoff to retry them
        // Based on a lookup by color, piece type and target square
        var hh = new int[2, 7, 64];

        // Killer moves: keep track on great moves that caused a cutoff to retry them
        // Based on a lookup by depth
        var killers = new Move[2, 128];

        // 1/30th of our remaining time, split among all of the moves
        _timeLimit = timer.MillisecondsRemaining / 30;

        // Iterative Deepening
        for (int depth = 1, alpha = -1_000_000, beta = 1_000_000; ;)
        {
            int eval = Pvs(depth, 0, alpha, beta, true);

#if X
            DebugHelper.LogDepth(GetType().ToString(), timer, depth, eval, Nodes, QNodes, RootMove);
#endif

            // Out of time
            if (timer.MillisecondsElapsedThisTurn > _timeLimit)
                return RootMove;

            // Gradual widening
            // Fell outside window, retry with wider window search
            if (eval <= alpha)
                alpha -= 62;
            else if (eval >= beta)
                beta += 62;
            else
            {
                // Set up window for next search
                alpha = eval - 17;
                beta = eval + 17;
                depth++;
            }
        }

        // 
        // Principle Variation Search: This method doubles as our PVS and QSearch in order to save tokens
        int Pvs(int depth, int plyFromRoot, int alpha, int beta, bool allowNull)
        {
            // Declare search variables
            bool notRoot = plyFromRoot++ > 0,
                canFutilityPrune = false,
                inCheck = board.IsInCheck();

            // Check for repetition since TT doesn't know that and we don't want draws when we can win
            if (notRoot && board.IsRepeatedPosition()) return 0;

            // Try to find the board position in the tt
            ulong zobristKey = board.ZobristKey;
            ref var ttEntry = ref _tt[zobristKey & 0x3FFFFF];

            // Declare search variables
            int pvsEval,
                startAlpha = alpha,
                bestEval = -100_000,
                movesSearched = 0,
                movesScored = 0,
                entryScore = ttEntry.Item2,
                entryFlag = ttEntry.Item4;

            // Using local method to simplify multiple similar calls to the Pvs (to combine with Late move reduction)
            int Search(int newAlpha, int reduction = 1, bool canNull = true) =>
                pvsEval = -Pvs(depth - reduction, plyFromRoot, -newAlpha, -alpha, canNull);

            // Transposition table lookup, we have calculated this position before
            // Avoid retrieving mate scores from the tt since they aren't accurate to the ply
            if (ttEntry.Item1 == zobristKey && notRoot && ttEntry.Item3 >= depth && Math.Abs(entryScore) < 50000 && (
                    entryFlag == 1 ||
                    entryFlag == 2 && entryScore <= alpha ||
                    entryFlag == 3 && entryScore >= beta))
                return entryScore;

            // Internal iterative reduction
            //if (depth > 3)
            //    depth--;

            // Check extensions
            if (inCheck)
                depth++;

            // Search quiescence position to prevent horizon effect
            bool inQSearch = depth <= 0;
            if (inQSearch)
            {
                // Determine if quiescence search should be continued
                bestEval = Evaluate();
                if (bestEval >= beta) return bestEval;
                alpha = Math.Max(alpha, bestEval);
            }
            // No pruning in QSearch and not when there is a check (unstable situation)
            else if (beta - alpha == 1 && !inCheck)
            {
                // Reverse futility pruning
                int staticEval = Evaluate();

                // Reverse futility pruning: if our position is much better than beta, even if we start losing material every depth
                // we'd still be above beta, so cutoff since unlikely opponent will allow us this path
                if (depth <= 10 && staticEval - 96 * depth >= beta)
                    return staticEval;

                // Null move pruning on pre-frontier nodes and up
                if (depth >= 2 && allowNull)
                {
                    board.ForceSkipTurn();
                    // depth - (1 + Reduction), using the classic 2 for reduction
                    Search(beta, 3 + (depth >> 2), false);
                    board.UndoSkipTurn();
                    // Prune branch when the side who got a free move can't even improve
                    if (beta <= pvsEval) return pvsEval;
                }

                // Futility pruning: if our position is so bad that even if we improve a lot
                // and we can't improve alpha, so we'll give up on this branch
                canFutilityPrune = depth <= 8 && staticEval + depth * 141 <= alpha;
            }

            // Generate appropriate moves only capture moves in q search unless in check
            Span<Move> moveSpan = stackalloc Move[128];
            board.GetLegalMovesNonAlloc(ref moveSpan, inQSearch && !inCheck);

            // Order moves in reverse order -> negative values are ordered higher hence the flipped values
            foreach (Move move in moveSpan)
                MoveScores[movesScored++] = -(
                    move == ttEntry.Item5 ? 9_000_000 :
                    move.IsCapture ? 1_000_000 * (int)move.CapturePieceType - (int)move.MovePieceType :
                    killers[0, plyFromRoot] == move || killers[1, plyFromRoot] == move ? 900_000 :
                    hh[plyFromRoot & 1, (int)move.MovePieceType, move.TargetSquare.Index]);

            MoveScores.AsSpan(0, moveSpan.Length).Sort(moveSpan);

            // Performant way to check for stalemate and checkmate
            if (!inQSearch && moveSpan.IsEmpty) return inCheck ? plyFromRoot - 100_000 : 0;

            // Start trying the moves based on their score
            Move bestMove = default;
            foreach (Move move in moveSpan)
            {
#if X
                if (depth > 0) Nodes++;
                else QNodes++;
#endif

                // Futility pruning on non tactical nodes
                if (canFutilityPrune && !(movesSearched == 0 || move.IsCapture || move.IsPromotion))
                    continue;

                board.MakeMove(move);

                // PVS + LMR

                // Full search in Q search or on first move
                if (movesSearched++ == 0 || inQSearch)
                    Search(beta);
                else
                {
                    // Late move reduction search 
                    if (movesSearched >= 6 && depth > 1)
                        Search(alpha + 1, 3);
                    else
                        pvsEval = alpha + 1;

                    // Zero window search when reduced search improved alpha (or alpha hacked see above)
                    if (pvsEval > alpha && Search(alpha + 1) > alpha)
                        // Full search when lmr search and zw search are likely to improve alpha 
                        Search(beta);
                }

                board.UndoMove(move);

                // When the eval is better than our best so far, we'll update our best with the current eval
                if (pvsEval > bestEval)
                {
                    bestEval = pvsEval;

                    // If this moves even improve the alpha (best branch so far), we'll improve alpha and the best move 
                    if (pvsEval > alpha)
                    {
                        alpha = pvsEval;
                        bestMove = move;
                        if (!notRoot) RootMove = move;
                    }

                    // Beta cutoff, move is too good, opposing player has a better option (beta) and won't play this subtree
                    if (beta <= alpha)
                    {
                        if (!move.IsCapture)
                        {
                            hh[plyFromRoot & 1, (int)move.MovePieceType, move.TargetSquare.Index] += depth * depth;
                            killers[1, plyFromRoot] = killers[0, plyFromRoot];
                            killers[0, plyFromRoot] = bestMove;
                        }

                        break;
                    }
                }

                // Out of time break out of the loop
                if (timer.MillisecondsElapsedThisTurn > _timeLimit) return 100_000;
            }

            // Save position to transposition table, Key, Score, Depth, Flag, Move
            ttEntry = new(
                zobristKey,
                bestEval,
                depth,
                bestEval >= beta ? 3 : bestEval <= startAlpha ? 2 : 1,
                bestMove == default ? ttEntry.Item5 : bestMove);

            return bestEval;
        }

        int Evaluate()
        {
            // Variables re-used during evaluate
            int mg = 0, eg = 0, gamePhase = 0, sideToMove = 2, piece, square;

            // Loop the two sides that have to move (white and black)
            // Flip score for optimised token count (always white perspective due to double flip)
            // Eg. White eval = 2300 -> flip -> -2300 -> black eval = 2000 -> -300 -> flip -> 300 k)
            for (; --sideToMove >= 0; mg = -mg, eg = -eg)
                for (piece = -1; ++piece < 6;)
                    for (ulong mask = board.GetPieceBitboard((PieceType)piece + 1, sideToMove > 0); mask != 0;)
                    {
                        // The less pieces, the more we bend towards our endgame strategy
                        // Values for the pieces are 0 for pawn, 1 for bishop and knight, 2 for rook and 4 for queen
                        gamePhase += 0x00042110 >> piece * 4 & 0x0F;

                        // A number between 0 to 63 that indicates which square the piece is on, flip for black
                        square = BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^ 56 * sideToMove;

                        // Piece values are baked into the pst
                        mg += _pst[square][piece];
                        eg += _pst[square][piece + 6];
                    }

            // Tempo bonus to help with aspiration windows
            return (mg * gamePhase + eg * (24 - gamePhase)) / 24 * (board.IsWhiteToMove ? 1 : -1) +
                   gamePhase / 2;
        }
    }

    // Constructor to unpack the packed PST 
    public NebulaAI2()
    {
        _pst = new[]
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
        }.Select(packedTable =>
            new System.Numerics.BigInteger(packedTable).ToByteArray().Take(12)
                // Using _timeLimit since it will be reset anyways before used, all this to save tokens
                .Select(square => (int)((sbyte)square * 1.461) + _pieceValues[_timeLimit++ % 12])
                .ToArray()
        ).ToArray();
    }
}