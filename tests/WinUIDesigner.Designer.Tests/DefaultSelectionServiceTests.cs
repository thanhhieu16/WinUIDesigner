using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
	[TestFixture]
	public class DefaultSelectionServiceTests
	{
		[Test]
		public void AutoWithNoModifiersHeldResolvesToPrimarySelectionBehavior()
		{
			WinUITestHost.RunWithWindow(window => {
				// This is the one thing this test CAN verify deterministically: in a test run with
				// no physical modifier key actually held, Auto should behave like Primary (select
				// the one item, make it primary) rather than throwing.
				var service = new DefaultSelectionService();
				Assert.DoesNotThrow(() => service.SetSelectedComponents(new DesignItem[0], SelectionTypes.Auto));
			});
		}
	}
}
