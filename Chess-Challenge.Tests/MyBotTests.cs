using ChessChallenge.API;
using ChessChallenge.Chess;
using Board = ChessChallenge.API.Board;
using Timer = ChessChallenge.API.Timer;

namespace Chess_Challenge.Tests
{
    [TestClass]
    public class MyBotTests
    {
        private readonly MyBot _myBot = new MyBot();
        private List<KeyValuePair<string, string>> testPositions;
        [TestInitialize]
        public void Initialize()
        {
            testPositions = new List<KeyValuePair<string, string>>
            {
                new("r1bqkb1r/ppp2pp1/5n1p/3P4/2P1p3/5N2/PPP2PPP/RNBQK2R w KQkq","f3d4"),
                new("r1bqk2r/pp1n1ppp/4pn2/2b5/3P4/3B1N2/PPP2PPP/R1BQ1RK1 w kq", "d4c5")
            };
        }

        [TestMethod]
        public void TestExpectedPositions()
        {
            var boardSource = new ChessChallenge.Chess.Board();
            foreach (var testPosition in testPositions)
            {
                boardSource.LoadPosition(FenUtility.PositionFromFen(testPosition.Key));
                var board = new Board(boardSource);
                // 4 seconds to think
#if DEBUG
                var timer = new Timer(20*60 * 1000);
#else
                var timer = new Timer(4 * 1000);
#endif
                var move = _myBot.Think(board, timer);
                Assert.AreEqual(move.ToString(), testPosition.Value);
            }
        }

    }
}