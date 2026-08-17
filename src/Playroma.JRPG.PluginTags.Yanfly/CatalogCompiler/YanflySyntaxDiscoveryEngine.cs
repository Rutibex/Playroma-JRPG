using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed class YanflySyntaxDiscoveryEngine
{
    private static readonly Regex Angle = new(@"<[^>\r\n]{1,240}>", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex Escape = new(@"\\(?:[A-Za-z]{1,12}(?:\[[^\]\r\n]{1,80}\]|<[^>\r\n]{1,80}>)?|[{}$!.|<>^\\])", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly Regex Script = new(@"^(?:\$[A-Za-z_][A-Za-z0-9_]*|[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+)\s*(?:\.|\[|\()", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    public IReadOnlyList<YanflySyntaxDiscovery> Discover(YanflyCanonicalPluginSource source, YanflyHelpDocument document)
    {
        List<YanflySyntaxDiscovery> items = [];
        for (int i = 0; i < document.Lines.Count; i++)
        {
            string line = document.Lines[i];
            string trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            YanflyHelpSection? section = document.Sections.LastOrDefault(s => i + 1 >= s.StartLine && i + 1 <= s.EndLine);
            string heading = section?.Heading ?? "Help";
            IReadOnlyList<PluginDocumentKind> kinds = YanflyHelpDocumentParser.InferDocumentKinds(document.Lines, i, section, source.InventoryEntry);
            ExtractAngles(source, i, line, trimmed, section, heading, kinds, items);
            if (section?.Surface == YanflyAuthoringSurface.MessageTextCode)
                foreach (Match m in Escape.Matches(line)) items.Add(Create(source.PluginId, m.Value, YanflyDiscoveryKind.TextCode, YanflyDiscoveryDisposition.ContextAssistant, PluginAuthoringSurface.MessageTextCode, kinds.Count == 0 ? [PluginDocumentKind.Message] : kinds, heading, i + 1, true, "Text code."));
            if (section?.Surface == YanflyAuthoringSurface.PluginCommand && IsCommand(document.Lines, i, trimmed))
                items.Add(Create(source.PluginId, trimmed, YanflyDiscoveryKind.PluginCommand, YanflyDiscoveryDisposition.ContextAssistant, PluginAuthoringSurface.PluginCommand, [PluginDocumentKind.EventPage], heading, i + 1, true, "Plugin command example."));
            if (section?.Surface == YanflyAuthoringSurface.ActionSequence)
            {
                string value = trimmed.StartsWith("Usage Example:", StringComparison.OrdinalIgnoreCase) ? trimmed[14..].Trim() : trimmed;
                if (value.Length > 0 && !value.StartsWith('<') && IsAction(document.Lines, i, trimmed))
                    items.Add(Create(source.PluginId, value, YanflyDiscoveryKind.ActionSequence, YanflyDiscoveryDisposition.ContextAssistant, PluginAuthoringSurface.ActionSequence, [PluginDocumentKind.Skill, PluginDocumentKind.Item], heading, i + 1, true, "Action sequence command."));
            }
            if ((section?.Surface is YanflyAuthoringSurface.ScriptCall or YanflyAuthoringSurface.MoveRouteScript) && Script.IsMatch(trimmed) && trimmed.Length <= 240)
            {
                bool move = section.Surface == YanflyAuthoringSurface.MoveRouteScript;
                items.Add(Create(source.PluginId, trimmed, move ? YanflyDiscoveryKind.MoveRouteScript : YanflyDiscoveryKind.ScriptCall, YanflyDiscoveryDisposition.ContextAssistant, move ? PluginAuthoringSurface.MoveRouteScript : PluginAuthoringSurface.ScriptCall, kinds, heading, i + 1, true, "Script helper."));
            }
        }
        foreach (YanflyPluginParameterDefinition parameter in document.Parameters)
            items.Add(Create(source.PluginId, parameter.Name, YanflyDiscoveryKind.PluginParameter, YanflyDiscoveryDisposition.PluginConfiguration, PluginAuthoringSurface.PluginParameter, [PluginDocumentKind.PluginManifest], "Plugin Parameters", parameter.HeaderLine, true, "Plugin parameter."));
        Pair(items);
        Deduplicate(items);
        return items.OrderBy(x => x.HelpLine).ThenBy(x => x.Syntax, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void ExtractAngles(YanflyCanonicalPluginSource source, int lineIndex, string line, string trimmed, YanflyHelpSection? section, string heading, IReadOnlyList<PluginDocumentKind> kinds, ICollection<YanflySyntaxDiscovery> target)
    {
        foreach (Match m in Angle.Matches(line))
        {
            string syntax = m.Value.Trim();
            bool starts = trimmed.StartsWith(syntax, StringComparison.OrdinalIgnoreCase);
            bool closing = syntax.StartsWith("</", StringComparison.Ordinal);
            YanflyDiscoveryKind kind;
            YanflyDiscoveryDisposition disposition;
            PluginAuthoringSurface? surface;
            string reason;
            if (heading.Contains("changelog", StringComparison.OrdinalIgnoreCase)) { kind = YanflyDiscoveryKind.ChangelogReference; disposition = YanflyDiscoveryDisposition.DocumentationOnly; surface = null; reason = "Changelog reference."; }
            else if (!starts && LooksLikeCode(line, m.Index)) { kind = YanflyDiscoveryKind.CodeComparison; disposition = YanflyDiscoveryDisposition.DocumentationOnly; surface = null; reason = "JavaScript comparison."; }
            else if (section?.Surface == YanflyAuthoringSurface.MessageTextCode) { kind = YanflyDiscoveryKind.TextCode; disposition = YanflyDiscoveryDisposition.ContextAssistant; surface = PluginAuthoringSurface.MessageTextCode; reason = "Text code."; }
            else if (section?.Surface == YanflyAuthoringSurface.EventPageComment) { kind = closing ? YanflyDiscoveryKind.BlockClosing : YanflyDiscoveryKind.CommentTag; disposition = YanflyDiscoveryDisposition.TypedFeature; surface = PluginAuthoringSurface.EventPageComment; reason = "Comment tag."; }
            else if (section?.Surface == YanflyAuthoringSurface.ActionSequence) { kind = closing ? YanflyDiscoveryKind.BlockClosing : YanflyDiscoveryKind.ActionSequence; disposition = YanflyDiscoveryDisposition.ContextAssistant; surface = PluginAuthoringSurface.ActionSequence; reason = "Action sequence tag."; }
            else if (IsNotes(section, heading, source.InventoryEntry) && (starts || Compact(trimmed))) { kind = closing ? YanflyDiscoveryKind.BlockClosing : YanflyDiscoveryKind.Notetag; disposition = heading.Contains("lunatic", StringComparison.OrdinalIgnoreCase) ? YanflyDiscoveryDisposition.AdvancedRawFeature : YanflyDiscoveryDisposition.TypedFeature; surface = ResolveNoteSurface(section, kinds); reason = disposition == YanflyDiscoveryDisposition.AdvancedRawFeature ? "Lunatic Mode notetag." : "Notetag."; }
            else { kind = YanflyDiscoveryKind.DocumentationMarkup; disposition = YanflyDiscoveryDisposition.DocumentationOnly; surface = null; reason = "Help markup."; }
            target.Add(Create(source.PluginId, syntax, kind, disposition, surface, kinds, heading, lineIndex + 1, starts, reason));
        }
    }

    private static bool IsCommand(IReadOnlyList<string> lines, int i, string v) => v.Length is >= 3 and <= 180 && !v.StartsWith('-') && !v.StartsWith('<') && !v.EndsWith('.') && !v.Equals("Plugin Command", StringComparison.OrdinalIgnoreCase) && (i + 1 < lines.Count && lines[i + 1].TrimStart().StartsWith('-') || v.Split(' ')[0].Any(char.IsUpper));
    private static bool IsAction(IReadOnlyList<string> lines, int i, string v) => v.StartsWith("Usage Example:", StringComparison.OrdinalIgnoreCase) || (v.Any(char.IsLetter) && v.Where(char.IsLetter).Count(char.IsUpper) * 3 >= v.Where(char.IsLetter).Count() * 2 && (v.Contains(':') || i + 1 < lines.Count && lines[i + 1].Trim().All(c => c == '-')));
    private static bool LooksLikeCode(string line, int at) { string before = line[..at]; return before.Contains("if (") || before.Contains("while (") || before.Contains("for (") || before.Contains('=') || before.Contains("return "); }
    private static bool Compact(string line) { if (!Angle.IsMatch(line)) return false; string rest = Angle.Replace(line, string.Empty).Trim(); return rest.Length == 0 || rest.StartsWith("or", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("Example", StringComparison.OrdinalIgnoreCase); }
    private static bool IsNotes(YanflyHelpSection? s, string h, YanflyPluginInventoryEntry p) => s?.Surface is YanflyAuthoringSurface.DatabaseNote or YanflyAuthoringSurface.MapNote or YanflyAuthoringSurface.TilesetNote or YanflyAuthoringSurface.EventNote || h.Contains("notetag", StringComparison.OrdinalIgnoreCase) || h.Contains("note tag", StringComparison.OrdinalIgnoreCase) || h.Contains("lunatic", StringComparison.OrdinalIgnoreCase) || p.AuthoringSurfaces.Any(x => x is YanflyAuthoringSurface.DatabaseNote or YanflyAuthoringSurface.MapNote or YanflyAuthoringSurface.TilesetNote or YanflyAuthoringSurface.EventNote);
    private static PluginAuthoringSurface ResolveNoteSurface(YanflyHelpSection? s, IReadOnlyList<PluginDocumentKind> k) { if (s?.Surface == YanflyAuthoringSurface.MapNote || k.Contains(PluginDocumentKind.Map)) return PluginAuthoringSurface.MapNote; if (s?.Surface == YanflyAuthoringSurface.TilesetNote || k.Contains(PluginDocumentKind.Tileset)) return PluginAuthoringSurface.TilesetNote; if (s?.Surface == YanflyAuthoringSurface.EventNote || k.Contains(PluginDocumentKind.Event)) return PluginAuthoringSurface.EventNote; return PluginAuthoringSurface.DatabaseNote; }

    private static YanflySyntaxDiscovery Create(string plugin, string syntax, YanflyDiscoveryKind kind, YanflyDiscoveryDisposition disposition, PluginAuthoringSurface? surface, IReadOnlyList<PluginDocumentKind> kinds, string heading, int line, bool canonical, string reason)
    {
        string normalized = NormalizeSyntax(syntax);
        string material = string.Join("\u001f", plugin, kind, normalized, line);
        string id = "yep-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant()[..16];
        return new(id, plugin, syntax, normalized, kind, disposition, surface, kinds.Distinct().OrderBy(x => x).ToArray(), heading, line, null, canonical, reason);
    }
    public static string NormalizeSyntax(string syntax) => Regex.Replace(syntax.Trim(), @"\s+", " ", RegexOptions.CultureInvariant).ToLowerInvariant();
    private static void Pair(IList<YanflySyntaxDiscovery> items)
    {
        Dictionary<string, Queue<int>> opens = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < items.Count; i++)
        {
            YanflySyntaxDiscovery d = items[i]; string key = BlockKey(d.PluginId, d.Syntax);
            if (d.Kind == YanflyDiscoveryKind.BlockClosing)
            {
                if (opens.TryGetValue(key, out Queue<int>? q) && q.Count > 0) { int oi = q.Dequeue(); YanflySyntaxDiscovery o = items[oi]; items[oi] = o with { Kind = YanflyDiscoveryKind.BlockOpening, PairedDiscoveryId = d.DiscoveryId, Disposition = o.Disposition == YanflyDiscoveryDisposition.TypedFeature ? YanflyDiscoveryDisposition.AdvancedRawFeature : o.Disposition }; items[i] = d with { PairedDiscoveryId = o.DiscoveryId }; }
            }
            else if (d.Kind is YanflyDiscoveryKind.Notetag or YanflyDiscoveryKind.CommentTag) { if (!opens.TryGetValue(key, out Queue<int>? q)) opens[key] = q = new(); q.Enqueue(i); }
        }
    }
    private static string BlockKey(string plugin, string syntax) { string inner = syntax.Trim().TrimStart('<').TrimEnd('>').Trim(); if (inner.StartsWith('/')) inner = inner[1..].TrimStart(); return plugin + "\u001f" + NormalizeSyntax(inner); }
    private static void Deduplicate(IList<YanflySyntaxDiscovery> items) { HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase); for (int i = 0; i < items.Count; i++) { YanflySyntaxDiscovery d = items[i]; string key = d.PluginId + "\u001f" + d.Surface + "\u001f" + d.NormalizedSyntax; if (!seen.Add(key)) items[i] = d with { Disposition = YanflyDiscoveryDisposition.Duplicate, IsCanonicalCandidate = false, ClassificationReason = "Duplicate example." }; } }
}
