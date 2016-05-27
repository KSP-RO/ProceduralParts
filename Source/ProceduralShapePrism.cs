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
        public float sides = 4f;
        private float oldSides;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Mode:"),
         UI_Toggle(disabledText = "Circumscribed", enabledText = "Inscribed")]
        public bool isInscribed = false;
        private bool oldIsInscribed;

        #endregion

        public override void OnStart(StartState state)
        {
            UpdateTechConstraints();
        }

        private static float GetRealOuterDiam(bool inscribed, float diam, int sides)
        {
            if (!inscribed)
                return diam;
            float theta = (Mathf.PI * 2f) / (float)sides;
            return diam / Mathf.Cos(theta / 2f);
        }

        private static float GetRealInnerDiam(bool inscribed, float diam, int sides)
        {
            if (inscribed)
                return diam;
            float theta = (Mathf.PI * 2f) / (float)sides;
            return diam * Mathf.Cos(theta / 2f);
        }

        private static float GetSideLength(bool inscribed, float diam, int sides)
        {
            float theta = (Mathf.PI * 2f) / (float)sides;
            var rad = GetRealInnerDiam(inscribed, diam, sides) / 2f;
            return 2f * rad * Mathf.Tan(theta / 2f);
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

            Volume = CalcVolume();

            Vector2 norm = new Vector2(1, 0);
            var realDiam = GetRealOuterDiam(isInscribed, diameter, (int)sides);

            var sideLen = GetSideLength(isInscribed, diameter, (int)sides);
            
            RaiseChangeTextureScale("sides", PPart.SidesMaterial, new Vector2(sideLen * sides * 2f, length));

            UpdateMeshNodesSizes(
                new ProfilePoint(realDiam, -0.5f * length, 0f, norm),
                new ProfilePoint(realDiam, 0.5f * length, 1f, norm)
                );

            var sideMesh = CreatePrismMesh((int)sides, diameter, length, isInscribed);
            var endsMesh = CreatePismEnds((int)sides, diameter, length, isInscribed);
            WriteMeshes(sideMesh, endsMesh, null);

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
            var bOffset = sides + 1;
            var alignOffset = theta / 2f;//angle offset so that the prism side face the camera

            //vertices/normals/tangents/uvs
            for (int s = 0; s <= sides; s++)
            {
                float posX = Mathf.Cos(theta * s - alignOffset);
                float posZ = -Mathf.Sin(theta * s - alignOffset);
                float tanX = Mathf.Cos(theta * s - alignOffset + Mathf.PI / 2f);
                float tanZ = -Mathf.Sin(theta * s - alignOffset + Mathf.PI / 2f);
                //top
                mesh.verticies[s] = new Vector3(posX * radius, -0.5f * length, posZ * radius);
                mesh.normals[s] = new Vector3(posX, 0, posZ);
                mesh.tangents[s] = new Vector4(tanX, 0, tanZ, -1);
                mesh.uv[s] = new Vector2((1f / (float)sides) * s, 0f);
                //botom
                mesh.verticies[s + bOffset] = new Vector3(posX * radius, 0.5f * length, posZ * radius);
                mesh.normals[s + bOffset] = mesh.normals[s];
                mesh.tangents[s + bOffset] = mesh.tangents[s];
                mesh.uv[s + bOffset] = new Vector2((1f / (float)sides) * s, 1f);
            }
            //triangles
            var tList = new List<int>();
            for (int s = 0; s < sides; s++)
            {
                tList.Add(s);
                tList.Add(s + 1);
                tList.Add(s + bOffset);

                tList.Add(s + bOffset);
                tList.Add(s + 1);
                tList.Add(s + bOffset + 1);
            }
            for (int i = 0; i < tList.Count; i++)
                mesh.triangles[i] = tList[i];
            return mesh;
        }

        private static UncheckedMesh CreatePismEnds(int sides, float diameter, float length, bool inscribed)
        {
            var outerDiam = GetRealOuterDiam(inscribed, diameter, sides);
            var innerDiam = GetRealInnerDiam(inscribed, diameter, sides);
            
            float theta = (Mathf.PI * 2f) / (float)sides;
            theta /= 2f;
            var oRad = outerDiam / 2f;
            var iRad = innerDiam / 2f;

            var vertices = new List<Vertex>();

            vertices.Add(new Vertex(new Vector3(0, -0.5f * length, 0), new Vector3(0, -1, 0), new Vector4(-1, 0, 0, 1), new Vector2(0.5f, 0.5f)));//top center

            for (int s = 0; s <= sides * 2; s++)
            {
                float posX = Mathf.Cos(theta * s - theta);
                float posZ = -Mathf.Sin(theta * s - theta);
                var radius = s % 2 == 0 ? oRad : iRad;

                vertices.Add(new Vertex(
                    new Vector3(posX * radius, -0.5f * length, posZ * radius),
                    new Vector3(0, -1, 0),
                    new Vector4(-1, 0, 0, 1),
                    new Vector2((posX + 1f) / 2f, (posZ + 1f) / 2f)));
            }

            int vPerSide = vertices.Count;

            vertices.Add(new Vertex(new Vector3(0, 0.5f * length, 0), new Vector3(0, 1, 0), new Vector4(-1, 0, 0, -1), new Vector2(0.5f, 0.5f)));//bottom center

            for (int s = 0; s <= sides * 2; s++)
            {
                float posX = Mathf.Cos(theta * s - theta);
                float posZ = -Mathf.Sin(theta * s - theta);
                var radius = s % 2 == 0 ? oRad : iRad;

                vertices.Add(new Vertex(
                    new Vector3(posX * radius, 0.5f * length, posZ * radius),
                    new Vector3(0, 1, 0),
                    new Vector4(-1, 0, 0, -1),
                    new Vector2((posX + 1f) / 2f, (posZ + 1f) / 2f)));
            }
            
            var tList = new List<int>();

            for (int s = 0; s < sides * 2; s++)
            {
                tList.Add(0);
                tList.Add(2 + s);
                tList.Add(1 + s);

                tList.Add(vPerSide);
                tList.Add(vPerSide + 1 + s);
                tList.Add(vPerSide + 2 + s);
            }

            Debug.Log(string.Format("{0} sided prism cap info: vertices={1} indices={2}, triangles={3}", sides, vertices.Count, tList.Count, tList.Count / 3));
            var mesh = new UncheckedMesh(vertices.Count, tList.Count / 3);

            for (int i = 0; i < vertices.Count; i++)
            {
                mesh.verticies[i] = vertices[i].Pos;
                mesh.normals[i] = vertices[i].Norm;
                mesh.tangents[i] = vertices[i].Tan;
                mesh.uv[i] = vertices[i].Uv;
            }

            for (int i = 0; i < tList.Count; i++)
                mesh.triangles[i] = tList[i];

            return mesh;
        }

        class Vertex
        {
            public Vector3 Pos { get; set; }
            public Vector3 Norm { get; set; }
            public Vector4 Tan { get; set; }
            public Vector2 Uv { get; set; }

            public Vertex()
            {
                Pos = Vector3.zero;
                Norm = Vector3.zero;
                Tan = Vector4.zero;
                Uv = Vector2.zero;
            }

            public Vertex(Vector3 pos, Vector3 norm, Vector4 tan, Vector2 uv)
            {
                Pos = pos;
                Norm = norm;
                Tan = tan;
                Uv = uv;
            }
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
