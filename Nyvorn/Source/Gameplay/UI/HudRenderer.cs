using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Gameplay.Items;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.UI
{
    public sealed class HudRenderer
    {
        public const int SlotSize = 36;
        public const int SlotGap = 6;

        private readonly Texture2D pixel;
        private readonly SpriteFont font;
        private readonly IReadOnlyDictionary<ItemId, Texture2D> itemTextures;

        public HudRenderer(GraphicsDevice graphicsDevice, SpriteFont font, IReadOnlyDictionary<ItemId, Texture2D> itemTextures)
        {
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
            this.font = font;
            this.itemTextures = itemTextures;
        }

        public void Draw(SpriteBatch spriteBatch, Hotbar hotbar, int selectedHotbarIndex, int currentHealth, int maxHealth, int screenWidth)
        {
            DrawHotbar(spriteBatch, hotbar, selectedHotbarIndex);
            DrawPlayerHealth(spriteBatch, currentHealth, maxHealth, screenWidth);
        }

        public Rectangle GetInventoryPanelBounds(int screenWidth, int screenHeight)
        {
            const int padding = 14;
            const int inventoryWidth = (5 * SlotSize) + (4 * SlotGap);
            const int panelWidth = inventoryWidth + 24;
            const int panelHeight = SlotSize + SlotGap + (2 * SlotSize) + SlotGap + 24;
            return new Rectangle(
                padding - 8,
                padding - 8,
                panelWidth,
                panelHeight);
        }

        public void DrawInventoryPanel(SpriteBatch spriteBatch, Hotbar hotbar, Inventory inventory, int selectedHotbarIndex, int screenWidth, int screenHeight)
        {
            Rectangle panel = GetInventoryPanelBounds(screenWidth, screenHeight);
            spriteBatch.Draw(pixel, panel, new Color(12, 12, 12, 220));
            spriteBatch.Draw(pixel, new Rectangle(panel.X - 2, panel.Y - 2, panel.Width + 4, panel.Height + 4), Color.Black * 0.9f);
            spriteBatch.DrawString(font, "Inventario", new Vector2(panel.X + 12, panel.Y + 8), Color.White);

            int inventoryStartX = panel.X + 12;
            int inventoryStartY = panel.Y + 34 + SlotSize + SlotGap;
            DrawSlots(spriteBatch, inventory.Slots, inventoryStartX, inventoryStartY, 5, 2);
        }

        public bool TryGetSlotAtPoint(Hotbar hotbar, Inventory inventory, int screenWidth, int screenHeight, Point point, out bool isHotbar, out int slotIndex)
        {
            Rectangle panel = GetInventoryPanelBounds(screenWidth, screenHeight);
            const int padding = 14;

            for (int i = 0; i < hotbar.Capacity; i++)
            {
                Rectangle bounds = GetSlotBounds(padding, padding, hotbar.Capacity, i);
                if (bounds.Contains(point))
                {
                    isHotbar = true;
                    slotIndex = i;
                    return true;
                }
            }

            for (int i = 0; i < inventory.Capacity; i++)
            {
                Rectangle bounds = GetSlotBounds(panel.X + 12, panel.Y + 34 + SlotSize + SlotGap, 5, i);
                if (bounds.Contains(point))
                {
                    isHotbar = false;
                    slotIndex = i;
                    return true;
                }
            }

            isHotbar = false;
            slotIndex = -1;
            return false;
        }

        private void DrawHotbar(SpriteBatch spriteBatch, Hotbar hotbar, int selectedHotbarIndex)
        {
            const int padding = 14;

            for (int i = 0; i < hotbar.Capacity; i++)
            {
                int x = padding + i * (SlotSize + SlotGap);
                int y = padding;
                DrawSlot(spriteBatch, hotbar.Slots[i], x, y, i == selectedHotbarIndex);
            }
        }

        private void DrawSlots(SpriteBatch spriteBatch, IReadOnlyList<InventorySlot> slots, int startX, int startY, int columns, int rows)
        {
            DrawSlots(spriteBatch, slots, startX, startY, columns, rows, -1);
        }

        private void DrawSlots(SpriteBatch spriteBatch, IReadOnlyList<InventorySlot> slots, int startX, int startY, int columns, int rows, int selectedIndex)
        {
            for (int i = 0; i < slots.Count && i < columns * rows; i++)
            {
                Rectangle slotBounds = GetSlotBounds(startX, startY, columns, i);
                int x = slotBounds.X;
                int y = slotBounds.Y;
                DrawSlot(spriteBatch, slots[i], x, y, i == selectedIndex);
            }
        }

        private void DrawSlot(SpriteBatch spriteBatch, InventorySlot slot, int x, int y, bool selected)
        {
            Rectangle outer = new Rectangle(x, y, SlotSize, SlotSize);
            Rectangle inner = new Rectangle(x + 2, y + 2, SlotSize - 4, SlotSize - 4);

            spriteBatch.Draw(pixel, outer, selected ? new Color(212, 190, 108, 240) : new Color(18, 18, 18, 220));
            spriteBatch.Draw(pixel, inner, new Color(66, 66, 66, 220));

            if (slot.IsEmpty || !itemTextures.TryGetValue(slot.ItemId, out Texture2D itemTexture) || !ItemDefinitions.TryGet(slot.ItemId, out ItemDefinition definition))
                return;

            Rectangle iconRect = new Rectangle(x + 2, y + 2, 32, 32);
            spriteBatch.Draw(itemTexture, iconRect, definition.SourceRectangle, Color.White);

            if (slot.Quantity > 1)
            {
                string text = slot.Quantity.ToString();
                Vector2 textSize = font.MeasureString(text);
                Vector2 textPos = new Vector2(x + SlotSize - textSize.X - 3f, y + SlotSize - textSize.Y - 2f);
                spriteBatch.DrawString(font, text, textPos + new Vector2(1f, 1f), Color.Black);
                spriteBatch.DrawString(font, text, textPos, Color.White);
            }
        }

        private Rectangle GetSlotBounds(int startX, int startY, int columns, int slotIndex)
        {
            int column = slotIndex % columns;
            int row = slotIndex / columns;
            int x = startX + column * (SlotSize + SlotGap);
            int y = startY + row * (SlotSize + SlotGap);
            return new Rectangle(x, y, SlotSize, SlotSize);
        }

        private void DrawPlayerHealth(SpriteBatch spriteBatch, int currentHealth, int maxHealth, int screenWidth)
        {
            const int width = 120;
            const int height = 14;
            const int padding = 14;

            float ratio = maxHealth <= 0 ? 0f : MathHelper.Clamp((float)currentHealth / maxHealth, 0f, 1f);
            int fill = (int)(width * ratio);
            int x = screenWidth - width - padding;
            int y = padding;

            spriteBatch.Draw(pixel, new Rectangle(x - 2, y - 2, width + 4, height + 4), Color.Black * 0.85f);
            spriteBatch.Draw(pixel, new Rectangle(x, y, width, height), new Color(52, 52, 52));
            if (fill > 0)
                spriteBatch.Draw(pixel, new Rectangle(x, y, fill, height), new Color(196, 44, 56));

            string label = $"{currentHealth}/{maxHealth}";
            Vector2 size = font.MeasureString(label);
            Vector2 textPos = new Vector2(x + (width - size.X) * 0.5f, y - size.Y - 2f);
            spriteBatch.DrawString(font, label, textPos + new Vector2(1f, 1f), Color.Black);
            spriteBatch.DrawString(font, label, textPos, Color.White);
        }
    }
}
