using System.Globalization;
using System.Text;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Videos.Reports;

public class AnalysisReportService(AppDbContext dbContext, IOptions<VideoProcessingOptions> options) : IAnalysisReportService
{
    private const decimal PercentageScale = 100m;
    private readonly VideoProcessingOptions _options = options.Value;

    public async Task<ApiResponse<AnalysisReportFile>> GeneratePdfAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var result = await dbContext.AiResults
            .AsNoTracking()
            .Include(aiResult => aiResult.Video)
                .ThenInclude(video => video.User)
            .Include(aiResult => aiResult.Video)
                .ThenInclude(video => video.MetadataResult)
            .Include(aiResult => aiResult.ModelVersion)
            .Include(aiResult => aiResult.EvidenceItems)
            .Where(aiResult => aiResult.VideoId == videoId
                && aiResult.Video.UserId == currentUserId
                && aiResult.Video.DeletedAt == null
                && aiResult.Video.Status != VideoStatus.Deleted)
            .OrderByDescending(aiResult => aiResult.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return ApiResponse<AnalysisReportFile>.ErrorResponse("Analysis report is not available yet.");
        }

        if (result.CreatedAt <= DateTimeOffset.UtcNow.Subtract(_options.ReportRetention))
        {
            return ApiResponse<AnalysisReportFile>.ErrorResponse(
                $"This report is no longer available because the {FormatRetentionPeriod(_options.ReportRetention)} report retention period has ended.");
        }

        var document = new ReportPdfDocument();
        BuildReport(document, result);

