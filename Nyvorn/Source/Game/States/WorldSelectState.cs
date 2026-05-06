using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Gameplay.UI;
using Nyvorn.Source.World.Generation;
using Nyvorn.Source.World.Persistence;
using System;
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
        private readonly Texture2D backgroundTexture;

        private MouseState previousMouse;
        private KeyboardState previousKeyboard;
        private IReadOnlyList<PlanetSaveSummary> worlds = new List<PlanetSaveSummary>();
        private int listScrollOffset;

        public WorldSelectState(GraphicsDevice graphicsDevice, ContentManager content, StateMachine stateMachine)
        {
            this.graphicsDevice = graphicsDevice;
            this.content = content;
            this.stateMachine = stateMachine;
            font = content.Load<SpriteFont>("ui/UIFont");
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
            backgroundTexture = content.Load<Texture2D>("ui/background_menu");
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
            Rectangle listBounds = GetWorldListBounds();

            if (refreshPressed)
                RefreshWorlds();

            if (listBounds.Contains(mouse.Position))
            {
                int wheelDelta = mouse.ScrollWheelValue - previousMouse.ScrollWheelValue;
                if (wheelDelta != 0)
                    listScrollOffset = Math.Clamp(listScrollOffset - Math.Sign(wheelDelta) * 40, 0, GetMaxScrollOffset());
            }

            if ((leftClickPressed && GetNewWorldButtonBounds().Contains(mouse.Position)) ||
                (keyboard.IsKeyDown(Keys.N) && !previousKeyboard.IsKeyDown(Keys.N)))
            {
                stateMachine.ReplaceState(new WorldCreationState(graphicsDevice, content, stateMachine));
                previousMouse = mouse;
                previousKeyboard = keyboard;
                return;
            }

            if (leftClickPressed && listBounds.Contains(mouse.Position))
            {
                foreach ((PlanetSaveSummary summary, Rectangle bounds) in GetWorldEntryBounds())
                {
                    if (!bounds.Intersects(listBounds))
                        continue;

                    Rectangle editButton = GetEditButtonBounds(bounds);
                    Rectangle deleteButton = GetDeleteButtonBounds(bounds);
                    if (editButton.Contains(mouse.Position))
                    {
                        stateMachine.ReplaceState(new WorldEditState(graphicsDevice, content, stateMachine, summary));
                        previousMouse = mouse;
                        previousKeyboard = keyboard;
                        return;
                    }

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
            Rectangle screenBounds = new Rectangle(0, 0, graphicsDevice.PresentationParameters.BackBufferWidth, graphicsDevice.PresentationParameters.BackBufferHeight);
            RasterizerState scissorRasterizer = new RasterizerState { ScissorTestEnable = true };

            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            spriteBatch.Draw(backgroundTexture, screenBounds, Color.White);
            spriteBatch.Draw(pixel, screenBounds, new Color(10, 22, 26, 150));
            spriteBatch.Draw(pixel, panel, new Color(23, 42, 49, 220));

            spriteBatch.DrawString(font, "Mundos", new Vector2(panel.X + 28, panel.Y + 24), new Color(255, 241, 193));
            string wrappedSubtitle = TextLayout.WrapText(font, "Continue um planeta salvo ou crie um novo.", panel.Width - 56);
            spriteBatch.DrawString(font, wrappedSubtitle, new Vector2(panel.X + 28, panel.Y + 50), new Color(168, 230, 207));

            spriteBatch.Draw(pixel, listBounds, new Color(18, 34, 40, 210));

            if (worlds.Count == 0)
            {
                string wrappedEmptyTitle = TextLayout.WrapText(font, "Nenhum mundo salvo ainda.", listBounds.Width - 36);
                string wrappedEmptyHint = TextLayout.WrapText(font, "Crie um novo para gerar o primeiro .plt.", listBounds.Width - 36);
                spriteBatch.DrawString(font, wrappedEmptyTitle, new Vector2(listBounds.X + 18, listBounds.Y + 18), Color.White);
                spriteBatch.DrawString(font, wrappedEmptyHint, new Vector2(listBounds.X + 18, listBounds.Y + 18 + font.LineSpacing * 2), new Color(143, 211, 255));
            }
            else
            {
                if (GetMaxScrollOffset() > 0)
                    DrawScrollHint(spriteBatch, listBounds);
            }

            bool buttonHovered = newWorldButton.Contains(Mouse.GetState().Position);
            Color buttonFill = buttonHovered ? new Color(255, 241, 193) : new Color(168, 230, 207);
            spriteBatch.Draw(pixel, new Rectangle(newWorldButton.X - 2, newWorldButton.Y - 2, newWorldButton.Width + 4, newWorldButton.Height + 4), new Color(143, 211, 255, 180));
            spriteBatch.Draw(pixel, newWorldButton, buttonFill);
            Vector2 buttonSize = font.MeasureString("Novo Mundo");
            Vector2 buttonPos = new Vector2(newWorldButton.X + (newWorldButton.Width - buttonSize.X) * 0.5f, newWorldButton.Y + (newWorldButton.Height - buttonSize.Y) * 0.5f);
            spriteBatch.DrawString(font, "Novo Mundo", buttonPos, new Color(16, 31, 36));

            string wrappedFooter = TextLayout.WrapText(font, "F5 atualiza a lista", panel.Width - 56);
            spriteBatch.DrawString(font, wrappedFooter, new Vector2(panel.X + 28, panel.Bottom - 32), new Color(143, 211, 255));
            spriteBatch.End();

            if (worlds.Count > 0)
            {
                graphicsDevice.ScissorRectangle = listBounds;
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, rasterizerState: scissorRasterizer);

                foreach ((PlanetSaveSummary summary, Rectangle bounds) in GetWorldEntryBounds())
                    DrawWorldEntry(spriteBatch, summary, bounds);

                spriteBatch.End();
            }
        }

        private void DrawWorldEntry(SpriteBatch spriteBatch, PlanetSaveSummary summary, Rectangle bounds)
        {
            bool hovered = bounds.Contains(Mouse.GetState().Position);
            Color fill = hovered ? new Color(39, 70, 79) : new Color(28, 50, 58);
            Color accent = hovered ? new Color(255, 241, 193) : new Color(143, 211, 255);
            Rectangle editButton = GetEditButtonBounds(bounds);
            Rectangle deleteButton = GetDeleteButtonBounds(bounds);
            bool editHovered = editButton.Contains(Mouse.GetState().Position);
            bool deleteHovered = deleteButton.Contains(Mouse.GetState().Position);

            spriteBatch.Draw(pixel, new Rectangle(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4), accent * 0.7f);
            spriteBatch.Draw(pixel, bounds, fill);

            string title = summary.Metadata.PlanetName;
            string subtitle = $"{GetPresetLabel(summary.Metadata.SizePreset)} | Seed {summary.Metadata.Seed}";
            string saveInfo = $"Salvo em {summary.SavedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}";

            float textWidth = editButton.X - bounds.X - 28;
            string wrappedTitle = TextLayout.WrapText(font, title, textWidth);
            string wrappedSubtitle = TextLayout.WrapText(font, subtitle, textWidth);
            string wrappedSaveInfo = TextLayout.WrapText(font, saveInfo, textWidth);

            spriteBatch.DrawString(font, wrappedTitle, new Vector2(bounds.X + 14, bounds.Y + 10), Color.White);
            spriteBatch.DrawString(font, wrappedSubtitle, new Vector2(bounds.X + 14, bounds.Y + 10 + font.LineSpacing), new Color(168, 230, 207));
            spriteBatch.DrawString(font, wrappedSaveInfo, new Vector2(bounds.X + 14, bounds.Y + 10 + (font.LineSpacing * 2)), new Color(255, 241, 193));

            spriteBatch.Draw(pixel, new Rectangle(editButton.X - 2, editButton.Y - 2, editButton.Width + 4, editButton.Height + 4), new Color(143, 211, 255, 150));
            spriteBatch.Draw(pixel, editButton, editHovered ? new Color(190, 238, 255) : new Color(143, 211, 255));
            Vector2 editSize = font.MeasureString("Editar");
            Vector2 editPos = new Vector2(editButton.X + (editButton.Width - editSize.X) * 0.5f, editButton.Y + (editButton.Height - editSize.Y) * 0.5f);
            spriteBatch.DrawString(font, "Editar", editPos, new Color(16, 31, 36));

            spriteBatch.Draw(pixel, new Rectangle(deleteButton.X - 2, deleteButton.Y - 2, deleteButton.Width + 4, deleteButton.Height + 4), new Color(255, 180, 180, 150));
            spriteBatch.Draw(pixel, deleteButton, deleteHovered ? new Color(255, 210, 210) : new Color(230, 140, 140));
            Vector2 deleteSize = font.MeasureString("Excluir");
            Vector2 deletePos = new Vector2(deleteButton.X + (deleteButton.Width - deleteSize.X) * 0.5f, deleteButton.Y + (deleteButton.Height - deleteSize.Y) * 0.5f);
            spriteBatch.DrawString(font, "Excluir", deletePos, new Color(68, 24, 24));
        }

        private void RefreshWorlds()
        {
            worlds = saveService.ListWorlds();
            listScrollOffset = Math.Clamp(listScrollOffset, 0, GetMaxScrollOffset());
        }

        private IEnumerable<(PlanetSaveSummary Summary, Rectangle Bounds)> GetWorldEntryBounds()
        {
            Rectangle listBounds = GetWorldListBounds();
            int entryHeight = 88;
            int gap = 10;

            for (int i = 0; i < worlds.Count; i++)
            {
                Rectangle bounds = new Rectangle(
                    listBounds.X + 16,
                    listBounds.Y + 16 + (i * (entryHeight + gap)) - listScrollOffset,
                    listBounds.Width - 32,
                    entryHeight);

                yield return (worlds[i], bounds);
            }
        }

        private int GetMaxScrollOffset()
        {
            const int entryHeight = 88;
            const int gap = 10;

            Rectangle listBounds = GetWorldListBounds();
            int contentHeight = worlds.Count == 0
                ? 0
                : 16 + (worlds.Count * entryHeight) + ((worlds.Count - 1) * gap) + 16;

            return Math.Max(0, contentHeight - listBounds.Height);
        }

        private void DrawScrollHint(SpriteBatch spriteBatch, Rectangle listBounds)
        {
            int maxScrollOffset = GetMaxScrollOffset();
            Rectangle track = new Rectangle(listBounds.Right - 10, listBounds.Y + 12, 4, listBounds.Height - 24);
            int thumbHeight = Math.Max(36, (int)(track.Height * (listBounds.Height / (float)(listBounds.Height + maxScrollOffset))));
            int thumbTravel = track.Height - thumbHeight;
            int thumbOffset = maxScrollOffset == 0 ? 0 : (int)(thumbTravel * (listScrollOffset / (float)maxScrollOffset));
            Rectangle thumb = new Rectangle(track.X, track.Y + thumbOffset, track.Width, thumbHeight);

            spriteBatch.Draw(pixel, track, new Color(70, 112, 128, 120));
            spriteBatch.Draw(pixel, thumb, new Color(168, 230, 207, 220));
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

        private Rectangle GetEditButtonBounds(Rectangle entryBounds)
        {
            return new Rectangle(entryBounds.Right - 118, entryBounds.Y + 10, 100, 28);
        }

        private Rectangle GetDeleteButtonBounds(Rectangle entryBounds)
        {
            return new Rectangle(entryBounds.Right - 118, entryBounds.Bottom - 38, 100, 28);
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
