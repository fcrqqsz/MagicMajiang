using MahjongGame.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace MahjongGame.UI
{
    public sealed class AlienationDialElement : VisualElement
    {
        private float _fill01;
        private DeckEditorBudgetTone _tone;

        public AlienationDialElement()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.right = 0;
            style.bottom = 0;
            generateVisualContent += Draw;
        }

        public void SetValue(float fill01, DeckEditorBudgetTone tone)
        {
            _fill01 = Mathf.Clamp01(fill01);
            _tone = tone;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            Vector2 center = contentRect.center;
            float radius = Mathf.Max(0f, Mathf.Min(contentRect.width, contentRect.height) * 0.5f - 8f);
            if (radius <= 0f) return;

            Painter2D painter = context.painter2D;
            painter.lineWidth = 10f;
            painter.strokeColor = new Color32(45, 60, 79, 255);
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(-90f), Angle.Degrees(270f));
            painter.Stroke();

            painter.strokeColor = _tone == DeckEditorBudgetTone.OverLimit
                ? new Color32(255, 107, 107, 255)
                : _tone == DeckEditorBudgetTone.NearLimit
                    ? new Color32(233, 194, 103, 255)
                    : new Color32(0, 173, 181, 255);
            painter.BeginPath();
            painter.Arc(
                center,
                radius,
                Angle.Degrees(-90f),
                Angle.Degrees(-90f + 360f * _fill01));
            painter.Stroke();
        }
    }
}
