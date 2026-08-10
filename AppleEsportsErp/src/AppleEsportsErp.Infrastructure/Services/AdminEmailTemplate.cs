using System.Text;

namespace AppleEsportsErp.Infrastructure.Services;

/// <summary>
/// The Apple Esports email shell, in one place.
///
/// The existing alerts each carry their own copy of about twenty lines of inline HTML. That
/// is fine for two and unmanageable for six, and they have already drifted apart from one
/// another. New alerts build on this instead.
/// </summary>
public static class AdminEmailTemplate
{
    public const string Red = "#dc2626";
    public const string Amber = "#f59e0b";
    public const string Green = "#22c55e";

    /// <summary>"2 hours 5 minutes" rather than "02:05:13.4471" - this is read by a person.</summary>
    public static string Describe(TimeSpan span)
    {
        if (span.TotalMinutes < 1) return $"{Math.Max(1, (int)span.TotalSeconds)} seconds";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} minutes";

        var hours = (int)span.TotalHours;
        var minutes = span.Minutes;
        return minutes == 0 ? $"{hours} hour{(hours == 1 ? "" : "s")}"
                            : $"{hours} hour{(hours == 1 ? "" : "s")} {minutes} minutes";
    }

    public static string Compose(
        string heading,
        string accent,
        IEnumerable<(string Label, string Value)> rows,
        string? footnote = null)
    {
        var body = new StringBuilder();

        body.Append(
            "<div style='background-color:#050505;color:#ffffff;font-family:\"Segoe UI\",Tahoma,Geneva,Verdana,sans-serif;padding:40px 20px;'>" +
            "<div style='max-width:600px;margin:0 auto;background-color:#111111;border:1px solid #333333;border-radius:12px;overflow:hidden;'>" +
            "<div style='background:linear-gradient(135deg,#1a1a24 0%,#0d0d14 100%);padding:30px 20px;border-bottom:2px solid " + accent + ";text-align:center;'>" +
            "<h1 style='margin:0;font-size:26px;letter-spacing:2px;color:#ffffff;text-transform:uppercase;'>APPLE ESPORTS</h1>" +
            "</div><div style='padding:36px 30px;'>" +
            "<h2 style='margin-top:0;color:" + accent + ";font-size:22px;border-bottom:2px solid #333333;padding-bottom:14px;'>" +
            System.Net.WebUtility.HtmlEncode(heading) + "</h2>" +
            "<table style='width:100%;border-collapse:collapse;font-size:15px;'>");

        foreach (var (label, value) in rows)
        {
            body.Append(
                "<tr>" +
                "<td style='padding:10px 0;color:#9ca3af;width:42%;vertical-align:top;'>" +
                System.Net.WebUtility.HtmlEncode(label) + "</td>" +
                "<td style='padding:10px 0;color:#ffffff;font-weight:600;'>" +
                System.Net.WebUtility.HtmlEncode(value) + "</td>" +
                "</tr>");
        }

        body.Append("</table>");

        if (!string.IsNullOrWhiteSpace(footnote))
        {
            body.Append(
                "<p style='margin-top:28px;padding-top:18px;border-top:1px solid #333333;color:#9ca3af;font-size:13px;line-height:1.6;'>" +
                System.Net.WebUtility.HtmlEncode(footnote) + "</p>");
        }

        body.Append(
            "</div><div style='padding:18px;text-align:center;color:#6b7280;font-size:11px;border-top:1px solid #222222;'>" +
            "Sent automatically by Apple Esports. Times are IST." +
            "</div></div></div>");

        return body.ToString();
    }
}
