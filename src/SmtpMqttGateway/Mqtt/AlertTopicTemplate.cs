using System.Text;
using System.Text.RegularExpressions;
using SmtpMqttGateway.Models;

namespace SmtpMqttGateway.Mqtt;

/// <summary>
/// Resolves a configurable topic template (e.g. "homelab/alerts/{senderLocal}")
/// into a concrete MQTT topic for a given alert event, so different senders
/// can be routed to different topics/flows without the gateway needing to
/// know anything about classification itself.
/// </summary>
public static partial class AlertTopicTemplate
{
    public static readonly IReadOnlyCollection<string> KnownVariables =
    [
        "sender", "senderLocal", "senderDomain",
        "recipient", "recipientLocal", "recipientDomain",
    ];

    [GeneratedRegex(@"\{([a-zA-Z]+)\}")]
    private static partial Regex PlaceholderPattern();

    /// <summary>
    /// Validates a template at startup: rejects unknown "{variable}" names and
    /// literal '+'/'#' characters outside of placeholders (both are MQTT
    /// wildcard characters and are invalid in a topic actually published to).
    /// </summary>
    public static IReadOnlyList<string> Validate(string template)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(template))
        {
            errors.Add("must not be empty");
            return errors;
        }

        var unknown = PlaceholderPattern()
            .Matches(template)
            .Select(m => m.Groups[1].Value)
            .Where(name => !KnownVariables.Contains(name))
            .Distinct()
            .ToList();

        if (unknown.Count > 0)
        {
            errors.Add($"references unknown variable(s) {{{string.Join(", ", unknown)}}}; known variables are: {string.Join(", ", KnownVariables)}");
        }

        var literalText = PlaceholderPattern().Replace(template, string.Empty);
        if (literalText.Contains('+') || literalText.Contains('#'))
        {
            errors.Add("must not contain literal '+' or '#' characters outside of {variable} placeholders (they are MQTT wildcard characters and are invalid in a published topic)");
        }

        return errors;
    }

    public static string Resolve(string template, AlertEnvelopeV1 alertEvent)
    {
        var (senderLocal, senderDomain) = SplitAddress(alertEvent.Envelope.From);
        var firstRecipient = alertEvent.Envelope.Recipients.Count > 0 ? alertEvent.Envelope.Recipients[0] : string.Empty;
        var (recipientLocal, recipientDomain) = SplitAddress(firstRecipient);

        return PlaceholderPattern().Replace(template, match => match.Groups[1].Value switch
        {
            "sender" => Sanitize(alertEvent.Envelope.From),
            "senderLocal" => Sanitize(senderLocal),
            "senderDomain" => Sanitize(senderDomain),
            "recipient" => Sanitize(firstRecipient),
            "recipientLocal" => Sanitize(recipientLocal),
            "recipientDomain" => Sanitize(recipientDomain),
            var name => throw new InvalidOperationException(
                $"Topic template references unknown variable '{{{name}}}'. This should have been caught by startup validation."),
        });
    }

    private static (string Local, string Domain) SplitAddress(string address)
    {
        var at = address.LastIndexOf('@');
        return at < 0
            ? (address, string.Empty)
            : (address[..at], address[(at + 1)..]);
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "unknown";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '/' or '+' or '#' => '_',
                _ when char.IsWhiteSpace(c) || char.IsControl(c) => '_',
                _ => char.ToLowerInvariant(c),
            });
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }
}
