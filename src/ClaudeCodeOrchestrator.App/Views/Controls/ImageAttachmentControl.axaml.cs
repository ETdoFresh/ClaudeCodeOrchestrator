using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using ClaudeCodeOrchestrator.App.Models;

namespace ClaudeCodeOrchestrator.App.Views.Controls;

public partial class ImageAttachmentControl : UserControl
{
    public static readonly StyledProperty<ImageAttachment?> AttachmentProperty =
        AvaloniaProperty.Register<ImageAttachmentControl, ImageAttachment?>(nameof(Attachment));

    public ImageAttachment? Attachment
    {
        get => GetValue(AttachmentProperty);
        set => SetValue(AttachmentProperty, value);
    }

    public event EventHandler<ImageAttachment>? RemoveRequested;

    public ImageAttachmentControl()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == AttachmentProperty && change.NewValue is ImageAttachment attachment)
        {
            LoadThumbnail(attachment);
        }
    }

    private void LoadThumbnail(ImageAttachment attachment)
    {
        try
        {
            using var stream = new MemoryStream(attachment.ImageBytes);
            ThumbnailImage.Source = new Bitmap(stream);
        }
        catch
        {
            // Failed to load image, leave empty
        }
    }

    private void RemoveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (Attachment != null)
        {
            RemoveRequested?.Invoke(this, Attachment);
        }
    }
}
