using System.Collections.Generic;
using UnityEngine;

namespace ProceduralParts
{
    public class ProceduralShapeCylinder : ProceduralAbstractSoRShape
    {
        private static readonly string ModTag = "[ProceduralShapeCylinder]";
        #region Config parameters

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Diameter", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit="m", useSI = true)]
        public float diameter = 1.25f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Length", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit="m", useSI = true)]
        public float length = 1f;

        #endregion

        #region Initialization

        public override void OnStart(StartState state)
        {
            UpdateTechConstraints();
            base.OnStart(state);

            Fields[nameof(diameter)].uiControlEditor.onFieldChanged =
                new Callback<BaseField, object>(OnShapeDimensionChanged);

            Fields[nameof(length)].uiControlEditor.onFieldChanged =
                new Callback<BaseField, object>(OnShapeDimensionChanged);
        }

        public override void UpdateTechConstraints()
        {
            Fields[nameof(length)].guiActiveEditor = PPart.lengthMin != PPart.lengthMax;
            UI_FloatEdit lengthEdit = Fields[nameof(length)].uiControlEditor as UI_FloatEdit;
            lengthEdit.maxValue = PPart.lengthMax;
            lengthEdit.minValue = PPart.lengthMin;
            lengthEdit.incrementLarge = PPart.lengthLargeStep;
            lengthEdit.incrementSmall = PPart.lengthSmallStep;
            length = Mathf.Clamp(length, PPart.lengthMin, PPart.lengthMax);

            Fields[nameof(diameter)].guiActiveEditor = PPart.diameterMin != PPart.diameterMax;
            UI_FloatEdit diameterEdit = Fields[nameof(diameter)].uiControlEditor as UI_FloatEdit;
            diameterEdit.maxValue = PPart.diameterMax;
            diameterEdit.minValue = PPart.diameterMin;
            diameterEdit.incrementLarge = PPart.diameterLargeStep;
            diameterEdit.incrementSmall = PPart.diameterSmallStep;
            diameter = Mathf.Clamp(diameter, PPart.diameterMin, PPart.diameterMax);
        }

        #endregion

        #region Update handlers

        internal override void UpdateShape(bool force = true)
        {
            Volume = CalculateVolume();
            Vector2 norm = new Vector2(1, 0);

            WriteMeshes(
                new ProfilePoint(diameter, -0.5f * length, 0f, norm),
                new ProfilePoint(diameter, 0.5f * length, 1f, norm)
                );
        }

        public override void AdjustDimensionBounds()
        {
            if (float.IsPositiveInfinity(PPart.volumeMax)) return;

            // V = 1/4 * pi * L * (d*d)
            // L = 4*V / pi * (d*d)
            // D = sqrt(4*V/pi / L)
            float t = PPart.volumeMax * 4f / Mathf.PI;
            float maxDiameter = PPart.diameterMax;
            float maxLength = PPart.lengthMax;

            if (CalculateVolume(length, PPart.diameterMax) > PPart.volumeMax)
            {
                maxDiameter = Mathf.Sqrt(t / length);
            }
            if (CalculateVolume(PPart.lengthMax, diameter) > PPart.volumeMax)
            {
                maxLength = t / (diameter * diameter);
            }
            (Fields[nameof(diameter)].uiControlEditor as UI_FloatEdit).maxValue = maxDiameter;
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).maxValue = maxLength;
        }

        public override float CalculateVolume() => CalculateVolume(length, diameter);
        public float CalculateVolume(float length, float diameter)
        {
            return diameter * diameter * 0.25f * Mathf.PI * length;
        }

        public override void UpdateTFInterops()
        {
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam1", diameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam2", diameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "length", length, "ProceduralParts" });
        }

        public override void TranslateAttachmentsAndNodes(BaseField f, object obj)
        {
            if (f.name == nameof(diameter) && obj is float oldDiameter)
            {
                HandleDiameterChange((float)f.GetValue(this), oldDiameter);
            }
            if (f.name == nameof(length) && obj is float oldLen)
            {
                HandleLengthChange((float)f.GetValue(this), oldLen);
            }
        }

        internal override void InitializeAttachmentNodes() => InitializeAttachmentNodes(length, diameter);

        public override void NormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r /= (diameter / 2);
            coords.y /= length;
        }

        public override void UnNormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r *= (diameter / 2);
            coords.y *= length;
        }

        #endregion
    }
}