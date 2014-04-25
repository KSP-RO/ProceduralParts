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

        public ConeEndMode coneTopMode = ConeEndMode.CanZero;
        public ConeEndMode coneBottomMode = ConeEndMode.CanZero;

        public enum ConeEndMode
        {
            CanZero, LimitMin, Constant,
        }

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

            if (pPart.lengthMin == pPart.lengthMax)
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
                topDiameterEdit.minValue = (coneTopMode == ConeEndMode.CanZero) ? 0 : pPart.diameterMin;
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
                bottomDiameterEdit.minValue = (coneBottomMode == ConeEndMode.CanZero) ? 0 : pPart.diameterMin;
            }

        }

        protected void MaintainParameterRelations()
        {
            if(bottomDiameter >= topDiameter)
                MaintainParameterRelations(ref bottomDiameter, oldBottomDiameter, ref topDiameter, oldTopDiameter);
            else
                MaintainParameterRelations(ref topDiameter, oldTopDiameter, ref bottomDiameter, oldBottomDiameter);

        }

        private void MaintainParameterRelations(ref float bigEnd, float oldBigEnd, ref float smallEnd, float oldSmallEnd)
        {
            // Ensure the bigger end is not smaller than the min diameter
            if (bigEnd < pPart.diameterMin)
                bigEnd = pPart.diameterMin;

            // Aspect ratio stuff
            if (pPart.aspectMin == 0 && float.IsPositiveInfinity(pPart.aspectMax))
                return;

            float aspect = (bigEnd - smallEnd) / length;

            if (!MathUtils.TestClamp(ref aspect, pPart.aspectMin, pPart.aspectMax))
                return;

            if (bigEnd != oldBigEnd)
            {
                // Big end can push the length
                try
                {
                    length = MathUtils.RoundTo((bigEnd - smallEnd) / aspect, 0.001f);

                    if (!MathUtils.TestClamp(ref length, pPart.lengthMin, pPart.lengthMax))
                        return;

                    // Bottom has gone out of range, push back.
                    bigEnd = MathUtils.RoundTo(aspect * length + smallEnd, 0.001f);
                }
                finally
                {
                    oldLength = length;
                }
            }
            else if (length != oldLength)
            {
                // Reset the length back in range.
                length = MathUtils.RoundTo((bigEnd - smallEnd) / aspect, 0.001f);
            }
            else if (smallEnd != oldSmallEnd)
            {
                // Just reset top diameter to extremum. 
                smallEnd = MathUtils.RoundTo(aspect * length + bigEnd, 0.001f);
            }
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
    }

}