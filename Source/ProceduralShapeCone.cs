using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;

namespace ProceduralParts
{

    public class ProceduralShapeCone : ProceduralAbstractSoRShape
    {
        #region Config parameters

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Top", guiFormat = "S4", guiUnits="m"),
         UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.25f, incrementLarge = 1.25f, incrementSmall = 0.25f, incrementSlide = 0.001f)]
        public float topDiameter = 1.25f;
        protected float oldTopDiameter;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Bottom", guiFormat = "S4", guiUnits = "m"),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = 0.001f)]
        public float bottomDiameter = 1.25f;
        protected float oldBottomDiameter;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Length", guiFormat = "S4", guiUnits = "m"),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = 0.001f)]
        public float length = 1f;
        protected float oldLength;

        #endregion

        #region Limit paramters

        /// <summary>
        /// The mode for the cone end. This can be set for the top and bottom ends to constrain editing
        /// </summary>
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

        #region Implementation

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            try
            {
                coneTopMode = (ConeEndMode)Enum.Parse(typeof(ConeEndMode), node.GetValue("coneTopMode"), true);
            }
            catch { }

            try
            {
                coneBottomMode = (ConeEndMode)Enum.Parse(typeof(ConeEndMode), node.GetValue("coneBottomMode"), true);
            }
            catch { }
        }

        public override void OnStart(StartState state)
        {
            if (!HighLogic.LoadedSceneIsEditor)
                return;

            if (pPart.lengthMin == pPart.lengthMax || pPart.aspectMin == pPart.aspectMax)
                Fields["length"].guiActiveEditor = false;
            else
            {
                UI_FloatEdit lengthEdit = (UI_FloatEdit)Fields["length"].uiControlEditor;
                lengthEdit.maxValue = pPart.lengthMax;
                lengthEdit.minValue = pPart.lengthMin;
                lengthEdit.incrementLarge = pPart.lengthLargeStep;
                lengthEdit.incrementSmall = pPart.lengthSmallStep;
            }

            if (pPart.diameterMin == pPart.diameterMax || (coneTopMode == ConeEndMode.Constant && coneBottomMode == ConeEndMode.Constant))
            {
                Fields["topDiameter"].guiActiveEditor = false;
                Fields["bottomDiameter"].guiActiveEditor = false;
                return;
            }

            if (coneTopMode == ConeEndMode.Constant)
            {
                // Top diameter is constant
                Fields["topDiameter"].guiActiveEditor = false;
                Fields["bottomDiameter"].guiName = "Diameter";
            }
            else
            {
                UI_FloatEdit topDiameterEdit = (UI_FloatEdit)Fields["topDiameter"].uiControlEditor;
                topDiameterEdit.incrementLarge = pPart.diameterLargeStep;
                topDiameterEdit.incrementSmall = pPart.diameterSmallStep;
                topDiameterEdit.maxValue = pPart.diameterMax;
                topDiameterEdit.minValue = (coneTopMode == ConeEndMode.CanZero && coneBottomMode != ConeEndMode.Constant) ? 0 : pPart.diameterMin;
            }

            if (coneBottomMode == ConeEndMode.Constant)
            {
                // Bottom diameter is constant
                Fields["bottomDiameter"].guiActiveEditor = false;
                Fields["topDiameter"].guiName = "Diameter";
            }
            else
            {
                UI_FloatEdit bottomDiameterEdit = (UI_FloatEdit)Fields["bottomDiameter"].uiControlEditor;
                bottomDiameterEdit.incrementLarge = pPart.diameterLargeStep;
                bottomDiameterEdit.incrementSmall = pPart.diameterSmallStep;
                bottomDiameterEdit.maxValue = pPart.diameterMax;
                bottomDiameterEdit.minValue = (coneBottomMode == ConeEndMode.CanZero && coneTopMode != ConeEndMode.Constant) ? 0 : pPart.diameterMin;
            }

        }

        protected void MaintainParameterRelations()
        {
            if(bottomDiameter >= topDiameter)
                MaintainParameterRelations(ref bottomDiameter, ref oldBottomDiameter, ref topDiameter, ref oldTopDiameter, coneTopMode);
            else
                MaintainParameterRelations(ref topDiameter, ref oldTopDiameter, ref bottomDiameter, ref oldBottomDiameter, coneBottomMode);

        }

        private void MaintainParameterRelations(ref float bigEnd, ref float oldBigEnd, ref float smallEnd, ref float oldSmallEnd, ConeEndMode smallEndMode)
        {
            // Ensure the bigger end is not smaller than the min diameter
            if (bigEnd < pPart.diameterMin)
                bigEnd = pPart.diameterMin;

            // Aspect ratio stuff
            if (pPart.aspectMin == 0 && float.IsPositiveInfinity(pPart.aspectMax))
                return;

            float aspect = bigEnd / length;

            if (!MathUtils.TestClamp(ref aspect, pPart.aspectMin, pPart.aspectMax))
                return;

            if (bigEnd != oldBigEnd)
            {
                // Big end can push the length
                try
                {
                    length = MathUtils.RoundTo(bigEnd / aspect, 0.001f);

                    // The aspect is within range if false, we can safely return
                    if (!MathUtils.TestClamp(ref length, pPart.lengthMin, pPart.lengthMax))
                        return;

                    // If the aspect is fixed, then the length is dependent on the diameter anyhow
                    // so just return (we can still limit the total length if we want)
                    if (pPart.aspectMin == pPart.aspectMax)
                        return;

                    // Bottom has gone out of range, push back.
                    bigEnd = MathUtils.RoundTo(aspect * length, 0.001f);
                }
                finally
                {
                    oldLength = length;
                }
            }
            else if (length != oldLength)
            {
                try
                {
                    // Length can push the big end
                    bigEnd = MathUtils.RoundTo(aspect * length, 0.001f);

                    // The aspect is within range if true
                    if (!MathUtils.TestClamp(ref bigEnd, pPart.diameterMin, pPart.diameterMax))
                    {
                        // need to push back on the length
                        length = MathUtils.RoundTo(bigEnd / aspect, 0.001f);
                    }

                    // Delta the small end by the same amount.
                    if(smallEndMode != ConeEndMode.Constant) 
                    {
                        smallEnd += bigEnd - oldBigEnd;

                        if (smallEndMode == ConeEndMode.LimitMin && smallEnd < pPart.diameterMin)
                            smallEnd = pPart.diameterMin;
                    }
                }
                finally
                {
                    oldBigEnd = bigEnd;
                    oldSmallEnd = smallEnd;
                }
            }
            // The small end is ignored for aspects.
        }

        protected override void UpdateShape(bool force)
        {
            if (!force && oldTopDiameter == topDiameter && oldBottomDiameter == bottomDiameter && oldLength == length)
                return;

            // Maxmin the volume.
            if (HighLogic.LoadedSceneIsEditor)
            {
                MaintainParameterRelations();

                float volume = (Mathf.PI * length * (topDiameter * topDiameter + topDiameter * bottomDiameter + bottomDiameter * bottomDiameter)) / 12f;

                if (MathUtils.TestClamp(ref volume, pPart.volumeMin, pPart.volumeMax))
                {
                    if (oldLength != length)
                        length = volume * 12f / (Mathf.PI * (topDiameter * topDiameter + topDiameter * bottomDiameter + bottomDiameter * bottomDiameter));
                    else if(oldBottomDiameter != bottomDiameter)
                    {
                        // this becomes solving the quadratic on bottomDiameter
                        float a = length * Mathf.PI;
                        float b = length * Mathf.PI * topDiameter;
                        float c = length * Mathf.PI * topDiameter * topDiameter - volume * 12f;

                        float det = Mathf.Sqrt(b * b - 4 * a * c);
                        bottomDiameter = (det - b) / (2f * a);
                    }
                    else 
                    {
                        // this becomes solving the quadratic on topDiameter
                        float a = length * Mathf.PI;
                        float b = length * Mathf.PI * bottomDiameter;
                        float c = length * Mathf.PI * bottomDiameter * bottomDiameter - volume * 12f;

                        float det = Mathf.Sqrt(b * b - 4 * a * c);
                        topDiameter = (det - b) / (2f * a);
                    }
                }
                this.volume = volume;
            }
            else
            {
                volume = (Mathf.PI * length * (topDiameter * topDiameter + topDiameter * bottomDiameter + bottomDiameter * bottomDiameter)) / 12f;
            }

            // Perpendicular.
            Vector2 norm = new Vector2(length, (bottomDiameter - topDiameter) / 2f);
            norm.Normalize();

            WriteMeshes(
                new ProfilePoint(bottomDiameter, -0.5f * length, 0f, norm),
                new ProfilePoint(topDiameter, 0.5f * length, 1f, norm)
                );

            oldTopDiameter = topDiameter;
            oldBottomDiameter = bottomDiameter;
            oldLength = length;
        }
        #endregion
    }

}