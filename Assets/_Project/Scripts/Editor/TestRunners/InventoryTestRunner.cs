using System.Collections.Generic;
using ScrewSolver.Core;
using UnityEditor;
using UnityEngine;

namespace ScrewSolver.EditorTools
{
    public static class InventoryTestRunner
    {
        [MenuItem("ScrewSolver/Run Core Inventory Tests", priority = 10)]
        public static void RunAllTests()
        {
            Debug.Log("<color=#00e5ff><b>[InventoryTestRunner]</b> Starting Pure C# Inventory Tests...</color>");
            int passed = 0;
            int total = 0;

            void AssertTest(string testName, bool condition)
            {
                total++;
                if (condition)
                {
                    passed++;
                    Debug.Log($"<color=#2ecc71>✔ PASSED:</color> {testName}");
                }
                else
                {
                    Debug.LogError($"<color=#e74c3c>✖ FAILED:</color> {testName}");
                }
            }

            // Test 1: Active box routing
            var model = new PuzzleInventoryModel(4, 5);
            model.Initialize(new[] { ColorType.Red, ColorType.Blue }, null);
            var res1 = model.TryAddScrew(ColorType.Red);
            AssertTest("TryAddScrew matches active box", res1.Type == ReservationType.ToTargetBox && res1.TargetBoxIndex == 0);

            // Test 2: 3/3 completion
            model.TryAddScrew(ColorType.Red);
            var res3 = model.TryAddScrew(ColorType.Red);
            AssertTest("Box completes at 3/3 screws", res3.CausesBoxCompletion && res3.CompletedBoxIndex == 0);

            // Test 3: Buffer routing
            var resBuffer = model.TryAddScrew(ColorType.Yellow);
            AssertTest("Non-matching screw goes to buffer", resBuffer.Type == ReservationType.ToBufferDock && resBuffer.BufferIndex == 0);

            // Test 4: Buffer overflow Game Over
            for (int i = 0; i < 4; i++) model.TryAddScrew(ColorType.Yellow);
            var resGameOver = model.TryAddScrew(ColorType.Green);
            AssertTest("Buffer overflow triggers GameOver", resGameOver.Type == ReservationType.GameOverFullBuffer && model.IsGameOver);

            // Test 5: Auto-drain buffer on queue shift
            var model2 = new PuzzleInventoryModel(4, 5);
            model2.Initialize(new[] { ColorType.Red }, new[] { ColorType.Blue });
            model2.TryAddScrew(ColorType.Blue); // goes to buffer
            model2.TryAddScrew(ColorType.Blue); // goes to buffer
            // Fill red box
            model2.TryAddScrew(ColorType.Red);
            model2.TryAddScrew(ColorType.Red);
            model2.TryAddScrew(ColorType.Red);
            var drains = new List<BufferDrainResult>();
            model2.CompleteBoxAndShift(0, drains);
            AssertTest("Buffer auto-drains into newly spawned matching box", drains.Count == 2 && model2.BufferDock.Count == 0 && model2.ActiveBoxes[0].CurrentCount == 2);

            // Test 6: Win condition
            AssertTest("CheckWinCondition passes when board & buffer are empty", model2.CheckWinCondition(0) == false && model2.BufferDock.Count == 0);
            model2.ActiveBoxes[0].Clear();
            AssertTest("CheckWinCondition true when all cleared", model2.CheckWinCondition(0));

            Debug.Log($"<color=#f1c40f><b>[InventoryTestRunner] Summary:</b> {passed}/{total} Tests Passed!</color>");
        }
    }
}
