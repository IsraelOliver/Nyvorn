using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Persistence;
using System.Collections.Generic;

namespace Nyvorn.Source.Game.States
{
    public sealed class WorldSelectState : IGameState
    {
        public bool UpdateBelow => false;
        public bool DrawBelow => false;
        public bool BlockInputBelow => true;

        private readonly GraphicsDevice graphicsDevice;
        private readonly ContentManager content;
        private readonly StateMachine stateMachine;
        private readonly PlanetSaveService saveService = new();
        private readonly SpriteFont font;
        private readonly Texture2D pixel;

        private MouseState previousMouse;
        private KeyboardState previousKeyboard;
        private IReadOnlyList<PlanetSaveSummary> worlds = new List<PlanetSaveSummary>();

        public WorldSelectState(GraphicsDevice graphicsDevice, ContentManager content, StateMachine stateMachine)
        {
            this.graphicsDevice = graphicsDevice;
            this.content = content;
            this.stateMachine = stateMachine;
            font = content.Load<SpriteFont>("ui/UIFont");
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        public void OnEnter()
        {
            previousMouse = Mouse.GetState();
            previousKeyboard = Keyboard.GetState();
            RefreshWorlds();
        }

        public void OnExit()
        {
        }

        public void Update(GameTime gameTime)
        {
            MouseState mouse = Mouse.GetState();
            KeyboardState keyboard = Keyboard.GetState();
            bool leftClickPressed = mouse.LeftButton == ButtonState.Pressed &&
                                    previousMouse.LeftButton == ButtonState.Released;
            bool refreshPressed = keyboard.IsKeyDown(Keys.F5) && !previousKeyboard.IsKeyDown(Keys.F5);

            if (refreshPressed)
                RefreshWorlds();

            if ((leftClickPressed && GetNewWorldButtonBounds().Contains(mouse.Position)) ||
                (keyboard.IsKeyDown(Keys.N) && !previousKeyboard.IsKeyDown(Keys.N)))
            {
                stateMachine.ReplaceState(new WorldCreationState(graphicsDevice, content, stateMachine));
            }

            if (leftClickPressed)
            {
                foreach ((PlanetSaveSummary summary, Rectangle bounds) in GetWorldEntryBounds())
                {
                    Rectangle deleteButton = GetDeleteButtonBounds(bounds);
                    if (deleteButton.Contains(mouse.Position))
                    {
                        saveService.Delete(summary.FilePath);
                        RefreshWorlds();
                        break;
                    }

                    if (!bounds.Contains(mouse.Position))
                        continue;

                    PlanetSaveData saveData = saveService.Load(summary.FilePath);
                    PlayingSessionFactory factory = new PlayingSessionFactory(graphicsDevice, content);
                    stateMachine.ReplaceState(new LoadingWorldState(
                        graphicsDevice,
                        content,
                        stateMachine,
                        factory.CreateBuildOperation(saveData),
                        "Carregando Planeta"));
                    break;
                }
            }

            previousMouse = mouse;
            previousKeyboard = keyboard;
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            Rectangle panel = GetPanelBounds();
            Rectangle listBounds = GetWorldListBounds();
            Rectangle newWorldButton = GetNewWorldButtonBounds();

            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            spriteBatch.Draw(pixel, new Rectangle(0, 0, graphicsDevice.PresentationParameters.BackBufferWidth, graphicsDevice.PresentationParameters.BackBufferHeight), new Color(10, 22, 26, 180));
            spriteBatch.Draw(pixel, panel, new Color(23, 42, 49, 220));

            spriteBatch.DrawString(font, "Mundos", new Vector2(panel.X + 28, panel.Y + 24), new Color(255, 241, 193));
            spriteBatch.DrawString(font, "Continue um planeta salvo ou crie um novo.", new Vector2(panel.X + 28, panel.Y + 50), new Color(168, 230, 207));

            spriteBatch.Draw(pixel, listBounds, new Color(18, 34, 40, 210));

            if (worlds.Count == 0)
            {
                spriteBatch.DrawString(font, "Nenhum mundo salvo ainda.", new Vector2(listBounds.X + 18, listBounds.Y + 18), Color.White);
                spriteBatch.DrawString(font, "Crie um novo para gerar o primeiro .plt.", new Vector2(listBounds.X + 18, listBounds.Y + 42), new Color(143, 211, 255));
            }
            else
            {
                foreach ((PlanetSaveSummary summary, Rectangle bounds) in GetWorldEntryBounds())
                    DrawWorldEntry(spriteBatch, summary, bounds);
            }

            bool buttonHovered = newWorldButton.Contains(Mouse.GetState().Position);
            Color buttonFill = buttonHovered ? new Color(255, 241, 193) : new Color(168, 230, 207);
            spriteBatch.Draw(pixel, new Rectangle(newWorldButton.X - 2, newWorldButton.Y - 2, newWorldButton.Width + 4, newWorldButton.Height + 4), new Color(143, 211, 255, 180));
            spriteBatch.Draw(pixel, newWorldButton, buttonFill);
            Vector2 buttonSize = font.MeasureString("Novo Mundo");
            Vector2 buttonPos = new Vector2(newWorldButton.X + (newWorldButton.Width - buttonSize.X) * 0.5f, newWorldButton.Y + (newWorldButton.Height - buttonSize.Y) * 0.5f);
            spriteBatch.DrawString(font, "Novo Mundo", buttonPos, new Color(16, 31, 36));

            spriteBatch.DrawString(font, "F5 atualiza a lista", new Vector2(panel.X + 28, panel.Bottom - 32), new Color(143, 211, 255));
            spriteBatch.End();
        }

        private void DrawWorldEntry(SpriteBatch spriteBatch, PlanetSaveSummary summary, Rectangle bounds)
        {
            bool hovered = bounds.Contains(Mouse.GetState().Position);
            Color fill = hovered ? new Color(39, 70, 79) : new Color(28, 50, 58);
            Color accent = hovered ? new Color(255, 241, 193) : new Color(143, 211, 255);
            Rectangle deleteButton = GetDeleteButtonBounds(bounds);
            bool deleteHovered = deleteButton.Contains(Mouse.GetState().Position);

            spriteBatch.Draw(pixel, new Rectangle(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4), accent * 0.7f);
            spriteBatch.Draw(pixel, bounds, fill);

            string title = summary.Metadata.PlanetName;
            string subtitle = $"{GetPresetLabel(summary.Metadata.SizePreset)} | Seed {summary.Metadata.Seed}";
            string saveInfo = $"Salvo em {summary.SavedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}";

            spriteBatch.DrawString(font, title, new Vector2(bounds.X + 14, bounds.Y + 10), Color.White);
            spriteBatch.DrawString(font, subtitle, new Vector2(bounds.X + 14, bounds.Y + 34), new Color(168, 230, 207));
            spriteBatch.DrawString(font, saveInfo, new Vector2(bounds.X + 14, bounds.Y + 58), new Color(255, 241, 193));

            spriteBatch.Draw(pixel, new Rectangle(deleteButton.X - 2, deleteButton.Y - 2, deleteButton.Width + 4, deleteButton.Height + 4), new Color(255, 180, 180, 150));
            spriteBatch.Draw(pixel, deleteButton, deleteHovered ? new Color(255, 210, 210) : new Color(230, 140, 140));
            Vector2 deleteSize = font.MeasureString("Excluir");
            Vector2 deletePos = new Vector2(deleteButton.X + (deleteButton.Width - deleteSize.X) * 0.5f, deleteButton.Y + (deleteButton.Height - deleteSize.Y) * 0.5f);
            spriteBatch.DrawString(font, "Excluir", deletePos, new Color(68, 24, 24));
        }

        private void RefreshWorlds()
        {
            worlds = saveService.ListWorlds();
        }

        private IEnumerable<(PlanetSaveSummary Summary, Rectangle Bounds)> GetWorldEntryBounds()
        {
            Rectangle listBounds = GetWorldListBounds();
            int entryHeight = 88;
            int gap = 10;

            for (int i = 0; i < worlds.Count && i < 5; i++)
            {
                Rectangle bounds = new Rectangle(
                    listBounds.X + 16,
                    listBounds.Y + 16 + (i * (entryHeight + gap)),
                    listBounds.Width - 32,
                    entryHeight);

                yield return (worlds[i], bounds);
            }
        }

        private string GetPresetLabel(WorldSizePreset preset)
        {
            return preset switch
            {
                WorldSizePreset.Small => "Pequeno",
                WorldSizePreset.Medium => "Medio",
                _ => "Grande"
            };
        }

        private Rectangle GetDeleteButtonBounds(Rectangle entryBounds)
        {
            return new Rectangle(entryBounds.Right - 118, entryBounds.Y + 24, 100, 36);
        }

        private Rectangle GetPanelBounds()
        {
            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
            return new Rectangle((screenW - 720) / 2, (screenH - 620) / 2, 720, 620);
        }

        private Rectangle GetWorldListBounds()
        {
            Rectangle panel = GetPanelBounds();
            return new Rectangle(panel.X + 28, panel.Y + 92, panel.Width - 56, 410);
        }

        private Rectangle GetNewWorldButtonBounds()
        {
            Rectangle panel = GetPanelBounds();
            return new Rectangle(panel.X + 28, panel.Bottom - 92, panel.Width - 56, 48);
        }
    }
}
