using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            if (!HighLogic.LoadedSceneIsEditor)
                return;

            if (pPart.lengthMin == pPart.lengthMax)
                Fields["length"].guiActiveEditor = false;
            else
            {
                UI_FloatEdit lengthEdit = (UI_FloatEdit)Fields["length"].uiControlEditor;
                lengthEdit.maxValue = pPart.lengthMax;
                lengthEdit.minValue = pPart.lengthMin;
                lengthEdit.incrementLarge = pPart.lengthLargeStep;
                lengthEdit.incrementSmall = pPart.lengthSmallStep;
            }

            if (pPart.diameterMin == pPart.diameterMax)
                Fields["diameter"].guiActiveEditor = false;
            else
            {
                UI_FloatEdit diameterEdit = (UI_FloatEdit)Fields["diameter"].uiControlEditor;
                diameterEdit.maxValue = pPart.diameterMax;
                diameterEdit.minValue = pPart.diameterMin;
                diameterEdit.incrementLarge = pPart.diameterLargeStep;
                diameterEdit.incrementSmall = pPart.diameterSmallStep;
            }
        }


        protected override void UpdateShape(bool force)
        {
            if (!force && oldDiameter == diameter && oldLength == length)
                return;

            volume = diameter * diameter * 0.25f * Mathf.PI * length;

            if (HighLogic.LoadedSceneIsEditor)
            {
                // Maxmin the volume.
                if (volume > pPart.volumeMax)
                    volume = pPart.volumeMax;
                else if (volume < pPart.volumeMin)
                    volume = pPart.volumeMin;
                else
                    goto nochange;

                if (oldDiameter != diameter)
                    diameter = Mathf.Sqrt(volume / (0.25f * Mathf.PI * length));
                else
                    length = volume / (diameter * diameter * 0.25f * Mathf.PI);
            }
        nochange:

            Vector2 norm = new Vector2(1, 0);

            WriteMeshes(
                new ProfilePoint(diameter, -0.5f * length, 0f, norm),
                new ProfilePoint(diameter, 0.5f * length, 1f, norm)
                );

            oldDiameter = diameter;
            oldLength = length;
        }
    }
}