using KSPAPIExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ProceduralParts
{
    public class ProceduralShapePrism : ProceduralAbstractSoRShape
    {
        #region Properties (fields)

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Diameter", guiFormat = "F3", guiUnits = "m"),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = 0.001f, sigFigs = 3, unit = "m", useSI = true)]
        public float diameter = 1.25f;
        private float oldDiameter;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Length", guiFormat = "F3", guiUnits = "m"),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = 0.001f, sigFigs = 3, unit = "m", useSI = true)]
        public float length = 1f;
        private float oldLength;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Sides", guiFormat = "0"),
         UI_FloatEdit(scene = UI_Scene.Editor)]
        public float sides = 6f;
        private float oldSides;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Mode:"),
         UI_Toggle(disabledText = "Inscribed", enabledText = "Circumscribed")]
        public bool isInscribed = false;
        private bool oldIsInscribed;

        #endregion

        private static float GetRealOuterDiam(bool inscribed, float diam, int sides)
        {
            if (!inscribed)
                return diam;
            float theta = (Mathf.PI * 2f) / (float)sides;
            float radius = diam / 2f;
            return 2f * (radius / Mathf.Cos(theta / 2f));
        }

        private float CalcVolume()
        {
            
            float theta = (Mathf.PI * 2f) / (float)sides;
            float radius = diameter / 2f;

            float tHeight = isInscribed ? radius : radius * Mathf.Cos(theta / 2f);
            float tBase = 2f * tHeight * Mathf.Tan(theta / 2f);

            return ((tHeight * tBase / 2f) * sides) * length;
        }

        private float MaxMinVolume()
        {
            Volume = CalcVolume();

            if (Volume > PPart.volumeMax)
            {
                float excess = Volume - PPart.volumeMax;
                Volume = PPart.volumeMax;
                return excess;
            }
            if (Volume < PPart.volumeMin)
            {
                float excess = Volume - PPart.volumeMin;
                Volume = PPart.volumeMin;
                return excess;
            }
            return 0;
        }

        protected override void UpdateShape(bool force)
        {
            if (!force &&
                oldDiameter == diameter &&
                oldLength == length &&
                oldSides == sides &&
                oldIsInscribed == isInscribed)
                return;

            if (HighLogic.LoadedSceneIsFlight)
            {
                Volume = CalcVolume();
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {


            }

            //var realDiam = GetRealOuterDiam(isInscribed, diameter, (int)sides);
            //var oldRealDiam = GetRealOuterDiam(oldIsInscribed, oldDiameter, (int)oldSides);



            oldDiameter = diameter;
            oldLength = length;
            oldSides = sides;
            oldIsInscribed = isInscribed;

            UpdateInterops();
        }

        #region Mesh Generation

        private static UncheckedMesh CreatePrismMesh(int sides, float diameter, float length, bool inscribed)
        {
            var realDiam = GetRealOuterDiam(inscribed, diameter, sides);
            var mesh = new UncheckedMesh(sides * 2 + 2, sides * 2);
            float theta = (Mathf.PI * 2f) / (float)sides;
            float radius = realDiam / 2;
            //vertices/normals/tangents/uvs
            for (int s = 0; s <= sides; s++)
            {
                float posX = Mathf.Cos(theta * s - (theta/2f));
                float posZ = -Mathf.Sin(theta * s - (theta / 2f));
                //top
                mesh.verticies[s] = new Vector3(posX * radius, -0.5f * length, posZ * radius);
                mesh.normals[s] = new Vector3(posX, 0, posZ).normalized;
                mesh.tangents[s] = new Vector4(-posZ, 0, posX, -1).normalized;
                mesh.uv[s] = new Vector2((1f / (float)sides) * s, 0f);
                //botom
                mesh.verticies[s + sides + 1] = new Vector3(posX * radius, 0.5f * length, posZ * radius);
                mesh.normals[s + sides + 1] = mesh.normals[s];
                mesh.tangents[s + sides + 1] = mesh.tangents[s];
                mesh.uv[s + sides + 1] = new Vector2((1f / (float)sides) * s, 0f);
            }
            //triangles
            for (int s = 0; s < sides; s++)
            {

            }

            return mesh;
        }

        #endregion

        public override void UpdateTechConstraints()
        {
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
                if (null != diameterEdit)
                {
                    diameterEdit.maxValue = PPart.diameterMax;
                    diameterEdit.minValue = PPart.diameterMin;
                    diameterEdit.incrementLarge = PPart.diameterLargeStep;
                    diameterEdit.incrementSmall = PPart.diameterSmallStep;
                    diameter = Mathf.Clamp(diameter, PPart.diameterMin, PPart.diameterMax);
                }
                else
                    Debug.LogError("*PP* could not find field 'diameter'");
            }


            UI_FloatEdit sidesEdit = (UI_FloatEdit)Fields["sides"].uiControlEditor;
            sidesEdit.maxValue = 12f;
            sidesEdit.minValue = 3f;
            sidesEdit.incrementLarge = 1f;
            sidesEdit.incrementSmall = 0f;
            sidesEdit.incrementSlide = 1f;
            sides = Mathf.Clamp(sides, sidesEdit.minValue, sidesEdit.maxValue);

            //UI_Toggle polygonModeEdit = (UI_Toggle)Fields["isInscribed"].uiControlEditor;
            //isInscribed = polygonModeEdit.controlEnabled;
        }
        

        public override void UpdateTFInterops()
        {
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam1", diameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam2", sides, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "length", length, "ProceduralParts" });
        }
    }
}
