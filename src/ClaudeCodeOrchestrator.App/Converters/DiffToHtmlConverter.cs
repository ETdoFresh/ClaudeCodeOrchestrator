using System;
using System.Globalization;
using System.Text;
using Avalonia.Data.Converters;

namespace ClaudeCodeOrchestrator.App.Converters;

public class DiffToHtmlConverter : IMultiValueConverter
{
    private const string DiffCss = @"
        body {
            font-family: 'SF Mono', Monaco, 'Cascadia Code', Consolas, monospace;
            font-size: 12px;
            color: #CCCCCC;
            background-color: transparent;
            margin: 0;
            padding: 0;
            line-height: 1.4;
        }
        .diff-container {
            border-radius: 4px;
            overflow: hidden;
        }
        .diff-line {
            padding: 1px 8px;
            white-space: pre-wrap;
            word-break: break-all;
        }
        .diff-removed {
            background-color: rgba(248, 81, 73, 0.2);
            color: #F85149;
        }
        .diff-added {
            background-color: rgba(63, 185, 80, 0.2);
            color: #3FB950;
        }
        .diff-context {
            color: #8B949E;
        }
    ";

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not string oldString || values[1] is not string newString)
            return string.Empty;

        var html = new StringBuilder();
        html.Append($"<html><head><style>{DiffCss}</style></head><body><div class='diff-container'>");

        // Simple line-based diff
        var oldLines = oldString.Split('\n');
        var newLines = newString.Split('\n');

        // Show removed lines (old)
        foreach (var line in oldLines)
        {
            var escapedLine = System.Net.WebUtility.HtmlEncode(line);
            html.Append($"<div class='diff-line diff-removed'>- {escapedLine}</div>");
        }

        // Show added lines (new)
        foreach (var line in newLines)
        {
            var escapedLine = System.Net.WebUtility.HtmlEncode(line);
            html.Append($"<div class='diff-line diff-added'>+ {escapedLine}</div>");
        }

        html.Append("</div></body></html>");
        return html.ToString();
    }
}

public class CodePreviewConverter : IValueConverter
{
    private const string CodeCss = @"
        body {
            font-family: 'SF Mono', Monaco, 'Cascadia Code', Consolas, monospace;
            font-size: 12px;
            color: #CCCCCC;
            background-color: #1E1E1E;
            margin: 0;
            padding: 8px;
            line-height: 1.4;
            border-radius: 4px;
            max-height: 200px;
            overflow: auto;
        }
        pre {
            margin: 0;
            white-space: pre-wrap;
            word-break: break-all;
        }
    ";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string content || string.IsNullOrEmpty(content))
            return string.Empty;

        // Limit preview to first 50 lines
        var lines = content.Split('\n');
        var preview = lines.Length > 50
            ? string.Join('\n', lines[..50]) + $"\n... ({lines.Length - 50} more lines)"
            : content;

        var escapedContent = System.Net.WebUtility.HtmlEncode(preview);
        return $"<html><head><style>{CodeCss}</style></head><body><pre>{escapedContent}</pre></body></html>";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
