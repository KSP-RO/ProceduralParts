using System;
using UnityEngine;

namespace ProceduralParts
{
    public class ProceduralShapeCone : ProceduralAbstractSoRShape
    {
        private static readonly string ModTag = "[ProceduralShapeCone]";
        public override Vector3 CoMOffset => CoMOffset_internal();

        #region Config parameters

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Top", guiFormat = "F3", guiUnits="m", groupName = ProceduralPart.PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, incrementLarge = 1.25f, incrementSmall = 0.25f, incrementSlide = SliderPrecision, sigFigs = 5, unit="m", useSI = true)]
        public float topDiameter = 1.25f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Bottom", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit="m", useSI = true)]
        public float bottomDiameter = 1.25f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Length", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit="m", useSI = true)]
        public float length = 1f;

        #endregion

        #region Limit paramters

        /// <summary>
        /// The mode for the cone end. This can be set for the top and bottom ends to constrain editing
        /// </summary>
        [Serializable]
        public enum ConeEndMode
        {
            /// <summary>
            /// If this end is the small end, it can be reduced down to zero and is unaffected by the <see cref="ProceduralPart.diameterMin"/>.
            /// If the end is the big end then it will be still be limited by the minimum diameter.
            /// The default mode. 
            /// </summary>
            CanZero, 
            /// <summary>
            /// Limit the minimum diameter regardless of if this is the big or the small end. Useful for parts that
            /// must have something attached to the end such as SRBs.
            /// </summary>
            LimitMin, 
            /// <summary>
            /// The diameter is fixed and cannot be changed. Set the diameter in the config file.
            /// </summary>
            Constant,
        }

        /// <summary>
        /// Limit mode for the top end of the cone
        /// </summary>
        public ConeEndMode coneTopMode = ConeEndMode.CanZero;
        /// <summary>
        /// Limit mode for the bottom end of the cone.
        /// </summary>
        public ConeEndMode coneBottomMode = ConeEndMode.CanZero;

        #endregion

