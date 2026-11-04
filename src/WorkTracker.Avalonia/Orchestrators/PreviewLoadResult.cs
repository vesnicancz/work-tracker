using WorkTracker.Avalonia.ViewModels;

namespace WorkTracker.Avalonia.Orchestrators;

public record PreviewLoadResult(List<WorklogPreviewItem> Items, int TotalSeconds, int DataItemCount);
