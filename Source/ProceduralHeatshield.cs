using KSPAPIExtensions;
using System;
using UnityEngine;

namespace ProceduralParts
{
    interface IProp
    {
        void UpdateProp();
    }

    public class ProceduralHeatshield : PartModule, ICostMultiplier, IPartMassModifier, IProp
    {
        private static readonly string ModTag = "[ProceduralHeatSheild]";

        #region IPartMassModifier implementation

        public float GetModuleMass (float defaultMass, ModifierStagingSituation sit) => mass - defaultMass;
        public ModifierChangeWhen GetModuleMassChangeWhen () => ModifierChangeWhen.FIXED;

        #endregion

        #region Fields

        [KSPField(isPersistant = true)]
        public float mass = -1;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "#PP_GUI_Fairing"),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = 0.01f, incrementLarge = -0.1f, incrementSmall = 0.01f, useSI = true, unit = "m", sigFigs = 5)]
        public float fairingThickness = 0.05f;

        [KSPField]
        public float fairingMinThickness = 0.01f;

        [KSPField]
        public float fairingMaxThickness = 0.5f;

        [KSPField]
        public bool useFairing = true;

        [KSPField]
        public string bottomNodeId = "bottom";

        [KSPField]
        public string topNodeId = "top";

        [KSPField]
        public float ablatorPerArea;

        [KSPField]
        public float massPerDiameter;

        [KSPField]
        public FloatCurve massFromDiameterCurve = new FloatCurve();

        [KSPField]
        public float multiplyCostByDiameter;

        [KSPField(isPersistant = true)]
        public string ablativeResource = "Ablator";

        [KSPField(isPersistant = true)]
        public string fairingTextureSet = "Original";

        [KSPField]
        public FloatCurve CoPoffset = new FloatCurve();

        private AttachNode bottomNode;
        private AttachNode topNode;
        public ProceduralPart PPart => _pPart ?? (_pPart = GetComponent<ProceduralPart>());
        //                _pPart ??= GetComponent<ProceduralPart>();
        private ProceduralPart _pPart;

        #endregion

        #region Unity Callbacks

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (PPart is null)
            {
                Debug.LogError($"{ModTag} {part}.{this} Procedural Part not found");
                return;
            }
            bottomNode = part.FindAttachNode(bottomNodeId);
            topNode = part.FindAttachNode(topNodeId);
            CopyNodeSizeAndStrength();

            if (HighLogic.LoadedSceneIsEditor)
            {
                if (PPart?.CurrentShape is ProceduralShapeBezierCone cone)
                {
                    cone.Fields[nameof(cone.topDiameter)].uiControlEditor.onFieldChanged += FairingThicknessChanged;
                    cone.Fields[nameof(cone.bottomDiameter)].uiControlEditor.onFieldChanged += FairingThicknessChanged;
                    cone.Fields[nameof(cone.length)].uiControlEditor.onFieldChanged += FairingThicknessChanged;
                }

                UI_FloatEdit fairingThicknessEdit = Fields[nameof(fairingThickness)].uiControlEditor as UI_FloatEdit;
                fairingThicknessEdit.maxValue = this.fairingMaxThickness;
                fairingThicknessEdit.minValue = this.fairingMinThickness;
                fairingThicknessEdit.onFieldChanged = new Callback<BaseField, object>(FairingThicknessChanged);
                fairingThickness = Mathf.Clamp(fairingThickness, fairingMinThickness, fairingMaxThickness);
            }
            UpdateFairing();
            InitializeTexture();
        }

        private void FairingThicknessChanged(BaseField f, object obj) 
        {
            UpdateFairing();
            UpdateFAR();
        }

        [KSPEvent(guiActive = false, active = true)]
        public void PartAttachNodeSizeChanged(BaseEventDetails data)  => CopyNodeSizeAndStrength();

        #endregion

        #region Textures and Meshes
        private void InitializeTexture()
        {
            Transform fairing = part.FindModelTransform("fairing");
            if (fairing.GetComponent<Renderer>().material is Material fairingMaterial &&
                LegacyTextureHandler.textureSets.ContainsKey(fairingTextureSet))
            {
                TextureSet tex = LegacyTextureHandler.textureSets[fairingTextureSet];
                if (!part.Modules.Contains("ModulePaintable"))
                {
                    fairingMaterial.shader = Shader.Find(tex.sidesBump is Texture ? "KSP/Bumped Specular" : "KSP/Specular");
                }

                fairingMaterial.SetColor("_SpecColor", tex.sidesSpecular);
                fairingMaterial.SetFloat("_Shininess", tex.sidesShininess);
                fairingMaterial.mainTextureScale = tex.scale;
                fairingMaterial.mainTextureOffset = Vector2.zero;
                fairingMaterial.SetTexture("_MainTex", tex.sides);
                if (tex.sidesBump is Texture)
                {
                    fairingMaterial.SetTextureScale("_BumpMap", tex.scale);
                    fairingMaterial.SetTextureOffset("_BumpMap", Vector2.zero);
                    fairingMaterial.SetTexture("_BumpMap", tex.sidesBump);
                }
            }
        }

        void UpdateFairing()
        {
            Transform fairing = part.FindModelTransform("fairing");
            Mesh fairingMesh = fairing.GetComponent<MeshFilter>().mesh;
            if (useFairing && fairingMesh is Mesh && PPart?.CurrentShape is ProceduralAbstractSoRShape shape)
            {
                Vector3[] topEndcapVerticies = shape.GetEndcapVerticies(true);

                Vector3[] topInner = new Vector3[topEndcapVerticies.Length + 1];

                topEndcapVerticies.CopyTo(topInner, 0);
                topInner[topEndcapVerticies.Length] = topEndcapVerticies[0];

                int vertCount = topInner.Length;
                Vector3[] topOuter = (Vector3[])topInner.Clone();

                for (int i = 0; i < vertCount; ++i)
                {
                    float r = topInner[i].magnitude;
                    float r_ = r + fairingThickness;
                    float scaleFactor = r_ / r;
                    topOuter[i].x *= scaleFactor;
                    topOuter[i].z *= scaleFactor;
                }

                Vector3[] sideTop = (Vector3[])topOuter.Clone();
                Vector3[] sideBottom = (Vector3[])sideTop.Clone();

                Vector3[] bottomInner = (Vector3[])topInner.Clone();
                Vector3[] bottomOuter = (Vector3[])topOuter.Clone();

                for (int i = 0; i < vertCount; ++i)
                {
                    if (bottomNode != null)
                    {
                        sideBottom[i].y = bottomNode.position.y;
                        bottomInner[i].y = bottomNode.position.y;
                        bottomOuter[i].y = bottomNode.position.y;
                    }
                }

                Vector3[] innerSideTop = (Vector3[])topInner.Clone();
                Vector3[] innerSideBottom = (Vector3[])bottomInner.Clone();

                int topInnerStart = 0;
                int topOuterStart = topInnerStart + vertCount;
                int sideTopStart = topOuterStart + vertCount;
                int sideBottomStart = sideTopStart + vertCount;
                int bottomInnerStart = sideBottomStart + vertCount;
                int bottomOuterStart = bottomInnerStart + vertCount;
                int innerSideTopStart = bottomOuterStart + vertCount;
                int innerSideBottomStart = innerSideTopStart + vertCount;

                UncheckedMesh m = new UncheckedMesh(vertCount * 8, vertCount * 8 * 6);
                //int tri = 0;
                for (int i = 0; i < vertCount; ++i)
                {
                    m.vertices[topInnerStart + i] = topInner[i];
                    m.vertices[topOuterStart + i] = topOuter[i];
                    m.vertices[sideTopStart + i] = sideTop[i];
                    m.vertices[sideBottomStart + i] = sideBottom[i];
                    m.vertices[bottomInnerStart + i] = bottomInner[i];
                    m.vertices[bottomOuterStart + i] = bottomOuter[i];
                    m.vertices[innerSideTopStart + i] = innerSideTop[i];
                    m.vertices[innerSideBottomStart + i] = innerSideBottom[i];

                    m.normals[topInnerStart + i] = new Vector3(0.0f, 1.0f, 0.0f);
                    m.normals[topOuterStart + i] = new Vector3(0.0f, 1.0f, 0.0f);

                    m.normals[sideTopStart + i] = m.vertices[sideTopStart + i].xz().normalized;
                    m.normals[sideBottomStart + i] = m.vertices[sideBottomStart + i].xz().normalized;

                    m.normals[bottomInnerStart + i] = new Vector3(0.0f, -1.0f, 0.0f);
                    m.normals[bottomOuterStart + i] = new Vector3(0.0f, -1.0f, 0.0f);

                    m.normals[innerSideTopStart + i] = -m.vertices[innerSideTopStart + i].xz().normalized;
                    m.normals[innerSideBottomStart + i] = -m.vertices[innerSideBottomStart + i].xz().normalized;

                    m.uv[topInnerStart + i] = new Vector2(Mathf.InverseLerp(0, vertCount - 1, i), 0.0f);
                    m.uv[topOuterStart + i] = new Vector2(Mathf.InverseLerp(0, vertCount - 1, i), 1.0f);

                    m.uv[sideTopStart + i] = new Vector2(Mathf.InverseLerp(0, vertCount - 1, i), 1.0f);
                    m.uv[sideBottomStart + i] = new Vector2(Mathf.InverseLerp(0, vertCount - 1, i), 0.0f);

                    m.uv[bottomInnerStart + i] = new Vector2(Mathf.InverseLerp(0, vertCount - 1, i), 0.0f);
                    m.uv[bottomOuterStart + i] = new Vector2(Mathf.InverseLerp(0, vertCount - 1, i), 1.0f);

                    m.uv[innerSideTopStart + i] = new Vector2(Mathf.InverseLerp(0, vertCount - 1, i), 0.0f);
                    m.uv[innerSideBottomStart + i] = new Vector2(Mathf.InverseLerp(0, vertCount - 1, i), 1.0f);

                    m.tangents[topInnerStart + i] = Vector3.Cross(m.normals[topInnerStart + i], m.vertices[topInnerStart + i]).xz().normalized.toVec4(-1);
                    m.tangents[topOuterStart + i] = Vector3.Cross(m.normals[topOuterStart + i], m.vertices[topOuterStart + i]).xz().normalized.toVec4(-1);

                    m.tangents[sideTopStart + i] = Vector3.Cross(m.normals[sideTopStart + i], new Vector3(0, 1, 0)).normalized.toVec4(-1);
                    m.tangents[sideBottomStart + i] = Vector3.Cross(m.normals[sideTopStart + i], new Vector3(0, 1, 0)).normalized.toVec4(-1);

                    m.tangents[bottomInnerStart + i] = Vector3.Cross(m.normals[bottomInnerStart + i], m.vertices[topInnerStart + i]).xz().normalized.toVec4(-1);
                    m.tangents[bottomOuterStart + i] = Vector3.Cross(m.normals[bottomOuterStart + i], m.vertices[topOuterStart + i]).xz().normalized.toVec4(-1);

                    m.tangents[innerSideTopStart + i] = Vector3.Cross(m.normals[innerSideTopStart + i], new Vector3(0, 1, 0)).normalized.toVec4(-1);
                    m.tangents[innerSideBottomStart + i] = Vector3.Cross(m.normals[innerSideTopStart + i], new Vector3(0, 1, 0)).normalized.toVec4(-1);
                }

                int triangleOffset = 0;
                triangleOffset = ConnectRings(m, topInnerStart, topOuterStart, vertCount, triangleOffset);
                triangleOffset = ConnectRings(m, sideTopStart, sideBottomStart, vertCount, triangleOffset);
                triangleOffset = ConnectRings(m, bottomOuterStart, bottomInnerStart, vertCount, triangleOffset);
                _ = ConnectRings(m, innerSideBottomStart, innerSideTopStart, vertCount, triangleOffset);

                m.WriteTo(fairingMesh);
                fairingMesh.RecalculateNormals();
            }
            UpdateMass();
        }

        int ConnectRings(UncheckedMesh m, int ring1Offset, int ring2Offset, int vertCount, int triOffset)
        {
            int tri = triOffset;
            for (int i = 0; i < vertCount; ++i)
            {
                //Debug.Log(i);
                if (i < vertCount - 1)
                {
                    m.triangles[tri++] = ring1Offset + i;
                    m.triangles[tri++] = ring1Offset + i + 1;
                    m.triangles[tri++] = ring2Offset + i + 1;

                    m.triangles[tri++] = ring1Offset + i;
                    m.triangles[tri++] = ring2Offset + i + 1;
                    m.triangles[tri++] = ring2Offset + i;
                }
                else
                {
                    //m.triangles[tri++] = ring1Offset + i;
                    //m.triangles[tri++] = ring1Offset;
                    //m.triangles[tri++] = ring2Offset;

                    //m.triangles[tri++] = ring1Offset + i;
                    //m.triangles[tri++] = ring2Offset;
                    //m.triangles[tri++] = ring2Offset + i;
                }
            }
            return tri;
        }

        #endregion

        #region Updaters

        public float GetCurrentCostMult() => (PPart?.CurrentShape is ProceduralShapeBezierCone shape && multiplyCostByDiameter > 0) ?
                                            shape.topDiameter * multiplyCostByDiameter : 1;

        public void UpdateProp()
        {
            UpdateFairing();
            if (HighLogic.LoadedSceneIsEditor)
                UpdateAblatorResource();

            if (PPart?.CurrentShape is ProceduralShapeBezierCone shape)
            {
                part.CoLOffset.y = -shape.length;
                part.CoPOffset.y = CoPoffset.Evaluate(shape.topDiameter);

                if (HighLogic.LoadedSceneIsEditor)
                    GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }
        }

        private void UpdateAblatorResource()
        {
            if (PPart?.CurrentShape is ProceduralShapeBezierCone shape)
            {
                float diameter = shape.topDiameter;
                // Cross-sectional surface area, maybe...
                float surfaceArea = Mathf.PI * (diameter / 2) * (diameter / 2);
                if (part.Resources[ablativeResource] is PartResource pr)
                {
                    double ratio = pr.maxAmount > 0 ? pr.amount / pr.maxAmount : 1.0;
                    pr.maxAmount = ablatorPerArea * surfaceArea;
                    pr.amount = Math.Min(ratio * pr.maxAmount, pr.maxAmount);
                    DirtyPAW();
                }
            }
        }

        private void UpdateMass()
        {
            if (PPart?.CurrentShape is ProceduralShapeBezierCone shape)
            {
                float diameter = shape.topDiameter;
                mass = massPerDiameter * diameter + massFromDiameterCurve.Evaluate(diameter);
            }
        }

        private void CopyNodeSizeAndStrength()
        {
            bottomNode.size = topNode.size;
            bottomNode.breakingForce = topNode.breakingForce;
            bottomNode.breakingTorque = topNode.breakingTorque;
        }

        public void UpdateFAR()
        {
            if (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)
            {
                part.SendMessage("GeometryPartModuleRebuildMeshData");
            }
        }

        #endregion
        private void DirtyPAW()
        {
            foreach (UIPartActionWindow window in UIPartActionController.Instance.windows)
            {
                if (window.part == this.part)
                {
                    window.displayDirty = true;
                }
            }
        }
    }
}
