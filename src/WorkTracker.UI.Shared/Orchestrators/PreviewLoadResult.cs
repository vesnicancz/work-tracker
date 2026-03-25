using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Orchestrators;

public record PreviewLoadResult(List<WorklogPreviewItem> Items, int TotalSeconds, int DataItemCount);