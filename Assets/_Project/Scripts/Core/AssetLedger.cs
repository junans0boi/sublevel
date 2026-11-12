using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Game.Core
{
    // Checks asset folders against docs/ASSET_LEDGER.md (ADR-027).
    public static class AssetLedger
    {
        static readonly Regex PathPattern = new Regex(@"Assets/_Project/[^\s|`]+", RegexOptions.Compiled);

        // Project paths recorded in the ledger. Example rows ("(예시)") are ignored.
        public static List<string> ListedPaths(string ledgerMarkdown)
        {
            var paths = new List<string>();
            foreach (var line in ledgerMarkdown.Split('\n'))
            {
                var row = line.TrimStart();
                if (!row.StartsWith("|") || row.TrimStart('|', ' ').StartsWith("(예시)")) continue;
                foreach (Match m in PathPattern.Matches(line))
                {
                    var path = m.Value.TrimEnd('.', '/');
                    // A row must name at least a category folder (Assets/_Project/Art/<category>); broader rows cover nothing.
                    if (path.Split('/').Length >= 4) paths.Add(path);
                }
            }
            return paths;
        }

        // Asset folders that no ledger row covers. A row covers a folder when it names the folder or one of its parents.
        public static List<string> FindUnlisted(string ledgerMarkdown, IEnumerable<string> assetFolders)
        {
            var listed = ListedPaths(ledgerMarkdown);
            var missing = new List<string>();
            foreach (var raw in assetFolders)
            {
                var folder = raw.Replace('\\', '/').TrimEnd('/');
                bool covered = false;
                foreach (var p in listed)
                {
                    if (folder.Equals(p, StringComparison.Ordinal) || folder.StartsWith(p + "/", StringComparison.Ordinal))
                    {
                        covered = true;
                        break;
                    }
                }
                if (!covered) missing.Add(folder);
            }
            return missing;
        }
    }
}
