using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Gameplay.Entities.Player;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Engine.Graphics.LightingV9Lab;

/// <summary>
/// Finite, disposable test data: no session, saves, world generation or lighting renderer.
/// Every draw supplies white vertex color; textures retain their original content-pipeline alpha.
/// </summary>
public sealed class V9LabScene
{
    public const int Width = 480;
    public const int Height = 272;
    public const int TileSize = 8;
    private const int Columns = Width / TileSize;
    private const int Rows = Height / TileSize;
    private const int RoofTop = 6;
    private const int OpeningLeft = 10;
    private const int OpeningRight = 17;

    private readonly Texture2D playerLower;
    private readonly Texture2D playerUpper;
    private readonly Texture2D torchPole;
    private readonly Texture2D torchFlame;
    private readonly Texture2D distantMountains;

    public WorldMap Map { get; } = new(Columns, Rows, TileSize);
    public PlayerAnimator Animator { get; } = new();
    public Vector2 PlayerFeet { get; set; } = new(214, 222);
    public bool OpeningOpen { get; private set; } = true;
    public bool BlockerPresent { get; private set; } = true;
    public Rectangle OpeningBounds { get; } = new(OpeningLeft * TileSize, RoofTop * TileSize,
        (OpeningRight - OpeningLeft + 1) * TileSize, TileSize);
    public Rectangle BlockerBounds { get; } = new(37 * TileSize, 18 * TileSize, 2 * TileSize, 10 * TileSize);

    public V9LabScene(ContentManager content)
    {
        Map.SetTextures(content.Load<Texture2D>("tiles/dirt_spritesheet"),
            content.Load<Texture2D>("tiles/grass_spritesheet"),
            content.Load<Texture2D>("tiles/sand_spritesheet"),
            content.Load<Texture2D>("tiles/stone_spritesheet"),
            content.Load<Texture2D>("tiles/wood_spritesheet"),
            content.Load<Texture2D>("tiles/iron-ore_spritesheet"));
        Map.NeutralLightingAlbedo = true;
        playerLower = content.Load<Texture2D>("entities/player/playerDown_sheet");
        playerUpper = content.Load<Texture2D>("entities/player/playerUp_sheet");
        torchPole = content.Load<Texture2D>("furniture/torch-Sheet");
        torchFlame = content.Load<Texture2D>("furniture/torch-animation-Sheet-Sheet");
        distantMountains = content.Load<Texture2D>("ui/background_parallax/background1");

        byte[] foreground = new byte[Columns * Rows];
        byte[] background = new byte[Columns * Rows];
        for (int y = 0; y < Rows; y++)
        for (int x = 0; x < Columns; x++)
        {
            int ceiling = CeilingBottom(x);
            int floor = FloorTop(x);
            bool apertureColumn = x >= OpeningLeft && x <= OpeningRight;
            bool roof = y >= RoofTop && y <= ceiling && !apertureColumn;
            int leftWall = y < 15 ? 4 : y < 22 ? 2 : 3;
            int rightWall = y < 15 ? 55 : y < 23 ? 57 : 55;
            bool wall = y >= RoofTop && (x <= leftWall || x >= rightWall);
            bool overhang = x >= 3 && x <= 15 && y >= 17 && y <= (x < 9 ? 19 : 18);
            bool solid = roof || wall || overhang || y >= floor;
            if (solid)
            {
                bool grassy = (y == RoofTop && roof) || (y == floor && x > leftWall && x < rightWall) ||
                    (y == 17 && overhang && x >= 6);
                bool stone = (x > 30 && y > 9) || (x < 8 && y > 21) || y >= floor + 3;
                foreground[y * Columns + x] = (byte)(grassy ? TileType.Grass : stone ? TileType.Stone : TileType.Dirt);
            }

            // Background is a receiving wall, never a blocker. The shaft also has a wall
            // behind it: only the explicitly marked band ABOVE the roof is exterior scenery.
            if (y >= RoofTop)
                background[y * Columns + x] = (byte)TileType.Stone;
        }
        Map.ImportTileSnapshot(foreground);
        Map.ImportBackgroundTileSnapshot(background);
        WriteBlocker(true);
    }

