using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;

namespace ProceduralParts
{
    public class ProceduralShapeBezierOffset : ProceduralAbstractShape
    {
        private const string ModTag = "[ProceduralShapeBezierOffset]";
        public override Vector3 CoMOffset => CoMOffset_internal();
        private const string CustomShapeName = "Custom";

        #region Config parameters

        internal class ShapePreset
        {
            [Persistent] public string name;
            [Persistent] public string displayName;
            [Persistent] public Vector4 points;
        }

        internal static readonly Dictionary<string, ShapePreset> shapePresets = new Dictionary<string, ShapePreset>();
        private ShapePreset selectedPreset;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Curve", groupName = ProceduralPart.PAWGroupName),
         UI_ChooseOption(scene = UI_Scene.Editor)]
        public string selectedShape;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Top", guiFormat = "F3", guiUnits="m", groupName = ProceduralPart.PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit="m", useSI = true)]
        public float topDiameter = 1.25f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Bottom", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit="m", useSI = true)]
        public float bottomDiameter = 1.25f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Length", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float length = 1f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Offset", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float offset = 0f;

        [KSPField(isPersistant = true)]
        public Vector4 shapePoints;

        [KSPField(guiName = "Curve.x", guiFormat = "F3", groupName = ProceduralPart.PAWGroupName)]
        [UI_FloatEdit(incrementSlide = 0.01f, sigFigs = 2, maxValue = 1, minValue = 0)]
        public float curve_x;

        [KSPField(guiName = "Curve.y", guiFormat = "F3", groupName = ProceduralPart.PAWGroupName)]
        [UI_FloatEdit(incrementSlide = 0.01f, sigFigs = 2, maxValue = 1, minValue = 0)]
        public float curve_y;

        [KSPField(guiName = "Curve.z", guiFormat = "F3", groupName = ProceduralPart.PAWGroupName)]
        [UI_FloatEdit(incrementSlide = 0.01f, sigFigs = 2, maxValue = 1, minValue = 0)]
        public float curve_z;

        [KSPField(guiName = "Curve.w", guiFormat = "F3", groupName = ProceduralPart.PAWGroupName)]
        [UI_FloatEdit(incrementSlide = 0.01f, sigFigs = 2, maxValue = 1, minValue = 0)]
        public float curve_w;

        #endregion

        #region Helper Funcs

        internal const float MaxCircleError = 0.01f;

        internal const int MinCircleVertexes = 12;

        internal const float MaxDiameterChange = 5.0f;

        public int numSides => (int)Math.Max(Mathf.PI * Mathf.Sqrt(Mathf.Sqrt((Math.Max(bottomDiameter, topDiameter)))/(2f * MaxCircleError)), 24);

        private float CornerCenterCornerAngle => 2 * Mathf.PI / numSides;

