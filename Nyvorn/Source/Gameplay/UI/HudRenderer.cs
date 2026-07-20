using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nyvorn.Source.Gameplay.Items;
using System;
using System.Collections.Generic;

namespace Nyvorn.Source.Gameplay.UI
{
    public sealed class HudRenderer
    {
        public const int SlotSize = 36;
        public const int SlotGap = 6;
        private const int HotbarPadding = 14;
        private const int HotbarSlotSize = 22;
        private const int HotbarSlotStep = 23;
        private const int HotbarItemSize = 16;
        private const int HotbarItemInset = 3;
        private const int HotbarScale = 2;

        // lifebar.png (21x13): a 3-slice chunk (start/middle/end) so any number of 10-HP chunks can
        // be tiled side by side to build a bar of any length - the growth system just adds more
        // middle chunks. Row 1 (y=0) is the filled variant, row 2 (y=7, after a 1px gap) is empty.
        // Each chunk is 6px tall; start/end are 5px wide (1px outer border + 3px fill + 1px divider),
        // middle is 4px wide (3px fill + 1px divider, no outer border since it butts against neighbors).
        private const int LifeBarChunkHeight = 6;
        private const int LifeBarStartEndWidth = 5;
        private const int LifeBarMiddleWidth = 4;
        private const int LifeBarRowGap = 1;
        private const int LifeBarHealthPerChunk = 10;
        private const int LifeBarPadding = 14;

        // Each 10-HP chunk fills in 4 discrete steps, not 3: the 3 "normal" fill columns plus a
        // 4th, final step where the divider column itself lights up. Steps 1-3 are drawn via the
        // fill-strip overlay (DrawLifeBarPartialFill, which never touches the divider); step 4 means
        // the chunk is fully filled, so it just switches to the pre-baked filled sprite instead
        // (which already has the divider lit in its own art).
        private const int LifeBarStepsPerChunk = 4;

        // Partial-fill overlay strip (also in lifebar.png, to the right of the empty row): a 1px
        // wide x 4px tall fill-color column, stamped over an empty chunk to show a fill amount
        // finer than the binary full/empty sprites allow.
        private const int LifeBarStripSourceX = 17;
        private const int LifeBarStripHeight = 4;

        private enum LifeBarChunkKind { Start, Middle, End }

        private readonly Texture2D pixel;
        private readonly Texture2D toolbarTexture;
        private readonly Texture2D lifeBarTexture;
        private readonly SpriteFont font;
        private readonly IReadOnlyDictionary<ItemId, Texture2D> itemTextures;

        private readonly record struct HotbarLayout(Rectangle Bounds, Rectangle Source, Rectangle SelectedSource);
        private readonly record struct InventoryLayout(Rectangle Panel, Point SlotStart);

