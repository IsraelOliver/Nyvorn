using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Gameplay.UI;
using Nyvorn.Source.World.Persistence;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Game.States
{
    public sealed class PlayerSelectState : IGameState
    {
        public bool UpdateBelow => false;
        public bool DrawBelow => false;
        public bool BlockInputBelow => true;

        private readonly GraphicsDevice graphicsDevice;
        private readonly ContentManager content;
        private readonly StateMachine stateMachine;
        private readonly PlayerSaveService saveService = new();
        private readonly SpriteFont font;
        private readonly Texture2D pixel;
        private readonly Texture2D backgroundTexture;

        private const int PlayerEntryHeight = 64;
        private const int PlayerEntryGap = 10;

        private MouseState previousMouse;
        private KeyboardState previousKeyboard;
        private IReadOnlyList<PlayerSaveSummary> players = new List<PlayerSaveSummary>();
        private int listScrollOffset;

        public PlayerSelectState(GraphicsDevice graphicsDevice, ContentManager content, StateMachine stateMachine)
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
            RefreshPlayers();
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
            Rectangle listBounds = GetPlayerListBounds();

            if (keyboard.IsKeyDown(Keys.Escape) && !previousKeyboard.IsKeyDown(Keys.Escape))
            {
                ReturnToWorldSelect();
                previousMouse = mouse;
                previousKeyboard = keyboard;
                return;
            }

            if (listBounds.Contains(mouse.Position))
            {
                int wheelDelta = mouse.ScrollWheelValue - previousMouse.ScrollWheelValue;
                if (wheelDelta != 0)
                    listScrollOffset = Math.Clamp(listScrollOffset - Math.Sign(wheelDelta) * 40, 0, GetMaxScrollOffset());
            }

            if (leftClickPressed && GetNewPlayerButtonBounds().Contains(mouse.Position))
            {
                stateMachine.ReplaceState(new PlayerCreationState(graphicsDevice, content, stateMachine));
                previousMouse = mouse;
                previousKeyboard = keyboard;
                return;
            }

            if (leftClickPressed && GetBackButtonBounds().Contains(mouse.Position))
            {
                ReturnToWorldSelect();
                previousMouse = mouse;
                previousKeyboard = keyboard;
                return;
            }

            if (leftClickPressed && listBounds.Contains(mouse.Position))
            {
                foreach ((PlayerSaveSummary summary, Rectangle bounds) in GetPlayerEntryBounds())
                {
                    if (!bounds.Intersects(listBounds))
                        continue;

                    Rectangle editButton = GetEditButtonBounds(bounds);
                    Rectangle deleteButton = GetDeleteButtonBounds(bounds);
                    if (editButton.Contains(mouse.Position))
                    {
                        stateMachine.ReplaceState(new PlayerEditState(graphicsDevice, content, stateMachine, summary));
                        previousMouse = mouse;
                        previousKeyboard = keyboard;
                        return;
                    }

                    if (deleteButton.Contains(mouse.Position))
                    {
                        saveService.Delete(summary.PlayerId);
                        if (string.Equals(saveService.GetLastSelectedPlayerId(), summary.PlayerId, StringComparison.Ordinal))
                            saveService.SetLastSelectedPlayerId(null);
                        RefreshPlayers();
                        break;
                    }

                    if (!bounds.Contains(mouse.Position))
                        continue;

                    saveService.SetLastSelectedPlayerId(summary.PlayerId);
                    ReturnToWorldSelect();
                    previousMouse = mouse;
                    previousKeyboard = keyboard;
                    return;
                }
            }

            previousMouse = mouse;
            previousKeyboard = keyboard;
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            Rectangle panel = GetPanelBounds();
            Rectangle listBounds = GetPlayerListBounds();
            Rectangle newPlayerButton = GetNewPlayerButtonBounds();
            Rectangle backButton = GetBackButtonBounds();
            Rectangle screenBounds = new Rectangle(0, 0, graphicsDevice.PresentationParameters.BackBufferWidth, graphicsDevice.PresentationParameters.BackBufferHeight);
            RasterizerState scissorRasterizer = new RasterizerState { ScissorTestEnable = true };

            spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            spriteBatch.Draw(backgroundTexture, screenBounds, Color.White);
            spriteBatch.Draw(pixel, screenBounds, new Color(10, 22, 26, 150));
            spriteBatch.Draw(pixel, panel, new Color(23, 42, 49, 220));

            spriteBatch.DrawString(font, "Jogadores", new Vector2(panel.X + 28, panel.Y + 24), new Color(255, 241, 193));
            string wrappedSubtitle = TextLayout.WrapText(font, "Escolha um personagem para levar aos mundos, ou crie um novo.", panel.Width - 56);
            spriteBatch.DrawString(font, wrappedSubtitle, new Vector2(panel.X + 28, panel.Y + 50), new Color(168, 230, 207));

            spriteBatch.Draw(pixel, listBounds, new Color(18, 34, 40, 210));

            if (players.Count == 0)
            {
                string wrappedEmptyTitle = TextLayout.WrapText(font, "Nenhum jogador criado ainda.", listBounds.Width - 36);
                string wrappedEmptyHint = TextLayout.WrapText(font, "Crie um novo para gerar o primeiro .ply.", listBounds.Width - 36);
                spriteBatch.DrawString(font, wrappedEmptyTitle, new Vector2(listBounds.X + 18, listBounds.Y + 18), Color.White);
                spriteBatch.DrawString(font, wrappedEmptyHint, new Vector2(listBounds.X + 18, listBounds.Y + 18 + font.LineSpacing * 2), new Color(143, 211, 255));
            }
            else if (GetMaxScrollOffset() > 0)
            {
                DrawScrollHint(spriteBatch, listBounds);
            }

            DrawButton(spriteBatch, newPlayerButton, "Novo Jogador", new Color(168, 230, 207), new Color(16, 31, 36));
            DrawButton(spriteBatch, backButton, "Voltar", new Color(28, 50, 58), Color.White);
            spriteBatch.End();

            if (players.Count > 0)
            {
                graphicsDevice.ScissorRectangle = listBounds;
                spriteBatch.Begin(samplerState: SamplerState.PointClamp, rasterizerState: scissorRasterizer);

                foreach ((PlayerSaveSummary summary, Rectangle bounds) in GetPlayerEntryBounds())
                    DrawPlayerEntry(spriteBatch, summary, bounds);

                spriteBatch.End();
            }
        }

        private void DrawPlayerEntry(SpriteBatch spriteBatch, PlayerSaveSummary summary, Rectangle bounds)
        {
            bool selected = string.Equals(saveService.GetLastSelectedPlayerId(), summary.PlayerId, StringComparison.Ordinal);
            bool hovered = bounds.Contains(Mouse.GetState().Position);
            Color fill = hovered ? new Color(39, 70, 79) : new Color(28, 50, 58);
            Color accent = selected ? new Color(255, 241, 193) : new Color(143, 211, 255);
            Rectangle editButton = GetEditButtonBounds(bounds);
            Rectangle deleteButton = GetDeleteButtonBounds(bounds);
            bool editHovered = editButton.Contains(Mouse.GetState().Position);
            bool deleteHovered = deleteButton.Contains(Mouse.GetState().Position);

            spriteBatch.Draw(pixel, new Rectangle(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4), accent * 0.7f);
            spriteBatch.Draw(pixel, bounds, fill);

            string title = selected ? $"{summary.Name} (Atual)" : summary.Name;
            string saveInfo = $"Salvo em {summary.SavedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}";
            float textWidth = GetPlayerEntryTextWidth(bounds);

            Vector2 textPos = new Vector2(bounds.X + 14, bounds.Y + 10);
            spriteBatch.DrawString(font, TextLayout.WrapText(font, title, textWidth), textPos, Color.White);
            textPos.Y += font.LineSpacing;
            spriteBatch.DrawString(font, TextLayout.WrapText(font, saveInfo, textWidth), textPos, new Color(168, 230, 207));

            spriteBatch.Draw(pixel, new Rectangle(editButton.X - 2, editButton.Y - 2, editButton.Width + 4, editButton.Height + 4), new Color(143, 211, 255, 150));
            spriteBatch.Draw(pixel, editButton, editHovered ? new Color(190, 238, 255) : new Color(143, 211, 255));
            DrawCenteredLabel(spriteBatch, editButton, "Editar", new Color(16, 31, 36));

            spriteBatch.Draw(pixel, new Rectangle(deleteButton.X - 2, deleteButton.Y - 2, deleteButton.Width + 4, deleteButton.Height + 4), new Color(255, 180, 180, 150));
            spriteBatch.Draw(pixel, deleteButton, deleteHovered ? new Color(255, 210, 210) : new Color(230, 140, 140));
            DrawCenteredLabel(spriteBatch, deleteButton, "Excluir", new Color(68, 24, 24));
        }

        private void DrawButton(SpriteBatch spriteBatch, Rectangle bounds, string label, Color fill, Color textColor)
        {
            bool hovered = bounds.Contains(Mouse.GetState().Position);
            Color actualFill = hovered ? Color.Lerp(fill, Color.White, 0.12f) : fill;

            spriteBatch.Draw(pixel, new Rectangle(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4), new Color(143, 211, 255, 150));
            spriteBatch.Draw(pixel, bounds, actualFill);
            DrawCenteredLabel(spriteBatch, bounds, label, textColor);
        }

        private void DrawCenteredLabel(SpriteBatch spriteBatch, Rectangle bounds, string label, Color textColor)
        {
            Vector2 labelSize = font.MeasureString(label);
            Vector2 labelPos = new Vector2(bounds.X + (bounds.Width - labelSize.X) * 0.5f, bounds.Y + (bounds.Height - labelSize.Y) * 0.5f);
            spriteBatch.DrawString(font, label, labelPos, textColor);
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

        private void RefreshPlayers()
        {
            players = saveService.ListPlayers();
            listScrollOffset = Math.Clamp(listScrollOffset, 0, GetMaxScrollOffset());
        }

        private void ReturnToWorldSelect()
        {
            stateMachine.ReplaceState(new WorldSelectState(graphicsDevice, content, stateMachine));
        }

        private IEnumerable<(PlayerSaveSummary Summary, Rectangle Bounds)> GetPlayerEntryBounds()
        {
            Rectangle listBounds = GetPlayerListBounds();
            int y = listBounds.Y + 16 - listScrollOffset;

            for (int i = 0; i < players.Count; i++)
            {
                Rectangle bounds = new Rectangle(listBounds.X + 16, y, listBounds.Width - 32, PlayerEntryHeight);
                yield return (players[i], bounds);
                y += PlayerEntryHeight + PlayerEntryGap;
            }
        }

        private int GetMaxScrollOffset()
        {
            Rectangle listBounds = GetPlayerListBounds();
            int contentHeight = players.Count == 0
                ? 0
                : 16 + 16 + (players.Count * PlayerEntryHeight) + ((players.Count - 1) * PlayerEntryGap);

            return Math.Max(0, contentHeight - listBounds.Height);
        }

        private float GetPlayerEntryTextWidth(Rectangle entryBounds)
        {
            return Math.Max(48f, entryBounds.Width - 146f);
        }

        private Rectangle GetEditButtonBounds(Rectangle entryBounds)
        {
            return new Rectangle(entryBounds.Right - 118, entryBounds.Y + 8, 100, 24);
        }

        private Rectangle GetDeleteButtonBounds(Rectangle entryBounds)
        {
            return new Rectangle(entryBounds.Right - 118, entryBounds.Bottom - 32, 100, 24);
        }

        private Rectangle GetPanelBounds()
        {
            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
            return new Rectangle((screenW - 720) / 2, (screenH - 620) / 2, 720, 620);
        }

        private Rectangle GetPlayerListBounds()
        {
            Rectangle panel = GetPanelBounds();
            return new Rectangle(panel.X + 28, panel.Y + 92, panel.Width - 56, 410);
        }

        private Rectangle GetNewPlayerButtonBounds()
        {
            Rectangle panel = GetPanelBounds();
            return new Rectangle(panel.X + 28, panel.Bottom - 92, (panel.Width - 56 - 20) / 2, 48);
        }

        private Rectangle GetBackButtonBounds()
        {
            Rectangle panel = GetPanelBounds();
            int width = (panel.Width - 56 - 20) / 2;
            return new Rectangle(panel.Right - 28 - width, panel.Bottom - 92, width, 48);
        }
    }
}
