using UnityEngine;
using UnityEngine.Profiling;

namespace ProceduralParts
{
    public class ProceduralShapeCylinder : ProceduralAbstractSoRShape
    {
        private const string ModTag = "[ProceduralShapeCylinder]";
        #region Config parameters

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Diameter", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit="m", useSI = true)]
        public float diameter = 1.25f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Length", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit="m", useSI = true)]
        public float length = 1f;

        #endregion

        public override string ShapeKey => $"PP-Cyl|{diameter}|{length}";

        #region Initialization

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (HighLogic.LoadedSceneIsEditor)
            {
                UpdateTechConstraints();
                Fields[nameof(diameter)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;
                Fields[nameof(length)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;
            }
        }

        public override void UpdateTechConstraints()
        {
            Fields[nameof(length)].guiActiveEditor = PPart.lengthMin != PPart.lengthMax;
            UI_FloatEdit lengthEdit = Fields[nameof(length)].uiControlEditor as UI_FloatEdit;
            lengthEdit.incrementLarge = PPart.lengthLargeStep;
            lengthEdit.incrementSmall = PPart.lengthSmallStep;

            Fields[nameof(diameter)].guiActiveEditor = PPart.diameterMin != PPart.diameterMax;
            UI_FloatEdit diameterEdit = Fields[nameof(diameter)].uiControlEditor as UI_FloatEdit;
            diameterEdit.incrementLarge = PPart.diameterLargeStep;
            diameterEdit.incrementSmall = PPart.diameterSmallStep;

            AdjustDimensionBounds();
            length = Mathf.Clamp(length, lengthEdit.minValue, lengthEdit.maxValue);
            diameter = Mathf.Clamp(diameter, diameterEdit.minValue, diameterEdit.maxValue);
        }

        #endregion

        #region Update handlers

        internal override void UpdateShape(bool force = true)
        {
            Profiler.BeginSample("UpdateShape Cyl");
            part.CoMOffset = CoMOffset;
            MaxDiameter = MinDiameter = diameter;
            InnerMaxDiameter = InnerMinDiameter = -1f;
            Length = length;
            NominalVolume = CalculateVolume();
            Volume = NominalVolume;
            Vector2 norm = new Vector2(1, 0);

            WriteMeshes(
                new ProfilePoint(diameter, -0.5f * length, 0f, norm),
                new ProfilePoint(diameter, 0.5f * length, 1f, norm)
                );
            Profiler.EndSample();
        }

        public override void AdjustDimensionBounds()
        {
            // V = 1/4 * pi * L * (d*d)
            // L = 4*V / pi * (d*d)
            // D = sqrt(4*V/pi / L)
            float t = PPart.volumeMax * 4f / Mathf.PI;
            float tMin = PPart.volumeMin * 4f / Mathf.PI;

            float maxDiameter = Mathf.Sqrt(t / length);
            float maxLength = t / (diameter * diameter);
            float minDiameter = Mathf.Sqrt(tMin / length);
            float minLength = tMin / (diameter * diameter);

            maxLength = Mathf.Clamp(maxLength, PPart.lengthMin, PPart.lengthMax);
            maxDiameter = Mathf.Clamp(maxDiameter, PPart.diameterMin, PPart.diameterMax);

            minLength = Mathf.Clamp(minLength, PPart.lengthMin, PPart.lengthMax - PPart.lengthSmallStep);
            minDiameter = Mathf.Clamp(minDiameter, PPart.diameterMin, PPart.diameterMax - PPart.diameterSmallStep);

            (Fields[nameof(diameter)].uiControlEditor as UI_FloatEdit).maxValue = maxDiameter;
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).maxValue = maxLength;
            (Fields[nameof(diameter)].uiControlEditor as UI_FloatEdit).minValue = minDiameter;
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).minValue = minLength;
        }

        public override float CalculateVolume() => CalculateVolume(length, diameter);
        public static float CalculateVolume(float length, float diameter)
        {
            return diameter * diameter * 0.25f * Mathf.PI * length;
        }

        public override bool SeekVolume(float targetVolume, int dir) => SeekVolume(targetVolume, Fields[nameof(length)], dir);

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