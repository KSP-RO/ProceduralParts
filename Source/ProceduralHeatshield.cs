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
        }

        [KSPField]
        public string bottomNodeId = "bottom";

        [KSPField]
        public string topNodeId = "top";

        private AttachNode bottomNode;
        private AttachNode topNode;
        
        [PartMessageListener(typeof(PartAttachNodeSizeChanged))]
        public void PartAttachNodeSizeChanged(AttachNode node, float minDia, float area) 
        {
            CopyNodeSizeAndStrength();
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

    }
}
