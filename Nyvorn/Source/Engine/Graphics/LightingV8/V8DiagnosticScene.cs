using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.World;
using Nyvorn.Source.Gameplay.Crafting;
using Nyvorn.Source.Gameplay.World.Objects;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.Gameplay.World.Simulation;

namespace Nyvorn.Source.Engine.Graphics.LightingV8;

/// <summary>In-memory fixture only: no session, simulation, save service, weather or old lighting.</summary>
public sealed class V8DiagnosticScene
{
    public WorldMap Map { get; } = new(96, 64, 8);
    public List<PlatformInstance> Platforms { get; } = new();
    public List<DoorInstance> Doors { get; } = new();
    public List<V8Light> Lights { get; } = new();
    public Vector2 Origin { get; } = new(-64, 128);
    public Vector2 EntityPosition { get; set; }
    public V8Light First { get; }
    public V8Light Second { get; }
    public V8Light RightRoom { get; }
    public DoorInstance ClosedDoor { get; }
    private readonly Texture2D platform, pole, flame, enemy, door;
    private readonly Rectangle enemyFrame = new(0, 0, 32, 32);
    public Texture2D EntityTexture => enemy;
    public Rectangle EntityFrame => enemyFrame;

    public V8AmbientContext ConfigureAmbient(string name)
    {
        // Reset only this in-memory fixture. The direct-only suite and gameplay are untouched.
        for (int y = 0; y < 32; y++) for (int x = 0; x < 48; x++)
        {
            Map.SetTile(x - 8, y + 16, y == 0 || y >= 30 || x == 26 ||
                (x >= 3 && x < 8 && y >= 22) || (x >= 34 && x < 41 && y >= 20) ? TileType.Dirt : TileType.Empty);
            if (Map.GetBackgroundTile(x - 8, y + 16) != TileType.Stone) Map.SetBackgroundTile(x - 8, y + 16, TileType.Stone);
        }
        Lights.Clear(); Doors.Clear(); Doors.Add(ClosedDoor);
        var context = new V8AmbientContext {
            Layers = new[] { new WorldLayerDefinition(WorldLayerType.Space, 0, 7),
                new(WorldLayerType.Surface, 8, 15), new(WorldLayerType.ShallowUnderground, 16, 39),
                new(WorldLayerType.Cavern, 40, 51), new(WorldLayerType.DeepCavern, 52, 63) },
            SkyState = default(SkyState) with { AmbientLight = new Color(130, 175, 225) },
            SkyEnabled = name.StartsWith("sky-") || name == "combined",
            LocalEnabled = !name.StartsWith("sky-")
        };
        if (context.LocalEnabled && !name.EndsWith("off")) Lights.Add(First);
        bool seal = context.SkyEnabled || name.StartsWith("door-");
        if (seal) for (int y = 0; y < 32; y++) { Map.SetTile(-8, y + 16, TileType.Dirt); Map.SetTile(39, y + 16, TileType.Dirt); }
        if (name is "sky-window" or "sky-moved" or "sky-interior-void" or "combined")
        {
            for (int y = 5; y < 9; y++) for (int x = 8; x < 11; x++) Map.SetBackgroundTile(x - 8, y + 16, TileType.Empty);
            // Second opening straddles the Shallow/Cavern border, exposing interpolation errors.
            for (int y = 21; y < 27; y++) Map.SetBackgroundTile(12 - 8, y + 16, TileType.Empty);
        }
        if (name == "sky-fissure")
            for (int y = 10; y < 17; y++) Map.SetBackgroundTile(14 - 8, y + 16, TileType.Empty);
        if (name == "sky-interior-void") context.ExteriorOverride = (_, _) => false;
        if (name == "sky-deep-void")
            for (int y = 24; y < 29; y++) Map.SetBackgroundTile(12 - 8, y + 16, TileType.Empty);
        if (name.StartsWith("local-cavern")) context.Layers = new[] {
            new WorldLayerDefinition(WorldLayerType.Surface, 0, 7), new(WorldLayerType.ShallowUnderground, 8, 15),
            new(WorldLayerType.Cavern, 16, 47), new(WorldLayerType.DeepCavern, 48, 63) };
        if (name.StartsWith("local-deep")) context.Layers = new[] {
            new WorldLayerDefinition(WorldLayerType.Surface, 0, 3), new(WorldLayerType.ShallowUnderground, 4, 7),
            new(WorldLayerType.Cavern, 8, 15), new(WorldLayerType.DeepCavern, 16, 63) };
        if (name.StartsWith("local-cavern") || name.StartsWith("local-deep")) context.SkyEnabled = true;
        if (name == "corner")
        {
            // Air pocket with four solid cardinal neighbours: diagonals must not illuminate it.
            Map.SetTile(11, 32, TileType.Dirt); Map.SetTile(13, 32, TileType.Dirt);
            Map.SetTile(12, 31, TileType.Dirt); Map.SetTile(12, 33, TileType.Dirt);
        }
        if (name.StartsWith("door-"))
        {
            for (int y = 12; y < 15; y++) Map.SetTile(18, y + 16, TileType.Empty);
            Doors.Clear(); Doors.Add(new DoorInstance(new Point(18, 28), 8, isOpen: name == "door-open"));
        }
        return context;
    }

