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
        private const int HotbarPadding = 14;
        private const int HotbarWidth = 137;
        private const int HotbarHeight = 22;
        private const int HotbarSlotSize = 22;
        private const int HotbarSlotStep = 23;
        private const int HotbarItemSize = 16;
        private const int HotbarItemInset = 3;
        private const int HotbarScale = 2;

        private readonly Texture2D pixel;
        private readonly Texture2D toolbarTexture;
        private readonly SpriteFont font;
        private readonly IReadOnlyDictionary<ItemId, Texture2D> itemTextures;

        private readonly record struct HotbarLayout(Rectangle Bounds, Rectangle Source, Rectangle SelectedSource);
        private readonly record struct InventoryLayout(Rectangle Panel, Point SlotStart);

        public HudRenderer(GraphicsDevice graphicsDevice, Texture2D toolbarTexture, SpriteFont font, IReadOnlyDictionary<ItemId, Texture2D> itemTextures)
        {
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
            this.toolbarTexture = toolbarTexture;
            this.font = font;
            this.itemTextures = itemTextures;
        }

        public void Draw(SpriteBatch spriteBatch, Hotbar hotbar, int selectedHotbarIndex, int currentHealth, int maxHealth, int screenWidth, int screenHeight)
        {
            DrawHotbar(spriteBatch, hotbar, selectedHotbarIndex, screenWidth, screenHeight);
            DrawPlayerHealth(spriteBatch, currentHealth, maxHealth, screenWidth);
        }

        public Rectangle GetInventoryPanelBounds(int screenWidth, int screenHeight)
        {
            return GetInventoryLayout(screenWidth, screenHeight).Panel;
        }

        public void DrawInventoryPanel(SpriteBatch spriteBatch, Hotbar hotbar, Inventory inventory, int selectedHotbarIndex, int screenWidth, int screenHeight)
        {
            InventoryLayout layout = GetInventoryLayout(screenWidth, screenHeight);
            Rectangle panel = layout.Panel;
            spriteBatch.Draw(pixel, panel, new Color(12, 12, 12, 220));
            spriteBatch.Draw(pixel, new Rectangle(panel.X - 2, panel.Y - 2, panel.Width + 4, panel.Height + 4), Color.Black * 0.9f);
            spriteBatch.DrawString(font, "Inventario", new Vector2(panel.X + 12, panel.Y + 8), Color.White);

            DrawSlots(spriteBatch, inventory.Slots, layout.SlotStart.X, layout.SlotStart.Y, 5, 2);
        }

        public bool TryGetSlotAtPoint(Hotbar hotbar, Inventory inventory, int screenWidth, int screenHeight, Point point, out bool isHotbar, out int slotIndex)
        {
            InventoryLayout inventoryLayout = GetInventoryLayout(screenWidth, screenHeight);
            for (int i = 0; i < hotbar.Capacity; i++)
            {
                Rectangle bounds = GetHotbarSlotBounds(i, screenWidth, screenHeight);
                if (bounds.Contains(point))
                {
                    isHotbar = true;
                    slotIndex = i;
                    return true;
                }
            }

            for (int i = 0; i < inventory.Capacity; i++)
            {
                Rectangle bounds = GetSlotBounds(inventoryLayout.SlotStart.X, inventoryLayout.SlotStart.Y, 5, i);
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

        private void DrawHotbar(SpriteBatch spriteBatch, Hotbar hotbar, int selectedHotbarIndex, int screenWidth, int screenHeight)
        {
            HotbarLayout layout = GetHotbarLayout(screenWidth, screenHeight);
            spriteBatch.Draw(toolbarTexture, layout.Bounds, layout.Source, Color.White);

            for (int i = 0; i < hotbar.Capacity; i++)
            {
                Rectangle slotBounds = GetHotbarSlotBounds(i, screenWidth, screenHeight);

                if (i == selectedHotbarIndex)
                    spriteBatch.Draw(toolbarTexture, slotBounds, layout.SelectedSource, Color.White);

                DrawHotbarItem(spriteBatch, hotbar.Slots[i], slotBounds);
            }
        }

        private void DrawHotbarItem(SpriteBatch spriteBatch, InventorySlot slot, Rectangle slotBounds)
        {
            if (slot.IsEmpty || !itemTextures.TryGetValue(slot.ItemId, out Texture2D itemTexture) || !ItemDefinitions.TryGet(slot.ItemId, out ItemDefinition definition))
                return;

            Rectangle iconRect = new Rectangle(
                slotBounds.X + HotbarItemInset * HotbarScale,
                slotBounds.Y + HotbarItemInset * HotbarScale,
                HotbarItemSize * HotbarScale,
                HotbarItemSize * HotbarScale);

            DrawItemIcon(spriteBatch, itemTexture, definition, iconRect);
            DrawStackCount(spriteBatch, slot, slotBounds, insetX: 2f, insetY: 1f);
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
            DrawItemIcon(spriteBatch, itemTexture, definition, iconRect);
            DrawStackCount(spriteBatch, slot, outer, insetX: 3f, insetY: 2f);
        }

        private void DrawItemIcon(SpriteBatch spriteBatch, Texture2D itemTexture, ItemDefinition definition, Rectangle destination)
        {
            spriteBatch.Draw(itemTexture, destination, definition.SourceRectangle, Color.White);
        }

        private void DrawStackCount(SpriteBatch spriteBatch, InventorySlot slot, Rectangle bounds, float insetX, float insetY)
        {
            if (slot.Quantity <= 1)
                return;

            string text = slot.Quantity.ToString();
            Vector2 textSize = font.MeasureString(text);
            Vector2 textPos = new Vector2(
                bounds.Right - textSize.X - insetX,
                bounds.Bottom - textSize.Y - insetY);

            spriteBatch.DrawString(font, text, textPos + new Vector2(1f, 1f), Color.Black);
            spriteBatch.DrawString(font, text, textPos, Color.White);
        }

        private Rectangle GetSlotBounds(int startX, int startY, int columns, int slotIndex)
        {
            int column = slotIndex % columns;
            int row = slotIndex / columns;
            int x = startX + column * (SlotSize + SlotGap);
            int y = startY + row * (SlotSize + SlotGap);
            return new Rectangle(x, y, SlotSize, SlotSize);
        }

        private Rectangle GetHotbarSlotBounds(int slotIndex, int screenWidth, int screenHeight)
        {
            Rectangle hotbarBounds = GetHotbarLayout(screenWidth, screenHeight).Bounds;
            return new Rectangle(
                hotbarBounds.X + slotIndex * HotbarSlotStep * HotbarScale,
                hotbarBounds.Y,
                HotbarSlotSize * HotbarScale,
                HotbarSlotSize * HotbarScale);
        }

        private HotbarLayout GetHotbarLayout(int screenWidth, int screenHeight)
        {
            int width = HotbarWidth * HotbarScale;
            int height = HotbarHeight * HotbarScale;
            Rectangle bounds = new Rectangle(
                (screenWidth - width) / 2,
                screenHeight - height - HotbarPadding,
                width,
                height);

            return new HotbarLayout(
                bounds,
                new Rectangle(0, 0, HotbarWidth, HotbarHeight),
                new Rectangle(0, 23, HotbarSlotSize, HotbarSlotSize));
        }

        private InventoryLayout GetInventoryLayout(int screenWidth, int screenHeight)
        {
            const int inventoryColumns = 5;
            const int inventoryRows = 2;
            const int panelPaddingX = 12;
            const int panelTopContentOffset = 34;

            int inventoryWidth = (inventoryColumns * SlotSize) + ((inventoryColumns - 1) * SlotGap);
            int panelWidth = inventoryWidth + (panelPaddingX * 2);
            int panelHeight = SlotSize + SlotGap + (inventoryRows * SlotSize) + SlotGap + 24;
            Rectangle panel = new Rectangle(
                (screenWidth - panelWidth) / 2,
                (screenHeight - panelHeight) / 2,
                panelWidth,
                panelHeight);

            Point slotStart = new Point(
                panel.X + panelPaddingX,
                panel.Y + panelTopContentOffset + SlotSize + SlotGap);

            return new InventoryLayout(panel, slotStart);
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