        return ApiResponse<AnalysisReportFile>.SuccessResponse(new AnalysisReportFile
        {
            FileName = CreateReportFileName(result.Video.OriginalName, result.CreatedAt),
            Content = document.Save()
        });
    }

    private static string FormatRetentionPeriod(TimeSpan retention)
    {
        if (retention.TotalDays >= 1 && retention.TotalDays % 1 == 0)
        {
            return $"{(int)retention.TotalDays}-day";
        }

        if (retention.TotalHours >= 1 && retention.TotalHours % 1 == 0)
        {
            return $"{(int)retention.TotalHours}-hour";
        }

        return $"{Math.Max(1, (int)Math.Ceiling(retention.TotalMinutes))}-minute";
    }

    private static void BuildReport(ReportPdfDocument document, AiResult result)
    {
        var evidenceItems = GetReportEvidenceItems(result.EvidenceItems);

        document.SetSection("Video result");
        document.AddPage();
        DrawResultOverview(document, result);

        document.SetSection("Video details");
        document.AddPage();
        DrawVideoDetails(document, result);

        if (evidenceItems.Count > 0)
        {
            document.SetSection("Important findings");
            document.AddPage();
            DrawEvidence(document, evidenceItems);
        }

        document.FinalizePages();
    }

    private static void DrawResultOverview(ReportPdfDocument document, AiResult result)
    {
        document.SectionTitle(
            "Video result",
            "A short authenticity summary for the analyzed video.");

        document.ResultSummaryCards(
            FormatLabel(result.Label).ToUpperInvariant(),
            result.Label,
            FormatPercent(result.Confidence));

        DrawProbabilityBalance(document, result);

        document.TwoColumnKeyValueCards(
            "Report owner",
            [
                ("Name", result.Video.User.Name ?? "Not available"),
                ("Email", result.Video.User.Email ?? "Not available"),
                ("Account status", result.Video.User.EmailConfirmed ? "Email confirmed" : "Email not confirmed")
            ],
            "Video file",
            [
                ("File name", result.Video.OriginalName),
                ("File type", result.Video.ContentType ?? "Not available"),
                ("File size", FormatBytes(result.Video.FileSize)),
                ("Duration", result.Video.DurationSeconds is null ? "Not available" : FormatSeconds(result.Video.DurationSeconds.Value)),
                ("Resolution", result.Video.MetadataResult?.Resolution ?? "Not available")
            ]);

        document.InformationBox("Recommended action", GetRecommendedAction(result));
    }

    private static void DrawVideoDetails(ReportPdfDocument document, AiResult result)
    {
        document.SectionTitle("Video details", "Core video properties and score details");

        document.TwoColumnKeyValueCards(
            "Video information",
            [
                ("Original file name", result.Video.OriginalName),
                ("Duration", result.Video.DurationSeconds is null ? "Not available" : FormatSeconds(result.Video.DurationSeconds.Value)),
                ("Format", result.Video.FormatName ?? "Not available"),
                ("Extension", result.Video.FileExtension ?? "Not available"),
                ("Resolution", result.Video.MetadataResult?.Resolution ?? "Not available"),
                ("FPS / Frame rate", result.Video.MetadataResult?.Fps?.ToString("0.###", CultureInfo.InvariantCulture) ?? "Not available")
            ],
            "Detection scores",
            [
                ("Visual model score", FormatPercent(result.VisualScore)),
                ("Metadata score", result.MetadataScore is null ? "Not available" : FormatPercent(result.MetadataScore.Value)),
                ("Temporal score", result.TemporalScore is null ? "Not available" : FormatPercent(result.TemporalScore.Value)),
                ("Final weighted score", FormatPercent(result.FinalScore)),
                ("Confidence", FormatPercent(result.Confidence))
            ]);

        DrawMetadataTiles(document, result.Video.MetadataResult);
    }

    private static void DrawMetadataTiles(ReportPdfDocument document, MetadataResult? metadata)
    {
        var tiles = new List<(string Label, string Value)>
        {
            ("Duration", metadata?.DurationSeconds is null ? "Not available" : FormatCompactSeconds(metadata.DurationSeconds.Value)),
            ("Resolution", metadata?.Resolution ?? "Not available"),
            ("FPS / Frame rate", metadata?.Fps?.ToString("0.###", CultureInfo.InvariantCulture) ?? "Not available"),
            ("Video codec", metadata?.Codec ?? "Not available"),
            ("Bitrate", metadata?.Bitrate?.ToString("N0", CultureInfo.InvariantCulture) ?? "Not available")
        };

        document.EnsureSpace(document.EstimateMetricTileBlockHeight(tiles.Count) + document.HeadingBlockHeight);
        document.Heading("Metadata summary");
        document.MetricTiles(tiles);

        if (metadata is null)
        {
            document.Paragraph("Metadata was not available for this video.");
            return;
        }

    }

    private static void DrawProbabilityBalance(ReportPdfDocument document, AiResult result)
    {
        document.Heading("Probability balance");
        document.MutedLine("These values are model estimates for decision support. They are not proof by themselves.");

        var aiPercent = Math.Clamp(result.FinalScore, 0m, 1m);
        var realPercent = 1m - aiPercent;
        var barWidth = document.ContentWidth;
        const float barHeight = 22;
        document.EnsureSpace(82);
        var x = document.Margin;
        var y = document.CursorY;
        var realWidth = (float)(realPercent * (decimal)barWidth);
        var aiWidth = barWidth - realWidth;
        document.FillRect(x, y, barWidth, barHeight, ReportColor.LightGray);
        document.FillRect(x, y, realWidth, barHeight, ReportColor.Green);
        document.FillRect(x + realWidth, y, aiWidth, barHeight, ReportColor.Red);
        if (realWidth >= 118)
        {
            document.TextTop("Likely real " + FormatPercent(realPercent), x + 10, y + 6, 8.5f, ReportColor.White, bold: true);
        }

        if (aiWidth >= 86)
        {
            document.TextRight("AI " + FormatPercent(aiPercent), x + barWidth - 10, y + 6, 8.5f, ReportColor.White, bold: true);
        }

        document.CursorY += 36;
        document.Text("Likely real: " + FormatPercent(realPercent), x, document.CursorY, 10, ReportColor.Green, bold: true);
        document.TextRight("AI/manipulated: " + FormatPercent(aiPercent), x + barWidth, document.CursorY, 10, ReportColor.Red, bold: true);
        document.CursorY += 24;
    }

    private static void DrawEvidence(ReportPdfDocument document, IEnumerable<EvidenceItem> evidenceItems)
    {
        document.SectionTitle("Important findings", "Only the main findings recorded for this video");
        var items = evidenceItems
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.CreatedAt)
            .ToList();

        if (items.Count == 0)
        {
            document.Paragraph("No evidence items were recorded for this result.");
            return;
        }

        foreach (var item in items)
        {
            var details = new List<string>
            {
                FormatWords(item.Type.ToString()) + " - " + FormatWords(item.Severity.ToString())
            };

            if (item.TimestampSeconds is not null)
            {
                details.Add("Timestamp " + FormatSeconds(item.TimestampSeconds.Value));
            }

            if (item.ScoreImpact is not null)
            {
                details.Add("Score impact " + item.ScoreImpact.Value.ToString("0.###", CultureInfo.InvariantCulture));
            }

            document.SectionCard(SanitizeProviderText(item.Title) ?? item.Title, SanitizeProviderText(item.Description) ?? item.Description, string.Join(" | ", details), ReportCardTone.Default);
        }
    }

    private static List<EvidenceItem> GetReportEvidenceItems(IEnumerable<EvidenceItem> evidenceItems)
    {
        return evidenceItems
            .Where(item => item.Type != EvidenceType.MetadataWarning)
            .OrderByDescending(item => item.Severity)
            .ThenBy(item => item.CreatedAt)
            .ToList();
    }

    private static string GetRecommendedAction(AiResult result)
    {
        return result.Label switch
        {
            AnalysisLabel.LikelyReal => "The video appears likely real. Keep the report with your review record.",
            AnalysisLabel.Suspicious => "Review carefully before trusting or publishing this video.",
            AnalysisLabel.LikelyAiGenerated => "Treat this video as likely AI-generated unless independent evidence proves otherwise.",
            AnalysisLabel.EditedManipulated => "Treat this video as potentially edited or manipulated until manual review is complete.",
            _ => "Use manual review or another verification method because the system could not make a strong decision."
        };
    }

    private static string CreateReportFileName(string originalName, DateTimeOffset createdAt)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalName);
        var safeName = new string(baseName
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            .Take(48)
            .ToArray());

        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "video";
        }

        return $"ai-video-detection-report-{safeName}-{createdAt:yyyyMMddHHmm}.pdf";
    }

    private static string FormatPercent(decimal value)
    {
        return (Math.Clamp(value, 0m, 1m) * PercentageScale).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return size.ToString(unit == 0 ? "0" : "0.##", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    private static string FormatSeconds(decimal seconds)
    {
        return seconds.ToString("0.##", CultureInfo.InvariantCulture) + " seconds";
    }

    private static string FormatCompactSeconds(decimal seconds)
    {
        return seconds.ToString("0.##", CultureInfo.InvariantCulture) + " sec";
    }

    private static string FormatLabel(AnalysisLabel label)
    {
        return label switch
        {
            AnalysisLabel.LikelyReal => "Likely real",
            AnalysisLabel.Suspicious => "Needs review",
            AnalysisLabel.LikelyAiGenerated => "Likely AI-generated",
            AnalysisLabel.EditedManipulated => "Edited or manipulated",
            _ => "Inconclusive"
        };
    }

    private static string FormatWords(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Not available";
        }

        var builder = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (character is '_' or '-')
            {
                builder.Append(' ');
                continue;
            }

            if (i > 0 && char.IsUpper(character) && char.IsLower(value[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(character);
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(builder.ToString().Trim().ToLowerInvariant());
    }

    private static string? SanitizeProviderText(string? value)
    {
        return value?
            .Replace("bitmind-oracle-v1-sn34", "external-verification-model", StringComparison.OrdinalIgnoreCase)
            .Replace("bitmind-subnet-34", "external-verification-model", StringComparison.OrdinalIgnoreCase)
            .Replace("External BitMind verification", "External verification", StringComparison.OrdinalIgnoreCase)
            .Replace("BitMind", "external verification", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class ReportPdfDocument
{
    public const float PageWidth = 595;
    private const float PageHeight = 842;
    private const float BottomMargin = 54;
    private const float HeaderTop = 48;
    private const float BodyFontSize = 9.5f;
    private const float BodyLineHeight = 13.5f;
    private const float HeaderHeight = 82;
    private const float SectionGap = 18;
    private const float CardGap = 16;
    private const float CardPadding = 16;
    private const float CardRadius = 10;
    private const float MinRowHeight = 34;
    private const float RowVerticalPadding = 9;
    private const float RowDividerInset = 14;

    private readonly List<PdfPageContent> _pages = [];
    private PdfPageContent _current = new();
    private string _sectionName = "Video result";

    public float Margin { get; } = 48;

    public float ContentWidth => PageWidth - Margin * 2;

    public float CursorY { get; set; } = 48;

    public float HeadingBlockHeight => 36;

    public void SetSection(string sectionName)
    {
        _sectionName = sectionName;
    }

    public void AddPage()
    {
        if (_current.HasContent)
        {
            _pages.Add(_current);
        }

        _current = new PdfPageContent();
        CursorY = HeaderHeight + 24;
        DrawPageHeader();
    }

    public void FinalizePages()
    {
        if (_current.HasContent)
        {
            _pages.Add(_current);
            _current = new PdfPageContent();
        }

        for (var index = 0; index < _pages.Count; index++)
        {
            DrawFooter(_pages[index], index + 1, _pages.Count);
        }
    }

    public byte[] Save()
    {
        if (_pages.Count == 0)
        {
            FinalizePages();
        }

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>"
        };

        var pageObjectNumbers = new List<int>();
        var contentObjectNumbers = new List<int>();
        var nextObjectNumber = 3;
        foreach (var page in _pages)
        {
            pageObjectNumbers.Add(nextObjectNumber++);
            contentObjectNumbers.Add(nextObjectNumber++);
        }

        objects.Add("<< /Type /Pages /Kids [" + string.Join(' ', pageObjectNumbers.Select(number => $"{number} 0 R")) + $"] /Count {_pages.Count} >>");

        for (var i = 0; i < _pages.Count; i++)
        {
            var contentBytes = Encoding.ASCII.GetBytes(_pages[i].Content.ToString());
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth.ToString(CultureInfo.InvariantCulture)} {PageHeight.ToString(CultureInfo.InvariantCulture)}] /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> /F2 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >> >> >> /Contents {contentObjectNumbers[i]} 0 R >>");
            objects.Add($"<< /Length {contentBytes.Length} >>\nstream\n{_pages[i].Content}\nendstream");
        }

        var output = new StringBuilder();
        var offsets = new List<int> { 0 };
        output.Append("%PDF-1.4\n");

        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(output.ToString()));
            output.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        var xrefStart = Encoding.ASCII.GetByteCount(output.ToString());
        output.Append("xref\n0 ").Append(objects.Count + 1).Append('\n');
        output.Append("0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            output.Append(offset.ToString("0000000000", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        output.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n");
        output.Append(xrefStart).Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(output.ToString());
    }

    public void Space(float y)
    {
        CursorY = Math.Max(CursorY, y);
    }

    public void SectionTitle(string title, string subtitle)
    {
        EnsureSpace(58);
        TextTop(title, Margin, CursorY, 19, ReportColor.Text, bold: true);
        CursorY += 25;
        TextTop(subtitle, Margin, CursorY, 10, ReportColor.Muted);
        CursorY += 30;
    }

    public void Heading(string text)
    {
        EnsureSpace(44);
        if (CursorY > HeaderHeight + 40)
        {
            CursorY += 4;
        }

        TextTop(text, Margin, CursorY, 14, ReportColor.Text, bold: true);
        FillRect(Margin, CursorY + 22, 42, 2, ReportColor.Cyan);
        CursorY += 36;
    }

    public void Paragraph(string text)
    {
        var lines = WrapToWidth(text, ContentWidth, BodyFontSize);
        EnsureSpace(lines.Count * BodyLineHeight + 16);
        foreach (var line in lines)
        {
            TextTop(line, Margin, CursorY, BodyFontSize, ReportColor.Muted);
            CursorY += BodyLineHeight;
        }

        CursorY += 10;
    }

    public void MutedLine(string text)
    {
        EnsureSpace(24);
        TextTop(text, Margin, CursorY, BodyFontSize, ReportColor.Muted);
        CursorY += 22;
    }

    public void ResultSummaryCards(string verdict, AnalysisLabel label, string confidence)
    {
        const float height = 98;
        var cardWidth = (ContentWidth - CardGap) / 2;
        EnsureSpace(height + SectionGap);

        var verdictStyle = GetVerdictStyle(label);
        DrawFilledCard(Margin, CursorY, cardWidth, height, verdictStyle.Fill, verdictStyle.Border);
        TextTop("FINAL VERDICT", Margin + CardPadding, CursorY + 16, 8.5f, verdictStyle.Text, bold: true);
        var verdictY = CursorY + 40;
        foreach (var line in WrapToWidth(verdict, cardWidth - CardPadding * 2, 20, bold: true).Take(2))
        {
            TextTop(line, Margin + CardPadding, verdictY, 20, verdictStyle.Text, bold: true);
            verdictY += 24;
        }

        DrawFilledCard(Margin + cardWidth + CardGap, CursorY, cardWidth, height, ReportColor.LightBlue, ReportColor.BlueSoft);
        TextTop("RESULT CONFIDENCE", Margin + cardWidth + CardGap + CardPadding, CursorY + 16, 8.5f, ReportColor.Blue, bold: true);
        TextTop(confidence, Margin + cardWidth + CardGap + CardPadding, CursorY + 40, 23, ReportColor.Blue, bold: true);
        TextTop("Confidence reflects available analysis signals.", Margin + cardWidth + CardGap + CardPadding, CursorY + 70, 9, ReportColor.Muted);

        CursorY += height + SectionGap;
    }

    public void TwoColumnKeyValueCards(
        string leftTitle,
        IReadOnlyList<(string Label, string Value)> leftRows,
        string rightTitle,
        IReadOnlyList<(string Label, string Value)> rightRows)
    {
        var cardWidth = (ContentWidth - CardGap) / 2;
        var leftRowsPrepared = PrepareRows(leftRows, cardWidth);
        var rightRowsPrepared = PrepareRows(rightRows, cardWidth);
        var leftHeight = CalculateKeyValueCardHeight(leftRowsPrepared);
        var rightHeight = CalculateKeyValueCardHeight(rightRowsPrepared);
        var blockHeight = Math.Max(leftHeight, rightHeight);
        EnsureSpace(blockHeight + SectionGap);

        DrawKeyValueCard(Margin, CursorY, cardWidth, leftHeight, leftTitle, leftRowsPrepared);
        DrawKeyValueCard(Margin + cardWidth + CardGap, CursorY, cardWidth, rightHeight, rightTitle, rightRowsPrepared);
        CursorY += blockHeight + SectionGap;
    }

    public void InformationBox(string title, string body)
    {
        var lines = WrapToWidth(body, ContentWidth - CardPadding * 2, BodyFontSize);
        var height = 44 + lines.Count * BodyLineHeight;
        EnsureSpace(height + 12);

        DrawFilledCard(Margin, CursorY, ContentWidth, height, ReportColor.LightBlue, ReportColor.BlueSoft);
        TextTop(title, Margin + CardPadding, CursorY + 11, 12, ReportColor.Blue, bold: true);
        var y = CursorY + 29;
        foreach (var line in lines)
        {
            TextTop(line, Margin + CardPadding, y, BodyFontSize, ReportColor.Text);
            y += BodyLineHeight;
        }

        CursorY += height + 14;
    }

    public void MetricTiles(IReadOnlyList<(string Label, string Value)> tiles)
    {
        const int columns = 4;
        const float tileHeight = 76;
        var tileWidth = (ContentWidth - CardGap * (columns - 1)) / columns;
        var rows = (int)Math.Ceiling(tiles.Count / (double)columns);
        EnsureSpace(EstimateMetricTileBlockHeight(tiles.Count));

        for (var index = 0; index < tiles.Count; index++)
        {
            var row = index / columns;
            var column = index % columns;
            var x = Margin + column * (tileWidth + CardGap);
            var y = CursorY + row * (tileHeight + CardGap);
            DrawFilledCard(x, y, tileWidth, tileHeight, ReportColor.Panel, ReportColor.Border);
            TextTop(tiles[index].Label.ToUpperInvariant(), x + 10, y + 14, 7.5f, ReportColor.Muted, bold: true);
            var valueLines = WrapToWidth(tiles[index].Value, tileWidth - 20, 12, bold: true).Take(2).ToList();
            var valueY = y + 34;
            foreach (var line in valueLines)
            {
                TextTop(line, x + 10, valueY, 12, ReportColor.Text, bold: true);
                valueY += 14;
            }
        }

        CursorY += rows * tileHeight + (rows - 1) * CardGap + SectionGap;
    }

    public float EstimateMetricTileBlockHeight(int tileCount)
    {
        const int columns = 4;
        const float tileHeight = 76;
        var rows = (int)Math.Ceiling(tileCount / (double)columns);
        return rows * tileHeight + Math.Max(0, rows - 1) * CardGap + SectionGap;
    }

    public void KeyValueGrid(IReadOnlyList<(string Label, string Value)> rows)
    {
        var preparedRows = rows
            .Select(row => (row.Label, Lines: Wrap(row.Value, 64)))
            .Select(row => (row.Label, row.Lines, Height: Math.Max(28, 14 + row.Lines.Count * BodyLineHeight)))
            .ToList();
        var height = 16 + preparedRows.Sum(row => row.Height) + 8;

        EnsureSpace(height + 14);
        var x = Margin;
        var y = CursorY;
        var width = ContentWidth;
        var labelX = x + 16;
        var valueX = x + 180;
        var rowTop = y + 12;

        FillRect(x, y, width, height, ReportColor.Panel);
        StrokeRect(x, y, width, height, ReportColor.Border);

        for (var index = 0; index < preparedRows.Count; index++)
        {
            var row = preparedRows[index];
            Text(row.Label, labelX, rowTop + 5, 8.5f, ReportColor.Muted, bold: true);
            var valueY = rowTop + 5;
            foreach (var line in row.Lines)
            {
                Text(line, valueX, valueY, BodyFontSize, ReportColor.Text);
                valueY += BodyLineHeight;
            }

            rowTop += row.Height;
            if (index < preparedRows.Count - 1)
            {
                StrokeLine(x + 14, rowTop - 4, x + width - 14, rowTop - 4, ReportColor.Border);
            }
        }

        CursorY += height + 18;
    }

    public void InfoBox(IReadOnlyList<(string Label, string Value)> rows)
    {
        var preparedRows = rows
            .Select(row => (row.Label, Lines: Wrap(row.Value, 62)))
            .Select(row => (row.Label, row.Lines, Height: Math.Max(32, 15 + row.Lines.Count * 14)))
            .ToList();
        var height = 18 + preparedRows.Sum(row => row.Height) + 10;

        EnsureSpace(height + 14);
        var x = Margin;
        var y = CursorY;
        var width = ContentWidth;
        var labelX = x + 16;
        var valueX = x + 190;
        var rowTop = y + 14;

        FillRect(x, y, width, height, ReportColor.LightBlue);
        StrokeRect(x, y, width, height, ReportColor.Blue);

        for (var index = 0; index < preparedRows.Count; index++)
        {
            var row = preparedRows[index];
            Text(row.Label, labelX, rowTop + 5, 8.5f, ReportColor.Blue, bold: true);
            var valueY = rowTop + 5;
            foreach (var line in row.Lines)
            {
                Text(line, valueX, valueY, row.Label == "Final verdict" ? 11 : BodyFontSize, ReportColor.Text, bold: row.Label == "Final verdict");
                valueY += 14;
            }

            rowTop += row.Height;
            if (index < preparedRows.Count - 1)
            {
                StrokeLine(x + 14, rowTop - 4, x + width - 14, rowTop - 4, ReportColor.BlueSoft);
            }
        }

        CursorY += height + 18;
    }

    public void WarningBox(string text)
    {
        var lines = WrapToWidth(text, ContentWidth - CardPadding * 2, BodyFontSize, bold: true);
        EnsureSpace(24 + lines.Count * 14);
        var height = 24 + lines.Count * 14;
        DrawFilledCard(Margin, CursorY, ContentWidth, height, ReportColor.WarningFill, ReportColor.Warning);
        var y = CursorY + 12;
        foreach (var line in lines)
        {
            TextTop(line, Margin + CardPadding, y, BodyFontSize, ReportColor.WarningText, bold: true);
            y += 14;
        }

        CursorY += height + 12;
    }

    public void WarningPanel(string title, string body)
    {
        var lines = WrapToWidth(body, ContentWidth - CardPadding * 2, BodyFontSize);
        var height = EstimateWarningPanelHeight(body);
        EnsureSpace(height + 12);

        DrawFilledCard(Margin, CursorY, ContentWidth, height, ReportColor.WarningFill, ReportColor.Warning);
        TextTop(title, Margin + CardPadding, CursorY + 14, 12, ReportColor.WarningText, bold: true);
        var y = CursorY + 36;
        foreach (var line in lines)
        {
            TextTop(line, Margin + CardPadding, y, BodyFontSize, ReportColor.WarningText);
            y += BodyLineHeight;
        }

        CursorY += height + 12;
    }

    public float EstimateWarningPanelHeight(string body)
    {
        var lines = WrapToWidth(body, ContentWidth - CardPadding * 2, BodyFontSize);
        return 50 + lines.Count * BodyLineHeight;
    }

    public void SectionCard(string title, string body, string meta, ReportCardTone tone = ReportCardTone.Default, string? continuationHeading = null)
    {
        var titleLines = Wrap(title, 74).Take(2).ToList();
        var metaLines = Wrap(meta, 96).Take(2).ToList();
        var bodyLines = Wrap(body, 90).Take(6).ToList();
        var height = 20 + titleLines.Count * 14 + metaLines.Count * 11 + bodyLines.Count * 13 + 14;
        if (!HasSpace(height + 12))
        {
            AddPage();
            if (!string.IsNullOrWhiteSpace(continuationHeading))
            {
                Heading(continuationHeading);
            }
        }

        var (fill, border, titleColor, metaColor) = GetCardTone(tone);
        DrawFilledCard(Margin, CursorY, ContentWidth, height, fill, border);

        var y = CursorY + 16;
        foreach (var line in titleLines)
        {
            TextTop(line, Margin + 14, y, 11, titleColor, bold: true);
            y += 14;
        }

        foreach (var line in metaLines)
        {
            TextTop(line, Margin + 14, y, 8, metaColor, bold: tone == ReportCardTone.Warning);
            y += 11;
        }

        y += 4;
        foreach (var line in bodyLines)
        {
            TextTop(line, Margin + 14, y, 9, ReportColor.Muted);
            y += 13;
        }

        CursorY += height + 12;
    }

    public void EnsureSpace(float requiredHeight)
    {
        if (HasSpace(requiredHeight))
        {
            return;
        }

        AddPage();
    }

    public bool HasSpace(float requiredHeight)
    {
        return CursorY + requiredHeight <= PageHeight - BottomMargin;
    }

    public void Text(string text, float x, float yFromTop, float size, ReportColor color, bool bold = false)
    {
        _current.HasContent = true;
        _current.Content.Append(color.TextCommand());
        _current.Content.Append("BT /").Append(bold ? "F2" : "F1").Append(' ')
            .Append(size.ToString("0.##", CultureInfo.InvariantCulture))
            .Append(" Tf ")
            .Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
            .Append((PageHeight - yFromTop).ToString("0.##", CultureInfo.InvariantCulture))
            .Append(" Td (")
            .Append(EscapePdfText(ToPdfSafeText(text)))
            .Append(") Tj ET\n");
    }

    public void TextTop(string text, float x, float yFromTop, float size, ReportColor color, bool bold = false)
    {
        Text(text, x, yFromTop + size, size, color, bold);
    }

    public void TextRight(string text, float rightX, float yFromTop, float size, ReportColor color, bool bold = false)
    {
        var safeText = ToPdfSafeText(text);
        var width = EstimateTextWidth(safeText, size, bold);
        TextTop(safeText, Math.Max(Margin, rightX - width), yFromTop, size, color, bold);
    }

    public void FillRect(float x, float yFromTop, float width, float height, ReportColor color)
    {
        _current.HasContent = true;
        _current.Content.Append(color.FillCommand());
        _current.Content.Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
            .Append((PageHeight - yFromTop - height).ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
            .Append(width.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
            .Append(height.ToString("0.##", CultureInfo.InvariantCulture)).Append(" re f\n");
    }

    public void FillRoundedRect(float x, float yFromTop, float width, float height, float radius, ReportColor color)
    {
        _current.HasContent = true;
        _current.Content.Append(color.FillCommand());
        _current.Content.Append(RoundedRectPath(x, yFromTop, width, height, radius)).Append(" f\n");
    }

    public void StrokeRect(float x, float yFromTop, float width, float height, ReportColor color)
    {
        _current.HasContent = true;
        _current.Content.Append(color.StrokeCommand());
        _current.Content.Append("0.8 w ")
            .Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
            .Append((PageHeight - yFromTop - height).ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
            .Append(width.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
            .Append(height.ToString("0.##", CultureInfo.InvariantCulture)).Append(" re S\n");
    }

    public void StrokeRoundedRect(float x, float yFromTop, float width, float height, float radius, ReportColor color)
    {
        _current.HasContent = true;
        _current.Content.Append(color.StrokeCommand());
        _current.Content.Append("0.7 w ").Append(RoundedRectPath(x, yFromTop, width, height, radius)).Append(" S\n");
    }

    public void StrokeLine(float x1, float y1FromTop, float x2, float y2FromTop, ReportColor color)
    {
        _current.HasContent = true;
        _current.Content.Append(color.StrokeCommand());
        _current.Content.Append("0.6 w ")
            .Append(x1.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
            .Append((PageHeight - y1FromTop).ToString("0.##", CultureInfo.InvariantCulture)).Append(" m ")
            .Append(x2.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
            .Append((PageHeight - y2FromTop).ToString("0.##", CultureInfo.InvariantCulture)).Append(" l S\n");
    }

    private void DrawPageHeader()
    {
        FillRect(0, 0, PageWidth, HeaderHeight, ReportColor.Navy);
        FillRect(0, HeaderHeight - 4, PageWidth, 4, ReportColor.Cyan);
        TextTop("AI VIDEO DETECTION", Margin, 20, 15, ReportColor.White, bold: true);
        TextTop("Video Authenticity Report", Margin, 42, 10, ReportColor.Cyan);
        TextRight(_sectionName, PageWidth - Margin, 32, 9.5f, ReportColor.Cyan, bold: true);
    }

    private void DrawFilledCard(float x, float y, float width, float height, ReportColor fill, ReportColor border)
    {
        FillRoundedRect(x, y, width, height, CardRadius, fill);
        StrokeRoundedRect(x, y, width, height, CardRadius, border);
    }

    private void DrawKeyValueCard(float x, float y, float width, float height, string title, IReadOnlyList<PreparedRow> rows)
    {
        DrawFilledCard(x, y, width, height, ReportColor.White, ReportColor.Border);
        TextTop(title, x + CardPadding, y + 16, 12, ReportColor.Text, bold: true);
        var rowTop = y + 45;
        var rowWidth = width - CardPadding * 2;

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            DrawCenteredTextBlock(row.LabelLines, x + CardPadding, rowTop, width * 0.31f, row.Height, 8, ReportColor.Muted, bold: true);
            DrawCenteredTextBlock(row.ValueLines, x + CardPadding + width * 0.35f, rowTop, width * 0.58f, row.Height, BodyFontSize, ReportColor.Text);
            rowTop += row.Height;
            if (index < rows.Count - 1)
            {
                StrokeLine(x + RowDividerInset, rowTop, x + rowWidth + CardPadding, rowTop, ReportColor.Border);
            }
        }
    }

    private void DrawCenteredTextBlock(
        IReadOnlyList<string> lines,
        float x,
        float rowTop,
        float width,
        float rowHeight,
        float fontSize,
        ReportColor color,
        bool bold = false)
    {
        var lineHeight = Math.Max(fontSize + 3.5f, 11);
        var blockHeight = lines.Count * lineHeight;
        var y = rowTop + Math.Max(0, (rowHeight - blockHeight) / 2);
        foreach (var line in lines)
        {
            TextTop(line, x, y, fontSize, color, bold);
            y += lineHeight;
        }
    }

    private static List<PreparedRow> PrepareRows(IReadOnlyList<(string Label, string Value)> rows, float cardWidth)
    {
        var labelWidth = cardWidth * 0.31f;
        var valueWidth = cardWidth * 0.56f;
        return rows.Select(row =>
        {
            var labelLines = WrapToWidth(row.Label.ToUpperInvariant(), labelWidth, 8, bold: true);
            var valueLines = WrapToWidth(row.Value, valueWidth, BodyFontSize);
            var labelHeight = labelLines.Count * 11f;
            var valueHeight = valueLines.Count * BodyLineHeight;
            var rowHeight = Math.Max(MinRowHeight, Math.Max(labelHeight, valueHeight) + RowVerticalPadding * 2);
            return new PreparedRow(labelLines, valueLines, rowHeight);
        }).ToList();
    }

    private static float CalculateKeyValueCardHeight(IReadOnlyList<PreparedRow> rows)
    {
        return 58 + rows.Sum(row => row.Height) + CardPadding;
    }

    private static (ReportColor Fill, ReportColor Border, ReportColor Text) GetVerdictStyle(AnalysisLabel label)
    {
        return label switch
        {
            AnalysisLabel.LikelyReal => (ReportColor.RealFill, ReportColor.RealBorder, ReportColor.Green),
            AnalysisLabel.LikelyAiGenerated or AnalysisLabel.EditedManipulated => (ReportColor.AiFill, ReportColor.AiBorder, ReportColor.Red),
            AnalysisLabel.Suspicious => (ReportColor.WarningFill, ReportColor.Warning, ReportColor.WarningText),
            _ => (ReportColor.NeutralFill, ReportColor.Border, ReportColor.Text)
        };
    }

    private static (ReportColor Fill, ReportColor Border, ReportColor Title, ReportColor Meta) GetCardTone(ReportCardTone tone)
    {
        return tone switch
        {
            ReportCardTone.Warning => (ReportColor.WarningFill, ReportColor.Warning, ReportColor.WarningText, ReportColor.WarningText),
            _ => (ReportColor.White, ReportColor.Border, ReportColor.Text, ReportColor.Muted)
        };
    }

    private static List<string> WrapToWidth(string text, float maxWidth, float fontSize, bool bold = false)
    {
        var safeText = ToPdfSafeText(text);
        var lines = new List<string>();
        var words = safeText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();

        foreach (var word in words)
        {
            var next = current.Length == 0 ? word : current + " " + word;
            if (EstimateTextWidth(next, fontSize, bold) <= maxWidth)
            {
                current.Clear();
                current.Append(next);
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            if (EstimateTextWidth(word, fontSize, bold) <= maxWidth)
            {
                current.Append(word);
                continue;
            }

            foreach (var chunk in BreakLongWord(word, maxWidth, fontSize, bold))
            {
                if (EstimateTextWidth(chunk, fontSize, bold) > maxWidth)
                {
                    lines.Add(chunk);
                }
                else if (current.Length == 0)
                {
                    current.Append(chunk);
                }
                else
                {
                    lines.Add(current.ToString());
                    current.Clear();
                    current.Append(chunk);
                }
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return lines.Count == 0 ? ["Not available"] : lines;
    }

    private static IEnumerable<string> BreakLongWord(string word, float maxWidth, float fontSize, bool bold)
    {
        var current = new StringBuilder();
        foreach (var character in word)
        {
            var next = current.ToString() + character;
            if (current.Length > 0 && EstimateTextWidth(next, fontSize, bold) > maxWidth)
            {
                yield return current.ToString();
                current.Clear();
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static List<string> Wrap(string text, int maxCharacters)
    {
        var lines = new List<string>();
        var words = ToPdfSafeText(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();

        foreach (var word in words)
        {
            if (current.Length == 0)
            {
                current.Append(word);
                continue;
            }

            if (current.Length + word.Length + 1 > maxCharacters)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            current.Append(current.Length == 0 ? word : " " + word);
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return lines.Count == 0 ? ["Not available"] : lines;
    }

    private static string Trim(string value, int maxCharacters)
    {
        var safe = ToPdfSafeText(value);
        return safe.Length <= maxCharacters ? safe : safe[..Math.Max(0, maxCharacters - 3)] + "...";
    }

    private static string ToPdfSafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Not available";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ReplaceLineEndings(" "))
        {
            builder.Append(character is >= ' ' and <= '~' ? character : '?');
        }

        return builder.ToString();
    }

    private static string EscapePdfText(string text)
    {
        return text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
    }

    private static float EstimateTextWidth(string text, float size, bool bold)
    {
        return text.Length * size * (bold ? 0.58f : 0.52f);
    }

    private static string RoundedRectPath(float x, float yFromTop, float width, float height, float radius)
    {
        var r = Math.Min(radius, Math.Min(width, height) / 2);
        const float k = 0.5522848f;
        var left = x;
        var right = x + width;
        var bottom = PageHeight - yFromTop - height;
        var top = PageHeight - yFromTop;
        var c = r * k;

        static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        return new StringBuilder()
            .Append(F(left + r)).Append(' ').Append(F(bottom)).Append(" m ")
            .Append(F(right - r)).Append(' ').Append(F(bottom)).Append(" l ")
            .Append(F(right - r + c)).Append(' ').Append(F(bottom)).Append(' ')
            .Append(F(right)).Append(' ').Append(F(bottom + r - c)).Append(' ')
            .Append(F(right)).Append(' ').Append(F(bottom + r)).Append(" c ")
            .Append(F(right)).Append(' ').Append(F(top - r)).Append(" l ")
            .Append(F(right)).Append(' ').Append(F(top - r + c)).Append(' ')
            .Append(F(right - r + c)).Append(' ').Append(F(top)).Append(' ')
            .Append(F(right - r)).Append(' ').Append(F(top)).Append(" c ")
            .Append(F(left + r)).Append(' ').Append(F(top)).Append(" l ")
            .Append(F(left + r - c)).Append(' ').Append(F(top)).Append(' ')
            .Append(F(left)).Append(' ').Append(F(top - r + c)).Append(' ')
            .Append(F(left)).Append(' ').Append(F(top - r)).Append(" c ")
            .Append(F(left)).Append(' ').Append(F(bottom + r)).Append(" l ")
            .Append(F(left)).Append(' ').Append(F(bottom + r - c)).Append(' ')
            .Append(F(left + r - c)).Append(' ').Append(F(bottom)).Append(' ')
            .Append(F(left + r)).Append(' ').Append(F(bottom)).Append(" c")
            .ToString();
    }

    private static void DrawFooter(PdfPageContent page, int pageNumber, int pageCount)
    {
        var footer = new StringBuilder();
        footer.Append(ReportColor.Border.StrokeCommand());
        footer.Append("0.6 w 48 42 m 547 42 l S\n");
        footer.Append(ReportColor.Muted.TextCommand());
        footer.Append("BT /F1 8 Tf 48 26 Td (SachAI - probability-based report) Tj ET\n");
        footer.Append("BT /F1 8 Tf 500 26 Td (Page ")
            .Append(pageNumber.ToString(CultureInfo.InvariantCulture))
            .Append(" of ")
            .Append(pageCount.ToString(CultureInfo.InvariantCulture))
            .Append(") Tj ET\n");
        page.Content.Append(footer);
    }

    private sealed class PdfPageContent
    {
        public StringBuilder Content { get; } = new();

        public bool HasContent { get; set; }
    }

    private sealed record PreparedRow(IReadOnlyList<string> LabelLines, IReadOnlyList<string> ValueLines, float Height);
}

internal readonly record struct ReportColor(decimal R, decimal G, decimal B)
{
    public static ReportColor Navy { get; } = new(0.03m, 0.13m, 0.25m);
    public static ReportColor White { get; } = new(1m, 1m, 1m);
    public static ReportColor Text { get; } = new(0.08m, 0.11m, 0.18m);
    public static ReportColor Muted { get; } = new(0.32m, 0.38m, 0.48m);
    public static ReportColor Border { get; } = new(0.81m, 0.87m, 0.94m);
    public static ReportColor Panel { get; } = new(0.97m, 0.99m, 1m);
    public static ReportColor LightBlue { get; } = new(0.93m, 0.97m, 1m);
    public static ReportColor Blue { get; } = new(0.15m, 0.39m, 0.92m);
    public static ReportColor BlueSoft { get; } = new(0.73m, 0.84m, 0.98m);
    public static ReportColor Cyan { get; } = new(0.40m, 0.93m, 1m);
    public static ReportColor Green { get; } = new(0.02m, 0.59m, 0.41m);
    public static ReportColor RealFill { get; } = new(0.92m, 0.99m, 0.96m);
    public static ReportColor RealBorder { get; } = new(0.65m, 0.88m, 0.77m);
    public static ReportColor Red { get; } = new(0.88m, 0.11m, 0.28m);
    public static ReportColor AiFill { get; } = new(1m, 0.94m, 0.95m);
    public static ReportColor AiBorder { get; } = new(0.99m, 0.72m, 0.77m);
    public static ReportColor NeutralFill { get; } = new(0.96m, 0.98m, 1m);
    public static ReportColor LightGray { get; } = new(0.89m, 0.93m, 0.97m);
    public static ReportColor Warning { get; } = new(0.92m, 0.51m, 0.12m);
    public static ReportColor WarningFill { get; } = new(1m, 0.97m, 0.89m);
    public static ReportColor WarningText { get; } = new(0.58m, 0.25m, 0.05m);

    public string FillCommand()
    {
        return $"{Format(R)} {Format(G)} {Format(B)} rg\n";
    }

    public string StrokeCommand()
    {
        return $"{Format(R)} {Format(G)} {Format(B)} RG\n";
    }

    public string TextCommand()
    {
        return FillCommand();
    }

    private static string Format(decimal value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

internal enum ReportCardTone
{
    Default,
    Warning
}