        private float NormSideLength => Mathf.Tan(CornerCenterCornerAngle / 2);

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
            InitializeSelectedShape();
        }

        public override void OnStart(StartState state)
        {
            Debug.Log("[ProcPartsDebug] OnStart called");
            InitializeSelectedShape();
            base.OnStart(state);
            if (HighLogic.LoadedSceneIsEditor)
            {
                UI_ChooseOption opt = Fields[nameof(selectedShape)].uiControlEditor as UI_ChooseOption;
                opt.options = shapePresets.Values.Select(x => x.name).ToArray();
                opt.display = shapePresets.Values.Select(x => x.displayName).ToArray();
                opt.onSymmetryFieldChanged += OnShapeSelectionChanged;
                opt.onFieldChanged += OnShapeSelectionChanged;

                Fields[nameof(length)].uiControlEditor.onFieldChanged += OnShapeDimensionChanged;
                Fields[nameof(offset)].uiControlEditor.onFieldChanged += OnShapeDimensionChanged;
                Fields[nameof(bottomDiameter)].uiControlEditor.onFieldChanged += OnShapeDimensionChanged;
                Fields[nameof(topDiameter)].uiControlEditor.onFieldChanged += OnShapeDimensionChanged;

                Fields[nameof(curve_x)].uiControlEditor.onFieldChanged += OnCurveChanged;
                Fields[nameof(curve_y)].uiControlEditor.onFieldChanged += OnCurveChanged;
                Fields[nameof(curve_z)].uiControlEditor.onFieldChanged += OnCurveChanged;
                Fields[nameof(curve_w)].uiControlEditor.onFieldChanged += OnCurveChanged;

                SetFieldVisibility();
            }
        }

        private void InitializeSelectedShape()
        {
            InitializePresets();
            if (string.IsNullOrEmpty(selectedShape) || !shapePresets.ContainsKey(selectedShape))
            {
                Debug.Log($"{ModTag} InitializeSelectedShape() Shape {selectedShape} not available, defaulting to {shapePresets.Keys.First()}");
                selectedShape = shapePresets.Keys.First();
            }
            selectedPreset = shapePresets[selectedShape];
            if (!selectedPreset.name.Equals(CustomShapeName))
                shapePoints = selectedPreset.points;
            else
                (curve_x, curve_y, curve_z, curve_w) = (shapePoints.x, shapePoints.y, shapePoints.z, shapePoints.w);
        }

        private void InitializePresets()
        {
            if (shapePresets.Count == 0 &&
                GameDatabase.Instance.GetConfigNode("ProceduralParts/ProceduralParts/ProceduralPartsSettings") is ConfigNode settings)
            {
                foreach (var shapeNode in settings.GetNodes("Shape"))
                {
                    var shape = new ShapePreset();
                    if (ConfigNode.LoadObjectFromConfig(shape, shapeNode))
                        shapePresets.Add(shape.name, shape);
                }
                var s = new ShapePreset
                {
                    name = CustomShapeName,
                    displayName = CustomShapeName,
                    points = Vector4.zero
                };
                shapePresets.Add(s.name, s);
            }
        }

        public override void UpdateTechConstraints()
        {
            InitializeSelectedShape();
        }

        #endregion

        #region Update handlers

        private void OnCurveChanged(BaseField f, object obj)
        {
            shapePoints = new Vector4(curve_x, curve_y, curve_z, curve_w);
            OnShapeChanged(f, obj);
        }

        public void OnShapeSelectionChanged(BaseField f, object obj)
        {
            selectedPreset = shapePresets[selectedShape];
            shapePoints = selectedPreset.points;
            SetFieldVisibility();
            OnShapeChanged(f, obj, true);
        }

        public void OnShapeChanged(BaseField f, object obj, bool forceRefresh = false)
        {
            float v = CalculateVolume();
            if (v > PPart.volumeMax || v < PPart.volumeMin)
            {
                UI_FloatEdit edt = Fields[nameof(length)].uiControlEditor as UI_FloatEdit;
                length = edt.minValue;
                AdjustDimensionBounds();
                length = Mathf.Clamp(length, edt.minValue, edt.maxValue);
            }
            OnShapeDimensionChanged(f, obj);
            if (forceRefresh)
                MonoUtilities.RefreshPartContextWindow(part);
        }

        private void SetFieldVisibility()
        {
            Fields[nameof(selectedShape)].guiActiveEditor = PPart.allowCurveTweaking;
            bool showPointEditor = selectedPreset.name == CustomShapeName && PPart.allowCurveTweaking;
            Fields[nameof(curve_x)].guiActiveEditor = showPointEditor;
            Fields[nameof(curve_y)].guiActiveEditor = showPointEditor;
            Fields[nameof(curve_z)].guiActiveEditor = showPointEditor;
            Fields[nameof(curve_w)].guiActiveEditor = showPointEditor;
        }

        internal override void UpdateShape(bool force = true)
        {
            Debug.Log("[ProcPartsDebug] UpdateShape called");
            Volume = CalculateVolume();
            SetControlPoints(length, topDiameter, bottomDiameter);
            // Ensure correct control points if something called AdjustDimensionBounds or CalculateVolume when handling volume change event
            WriteBezier();
            part.CoMOffset = CoMOffset;
            // WriteMeshes in AbstractSoRShape typically does UpdateNodeSize, UpdateProps, RaiseModelAndColliderChanged
            // UpdateNodeSize(TopNodeName);
            // UpdateNodeSize(BottomNodeName);
            PPart.UpdateProps();
            RaiseModelAndColliderChanged();
        }

        public override void AdjustDimensionBounds()
        {
            float maxLength = PPart.lengthMax;
            float maxBottomDiameter = PPart.diameterMax;
            float maxTopDiameter = PPart.diameterMax;
            float minLength = PPart.lengthMin;
            float minTopDiameter = (coneTopMode == ConeEndMode.CanZero && coneBottomMode != ConeEndMode.Constant) ? 0 : PPart.diameterMin;
            float minBottomDiameter = (coneBottomMode == ConeEndMode.CanZero && coneTopMode != ConeEndMode.Constant) ? 0 : PPart.diameterMin;
            // float minTopDiameter = 0;
            // float minBottomDiameter = 0;

            float minBottomDiameterOrig = minBottomDiameter;
            float minTopDiameterOrig = minTopDiameter;

            if (PPart.volumeMax < float.PositiveInfinity)
            {
                maxBottomDiameter = bottomDiameter;
                maxTopDiameter = topDiameter;
                maxLength = length;
                IterateVolumeLimits(ref maxTopDiameter, ref maxBottomDiameter, ref maxLength, PPart.volumeMax, IteratorIncrement);
            }
            if (PPart.volumeMin > 0)
            {
                minBottomDiameter = bottomDiameter;
                minTopDiameter = topDiameter;
                minLength = length;
                IterateVolumeLimits(ref minTopDiameter, ref minBottomDiameter, ref minLength, PPart.volumeMin, IteratorIncrement);
            }

            maxLength = Mathf.Clamp(maxLength, PPart.lengthMin, PPart.lengthMax);
            maxTopDiameter = Mathf.Clamp(maxTopDiameter, PPart.diameterMin, PPart.diameterMax);
            maxBottomDiameter = Mathf.Clamp(maxBottomDiameter, PPart.diameterMin, PPart.diameterMax);
            minLength = Mathf.Clamp(minLength, PPart.lengthMin, PPart.lengthMax - PPart.lengthSmallStep);
            minTopDiameter = Mathf.Clamp(minTopDiameter, minTopDiameterOrig, PPart.diameterMax - PPart.diameterSmallStep);
            minBottomDiameter = Mathf.Clamp(minBottomDiameter, minBottomDiameterOrig, PPart.diameterMax - PPart.diameterSmallStep);

            (Fields[nameof(topDiameter)].uiControlEditor as UI_FloatEdit).maxValue = maxTopDiameter;
            (Fields[nameof(bottomDiameter)].uiControlEditor as UI_FloatEdit).maxValue = maxBottomDiameter;
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).maxValue = maxLength;
            (Fields[nameof(topDiameter)].uiControlEditor as UI_FloatEdit).minValue = minTopDiameter;
            (Fields[nameof(bottomDiameter)].uiControlEditor as UI_FloatEdit).minValue = minBottomDiameter;
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).minValue = minLength;
            Debug.Log($"[ProcPartsDebug] MinLength = {minLength}");
        }

        // IteratorIncrement is approximately 1/1000.
        // For any sizable volume limit, simple iteration will be very slow.
        // Instead, search by variable increment.  Minimal harm in setting scale default high, because
        // early passes will terminate on first loop.
        private void IterateVolumeLimits(ref float top, ref float bottom, ref float len, float target, float inc, int scale=6)
        {
            if (inc <= 0) return;

            float originalTop = top, originalBottom = bottom, originalLen = len;
            top = bottom = len = 0;
            while (scale-- >= 0)
            {
                float curInc = inc * Mathf.Pow(10, scale);
                while (CalculateVolume(len + curInc, originalTop, originalBottom) < target && len + curInc < PPart.lengthMax)
                {
                    len += curInc;
                }
                while (CalculateVolume(originalLen, top + curInc, originalBottom) < target && top + curInc < PPart.diameterMax)
                {
                    top += curInc;
                }
                while (CalculateVolume(originalLen, originalTop, bottom + curInc) < target && bottom + curInc < PPart.diameterMax)
                {
                    bottom += curInc;
                }
            }
        }

        public override float CalculateVolume() => CalculateVolume(length, topDiameter, bottomDiameter);
        public float CalculateVolume(float length, float topDiameter, float bottomDiameter)
        {
            SetControlPoints(length, topDiameter, bottomDiameter);
            // The maths for the area under the bezier can be calculated using 
            // pappus's centroid theroem: http://mathworld.wolfram.com/PappussCentroidTheorem.html
            // V = 2pi * Area of lamina * x geometric centroid y axis
            // geometric centroid about y axis = Moment about y / Area of curve
            // so area under curve ends up factoring out.

            // factor 1/2 taken out. The 1/4 is because the pN.x are diameters not radii.
            // ReSharper disable once InconsistentNaming
            float M_y  = 1f/4f* ((p1.y-p0.y)*(1f/3f * p0.x*p0.x + 1f/4f * p0.x*p1.x + 1f/14f* p0.x*p2.x + 3f/28f* p1.x*p1.x+ 3f/28f * p1.x*p2.x + 1f/84f * p0.x*p3.x + 3f/70f * p2.x*p2.x + 1f/35f* p1.x*p3.x + 1f/28f* p2.x*p3.x + 1f/84f* p3.x*p3.x) +
                                 (p2.y-p1.y)*(1f/12f* p0.x*p0.x + 1f/7f * p0.x*p1.x + 1f/14f* p0.x*p2.x + 3f/28f* p1.x*p1.x+ 6f/35f * p1.x*p2.x + 2f/105f* p0.x*p3.x + 3f/28f * p2.x*p2.x + 1f/14f* p1.x*p3.x + 1f/7f * p2.x*p3.x + 1f/12f* p3.x*p3.x) +
                                 (p3.y-p2.y)*(1f/84f* p0.x*p0.x + 1f/28f* p0.x*p1.x + 1f/35f* p0.x*p2.x + 3f/70f* p1.x*p1.x+ 3f/28f * p1.x*p2.x + 1f/84f * p0.x*p3.x + 3f/28f * p2.x*p2.x + 1f/14f* p1.x*p3.x + 1f/4f * p2.x*p3.x + 1f/3f * p3.x*p3.x));

            return Mathf.PI * M_y;
        }

        public override bool SeekVolume(float targetVolume, int dir=0) => SeekVolume(targetVolume, Fields[nameof(length)], dir);

        public override void TranslateAttachmentsAndNodes(BaseField f, object obj)
        {
            if (f.name == nameof(bottomDiameter) && obj is float oldBottomDiameter)
            {
                HandleDiameterChange((bottomDiameter + topDiameter) / 2, (oldBottomDiameter + topDiameter) / 2);
            }
            if (f.name == nameof(topDiameter) && obj is float oldTopDiameter)
            {
                HandleDiameterChange((topDiameter + bottomDiameter) / 2, (oldTopDiameter + bottomDiameter) / 2);
            }
            if (f.name == nameof(length) && obj is float oldLength)
            {
                HandleLengthChange(length, oldLength);
            }
            if (f.name == nameof(offset) && obj is float oldOffset)
            {
                HandleOffsetChange(offset, oldOffset);
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

        private void HandleOffsetChange(float offset, float oldOffset)
        {
            float trans = offset - oldOffset;
            foreach (AttachNode node in part.attachNodes)
            {
                // Our nodes are relative to part center 0,0,0.  position.y > 0 are top nodes.
                if (node.position.y > 0 && node.nodeType == AttachNode.NodeType.Stack)
                {
                    Vector3 translation = trans * Vector3.forward;
                    TranslateNode(node, translation);
                    if (node.attachedPart is Part pushTarget)
                    {
                        TranslatePart(pushTarget, translation);
                    }
                }
            }

            // TODO: translate surface attached parts when changing offset
            // foreach (Part p in part.children)
            // {
            //     if (p.FindAttachNodeByPart(part) is AttachNode node && node.nodeType == AttachNode.NodeType.Surface)
            //     {
            //         GetAttachmentNodeLocation(node, out Vector3 worldSpace, out Vector3 localToHere, out ShapeCoordinates coord);
            //         float ratio = offset / oldOffset;
            //         coord.y *= ratio;
            //         MovePartByAttachNode(node, coord);
            //     }
            // }
        }

        private Vector3 CoMOffset_internal()
        {
            // Does not handle horizontal offset, only vertical

            //CoM integral over x(t)^2*y'(t):
            // int_0->1:y(t)*x(t)^2*y'(t) dt / int_0->1:x(t)^2*y'(t) dt
            //where x(t), x'(t), y(t) are from the Bezier function

            //Function below derived using symbolic math in Matlab
            float x0 = p0.x, x1 = p1.x, x2 = p2.x, x3 = p3.x, y0 = p0.y, y1 = p1.y, y2 = p2.y, y3 = p3.y;
            float num = (2310f*y0*y0 - 1260f*y0*y1 - 252f*y0*y2 - 28f*y0*y3 - 378f*y1*y1 - 252f*y1*y2 - 42f*y1*y3 - 63f*y2*y2 - 30f*y2*y3 - 5f*y3*y3)*x0*x0 +
                        (1260f*x1*y0*y0 - 252f*x1*y1*y1 + 252f*x2*y0*y0 - 162f*x1*y2*y2 + 28f*x3*y0*y0 - 30f*x1*y3*y3 - 90f*x2*y2*y2 + 18f*x3*y1*y1 - 42f*x2*y3*y3 - 18f*x3*y2*y2 - 28f*x3*y3*y3 - 168f*x1*y0*y2 +
                        168f*x2*y0*y1 - 42f*x1*y0*y3 - 378f*x1*y1*y2 + 42f*x3*y0*y1 - 108f*x1*y1*y3 - 12f*x2*y0*y3 - 108f*x2*y1*y2 + 12f*x3*y0*y2 - 120f*x1*y2*y3 - 60f*x2*y1*y3 - 108f*x2*y2*y3 - 12f*x3*y1*y3 - 42f*x3*y2*y3)*x0 +
                        378f*x1*x1*y0*y0 + 252f*x1*x1*y0*y1 - 18f*x1*x1*y0*y3 - 162f*x1*x1*y1*y2 - 90f*x1*x1*y1*y3 - 135f*x1*x1*y2*y2 - 162f*x1*x1*y2*y3 - 63f*x1*x1*y3*y3 + 252f*x1*x2*y0*y0 + 378f*x1*x2*y0*y1 + 108f*x1*x2*y0*y2 +
                        162f*x1*x2*y1*y1 - 108f*x1*x2*y1*y3 - 162f*x1*x2*y2*y2 - 378f*x1*x2*y2*y3 - 252f*x1*x2*y3*y3 + 42f*x1*x3*y0*y0 + 108f*x1*x3*y0*y1 + 60f*x1*x3*y0*y2 + 12f*x1*x3*y0*y3 + 90f*x1*x3*y1*y1 + 108f*x1*x3*y1*y2 -
                        168f*x1*x3*y2*y3 - 252f*x1*x3*y3*y3 + 63f*x2*x2*y0*y0 + 162f*x2*x2*y0*y1 + 90f*x2*x2*y0*y2 + 18f*x2*x2*y0*y3 + 135f*x2*x2*y1*y1 + 162f*x2*x2*y1*y2 - 252f*x2*x2*y2*y3 - 378f*x2*x2*y3*y3 + 30f*x2*x3*y0*y0 +
                        120f*x2*x3*y0*y1 + 108f*x2*x3*y0*y2 + 42f*x2*x3*y0*y3 + 162f*x2*x3*y1*y1 + 378f*x2*x3*y1*y2 + 168f*x2*x3*y1*y3 + 252f*x2*x3*y2*y2 - 1260f*x2*x3*y3*y3 + 5f*x3*x3*y0*y0 + 30f*x3*x3*y0*y1 + 42f*x3*x3*y0*y2 +
                        28f*x3*x3*y0*y3 + 63f*x3*x3*y1*y1 + 252f*x3*x3*y1*y2 + 252f*x3*x3*y1*y3 + 378f*x3*x3*y2*y2 + 1260f*x3*x3*y2*y3 - 2310f*x3*x3*y3*y3;

            float den = (3080f*y0 - 2310f*y1 - 660f*y2 - 110f*y3)*x0*x0 +
                        (2310f*x1*y0 - 990f*x1*y1 + 660f*x2*y0 - 990f*x1*y2 + 110f*x3*y0 - 330f*x1*y3 - 396f*x2*y2 + 66f*x3*y1 - 264f*x2*y3 - 66f*x3*y2 - 110f*x3*y3)*x0 +
                        990f*x1*x1*y0 + 396f*x2*x2*y0 - 594f*x1*x1*y2 + 594f*x2*x2*y1 + 110f*x3*x3*y0 - 396f*x1*x1*y3 + 660f*x3*x3*y1 - 990f*x2*x2*y3 + 2310f*x3*x3*y2 -
                        3080f*x3*x3*y3 + 990f*x1*x2*y0 + 594f*x1*x2*y1 + 264f*x1*x3*y0 - 594f*x1*x2*y2 + 396f*x1*x3*y1 + 330f*x2*x3*y0 - 990f*x1*x2*y3 + 990f*x2*x3*y1 - 660f*x1*x3*y3 + 990f*x2*x3*y2 - 2310f*x2*x3*y3;

            return new Vector3(0, num/den, 0);
        }

        public override void UpdateTFInterops()
        {
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam1", topDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam2", bottomDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "length", length, "ProceduralParts" });
        }

        internal override void InitializeAttachmentNodes() => InitializeAttachmentNodes(length, (bottomDiameter + topDiameter) / 2, (node) => node.position.y>0, offset * Vector3.forward);

        public override void NormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            CalculateVolume();  // Update the bezier curve control nodes
            // The Bezier curve control points are NOT normalized.
            //  p0 = new Vector2(bottomDiameter, -length / 2f);
            //  p3 = new Vector2(topDiameter, length / 2f);
            // But we do need to normalize the length and shift such that 0 <= t <= 1
            float t = (coords.y / length) + (1f / 2);
            Vector2 Bt = B(t);
            Debug.Log($"{ModTag} Normalized {coords.y} to {t} (0=bottom, 1=top), B(t)={Bt} as (diameter, length). TopDiameter:{topDiameter} BotDiameter:{bottomDiameter} Len: {length}");
            // For a given normalized length (t), B(t).x is the diameter of the surface cross-section at that length, and B(t).y is .. what?

            coords.y /= length;
            coords.r /= (Bt.x / 2);
        }

        public override void UnNormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            CalculateVolume();  // Update the bezier curve control nodes
            float t = (coords.y + (1f / 2));    // Shift coords.y from [-0.5..0.5] to [0..1]
            Vector2 Bt = B(t);
            Debug.Log($"{ModTag} From normalized {coords.y}, shape unnormalized bottom {bottomDiameter} top {topDiameter} length {length},  B({t}) yields [{Bt.x:F3}, {Bt.y:F3}] as (diameter, length)");
            // B(t).x is the diameter of the curve at t.  /2 for radius.

            coords.y *= length;
            coords.r *= Bt.x / 2;
        }

        #endregion

        #region Control point calculation
        private Vector2 p0, p1, p2, p3;

        private void SetControlPoints(float length, float topDiameter, float bottomDiameter)
        {
            // So we have a rotated bezier curve from bottom to top.
            // There are four control points, the bottom (p0) and the top ones (p3) are obvious
            p0 = new Vector2(bottomDiameter, -length / 2f);
            p3 = new Vector2(topDiameter, length / 2f);
            float[] shape = { shapePoints.x, shapePoints.y, shapePoints.z, shapePoints.w };

            // Pretty obvious below what the shape points mean
            if (bottomDiameter < topDiameter)
            {
                p1 = new Vector2(Mathf.Lerp(p0.x, p3.x, shape[0]), Mathf.Lerp(p0.y, p3.y, shape[1]));
                p2 = new Vector2(Mathf.Lerp(p0.x, p3.x, shape[2]), Mathf.Lerp(p0.y, p3.y, shape[3]));
            }
            else
            {
                p2 = new Vector2(Mathf.Lerp(p3.x, p0.x, shape[0]), Mathf.Lerp(p3.y, p0.y, shape[1]));
                p1 = new Vector2(Mathf.Lerp(p3.x, p0.x, shape[2]), Mathf.Lerp(p3.y, p0.y, shape[3]));
            }
            SetOffsetControlPoints(length, offset);
        }

        private Vector2 op0, op1, op2, op3;

        private void SetOffsetControlPoints(float length, float offset)
        {
            // Debug.Log("[ProcPartsDebug] offset control points were set");
            // There are four control points, the bottom (p0) and the top ones (p3) are obvious
            op0 = new Vector2(0, -length / 2f);
            op3 = new Vector2(offset, length / 2f);

            // Currently hard coded to be identical with the shape - change later
            float[] shape = { shapePoints.x, shapePoints.y, shapePoints.z, shapePoints.w };

            if (bottomDiameter < topDiameter)
            {
                op1 = new Vector2(Mathf.Lerp(op0.x, op3.x, shape[0]), Mathf.Lerp(op0.y, op3.y, shape[1]));
                op2 = new Vector2(Mathf.Lerp(op0.x, op3.x, shape[2]), Mathf.Lerp(op0.y, op3.y, shape[3]));
            }
            else
            {
                op2 = new Vector2(Mathf.Lerp(op3.x, op0.x, shape[0]), Mathf.Lerp(op3.y, op0.y, shape[1]));
                op1 = new Vector2(Mathf.Lerp(op3.x, op0.x, shape[2]), Mathf.Lerp(op3.y, op0.y, shape[3]));
            }
        }



        #endregion

        #region Bezier Bits

        private void WriteBezier()
        {
            var points1 = GenPoints(1f);
            var points2 = GenPoints(-1f);

            var points = points1.Count > points2.Count ? points1 : points2;

            // Subdivide due to diameter changes that are straight lines
            SubdivHorizontal(points);
            // Figure out what parts of the cone are concave to split them into convex parts for colliders
            var splitsList = ConcavePoints(points);

            // Need to figure out the v coords.
            float sumLengths = 0;
            float[] cumLengths = new float[points.Count - 1];

            LinkedListNode<ProfilePoint> pv = points.First;
            LinkedListNode<ProfilePoint> nx = pv.Next;
            for (int i = 0; i < cumLengths.Length; ++i, pv = nx, nx = nx.Next)
            {
                // ReSharper disable once PossibleNullReferenceException
                float dX = nx.Value.x - pv.Value.x;
                float dY = nx.Value.y - pv.Value.y;

                cumLengths[i] = sumLengths += Mathf.Sqrt(dX * dX + dY * dY);
            }

            points.First.Value.v = 0;
            nx = points.First.Next;
            for (int i = 0; i < cumLengths.Length; ++i, nx = nx.Next)
            {
                // ReSharper disable once PossibleNullReferenceException
                nx.Value.v = cumLengths[i] / sumLengths;
            }

            BSolverGetT(p0.x, p1.x, p2.x, p3.x, 0.5f);
            WriteMeshes(points);
            WriteCollider(points, splitsList);
        }

        /// <summary>
        /// Subdivide profile points according to the max diameter change. 
        /// </summary>
        private void SubdivHorizontal(LinkedList<ProfilePoint> pts)
        {
            ProfilePoint prev = pts.First.Value;
            for (LinkedListNode<ProfilePoint> node = pts.First.Next; node != null; node = node.Next)
            {
                ProfilePoint curr = node.Value;
                float positiveChange = CreatePoint(curr.t, 1).x - CreatePoint(prev.t, 1).x;
                float negativeChange = CreatePoint(curr.t, -1).x - CreatePoint(prev.t, -1).x;
                float largestChange = Mathf.Max(Math.Abs(positiveChange), Math.Abs(negativeChange));
                float dPercentage = largestChange / (Math.Max(B(curr.t).x, B(prev.t).x) / 100.0f);
                int subdiv = Math.Min((int)(Math.Truncate(dPercentage / MaxDiameterChange)), 30);
                if (subdiv > 1)
                {
                    for (int i = 1; i < subdiv; ++i)
                    {
                        float frac = i / (float)subdiv;
                        float t = Mathf.Lerp(prev.t, curr.t, frac);

                        pts.AddBefore(node, CreatePoint(t, 1, false));
                    }
                }

                prev = curr;
            }
        }

        private LinkedList<int> ConcavePoints(LinkedList<ProfilePoint> points)
        {
            LinkedList<int> breakPoints = new LinkedList<int>();
            breakPoints.AddLast(0);
            Debug.Log($"BreakPoint: {0}");
            ProfilePoint lastLast1 = null;
            ProfilePoint prev1 = null;
            ProfilePoint lastLast2 = null;
            ProfilePoint prev2 = null;
            int prevIndex = 0;
            // counter starts at -1 because it's incremented at the start of the loop
            int counter = -1;
            foreach (var pt in points)
            {
                counter++;
                if (!pt.inCollider)
                    continue;

                var pt1 = CreatePoint(pt.t, 1);
                var pt2 = CreatePoint(pt.t, -1);
                if (lastLast1 != null)
                {
                    // check if "prev1" or "prev2" is concave
                    Vector2 norm1 = new Vector2(-lastLast1.y + pt1.y, lastLast1.x - pt1.x);
                    Vector2 diff1 = new Vector2(lastLast1.x - prev1.x, lastLast1.y - prev1.y);
                    var sign1 = Vector2.Dot(norm1, diff1);
                    Vector2 norm2 = new Vector2(-lastLast2.y + pt2.y, lastLast2.x - pt2.x);
                    Vector2 diff2 = new Vector2(lastLast2.x - prev2.x, lastLast2.y - prev2.y);
                    var sign2 = Vector2.Dot(norm2, diff2);
                    if (sign1 > 0 || sign2 > 0)
                    {
                        breakPoints.AddLast(prevIndex);
                        Debug.Log($"BreakPoint: {prevIndex}");
                    }
                }
                prevIndex = counter;
                lastLast1 = prev1;
                prev1 = pt1;
                lastLast2 = prev2;
                prev2 = pt2;
            }
            breakPoints.AddLast(points.Count-1);
            Debug.Log($"BreakPoint: {points.Count-1}");
            return breakPoints;
        }

        private LinkedList<ProfilePoint> GenPoints(float offsetMult)
        {
            LinkedList<ProfilePoint> points = new LinkedList<ProfilePoint>();

            points.AddLast(CreatePoint(0, offsetMult));
            points.AddLast(CreatePoint(1, offsetMult));

            Queue<LinkedListNode<ProfilePoint>> process = new Queue<LinkedListNode<ProfilePoint>>();
            process.Enqueue(points.First);

            while (process.Count > 0)
            {
                LinkedListNode<ProfilePoint> node = process.Dequeue();
                ProfilePoint pM = node.Value;
                // ReSharper disable once PossibleNullReferenceException
                ProfilePoint pN = node.Next.Value;

                float tM = pM.v;
                float tN = pN.v;

                // So we want to find the point where the curve is maximally distant from the line between pM and pN

                // First we need the normal to the line:
                Vector2 norm = new Vector2(-pN.y + pM.y, pN.x - pM.x);

                // The deviation is:
                // Dev = B(t) . norm - B(m) . norm    (where m = t at point M)

                // We want to know the maxima, so take the derivative and solve for = 0
                // Dev' = B'(t) . norm
                //      = 3(1-t)^2 ((p1.x-p0.x) norm.x + (p1.y-p0.y) norm.y) + 6t(1-t) ((p2.x-p1.x) norm.x + (p2.y-p1.y) norm.y) + 3t^2 ((p3.x-p2.x) norm.x + (p3.y-p2.y) norm.y) = 0

                // This is a quadratic, which we can solve directly.

                float a = ((p1.x + op1.x * offsetMult - p0.x - op0.x * offsetMult) * norm.x + (p1.y - p0.y) * norm.y);
                float b = ((p2.x + op2.x * offsetMult - p1.x - op1.x * offsetMult) * norm.x + (p2.y - p1.y) * norm.y);
                float c = ((p3.x + op3.x * offsetMult - p2.x - op2.x * offsetMult) * norm.x + (p3.y - p2.y) * norm.y);

                // solve a (1-t)^2+2 b (t (1-t))+c t^2 = 0

                // t = (-/+ sqrt(b^2-a c)-a+b)/(-a+2 b-c)   for  a-2 b+c!=0
                // t = (2 b-c)/(2 (b-c))                    for  a = 2 b-c and b-c!=0

                List<float> ts = new List<float>(2);
                //Debug.LogWarning(string.Format("t={0:F3}..{1:F3} perp=({2:F3}, {3:F3}) a={4:F3} b={5:F3} c={6:F3}", tM, tN, norm.x, norm.y, a, b, c));

                if (Math.Abs(a - 2 * b + c) < 1e-6f)
                {
                    if (Math.Abs(b - c) < 1e-6f)
                    {
                        // This is the straight line case, no need to subdivide
                        continue;
                    }
                    float t1 = (2f * b - c) / (2f * (b - c));
                    //Debug.LogWarning(string.Format("t={0:F3}..{1:F3} -> {2:F3}", tM, tN, t1));

                    ts.Add(t1);
                }
                else
                {
                    float sqrt = Mathf.Sqrt(b * b - a * c);

                    float t1 = (sqrt - a + b) / (-a + 2 * b - c);
                    float t2 = (-sqrt - a + b) / (-a + 2 * b - c);
                    //Debug.LogWarning(string.Format("t={0:F3}..{1:F3} -> {2:F3} {3:F3} ", tM, tN, t1, t2));


                    ts.Add(t1);
                    ts.Add(t2);

                    ts.Sort();
                }


                for (int i = 0; i < ts.Count; ++i)
                {
                    if (ts[i] < tM || ts[i] > tN)
                        ts.RemoveAt(i--);
                }

                if (ts.Count == 0)
                    throw new InvalidProgramException("There should be a point maximally distant from the line or the maths is really wrong.");

                norm = norm.normalized;
                float devM = pM.x * norm.x + pM.y * norm.y;

                for (int i = 0; i < ts.Count; ++i)
                {
                    // The difference from the line
                    var offsetVec = OB(ts[i])*offsetMult;
                    offsetVec.y = 0;
                    float devTS = Vector2.Dot(B(ts[i])+offsetVec, norm) - devM;

                    if (Mathf.Abs(devTS) < MaxCircleError)
                        ts.RemoveAt(i--);
                }

                switch (ts.Count)
                {
                    case 0:
                        break;
                    case 1:
                        LinkedListNode<ProfilePoint> next = node.List.AddAfter(node, CreatePoint(ts[0], offsetMult));
                        process.Enqueue(node);
                        process.Enqueue(next);
                        break;
                    case 2:
                        LinkedListNode<ProfilePoint> next0 = node.List.AddAfter(node, CreatePoint(ts[0], offsetMult));
                        LinkedListNode<ProfilePoint> next1 = node.List.AddAfter(next0, CreatePoint(ts[1], offsetMult));

                        process.Enqueue(node);
                        process.Enqueue(next0);
                        process.Enqueue(next1);
                        break;
                }
            }
            return points;
        }

        #endregion

        #region Meshes

        private void WriteMeshes(LinkedList<ProfilePoint> points)
        {
            int nSides = numSides;
            int vertPerLayer = nSides + 1;
            int vertCount = vertPerLayer*points.Count;
            int triPerLayer = 2*3*nSides;
            int triCount = triPerLayer*(points.Count-1);
            int vertOffset = 0;
            int triVertOffset = 0;
            int triOffset = 0;
            UncheckedMesh m = new UncheckedMesh(vertCount, triCount);
            ProfilePoint prev = null;
            bool odd = false;
            foreach (var pt in points)
            {
                WriteVertices(pt, m, nSides, vertOffset, odd);
                if (prev != null)
                {
                    WriteTriangles(m, nSides, triVertOffset, triOffset, odd);

                    triVertOffset += vertPerLayer;
                    triOffset += triPerLayer;
                }
                odd = !odd;
                prev = pt;
                vertOffset += vertPerLayer;
            }

            var tankULength = nSides * NormSideLength * (topDiameter + bottomDiameter);

            RaiseChangeTextureScale("sides", PPart.legacyTextureHandler.SidesMaterial, new Vector2(tankULength, length));
            WriteToAppropriateMesh(m, PPart.SidesIconMesh, SidesMesh);

            m = new UncheckedMesh(2 * vertPerLayer, 4 * vertPerLayer);
            WriteCapVertices(points.First.Value, m, nSides, 0, true);
            WriteCapVertices(points.Last.Value, m, nSides, vertPerLayer, odd);
            WriteCapTriangles(m, nSides, 0, 0, false);
            WriteCapTriangles(m, nSides, vertPerLayer, vertPerLayer, true);
            WriteToAppropriateMesh(m, PPart.EndsIconMesh, EndsMesh);
        }

        private void WriteCollider(LinkedList<ProfilePoint> points, LinkedList<int> splitsList)
        {
            PPart.ClearColliderHolder();
            int nSides = numSides;
            int vertPerLayer = nSides + 1;
            int triPerLayer = 2 * 3 * nSides;
            bool odd = false;

            for (int i = 0; i < splitsList.Count - 1; i++)
            {
                int first = splitsList.ElementAt(i);
                int last = splitsList.ElementAt(i + 1);
                LinkedList<ProfilePoint> currentPoints = new LinkedList<ProfilePoint>();
                for (int j = first; j <= last; j++)
                {
                    var pt = points.ElementAt(j);
                    if (pt.inCollider)
                    {
                        currentPoints.AddLast(pt);
                    }
                }
                int layers = currentPoints.Count;
                var go = new GameObject($"Mesh_Collider_{i}");
                var coll = go.AddComponent<MeshCollider>();
                go.transform.SetParent(PPart.ColliderHolder.transform, false);
                coll.convex = true;

                UncheckedMesh uMesh = new UncheckedMesh(vertPerLayer * layers, triPerLayer * (layers) + 2 * (nSides - 2));

                GenerateColliderMesh(currentPoints, nSides, vertPerLayer, triPerLayer, uMesh, ref odd);
                odd = !odd;

                var colliderMesh = new Mesh();
                uMesh.WriteTo(colliderMesh);
                coll.sharedMesh = colliderMesh;
            }
        }

        void GenerateColliderMesh(LinkedList<ProfilePoint> points, int nSides, int vertPerLayer, int triPerLayer, UncheckedMesh m, ref bool odd)
        {
            int vertOffset = 0;
            int triVertOffset = 0;
            int triOffset = 0;
            ProfilePoint prev = null;
            foreach (ProfilePoint pt in points)
            {
                WriteVertices(pt, m, nSides, vertOffset, odd);
                if (prev != null)
                {
                    WriteTriangles(m, nSides, triVertOffset, triOffset, odd);
                    triVertOffset += vertPerLayer;
                    triOffset += triPerLayer;
                }
                else
                {
                    // Generate bottom tris
                    WriteCapTriangles(m, nSides, 0, 0, false);
                    triOffset += 3 * (nSides - 2);
                }
                odd = !odd;
                prev = pt;
                vertOffset += vertPerLayer;
            }
            // Generate top tris
            WriteCapTriangles(m, nSides, triVertOffset, triOffset, true);
            // Debug.Log($"[ProcPartsDebug] Points in Collider: {points.Count}");
            Debug.Log($"[ProcPartsDebug] Collider Mesh: {m.DumpMesh()}");
        }

        private void WriteCapVertices(ProfilePoint pt, UncheckedMesh m, int nSides, int vertOffset, bool odd)
        {
            int o = odd ? 0 : 1;
            pt = CreatePoint(pt.t, 0);
            float _2pi = Mathf.PI * 2f;
            for (int side = 0; side <= nSides; side++)
            {
                int currSide = side == nSides ? 0 : side;
                float t1 = ((float)(currSide + o/2f) / nSides + 0.25f) * _2pi;
                var offset = OB(pt.t);
                // We do not care about offset.y, since this should be the same as for pt
                var offsetVec = new Vector3(0, 0, offset.x);
                m.vertices[side+vertOffset] = new Vector3(pt.x/2f*Mathf.Cos(t1), pt.y, pt.x/2f*Mathf.Sin(t1)) + offsetVec;
                m.normals[side+vertOffset] = new Vector3(0, pt.y > 0 ? 1f : -1f, 0);
                m.tangents[side+vertOffset] = new Vector4(-Mathf.Sin(t1), 0, Mathf.Cos(t1), -1);
                m.uv[side+vertOffset] = new Vector2(Mathf.Cos(t1)/2f + 1f/2f, Mathf.Sin(t1)/2f + 1f/2f);
            }
        }

        private void WriteCapTriangles(UncheckedMesh mesh, int nSides, int vertexOffset, int triangleOffset, bool up)
        {
            var triangleIndexOffset = triangleOffset * 3;
            for (var i = 0; i < nSides-2; i++)
            {
                mesh.triangles[i * 3 + triangleIndexOffset] = vertexOffset;
                mesh.triangles[i * 3 + 1 + triangleIndexOffset] = (up ? i + 2 : i + 1) + vertexOffset;
                mesh.triangles[i * 3 + 2 + triangleIndexOffset] = (up ? i + 1 : i + 2) + vertexOffset;
            }
        }

        private void WriteVertices(ProfilePoint pt, UncheckedMesh m, int nSides, int vertOffset, bool odd)
        {
            int o = odd ? 1 : 0;
            pt = CreatePoint(pt.t, 0);
            float _2pi = Mathf.PI * 2f;
            for (int side = 0; side <= nSides; side++)
            {
                int currSide = side == nSides ? 0 : side;
                float t1 = ((float)(currSide + o/2f) / nSides + 0.25f) * _2pi;
                var offset = OB(pt.t);
                var offsetDeriv = OBdt(pt.t);
                Vector3 norm = new Vector3(offsetDeriv.x / 2f, offsetDeriv.y / 2f, 0).normalized;
                // We do not care about offset.y, since this should be the same as for pt
                var offsetVec = new Vector3(0, 0, offset.x);
                // change later, currently wrong (rotate the normal wrt the offset normal)
                var trueNormal = pt.norm;
                m.vertices[side+vertOffset] = new Vector3(pt.x/2f*Mathf.Cos(t1), pt.y, pt.x/2f*Mathf.Sin(t1)) + offsetVec;
                m.normals[side+vertOffset] = Quaternion.AngleAxis(Vector3.Angle(Vector3.up, norm)/2f, Vector3.back) * new Vector3(pt.norm.x*Mathf.Cos(t1), pt.norm.y, pt.norm.x*Mathf.Sin(t1));
                m.tangents[side+vertOffset] = new Vector4(-Mathf.Sin(t1), 0, Mathf.Cos(t1), -1);
                m.uv[side+vertOffset] = new Vector2((float)(side + o/2f) / nSides, pt.v);
            }
        }

        private void WriteTriangles(UncheckedMesh m, int nSides, int vertOffset, int triOffset, bool odd)
        {
            int i = 0;
            for (int side = 0; side < nSides; side++)
            {
                int current = side;
                // int next = (side < (nSides) ? (side + 1) * 2 : 0);
                int next = side + nSides + 1;
                if (odd)
                {
                    m.triangles[triOffset + i++] = vertOffset + next + 1;
                    m.triangles[triOffset + i++] = vertOffset + current + 1;
                    m.triangles[triOffset + i++] = vertOffset + next;

                    m.triangles[triOffset + i++] = vertOffset + current;
                    m.triangles[triOffset + i++] = vertOffset + next;
                    m.triangles[triOffset + i++] = vertOffset + current + 1;
                } else
                {
                    m.triangles[triOffset + i++] = vertOffset + current;
                    m.triangles[triOffset + i++] = vertOffset + next;
                    m.triangles[triOffset + i++] = vertOffset + next + 1;

                    m.triangles[triOffset + i++] = vertOffset + current;
                    m.triangles[triOffset + i++] = vertOffset + next + 1;
                    m.triangles[triOffset + i++] = vertOffset + current + 1;
                }
            }
        }

        private ProfilePoint CreatePoint(float t, float offsetMult, bool inCollider = true)
        {
            // ReSharper disable once InconsistentNaming
            // B(t) = (1-t)^3 p0 + t(1-t)^2 p1 + t^2(1-t) p2 + t^3 p3
            Vector2 Bt = B(t);
            Vector2 OBt = OB(t);

            // ReSharper disable once InconsistentNaming
            // B'(t) = (1-t)^2 (p1-p0) + t(1-t) (p2-p1) + t^2 (p3-p2)
            Vector2 Btdt = Bdt(t);
            Vector2 OBtdt = OBdt(t);

            // normalized perpendicular to tangent (derivative)
            Vector2 norm = new Vector2(Btdt.y, -Btdt.x / 2f).normalized;

            return new ProfilePoint(Bt.x + OBt.x*offsetMult, Bt.y, t, t, norm, inCollider);
        }

        private Vector2 B(float t)
        {
            return (1 - t) * (1 - t) * (1 - t) * p0 + 3 * t * (1 - t) * (1 - t) * p1 + 3 * t * t * (1 - t) * p2 + t * t * t * p3;
        }

        private Vector2 Bdt(float t)
        {
            return 3 * (1 - t) * (1 - t) * (p1 - p0) + 6 * t * (1 - t) * (p2 - p1) + 3 * t * t * (p3 - p2);
        }

        private Vector2 OB(float t)
        {
            return (1 - t) * (1 - t) * (1 - t) * op0 + 3 * t * (1 - t) * (1 - t) * op1 + 3 * t * t * (1 - t) * op2 + t * t * t * op3;
        }

        private Vector2 OBdt(float t)
        {
            return 3 * (1 - t) * (1 - t) * (op1 - op0) + 6 * t * (1 - t) * (op2 - op1) + 3 * t * t * (op3 - op2);
        }

        private float BSolverGetT(float p0, float p1, float p2, float p3, float target)
        {
            // a*t^3+b*t^2+c*t+d
            float a = 3*p1 - p0 - 3*p2 + p3;
            float b = 3*p0 - 6*p1 + 3*p2;
            float c = 3*p1 - 3*p0;
            float d = p0-target;

            float t = 0;

            return t;
        }

        #endregion

        #region Helper Classes
        protected class ProfilePoint
        {
            public readonly float x;
            public readonly float y;
            public float v;
            public float t;

            // the normal as a 2 component unit vector (dia, y)
            // eg: for cylinders this would be (1,0), for endcaps (0,+/-1)
            public readonly Vector2 norm;

            public bool inCollider;

            public ProfilePoint(float x, float y, float v, float t, Vector2 norm, bool inCollider = true)
            {
                this.x = x;
                this.y = y;
                this.v = v;
                this.t = t;
                this.norm = norm;
                this.inCollider = inCollider;
            }
        }

        private static void WriteToAppropriateMesh(UncheckedMesh mesh, Mesh iconMesh, Mesh normalMesh)
        {
            var target = HighLogic.LoadedScene == GameScenes.LOADING ? iconMesh : normalMesh;
            mesh.WriteTo(target);
        }

        #endregion
    }
}
