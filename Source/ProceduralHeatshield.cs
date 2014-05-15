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
            if(minDia > 0)
                UpdateDissipationAndLoss(minDia);
        }

        [KSPField]
        public string bottomNodeId = "bottom";

        [KSPField]
        public string topNodeId = "top";

        [KSPField(isPersistant=true)]
        public float minDia;

        private AttachNode bottomNode;
        private AttachNode topNode;
        
        [PartMessageListener(typeof(PartAttachNodeSizeChanged))]
        public void PartAttachNodeSizeChanged(AttachNode node, float minDia, float area) 
        {
            if (node.id != topNodeId)
                return;
            this.minDia = minDia;
            CopyNodeSizeAndStrength();
            if (GameSceneFilter.AnyEditor.IsLoaded())
                UpdateDissipationAndLoss(minDia);
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


        private void UpdateDissipationAndLoss(float minDia)
        {
            // The heat model is going to change considerably.
            // Will just do an unconfigurable quick and dirty way for now.
            FloatCurve loss = new FloatCurve();
            loss.Add(650, 0, 0, 0);
            loss.Add(1000, (float)Math.Round(40 * Math.Pow(minDia, 2), 1));
            loss.Add(3000, (float)Math.Round(50 * Math.Pow(minDia, 2), 1), 0, 0);

            FloatCurve dissipation = new FloatCurve();
            dissipation.Add(300, 0, 0, 0);
            dissipation.Add(500, (float)Math.Round(70 * Math.Pow(minDia, -2), 1), 0, 0);
            
            // Save it.
            PartModule modHeatShield = part.Modules["ModuleHeatShield"];

            Type type = modHeatShield.GetType();

            type.GetField("loss").SetValue(modHeatShield, loss);
            type.GetField("dissipation").SetValue(modHeatShield, dissipation);

            ConfigNode confNode = new ConfigNode();
            loss.Save(confNode);

            Debug.LogWarning("Updating float curves: minDia= " + minDia + "\n" + confNode);
        }

    }
}
