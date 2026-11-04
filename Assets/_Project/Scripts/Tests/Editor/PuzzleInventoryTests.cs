using System.Collections.Generic;
using NUnit.Framework;
using ScrewSolver.Core;

namespace ScrewSolver.Tests
{
    [TestFixture]
    public class PuzzleInventoryTests
    {
        private PuzzleInventoryModel _model;

        [SetUp]
        public void Setup()
        {
            _model = new PuzzleInventoryModel(activeBoxCount: 4, bufferCapacity: 5);
        }

        [Test]
        public void TryAddScrew_WhenMatchingActiveBoxExists_RoutesToBox()
        {
            // Arrange: Box 0 requires Red
            _model.Initialize(
                initialActiveBoxColors: new[] { ColorType.Red, ColorType.Blue },
                queuedBoxColors: null
            );

            // Act
            ReservationResult result = _model.TryAddScrew(ColorType.Red);

            // Assert
            Assert.AreEqual(ReservationType.ToTargetBox, result.Type);
            Assert.AreEqual(0, result.TargetBoxIndex);
            Assert.AreEqual(0, result.TargetSlotIndex);
            Assert.IsFalse(result.CausesBoxCompletion);
        }

        [Test]
        public void TryAddScrew_WhenThirdScrewAdded_CausesBoxCompletion()
        {
            // Arrange
            _model.Initialize(
                initialActiveBoxColors: new[] { ColorType.Red },
                queuedBoxColors: null
            );

            _model.TryAddScrew(ColorType.Red); // 1/3
            _model.TryAddScrew(ColorType.Red); // 2/3

            // Act
            ReservationResult result = _model.TryAddScrew(ColorType.Red); // 3/3

            // Assert
            Assert.AreEqual(ReservationType.ToTargetBox, result.Type);
            Assert.AreEqual(2, result.TargetSlotIndex);
            Assert.IsTrue(result.CausesBoxCompletion);
            Assert.AreEqual(0, result.CompletedBoxIndex);
        }

        [Test]
        public void TryAddScrew_WhenNoActiveBoxMatches_RoutesToBuffer()
        {
            // Arrange: Only Red is active
            _model.Initialize(
                initialActiveBoxColors: new[] { ColorType.Red },
                queuedBoxColors: null
            );

            // Act: Yellow screw added
            ReservationResult result = _model.TryAddScrew(ColorType.Yellow);

            // Assert
            Assert.AreEqual(ReservationType.ToBufferDock, result.Type);
            Assert.AreEqual(0, result.BufferIndex);
            Assert.AreEqual(1, _model.BufferDock.Count);
        }

        [Test]
        public void TryAddScrew_WhenBufferFullAndNoBoxMatches_TriggersGameOver()
        {
            // Arrange: Fill 5 buffer slots with Yellow
            _model.Initialize(
                initialActiveBoxColors: new[] { ColorType.Red },
                queuedBoxColors: null
            );

            for (int i = 0; i < 5; i++)
            {
                ReservationResult res = _model.TryAddScrew(ColorType.Yellow);
                Assert.AreEqual(ReservationType.ToBufferDock, res.Type);
            }

            // Act: 6th non-matching screw
            ReservationResult overflowResult = _model.TryAddScrew(ColorType.Blue);

            // Assert
            Assert.AreEqual(ReservationType.GameOverFullBuffer, overflowResult.Type);
            Assert.IsTrue(_model.IsGameOver);
        }

        [Test]
        public void CompleteBoxAndShift_AutomaticallyDrainsMatchingScrewsFromBuffer()
        {
            // Arrange: Active box 0 is Red, Next in queue is Blue.
            // 2 Blue screws are already sitting in buffer.
            _model.Initialize(
                initialActiveBoxColors: new[] { ColorType.Red },
                queuedBoxColors: new[] { ColorType.Blue }
            );

            _model.TryAddScrew(ColorType.Blue); // Goes to buffer (index 0)
            _model.TryAddScrew(ColorType.Blue); // Goes to buffer (index 1)
            Assert.AreEqual(2, _model.BufferDock.Count);

            // Complete the Red box (3 Red screws)
            _model.TryAddScrew(ColorType.Red);
            _model.TryAddScrew(ColorType.Red);
            _model.TryAddScrew(ColorType.Red);

            // Act: Complete box 0 and shift queue to Blue
            List<BufferDrainResult> drains = new List<BufferDrainResult>();
            bool shifted = _model.CompleteBoxAndShift(0, drains);

            // Assert
            Assert.IsTrue(shifted);
            Assert.AreEqual(ColorType.Blue, _model.ActiveBoxes[0].RequiredColor);
            Assert.AreEqual(2, drains.Count, "Both Blue screws should be drained from buffer into the new box.");
            Assert.AreEqual(0, _model.BufferDock.Count, "Buffer dock should be completely emptied.");
            Assert.AreEqual(2, _model.ActiveBoxes[0].CurrentCount, "New Blue box should now have 2/3 screws.");
        }

        [Test]
        public void CheckWinCondition_WhenBoardEmptyAndBufferEmpty_ReturnsTrue()
        {
            _model.Initialize(
                initialActiveBoxColors: new[] { ColorType.Red },
                queuedBoxColors: null
            );

            bool won = _model.CheckWinCondition(remainingScrewsOnBoard: 0);
            Assert.IsTrue(won);
            Assert.IsTrue(_model.IsGameWon);
        }
    }
}
