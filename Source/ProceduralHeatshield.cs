using KSPAPIExtensions.PartMessage;
using KSPAPIExtensions;
using System;
using UnityEngine;

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
        public event PartResourceListChanged ResourceListChanged;
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

        private AttachNode bottomNode;
        private AttachNode topNode;

        
        
        [PartMessageListener(typeof(PartAttachNodeSizeChanged))]
        public void PartAttachNodeSizeChanged(AttachNode node, float minDia, float area) 
        {
            if (node.id != topNodeId)
                return;
            CopyNodeSizeAndStrength();
        }

        [PartMessageListener(typeof(PartModelChanged), scenes: ~GameSceneFilter.Flight)]
        public void PartModelChanged()
        {
            ProceduralPart ppart = PPart;

            if(ppart != null)
            {
                ProceduralShapeBezierCone shape = ppart.CurrentShape as ProceduralShapeBezierCone;

                if(null != shape)
                {
                    float diameter = shape.topDiameter;
                    float surfaceArea = Mathf.PI * (diameter/2) * (diameter/2);

                    PartResource pr = part.Resources["Ablator"];

                    if(null != pr)
                    {
                        double ratio = pr.amount / pr.maxAmount;

                        pr.maxAmount = (double)(ablatorPerArea * surfaceArea);
                        pr.amount = Math.Max(ratio * pr.maxAmount, pr.maxAmount);
                        //ResourceListChanged();
                        MaxAmountChanged(pr, pr.maxAmount);
                        InitialAmountChanged(pr, pr.maxAmount);

                        
                    }

                    part.mass = massPerDiameter * diameter;
                    MassChanged(part.mass);

                    if (HighLogic.LoadedSceneIsEditor)
                        GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                }
            }


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
