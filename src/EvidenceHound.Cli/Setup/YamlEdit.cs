using System.Text.RegularExpressions;
using EvidenceHound.Core.Results;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace EvidenceHound.Cli.Setup;

/// <summary>A value to write into a YAML file.</summary>
internal abstract record YamlValue;

/// <summary>One scalar, quoted only when YAML would read it as something else.</summary>
internal sealed record YamlText(string Value) : YamlValue;

/// <summary>A short list of scalars, written on one line: <c>[a, b]</c>.</summary>
internal sealed record YamlFlowList(IReadOnlyList<string> Items) : YamlValue;

/// <summary>A map, in the order given.</summary>
internal sealed record YamlMap(IReadOnlyList<KeyValuePair<string, YamlValue>> Entries) : YamlValue
{
    /// <summary>The map without its empty entries: blank text and empty lists are left out, as a hand-written file would.</summary>
    public static YamlMap Of(params (string Key, YamlValue? Value)[] entries) =>
        new([.. entries.Where(e => e.Value switch
            {
                null => false,
                YamlText text => text.Value.Length > 0,
                YamlFlowList list => list.Items.Count > 0,
                YamlMap map => map.Entries.Count > 0,
                _ => true,
            })
            .Select(e => KeyValuePair.Create(e.Key, e.Value!))]);
}

/// <summary>
/// Adds to a YAML file in place, keeping its comments and every other line as they were. A path names a key from the top of the
/// file, through maps (<c>target.controls</c>) and list items picked by a field (<c>domains[name=Example Pty Ltd].systems</c>).
/// </summary>
internal static partial class YamlEdit
{
    /// <summary>Adds <paramref name="item"/> at the end of the list at <paramref name="path"/>, creating the list when it is missing or <c>[]</c>.</summary>
    public static Result<string> AppendItem(string yaml, string path, YamlMap item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Locate(yaml, path).Bind(found => found.Value switch
        {
            null => Result.Ok(found.Lines.Insert(LastLine(found.Parent), [$"{Indent(found.KeyIndent)}{found.Key}:", .. Item(item, found.KeyIndent + 2)])),
            YamlSequenceNode { Style: SequenceStyle.Flow, Children.Count: 0 } empty =>
                Result.Ok(found.Lines.RemoveFlowList(empty).Insert(empty.Start.Line, Item(item, found.KeyIndent + 2))),
            YamlSequenceNode { Style: not SequenceStyle.Flow, Children.Count: > 0 } list =>
                Result.Ok(found.Lines.Insert(LastLine(list), Item(item, found.Lines.IndentOf(list.Children[0].Start.Line)))),
            _ => Result.Fail<string>(new Problem("setup.not_a_list", $"'{path}' is not a list that can take a new entry; add it by hand.")),
        });
    }

    /// <summary>Adds <paramref name="value"/> at the end of the list of names at <paramref name="path"/>, creating the list when it is missing.</summary>
    public static Result<string> AppendScalar(string yaml, string path, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Locate(yaml, path).Bind(found => found.Value switch
        {
            null => Result.Ok(found.Lines.Insert(LastLine(found.Parent), [$"{Indent(found.KeyIndent)}{found.Key}: [{Scalar(value)}]"])),
            YamlSequenceNode { Style: SequenceStyle.Flow } flow => Result.Ok(found.Lines.AppendToFlowList(flow, Scalar(value))),
            YamlSequenceNode { Children.Count: > 0 } list =>
                Result.Ok(found.Lines.Insert(LastLine(list), [$"{Indent(found.Lines.IndentOf(list.Children[0].Start.Line))}- {Scalar(value)}"])),
            _ => Result.Fail<string>(new Problem("setup.not_a_list", $"'{path}' is not a list that can take a new entry; add it by hand.")),
        });
    }

    /// <summary>The item as block lines, its dash at <paramref name="indent"/>.</summary>
    private static IEnumerable<string> Item(YamlMap item, int indent) =>
        Entries(item, indent + 2).Select((line, index) => index == 0 ? $"{Indent(indent)}- {line.TrimStart()}" : line);

    private static IEnumerable<string> Entries(YamlMap map, int indent) =>
        map.Entries.SelectMany(entry => entry.Value switch
        {
            YamlMap nested => [$"{Indent(indent)}{entry.Key}:", .. Entries(nested, indent + 2)],
            YamlFlowList list => [$"{Indent(indent)}{entry.Key}: [{string.Join(", ", list.Items.Select(Scalar))}]"],
            YamlText text => [$"{Indent(indent)}{entry.Key}: {Scalar(text.Value)}"],
            _ => Array.Empty<string>(),
        });

    /// <summary>The value as a YAML scalar: plain when YAML reads it back as the same text, double-quoted otherwise.</summary>
    internal static string Scalar(string value) =>
        PlainSafe().IsMatch(value) && !value.Contains(": ", StringComparison.Ordinal) && !value.EndsWith(':') && !value.EndsWith(' ')
            && !LooksTyped().IsMatch(value)
            ? value
            : $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string Indent(int columns) => new(' ', columns);

    /// <summary>The last line holding any of the node's content, so a new entry goes after it and before the next key or comment block.</summary>
    private static int LastLine(YamlNode node) => node switch
    {
        YamlScalarNode scalar => scalar.End.Column == 1 && scalar.End.Line > scalar.Start.Line ? (int)scalar.End.Line - 1 : (int)scalar.End.Line,
        // A flow collection is taken to sit on one line: YamlDotNet's end mark for it does not point at its closing bracket.
        YamlSequenceNode { Style: SequenceStyle.Flow } or YamlMappingNode { Style: MappingStyle.Flow } => (int)node.Start.Line,
        YamlSequenceNode list => list.Children.Select(LastLine).DefaultIfEmpty((int)list.Start.Line).Max(),
        YamlMappingNode map => map.Children.SelectMany(pair => new[] { LastLine(pair.Key), LastLine(pair.Value) }).DefaultIfEmpty((int)map.Start.Line).Max(),
        _ => (int)node.End.Line,
    };