        public HudRenderer(GraphicsDevice graphicsDevice, Texture2D toolbarTexture, Texture2D lifeBarTexture, SpriteFont font, IReadOnlyDictionary<ItemId, Texture2D> itemTextures)
        {
            pixel = new Texture2D(graphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
            this.toolbarTexture = toolbarTexture;
            this.lifeBarTexture = lifeBarTexture;
            this.font = font;
            this.itemTextures = itemTextures;
        }

        public SpriteFont Font => font;

        public void Draw(SpriteBatch spriteBatch, Hotbar hotbar, int selectedHotbarIndex, int currentHealth, int maxHealth, int screenWidth, int screenHeight, string clockText, float zoom = 1f)
        {
            DrawHotbar(spriteBatch, hotbar, selectedHotbarIndex, screenWidth, screenHeight);
            DrawPlayerHealth(spriteBatch, currentHealth, maxHealth, zoom);
            DrawWorldClock(spriteBatch, clockText, screenWidth);
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
                Rectangle bounds = GetHotbarSlotBounds(i, hotbar.Capacity, screenWidth, screenHeight);
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
            HotbarLayout layout = GetHotbarLayout(hotbar.Capacity, screenWidth, screenHeight);
            spriteBatch.Draw(toolbarTexture, layout.Bounds, layout.Source, Color.White);

            for (int i = 0; i < hotbar.Capacity; i++)
            {
                Rectangle slotBounds = GetHotbarSlotBounds(i, hotbar.Capacity, screenWidth, screenHeight);

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
            spriteBatch.Draw(itemTexture, ItemIconLayout.FitInside(definition, destination), definition.SourceRectangle, Color.White);
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

        private Rectangle GetHotbarSlotBounds(int slotIndex, int hotbarCapacity, int screenWidth, int screenHeight)
        {
            Rectangle hotbarBounds = GetHotbarLayout(hotbarCapacity, screenWidth, screenHeight).Bounds;
            return new Rectangle(
                hotbarBounds.X + slotIndex * HotbarSlotStep * HotbarScale,
                hotbarBounds.Y,
                HotbarSlotSize * HotbarScale,
                HotbarSlotSize * HotbarScale);
        }

        private HotbarLayout GetHotbarLayout(int hotbarCapacity, int screenWidth, int screenHeight)
        {
            int sourceWidth = toolbarTexture.Width;
            int sourceHeight = HotbarSlotSize;
            int minimumSourceWidth = HotbarSlotSize + Math.Max(0, hotbarCapacity - 1) * HotbarSlotStep;
            if (sourceWidth < minimumSourceWidth)
                sourceWidth = minimumSourceWidth;

            int width = sourceWidth * HotbarScale;
            int height = sourceHeight * HotbarScale;
            Rectangle bounds = new Rectangle(
                (screenWidth - width) / 2,
                screenHeight - height - HotbarPadding,
                width,
                height);

            return new HotbarLayout(
                bounds,
                new Rectangle(0, 0, sourceWidth, sourceHeight),
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

        private void DrawPlayerHealth(SpriteBatch spriteBatch, int currentHealth, int maxHealth, float zoom)
        {
            if (lifeBarTexture == null)
            {
                DrawPlayerHealthFallback(spriteBatch, currentHealth, maxHealth);
                return;
            }

            int totalChunks = Math.Max(1, (maxHealth + LifeBarHealthPerChunk - 1) / LifeBarHealthPerChunk);
            int x = LifeBarPadding;
            int y = LifeBarPadding;

            for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
            {
                LifeBarChunkKind kind = chunkIndex == 0
                    ? LifeBarChunkKind.Start
                    : chunkIndex == totalChunks - 1 ? LifeBarChunkKind.End : LifeBarChunkKind.Middle;

                int chunkWidth = kind == LifeBarChunkKind.Middle ? LifeBarMiddleWidth : LifeBarStartEndWidth;
                int chunkStartHealth = chunkIndex * LifeBarHealthPerChunk;
                int chunkCapacity = Math.Min(LifeBarHealthPerChunk, maxHealth - chunkStartHealth);
                int chunkHealth = Math.Clamp(currentHealth - chunkStartHealth, 0, chunkCapacity);
                int filledSteps = chunkCapacity <= 0
                    ? 0
                    : Math.Clamp((int)MathF.Round((chunkHealth / (float)chunkCapacity) * LifeBarStepsPerChunk), 0, LifeBarStepsPerChunk);

                Rectangle destRect = new Rectangle(x, y, (int)(chunkWidth * zoom), (int)(LifeBarChunkHeight * zoom));

                if (filledSteps >= LifeBarStepsPerChunk)
                {
                    DrawLifeBarChunk(spriteBatch, kind, destRect, filled: true);
                }
                else
                {
                    DrawLifeBarChunk(spriteBatch, kind, destRect, filled: false);
                    if (filledSteps > 0)
                        DrawLifeBarPartialFill(spriteBatch, kind, destRect, filledSteps, zoom);
                }

                x += destRect.Width;
            }

            DrawLifeBarNumber(spriteBatch, new Rectangle(x, y, 0, (int)(LifeBarChunkHeight * zoom)), currentHealth);
        }

        private void DrawLifeBarChunk(SpriteBatch spriteBatch, LifeBarChunkKind kind, Rectangle destRect, bool filled)
        {
            int sheetX = kind switch
            {
                LifeBarChunkKind.Start => 0,
                LifeBarChunkKind.Middle => 6,
                _ => 11
            };
            int chunkWidth = kind == LifeBarChunkKind.Middle ? LifeBarMiddleWidth : LifeBarStartEndWidth;
            int sheetY = filled ? 0 : LifeBarChunkHeight + LifeBarRowGap;

            Rectangle sourceRect = new Rectangle(sheetX, sheetY, chunkWidth, LifeBarChunkHeight);
            spriteBatch.Draw(lifeBarTexture, destRect, sourceRect, Color.White);
        }

        // Stamps 1-3 fill-color columns onto an already-drawn empty chunk, so a chunk mid-way
        // through its 10 HP shows partial progress. Never touches the divider column - that only
        // lights up at step 4 (fully filled), via DrawLifeBarChunk's pre-baked filled sprite.
        private void DrawLifeBarPartialFill(SpriteBatch spriteBatch, LifeBarChunkKind kind, Rectangle destRect, int filledSteps, float zoom)
        {
            int fillLocalX = kind == LifeBarChunkKind.Start ? 1 : 0;
            int stripSourceY = LifeBarChunkHeight + LifeBarRowGap + 1;
            int stripDestHeight = (int)(LifeBarStripHeight * zoom);
            int stripDestY = destRect.Y + (int)zoom;
            int stripDestWidth = Math.Max(1, (int)MathF.Ceiling(zoom));

            Rectangle fillSource = new Rectangle(LifeBarStripSourceX, stripSourceY, 1, LifeBarStripHeight);
            for (int i = 0; i < filledSteps; i++)
            {
                Rectangle fillDest = new Rectangle(destRect.X + (int)((fillLocalX + i) * zoom), stripDestY, stripDestWidth, stripDestHeight);
                spriteBatch.Draw(lifeBarTexture, fillDest, fillSource, Color.White);
            }
        }

        // Drawn just to the right of the whole bar - plenty of room here (unlike the tiny 4-5px
        // chunks), so no need for the tiny-scale/LinearClamp workaround the single-bar design needed.
        private void DrawLifeBarNumber(SpriteBatch spriteBatch, Rectangle barEnd, int currentHealth)
        {
            string text = currentHealth.ToString();
            Vector2 textSize = font.MeasureString(text);
            Vector2 textPos = new Vector2(
                barEnd.X + 8f,
                barEnd.Y + (barEnd.Height - textSize.Y) * 0.5f);

            spriteBatch.DrawString(font, text, textPos + new Vector2(1f, 1f), Color.Black);
            spriteBatch.DrawString(font, text, textPos, Color.White);
        }

        private void DrawPlayerHealthFallback(SpriteBatch spriteBatch, int currentHealth, int maxHealth)
        {
            const int width = 120;
            const int height = 14;

            float ratio = maxHealth <= 0 ? 0f : MathHelper.Clamp((float)currentHealth / maxHealth, 0f, 1f);
            int fill = (int)(width * ratio);
            int x = LifeBarPadding;
            int y = LifeBarPadding;

            spriteBatch.Draw(pixel, new Rectangle(x - 2, y - 2, width + 4, height + 4), Color.Black * 0.85f);
            spriteBatch.Draw(pixel, new Rectangle(x, y, width, height), new Color(52, 52, 52));
            if (fill > 0)
                spriteBatch.Draw(pixel, new Rectangle(x, y, fill, height), new Color(196, 44, 56));

            string label = $"{currentHealth}/{maxHealth}";
            Vector2 size = font.MeasureString(label);
            Vector2 textPos = new Vector2(x + (width - size.X) * 0.5f, y + height + 2f);
            spriteBatch.DrawString(font, label, textPos + new Vector2(1f, 1f), Color.Black);
            spriteBatch.DrawString(font, label, textPos, Color.White);
        }

        private void DrawWorldClock(SpriteBatch spriteBatch, string clockText, int screenWidth)
        {
            if (string.IsNullOrWhiteSpace(clockText))
                return;

            const int padding = 14;
            const int clockPaddingX = 7;
            const int clockPaddingY = 3;

            Vector2 textSize = font.MeasureString(clockText);
            int width = (int)MathF.Ceiling(textSize.X) + (clockPaddingX * 2);
            int height = (int)MathF.Ceiling(textSize.Y) + (clockPaddingY * 2);
            int x = screenWidth - width - padding;
            int y = padding;
            Rectangle bounds = new Rectangle(x, y, width, height);

            spriteBatch.Draw(pixel, new Rectangle(bounds.X - 1, bounds.Y - 1, bounds.Width + 2, bounds.Height + 2), Color.Black * 0.82f);
            spriteBatch.Draw(pixel, bounds, new Color(13, 24, 34, 220));

            Vector2 textPos = new Vector2(bounds.X + clockPaddingX, bounds.Y + clockPaddingY);
            spriteBatch.DrawString(font, clockText, textPos + new Vector2(1f, 1f), Color.Black * 0.85f);
            spriteBatch.DrawString(font, clockText, textPos, new Color(220, 240, 255));
        }
    }
}
