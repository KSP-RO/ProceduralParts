using KSPAPIExtensions.PartMessage;
using KSPAPIExtensions;
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace ProceduralParts
{
    /// <summary>
    /// For heat shields. All this does is copies the top node size to the bottom.
    /// </summary>
    public class ProceduralHeatshield : PartModule, ICostMultiplier, IPartMassModifier
    {
        [PartMessageEvent]
        public event PartMassChanged MassChanged;
       
        [PartMessageEvent]
        public event PartResourceMaxAmountChanged MaxAmountChanged;
        [PartMessageEvent]
        public event PartResourceInitialAmountChanged InitialAmountChanged;

        ProceduralPart _pPart = null;
        
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
            PartMessageService.Register(this);
        }

        public override void OnStart(StartState state)
        {
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
            }
            else
                Debug.LogError("Procedural Part not found");
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

            }
            catch(Exception e)
            {
                Debug.LogError("Could not find fairing mesh");
                Debug.LogException(e);
                useFairing = false;
            }

        }

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
        public float multiplyCostByDiameter;

        [KSPField(isPersistant=true)]
        public string ablativeResource = "Ablator";

        [KSPField]
        public FloatCurve CoPoffset = new FloatCurve();

        private AttachNode bottomNode;
        private AttachNode topNode;

        private Mesh fairingMesh;
        //private Mesh endsMesh;
        
        [PartMessageListener(typeof(PartAttachNodeSizeChanged))]
        public void PartAttachNodeSizeChanged(AttachNode node, float minDia, float area) 
        {
            if (node.id != topNodeId)
                return;
            CopyNodeSizeAndStrength();
        }

        [PartMessageListener(typeof(PartModelChanged), scenes: GameSceneFilter.AnyEditorOrFlight)]
        public void PartModelChanged()
        {

            ProceduralPart ppart = PPart;

            if(useFairing && ppart != null)
            {
                ProceduralAbstractSoRShape shape = ppart.CurrentShape as ProceduralAbstractSoRShape;

                if (shape != null)
                {
                    Vector3[] topInner = shape.GetEndcapVerticies(true);

                    int vertCount = topInner.Length;

                    //foreach (Vector3 v in topInner)
                    //    Debug.Log(v);

                    Vector3[] topOuter = (Vector3[])topInner.Clone();

                    for (int i = 0; i < vertCount; ++i)
                    {
                        topOuter[i].x *= 1.25f;
                        topOuter[i].z *= 1.25f;
                    }

                    Vector3[] sideTop = (Vector3[])topOuter.Clone();
                    Vector3[] sideBottom = (Vector3[])sideTop.Clone();

                    Vector3[] bottomInner = (Vector3[])topInner.Clone();
                    Vector3[] bottomOuter = (Vector3[])topOuter.Clone();

                    

                    for (int i = 0; i < vertCount; ++i)
                    {
                        sideBottom[i].y -= 0.5f;
                        bottomInner[i].y -= 0.5f;
                        bottomOuter[i].y -= 0.5f;
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

                    UncheckedMesh m = new UncheckedMesh(vertCount*8, vertCount * 8 * 6);
                    //int tri = 0;
                    for (int i = 0; i < vertCount; ++i)
                    {
                        m.verticies[topInnerStart + i] = topInner[i];
                        m.verticies[topOuterStart + i] = topOuter[i];
                        m.verticies[sideTopStart + i] = sideTop[i];
                        m.verticies[sideBottomStart + i] = sideBottom[i];
                        m.verticies[bottomInnerStart + i] = bottomInner[i];
                        m.verticies[bottomOuterStart + i] = bottomOuter[i];
                        m.verticies[innerSideTopStart + i] = innerSideTop[i];
                        m.verticies[innerSideBottomStart + i] = innerSideBottom[i];

                        m.normals[topInnerStart + i] = new Vector3(0.0f, 1.0f, 0.0f);
                        m.normals[topOuterStart + i] = new Vector3(0.0f, 1.0f, 0.0f);
                        
                        m.normals[sideTopStart + i]    = m.verticies[sideTopStart + i].xz().normalized;
                        m.normals[sideBottomStart + i] = m.verticies[sideBottomStart + i].xz().normalized;

                        m.normals[bottomInnerStart + i] = new Vector3(0.0f, -1.0f, 0.0f);
                        m.normals[bottomOuterStart + i] = new Vector3(0.0f, -1.0f, 0.0f);

                        m.normals[innerSideTopStart + i] = -m.verticies[innerSideTopStart + i].xz().normalized;
                        m.normals[innerSideBottomStart + i] = -m.verticies[innerSideBottomStart + i].xz().normalized;

                        m.uv[topInnerStart + i] = new Vector2(Mathf.InverseLerp(0, vertCount - 1, i), 1.0f);
                        m.uv[topOuterStart + i] = new Vector2(Mathf.InverseLerp(0, vertCount - 1, i), 0.0f);

                        m.uv[sideTopStart + i]    = new Vector2(Mathf.InverseLerp(0, vertCount-1,i), 1.0f);
                        m.uv[sideBottomStart + i] = new Vector2(Mathf.InverseLerp(0, vertCount - 1, i), 0.0f);

                        m.uv[bottomInnerStart + i] = new Vector2(Mathf.InverseLerp(0, vertCount - 1, i), 0.0f);
                        m.uv[bottomOuterStart + i] = new Vector2(Mathf.InverseLerp(0, vertCount - 1, i), 1.0f);

                        m.uv[innerSideTopStart + i] = new Vector2(Mathf.InverseLerp(0, vertCount - 1, i), 1.0f);
                        m.uv[innerSideBottomStart + i] = new Vector2(Mathf.InverseLerp(0, vertCount - 1, i), 0.0f);

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
            }

            if(ppart != null)
            {
                ProceduralShapeBezierCone shape = ppart.CurrentShape as ProceduralShapeBezierCone;
                
                if(null != shape)
                {
                    float diameter = shape.topDiameter;
                    float length = shape.length;

                    if (HighLogic.LoadedSceneIsEditor)
                    {
                        
                        float surfaceArea = Mathf.PI * (diameter / 2) * (diameter / 2);

                        PartResource pr = part.Resources[ablativeResource];

                        if (null != pr)
                        {
                            double ratio = pr.amount / pr.maxAmount;

                            pr.maxAmount = (double)(ablatorPerArea * surfaceArea);
                            pr.amount = Math.Max(ratio * pr.maxAmount, pr.maxAmount);
                            //ResourceListChanged();
                            MaxAmountChanged(pr, pr.maxAmount);
                            InitialAmountChanged(pr, pr.maxAmount);


                        }
                        
                    }

                    //Debug.Log(massPerDiameter + " * " + diameter);
                    part.mass = massPerDiameter * diameter;
                    MassChanged(part.mass);
                                    
                    //Debug.Log("CoL offset " + -length);
                    
                    part.CoLOffset.y = -length;

                    part.CoPOffset.y = CoPoffset.Evaluate(diameter);
                    //Debug.Log("CoP offset: "+ part.CoPOffset.y);
                   
                   
                    if (HighLogic.LoadedSceneIsEditor)
                        GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                }
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
                    m.triangles[tri++] = ring1Offset + i;
                    m.triangles[tri++] = ring1Offset;
                    m.triangles[tri++] = ring2Offset;

                    m.triangles[tri++] = ring1Offset + i;
                    m.triangles[tri++] = ring2Offset;
                    m.triangles[tri++] = ring2Offset + i;
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
                bottomNode = part.findAttachNode(bottomNodeId);
            if (topNode == null)
                topNode = part.findAttachNode(topNodeId);
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


        public float GetModuleMass(float defaultMass)
        {
            return part.mass - defaultMass;
        }
    }// class
}// namespace
