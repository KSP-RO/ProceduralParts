//using KSPAPIExtensions.PartMessage;
using KSPAPIExtensions;
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace ProceduralParts
{

    interface IProp
    {
        void UpdateProp();
    }

    /// <summary>
    /// For heat shields. All this does is copies the top node size to the bottom.
    /// </summary>
    public class ProceduralHeatshield : PartModule, ICostMultiplier, IPartMassModifier, IProp
    {
		#region IPartMassModifier implementation

		public float GetModuleMass (float defaultMass, ModifierStagingSituation sit)
		{
			return mass - defaultMass;
		}

		public ModifierChangeWhen GetModuleMassChangeWhen ()
		{
			return ModifierChangeWhen.FIXED;
		}

		#endregion

        //[PartMessageEvent]
        //public event PartMassChanged MassChanged;
		public void MassChanged (float mass)
		{
			var data = new BaseEventDetails (BaseEventDetails.Sender.USER);
			data.Set<float> ("mass", mass);

			part.SendEvent ("OnPartMassChanged", data, 0);
		}

        //[PartMessageEvent]
        //public event PartResourceMaxAmountChanged MaxAmountChanged;
        //[PartMessageEvent]
        //public event PartResourceInitialAmountChanged InitialAmountChanged;

		public void MaxAmountChanged (Part part, PartResource resource, double amount)
		{
			var data = new BaseEventDetails (BaseEventDetails.Sender.USER);
			data.Set<PartResource> ("resource", resource);
			data.Set<double> ("amount", amount);
			part.SendEvent ("OnResourceMaxChanged", data, 0);
		}

		//[PartMessageEvent]
		//public event PartResourceInitialAmountChanged InitialAmountChanged;
		public void InitialAmountChanged (Part part, PartResource resource, double amount)
		{
			var data = new BaseEventDetails (BaseEventDetails.Sender.USER);
			data.Set<PartResource> ("resource", resource);
			data.Set<double> ("amount", amount);
			part.SendEvent ("OnResourceInitialChanged", data, 0);
		}

        ProceduralPart _pPart = null;

        [KSPField(isPersistant=true)]
        public float mass = -1;
        
        public ProceduralPart PPart
        {
            get
            {
                try
                {
                    if (_pPart != null)
                        return _pPart;
                    else    
                        _pPart = GetComponent<ProceduralPart>();
                }
                catch(Exception)
                {
                    _pPart = null;
                }
                return null;
            }
        }

        public override void OnAwake()
        {
            base.OnAwake();
            //PartMessageService.Register(this);
        }

        public override void OnStart(StartState state)
        {

            if (HighLogic.LoadedSceneIsFlight)
            {
                if (mass <= 0)
                    mass = 0.000001f;

                MassChanged(mass);

                //double skinThermalMassModifier = part.thermalMassModifier;
                //double skinThicknessFactor = 0.1;
                //Debug.Log(skinThermalMassModifier);
                //Debug.LogWarning((double)part.mass * PhysicsGlobals.StandardSpecificHeatCapacity * skinThermalMassModifier * skinThicknessFactor);
            }

            InitializeObjects();
            CopyNodeSizeAndStrength();
            //if (HighLogic.LoadedSceneIsFlight)
            //{
            //    PartResource resource = part.Resources["AblativeShielding"];
            //    UpdateDissipationAndLoss(resource.maxAmount);
            //}

            if (PPart != null)
            {
                //PPart.AddNodeOffset(topNodeId, GetNodeOffset);
                loadedTextureSets = PPart.TextureSets.ToList();
                loadedTextureSetNames = loadedTextureSets.Select<ProceduralPart.TextureSet, string>(x => x.name).ToArray();

                BaseField field = Fields["textureSet"];
                UI_ChooseOption range = (UI_ChooseOption)field.uiControlEditor;

                range.options = loadedTextureSetNames;
                if (textureSet == null || !loadedTextureSetNames.Contains(textureSet))
                    textureSet = loadedTextureSetNames[0];

                UpdateTexture();
                UpdateFairing();
                
            }
            else
                Debug.LogError("Procedural Part not found");

            UI_FloatEdit fairingThicknessEdit = (UI_FloatEdit)Fields["fairingThickness"].uiControlEditor;
            fairingThicknessEdit.maxValue = this.fairingMaxThickness;
            fairingThicknessEdit.minValue = this.fairingMinThickness;
            fairingThicknessEdit.incrementLarge = 0.1f;
            fairingThicknessEdit.incrementSmall = 0.01f;
            fairingThickness = Mathf.Clamp(fairingThickness, fairingMinThickness, fairingMaxThickness);
        }


        private void InitializeObjects()
        {
            //Transform fairing = part.FindModelTransform("fairing");
            Transform fairing = part.FindModelTransform("fairing");

            try
            {
                foreach (MeshFilter mf in fairing.GetComponents<MeshFilter>())
                    Debug.Log("MeshFilterFound: " + mf.name);
                //fairingMesh = fairing.GetComponent<MeshFilter>().mesh;
                fairingMesh = fairing.GetComponent<MeshFilter>().mesh;
                fairingMaterial = fairing.GetComponent<Renderer>().material;

            }
            catch(Exception e)
            {
                Debug.LogError("Could not find fairing mesh");
                Debug.LogException(e);
                useFairing = false;
            }

        }

        public void Update()
        {
            if (HighLogic.LoadedSceneIsEditor)
                UpdateEditor();
        }

        public void UpdateFAR()
        {
            if (HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)
            {
                part.SendMessage("GeometryPartModuleRebuildMeshData");
            }
        }

        private void UpdateEditor()
        {
            UpdateTexture();

            if(fairingThickness != oldFairingThickness)
            {
                UpdateFairing();
                oldFairingThickness = fairingThickness;

                // We need to tell FAR that something has changed
                UpdateFAR();

            }
        }

        Vector2 TextureScale = Vector2.one;

        private void UpdateTexture()
        {
            if (fairingMaterial == null || textureSet == oldTextureSet)
                return;

            
            int newIdx = loadedTextureSets.FindIndex(set => set.name == textureSet);
            if (newIdx < 0)
            {
                Debug.LogError("*ST* Unable to find texture set: " + textureSet);
                textureSet = oldTextureSet;
                return;
            }
            oldTextureSet = textureSet;

            ProceduralPart.TextureSet tex = loadedTextureSets[newIdx];

            // Set shaders
            if (!part.Modules.Contains("ModulePaintable"))
            {
                fairingMaterial.shader = Shader.Find(tex.sidesBump != null ? "KSP/Bumped Specular" : "KSP/Specular");

                //// pt is no longer specular ever, just diffuse.
                //if (EndsMaterial != null)
                //    EndsMaterial.shader = Shader.Find("KSP/Diffuse");
            }

            fairingMaterial.SetColor("_SpecColor", tex.sidesSpecular);
            fairingMaterial.SetFloat("_Shininess", tex.sidesShininess);

            //// TODO: shove into config file.
            //if (EndsMaterial != null)
            //{
            //    const float scale = 0.93f;
            //    const float offset = (1f / scale - 1f) / 2f;
            //    EndsMaterial.mainTextureScale = new Vector2(scale, scale);
            //    EndsMaterial.mainTextureOffset = new Vector2(offset, offset);
            //}

            // set up UVs
            Vector2 scaleUV = tex.scale;
            
            if (tex.autoScale)
            {
                scaleUV.x = (float)Math.Round(scaleUV.x * TextureScale.x / 8f);
                if (scaleUV.x < 1)
                    scaleUV.x = 1;
                if (tex.autoWidthDivide)
                {
                    if (tex.autoHeightSteps > 0)
                        scaleUV.y = (float)Math.Ceiling(scaleUV.y * TextureScale.y / scaleUV.x * (1f / tex.autoHeightSteps)) * tex.autoHeightSteps;
                    else
                        scaleUV.y *= TextureScale.y / scaleUV.x;
                }
                else
                {
                    if (tex.autoHeightSteps > 0)
                        scaleUV.y = (float)Math.Max(Math.Round(TextureScale.y / tex.autoHeightSteps), 1f) * tex.autoHeightSteps;
                    else
                        scaleUV.y *= TextureScale.y;
                }
            }

            // apply
            fairingMaterial.mainTextureScale = scaleUV;
            fairingMaterial.mainTextureOffset = Vector2.zero;
            fairingMaterial.SetTexture("_MainTex", tex.sides);
            if (tex.sidesBump != null)
            {
                fairingMaterial.SetTextureScale("_BumpMap", scaleUV);
                fairingMaterial.SetTextureOffset("_BumpMap", Vector2.zero);
                fairingMaterial.SetTexture("_BumpMap", tex.sidesBump);
            }
            //if (EndsMaterial != null)
            //    EndsMaterial.SetTexture("_MainTex", tex.ends);

        }

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Fairing"),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = 0.01f, useSI=true, unit = "m", sigFigs = 5)]
        public float fairingThickness = 0.05f;
        private float oldFairingThickness;

        [KSPField]
        public float fairingMinThickness = 0.01f;

        [KSPField]
        public float fairingMaxThickness = 0.5f;

        [KSPField]
        public bool useFairing = true;

		[KSPField]
		public float lossTweak = 1.0f;

		[KSPField]
		public float dissipationTweak = 1.0f;


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

        [KSPField(isPersistant=true)]
        public string ablativeResource = "Ablator";

        [KSPField]
        public FloatCurve CoPoffset = new FloatCurve();

        [KSPField(guiName = "Fairing Texture", guiActive = false, guiActiveEditor = true, isPersistant = true), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string textureSet = "Original";
        private string oldTextureSet = "*****";

        private List<ProceduralPart.TextureSet> loadedTextureSets;
        private static string[] loadedTextureSetNames;

        private AttachNode bottomNode;
        private AttachNode topNode;

        private Mesh fairingMesh;
        //private Mesh endsMesh;
        private Material fairingMaterial;
        
        //[PartMessageListener(typeof(PartAttachNodeSizeChanged))]
        //public void PartAttachNodeSizeChanged(AttachNode node, float minDia, float area) 
		[KSPEvent(guiActive = false, active = true)]
		public void PartAttachNodeSizeChanged(BaseEventDetails data) 
        {
			AttachNode node = data.Get<AttachNode> ("node");
            if (node.id != topNodeId)
                return;
            CopyNodeSizeAndStrength();
        }

        void UpdateFairing()
        {

            ProceduralPart ppart = PPart;

            if (useFairing && ppart != null)
            {
                ProceduralAbstractSoRShape shape = ppart.CurrentShape as ProceduralAbstractSoRShape;

                if (shape != null)
                {
                    Vector3[] topEndcapVerticies = shape.GetEndcapVerticies(true);

                    Vector3[] topInner = new Vector3[topEndcapVerticies.Length + 1];

                    topEndcapVerticies.CopyTo(topInner, 0);
                    topInner[topEndcapVerticies.Length] = topEndcapVerticies[0];

                    int vertCount = topInner.Length;

                    //foreach (Vector3 v in topInner)
                    //    Debug.Log(v);

                    Vector3[] topOuter = (Vector3[])topInner.Clone();

                    for (int i = 0; i < vertCount; ++i)
                    {
                        float r = topInner[i].magnitude;
                        float r_ = r + fairingThickness;
                        float scaleFactor = r_ / r;
                        topOuter[i].x *= scaleFactor;
                        topOuter[i].z *= scaleFactor;
                    }

                    TextureScale.x = topOuter[0].magnitude * 2 * Mathf.PI;

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

                    TextureScale.y = Mathf.Abs(topOuter[0].y - bottomOuter[0].y);

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

                        //Debug.Log(i +" uv: " + Mathf.InverseLerp(0, vertCount - 1, i));

                    }

                    int triangleOffset = 0;
                    triangleOffset = ConnectRings(m, topInnerStart, topOuterStart, vertCount, 0);
                    triangleOffset = ConnectRings(m, sideTopStart, sideBottomStart, vertCount, triangleOffset);
                    triangleOffset = ConnectRings(m, bottomOuterStart, bottomInnerStart, vertCount, triangleOffset);
                    triangleOffset = ConnectRings(m, innerSideBottomStart, innerSideTopStart, vertCount, triangleOffset);

                    if (fairingMesh != null)
                    {
                        m.WriteTo(fairingMesh);
                        fairingMesh.RecalculateNormals();

                    }
                    else
                        Debug.Log("no fairing mesh");

                }
                oldTextureSet = null;
                UpdateTexture();
            }

        }

        int ConnectRings(UncheckedMesh m, int ring1Offset, int ring2Offset, int vertCount, int triOffset, bool inverse = false)
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

        //[PartMessageListener(typeof(PartResourceMaxAmountChanged))]
        //public void PartResourceMaxAmountChanged(PartResource resource, double maxAmount)
        //{
        //    if (resource.name != "AblativeShielding")
        //        return;
        //    UpdateDissipationAndLoss(resource.maxAmount);
        //}


        private void CopyNodeSizeAndStrength()
        {
            if (bottomNode == null)
                bottomNode = part.FindAttachNode(bottomNodeId);
            if (topNode == null)
                topNode = part.FindAttachNode(topNodeId);
            bottomNode.size = topNode.size;
            bottomNode.breakingForce = topNode.breakingForce;
            bottomNode.breakingTorque = topNode.breakingTorque;
        }



        // Thats for DRE, which is not updated yet 
        //private void UpdateDissipationAndLoss(double ablativeResource)
        //{
        //    // The heat model is going to change considerably.
        //    // Will just do an unconfigurable quick and dirty way for now.
        //    FloatCurve loss = new FloatCurve();
        //    loss.Add(650, 0, 0, 0);
        //    loss.Add(1000, (float)(0.2 * lossTweak * ablativeResource));
        //    loss.Add(3000, (float)(0.3 * lossTweak * ablativeResource), 0, 0);

        //    FloatCurve dissipation = new FloatCurve();
        //    dissipation.Add(300, 0, 0, 0);
        //    dissipation.Add(500, (float)(80000 * dissipationTweak / ablativeResource), 0, 0);
            
        //    // Save it.
        //    PartModule modHeatShield = part.Modules["ModuleHeatShield"];

        //    Type type = modHeatShield.GetType();

        //    type.GetField("loss").SetValue(modHeatShield, loss);
        //    type.GetField("dissipation").SetValue(modHeatShield, dissipation);
        //}


        public float GetCurrentCostMult()
        {
            if (multiplyCostByDiameter != 0)
            {
                ProceduralPart ppart = PPart;

                if (ppart != null)
                {
                    ProceduralShapeBezierCone shape = ppart.CurrentShape as ProceduralShapeBezierCone;

                    if (null != shape)
                    {
                        float diameter = shape.topDiameter;
                        return diameter * multiplyCostByDiameter;                    
                    }
     
                }
            }
           
                
            return 1;
        }

        public void UpdateProp()
        {
            ProceduralPart ppart = PPart;

            UpdateFairing();

            if (ppart != null)
            {
                ProceduralShapeBezierCone shape = ppart.CurrentShape as ProceduralShapeBezierCone;

                if (null != shape)
                {
                    float diameter = shape.topDiameter;
                    float length = shape.length;

                    if (HighLogic.LoadedSceneIsEditor)
                    {

                        float surfaceArea = Mathf.PI * (diameter / 2) * (diameter / 2);

                        PartResource pr = part.Resources[ablativeResource];

                        if (null != pr)
                        {
                            double ratio = pr.maxAmount != 0 ? pr.amount / pr.maxAmount : 1.0;
                            //Debug.LogWarning("ratio: " + ratio);
                            //Debug.LogWarning("amount: " + pr.amount);
                            //Debug.LogWarning("max amount: " + pr.maxAmount);
                            pr.maxAmount = (double)(ablatorPerArea * surfaceArea);
                            pr.amount = Math.Min(ratio * pr.maxAmount, pr.maxAmount);
                            //ResourceListChanged();
                            MaxAmountChanged(part, pr, pr.maxAmount);
                            InitialAmountChanged(part, pr, pr.maxAmount);


                        }

                    }

                    //Debug.Log(massPerDiameter + " * " + diameter);
                    mass = massPerDiameter * diameter + massFromDiameterCurve.Evaluate(diameter);
                    //Debug.LogWarning("changed mass: " + mass);
                    MassChanged(mass);

                    //Debug.Log("CoL offset " + -length);

                    part.CoLOffset.y = -length;

                    part.CoPOffset.y = CoPoffset.Evaluate(diameter);
                    //Debug.Log("CoP offset: "+ part.CoPOffset.y);


                    if (HighLogic.LoadedSceneIsEditor)
                        GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                }
            }

            // We don't need to tell FAR, because the shape will do it for us anyway.
        }
    }// class
}// namespace
