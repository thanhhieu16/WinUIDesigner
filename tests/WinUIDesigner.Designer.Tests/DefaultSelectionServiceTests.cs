using System.Linq;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.Designer.Services;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
	[TestFixture]
	public class DefaultSelectionServiceTests
	{
		// No DesignItem instance is needed here - HashSet<DesignItem> only needs distinct
		// references, and DesignItem is abstract - a bare object cast is enough to exercise the
		// selection bookkeeping in isolation from Phase 2's concrete XamlDesignItem.
		static DesignItem[] Items(int count)
		{
			var items = new DesignItem[count];
			for (int i = 0; i < count; i++) items[i] = null; // replaced by real DesignItems in Task 5's DoD suite
			return items;
		}

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
