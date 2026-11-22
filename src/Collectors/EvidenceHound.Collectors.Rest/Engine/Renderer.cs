using System.Text;
using System.Text.Json.Nodes;
using EvidenceHound.Collectors.Rest.Profiles;
using EvidenceHound.Core.Results;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Collectors.Rest.Engine;

/// <summary>How placeholders are filled: for a request, or for evidence (secrets and item fields left as placeholders).</summary>
public enum RenderMode
{
    Request,
    Evidence,
}

/// <summary>
/// Fills in <c>${...}</c> placeholders: parameters (or their defaults), variables set by queries, secrets, and fields of the
/// current <c>for_each</c> item. A value that is not there is a problem, never an empty string.
/// </summary>
public sealed record Renderer(IReadOnlyDictionary<string, string> Parameters, IReadOnlyDictionary<string, string> Vars, IReadOnlyDictionary<string, Secret> Secrets)
{
    public Renderer WithVars(IReadOnlyDictionary<string, string> vars) => this with { Vars = vars };

    public Result<string> Render(string template, JsonNode? item = null, RenderMode mode = RenderMode.Request, Func<string, string>? escapeItem = null) =>
        Template.Parse(template).Bind(parts =>
        {
            var text = new StringBuilder();
            foreach (var part in parts)
            {
                var value = part switch
                {
                    LiteralPart literal => Result.Ok(literal.Text),
                    ReferencePart { Kind: ReferenceKind.Secret } secret when mode == RenderMode.Evidence => Result.Ok($"${{secrets.{secret.Name}}}"),
                    ReferencePart { Kind: ReferenceKind.Item } field when mode == RenderMode.Evidence => Result.Ok($"${{item{field.Name[1..]}}}"),
                    ReferencePart reference => Value(reference, item).Map(v => reference.Kind == ReferenceKind.Item && escapeItem is not null ? escapeItem(v) : v),
                    _ => Result.Fail<string>(new Problem("rest.template_invalid", $"'{template}' could not be read.")),
                };
                if (value is Result<string>.Fail failed)
                {
                    return Result.Fail<string>(failed.Errors);
                }

                text.Append(((Result<string>.Ok)value).Value);
            }

            return Result.Ok(text.ToString());
        });

    /// <summary>A copy of a request body with every text value filled in.</summary>
    public Result<JsonBody> RenderBody(JsonNode? body, JsonNode? item) => body switch
    {
        null => Result.Ok(new JsonBody(null)),
        JsonObject obj => Result.Combine(obj.Select(p => RenderBody(p.Value, item).Map(v => KeyValuePair.Create(p.Key, v.Node))))
            .Map(pairs => new JsonBody(new JsonObject(pairs))),
        JsonArray array => Result.Combine(array.Select(i => RenderBody(i, item)))
            .Map(values => new JsonBody(new JsonArray([.. values.Select(b => b.Node)]))),
        JsonValue value when value.TryGetValue<string>(out var text) => Render(text, item).Map(t => new JsonBody(JsonValue.Create(t))),
        _ => Result.Ok(new JsonBody(body.DeepClone())),
    };

    private Result<string> Value(ReferencePart reference, JsonNode? item) => reference.Kind switch
    {
        ReferenceKind.Secret => Secrets.TryGetValue(reference.Name, out var secret)
            ? Result.Ok(secret.Reveal())
            : Result.Fail<string>(new Problem("rest.secret_missing", $"Secret '{reference.Name}' is not set for this instance.")),
        ReferenceKind.Item => JsonPath.Parse(reference.Name).Bind(path => path.Select(item) switch
        {
            { Found: true, Values: [JsonValue scalar] } => Result.Ok(Text(scalar)),
            _ => Result.Fail<string>(new Problem("rest.item_field_missing", $"The item has no single value at {reference.Name}.")),
        }),
        _ => Parameters.TryGetValue(reference.Name, out var parameter) ? Result.Ok(parameter)
            : Vars.TryGetValue(reference.Name, out var variable) ? Result.Ok(variable)
            : Result.Fail<string>(new Problem("rest.value_missing", $"${{{reference.Name}}} has no value: it is not set and has no default, or the query that sets it did not run.")),
    };

    /// <summary>A scalar as text: strings as they are, numbers and booleans as JSON writes them.</summary>
    public static string Text(JsonValue value) =>
        value.TryGetValue<string>(out var text) ? text : value.ToJsonString();
}

/// <summary>A JSON value that may be null, so it can travel in a result.</summary>
public sealed record JsonBody(JsonNode? Node);
