using UnityEngine;
using KSPAPIExtensions;

namespace ProceduralParts
{

    public class ProceduralShapeCylinder : ProceduralAbstractSoRShape
    {

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Diameter", guiFormat = "S4", guiUnits = "m"),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = 0.001f)]
        public float diameter = 1.25f;
        private float oldDiameter;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Length", guiFormat = "S4", guiUnits = "m"),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = 0.001f)]
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

            Volume = diameter * diameter * 0.25f * Mathf.PI * length;

            if (HighLogic.LoadedSceneIsEditor)
            {
                // Maxmin the volume.
                if (Volume > PPart.volumeMax)
                    Volume = PPart.volumeMax;
                else if (Volume < PPart.volumeMin)
                    Volume = PPart.volumeMin;
                else
                    goto nochange;

                if (oldDiameter != diameter)
                    diameter = Mathf.Sqrt(Volume / (0.25f * Mathf.PI * length));
                else
                    length = Volume / (diameter * diameter * 0.25f * Mathf.PI);
            }
        nochange:

            Vector2 norm = new Vector2(1, 0);

            WriteMeshes(
                new ProfilePoint(diameter, -0.5f * length, 0f, norm),
                new ProfilePoint(diameter, 0.5f * length, 1f, norm)
                );

            oldDiameter = diameter;
            oldLength = length;
            // ReSharper restore CompareOfFloatsByEqualityOperator

            UpdateFAR();
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
                diameterEdit.maxValue = PPart.diameterMax;
                diameterEdit.minValue = PPart.diameterMin;
                diameterEdit.incrementLarge = PPart.diameterLargeStep;
                diameterEdit.incrementSmall = PPart.diameterSmallStep;
                diameter = Mathf.Clamp(diameter, PPart.diameterMin, PPart.diameterMax);
            }
            // ReSharper restore CompareOfFloatsByEqualityOperator
        }
    }
}