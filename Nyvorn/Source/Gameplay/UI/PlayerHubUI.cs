using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nyvorn.Source.Engine.Input;
using Nyvorn.Source.Game.States;
using Nyvorn.Source.Gameplay.Crafting;
using Nyvorn.Source.Gameplay.Items;

namespace Nyvorn.Source.Gameplay.UI
{
    public sealed class PlayerHubUI
    {
        private const int WorkbenchWoodCost = 10;
        private const int WoodPickaxeWoodCost = 10;
        private const int StonePickaxeWoodCost = 7;
        private const int StonePickaxeStoneCost = 4;
        private const int WoodDoorWoodCost = 12;

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
            if (CanCraftWorkbench() && GetWorkbenchRecipeBounds().Contains(mousePosition))
            {
                CraftWorkbench();
                return true;
            }

            if (CanCraftWoodPickaxe() && GetWoodPickaxeRecipeBounds().Contains(mousePosition))
            {
                CraftWoodPickaxe();
                return true;
            }

            if (CanCraftStonePickaxe(craftTier) && GetStonePickaxeRecipeBounds().Contains(mousePosition))
            {
                CraftStonePickaxe();
                return true;
            }

            if (CanCraftWoodDoor(craftTier) && GetWoodDoorRecipeBounds().Contains(mousePosition))
            {
                CraftWoodDoor();
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

            if (CanCraftWorkbench())
                DrawRecipe(spriteBatch, GetWorkbenchRecipeBounds(), ItemId.Workbench, "Workbench", "10 Raw Wood");

            if (CanCraftWoodPickaxe())
                DrawRecipe(spriteBatch, GetWoodPickaxeRecipeBounds(), ItemId.WoodPickaxe, "Wood Pickaxe", "10 Raw Wood");

            if (CanCraftStonePickaxe(craftTier))
                DrawRecipe(spriteBatch, GetStonePickaxeRecipeBounds(), ItemId.StonePickaxe, "Stone Pickaxe", "7 Raw Wood + 4 Stone");

            if (CanCraftWoodDoor(craftTier))
                DrawRecipe(spriteBatch, GetWoodDoorRecipeBounds(), ItemId.WoodDoor, "Wood Door", "12 Raw Wood");
        }

        private void DrawRecipe(SpriteBatch spriteBatch, Rectangle bounds, ItemId itemId, string name, string cost)
        {
            MouseState mouse = Mouse.GetState();
            bool hovering = bounds.Contains(mouse.Position);
            spriteBatch.Draw(pixel, bounds, hovering ? new Color(82, 74, 50, 235) : new Color(48, 48, 48, 225));
            spriteBatch.Draw(pixel, new Rectangle(bounds.X + 2, bounds.Y + 2, 36, 36), new Color(22, 22, 22, 230));

            if (session.TryGetItemTexture(itemId, out Texture2D texture) &&
                ItemDefinitions.TryGet(itemId, out ItemDefinition definition))
            {
                Rectangle iconRect = itemId switch
                {
                    ItemId.Workbench => new Rectangle(bounds.X + 8, bounds.Y + 12, 24, 16),
                    ItemId.WoodDoor => new Rectangle(bounds.X + 15, bounds.Y + 5, 10, 30),
                    _ => new Rectangle(bounds.X + 4, bounds.Y + 4, 32, 32)
                };
                spriteBatch.Draw(texture, iconRect, definition.SourceRectangle, Color.White);
            }

            SpriteFont font = session.HudRenderer.Font;
            spriteBatch.DrawString(font, name, new Vector2(bounds.X + 48, bounds.Y + 6), Color.White);
            spriteBatch.DrawString(font, cost, new Vector2(bounds.X + 48, bounds.Y + 24), new Color(214, 196, 150));
        }

        private void DrawHeldItem(SpriteBatch spriteBatch, Point mousePosition)
        {
            if (heldSlot.IsEmpty || !ItemDefinitions.TryGet(heldSlot.ItemId, out ItemDefinition definition) || !session.TryGetItemTexture(heldSlot.ItemId, out Texture2D itemTexture))
                return;

            Rectangle iconRect = new Rectangle(mousePosition.X - 16, mousePosition.Y - 16, 32, 32);
            spriteBatch.Draw(itemTexture, iconRect, definition.SourceRectangle, Color.White);
        }

