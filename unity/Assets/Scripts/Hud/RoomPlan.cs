#nullable enable
using System.Linq;
using LabEscape.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace LabEscape.Client
{
    /// <summary>
    /// A plan of a room for the briefing: its tiles from above, coloured by what they are, with the lab's devices outlined and
    /// the player's start marked. Built from the room's data, so any size or shape draws. It shows what the board shows, and no
    /// more: not where the waves come in.
    /// </summary>
    public static class RoomPlan
    {
        public const int TilePx = 34;

        /// <summary>The plan's colours: open floor, a wall that blocks sight, a barrier that blocks movement but not sight.</summary>
        public static readonly Color Floor = Palette.Hex(0x2c353a);
        public static readonly Color Wall = Palette.Hex(0x59656c);
        public static readonly Color Barrier = Palette.Hex(0x8a6a2a);

        /// <summary>What a tile looks like on the plan: "floor", "wall" or "barrier".</summary>
        public static string Kind(RoomDef room, Pos tile)
        {
            if (!room.Board.BlocksMove(tile)) return "floor";
            return room.Board.BlocksSight(tile) ? "wall" : "barrier";
        }

        public static VisualElement Build(Content c, RoomDef room)
        {
            var plan = Ui.El("plan-grid");
            plan.style.width = room.Width * TilePx;
            plan.style.height = room.Height * TilePx;
            var devices = room.Hazards.SelectMany(h => h.Tiles).Select(Tiles.Parse).ToHashSet();
            var start = Tiles.IsName(room.PlayerStart) ? Tiles.Parse(room.PlayerStart) : (Pos?)null;
            for (var y = 0; y < room.Height; y++)
            {
                for (var x = 0; x < room.Width; x++)
                {
                    var tile = new Pos(x, y);
                    var kind = Kind(room, tile);
                    var cell = Ui.El($"plan-tile {kind}");
                    cell.name = "plan-" + Tiles.Name(tile);
                    cell.style.left = x * TilePx;
                    cell.style.top = y * TilePx;
                    cell.style.width = TilePx - 2;
                    cell.style.height = TilePx - 2;
                    cell.style.backgroundColor = kind == "floor" ? Floor : kind == "wall" ? Wall : Barrier;
                    if (devices.Contains(tile)) cell.AddToClassList("device");
                    if (start.HasValue && start.Value == tile)
                    {
                        cell.AddToClassList("start");
                        cell.Add(Ui.Text("@", "plan-mark mono"));
                    }
                    plan.Add(cell);
                }
            }
            // The edges are lettered and numbered as the log names tiles.
            for (var x = 0; x < room.Width; x++) plan.Add(Edge(((char)('A' + x)).ToString(), x * TilePx, room.Height * TilePx));
            for (var y = 0; y < room.Height; y++) plan.Add(Edge((y + 1).ToString(), -TilePx / 2, y * TilePx));
            return plan;
        }

        private static VisualElement Edge(string text, float left, float top)
        {
            var label = Ui.Text(text, "plan-edge mono");
            label.style.left = left;
            label.style.top = top;
            label.style.width = TilePx;
            return label;
        }
    }
}
