using KSPAPIExtensions;
using KSPAPIExtensions.PartMessage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ProceduralParts
{
    /// <summary>
    /// For heat shields. All this does is copies the top node size to the bottom.
    /// </summary>
    public class ProceduralHeatshield : PartModule
    {
        public override void OnAwake()
        {
            base.OnAwake();
            PartMessageService.Register(this);
        }

        public override void OnStart(StartState state)
        {
            CopyNodeSizeAndStrength();
			if (HighLogic.LoadedSceneIsFlight)
			{
				PartResource resource = part.Resources["AblativeShielding"];
				UpdateDissipationAndLoss(resource.maxAmount);
			}
        }

		[KSPField]
		public float lossTweak = 1.0f;

		[KSPField]
		public float dissipationTweak = 1.0f;


        [KSPField]
        public string bottomNodeId = "bottom";

        [KSPField]
        public string topNodeId = "top";

        private AttachNode bottomNode;
        private AttachNode topNode;
        
        [PartMessageListener(typeof(PartAttachNodeSizeChanged))]
        public void PartAttachNodeSizeChanged(AttachNode node, float minDia, float area) 
        {
            if (node.id != topNodeId)
                return;
            CopyNodeSizeAndStrength();
        }

		[PartMessageListener(typeof(PartResourceMaxAmountChanged))]
		private void PartResourceMaxAmountChanged(PartResource resource, double maxAmount)
		{
			if (resource.name != "AblativeShielding")
				return;
			UpdateDissipationAndLoss(resource.maxAmount);
		}


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



        private void UpdateDissipationAndLoss(double ablativeResource)
        {
            // The heat model is going to change considerably.
            // Will just do an unconfigurable quick and dirty way for now.
            FloatCurve loss = new FloatCurve();
            loss.Add(650, 0, 0, 0);
            loss.Add(1000, (float)(0.2 * lossTweak * ablativeResource));
			loss.Add(3000, (float)(0.3 * lossTweak * ablativeResource), 0, 0);

            FloatCurve dissipation = new FloatCurve();
            dissipation.Add(300, 0, 0, 0);
			dissipation.Add(500, (float)(80000 * dissipationTweak / ablativeResource), 0, 0);
            
            // Save it.
            PartModule modHeatShield = part.Modules["ModuleHeatShield"];

            Type type = modHeatShield.GetType();

            type.GetField("loss").SetValue(modHeatShield, loss);
            type.GetField("dissipation").SetValue(modHeatShield, dissipation);
        }

    }
}
