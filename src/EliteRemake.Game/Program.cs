using EliteRemake.Game;

var options = GameOptions.Parse(args);
using var game = new EliteGame(options);
game.Run();
