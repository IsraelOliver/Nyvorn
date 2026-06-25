using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Game.States;
using Nyvorn.Source.Gameplay.Crafting;
using Nyvorn.Source.Gameplay.Items;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.UI
{
    public sealed class PlayerHubUI
    {
        private const int CraftPanelWidth = 252;
        private const int RecipeStartYOffset = 48;
        private const int RecipeHeight = 42;
        private const int RecipeStep = 48;
        private const int CraftPanelBottomPadding = 12;

        private readonly GraphicsDevice graphicsDevice;
        private readonly PlayingSession session;
        private readonly Texture2D pixel;
        private readonly InventorySlot heldSlot = new();

        public PlayerHubUI(GraphicsDevice graphicsDevice, PlayingSession session)
        {
            this.graphicsDevice = graphicsDevice;
            this.session = session;
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
        }

        public bool IsOpen { get; private set; }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                IsOpen = true;
        }

        public void Open()
        {
            IsOpen = true;
        }

        public void Close()
        {
            ReturnHeldItem();
            IsOpen = false;
        }

        public bool ContainsMouse(Point mousePosition, CraftTier craftTier)
        {
            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
            if (session.HudRenderer.TryGetSlotAtPoint(session.Hotbar, session.Inventory, screenW, screenH, mousePosition, out _, out _))
                return true;

            return session.GetInventoryPanelBounds(screenW, screenH).Contains(mousePosition) ||
                   GetCraftPanelBounds(screenW, screenH).Contains(mousePosition);
        }

        public void Update(InputState input, CraftTier craftTier)
        {
            if (!IsOpen)
                return;

            Point mousePosition = input.MouseScreenPosition.ToPoint();
            if (!input.AttackJustPressed)
                return;

            if (TryHandleRecipeClick(mousePosition, craftTier))
                return;

            TryHandleSlotClick(mousePosition, craftTier);
        }

        public void Draw(SpriteBatch spriteBatch, CraftTier craftTier)
        {
            if (!IsOpen)
                return;

            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;

            spriteBatch.Draw(pixel, new Rectangle(0, 0, screenW, screenH), Color.Black * 0.18f);
            session.DrawInventory(spriteBatch, screenW, screenH);
            DrawCraftPanel(spriteBatch, GetCraftPanelBounds(screenW, screenH), craftTier);
            DrawHeldItem(spriteBatch, Mouse.GetState().Position);
        }

        private bool TryHandleRecipeClick(Point mousePosition, CraftTier craftTier)
        {
            List<RecipeDefinition> recipes = GetVisibleRecipes(craftTier);
            for (int i = 0; i < recipes.Count; i++)
            {
                if (!GetRecipeBounds(GetCraftPanelBounds(
                        graphicsDevice.PresentationParameters.BackBufferWidth,
                        graphicsDevice.PresentationParameters.BackBufferHeight), i).Contains(mousePosition))
                {
                    continue;
                }

                CraftRecipe(recipes[i], craftTier);
                return true;
            }

            return false;
        }

        private void TryHandleSlotClick(Point mousePosition, CraftTier craftTier)
        {
            int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
            int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;

            if (!session.HudRenderer.TryGetSlotAtPoint(session.Hotbar, session.Inventory, screenW, screenH, mousePosition, out bool isHotbar, out int slotIndex))
            {
                if (!heldSlot.IsEmpty && !ContainsMouse(mousePosition, craftTier) && session.TryDropItem(heldSlot.ItemId))
                    heldSlot.RemoveOne();

                return;
            }

            InventorySlot clickedSlot = isHotbar ? session.Hotbar.GetSlot(slotIndex) : session.Inventory.GetSlot(slotIndex);

            if (heldSlot.IsEmpty)
            {
                heldSlot.CopyFrom(clickedSlot);
                clickedSlot.Clear();
                return;
            }

            if (clickedSlot.IsEmpty)
            {
                clickedSlot.CopyFrom(heldSlot);
                heldSlot.Clear();
                return;
            }

            if (heldSlot.ItemId == clickedSlot.ItemId && ItemDefinitions.TryGet(heldSlot.ItemId, out ItemDefinition definition) && definition.Stackable)
            {
                int added = clickedSlot.Add(definition, heldSlot.Quantity);
                if (added > 0)
                    heldSlot.Set(heldSlot.ItemId, heldSlot.Quantity - added);

                return;
            }

            InventorySlot temp = clickedSlot.Clone();
            clickedSlot.CopyFrom(heldSlot);
            heldSlot.CopyFrom(temp);
        }

        private void DrawCraftPanel(SpriteBatch spriteBatch, Rectangle panel, CraftTier craftTier)
        {
            spriteBatch.Draw(pixel, panel, new Color(12, 12, 12, 230));
            spriteBatch.Draw(pixel, new Rectangle(panel.X - 2, panel.Y - 2, panel.Width + 4, panel.Height + 4), Color.Black * 0.9f);
            spriteBatch.DrawString(session.HudRenderer.Font, "Crafting", new Vector2(panel.X + 12, panel.Y + 10), Color.White);

            List<RecipeDefinition> recipes = GetVisibleRecipes(craftTier);
            for (int i = 0; i < recipes.Count; i++)
                DrawRecipe(spriteBatch, GetRecipeBounds(panel, i), recipes[i]);
        }

        private void DrawRecipe(SpriteBatch spriteBatch, Rectangle bounds, RecipeDefinition recipe)
        {
            MouseState mouse = Mouse.GetState();
            bool hovering = bounds.Contains(mouse.Position);
            spriteBatch.Draw(pixel, bounds, hovering ? new Color(82, 74, 50, 235) : new Color(48, 48, 48, 225));
            spriteBatch.Draw(pixel, new Rectangle(bounds.X + 2, bounds.Y + 2, 36, 36), new Color(22, 22, 22, 230));

            if (session.TryGetItemTexture(recipe.ResultItemId, out Texture2D texture) &&
                ItemDefinitions.TryGet(recipe.ResultItemId, out ItemDefinition definition))
            {
                Rectangle iconRect = recipe.ResultItemId switch
                {
                    ItemId.Workbench => new Rectangle(bounds.X + 8, bounds.Y + 12, 24, 16),
                    ItemId.WoodDoor => new Rectangle(bounds.X + 15, bounds.Y + 5, 10, 30),
                    _ => new Rectangle(bounds.X + 4, bounds.Y + 4, 32, 32)
                };
                spriteBatch.Draw(texture, ItemIconLayout.FitInside(definition, iconRect), definition.SourceRectangle, Color.White);
            }

            SpriteFont font = session.HudRenderer.Font;
            spriteBatch.DrawString(font, recipe.DisplayName, new Vector2(bounds.X + 48, bounds.Y + 6), Color.White);
            spriteBatch.DrawString(font, recipe.DisplayCost, new Vector2(bounds.X + 48, bounds.Y + 24), new Color(214, 196, 150));
        }

        private void DrawHeldItem(SpriteBatch spriteBatch, Point mousePosition)
        {
            if (heldSlot.IsEmpty || !ItemDefinitions.TryGet(heldSlot.ItemId, out ItemDefinition definition) || !session.TryGetItemTexture(heldSlot.ItemId, out Texture2D itemTexture))
                return;

            Rectangle iconRect = new Rectangle(mousePosition.X - 16, mousePosition.Y - 16, 32, 32);
            spriteBatch.Draw(itemTexture, ItemIconLayout.FitInside(definition, iconRect), definition.SourceRectangle, Color.White);
        }

        private List<RecipeDefinition> GetVisibleRecipes(CraftTier craftTier)
        {
            List<RecipeDefinition> availableRecipes = RecipeRegistry.GetAvailable(craftTier);
            List<RecipeDefinition> visibleRecipes = new();
            for (int i = 0; i < availableRecipes.Count; i++)
            {
                RecipeDefinition recipe = availableRecipes[i];
                if (CanCraftRecipe(recipe, craftTier))
                    visibleRecipes.Add(recipe);
            }

            return visibleRecipes;
        }

        private bool CanCraftRecipe(RecipeDefinition recipe, CraftTier craftTier)
        {
            if (recipe.RequiredTier > craftTier)
                return false;

            for (int i = 0; i < recipe.Ingredients.Count; i++)
            {
                RecipeIngredient ingredient = recipe.Ingredients[i];
                if (session.CountItem(ingredient.ItemId) < ingredient.Quantity)
                    return false;
            }

            return true;
        }

        private void CraftRecipe(RecipeDefinition recipe, CraftTier craftTier)
        {
            if (!CanCraftRecipe(recipe, craftTier))
                return;

            List<RecipeIngredient> consumed = new();
            for (int i = 0; i < recipe.Ingredients.Count; i++)
            {
                RecipeIngredient ingredient = recipe.Ingredients[i];
                if (!session.TryConsumeItem(ingredient.ItemId, ingredient.Quantity))
                {
                    RollBackConsumedIngredients(consumed);
                    return;
                }

                consumed.Add(ingredient);
            }

            if (!session.TryStoreItem(recipe.ResultItemId, recipe.ResultQuantity, preferInventory: true))
                session.TryDropItem(recipe.ResultItemId);
        }

        private void RollBackConsumedIngredients(List<RecipeIngredient> consumed)
        {
            for (int i = 0; i < consumed.Count; i++)
            {
                RecipeIngredient ingredient = consumed[i];
                session.TryStoreItem(ingredient.ItemId, ingredient.Quantity, preferInventory: true);
            }
        }

        private Rectangle GetCraftPanelBounds(int screenWidth, int screenHeight)
        {
            Rectangle inventory = session.GetInventoryPanelBounds(screenWidth, screenHeight);
            int recipeCount = RecipeRegistry.GetAll().Count;
            int height = RecipeStartYOffset + (recipeCount * RecipeStep) - (RecipeStep - RecipeHeight) + CraftPanelBottomPadding;
            int x = inventory.Right + 12;
            int y = inventory.Y;

            if (x + CraftPanelWidth > screenWidth - 12)
            {
                x = inventory.X;
                y = inventory.Bottom + 12;
            }

            if (y + height > screenHeight - 12)
                y = System.Math.Max(12, inventory.Y - height - 12);

            return new Rectangle(x, y, CraftPanelWidth, height);
        }

        private static Rectangle GetRecipeBounds(Rectangle panel, int recipeIndex)
        {
            return new Rectangle(
                panel.X + 12,
                panel.Y + RecipeStartYOffset + (recipeIndex * RecipeStep),
                panel.Width - 24,
                RecipeHeight);
        }

        private void ReturnHeldItem()
        {
            if (heldSlot.IsEmpty)
                return;

            if (session.TryStoreItem(heldSlot.ItemId, heldSlot.Quantity, preferInventory: true))
                heldSlot.Clear();
        }
    }
}
