using System.Windows;
using System.Windows.Controls;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.WPF.Selectors;

/// <summary>
/// Template selector for worklog preview items - separates date headers from work entries
/// </summary>
public class WorklogPreviewItemTemplateSelector : DataTemplateSelector
{
	public DataTemplate? DateHeaderTemplate { get; set; }
	public DataTemplate? WorkEntryTemplate { get; set; }

	public override DataTemplate? SelectTemplate(object item, DependencyObject container)
	{
		if (item is WorklogPreviewItem previewItem)
		{
			return previewItem.IsDateHeader ? DateHeaderTemplate : WorkEntryTemplate;
		}

		return base.SelectTemplate(item, container);
	}
}