        #region Initialization

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            if (!HighLogic.LoadedSceneIsFlight)
            {
                try
                {
                    coneTopMode = (node.HasValue("coneTopMode")) ?
                        (ConeEndMode) Enum.Parse(typeof(ConeEndMode), node.GetValue("coneTopMode"), true) :
                        ConeEndMode.CanZero;
                    coneBottomMode = (node.HasValue("coneBottomMode")) ?
                        (ConeEndMode)Enum.Parse(typeof(ConeEndMode), node.GetValue("coneBottomMode"), true) :
                        ConeEndMode.CanZero;
                }
                catch
                {
                    Debug.Log($"{ModTag} Invalid coneTopMode or coneBottomMode set for {this} in {node}");
                    coneTopMode = coneBottomMode = ConeEndMode.CanZero;
                }
            }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (HighLogic.LoadedSceneIsEditor)
            {
                UpdateTechConstraints();
                Fields[nameof(topDiameter)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;
                Fields[nameof(bottomDiameter)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;
                Fields[nameof(length)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;
            }
        }

        public override void UpdateTechConstraints()
        {
            Fields[nameof(length)].guiActiveEditor = PPart.lengthMin != PPart.lengthMax;
            UI_FloatEdit lengthEdit = Fields[nameof(length)].uiControlEditor as UI_FloatEdit;
            lengthEdit.incrementLarge = PPart.lengthLargeStep;
            lengthEdit.incrementSmall = PPart.lengthSmallStep;

            UI_FloatEdit topDiameterEdit = Fields[nameof(topDiameter)].uiControlEditor as UI_FloatEdit;
            topDiameterEdit.incrementLarge = PPart.diameterLargeStep;
            topDiameterEdit.incrementSmall = PPart.diameterSmallStep;

            UI_FloatEdit bottomDiameterEdit = Fields[nameof(bottomDiameter)].uiControlEditor as UI_FloatEdit;
            bottomDiameterEdit.incrementLarge = PPart.diameterLargeStep;
            bottomDiameterEdit.incrementSmall = PPart.diameterSmallStep;

            if (PPart.diameterMin == PPart.diameterMax || (coneTopMode == ConeEndMode.Constant && coneBottomMode == ConeEndMode.Constant))
            {
                Fields[nameof(topDiameter)].guiActiveEditor = false;
                Fields[nameof(bottomDiameter)].guiActiveEditor = false;
            }
            else
            {
                if (coneTopMode == ConeEndMode.Constant)
                {
                    Fields[nameof(topDiameter)].guiActiveEditor = false;
                    Fields[nameof(bottomDiameter)].guiName = "Diameter";
                }
                if (coneBottomMode == ConeEndMode.Constant)
                {
                    Fields[nameof(bottomDiameter)].guiActiveEditor = false;
                    Fields[nameof(topDiameter)].guiName = "Diameter";
                }
            }
            AdjustDimensionBounds();
            length = Mathf.Clamp(length, lengthEdit.minValue, lengthEdit.maxValue);
            topDiameter = Mathf.Clamp(topDiameter, topDiameterEdit.minValue, topDiameterEdit.maxValue);
            bottomDiameter = Mathf.Clamp(bottomDiameter, bottomDiameterEdit.minValue, bottomDiameterEdit.maxValue);
        }

        #endregion

        #region Update handlers

        internal override void UpdateShape(bool force = true)
        {
            part.CoMOffset = CoMOffset;
            Volume = CalculateVolume();
            Vector2 norm = new Vector2(length, (bottomDiameter - topDiameter) / 2f);
            norm.Normalize();

            WriteMeshes(
                new ProfilePoint(bottomDiameter, -0.5f * length, 0f, norm),
                new ProfilePoint(topDiameter, 0.5f * length, 1f, norm)
                );
        }

        public override void AdjustDimensionBounds()
        {
            // V = (PI * length * (topDiameter * topDiameter + topDiameter * bottomDiameter + bottomDiameter * bottomDiameter)) / 12f;
            // V = PI * L * diamQuad / 12
            // L = 12 * V / (PI * diamQuad)
            // diamQuad = 12 * V / (PI * L)

            float diamQuad = (topDiameter * topDiameter + topDiameter * bottomDiameter + bottomDiameter * bottomDiameter);
            float maxTopDiameter = PPart.diameterMax;
            float maxBottomDiameter = PPart.diameterMax;
            float maxLength = PPart.lengthMax;

            float minTopDiameter = (coneTopMode == ConeEndMode.CanZero && coneBottomMode != ConeEndMode.Constant) ? 0 : PPart.diameterMin;
            float minBottomDiameter = (coneBottomMode == ConeEndMode.CanZero && coneTopMode != ConeEndMode.Constant) ? 0 : PPart.diameterMin;
            float minLength = PPart.lengthMin;

            if (PPart.volumeMax < float.PositiveInfinity)
            {
                // Solutions for top and bottom diameter are the symmetric.
                if (CalculateVolume(length, topDiameter, PPart.diameterMax) > PPart.volumeMax)
                {
                    float maxDiamQuad = PPart.volumeMax * 12 / (Mathf.PI * length);
                    float a = 1f;
                    float b = topDiameter;
                    float c = topDiameter * topDiameter - maxDiamQuad;
                    // = top^2 + (top*bot) + (bottom^2)
                    // top^2 + (top*bot) + (bottom^2-maxdiamquad) = 0
                    // -b +/- sqrt(b^2-4ac)/2a
                    float det = Mathf.Sqrt((b * b) - (4 * a * c));
                    maxBottomDiameter = (det - b) / (a * 2);
                }
                if (CalculateVolume(length, PPart.diameterMax, bottomDiameter) > PPart.volumeMax)
                {
                    float maxDiamQuad = PPart.volumeMax * 12 / (Mathf.PI * length);
                    float a = 1f;
                    float b = bottomDiameter;
                    float c = bottomDiameter * bottomDiameter - maxDiamQuad;
                    float det = Mathf.Sqrt((b * b) - (4 * a * c));
                    maxTopDiameter = (det - b) / (a * 2);
                }
                if (CalculateVolume(PPart.lengthMax, topDiameter, bottomDiameter) > PPart.volumeMax)
                    maxLength = 12 * PPart.volumeMax / (Mathf.PI * diamQuad);
            }
            if (PPart.volumeMin > 0)
            {
                // Solutions for top and bottom diameter are the symmetric.
                if (CalculateVolume(length, topDiameter, PPart.diameterMin) < PPart.volumeMin)
                {
                    float minDiamQuad = PPart.volumeMin * 12 / (Mathf.PI * length);
                    float a = 1f;
                    float b = topDiameter;
                    float c = topDiameter * topDiameter - minDiamQuad;
                    // = top^2 + (top*bot) + (bottom^2)
                    // top^2 + (top*bot) + (bottom^2-maxdiamquad) = 0
                    // -b +/- sqrt(b^2-4ac)/2a
                    float det = Mathf.Sqrt((b * b) - (4 * a * c));
                    minBottomDiameter = (det - b) / (a * 2);
                }
                if (CalculateVolume(length, PPart.diameterMin, bottomDiameter) < PPart.volumeMin)
                {
                    float minDiamQuad = PPart.volumeMin * 12 / (Mathf.PI * length);
                    float a = 1f;
                    float b = bottomDiameter;
                    float c = bottomDiameter * bottomDiameter - minDiamQuad;
                    float det = Mathf.Sqrt((b * b) - (4 * a * c));
                    minTopDiameter = (det - b) / (a * 2);
                }
                if (CalculateVolume(PPart.lengthMin, topDiameter, bottomDiameter) < PPart.volumeMin)
                    minLength = 12 * PPart.volumeMin / (Mathf.PI * diamQuad);
            }

            maxLength = Mathf.Clamp(maxLength, PPart.lengthMin, PPart.lengthMax);
            maxTopDiameter = Mathf.Clamp(maxTopDiameter, PPart.diameterMin, PPart.diameterMax);
            maxBottomDiameter = Mathf.Clamp(maxBottomDiameter, PPart.diameterMin, PPart.diameterMax);

            minLength = Mathf.Clamp(minLength, PPart.lengthMin, PPart.lengthMax - PPart.lengthSmallStep);
            minTopDiameter = Mathf.Clamp(minTopDiameter, PPart.diameterMin, PPart.diameterMax - PPart.diameterSmallStep);
            minBottomDiameter = Mathf.Clamp(minBottomDiameter, PPart.diameterMin, PPart.diameterMax - PPart.diameterSmallStep);

            (Fields[nameof(topDiameter)].uiControlEditor as UI_FloatEdit).maxValue = maxTopDiameter;
            (Fields[nameof(bottomDiameter)].uiControlEditor as UI_FloatEdit).maxValue = maxBottomDiameter;
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).maxValue = maxLength;

            (Fields[nameof(topDiameter)].uiControlEditor as UI_FloatEdit).minValue = minTopDiameter;
            (Fields[nameof(bottomDiameter)].uiControlEditor as UI_FloatEdit).minValue = minBottomDiameter;
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).minValue = minLength;
        }