    public V8DiagnosticScene(ContentManager content)
    {
        Map.SetTextures(content.Load<Texture2D>("tiles/dirt_spritesheet"), content.Load<Texture2D>("tiles/grass_spritesheet"),
            content.Load<Texture2D>("tiles/sand_spritesheet"), content.Load<Texture2D>("tiles/stone_spritesheet"),
            content.Load<Texture2D>("tiles/wood_spritesheet"), content.Load<Texture2D>("tiles/iron-ore_spritesheet"));
        platform = content.Load<Texture2D>("tiles/wood_platform");
        pole = content.Load<Texture2D>("furniture/torch-Sheet");
        flame = content.Load<Texture2D>("furniture/torch-animation-Sheet-Sheet");
        enemy = content.Load<Texture2D>("entities/enemy/enemy_test");
        door = content.Load<Texture2D>("objects/wood_door");
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 48; x++)
            {
                Map.SetBackgroundTile(x - 8, y + 16, TileType.Stone);
                if (y == 0 || y >= 30 || x == 26 || (x >= 3 && x < 8 && y >= 22) || (x >= 34 && x < 41 && y >= 20))
                    Map.SetTile(x - 8, y + 16, TileType.Dirt);
            }
        for (int x = 14; x < 20; x++) Platforms.Add(new(new Point(x - 8, 12 + 16), 8));
        ClosedDoor = new(new Point(38 - 8, 13 + 16), 8);
        Doors.Add(ClosedDoor);
        First = new(Origin + new Vector2(80.25f, 56.75f), new Vector3(1f, .72f, .38f), 280);
        Second = new(Origin + new Vector2(176.75f, 176.25f), new Vector3(.38f, .70f, 1f), 220);
        RightRoom = new(Origin + new Vector2(256.75f, 96.25f), new Vector3(.38f, .70f, 1f), 220);
        Lights.Add(First);
        EntityPosition = Origin + new Vector2(120, 116);
    }

    public void DrawBackground(SpriteBatch batch, Rectangle view)
    {
        DrawTiles(batch, view, true);
    }
    public void DrawExterior(SpriteBatch batch, Rectangle view, V8AmbientContext context, Texture2D pixel)
    {
        if (context == null || !context.SkyEnabled) return;
        for (int y = System.Math.Max(0, (int)System.MathF.Floor(view.Top / 8f)); y < System.Math.Min(Map.Height, (int)System.MathF.Ceiling(view.Bottom / 8f)); y++)
            for (int x = (int)System.MathF.Floor(view.Left / 8f); x < (int)System.MathF.Ceiling(view.Right / 8f); x++)
                if (context.IsExteriorAperture(Map, x, y))
                    // Diagnostic exterior backdrop only. It is not a receiver, a light floor or a sun.
                    batch.Draw(pixel, new Rectangle(x * 8, y * 8, 8, 8), context.SkyState.AmbientLight);
    }
    public void DrawForeground(SpriteBatch batch, Rectangle view)
    {
        DrawTiles(batch, view, false);
        foreach (var p in Platforms)
            DrawWrapped(batch, platform, p.Bounds, new Rectangle(0, 0, 8, 8), view);
        foreach (var d in Doors)
            if (!d.IsOpen) DrawWrapped(batch, door, d.DrawBounds, new Rectangle(0, 0, 8, 24), view);
    }
    public void DrawEntities(SpriteBatch batch, Rectangle view)
    {
        foreach (var d in Doors)
            if (d.IsOpen) DrawWrapped(batch, door, d.DrawBounds, new Rectangle(8, 0, 16, 24), view);
        DrawWrapped(batch, enemy, new Rectangle((int)EntityPosition.X, (int)EntityPosition.Y, 32, 32), enemyFrame, view);
        DrawTorch(batch, First, view);
        if (Lights.Contains(Second)) DrawTorch(batch, Second, view);
        if (Lights.Contains(RightRoom)) DrawTorch(batch, RightRoom, view);
    }
    private void DrawTorch(SpriteBatch batch, V8Light light, Rectangle view)
    {
        var rect = new Rectangle((int)light.Position.X - 4, (int)light.Position.Y - 1, 8, 8);
        DrawWrapped(batch, pole, rect, new Rectangle(0, 0, 8, 8), view);
        rect.Y -= 3;
        DrawWrapped(batch, flame, rect, new Rectangle(0, 0, 8, 8), view);
    }
    private void DrawTiles(SpriteBatch batch, Rectangle view, bool background)
    {
        int size = Map.TileSize;
        for (int y = System.Math.Max(0, (int)System.MathF.Floor(view.Top / (float)size)); y < System.Math.Min(Map.Height, (int)System.MathF.Ceiling(view.Bottom / (float)size)); y++)
            for (int x = (int)System.MathF.Floor(view.Left / (float)size); x < (int)System.MathF.Ceiling(view.Right / (float)size); x++)
            {
                int wrapped = Map.WrapTileX(x);
                var tile = background ? Map.GetBackgroundTile(wrapped, y) : Map.GetTile(wrapped, y);
                // Reuse the real autotile rectangles, but deliberately ignore the old background tint.
                if (Map.TryGetTileParticleRenderData(tile, wrapped, y, background, out var texture, out var source, out _))
                    batch.Draw(texture, new Rectangle(x * size, y * size, size, size), source, Color.White);
            }
    }
    private void DrawWrapped(SpriteBatch batch, Texture2D texture, Rectangle bounds, Rectangle source, Rectangle view)
    {
        int first = (int)System.MathF.Floor((view.Left - bounds.Right) / (float)Map.PixelWidth) + 1;
        int last = (int)System.MathF.Ceiling((view.Right - bounds.Left) / (float)Map.PixelWidth) - 1;
        for (int k = first; k <= last; k++)
            batch.Draw(texture, new Rectangle(bounds.X + k * Map.PixelWidth, bounds.Y, bounds.Width, bounds.Height), source, Color.White);
    }
}


