using Eto.Drawing;
using Eto.Forms;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Eto.Test.WinUI
{
	/// <summary>
	/// An empty window that can be used on its own or navigated to within a Frame.
	/// </summary>
	public class MainWindow : Eto.Forms.Form
	{
		public MainWindow()
		{
			Content = new TableLayout
			{
				Rows = {
					new TableRow(new Label { Text = "This is an Eto.Forms Label" }, new Label { Text = "Second Column" }),
					new Label { Text = "Second Row" }
				}
			};

		}
	}
}
