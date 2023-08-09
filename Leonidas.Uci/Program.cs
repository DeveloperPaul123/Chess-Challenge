using ChessChallenge.Application;

namespace Leonidas.Uci
{
    internal class Program
    {
        static (int totalCount, int debugCount) GetTokenCount()
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "src", "My Bot", "MyBot.cs");
            var txt = File.ReadAllText(path);
            return TokenCounter.CountTokens(txt);
        }

        static void Main(string[] args)
        {
            if (args.Length <= 1 || args[0] != "uci") return;

            Console.WriteLine("Starting up in UCI mode...");
            var success = Enum.TryParse(args[1], out ChallengeController.PlayerType player);
            if (!success)
            {
                Console.Error.WriteLine($"Failed to start bot with player typer {args[1]}");
            }

            Console.WriteLine("Sebastian Lague's Chess Challenge UCI interface by Gediminas Masaitis");
            var (totalTokens, debugTokens) = GetTokenCount();
            Console.WriteLine($"Current token count: {totalTokens}");
            Console.WriteLine($"Debug token count: {debugTokens}");
            Console.WriteLine();

            var uci = new Uci(player);
            uci.Run();
        }
    }
}