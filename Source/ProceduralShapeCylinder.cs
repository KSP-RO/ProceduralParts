using UnityEngine;
using KSPAPIExtensions;
using System;

namespace ProceduralParts
{

    public class ProceduralShapeCylinder : ProceduralAbstractSoRShape
    {
        private static readonly string ModTag = "[ProceduralShapeCylinder]";
        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Diameter", guiFormat = "F3", guiUnits = "m"),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit="m", useSI = true)]
        public float diameter = 1.25f;
        private float oldDiameter;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Length", guiFormat = "F3", guiUnits = "m"),
		 UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit="m", useSI = true)]
        public float length = 1f;
        private float oldLength;

        public override void OnStart(StartState state)
        {
            UpdateTechConstraints();
        }


        protected override void UpdateShape(bool force)
        {
            // ReSharper disable CompareOfFloatsByEqualityOperator
            if (!force && oldDiameter == diameter && oldLength == length)
                return;
            var volume = CalculateVolume();
            var oldVolume = volume;
            var refreshRequired = false;

            if (HighLogic.LoadedSceneIsEditor)
            {
                // Maxmin the volume.
                if (volume > PPart.volumeMax)
                {
                    volume = PPart.volumeMax;
                }
                else
                    goto nochange;

                refreshRequired = true;
                var excessVol = oldVolume - volume;
                if (oldDiameter != diameter)
                {
                    diameter = TruncateForSlider(Mathf.Sqrt(volume / (0.25f * Mathf.PI * length)), -excessVol);
                }
                else
                {
                    length = TruncateForSlider(volume / (diameter * diameter * 0.25f * Mathf.PI), -excessVol);
                }
                volume = CalculateVolume();
            }
        nochange:
            Volume = volume;
            Vector2 norm = new Vector2(1, 0);

            WriteMeshes(
                new ProfilePoint(diameter, -0.5f * length, 0f, norm),
                new ProfilePoint(diameter, 0.5f * length, 1f, norm)
                );

            oldDiameter = diameter;
            oldLength = length;
            // ReSharper restore CompareOfFloatsByEqualityOperator
            if (refreshRequired)
            {
                RefreshPartEditorWindow();
            }
            UpdateInterops();
        }

        private float CalculateVolume()
        {
            return diameter * diameter * 0.25f * Mathf.PI * length;
        }

        public override void UpdateTechConstraints()
        {
            // ReSharper disable CompareOfFloatsByEqualityOperator
            if (!HighLogic.LoadedSceneIsEditor)
                return;

            if (PPart.lengthMin == PPart.lengthMax)
                Fields["length"].guiActiveEditor = false;
            else
            {
                UI_FloatEdit lengthEdit = (UI_FloatEdit)Fields["length"].uiControlEditor;
                lengthEdit.maxValue = PPart.lengthMax;
                lengthEdit.minValue = PPart.lengthMin;
                lengthEdit.incrementLarge = PPart.lengthLargeStep;
                lengthEdit.incrementSmall = PPart.lengthSmallStep;
                length = Mathf.Clamp(length, PPart.lengthMin, PPart.lengthMax);
            }

            if (PPart.diameterMin == PPart.diameterMax)
                Fields["diameter"].guiActiveEditor = false;
            else
            {
                UI_FloatEdit diameterEdit = (UI_FloatEdit)Fields["diameter"].uiControlEditor;
				if (null != diameterEdit) {
					diameterEdit.maxValue = PPart.diameterMax;
					diameterEdit.minValue = PPart.diameterMin;
					diameterEdit.incrementLarge = PPart.diameterLargeStep;
					diameterEdit.incrementSmall = PPart.diameterSmallStep;
					diameter = Mathf.Clamp (diameter, PPart.diameterMin, PPart.diameterMax);
				} else
					Debug.LogError ("*PP* could not find field 'diameter'");
            }
            // ReSharper restore CompareOfFloatsByEqualityOperator
        }

        public override void UpdateTFInterops()
        {
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam1", diameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam2", diameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "length", length, "ProceduralParts" });
        }
    }
}