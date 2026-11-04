using UnityEngine;

namespace ScrewSolver.Config
{
    /// <summary>
    /// Central ScriptableObject holding animation timing and curve parameters.
    /// Can be tuned in the Inspector in real-time during playtests.
    /// </summary>
    [CreateAssetMenu(fileName = "ScrewAnimationConfig", menuName = "ScrewSolver/Animation Config", priority = 1)]
    public sealed class ScrewAnimationConfigSO : ScriptableObject
    {
        [Header("Unscrew Settings (Delikten Çıkış)")]
        [Tooltip("Vidanın delikten dönerek yukarı çıkma süresi (saniye)")]
        [SerializeField, Range(0.05f, 1.0f)] private float _unscrewDuration = 0.22f;

        [Tooltip("Delikten yukarı çıkış mesafesi (yerel Y ekseni)")]
        [SerializeField, Range(0.1f, 2.0f)] private float _unscrewLiftDistance = 0.5f;

        [Tooltip("Dönüş açısı (derece - 360 = 1 tam tur)")]
        [SerializeField, Range(180f, 1440f)] private float _unscrewRotationDegrees = 720f;

        [Header("Flight Settings (Kutuya / Bar'a Uçuş)")]
        [Tooltip("Vidanın delikten hedefe parabolik uçuş süresi (saniye)")]
        [SerializeField, Range(0.1f, 1.5f)] private float _flyDuration = 0.38f;

        [Tooltip("Uçuş sırasındaki parabolik kavis yüksekliği")]
        [SerializeField, Range(0.2f, 5.0f)] private float _arcHeight = 1.8f;

        [Header("Locked Wiggle Settings (Kilitli Vida Geri Bildirimi)")]
        [Tooltip("Kilitli vidaya tıklandığında titreme süresi")]
        [SerializeField, Range(0.05f, 0.5f)] private float _wiggleDuration = 0.20f;

        [Tooltip("Titreme şiddeti (açı)")]
        [SerializeField, Range(2f, 30f)] private float _wiggleStrength = 12f;

        [Header("Global Speed Multiplier")]
        [Tooltip("Tüm animasyonları genel olarak hızlandırma / yavaşlatma çarpanı")]
        [SerializeField, Range(0.2f, 3.0f)] private float _globalSpeedMultiplier = 1.0f;

        // Scaled getters accounting for GlobalSpeedMultiplier
        public float UnscrewDuration => Mathf.Max(0.01f, _unscrewDuration / _globalSpeedMultiplier);
        public float UnscrewLiftDistance => _unscrewLiftDistance;
        public float UnscrewRotationDegrees => _unscrewRotationDegrees;

        public float FlyDuration => Mathf.Max(0.01f, _flyDuration / _globalSpeedMultiplier);
        public float ArcHeight => _arcHeight;

        public float WiggleDuration => Mathf.Max(0.01f, _wiggleDuration / _globalSpeedMultiplier);
        public float WiggleStrength => _wiggleStrength;
        public float GlobalSpeedMultiplier => _globalSpeedMultiplier;
    }
}