        public override float CalculateVolume() => CalculateVolume(length, topDiameter, bottomDiameter);
        public virtual float CalculateVolume(float length, float topDiameter, float bottomDiameter)
        {
            return (Mathf.PI * length * (topDiameter * topDiameter + topDiameter * bottomDiameter + bottomDiameter * bottomDiameter)) / 12f;
        }
        public override bool SeekVolume(float targetVolume) => SeekVolume(targetVolume, Fields[nameof(length)]);

        private Vector3 CoMOffset_internal()
        {
            //h * (B^2 + 2BT + 3T^2) / 4 * (B^2 + BT + T^2)
            float num = Mathf.Pow(bottomDiameter, 2) + (2 * bottomDiameter * topDiameter) + (3 * Mathf.Pow(topDiameter, 2));
            float denom = 4 * (Mathf.Pow(bottomDiameter, 2) + (bottomDiameter * topDiameter) + Mathf.Pow(topDiameter, 2));
            Vector3 res = new Vector3(0, length * ((num / denom) - 0.5f), 0);
            return res;
        }

        public override void UpdateTFInterops()
        {
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam1", topDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam2", bottomDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "length", length, "ProceduralParts" });
        }

        public override void TranslateAttachmentsAndNodes(BaseField f, object obj)
        {
            if (f.name == nameof(topDiameter))
            {
                HandleDiameterChange(f, obj);
            }
            else if (f.name == nameof(bottomDiameter))
            {
                HandleDiameterChange(f, obj);
            }
            if (f.name == nameof(length) && obj is float oldLen)
            {
                HandleLengthChange((float)f.GetValue(this), oldLen);
            }
        }

        private void HandleDiameterChange(BaseField f, object obj)
        {
            if ((f.name == nameof(topDiameter) || f.name == nameof(bottomDiameter)) && obj is float prevDiam)
            {
                // Nothing to do for stack-attached nodes.
                float oldTopDiameter = (f.name == nameof(topDiameter)) ? prevDiam : topDiameter;
                float oldBottomDiameter = (f.name == nameof(bottomDiameter)) ? prevDiam : bottomDiameter;
                foreach (Part p in part.children)
                {
                    if (p.FindAttachNodeByPart(part) is AttachNode node && node.nodeType == AttachNode.NodeType.Surface)
                    {
                        GetAttachmentNodeLocation(node, out Vector3 worldSpace, out Vector3 localToHere, out ShapeCoordinates coord);
                        float y_from_bottom = coord.y + (length / 2);
                        float oldDiameterAtY = Mathf.Lerp(oldBottomDiameter, oldTopDiameter, y_from_bottom / length);
                        float newDiameterAtY = Mathf.Lerp(bottomDiameter, topDiameter, y_from_bottom / length);
                        float ratio = newDiameterAtY / oldDiameterAtY;
                        coord.r *= ratio;
                        MovePartByAttachNode(node, coord);
                    }
                }
            }
        }

        internal override void InitializeAttachmentNodes() => InitializeAttachmentNodes(length, (topDiameter + bottomDiameter) / 2);

        public override void NormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r /= (bottomDiameter / 2);
            coords.y /= length;
        }

        public override void UnNormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r *= (bottomDiameter / 2);
            coords.y *= length;
        }
        #endregion

    }
}