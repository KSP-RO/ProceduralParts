using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;

namespace ProceduralParts
{
    public class ProceduralShapeBezierCone : ProceduralAbstractShape
    {
        private const string ModTag = "[ProceduralShapeBezierCone]";
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

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Top", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float topDiameter = 1.25f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Bottom", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
         UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
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

        public int NumSides => (int)Math.Max(Mathf.PI * Mathf.Sqrt(Mathf.Sqrt((Math.Max(bottomDiameter, topDiameter))) / (2f * MaxCircleError)), 24);

        private float CornerCenterCornerAngle => 2 * Mathf.PI / NumSides;

        private float NormSideLength => Mathf.Tan(CornerCenterCornerAngle / 2);

        [KSPField]
        public string TopNodeName = "top";

        [KSPField]
        public string BottomNodeName = "bottom";

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
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                try
                {
                    coneTopMode = (node.HasValue("coneTopMode")) ?
                        (ConeEndMode)Enum.Parse(typeof(ConeEndMode), node.GetValue("coneTopMode"), true) :
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
            InitializeSelectedShape();
        }

        public override void OnStart(StartState state)
        {
            InitializeSelectedShape();
            base.OnStart(state);
            if (HighLogic.LoadedSceneIsEditor)
            {
                UpdateTechConstraints();
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
                var customShape = new ShapePreset
                {
                    name = CustomShapeName,
                    displayName = CustomShapeName,
                    points = Vector4.zero
                };
                shapePresets.Add(customShape.name, customShape);
            }
        }

        public override void UpdateTechConstraints()
        {
            InitializeSelectedShape();

            Fields[nameof(topDiameter)].guiActiveEditor = PPart.diameterMin != PPart.diameterMax;
            UI_FloatEdit topDiameterEdit = Fields[nameof(topDiameter)].uiControlEditor as UI_FloatEdit;
            topDiameterEdit.incrementLarge = PPart.diameterLargeStep;
            topDiameterEdit.incrementSmall = PPart.diameterSmallStep;

            Fields[nameof(bottomDiameter)].guiActiveEditor = PPart.diameterMin != PPart.diameterMax;
            UI_FloatEdit bottomDiameterEdit = Fields[nameof(bottomDiameter)].uiControlEditor as UI_FloatEdit;
            bottomDiameterEdit.incrementLarge = PPart.diameterLargeStep;
            bottomDiameterEdit.incrementSmall = PPart.diameterSmallStep;

            Fields[nameof(length)].guiActiveEditor = PPart.lengthMin != PPart.lengthMax;
            UI_FloatEdit lengthEdit = Fields[nameof(length)].uiControlEditor as UI_FloatEdit;
            lengthEdit.incrementLarge = PPart.lengthLargeStep;
            lengthEdit.incrementSmall = PPart.lengthSmallStep;

            UI_FloatEdit offsetEdit = Fields[nameof(offset)].uiControlEditor as UI_FloatEdit;
            offsetEdit.incrementLarge = PPart.diameterLargeStep;
            offsetEdit.incrementSmall = PPart.diameterSmallStep;

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
            if (coneBottomMode != ConeEndMode.Constant)
                bottomDiameter = Mathf.Clamp(bottomDiameter, bottomDiameterEdit.minValue, bottomDiameterEdit.maxValue);
            if (coneTopMode != ConeEndMode.Constant)
                topDiameter = Mathf.Clamp(topDiameter, topDiameterEdit.minValue, topDiameterEdit.maxValue);
            length = Mathf.Clamp(length, lengthEdit.minValue, lengthEdit.maxValue);
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
            Volume = CalculateVolume();
            SetControlPoints(length, topDiameter, bottomDiameter);
            // Ensure correct control points if something called AdjustDimensionBounds or CalculateVolume when handling volume change event
            WriteBezier();
            part.CoMOffset = CoMOffset;
            // WriteMeshes in AbstractSoRShape typically does UpdateNodeSize, UpdateProps, RaiseModelAndColliderChanged
            UpdateNodeSize(TopNodeName);
            UpdateNodeSize(BottomNodeName);
            PPart.UpdateProps();
            RaiseModelAndColliderChanged();
        }

        private void UpdateNodeSize(string nodeName)
        {
            if (part.attachNodes.Find(n => n.id == nodeName) is AttachNode node)
            {
                float relevantDiameter;
                if (nodeName == TopNodeName)
                    relevantDiameter = topDiameter;
                else
                    relevantDiameter = bottomDiameter;

                node.size = Math.Min((int)(relevantDiameter / PPart.diameterLargeStep), 3);
                node.breakingTorque = node.breakingForce = Mathf.Max(50 * node.size * node.size, 50);
                RaiseChangeAttachNodeSize(node, relevantDiameter, Mathf.PI * relevantDiameter * relevantDiameter * 0.25f);
            }
        }

