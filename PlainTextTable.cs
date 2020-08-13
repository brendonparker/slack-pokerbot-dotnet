using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace slack_pokerbot_dotnet
{
    class PlainTextTable
    {
        static int COLUMN_SPACING = 2;
        public static string ToTable<T>(IEnumerable<T> data)
        {
            if (data.Any())
            {
                var first = data.First();
                var props = first.GetType().GetProperties();

                var tableColumnWidths = props.Select(prop =>
                {
                    var propValueMaxLength = data
                    .Select(x => prop.GetValue(x)?.ToString() ?? string.Empty)
                    .Max(x => x.Length);
                    return Math.Max(propValueMaxLength, prop.Name.Length) + COLUMN_SPACING;
                }).ToArray();


                var sb = new StringBuilder();
                for (var i = 0; i < props.Length; i++)
                {
                    var columnWidth = tableColumnWidths[i];
                    var prop = props[i];
                    sb.Append(prop.Name.PadRight(tableColumnWidths[i]));
                }
                sb.AppendLine();

                foreach (var columnWidth in tableColumnWidths)
                {
                    sb.Append("".PadLeft(columnWidth - 2, '-') + "".PadLeft(COLUMN_SPACING));
                }
                sb.AppendLine();

                foreach (var row in data)
                {
                    for (var i = 0; i < props.Length; i++)
                    {
                        var columnWidth = tableColumnWidths[i];
                        var prop = props[i];

                        sb.Append((prop.GetValue(row)?.ToString() ?? string.Empty).PadRight(columnWidth));
                    }
                    sb.AppendLine();
                }

                return $"```\r{sb}```";
            }
            else
            {
                return "No history :sad:";
            }
        }
    }
}
