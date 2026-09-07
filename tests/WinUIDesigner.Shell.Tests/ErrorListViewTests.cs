using System.Collections.Generic;
using Microsoft.UI.Xaml;
using NUnit.Framework;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Shell.Tests
{
    [TestFixture]
    public class ErrorListViewTests
    {
        static IReadOnlyList<XamlError> Two()
        {
            return new List<XamlError>
            {
                new XamlError { Line = 3, Column = 5, Message = "first problem" },
                new XamlError { Line = 9, Column = 1, Message = "second problem" }
            };
        }

        [Test]
        public void ShowingErrorsMakesTheListVisibleAndOneRowPerError()
        {
            WinUITestHost.Run(() =>
            {
                var view = new ErrorListView();
                Assert.That(view.Visibility, Is.EqualTo(Visibility.Collapsed),
                    "an empty error list must not take space");

                view.Show(Two());

                Assert.That(view.Visibility, Is.EqualTo(Visibility.Visible));
                var rows = (IReadOnlyList<XamlErrorNode>)view.List.ItemsSource;
                Assert.That(rows.Count, Is.EqualTo(2));
                StringAssert.Contains("first problem", rows[0].Description);
                StringAssert.Contains("3", rows[0].Description);
                StringAssert.Contains("5", rows[0].Description);
            });
        }

        [Test]
        public void ShowingNothingCollapsesTheList()
        {
            WinUITestHost.Run(() =>
            {
                var view = new ErrorListView();
                view.Show(Two());
                Assert.That(view.Visibility, Is.EqualTo(Visibility.Visible));

                view.Show(new List<XamlError>());
                Assert.That(view.Visibility, Is.EqualTo(Visibility.Collapsed));

                view.Show(null);
                Assert.That(view.Visibility, Is.EqualTo(Visibility.Collapsed));
            });
        }

        [Test]
        public void TheListIsHeightCapped()
        {
            WinUITestHost.Run(() =>
            {
                var view = new ErrorListView();
                // CLAUDE.md records a measured ListView growing to 6360px inside an unbounded
                // parent. The error row's Height is Auto, which is exactly that parent, so the
                // cap has to live on the list itself.
                Assert.That(view.List.MaxHeight, Is.EqualTo(ErrorListView.MaxListHeight));
            });
        }
    }
}