        public override void AdjustDimensionBounds()
        {
            float maxLength = PPart.lengthMax;
            float maxBottomDiameter = PPart.diameterMax;
            float maxTopDiameter = PPart.diameterMax;
            float minLength = PPart.lengthMin;
            float minTopDiameter = (coneTopMode == ConeEndMode.CanZero && coneBottomMode != ConeEndMode.Constant) ? 0 : PPart.diameterMin;
            float minBottomDiameter = (coneBottomMode == ConeEndMode.CanZero && coneTopMode != ConeEndMode.Constant) ? 0 : PPart.diameterMin;

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
        }

        // IteratorIncrement is approximately 1/1000.
        // For any sizable volume limit, simple iteration will be very slow.
        // Instead, search by variable increment.  Minimal harm in setting scale default high, because
        // early passes will terminate on first loop.
        private void IterateVolumeLimits(ref float top, ref float bottom, ref float len, float target, float inc, int scale = 6)
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
            float M_y = 1f / 4f * ((p[1].y - p[0].y) * (1f / 3f * p[0].x * p[0].x + 1f / 4f * p[0].x * p[1].x + 1f / 14f * p[0].x * p[2].x + 3f / 28f * p[1].x * p[1].x + 3f / 28f * p[1].x * p[2].x + 1f / 84f * p[0].x * p[3].x + 3f / 70f * p[2].x * p[2].x + 1f / 35f * p[1].x * p[3].x + 1f / 28f * p[2].x * p[3].x + 1f / 84f * p[3].x * p[3].x) +
                                 (p[2].y - p[1].y) * (1f / 12f * p[0].x * p[0].x + 1f / 7f * p[0].x * p[1].x + 1f / 14f * p[0].x * p[2].x + 3f / 28f * p[1].x * p[1].x + 6f / 35f * p[1].x * p[2].x + 2f / 105f * p[0].x * p[3].x + 3f / 28f * p[2].x * p[2].x + 1f / 14f * p[1].x * p[3].x + 1f / 7f * p[2].x * p[3].x + 1f / 12f * p[3].x * p[3].x) +
                                 (p[3].y - p[2].y) * (1f / 84f * p[0].x * p[0].x + 1f / 28f * p[0].x * p[1].x + 1f / 35f * p[0].x * p[2].x + 3f / 70f * p[1].x * p[1].x + 3f / 28f * p[1].x * p[2].x + 1f / 84f * p[0].x * p[3].x + 3f / 28f * p[2].x * p[2].x + 1f / 14f * p[1].x * p[3].x + 1f / 4f * p[2].x * p[3].x + 1f / 3f * p[3].x * p[3].x));

            return Mathf.PI * M_y;
        }

        public override bool SeekVolume(float targetVolume, int dir = 0) => SeekVolume(targetVolume, Fields[nameof(length)], dir);

