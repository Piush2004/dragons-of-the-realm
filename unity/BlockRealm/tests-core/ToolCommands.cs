using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BlockRealm;

namespace BlockRealm.Tests
{
    /// <summary>Command-line helpers that export data from the core for the Blender art scripts.</summary>
    public static class ToolCommands
    {
        private static string Q(string s) { return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }

        /// <summary>Writes every block tile name, item icon name and block summary as JSON, in texture-array order.</summary>
        public static int WriteTileList(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"tiles\": [");
            for (int i = 0; i < Blocks.TileNames.Count; i++) sb.Append(i == 0 ? "" : ", ").Append(Q(Blocks.TileNames[i]));
            sb.Append("],\n");
            sb.Append("  \"items\": [");
            for (int i = 0; i < Items.IconNames.Count; i++) sb.Append(i == 0 ? "" : ", ").Append(Q(Items.IconNames[i]));
            sb.Append("],\n");
            sb.Append("  \"blocks\": [\n");
            bool first = true;
            for (int i = 1; i < B.Count; i++)
            {
                BlockInfo b = Blocks.All[i];
                if (b == null) continue;
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append("    {\"id\": ").Append(i)
                  .Append(", \"name\": ").Append(Q(b.Name))
                  .Append(", \"render\": ").Append(Q(b.Render.ToString()))
                  .Append(", \"top\": ").Append(Q(b.TileTop))
                  .Append(", \"side\": ").Append(Q(b.TileSide))
                  .Append(", \"bottom\": ").Append(Q(b.TileBottom)).Append("}");
            }
            sb.Append("\n  ]\n}\n");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine("wrote " + path);
            return 0;
        }
    }
}