    /// <summary>Exterior is an explicit world-space band, independent of sources and closure.</summary>
    public bool IsExteriorPixel(float x, float y) => x >= 0 && x < Width && y >= 0 && y < RoofTop * TileSize;

    /// <summary>Unlike WorldMap's horizontal wrapping, this laboratory has finite solid boundaries.</summary>
    public bool Solid(int tileX, int tileY) => tileX < 0 || tileY < 0 || tileX >= Columns || tileY >= Rows ||
        Map.GetTile(tileX, tileY) != TileType.Empty;

    public void SetOpening(bool open)
    {
        if (OpeningOpen == open) return;
        OpeningOpen = open;
        for (int x = OpeningLeft; x <= OpeningRight; x++)
            Map.SetTile(x, RoofTop, open ? TileType.Empty : TileType.Dirt);
    }

    public void SetBlocker(bool present)
    {
        if (BlockerPresent == present) return;
        BlockerPresent = present;
        WriteBlocker(present);
    }

    public void DrawBackground(SpriteBatch spriteBatch)
    {
        // Passing White is deliberate: WorldMap's optional default uses its legacy dark tint.
        Map.DrawBackground(spriteBatch, 0, Columns - 1, 0, Rows - 1, Color.White);
    }

    /// <summary>Unlit distant scenery is clipped to the explicit exterior band, above the roof.</summary>
    public void DrawExterior(SpriteBatch spriteBatch)
    {
        spriteBatch.Draw(distantMountains, new Rectangle(0, 0, Width, OpeningBounds.Top),
            new Rectangle(0, 0, distantMountains.Width, 110), Color.White);
    }

    public void DrawTerrain(SpriteBatch spriteBatch)
    {
        // Raw source lookup only; no cached render target, wetness/night overlays or shader state.
        for (int y = 0; y < Rows; y++)
        for (int x = 0; x < Columns; x++)
            if (Map.TryGetTileSprite(x, y, out Texture2D texture, out Rectangle? source))
                spriteBatch.Draw(texture, new Rectangle(x * TileSize, y * TileSize, TileSize, TileSize), source, Color.White);
    }

    public void DrawPlayer(SpriteBatch spriteBatch)
    {
        Vector2 root = Animator.GetDrawPosition(PlayerFeet);
        Animator.DrawLowerBody(spriteBatch, playerLower, root, Color.White);
        Animator.DrawUpperBody(spriteBatch, playerUpper, root, Color.White);
    }

    /// <summary>Each source position is the flame center in world pixels. The pole is ordinary albedo.</summary>
    public void DrawTorches(SpriteBatch spriteBatch, IReadOnlyList<Vector2> positions, bool flames)
    {
        Rectangle source = new(0, 0, 8, 8);
        foreach (Vector2 position in positions)
        {
            int x = (int)MathF.Round(position.X) - 4;
            int y = (int)MathF.Round(position.Y);
            if (flames)
                spriteBatch.Draw(torchFlame, new Rectangle(x, y - 4, 8, 8), source, Color.White);
            else
                spriteBatch.Draw(torchPole, new Rectangle(x, y + 3, 8, 8), source, Color.White);
        }
    }

    private void WriteBlocker(bool present)
    {
        for (int y = 18; y < 28; y++)
        for (int x = 37; x <= 38; x++)
            Map.SetTile(x, y, present ? (y == 18 ? TileType.Dirt : TileType.Stone) : TileType.Empty);
    }

    private static int CeilingBottom(int x) => x < 8 ? 10 : x < 20 ? 8 : x < 27 ? 7 :
        x < 34 ? 9 : x < 43 ? 10 : x < 51 ? 8 : 10;

    private static int FloorTop(int x) => x < 10 ? 27 : x < 19 ? 29 : x < 34 ? 28 :
        x < 42 ? 28 : x < 51 ? 27 : 26;

}