        public override void TranslateAttachmentsAndNodes(BaseField f, object obj)
        {
            if (f.name == nameof(bottomDiameter) || f.name == nameof(topDiameter))
            {
                HandleDiameterChange(f, obj);
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
                        GetAttachmentNodeLocation(node, out Vector3 _, out Vector3 _, out ShapeCoordinates coord);
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

            foreach (Part p in part.children)
            {
                if (p.FindAttachNodeByPart(part) is AttachNode node && node.nodeType == AttachNode.NodeType.Surface)
                {
                    GetAttachmentNodeLocation(node, out Vector3 _, out Vector3 _, out ShapeCoordinates coord);
                    float yFromBottom = coord.y + (length / 2);
                    float oldOffsetAtY = Mathf.Lerp(0, oldOffset, yFromBottom / length);
                    float newOffsetAtY = Mathf.Lerp(0, offset, yFromBottom / length);
                    float change = newOffsetAtY - oldOffsetAtY;
                    TranslatePart(p, change * Vector3.forward);
                }
            }
        }

        private Vector3 CoMOffset_internal()
        {
            // Does not handle horizontal offset, only vertical

            //CoM integral over x(t)^2*y'(t):
            // int_0->1:y(t)*x(t)^2*y'(t) dt / int_0->1:x(t)^2*y'(t) dt
            //where x(t), x'(t), y(t) are from the Bezier function

            //Function below derived using symbolic math in Matlab
            float x0 = p[0].x, x1 = p[1].x, x2 = p[2].x, x3 = p[3].x, y0 = p[0].y, y1 = p[1].y, y2 = p[2].y, y3 = p[3].y;
            float num = (2310f * y0 * y0 - 1260f * y0 * y1 - 252f * y0 * y2 - 28f * y0 * y3 - 378f * y1 * y1 - 252f * y1 * y2 - 42f * y1 * y3 - 63f * y2 * y2 - 30f * y2 * y3 - 5f * y3 * y3) * x0 * x0 +
                        (1260f * x1 * y0 * y0 - 252f * x1 * y1 * y1 + 252f * x2 * y0 * y0 - 162f * x1 * y2 * y2 + 28f * x3 * y0 * y0 - 30f * x1 * y3 * y3 - 90f * x2 * y2 * y2 + 18f * x3 * y1 * y1 - 42f * x2 * y3 * y3 - 18f * x3 * y2 * y2 - 28f * x3 * y3 * y3 - 168f * x1 * y0 * y2 +
                        168f * x2 * y0 * y1 - 42f * x1 * y0 * y3 - 378f * x1 * y1 * y2 + 42f * x3 * y0 * y1 - 108f * x1 * y1 * y3 - 12f * x2 * y0 * y3 - 108f * x2 * y1 * y2 + 12f * x3 * y0 * y2 - 120f * x1 * y2 * y3 - 60f * x2 * y1 * y3 - 108f * x2 * y2 * y3 - 12f * x3 * y1 * y3 - 42f * x3 * y2 * y3) * x0 +
                        378f * x1 * x1 * y0 * y0 + 252f * x1 * x1 * y0 * y1 - 18f * x1 * x1 * y0 * y3 - 162f * x1 * x1 * y1 * y2 - 90f * x1 * x1 * y1 * y3 - 135f * x1 * x1 * y2 * y2 - 162f * x1 * x1 * y2 * y3 - 63f * x1 * x1 * y3 * y3 + 252f * x1 * x2 * y0 * y0 + 378f * x1 * x2 * y0 * y1 + 108f * x1 * x2 * y0 * y2 +
                        162f * x1 * x2 * y1 * y1 - 108f * x1 * x2 * y1 * y3 - 162f * x1 * x2 * y2 * y2 - 378f * x1 * x2 * y2 * y3 - 252f * x1 * x2 * y3 * y3 + 42f * x1 * x3 * y0 * y0 + 108f * x1 * x3 * y0 * y1 + 60f * x1 * x3 * y0 * y2 + 12f * x1 * x3 * y0 * y3 + 90f * x1 * x3 * y1 * y1 + 108f * x1 * x3 * y1 * y2 -
                        168f * x1 * x3 * y2 * y3 - 252f * x1 * x3 * y3 * y3 + 63f * x2 * x2 * y0 * y0 + 162f * x2 * x2 * y0 * y1 + 90f * x2 * x2 * y0 * y2 + 18f * x2 * x2 * y0 * y3 + 135f * x2 * x2 * y1 * y1 + 162f * x2 * x2 * y1 * y2 - 252f * x2 * x2 * y2 * y3 - 378f * x2 * x2 * y3 * y3 + 30f * x2 * x3 * y0 * y0 +
                        120f * x2 * x3 * y0 * y1 + 108f * x2 * x3 * y0 * y2 + 42f * x2 * x3 * y0 * y3 + 162f * x2 * x3 * y1 * y1 + 378f * x2 * x3 * y1 * y2 + 168f * x2 * x3 * y1 * y3 + 252f * x2 * x3 * y2 * y2 - 1260f * x2 * x3 * y3 * y3 + 5f * x3 * x3 * y0 * y0 + 30f * x3 * x3 * y0 * y1 + 42f * x3 * x3 * y0 * y2 +
                        28f * x3 * x3 * y0 * y3 + 63f * x3 * x3 * y1 * y1 + 252f * x3 * x3 * y1 * y2 + 252f * x3 * x3 * y1 * y3 + 378f * x3 * x3 * y2 * y2 + 1260f * x3 * x3 * y2 * y3 - 2310f * x3 * x3 * y3 * y3;

            float den = (3080f * y0 - 2310f * y1 - 660f * y2 - 110f * y3) * x0 * x0 +
                        (2310f * x1 * y0 - 990f * x1 * y1 + 660f * x2 * y0 - 990f * x1 * y2 + 110f * x3 * y0 - 330f * x1 * y3 - 396f * x2 * y2 + 66f * x3 * y1 - 264f * x2 * y3 - 66f * x3 * y2 - 110f * x3 * y3) * x0 +
                        990f * x1 * x1 * y0 + 396f * x2 * x2 * y0 - 594f * x1 * x1 * y2 + 594f * x2 * x2 * y1 + 110f * x3 * x3 * y0 - 396f * x1 * x1 * y3 + 660f * x3 * x3 * y1 - 990f * x2 * x2 * y3 + 2310f * x3 * x3 * y2 -
                        3080f * x3 * x3 * y3 + 990f * x1 * x2 * y0 + 594f * x1 * x2 * y1 + 264f * x1 * x3 * y0 - 594f * x1 * x2 * y2 + 396f * x1 * x3 * y1 + 330f * x2 * x3 * y0 - 990f * x1 * x2 * y3 + 990f * x2 * x3 * y1 - 660f * x1 * x3 * y3 + 990f * x2 * x3 * y2 - 2310f * x2 * x3 * y3;

            return new Vector3(0, num / den, 0);
        }

        public override void UpdateTFInterops()
        {
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam1", topDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "diam2", bottomDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new System.Object[] { this.part, "length", length, "ProceduralParts" });
        }

        // We completely override these methods because we need to handle the offset
        internal override void InitializeAttachmentNodes()
        {
            InitializeStackAttachmentNodes(length);
            InitializeSurfaceAttachmentNode(length, (bottomDiameter + topDiameter) / 2);
        }

        internal override void InitializeStackAttachmentNodes(float length)
        {
            foreach (AttachNode node in part.attachNodes)
            {
                if (node.owner != part)
                    node.owner = part;
                float direction = (node.position.y > 0) ? 1 : -1;
                Vector3 translation = direction * (length / 2) * Vector3.up;
                if (node.position.y > 0)
                {
                    translation += offset * Vector3.forward;
                }
                if (node.nodeType == AttachNode.NodeType.Stack)
                    MoveNode(node, translation);
            }
        }

        public override void NormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            CalculateVolume();  // Update the bezier curve control nodes

            // The Bezier curve control points are NOT normalized.
            //  p0 = new Vector2(bottomDiameter, -length / 2f);
            //  p3 = new Vector2(topDiameter, length / 2f);
            // But we do need to normalize the length and shift such that 0 <= t <= 1
            float t = (coords.y / length) + (1f / 2);
            Vector2 Bt = B(t, p);
            Debug.Log($"{ModTag} Normalized {coords.y} to {t} (0=bottom, 1=top), B(t)={Bt} as (diameter, length). TopDiameter:{topDiameter} BotDiameter:{bottomDiameter} Len: {length}");
            // For a given normalized length (t), B(t).x is the diameter of the surface cross-section at that length, and B(t).y is .. what?

            coords.y /= length;
            coords.r /= (Bt.x / 2);
        }

