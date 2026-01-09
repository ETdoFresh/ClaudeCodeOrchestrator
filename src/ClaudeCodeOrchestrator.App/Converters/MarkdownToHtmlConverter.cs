using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Markdig;

namespace ClaudeCodeOrchestrator.App.Converters;

public class MarkdownToHtmlConverter : IValueConverter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private const string DarkThemeCss = @"
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            font-size: 13px;
            color: #CCCCCC;
            background-color: transparent;
            margin: 0;
            padding: 0;
            line-height: 1.5;
        }
        h1, h2, h3, h4, h5, h6 {
            color: #569CD6;
            margin-top: 0.5em;
            margin-bottom: 0.3em;
            font-weight: 600;
        }
        h1 { font-size: 1.5em; }
        h2 { font-size: 1.3em; }
        h3 { font-size: 1.1em; }
        code {
            font-family: 'SF Mono', Monaco, 'Cascadia Code', Consolas, monospace;
            background-color: #2D2D30;
            color: #CE9178;
            padding: 2px 5px;
            border-radius: 3px;
            font-size: 0.9em;
        }
        pre {
            background-color: #2D2D30;
            padding: 10px;
            border-radius: 4px;
            overflow-x: auto;
            margin: 0.5em 0;
        }
        pre code {
            background-color: transparent;
            padding: 0;
        }
        a {
            color: #4EC9B0;
            text-decoration: none;
        }
        a:hover {
            text-decoration: underline;
        }
        blockquote {
            border-left: 3px solid #858585;
            margin: 0.5em 0;
            padding-left: 10px;
            color: #858585;
        }
        ul, ol {
            margin: 0.5em 0;
            padding-left: 1.5em;
        }
        li {
            margin: 0.2em 0;
        }
        strong {
            font-weight: 600;
            color: #FFFFFF;
        }
        em {
            font-style: italic;
        }
        hr {
            border: none;
            border-top: 1px solid #3C3C3C;
            margin: 1em 0;
        }
        table {
            border-collapse: collapse;
            margin: 0.5em 0;
        }
        th, td {
            border: 1px solid #3C3C3C;
            padding: 6px 10px;
        }
        th {
            background-color: #252526;
        }
    ";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string markdown || string.IsNullOrEmpty(markdown))
            return string.Empty;

        var html = Markdown.ToHtml(markdown, Pipeline);
        return $"<html><head><style>{DarkThemeCss}</style></head><body>{html}</body></html>";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
