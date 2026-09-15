using System.Text;
using MimeKit.Text;

namespace SmtpMqttGateway.Services;

/// <summary>
/// Derives a plain-text approximation of an HTML body using MimeKit's own
/// HtmlTokenizer (the mechanism documented by MimeKit for this purpose),
/// rather than a hand-rolled HTML parser.
/// </summary>
public static class HtmlTextExtractor
{
    public static string ExtractText(string html)
    {
        using var reader = new StringReader(html);
        var tokenizer = new HtmlTokenizer(reader) { DecodeCharacterReferences = true };
        var text = new StringBuilder();
        var skipDepth = 0;

        while (tokenizer.ReadNextToken(out var token))
        {
            switch (token.Kind)
            {
                case HtmlTokenKind.Tag:
                    var tag = (HtmlTagToken)token;
                    if (tag.Id is HtmlTagId.Script or HtmlTagId.Style)
                    {
                        if (tag.IsEndTag)
                        {
                            skipDepth = Math.Max(0, skipDepth - 1);
                        }
                        else if (!tag.IsEmptyElement)
                        {
                            skipDepth++;
                        }
                    }
                    else if (IsBlockLevel(tag.Id) && text.Length > 0 && text[^1] != '\n')
                    {
                        text.Append('\n');
                    }
                    break;

                case HtmlTokenKind.Data:
                    if (skipDepth == 0)
                    {
                        text.Append(((HtmlDataToken)token).Data);
                    }
                    break;
            }
        }

        return CollapseWhitespace(text.ToString());
    }

    private static bool IsBlockLevel(HtmlTagId id) => id switch
    {
        HtmlTagId.P or HtmlTagId.Br or HtmlTagId.Div or HtmlTagId.TR or HtmlTagId.LI
            or HtmlTagId.H1 or HtmlTagId.H2 or HtmlTagId.H3 or HtmlTagId.H4 or HtmlTagId.H5 or HtmlTagId.H6
            or HtmlTagId.BlockQuote or HtmlTagId.Table or HtmlTagId.UL or HtmlTagId.OL
            or HtmlTagId.Header or HtmlTagId.Footer or HtmlTagId.Section or HtmlTagId.Article => true,
        _ => false,
    };

    private static string CollapseWhitespace(string input)
    {
        var lines = input
            .Split('\n')
            .Select(line => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Where(line => line.Length > 0);

        return string.Join('\n', lines).Trim();
    }
}
