using System;
using UnityEngine;

namespace ScrewSolver.Core
{
    /// <summary>
    /// Identifies screw and target box colors in the puzzle.
    /// </summary>
    public enum ColorType
    {
        None = 0,
        Red = 1,
        Blue = 2,
        Green = 3,
        Yellow = 4,
        Orange = 5,
        Purple = 6,
        Pink = 7,
        Cyan = 8
    }

    /// <summary>
    /// Color utility extensions for mapping ColorType to RGBA Unity colors.
    /// </summary>
    public static class ColorTypeExtensions
    {
        public static Color ToUnityColor(this ColorType colorType)
        {
            return colorType switch
            {
                ColorType.Red => new Color(0.92f, 0.24f, 0.24f, 1f),
                ColorType.Blue => new Color(0.20f, 0.55f, 0.95f, 1f),
                ColorType.Green => new Color(0.22f, 0.78f, 0.35f, 1f),
                ColorType.Yellow => new Color(0.98f, 0.82f, 0.18f, 1f),
                ColorType.Orange => new Color(0.98f, 0.55f, 0.15f, 1f),
                ColorType.Purple => new Color(0.65f, 0.32f, 0.88f, 1f),
                ColorType.Pink => new Color(0.95f, 0.40f, 0.70f, 1f),
                ColorType.Cyan => new Color(0.18f, 0.85f, 0.88f, 1f),
                _ => Color.white
            };
        }
    }
}
