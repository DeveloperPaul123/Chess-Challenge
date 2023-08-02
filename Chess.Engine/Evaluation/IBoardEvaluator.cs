using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ChessChallenge.API;

namespace Chess.Engine.Evaluation;

internal interface IBoardEvaluator
{
    public int Evaluate(Board board);
}