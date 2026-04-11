using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GuardCode.Content.Loading;

/// <summary>
/// The outcome of a successful <see cref="FrontmatterParser.Parse{T}(string)"/> call:
/// the strongly-typed frontmatter and the remaining markdown body.
/// </summary>
/// <remarks>
/// Declared at the top level (not nested inside <see cref="FrontmatterParser"/>)
/// to satisfy CA1034 — nested types should not be publicly visible.
/// </remarks>
public readonly record struct ParseResult<T>(T Frontmatter, string Body);

/// <summary>
/// Splits a markdown file into its YAML frontmatter and body, then
/// strictly deserializes the frontmatter into a typed record.
/// </summary>
/// <remarks>
/// Strictness is load-bearing for security (design spec §6.3):
/// unknown properties are rejected, the naming convention is fixed,
/// and no dynamic type resolution is ever performed.
/// </remarks>
public static class FrontmatterParser
{
    private const string Delimiter = "---";
    private const string ErrNullContent = "file content is null";
    private const string ErrMissingOpen = "file does not begin with YAML frontmatter delimiter (---)";
    private const string ErrUnclosed = "YAML frontmatter is not closed — missing terminating --- delimiter";
    private const string ErrMalformedPrefix = "YAML frontmatter is malformed or contains unknown fields: ";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        // Do NOT call IgnoreUnmatchedProperties: we want unknown fields to throw.
        .Build();

    public static ParseResult<T> Parse<T>(string fileContent) where T : class, new()
    {
        if (fileContent is null)
        {
            throw new FrontmatterParseException(ErrNullContent);
        }

        using var reader = new StringReader(fileContent);

        // First non-empty line must be the opening delimiter.
        var firstLine = ReadNextNonEmptyLine(reader);
        if (firstLine is null || firstLine.Trim() != Delimiter)
        {
            throw new FrontmatterParseException(ErrMissingOpen);
        }

        // Accumulate until the closing delimiter.
        var yamlBuilder = new StringBuilder();
        string? line;
        var foundClose = false;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Trim() == Delimiter)
            {
                foundClose = true;
                break;
            }
            yamlBuilder.AppendLine(line);
        }

        if (!foundClose)
        {
            throw new FrontmatterParseException(ErrUnclosed);
        }

        T frontmatter;
        try
        {
            frontmatter = Deserializer.Deserialize<T>(yamlBuilder.ToString()) ?? new T();
        }
        catch (YamlException ex)
        {
            throw new FrontmatterParseException(ErrMalformedPrefix + ex.Message, ex);
        }

        var body = reader.ReadToEnd().TrimStart('\r', '\n');
        return new ParseResult<T>(frontmatter, body);
    }

    private static string? ReadNextNonEmptyLine(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }
        return null;
    }
}
