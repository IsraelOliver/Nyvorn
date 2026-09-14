using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Graphics.LightingV8;

V8GameplayOptions.Configure(args);

using Game game = System.Array.Exists(args, a => a == "--v8-probe" || a == "--v8-scene")
    ? new V8DiagnosticGame(args)
    : new Nyvorn.Game1();
game.Run();