    private static Result<Location> Locate(string yaml, string path)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            return Result.Fail<Location>(new Problem("setup.yaml_invalid", $"the file is not valid YAML: {ex.Message}"));
        }

        if (stream.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode current)
        {
            return Result.Fail<Location>(new Problem("setup.yaml_invalid", "the file must be a map of keys."));
        }

        var steps = Step().Matches(path).ToArray();
        foreach (var step in steps[..^1])
        {
            var next = Child(current, step.Groups["key"].Value);
            if (step.Groups["field"].Success)
            {
                next = (next as YamlSequenceNode)?.Children.OfType<YamlMappingNode>()
                    .FirstOrDefault(item => Child(item, step.Groups["field"].Value) is YamlScalarNode { Value: var v } && v == step.Groups["value"].Value);
            }

            if (next is not YamlMappingNode map)
            {
                return Result.Fail<Location>(new Problem("setup.path_missing", $"the file has nothing at '{path}'."));
            }

            current = map;
        }

        var key = steps[^1].Groups["key"].Value;
        var lines = new Lines(yaml);
        var keyNode = current.Children.Keys.OfType<YamlScalarNode>().FirstOrDefault(k => k.Value == key);
        var keyIndent = keyNode is not null ? (int)keyNode.Start.Column - 1 : (int)current.Children.Keys.First().Start.Column - 1;
        return Result.Ok(new Location(lines, current, key, keyIndent, Child(current, key)));
    }

    private static YamlNode? Child(YamlMappingNode map, string key) =>
        map.Children.FirstOrDefault(pair => pair.Key is YamlScalarNode { Value: var k } && k == key).Value;

    [GeneratedRegex(@"(?<key>[^.\[\]]+)(\[(?<field>[^=\]]+)=(?<value>[^\]]*)\])?")]
    private static partial Regex Step();

    [GeneratedRegex(@"^[A-Za-z0-9_./][A-Za-z0-9 _./@:+()-]*$")]
    private static partial Regex PlainSafe();

    [GeneratedRegex(@"^([-+]?[0-9._]+|0x[0-9a-fA-F]+|\d{4}-\d\d-\d\d.*|true|false|yes|no|on|off|y|n|null|~)$", RegexOptions.IgnoreCase)]
    private static partial Regex LooksTyped();

    /// <summary>The key to add to or create, in the map that holds it, with its current value (null when missing).</summary>
    private sealed record Location(Lines Lines, YamlMappingNode Parent, string Key, int KeyIndent, YamlNode? Value);

    /// <summary>The file as lines, edited by line number (1-based, as YamlDotNet's marks count them).</summary>
    private sealed class Lines(string yaml)
    {
        private readonly List<string> _lines = [.. yaml.ReplaceLineEndings("\n").Split('\n')];

        public int IndentOf(long line) => _lines[(int)line - 1].Length - _lines[(int)line - 1].TrimStart(' ').Length;

        /// <summary>The file with <paramref name="added"/> after line <paramref name="after"/>.</summary>
        public string Insert(long after, IEnumerable<string> added)
        {
            _lines.InsertRange((int)after, added);
            return string.Join('\n', _lines);
        }

        /// <summary>Takes out an empty <c>[]</c> and the space before it, keeping any comment after it.</summary>
        public Lines RemoveFlowList(YamlSequenceNode empty)
        {
            var (line, start) = ((int)empty.Start.Line - 1, (int)empty.Start.Column - 1);
            var (_, close) = Closing(line, start);
            var text = _lines[line];
            var from = start > 0 && text[start - 1] == ' ' ? start - 1 : start;
            _lines[line] = text[..from] + text[(close + 1)..];
            return this;
        }

        /// <summary>The file with <paramref name="scalar"/> before the list's closing bracket.</summary>
        public string AppendToFlowList(YamlSequenceNode flow, string scalar)
        {
            var (line, close) = Closing((int)flow.Start.Line - 1, (int)flow.Start.Column - 1);
            _lines[line] = _lines[line].Insert(close, flow.Children.Count > 0 ? $", {scalar}" : scalar);
            return string.Join('\n', _lines);
        }

        /// <summary>
        /// The line and column of the bracket that closes the flow list opening at <paramref name="line"/>, <paramref name="column"/>
        /// (0-based). YamlDotNet's end mark for a flow list does not point at it, so the text is scanned, skipping quoted scalars.
        /// </summary>
        private (int Line, int Column) Closing(int line, int column)
        {
            var depth = 0;
            char? quote = null;
            for (var l = line; l < _lines.Count; l++)
            {
                var text = _lines[l];
                for (var c = l == line ? column : 0; c < text.Length; c++)
                {
                    var ch = text[c];
                    if (quote is not null)
                    {
                        if (ch == '\\' && quote == '"')
                        {
                            c++;
                        }
                        else if (ch == quote)
                        {
                            quote = null;
                        }
                    }
                    else if (ch is '"' or '\'')
                    {
                        quote = ch;
                    }
                    else if (ch == '[')
                    {
                        depth++;
                    }
                    else if (ch == ']' && --depth == 0)
                    {
                        return (l, c);
                    }
                }
            }

            throw new InvalidOperationException("A flow list that YamlDotNet parsed has no closing bracket.");
        }
    }
}