        private bool CanCraftWorkbench()
        {
            return session.CountItem(ItemId.RawWood) >= WorkbenchWoodCost;
        }

        private bool CanCraftWoodPickaxe()
        {
            return session.CountItem(ItemId.RawWood) >= WoodPickaxeWoodCost;
        }

        private bool CanCraftStonePickaxe(CraftTier craftTier)
        {
            return craftTier >= CraftTier.Workbench &&
                   session.CountItem(ItemId.RawWood) >= StonePickaxeWoodCost &&
                   session.CountItem(ItemId.StoneBlock) >= StonePickaxeStoneCost;
        }

        private bool CanCraftWoodDoor(CraftTier craftTier)
        {
            return craftTier >= CraftTier.Workbench &&
                   session.CountItem(ItemId.RawWood) >= WoodDoorWoodCost;
        }

        private void CraftWorkbench()
        {
            if (!session.TryConsumeItem(ItemId.RawWood, WorkbenchWoodCost))
                return;

            if (!session.TryStoreItem(ItemId.Workbench, 1, preferInventory: true))
                session.TryDropItem(ItemId.Workbench);
        }

        private void CraftWoodPickaxe()
        {
            if (!session.TryConsumeItem(ItemId.RawWood, WoodPickaxeWoodCost))
                return;

            if (!session.TryStoreItem(ItemId.WoodPickaxe, 1, preferInventory: true))
                session.TryDropItem(ItemId.WoodPickaxe);
        }

        private void CraftStonePickaxe()
        {
            if (!session.TryConsumeItem(ItemId.RawWood, StonePickaxeWoodCost))
                return;

            if (!session.TryConsumeItem(ItemId.StoneBlock, StonePickaxeStoneCost))
            {
                session.TryStoreItem(ItemId.RawWood, StonePickaxeWoodCost, preferInventory: true);
                return;
            }

            if (!session.TryStoreItem(ItemId.StonePickaxe, 1, preferInventory: true))
                session.TryDropItem(ItemId.StonePickaxe);
        }

        private void CraftWoodDoor()
        {
            if (!session.TryConsumeItem(ItemId.RawWood, WoodDoorWoodCost))
                return;

            if (!session.TryStoreItem(ItemId.WoodDoor, 1, preferInventory: true))
                session.TryDropItem(ItemId.WoodDoor);
        }

        private Rectangle GetCraftPanelBounds(int screenWidth, int screenHeight)
        {
            Rectangle inventory = session.GetInventoryPanelBounds(screenWidth, screenHeight);
            const int width = 252;
            const int height = 246;
            int x = inventory.Right + 12;
            int y = inventory.Y;

            if (x + width > screenWidth - 12)
            {
                x = inventory.X;
                y = inventory.Bottom + 12;
            }

            if (y + height > screenHeight - 12)
                y = System.Math.Max(12, inventory.Y - height - 12);

            return new Rectangle(x, y, width, height);
        }

        private Rectangle GetWorkbenchRecipeBounds()
        {
            Rectangle panel = GetCraftPanelBounds(
                graphicsDevice.PresentationParameters.BackBufferWidth,
                graphicsDevice.PresentationParameters.BackBufferHeight);
            return new Rectangle(panel.X + 12, panel.Y + 48, panel.Width - 24, 42);
        }

        private Rectangle GetWoodPickaxeRecipeBounds()
        {
            Rectangle panel = GetCraftPanelBounds(
                graphicsDevice.PresentationParameters.BackBufferWidth,
                graphicsDevice.PresentationParameters.BackBufferHeight);
            return new Rectangle(panel.X + 12, panel.Y + 96, panel.Width - 24, 42);
        }

        private Rectangle GetStonePickaxeRecipeBounds()
        {
            Rectangle panel = GetCraftPanelBounds(
                graphicsDevice.PresentationParameters.BackBufferWidth,
                graphicsDevice.PresentationParameters.BackBufferHeight);
            return new Rectangle(panel.X + 12, panel.Y + 144, panel.Width - 24, 42);
        }

        private Rectangle GetWoodDoorRecipeBounds()
        {
            Rectangle panel = GetCraftPanelBounds(
                graphicsDevice.PresentationParameters.BackBufferWidth,
                graphicsDevice.PresentationParameters.BackBufferHeight);
            return new Rectangle(panel.X + 12, panel.Y + 192, panel.Width - 24, 42);
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