        public override void UnNormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            CalculateVolume();  // Update the bezier curve control nodes
            float t = coords.y + (1f / 2);    // Shift coords.y from [-0.5..0.5] to [0..1]
            Vector2 Bt = B(t, p);
            Debug.Log($"{ModTag} From normalized {coords.y}, shape unnormalized bottom {bottomDiameter} top {topDiameter} length {length},  B({t}) yields [{Bt.x:F3}, {Bt.y:F3}] as (diameter, length)");
            // B(t).x is the diameter of the curve at t.  /2 for radius.

            coords.y *= length;
            coords.r *= Bt.x / 2;
        }

        #endregion

        #region Control point calculation
        private readonly Vector2[] p = new Vector2[4];

        private void SetControlPoints(float length, float topDiameter, float bottomDiameter)
        {
            // So we have a rotated bezier curve from bottom to top.
            // There are four control points, the bottom (p0) and the top ones (p3) are obvious
            p[0] = new Vector2(bottomDiameter, -length / 2f);
            p[3] = new Vector2(topDiameter, length / 2f);
            float[] shape = { shapePoints.x, shapePoints.y, shapePoints.z, shapePoints.w };

            // Pretty obvious below what the shape points mean
            if (bottomDiameter < topDiameter)
            {
                p[1] = new Vector2(Mathf.Lerp(p[0].x, p[3].x, shape[0]), Mathf.Lerp(p[0].y, p[3].y, shape[1]));
                p[2] = new Vector2(Mathf.Lerp(p[0].x, p[3].x, shape[2]), Mathf.Lerp(p[0].y, p[3].y, shape[3]));
            }
            else
            {
                p[2] = new Vector2(Mathf.Lerp(p[3].x, p[0].x, shape[0]), Mathf.Lerp(p[3].y, p[0].y, shape[1]));
                p[1] = new Vector2(Mathf.Lerp(p[3].x, p[0].x, shape[2]), Mathf.Lerp(p[3].y, p[0].y, shape[3]));
            }
            SetOffsetControlPoints(length, offset);
        }

        private readonly Vector2[] op = new Vector2[4];

        private void SetOffsetControlPoints(float length, float offset)
        {
            // There are four control points, the bottom (p0) and the top ones (p3) are obvious
            op[0] = new Vector2(0, -length / 2f);
            op[3] = new Vector2(offset * 2f, length / 2f);

            // Currently hard coded to be identical with the shape - change later
            float[] shape = { shapePoints.x, shapePoints.y, shapePoints.z, shapePoints.w };

            if (bottomDiameter < topDiameter)
            {
                op[1] = new Vector2(Mathf.Lerp(op[0].x, op[3].x, shape[0]), Mathf.Lerp(op[0].y, op[3].y, shape[1]));
                op[2] = new Vector2(Mathf.Lerp(op[0].x, op[3].x, shape[2]), Mathf.Lerp(op[0].y, op[3].y, shape[3]));
            }
            else
            {
                op[2] = new Vector2(Mathf.Lerp(op[3].x, op[0].x, shape[0]), Mathf.Lerp(op[3].y, op[0].y, shape[1]));
                op[1] = new Vector2(Mathf.Lerp(op[3].x, op[0].x, shape[2]), Mathf.Lerp(op[3].y, op[0].y, shape[3]));
            }
        }

        #endregion

        #region Bezier Bits

        private void WriteBezier()
        {
            LinkedList<ProfilePoint> pointsPositive = GeneratePoints(1f);
            LinkedList<ProfilePoint> pointsNegative = GeneratePoints(-1f);

            LinkedList<ProfilePoint> points;
            float offsetMult;
            if (pointsPositive.Count > pointsNegative.Count)
            {
                points = pointsPositive;
                offsetMult = 1f;
            }
            else
            {
                points = pointsNegative;
                offsetMult = -1f;
            }

            // Subdivide due to diameter changes that are straight lines
            SubdivideHorizontal(points, offsetMult);
            // Figure out what parts of the cone are concave to split them into convex parts for colliders
            LinkedList<int> splitsList = ConcavePoints(points);

            // Need to figure out the v coords.
            float sumLengths = 0;
            float[] cumLengths = new float[points.Count - 1];

            LinkedListNode<ProfilePoint> current = points.First;
            LinkedListNode<ProfilePoint> next = current.Next;
            for (int i = 0; i < cumLengths.Length; ++i, current = next, next = next.Next)
            {
                float dX = next.Value.diameter - current.Value.diameter;
                float dY = next.Value.y - current.Value.y;

                cumLengths[i] = sumLengths += Mathf.Sqrt(dX * dX + dY * dY);
            }

            points.First.Value.v = 0;
            next = points.First.Next;
            for (int i = 0; i < cumLengths.Length; ++i, next = next.Next)
            {
                next.Value.v = cumLengths[i] / sumLengths;
            }

            WriteMeshes(points);
            WriteCollider(points, splitsList);
        }

        /// <summary>
        /// Subdivide profile points according to the max diameter change.
        /// </summary>
        private void SubdivideHorizontal(LinkedList<ProfilePoint> points, float offsetMult)
        {
            ProfilePoint prev = points.First.Value;
            for (LinkedListNode<ProfilePoint> node = points.First.Next; node != null; node = node.Next)
            {
                ProfilePoint curr = node.Value;
                float positiveChange = CreatePoint(curr.t, 1).diameter - CreatePoint(prev.t, 1).diameter;
                float negativeChange = CreatePoint(curr.t, -1).diameter - CreatePoint(prev.t, -1).diameter;
                float largestChange = Mathf.Max(Math.Abs(positiveChange), Math.Abs(negativeChange));
                float dPercentage = largestChange / (Math.Max(B(curr.t, p).x, B(prev.t, p).x) / 100.0f);
                int subdiv = Math.Min((int)Math.Truncate(dPercentage / MaxDiameterChange), 30);
                if (subdiv > 1)
                {
                    for (int i = 1; i < subdiv; ++i)
                    {
                        float frac = i / (float)subdiv;
                        float t = Mathf.Lerp(prev.t, curr.t, frac);

                        points.AddBefore(node, CreatePoint(t, offsetMult, false));
                    }
                }

                prev = curr;
            }
        }

        #region Collider Splitting algorithm
        /// <summary>
        /// Returns True if the middle point is found to the left of the line going from the 'left' to the 'right' point.
        /// Returns False if any point is null
        /// </summary>
        private static bool MiddleIsConcave(ProfilePoint left, ProfilePoint middle, ProfilePoint right)
        {
            // NOTE: The 'left' point is below the 'right' point, and they are to the right of the y-axis. The math is correct

            // If any point is null, default to false.
            if (left == null || middle == null || right == null)
                return false;

            Vector2 line = right.Pos - left.Pos; // Line from 'left' to 'right'
            Vector2 normal = new Vector2(-line.y, line.x); // Rotate 90° left
            Vector2 difference = middle.Pos - left.Pos; // from 'left' to 'middle'
            bool sign = Vector2.Dot(difference, normal) > 0; // Does 'difference' point in the same direction as 'normal'?
            return sign;
        }

        private bool PointIsConcave(LinkedListNode<ProfilePoint> pointNode, float offsetMult)
        {
            ProfilePoint left = CreatePoint(pointNode.Previous.Value.t, offsetMult);
            ProfilePoint middle = CreatePoint(pointNode.Value.t, offsetMult);
            ProfilePoint right = CreatePoint(pointNode.Next.Value.t, offsetMult);
            return MiddleIsConcave(left, middle, right);
        }

        private LinkedList<int> ConcavePointsWithOffset(LinkedList<ProfilePoint> points, float offsetMult)
        {
            LinkedList<int> breakPoints = new LinkedList<int>();
            breakPoints.AddLast(0);

            int index = 1;
            for (var currentPoint = points.First.Next; currentPoint.Next != null; currentPoint = currentPoint.Next)
            {
                if (currentPoint.Value.inCollider && PointIsConcave(currentPoint, offsetMult))
                {
                    breakPoints.AddLast(index);
                }

                index++;
            }
            breakPoints.AddLast(points.Count - 1);

            // Every index should be unique, so if unique count is not same as list count, something is wrong
            if (breakPoints.Distinct().Count() != breakPoints.Count)
                throw new InvalidProgramException("Collider splitting resulted in the same index twice.");
            return breakPoints;
        }

        private LinkedList<int> ConcavePoints(LinkedList<ProfilePoint> points)
        {
            LinkedList<int> positivePoints = ConcavePointsWithOffset(points, 1);
            LinkedList<int> negativePoints = ConcavePointsWithOffset(points, -1);
            // If an element exists in either of the lists, we want it in the result
            LinkedList<int> breakPoints = new LinkedList<int>(positivePoints.Union(negativePoints).OrderBy(x => x));
            return breakPoints;
        }
        #endregion

        private LinkedList<ProfilePoint> GeneratePoints(float offsetMult)
        {
            LinkedList<ProfilePoint> points = new LinkedList<ProfilePoint>();

            points.AddLast(CreatePoint(0f, offsetMult));
            points.AddLast(CreatePoint(1f, offsetMult));

            Queue<LinkedListNode<ProfilePoint>> process = new Queue<LinkedListNode<ProfilePoint>>();
            process.Enqueue(points.First);

            while (process.Count > 0)
            {
                LinkedListNode<ProfilePoint> node = process.Dequeue();
                ProfilePoint pM = node.Value;
                ProfilePoint pN = node.Next.Value;

                float tM = pM.t;
                float tN = pN.t;

                // So we want to find the point where the curve is maximally distant from the line between pM and pN

                // First we need the normal to the line:
                Vector2 norm = new Vector2(-pN.y + pM.y, pN.diameter - pM.diameter);

                // The deviation is:
                // Dev = B(t) . norm - B(m) . norm    (where m = t at point M)

                // We want to know the maxima, so take the derivative and solve for = 0
                // Dev' = B'(t) . norm
                //      = 3(1-t)^2 ((p1.x-p0.x) norm.x + (p1.y-p0.y) norm.y) + 6t(1-t) ((p2.x-p1.x) norm.x + (p2.y-p1.y) norm.y) + 3t^2 ((p3.x-p2.x) norm.x + (p3.y-p2.y) norm.y) = 0

                // This is a quadratic, which we can solve directly.

                float a = (p[1].x + op[1].x * offsetMult - p[0].x - op[0].x * offsetMult) * norm.x + (p[1].y - p[0].y) * norm.y;
                float b = (p[2].x + op[2].x * offsetMult - p[1].x - op[1].x * offsetMult) * norm.x + (p[2].y - p[1].y) * norm.y;
                float c = (p[3].x + op[3].x * offsetMult - p[2].x - op[2].x * offsetMult) * norm.x + (p[3].y - p[2].y) * norm.y;

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
                float devM = pM.diameter * norm.x + pM.y * norm.y;

                for (int i = 0; i < ts.Count; ++i)
                {
                    // The difference from the line
                    Vector2 offsetVec = B(ts[i], op) * offsetMult;
                    offsetVec.y = 0;
                    float devTS = Vector2.Dot(B(ts[i], p) + offsetVec, norm) - devM;

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
            float tankVLength = 0f;

            int nSides = NumSides;
            int verticesPerLayer = nSides + 1;
            int verticeCount = verticesPerLayer * points.Count;
            int triangleCornersPerLayer = 2 * 3 * nSides;
            int triangleCornerCount = triangleCornersPerLayer * (points.Count - 1);
            int verticeOffset = 0;
            int triangleVerticeOffset = 0;
            int triangleCornerOffset = 0;
            UncheckedMesh sideMesh = new UncheckedMesh(verticeCount, triangleCornerCount);
            ProfilePoint previous = null;
            bool odd = false;
            foreach (ProfilePoint point in points)
            {
                WriteVertices(point, sideMesh, nSides, ref verticeOffset, odd);
                if (previous == null)
                {
                    previous = point;
                    odd = !odd;
                    continue;
                }
                WriteTriangles(sideMesh, nSides, ref triangleVerticeOffset, ref triangleCornerOffset, odd);

                float dy = point.y - previous.y;
                float dx = (point.diameter - previous.diameter) * 0.5f;
                tankVLength += Mathf.Sqrt(dx * dx + dy * dy);

                odd = !odd;
                previous = point;
            }

            float tankULength = nSides * NormSideLength * (topDiameter + bottomDiameter);

            RaiseChangeTextureScale("sides", PPart.legacyTextureHandler.SidesMaterial, new Vector2(tankULength, tankVLength));
            WriteToAppropriateMesh(sideMesh, PPart.SidesIconMesh, SidesMesh);

            UncheckedMesh capMesh = new UncheckedMesh(2 * verticesPerLayer, 4 * verticesPerLayer);
            WriteCapVertices(points.First.Value, capMesh, nSides, 0, true);
            WriteCapVertices(points.Last.Value, capMesh, nSides, verticesPerLayer, odd);
            WriteCapTriangles(capMesh, nSides, 0, 0, false);
            WriteCapTriangles(capMesh, nSides, verticesPerLayer, verticesPerLayer, true);
            WriteToAppropriateMesh(capMesh, PPart.EndsIconMesh, EndsMesh);
        }

        private void WriteCollider(LinkedList<ProfilePoint> points, LinkedList<int> splitsList)
        {
            PPart.ClearColliderHolder();
            int nSides = NumSides;
            int vertPerLayer = nSides + 1;
            int triPerLayer = 2 * 3 * nSides;
            bool odd = false;

            int colliderNumber = 0;
            int first = 0;
            bool firstIteration = true;
            foreach (int last in splitsList)
            {
                if (firstIteration)
                {
                    firstIteration = false;
                    first = last;
                    continue;
                }

                LinkedList<ProfilePoint> currentPoints = new LinkedList<ProfilePoint>();
                for (int j = first; j <= last; j++)
                {
                    ProfilePoint point = points.ElementAt(j);
                    if (point.inCollider)
                    {
                        currentPoints.AddLast(point);
                    }
                }
                int layers = currentPoints.Count;
                GameObject go = new GameObject($"Mesh_Collider_{colliderNumber++}");
                MeshCollider collider = go.AddComponent<MeshCollider>();
                go.transform.SetParent(PPart.ColliderHolder.transform, false);
                collider.convex = true;

                UncheckedMesh uMesh = new UncheckedMesh(vertPerLayer * layers, triPerLayer * (layers) + 2 * (nSides - 2));

                GenerateColliderMesh(currentPoints, nSides, uMesh, ref odd);
                odd = !odd;

                Mesh colliderMesh = new Mesh();
                uMesh.WriteTo(colliderMesh);
                collider.sharedMesh = colliderMesh;

                first = last;
            }
        }

        void GenerateColliderMesh(LinkedList<ProfilePoint> points, int nSides, UncheckedMesh mesh, ref bool odd)
        {
            int vertOffset = 0;
            int triVertOffset = 0;
            int triOffset = 0;
            ProfilePoint prev = null;
            foreach (ProfilePoint point in points)
            {
                WriteVertices(point, mesh, nSides, ref vertOffset, odd);
                if (prev != null)
                {
                    WriteTriangles(mesh, nSides, ref triVertOffset, ref triOffset, odd);
                }
                else
                {
                    // Generate bottom tris
                    WriteCapTriangles(mesh, nSides, 0, 0, false);
                    triOffset += 3 * (nSides - 2);
                }
                odd = !odd;
                prev = point;
            }
            // Generate top tris
            WriteCapTriangles(mesh, nSides, triVertOffset, triOffset, true);
            // Debug.Log($"[ProcPartsDebug] Points in Collider: {points.Count}");
            // Debug.Log($"[ProcPartsDebug] Collider Mesh: {m.DumpMesh()}");
        }

        private void WriteCapVertices(ProfilePoint point, UncheckedMesh mesh, int nSides, int vertOffset, bool odd)
        {
            int o = odd ? 0 : 1;
            point = CreatePoint(point.t, 0);
            float _2pi = Mathf.PI * 2f;
            for (int side = 0; side <= nSides; side++)
            {
                int currSide = side == nSides ? 0 : side;
                float t1 = ((float)(currSide + o / 2f) / nSides + 0.25f) * _2pi;
                Vector2 offset = B(point.t, op);
                // We do not care about offset.y, since this should be the same as for pt
                Vector3 offsetVec = new Vector3(0, 0, offset.x / 2f);
                mesh.vertices[side + vertOffset] = new Vector3(point.diameter / 2f * Mathf.Cos(t1), point.y, point.diameter / 2f * Mathf.Sin(t1)) + offsetVec;
                mesh.normals[side + vertOffset] = new Vector3(0, point.y > 0 ? 1f : -1f, 0);
                mesh.tangents[side + vertOffset] = new Vector4(-Mathf.Sin(t1), 0, Mathf.Cos(t1), -1);
                mesh.uv[side + vertOffset] = new Vector2(Mathf.Cos(t1) / 2f + 1f / 2f, Mathf.Sin(t1) / 2f + 1f / 2f);
            }
        }

        private void WriteCapTriangles(UncheckedMesh mesh, int nSides, int vertexOffset, int triangleOffset, bool up)
        {
            int triangleIndexOffset = triangleOffset * 3;
            for (int i = 0; i < nSides - 2; i++)
            {
                mesh.triangles[i * 3 + triangleIndexOffset] = vertexOffset;
                mesh.triangles[i * 3 + 1 + triangleIndexOffset] = (up ? i + 2 : i + 1) + vertexOffset;
                mesh.triangles[i * 3 + 2 + triangleIndexOffset] = (up ? i + 1 : i + 2) + vertexOffset;
            }
        }

        private void WriteVertices(ProfilePoint point, UncheckedMesh mesh, int nSides, ref int vertOffset, bool odd)
        {
            int o = odd ? 1 : 0;
            float uvv = point.v;
            point = CreatePoint(point.t, 0);
            float _2pi = Mathf.PI * 2f;
            for (int side = 0; side <= nSides; side++, vertOffset++)
            {
                int currSide = side == nSides ? 0 : side;
                float t1 = ((float)(currSide + o / 2f) / nSides + 0.25f) * _2pi;
                Vector2 offset = B(point.t, op);
                // We do not care about offset.y, since this should be the same as for pt
                Vector3 offsetVec = new Vector3(0, 0, offset.x / 2f);
                mesh.vertices[vertOffset] = new Vector3(point.diameter / 2f * Mathf.Cos(t1), point.y, point.diameter / 2f * Mathf.Sin(t1)) + offsetVec;
                // TODO: Implement correct normals and tangents. Currently using mesh.RecalculateNormals()
                // change later, currently wrong (rotate the normal wrt the offset normal)
                // var offsetDeriv = Bdt(point.t, op);
                // Vector3 norm = new Vector3(offsetDeriv.x / 2f, offsetDeriv.y / 2f, 0).normalized;
                // var trueNormal = point.norm;
                // mesh.normals[vertOffset] = Quaternion.AngleAxis(Vector3.Angle(Vector3.up, norm) / 2f, Vector3.back) * new Vector3(point.norm.x * Mathf.Cos(t1), point.norm.y, point.norm.x * Mathf.Sin(t1));
                // mesh.tangents[vertOffset] = new Vector4(-Mathf.Sin(t1), 0, Mathf.Cos(t1), -1);
                mesh.uv[vertOffset] = new Vector2((float)(side + o / 2f) / nSides, uvv);
            }
        }

        private void WriteTriangles(UncheckedMesh mesh, int nSides, ref int vertOffset, ref int triOffset, bool odd)
        {
            int counter = 0;
            for (int side = 0; side < nSides; side++)
            {
                counter++; // Just for adding to the offset
                int current = side;
                // int next = (side < (nSides) ? (side + 1) * 2 : 0);
                int next = side + nSides + 1;
                if (odd)
                {
                    mesh.triangles[triOffset++] = vertOffset + next + 1;
                    mesh.triangles[triOffset++] = vertOffset + current + 1;
                    mesh.triangles[triOffset++] = vertOffset + next;

                    mesh.triangles[triOffset++] = vertOffset + current;
                    mesh.triangles[triOffset++] = vertOffset + next;
                    mesh.triangles[triOffset++] = vertOffset + current + 1;
                }
                else
                {
                    mesh.triangles[triOffset++] = vertOffset + current;
                    mesh.triangles[triOffset++] = vertOffset + next;
                    mesh.triangles[triOffset++] = vertOffset + next + 1;

                    mesh.triangles[triOffset++] = vertOffset + current;
                    mesh.triangles[triOffset++] = vertOffset + next + 1;
                    mesh.triangles[triOffset++] = vertOffset + current + 1;
                }
            }
            vertOffset += counter + 1;
        }

        private ProfilePoint CreatePoint(float t, float offsetMult, bool inCollider = true)
        {
            // B(t) = (1-t)^3 p0 + t(1-t)^2 p1 + t^2(1-t) p2 + t^3 p3
            Vector2 Bt = B(t, p);
            Vector2 OBt = B(t, op);

            // B'(t) = (1-t)^2 (p1-p0) + t(1-t) (p2-p1) + t^2 (p3-p2)
            Vector2 Btdt = Bdt(t, p);

            // normalized perpendicular to tangent (derivative)
            Vector2 norm = new Vector2(Btdt.y, -Btdt.x / 2f).normalized;

            return new ProfilePoint(Bt.x + OBt.x * offsetMult, Bt.y, t, t, norm, inCollider);
        }

        private static Vector2 B(float t, Vector2[] p)
        {
            return (1 - t) * (1 - t) * (1 - t) * p[0] + 3 * t * (1 - t) * (1 - t) * p[1] + 3 * t * t * (1 - t) * p[2] + t * t * t * p[3];
        }

        private static Vector2 Bdt(float t, Vector2[] p)
        {
            return 3 * (1 - t) * (1 - t) * (p[1] - p[0]) + 6 * t * (1 - t) * (p[2] - p[1]) + 3 * t * t * (p[3] - p[2]);
        }

        #endregion

        #region Helper Classes
        protected class ProfilePoint
        {
            public readonly float diameter;
            public readonly float y;
            public float v;
            public float t;

            // the normal as a 2 component unit vector (dia, y)
            // eg: for cylinders this would be (1,0), for endcaps (0,+/-1)
            public readonly Vector2 norm;

            public bool inCollider;

            public ProfilePoint(float diameter, float y, float v, float t, Vector2 norm, bool inCollider = true)
            {
                this.diameter = diameter;
                this.y = y;
                this.v = v;
                this.t = t;
                this.norm = norm;
                this.inCollider = inCollider;
            }

            public Vector2 Pos => new Vector2(diameter, y);
        }

        private static void WriteToAppropriateMesh(UncheckedMesh mesh, Mesh iconMesh, Mesh normalMesh)
        {
            Mesh target = HighLogic.LoadedScene == GameScenes.LOADING ? iconMesh : normalMesh;
            mesh.WriteTo(target);
            target.RecalculateNormals();
            target.RecalculateTangents();
        }

        #endregion
    }
}
