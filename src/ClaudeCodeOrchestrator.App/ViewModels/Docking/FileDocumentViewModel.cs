using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeCodeOrchestrator.App.ViewModels.Docking;

/// <summary>
/// Document view model for viewing file contents.
/// </summary>
public partial class FileDocumentViewModel : DocumentViewModelBase
{
    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private bool _isPreview;

    [ObservableProperty]
    private string _language = "text";

    public FileDocumentViewModel()
    {
        Id = Guid.NewGuid().ToString();
        Title = "File";
        CanClose = true;
        CanFloat = true;
    }

    public FileDocumentViewModel(string filePath)
    {
        FilePath = filePath;
        Id = filePath;
        Title = Path.GetFileName(filePath);
        CanClose = true;
        CanFloat = true;

        // Detect language from extension
        Language = GetLanguageFromExtension(Path.GetExtension(filePath));

        // Load file content
        LoadContent();
    }

    private void LoadContent()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                Content = File.ReadAllText(FilePath);
            }
            else
            {
                Content = $"File not found: {FilePath}";
            }
        }
        catch (Exception ex)
        {
            Content = $"Error loading file: {ex.Message}";
        }
    }

    private static string GetLanguageFromExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".axaml" or ".xaml" or ".xml" => "xml",
            ".json" => "json",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".tsx" => "typescript",
            ".jsx" => "javascript",
            ".css" => "css",
            ".scss" => "scss",
            ".html" or ".htm" => "html",
            ".md" => "markdown",
            ".py" => "python",
            ".sh" or ".bash" => "bash",
            ".yaml" or ".yml" => "yaml",
            ".sql" => "sql",
            ".csproj" or ".sln" or ".props" or ".targets" => "xml",
            ".gitignore" or ".editorconfig" => "text",
            _ => "text"
        };
    }

    /// <summary>
    /// Reloads the file content from disk.
    /// </summary>
    public void Reload()
    {
        LoadContent();
    }
}
