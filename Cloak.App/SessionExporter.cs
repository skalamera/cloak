using System;
using System.IO;
using System.Text;
using System.Windows.Controls;

namespace Cloak.App
{
    internal static class SessionExporter
    {
        public static void ExportToMarkdown(ItemsControl transcriptItems, ItemsControl suggestionItems)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Session Transcript");
            sb.AppendLine();
            foreach (var item in transcriptItems.Items)
            {
                sb.AppendLine("- " + item?.ToString());
            }
            sb.AppendLine();
            sb.AppendLine("# Suggestions");
            sb.AppendLine();
            foreach (var item in suggestionItems.Items)
            {
                sb.AppendLine("- " + item?.ToString());
            }

            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"cloak-session-{DateTime.Now:yyyyMMdd-HHmmss}.md");
            File.WriteAllText(file, sb.ToString());
        }
    }
}


