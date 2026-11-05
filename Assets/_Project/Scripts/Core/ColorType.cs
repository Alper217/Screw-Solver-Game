using System;
using UnityEngine;

namespace ScrewSolver.Core
{
    public enum ColorType
    {
        Red = 0,
        Green = 1,
        Blue = 2,
        Yellow = 3,
        Purple = 4,
        Orange = 5,
        Cyan = 6,
        Pink = 7
    }

    public static class ColorTypeExtensions
    {
        public static Color ToUnityColor(this ColorType colorType)
        {
            return colorType switch
            {
                ColorType.Red => new Color(0.92f, 0.28f, 0.28f, 1f),
                ColorType.Green => new Color(0.18f, 0.80f, 0.44f, 1f),
                ColorType.Blue => new Color(0.20f, 0.60f, 1.00f, 1f),
                ColorType.Yellow => new Color(0.95f, 0.77f, 0.06f, 1f),
                ColorType.Purple => new Color(0.61f, 0.35f, 0.71f, 1f),
                ColorType.Orange => new Color(0.90f, 0.49f, 0.13f, 1f),
                ColorType.Cyan => new Color(0.10f, 0.74f, 0.61f, 1f),
                ColorType.Pink => new Color(0.91f, 0.45f, 0.65f, 1f),
                _ => Color.white
            };
        }
    }
}

