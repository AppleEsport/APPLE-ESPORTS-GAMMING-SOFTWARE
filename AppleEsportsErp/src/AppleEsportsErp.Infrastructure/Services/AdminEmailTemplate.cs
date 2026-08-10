using System.Text;
using System.Net;

namespace AppleEsportsErp.Infrastructure.Services;

/// <summary>
/// The Apple Esports email shell, in one place.
///
/// Built with tables and inline styles throughout, deliberately. Outlook renders none of
/// flexbox, grid or external stylesheets, and a layout that only works in Gmail is a layout
/// that half the recipients see collapsed into a column of unstyled text.
///
/// Every alert reads the same way: a plain-English sentence saying what happened, one large
/// figure if there is one worth seeing at a glance, then the detail. Someone checking their
/// phone should be able to stop after the first line.
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
        if (span.TotalHours < 1)
        {
            var mins = (int)span.TotalMinutes;
            return $"{mins} minute{(mins == 1 ? "" : "s")}";
        }

        var hours = (int)span.TotalHours;
        var minutes = span.Minutes;
        return minutes == 0
            ? $"{hours} hour{(hours == 1 ? "" : "s")}"
            : $"{hours} hour{(hours == 1 ? "" : "s")} {minutes} minute{(minutes == 1 ? "" : "s")}";
    }

    /// <param name="heading">Short title, e.g. "Money taken today".</param>
    /// <param name="summary">One sentence in plain English saying what happened and why it matters.</param>
    /// <param name="headline">Optional single figure worth seeing without reading, e.g. "Rs 12,450".</param>
    /// <param name="rows">Label/value detail. A blank label draws a divider.</param>
    /// <param name="footnote">Optional closing note - what to do, or reassurance.</param>
    public static string Compose(
        string heading,
        string accent,
        string summary,
        IEnumerable<(string Label, string Value)> rows,
        string? headline = null,
        string? footnote = null)
    {
        var m = new StringBuilder();

        m.Append(
            "<!DOCTYPE html><html><body style='margin:0;padding:0;background-color:#0a0a0f;'>" +
            // Preheader: what the inbox shows next to the subject before it is opened.
            "<div style='display:none;max-height:0;overflow:hidden;'>" + Esc(summary) + "</div>" +
            "<table role='presentation' width='100%' cellpadding='0' cellspacing='0' " +
                   "style='background-color:#0a0a0f;padding:28px 12px;'><tr><td align='center'>" +
            "<table role='presentation' width='100%' cellpadding='0' cellspacing='0' " +
                   "style='max-width:600px;background-color:#131319;border:1px solid #2a2a33;border-radius:14px;overflow:hidden;'>");

        // ── Brand bar ──
        m.Append(
            "<tr><td style='background-color:#0e0e14;padding:26px 30px;border-bottom:3px solid " + accent + ";'>" +
            "<div style='font-family:Arial,Helvetica,sans-serif;font-size:21px;font-weight:bold;" +
                   "letter-spacing:3px;color:#ffffff;'>APPLE ESPORTS</div>" +
            "<div style='font-family:Arial,Helvetica,sans-serif;font-size:11px;letter-spacing:2px;" +
                   "color:#7a7a88;padding-top:5px;text-transform:uppercase;'>Gaming Caf" + "é" + " Management</div>" +
            "</td></tr>");

        // ── Heading + the plain-English line ──
        m.Append(
            "<tr><td style='padding:30px 30px 0 30px;font-family:Arial,Helvetica,sans-serif;'>" +
            "<div style='font-size:20px;font-weight:bold;color:" + accent + ";padding-bottom:12px;'>" +
            Esc(heading) + "</div>" +
            "<div style='font-size:15px;line-height:1.65;color:#d4d4dc;'>" + Esc(summary) + "</div>" +
            "</td></tr>");

        // ── The one figure worth seeing at a glance ──
        if (!string.IsNullOrWhiteSpace(headline))
        {
            m.Append(
                "<tr><td style='padding:24px 30px 0 30px;'>" +
                "<table role='presentation' width='100%' cellpadding='0' cellspacing='0' " +
                       "style='background-color:#0e0e14;border:1px solid #2a2a33;border-radius:10px;'>" +
                "<tr><td align='center' style='padding:22px;font-family:Arial,Helvetica,sans-serif;" +
                       "font-size:30px;font-weight:bold;color:#ffffff;letter-spacing:1px;'>" +
                Esc(headline) + "</td></tr></table></td></tr>");
        }

        // ── Detail ──
        m.Append("<tr><td style='padding:26px 30px 0 30px;'>" +
                 "<table role='presentation' width='100%' cellpadding='0' cellspacing='0' " +
                        "style='font-family:Arial,Helvetica,sans-serif;font-size:14px;'>");

        foreach (var (label, value) in rows)
        {
            if (string.IsNullOrWhiteSpace(label) && string.IsNullOrWhiteSpace(value))
            {
                m.Append("<tr><td colspan='2' style='padding:6px 0;'>" +
                         "<div style='border-top:1px solid #2a2a33;'></div></td></tr>");
                continue;
            }

            // Indented labels group under the line above them, so a breakdown reads as one.
            var indented = label.StartsWith("  ");
            m.Append(
                "<tr>" +
                "<td style='padding:11px 12px 11px 0;color:" + (indented ? "#7a7a88" : "#9a9aa8") + ";" +
                       "border-bottom:1px solid #1f1f27;vertical-align:top;width:48%;" +
                       (indented ? "padding-left:16px;font-size:13px;" : "") + "'>" +
                Esc(label.TrimStart()) + "</td>" +
                "<td style='padding:11px 0;color:#ffffff;font-weight:bold;" +
                       "border-bottom:1px solid #1f1f27;vertical-align:top;" +
                       (indented ? "font-size:13px;font-weight:normal;color:#d4d4dc;" : "") + "'>" +
                Esc(value) + "</td></tr>");
        }

        m.Append("</table></td></tr>");

        if (!string.IsNullOrWhiteSpace(footnote))
        {
            m.Append(
                "<tr><td style='padding:24px 30px 0 30px;'>" +
                "<table role='presentation' width='100%' cellpadding='0' cellspacing='0' " +
                       "style='background-color:#0e0e14;border-left:3px solid " + accent + ";border-radius:6px;'>" +
                "<tr><td style='padding:16px 18px;font-family:Arial,Helvetica,sans-serif;font-size:13px;" +
                       "line-height:1.65;color:#b4b4c2;'>" + Esc(footnote) + "</td></tr></table></td></tr>");
        }

        m.Append(
            "<tr><td style='padding:28px 30px 26px 30px;'>" +
            "<div style='border-top:1px solid #2a2a33;padding-top:16px;font-family:Arial,Helvetica,sans-serif;" +
                   "font-size:11px;line-height:1.7;color:#6a6a78;'>" +
            "Sent automatically by Apple Esports. All times are Indian Standard Time.<br>" +
            "You are receiving this because you are an owner or admin on this system." +
            "</div></td></tr>" +
            "</table></td></tr></table></body></html>");

        return m.ToString();
    }

    private static string Esc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
