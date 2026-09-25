using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Graphics.LightingV8;
using Nyvorn.Source.Engine.Graphics.LightingV9Lab;
using Nyvorn.Source.Engine.Graphics.LightingV9Probe;

if (System.Array.Exists(args, a => a == "--lighting-v9-lab"))
{
    using var lab = new V9LabGame(args);
    lab.Run();
    return;
}

V8GameplayOptions.Configure(args);
V9ProbeOptions.Configure(args);
// V9 is the gameplay's default lighting; V7/V8 only when selected explicitly (--lighting-v7, --lighting-v8, ...).
V9Gameplay.Configure(args);

using Game game = System.Array.Exists(args, a => a == "--v8-probe" || a == "--v8-scene")
    ? new V8DiagnosticGame(args)
    : new Nyvorn.Game1();
game.Run();
