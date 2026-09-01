using System;
using NUnit.Framework;
using WinUIDesigner.DesignModel.Services;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class UndoServiceTests
    {
        [Test]
        public void NewServiceCannotUndoOrRedo()
        {
            var undoService = new UndoService();
            Assert.IsFalse(undoService.CanUndo);
            Assert.IsFalse(undoService.CanRedo);
        }

        [Test]
        public void UndoOnEmptyStackThrows()
        {
            var undoService = new UndoService();
            Assert.Throws<InvalidOperationException>(() => undoService.Undo());
        }

        [Test]
        public void RedoOnEmptyStackThrows()
        {
            var undoService = new UndoService();
            Assert.Throws<InvalidOperationException>(() => undoService.Redo());
        }

        [Test]
        public void ClearOnEmptyServiceDoesNotThrow()
        {
            var undoService = new UndoService();
            Assert.DoesNotThrow(() => undoService.Clear());
        }
    }
}
