using ChessChallenge.API;

namespace Chess.Engine.Evaluation;

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

    public int Evaluate(Board board)
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

        // TODO: Take into account king safety
        // TODO: Take into account piece board control

        return score;
    }
}
