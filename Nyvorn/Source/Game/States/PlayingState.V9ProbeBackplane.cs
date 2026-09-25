using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Nyvorn.Source.Engine.Graphics.LightingV9Lab;
using Nyvorn.Source.Engine.Graphics.LightingV9Probe;
using Nyvorn.Source.World;

namespace Nyvorn.Source.Game.States;

public partial class PlayingState
{
    private (Vector3[] Energy, Rectangle Bounds) v9BackplanePrevious, v9BackplaneOffsetOpen, v9BackplaneEntranceOpen;

    private object CheckV9Backplane(string name)
    {
        var map = session.WorldMap;
        int exposed = 0, wrongDirect = 0, wrongClass = 0, deepLit = 0, fgGlow = 0, fgGlowNotAdjacent = 0;
        float glowPeak = 0;
        for (int y = 0, i = 0; y < v9Field.Height; y++)
            for (int x = 0; x < v9Field.Width; x++, i++)
            {
                int wx = v9Field.Origin.X + x, wy = v9Field.Origin.Y + y;
                int cx = V9ProbeSky.FloorDiv(wx, 8), cy = V9ProbeSky.FloorDiv(wy, 8);
                bool solid = V9ProbeOccupancy.WorldSolid(map, cx, cy);
                bool empty = !solid && map.GetBackgroundTile(cx, cy) == TileType.Empty;
                float weight = v9Field.BackplaneDepth((wy + .5f) / 8);
                if ((v9Field.SkyVisibleAt(cx, cy) > 0) != (empty && v9Field.BackplaneDepth(cy + .5f) > 0)) wrongClass++;
                if (empty && weight > 0)
                {
                    exposed++;
                    if (v9Field.NaturalEnergy[i] != V9LabSettings.SkyLinearRgb * (V9ProbeField.OpenSky * weight)) wrongDirect++;
                }
                if (weight == 0 && v9Field.NaturalEnergy[i] != Vector3.Zero) deepLit++;
                if (v9Field.BackplaneForegroundGlow[i] > 0)
                {
                    fgGlow++;
                    glowPeak = MathF.Max(glowPeak, v9Field.BackplaneForegroundGlow[i]);
                    if (V9Neighbours(new Point(cx, cy)).All(p => v9Field.SkyVisibleAt(p.X, p.Y) == 0)) fgGlowNotAdjacent++;
                }
            }
        V9Check($"FG/BG exposure: {exposed} directly exposed texels; classification errors={wrongClass}, direct errors={wrongDirect}, Cavern/Deep lit={deepLit}",
            wrongClass == 0 && wrongDirect == 0 && deepLit == 0);
        V9Check($"terminal response: {v9ForegroundSky.Affected} texels, max depth={v9ForegroundSky.MaxDepth}; glow-fed={fgGlow}, without adjacent sky={fgGlowNotAdjacent}",
            v9ForegroundSky.MaxDepth <= V9LabSettings.SurfaceDepthPixels);
        CheckV9GlowInvariants();
        bool monotone = true;
        float previous = 1;
        for (float row = v9Field.SurfaceStartRow - 1; row <= v9Field.ShallowLayer.EndY + 2; row += .125f)
        {
            float w = v9Field.BackplaneDepth(row);
            monotone &= w >= 0 && w <= previous && previous - w < .002f;
            previous = w;
        }
        V9Check("single continuous depth curve (1/8-tile steps), strong above Surface, zero from Cavern onward",
            monotone && v9Field.BackplaneDepth(v9Field.SurfaceStartRow) == 1 && previous == 0);

        int Compare((Vector3[] Energy, Rectangle Bounds) a, bool relabel = false)
        {
            if (a.Energy == null) return -1;
            var bounds = a.Bounds;
            if (relabel) bounds.Offset(map.PixelWidth, 0);
            Rectangle r = Rectangle.Intersect(bounds, v9Field.Bounds);
            if (r.IsEmpty) return -1;
            int differs = 0;
            for (int y = r.Top; y < r.Bottom; y++)
                for (int x = r.Left; x < r.Right; x++)
                    if (a.Energy[(y - bounds.Y) * bounds.Width + x - bounds.X] != v9Field.Energy[(y - v9Field.Origin.Y) * v9Field.Width + x - v9Field.Origin.X]) differs++;
            return differs;
        }
        var snapshot = ((Vector3[])v9Field.Energy.Clone(), v9Field.Bounds);
        if (name == "entrance-inside") v9BackplaneEntranceOpen = snapshot;
        if (name == "entrance-reopened") V9Check("foreground reopening restores the field bit for bit", Compare(v9BackplaneEntranceOpen) == 0);
        if (name == "wrap-seam-shifted") V9Check("wrap relabel keeps the field bit for bit", Compare(v9BackplanePrevious, true) == 0);
        if (name is "entrance-recentre-b" or "entrance-recentre-c") V9Check("camera recenter keeps all overlapping world texels identical", Compare(v9BackplanePrevious) == 0);
        v9BackplanePrevious = snapshot;

        int roomLit = 0, leftWallGlow = 0, behindPillar = 0;
        if (name.StartsWith("shallow-room"))
        {
            foreach (Point c in V9RoomInterior())
                for (int dy = 0; dy < 8; dy++)
                    for (int dx = 0; dx < 8; dx++)
                        if (V9NaturalAt(new Vector2(c.X * 8 + dx, c.Y * 8 + dy)) is Vector3 e && e != Vector3.Zero)
                        {
                            roomLit++;
                            if (c.X > v9RoomOrigin.X + 5) behindPillar++;
                        }
            for (int y = (v9RoomOrigin.Y + 3) * 8; y < (v9RoomOrigin.Y + 9) * 8; y++)
                for (int x = v9RoomOrigin.X * 8; x < (v9RoomOrigin.X + 1) * 8; x++)
                {
                    int i = (y - v9Field.Origin.Y) * v9Field.Width + x - v9Field.Origin.X;
                    if (v9Field.BackplaneForegroundGlow[i] > 0) leftWallGlow++;
                }
            if (name is "shallow-room-closed" or "shallow-room-torch-5" or "shallow-room-offset-closed")
                V9Check($"full BG seals the room: {roomLit} natural-lit interior texels", roomLit == 0);
            if (name == "shallow-room-offset-open")
            {
                v9BackplaneOffsetOpen = snapshot;
                bool responseEnabled = v9Field.GlowSettings.Enabled && v9Field.ForegroundSky;
                V9Check($"opening two BG tiles from FG: glow response enabled={responseEnabled}, {leftWallGlow} wall texels",
                    responseEnabled ? leftWallGlow > 0 : leftWallGlow == 0);
            }
            if (name == "shallow-room-offset-blocked") V9Check($"full-height FG pillar stops BG transport: {behindPillar} lit interior texels behind it", behindPillar == 0);
            if (name == "shallow-room-offset-reopened") V9Check("BG reopening restores the field bit for bit", Compare(v9BackplaneOffsetOpen) == 0);
        }
        return new { exposed, wrongDirect, wrongClass, deepLit, fgGlow, fgGlowNotAdjacent, glowPeak, roomLit, leftWallGlow, behindPillar,
            surfaceWeight = v9Field.BackplaneDepth(v9Field.SurfaceStartRow),
            shallowStartWeight = v9Field.BackplaneDepth(v9Field.ShallowLayer.StartY), shallowEndWeight = v9Field.BackplaneDepth(v9Field.ShallowLayer.EndY + .5f) };
    }
}
