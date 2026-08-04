using KSPAPIExtensions;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace ProceduralParts
{
    // Cross-section outline family. All are sampled by direction angle (same point ordering) so a
    // segment can morph point-for-point between two different profiles.
    public enum SpineProfile { Ellipse, Rectangle, Mk2, Mk3 }

    // Silhouette of the segment ABOVE this node. Phase 1 implements Linear; the rest in Phase 2.
    public enum SpineSlope { Linear, Concave, Convex, Waisted }

    // Which nodes show their edge (resize) tip handles. The node selectors themselves always show.
    public enum SpineHandleVis { All, Active, None }

    // One cross-section along the spine. Persisted as a SPINE_NODE child node (see OnSave/OnLoad),
    // the same variable-length pattern ProceduralSRB uses for its bell configs.
    [Serializable]
    public class SpineNode : IConfigNode
    {
        [Persistent] public float position = 0.5f;   // 0..1 along the spine (0 = bottom, 1 = top)
        [Persistent] public float sizeH = 1.25f;     // horizontal diameter (m), before hScale
        [Persistent] public float sizeV = 1.25f;     // vertical diameter (m), before vScale
        [Persistent] public float offsetV = 0f;      // vertical centre offset (m), before vScale
        [Persistent] public float offsetH = 0f;      // horizontal centre offset (m), before hScale
        [Persistent] public float tiltV = 0f;        // vertical tilt (deg): +ve pitches the top of the ring forward (+spine)
        [Persistent] public float tiltH = 0f;        // horizontal tilt (deg): +ve yaws the right of the ring forward (+spine)
        [Persistent] public float filletTop = 0f;    // top corner rounding, 0..1 of the half-min-extent
        [Persistent] public float filletBottom = 0f; // bottom corner rounding, 0..1
        [Persistent] public float stretch = 0f;      // ellipse only: 0 = true ellipse, 1 = flat faces on the longer axis (toward a rectangle)
        [Persistent] public SpineProfile profile = SpineProfile.Ellipse;
        [Persistent] public SpineSlope slopeAbove = SpineSlope.Linear;

        public SpineNode() { }
        public SpineNode(float pos, float h, float v) { position = pos; sizeH = h; sizeV = v; }
        public void Load(ConfigNode node) => ConfigNode.LoadObjectFromConfig(this, node);
        public void Save(ConfigNode node) => ConfigNode.CreateConfigFromObject(this, node);
        public SpineNode Clone() => new SpineNode
        {
            position = position, sizeH = sizeH, sizeV = sizeV, offsetV = offsetV, offsetH = offsetH,
            tiltV = tiltV, tiltH = tiltH, filletTop = filletTop, filletBottom = filletBottom,
            stretch = stretch,
            profile = profile, slopeAbove = slopeAbove,
        };
    }

    class ProceduralShapeSpine : ProceduralAbstractShape
    {
        private const string ModTag = "[ProceduralShapeSpine]";
        private const int Sides = 24;          // ellipse tessellation for the visible mesh
        private const int ColliderSides = 12;  // coarser collider

        internal override void InitializeAttachmentNodes() => InitializeAttachmentNodes(length, EndDiameter(0));

        #region Config parameters

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Length", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 5, unit = "m", useSI = true)]
        public float length = 2f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Width scale", guiFormat = "F3", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 3)]
        public float hScale = 1f;

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Height scale", guiFormat = "F3", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 3)]
        public float vScale = 1f;

        // Lifting body: when on, drives the part's stock ModuleLiftingSurface from the planform area so
        // the body generates lift. (With FAR installed, FAR voxel aero supersedes ModuleLiftingSurface.)
        // affectSymCounterparts is None: OnAirfoilChanged propagates to counterparts manually. Letting the
        // stock All fan-out also fire would double-apply and trigger N^2 rebuilds across the symmetry group.
        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Airfoil", groupName = ProceduralPart.PAWGroupName),
            UI_Toggle(scene = UI_Scene.Editor, enabledText = "On", disabledText = "Off", affectSymCounterparts = UI_Scene.None)]
        public bool airfoil = false;

        // Shell: open both ends and drop the solid interior (a thin skin) -- for custom shrouds / shields.
        // affectSymCounterparts None: the onFieldChanged -> RebuildAndPropagate -> SyncToSymmetry path
        // already copies shellMode to and rebuilds every counterpart (stock All would double that to N^2).
        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Hollow shell", groupName = ProceduralPart.PAWGroupName),
            UI_Toggle(scene = UI_Scene.Editor, enabledText = "On", disabledText = "Off", affectSymCounterparts = UI_Scene.None)]
        public bool shellMode = false;

        // Wall thickness (m) of the hollow shell: the inner skin is inset from the outer by this, and the
        // open ends get a rim so the wall reads solid instead of paper-thin. Shown only in shell mode.
        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Shell thickness", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0.005f, maxValue = 1.0f, incrementLarge = 0.1f, incrementSmall = 0.02f, incrementSlide = SliderPrecision, sigFigs = 3, unit = "m", useSI = true, affectSymCounterparts = UI_Scene.None)]
        public float shellThickness = 0.05f;
        // Max fraction of a section's smaller half-extent the wall may inset (so the inner bore never
        // crosses the centre). Shared by the geometry clamp (InnerRing) and the slider's upper bound.
        private const float ShellInsetFraction = 0.49f;
        private const float MinShellThickness = 0.005f;

        // Gizmo behaviour prefs (editor-only). handleVisOpt picks which nodes show their edge tips:
        // All / Active (selected node only) / None. Node selectors + section glyphs always show.
        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Show Handles", groupName = ProceduralPart.PAWGroupName),
            UI_ChooseOption(scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.None)]
        public string handleVisOpt = nameof(SpineHandleVis.Active);

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Must select node before dragging", groupName = ProceduralPart.PAWGroupName),
            UI_Toggle(scene = UI_Scene.Editor, enabledText = "On", disabledText = "Off", affectSymCounterparts = UI_Scene.None)]
        public bool requireSelectToDrag = true;

        [KSPField] public string TopNodeName = "top";
        [KSPField] public string BottomNodeName = "bottom";

        // Smallest bounding-circle diameter over the whole spine (set in UpdateShape). MaxDiameter is the
        // largest; RP-1 tooling reads both to tool the spine as its min + max bounding cylinders.
        public float MinBoundingDiameter { get; private set; }

        // Spine cross-sections, sorted by position. Serialized as SPINE_NODE subnodes.
        public readonly List<SpineNode> nodes = new List<SpineNode>();

        // --- Per-node editing proxies (PAW) ---
        // KSPFields are scalars, so these proxy whichever node `selectedNode` points at: changing one
        // writes back to that SpineNode. Non-persistent (the node list is the source of truth) except
        // selectedNode. Symmetry is handled manually (SyncToSymmetry), so they don't auto-propagate.
        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Node", guiFormat = "F0", groupName = ProceduralPart.PAWGroupName),
            UI_FloatRange(minValue = 0, maxValue = 2, stepIncrement = 1, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.None)]
        public float selectedNode = 0;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Node width", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 4, unit = "m", useSI = true, affectSymCounterparts = UI_Scene.None)]
        public float nodeWidth = 1.25f;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Node height", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, incrementSlide = SliderPrecision, sigFigs = 4, unit = "m", useSI = true, affectSymCounterparts = UI_Scene.None)]
        public float nodeHeight = 1.25f;

        // Tilt the ring's plane: vertical tilt rakes the top of the section fore/aft, horizontal tilt rakes
        // the right side fore/aft (a shear along the spine, not editable from the gizmo).
        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Node vertical tilt", guiFormat = "F1", guiUnits = "°", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, minValue = -60f, maxValue = 60f, incrementLarge = 15f, incrementSmall = 5f, incrementSlide = 0.1f, sigFigs = 1, affectSymCounterparts = UI_Scene.None)]
        public float nodeTiltV = 0f;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Node horizontal tilt", guiFormat = "F1", guiUnits = "°", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, minValue = -60f, maxValue = 60f, incrementLarge = 15f, incrementSmall = 5f, incrementSlide = 0.1f, sigFigs = 1, affectSymCounterparts = UI_Scene.None)]
        public float nodeTiltH = 0f;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Node position", guiFormat = "F3", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, minValue = 0f, maxValue = 1f, incrementLarge = 0.25f, incrementSmall = 0.05f, incrementSlide = SliderPrecision, sigFigs = 3, affectSymCounterparts = UI_Scene.None)]
        public float nodePos = 0.5f;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Slope above", groupName = ProceduralPart.PAWGroupName),
            UI_ChooseOption(scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.None)]
        public string nodeSlopeOpt = "Linear";

        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Section", groupName = ProceduralPart.PAWGroupName),
            UI_ChooseOption(scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.None)]
        public string nodeProfileOpt = "Ellipse";

        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Node height offset", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, minValue = -10f, maxValue = 10f, incrementLarge = 0.5f, incrementSmall = 0.1f, incrementSlide = SliderPrecision, sigFigs = 3, unit = "m", affectSymCounterparts = UI_Scene.None)]
        public float nodeOffsetV = 0f;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Node width offset", guiFormat = "F3", guiUnits = "m", groupName = ProceduralPart.PAWGroupName),
            UI_FloatEdit(scene = UI_Scene.Editor, minValue = -10f, maxValue = 10f, incrementLarge = 0.5f, incrementSmall = 0.1f, incrementSlide = SliderPrecision, sigFigs = 3, unit = "m", affectSymCounterparts = UI_Scene.None)]
        public float nodeOffsetH = 0f;

        // Rectangle: independent top/bottom corner rounding (0 = sharp). Shown only for Rectangle.
        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Top fillet", guiFormat = "F2", groupName = ProceduralPart.PAWGroupName),
            UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = 0.05f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.None)]
        public float nodeFilletTop = 0f;

        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Bottom fillet", guiFormat = "F2", groupName = ProceduralPart.PAWGroupName),
            UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = 0.05f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.None)]
        public float nodeFilletBottom = 0f;

        // Ellipse: flatten the faces toward a rectangle (0 = true ellipse, 1 = flat wall). Applies to the
        // longer axis's faces (they're congruent for a circle, so a single slider covers both). Ellipse only.
        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Stretch", guiFormat = "F2", groupName = ProceduralPart.PAWGroupName),
            UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = 0.05f, scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.None)]
        public float nodeStretch = 0f;

        [KSPEvent(guiActiveEditor = true, guiName = "Add node in front", groupName = ProceduralPart.PAWGroupName)]
        public void AddNodeInFrontEvent() => AddNode(true);

        [KSPEvent(guiActiveEditor = true, guiName = "Add node behind", groupName = ProceduralPart.PAWGroupName)]
        public void AddNodeBehindEvent() => AddNode(false);

        // Insert a copy of the selected node (same section type / mode / slope / size) toward the +position
        // (front) or -position (behind) side, halfway to the neighbour (or the spine end).
        private void AddNode(bool inFront)
        {
            int i = SelIndex;
            SpineNode cur = nodes[i];
            float bound = inFront ? ((i < nodes.Count - 1) ? nodes[i + 1].position : 1f)
                                  : ((i > 0) ? nodes[i - 1].position : 0f);
            if (Mathf.Abs(bound - cur.position) < 0.02f)
            {
                ScreenMessages.PostScreenMessage($"No room to add a node {(inFront ? "in front" : "behind")}.", 4f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            SpineNode add = cur.Clone();
            add.position = 0.5f * (cur.position + bound);
            nodes.Add(add);
            SortNodes();
            ClampNodeSizes();   // a clone of a closed end lands inside the body, where zero isn't allowed
            selectedNode = nodes.IndexOf(add);
            RefreshSelector();
            LoadProxyFromNode();
            RebuildAndPropagate();
            MonoUtilities.RefreshPartContextWindow(part);
        }

        [KSPEvent(guiActiveEditor = true, guiName = "Remove node", groupName = ProceduralPart.PAWGroupName)]
        public void RemoveNodeEvent()
        {
            if (nodes.Count <= 2)
            {
                ScreenMessages.PostScreenMessage("Spine needs at least 2 nodes.", 4f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }
            nodes.RemoveAt(SelIndex);
            selectedNode = Mathf.Clamp((int)selectedNode, 0, nodes.Count - 1);
            ClampNodeSizes();   // dropping to two nodes can leave both ends closed -> reopen one
            RefreshSelector();
            LoadProxyFromNode();
            RebuildAndPropagate();
            MonoUtilities.RefreshPartContextWindow(part);
        }

        private int SelIndex => Mathf.Clamp((int)selectedNode, 0, Mathf.Max(0, nodes.Count - 1));

        // The first/last nodes are the spine ends -- pinned fore/aft, can't be repositioned along the axis.
        private bool IsEndNode(int i) => i == 0 || i == nodes.Count - 1;

        #endregion

        public override string ShapeKey
        {
            get
            {
                var sb = new StringBuilder("PP-Spine|").Append(length).Append('|').Append(hScale).Append('|').Append(vScale)
                    .Append('|').Append(shellMode ? 1 : 0).Append(',').Append(shellThickness);
                foreach (SpineNode n in nodes)
                    sb.Append('|').Append(n.position).Append(',').Append(n.sizeH).Append(',').Append(n.sizeV)
                      .Append(',').Append(n.offsetV).Append(',').Append(n.offsetH).Append(',').Append(n.tiltV).Append(',').Append(n.tiltH)
                      .Append(',').Append(n.filletTop).Append(',').Append(n.filletBottom)
                      .Append(',').Append(n.stretch)
                      .Append(',').Append((int)n.profile).Append(',').Append((int)n.slopeAbove);
                return sb.ToString();
            }
        }

        #region Serialization

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            ConfigNode[] cfgs = node.GetNodes("SPINE_NODE");
            if (cfgs != null && cfgs.Length > 0)
            {
                nodes.Clear();
                foreach (ConfigNode c in cfgs)
                {
                    SpineNode n = new SpineNode();
                    n.Load(c);
                    nodes.Add(n);
                }
                SortNodes();
            }
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            foreach (SpineNode n in nodes)
                n.Save(node.AddNode("SPINE_NODE"));
        }

        // Editor copy (alt-drag / clone) copies KSPFields but not our node list, so carry it across here.
        public override void OnCopy(PartModule fromModule)
        {
            base.OnCopy(fromModule);
            if (fromModule is ProceduralShapeSpine src)
            {
                nodes.Clear();
                foreach (SpineNode n in src.nodes) nodes.Add(n.Clone());
                selectedNode = src.selectedNode;
            }
        }

        private void SortNodes()
        {
            nodes.Sort((a, b) => a.position.CompareTo(b.position));
            NormalizeEnds();
        }

        // The first/last nodes are the spine ends and are pinned at 0/1. Enforcing it here (rather than
        // only assuming it in the gizmo) keeps the mesh extent aligned with the stack attach nodes even
        // after a craft loads with off-end positions, or RemoveNode promotes an interior node to an end.
        private void NormalizeEnds()
        {
            if (nodes.Count < 2) return;
            nodes[0].position = 0f;
            nodes[nodes.Count - 1].position = 1f;
        }

        // Provide a sensible default spine if the part config / craft supplied none.
        private void EnsureNodes()
        {
            if (nodes.Count >= 2) return;
            nodes.Clear();
            nodes.Add(new SpineNode(0f, 1.25f, 1.25f));
            nodes.Add(new SpineNode(0.5f, 1.6f, 1.0f));
            nodes.Add(new SpineNode(1f, 0.8f, 0.8f));
        }

        #endregion

        #region Initialization

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            EnsureNodes();
            if (HighLogic.LoadedSceneIsEditor)
            {
                UpdateTechConstraints();
                Fields[nameof(length)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;
                Fields[nameof(hScale)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;
                Fields[nameof(vScale)].uiControlEditor.onFieldChanged = OnShapeDimensionChanged;

                Fields[nameof(selectedNode)].uiControlEditor.onFieldChanged = OnSelectedNodeChanged;
                UI_ChooseOption slopeOpt = Fields[nameof(nodeSlopeOpt)].uiControlEditor as UI_ChooseOption;
                slopeOpt.options = Enum.GetNames(typeof(SpineSlope));
                slopeOpt.onFieldChanged = OnNodeFieldChanged;
                UI_ChooseOption profOpt = Fields[nameof(nodeProfileOpt)].uiControlEditor as UI_ChooseOption;
                profOpt.options = Enum.GetNames(typeof(SpineProfile));
                profOpt.onFieldChanged = OnNodeFieldChanged;
                UI_ChooseOption handleOpt = Fields[nameof(handleVisOpt)].uiControlEditor as UI_ChooseOption;
                handleOpt.options = Enum.GetNames(typeof(SpineHandleVis));
                Fields[nameof(nodeWidth)].uiControlEditor.onFieldChanged = OnNodeFieldChanged;
                Fields[nameof(nodeHeight)].uiControlEditor.onFieldChanged = OnNodeFieldChanged;
                Fields[nameof(nodeTiltV)].uiControlEditor.onFieldChanged = OnNodeFieldChanged;
                Fields[nameof(nodeTiltH)].uiControlEditor.onFieldChanged = OnNodeFieldChanged;
                Fields[nameof(nodePos)].uiControlEditor.onFieldChanged = OnNodeFieldChanged;
                Fields[nameof(nodeOffsetV)].uiControlEditor.onFieldChanged = OnNodeFieldChanged;
                Fields[nameof(nodeOffsetH)].uiControlEditor.onFieldChanged = OnNodeFieldChanged;
                Fields[nameof(nodeFilletTop)].uiControlEditor.onFieldChanged = OnNodeFieldChanged;
                Fields[nameof(nodeFilletBottom)].uiControlEditor.onFieldChanged = OnNodeFieldChanged;
                Fields[nameof(nodeStretch)].uiControlEditor.onFieldChanged = OnNodeFieldChanged;
                Fields[nameof(airfoil)].uiControlEditor.onFieldChanged = (f, o) => OnAirfoilChanged();
                Fields[nameof(shellMode)].uiControlEditor.onFieldChanged = (f, o) => { UpdateShellVisibility(); RebuildAndPropagate(); };
                Fields[nameof(shellThickness)].uiControlEditor.onFieldChanged = (f, o) => RebuildAndPropagate();

                UpdateShellVisibility();

                RefreshSelector();
                LoadProxyFromNode();
            }
        }

        public override void UpdateTechConstraints()
        {
            EnsureNodes();
            Fields[nameof(length)].guiActiveEditor = PPart.lengthMin != PPart.lengthMax;
            UI_FloatEdit lengthEdit = Fields[nameof(length)].uiControlEditor as UI_FloatEdit;
            lengthEdit.incrementLarge = PPart.lengthLargeStep;
            lengthEdit.incrementSmall = PPart.lengthSmallStep;

            UI_FloatEdit hEdit = Fields[nameof(hScale)].uiControlEditor as UI_FloatEdit;
            UI_FloatEdit vEdit = Fields[nameof(vScale)].uiControlEditor as UI_FloatEdit;
            hEdit.incrementLarge = vEdit.incrementLarge = 0.25f;
            hEdit.incrementSmall = vEdit.incrementSmall = 0.05f;

            foreach (string fn in new[] { nameof(nodeWidth), nameof(nodeHeight) })
            {
                UI_FloatEdit e = Fields[fn].uiControlEditor as UI_FloatEdit;
                e.maxValue = MaxSize;
                e.incrementLarge = PPart.diameterLargeStep;
                e.incrementSmall = PPart.diameterSmallStep;
            }
            UpdateSizeFieldBounds();

            ClampNodeSizes();   // heal any out-of-range sizes from older builds/crafts
            AdjustDimensionBounds();
            length = Mathf.Clamp(length, lengthEdit.minValue, lengthEdit.maxValue);
            hScale = Mathf.Clamp(hScale, hEdit.minValue, hEdit.maxValue);
            vScale = Mathf.Clamp(vScale, vEdit.minValue, vEdit.maxValue);
            LoadProxyFromNode();
        }

        #endregion

        #region Update

        // Set while a rebuild is in flight. The Volume setter fires onEditorShipModified, which can
        // synchronously rebuild the PAW and re-fire our proxy field handlers -> guard against that
        // re-entering and recursively mangling the node sizes (it was a stack overflow / crash).
        private bool _rebuilding;

        // Top-view planform area (m^2), integrated from the rings; drives the lifting-surface coeff.
        private float _planformArea;
        private const float PlanformLiftFactor = 1f;   // deflectionLiftCoeff per m^2 of planform

        internal override void UpdateShape(bool force = true)
        {
            Profiler.BeginSample("UpdateShape Spine");
            bool prevRebuilding = _rebuilding;
            _rebuilding = true;
            try
            {
            EnsureNodes();
            SortNodes();
            part.CoMOffset = CoMOffset;
            if (HighLogic.LoadedSceneIsEditor && shellMode) UpdateShellThicknessRange();

            List<Ring> rings = BuildRings();
            float maxDia = 0f, minDia = float.MaxValue, minBoundDia = float.MaxValue;
            foreach (Ring r in rings)
            {
                float bound = 2f * Mathf.Max(r.rH, r.rV);   // bounding-circle diameter of this ring
                maxDia = Mathf.Max(maxDia, bound);
                minBoundDia = Mathf.Min(minBoundDia, bound);
                minDia = Mathf.Min(minDia, 2f * Mathf.Min(r.rH, r.rV));
            }
            MaxDiameter = maxDia;
            MinDiameter = minDia;
            // Smallest bounding-circle diameter over the spine (widest extent of the narrowest ring). Paired
            // with MaxDiameter it gives RP-1 tooling the min/max bounding cylinders; equal for a constant
            // section (even a non-circular one), so such a spine tools as a single cylinder, not two.
            MinBoundingDiameter = (minBoundDia == float.MaxValue) ? maxDia : minBoundDia;
            InnerMaxDiameter = InnerMinDiameter = -1f;
            Length = length;

            GenerateSideMesh(rings);
            GenerateCapMesh(rings);          // emits empty end meshes when shellMode (open ends)
            GenerateColliderMesh(rings);

            UpdateNodeSize(BottomNodeName, EndDiameter(0));
            UpdateNodeSize(TopNodeName, EndDiameter(rings.Count - 1));

            _planformArea = PlanformArea(rings);
            ApplyLiftingSurface();

            // Set volume last (the setter fires OnPartVolumeChanged / onEditorShipModified) so the
            // visible mesh is already updated even if a volume listener throws.
            NominalVolume = CalculateVolume(rings);
            Volume = NominalVolume;

            PPart.UpdateProps();
            RaiseModelAndColliderChanged();
            }
            finally
            {
                _rebuilding = prevRebuilding;
                Profiler.EndSample();
            }
        }

        // A loft ring: position + half-extents (for diameter) + centre offsets + tilt (the ring plane is
        // sheared along the spine by tan(tilt) per unit of in-plane coordinate) + the precomputed
        // Sides-point boundary outline (profile morph already applied).
        private struct Ring { public float y, rH, rV, offsetZ, offsetX, tanTiltV, tanTiltH; public Vector2[] outline; }

        // 3D vertex of ring outline point j (index wraps): outline (x=width, y=height) placed at the ring's
        // y and shifted by its centre offsets, with the plane sheared along the spine by the tilts (top/right
        // of the section move fore/aft). Tilt uses the point's in-plane coords so it pivots about the centre.
        private static Vector3 RingV(Ring ring, int j)
        {
            int n = ring.outline.Length;
            Vector2 p = ring.outline[((j % n) + n) % n];
            float y = ring.y + p.y * ring.tanTiltV + p.x * ring.tanTiltH;
            return new Vector3(p.x + ring.offsetX, y, p.y + ring.offsetZ);
        }

        // Sides-point outline of a ring, morphing profile pa->pb by blend, scaled to rH x rV, with the
        // top/bottom fillet fractions also blended (a..b) for the rounded-rect profiles.
        private static Vector2[] RingOutline(SpineProfile pa, SpineProfile pb, float blend,
                                             float rH, float rV, float ftA, float fbA, float ftB, float fbB,
                                             float sA, float sB)
        {
            Vector2[] a = ProfileOutline(pa, rH, rV, ftA, fbA, sA);
            if (blend <= 0f || (pa == pb && ftA == ftB && fbA == fbB && sA == sB)) return a;
            Vector2[] b = ProfileOutline(pb, rH, rV, ftB, fbB, sB);
            var o = new Vector2[Sides];
            for (int j = 0; j < Sides; j++) o[j] = Vector2.Lerp(a[j], b[j], blend);
            return o;
        }

        // Sides boundary points of a profile scaled to rH x rV. Each profile has a FIXED Sides-point
        // topology (its corners forced to vertices) so flats stay flat, corners stay crisp, and rings
        // of the same profile loft with aligned edges. Rectangle and Mk3 are rounded rectangles whose
        // corner radii come from the top/bottom fillet fractions (0 = sharp corner).
        private static Vector2[] ProfileOutline(SpineProfile profile, float rH, float rV, float ftFrac, float fbFrac,
                                                float stretch)
        {
            switch (profile)
            {
                case SpineProfile.Rectangle: return BuildRoundedRect(rH, rV, ftFrac, fbFrac);
                case SpineProfile.Mk3: return BuildMk3Outline(rH, rV);
                case SpineProfile.Mk2: break;
                default: // Ellipse: flatten toward a rectangle (superellipse) if the stretch slider is non-zero.
                    if (stretch > 0f) return BuildStretchedEllipse(rH, rV, stretch);
                    break;
            }

            Vector2[] unit = (profile == SpineProfile.Mk2) ? UnitMk2 : UnitCircle;   // Mk2 / plain Ellipse
            Vector2 scale = new Vector2(rH, rV);
            var o = new Vector2[Sides];
            for (int j = 0; j < Sides; j++) o[j] = Vector2.Scale(unit[j], scale);
            return o;
        }

        // Ellipse with the faces flattened toward a rectangle -- a superellipse sampled at the same uniform
        // angles as UnitCircle (so it lofts/blends point-for-point with plain-ellipse and other-profile
        // neighbours). Exponent e: 1 = true ellipse, ->0 flattens a face into a flat wall. The flattening is
        // applied to the LONGER axis's end faces (left/right when wider, top/bottom when taller) -- for a
        // circle the two are congruent, so one slider covers both; the shorter axis stays elliptical.
        private static Vector2[] BuildStretchedEllipse(float rH, float rV, float stretch)
        {
            float e = SquarenessToExp(stretch);
            bool wide = rH >= rV;
            float eH = wide ? e : 1f;   // wide  -> flatten the left/right sides (x-faces)
            float eV = wide ? 1f : e;   // tall  -> flatten the top/bottom (y-faces)
            var o = new Vector2[Sides];
            for (int j = 0; j < Sides; j++)
            {
                float a = 2f * Mathf.PI * j / Sides;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                o[j] = new Vector2(Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), eH) * rH,
                                   Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), eV) * rV);
            }
            return o;
        }

        // Map a 0..1 stretch slider to a superellipse exponent: 0 -> 1 (true ellipse), 1 -> near 0 (flat wall).
        private static float SquarenessToExp(float sq) => Mathf.Lerp(1f, 0.08f, Mathf.Clamp01(sq));

        // Rounded rectangle: half-extents (rH, rV), independent top/bottom corner radii from the fillet
        // fractions (of the smaller half-extent). Tangent points are forced so the flats stay flat; the
        // fillet arcs are sampled smoothly. Fillet 0 -> a forced sharp corner.
        private static Vector2[] BuildRoundedRect(float rH, float rV, float ftFrac, float fbFrac)
        {
            float m = Mathf.Min(rH, rV);
            float tr = Mathf.Clamp01(ftFrac) * m;
            float br = Mathf.Clamp01(fbFrac) * m;
            var pts = new List<Vector2>();
            var corners = new List<int>();
            pts.Add(new Vector2(rH, 0f));                                                          // index 0: mid right edge
            AddFillet(pts, corners, new Vector2(rH - tr, rV - tr), tr, 0f, 90f, new Vector2(rH, rV));      // top-right
            AddFillet(pts, corners, new Vector2(-(rH - tr), rV - tr), tr, 90f, 180f, new Vector2(-rH, rV)); // top-left
            AddFillet(pts, corners, new Vector2(-(rH - br), -(rV - br)), br, 180f, 270f, new Vector2(-rH, -rV)); // bottom-left
            AddFillet(pts, corners, new Vector2(rH - br, -(rV - br)), br, 270f, 360f, new Vector2(rH, -rV));     // bottom-right
            return ResampleWithCorners(pts, corners, Sides);
        }

        // Append one corner: a quarter arc of `radius` about `center` (left smooth so arc-length sampling
        // rounds it evenly), or a single forced-sharp point when the radius is ~0.
        private static void AddFillet(List<Vector2> pts, List<int> corners, Vector2 center, float radius,
                                      float startDeg, float endDeg, Vector2 sharp)
        {
            if (radius < 1e-4f) { corners.Add(pts.Count); pts.Add(sharp); return; }
            const int steps = 8;
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(startDeg, endDeg, (float)i / steps);
                pts.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
        }

        // Mk3: a vertical circle (radius = rV) with its left/right sides sliced off flat at x = +/-rH, so
        // the top/bottom are arcs and the sides are flats (the real Mk3 is a 3.75 m circle cut to 3.25 m
        // wide). The 4 flat/arc junctions are forced sharp. If rH >= rV there's no truncation -> ellipse.
        private static Vector2[] BuildMk3Outline(float rH, float rV)
        {
            float R = rV;
            if (rH >= R)
            {
                var e = new Vector2[Sides];
                for (int j = 0; j < Sides; j++) { float a = 2f * Mathf.PI * j / Sides; e[j] = new Vector2(Mathf.Cos(a) * rH, Mathf.Sin(a) * rV); }
                return e;
            }
            float yc = Mathf.Sqrt(Mathf.Max(0f, R * R - rH * rH));   // flat/arc junction height
            float tc = Mathf.Atan2(yc, rH);                          // junction angle
            const int arc = 10;
            var pts = new List<Vector2>();
            var corners = new List<int>();
            corners.Add(pts.Count); pts.Add(new Vector2(rH, -yc));   // right flat, bottom
            corners.Add(pts.Count); pts.Add(new Vector2(rH, yc));    // right flat, top
            for (int i = 1; i < arc; i++) { float a = Mathf.Lerp(tc, Mathf.PI - tc, (float)i / arc); pts.Add(new Vector2(Mathf.Cos(a) * R, Mathf.Sin(a) * R)); }  // top arc
            corners.Add(pts.Count); pts.Add(new Vector2(-rH, yc));   // left flat, top
            corners.Add(pts.Count); pts.Add(new Vector2(-rH, -yc));  // left flat, bottom
            for (int i = 1; i < arc; i++) { float a = Mathf.Lerp(Mathf.PI + tc, 2f * Mathf.PI - tc, (float)i / arc); pts.Add(new Vector2(Mathf.Cos(a) * R, Mathf.Sin(a) * R)); }  // bottom arc
            return ResampleWithCorners(pts, corners, Sides);
        }

        // Unit outlines in the [-1,1] box, Sides points each, starting near +x going CCW.
        private static Vector2[] _unitCircle, _unitRect, _unitMk2;
        private static Vector2[] UnitCircle
        {
            get
            {
                if (_unitCircle == null)
                {
                    _unitCircle = new Vector2[Sides];
                    for (int j = 0; j < Sides; j++) { float a = 2f * Mathf.PI * j / Sides; _unitCircle[j] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); }
                }
                return _unitCircle;
            }
        }
        private static Vector2[] UnitRect => _unitRect ??= ResampleWithCorners(
            new List<Vector2> { new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(-1f, -1f) },
            new List<int> { 0, 1, 2, 3 }, Sides);
        private static Vector2[] UnitMk2
        {
            get
            {
                if (_unitMk2 == null) { Vector2[] poly = BuildMk2Unit(out List<int> corners); _unitMk2 = ResampleWithCorners(new List<Vector2>(poly), corners, Sides); }
                return _unitMk2;
            }
        }

        // Resample a closed polygon to N points by arc length, forcing the corner vertices to be kept
        // (flats stay flat, corners stay crisp).
        private static Vector2[] ResampleWithCorners(IList<Vector2> verts, IList<int> cornerIdx, int N)
        {
            int M = verts.Count;
            float[] arcAt = new float[M + 1];
            for (int i = 0; i < M; i++) arcAt[i + 1] = arcAt[i] + (verts[(i + 1) % M] - verts[i]).magnitude;
            float perim = arcAt[M];
            var targets = new List<float>();
            foreach (int ci in cornerIdx) targets.Add(arcAt[ci]);
            int fills = Mathf.Max(0, N - cornerIdx.Count);
            for (int i = 0; i < fills; i++) targets.Add((i + 0.5f) / fills * perim);
            targets.Sort();
            var outp = new Vector2[N];
            for (int k = 0; k < N; k++) outp[k] = PointAtArc(verts, arcAt, perim, targets[k]);
            return outp;
        }

        private static Vector2 PointAtArc(IList<Vector2> verts, float[] arcAt, float perim, float arc)
        {
            int M = verts.Count;
            arc = Mathf.Clamp(arc, 0f, perim);
            for (int i = 0; i < M; i++)
            {
                if (arc <= arcAt[i + 1] + 1e-6f)
                {
                    float seg = arcAt[i + 1] - arcAt[i];
                    float u = (seg > 1e-9f) ? (arc - arcAt[i]) / seg : 0f;
                    return Vector2.Lerp(verts[i], verts[(i + 1) % M], u);
                }
            }
            return verts[0];
        }

        // --- Mk2 cross-section (ported from ProceduralShapeMk2) ---
        // Proportions as ratios of the half-width, calibrated against the real Mk2.
        private const float Mk2HeightRatio = 0.6047f;    // full height / full width
        private const float Mk2SideFlatRatio = 0.11907f; // side-flat half-height / half-width
        private const float Mk2RoundRatio = 0.63628f;    // top/bottom arc radius / half-width

        // Mk2 silhouette polygon normalized into [-1,1]^2; cornerIdx = the 4 hard shoulder vertices.
        private static Vector2[] BuildMk2Unit(out List<int> cornerIdx)
        {
            const float hw = 1f;
            float hh = Mk2HeightRatio * hw, sf = Mk2SideFlatRatio * hw, tr = Mk2RoundRatio * hw;
            Vector2 pRt = new Vector2(hw, sf), pRb = new Vector2(hw, -sf);
            Vector2 pLt = new Vector2(-hw, sf), pLb = new Vector2(-hw, -sf);
            Vector2 cTop = new Vector2(0f, hh - tr);
            Vector2 tUr = Mk2Tangent(pRt, cTop, tr);
            Vector2 tUl = new Vector2(-tUr.x, tUr.y);
            Vector2 tLr = new Vector2(tUr.x, -tUr.y);
            Vector2 tLl = new Vector2(-tUr.x, -tUr.y);
            float thR = Mathf.Atan2(tUr.y - cTop.y, tUr.x - cTop.x), thL = Mathf.PI - thR;
            const int arc = 12;

            var pts = new List<Vector2>();
            cornerIdx = new List<int>();
            cornerIdx.Add(pts.Count); pts.Add(pRb);     // right-bottom shoulder
            cornerIdx.Add(pts.Count); pts.Add(pRt);     // right-top shoulder
            pts.Add(tUr);                                // up-right diagonal end
            for (int k = 1; k < arc; k++) { float th = thR + (thL - thR) * k / arc; pts.Add(cTop + new Vector2(Mathf.Cos(th), Mathf.Sin(th)) * tr); }
            pts.Add(tUl);
            cornerIdx.Add(pts.Count); pts.Add(pLt);     // left-top shoulder
            cornerIdx.Add(pts.Count); pts.Add(pLb);     // left-bottom shoulder
            pts.Add(tLl);                                // low-left diagonal end
            for (int k = arc - 1; k >= 1; k--) { float th = thR + (thL - thR) * k / arc; pts.Add(new Vector2(Mathf.Cos(th) * tr, -(cTop.y + Mathf.Sin(th) * tr))); }
            pts.Add(tLr);                                // bottom arc end + low-right diagonal closes to pRb

            for (int i = 0; i < pts.Count; i++) pts[i] = new Vector2(pts[i].x, pts[i].y / hh);   // normalize y to [-1,1]
            return pts.ToArray();
        }

        // Tangent point on circle (centre C, radius r) from external point P, toward the cap apex.
        private static Vector2 Mk2Tangent(Vector2 P, Vector2 C, float r)
        {
            Vector2 d = P - C;
            float dist = d.magnitude;
            float a = Mathf.Atan2(d.y, d.x);
            float b = Mathf.Acos(Mathf.Clamp(r / dist, -1f, 1f));
            Vector2 t1 = C + new Vector2(Mathf.Cos(a + b), Mathf.Sin(a + b)) * r;
            Vector2 t2 = C + new Vector2(Mathf.Cos(a - b), Mathf.Sin(a - b)) * r;
            return (Mathf.Abs(t1.y) >= Mathf.Abs(t2.y)) ? t1 : t2;
        }

        // Inset a (CCW) outline inward by `t` metres along the local inward normal -- the shell's inner
        // wall. Interior is to the LEFT of edge travel for a CCW polygon, so an edge (dx,dy)'s inward
        // normal is (-dy,dx); a vertex uses the averaged normal of its two edges. Averaging undershoots
        // slightly at corners (fine for a wall). Caller clamps `t` so a thick wall can't invert a small ring.
        private static Vector2[] InsetOutline(Vector2[] o, float t)
        {
            int n = o.Length;
            var res = new Vector2[n];
            for (int j = 0; j < n; j++)
            {
                Vector2 prev = o[(j - 1 + n) % n], cur = o[j], next = o[(j + 1) % n];
                Vector2 e1 = cur - prev, e2 = next - cur;
                Vector2 in1 = new Vector2(-e1.y, e1.x); if (in1.sqrMagnitude > 1e-12f) in1.Normalize();
                Vector2 in2 = new Vector2(-e2.y, e2.x); if (in2.sqrMagnitude > 1e-12f) in2.Normalize();
                Vector2 inward = in1 + in2;
                inward = (inward.sqrMagnitude > 1e-12f) ? inward.normalized : in2;
                res[j] = cur + inward * t;
            }
            return res;
        }

        // A ring's inner wall: same placement (y / offsets / tilt), outline inset by the shell thickness,
        // clamped so a thick wall on a small section can't collapse or invert the hole.
        private Ring InnerRing(Ring outer)
        {
            float t = Mathf.Min(shellThickness, ShellInsetFraction * Mathf.Min(outer.rH, outer.rV));
            Ring inner = outer;                       // struct copy carries y/offsets/tilt
            inner.outline = InsetOutline(outer.outline, t);
            return inner;
        }

        private List<Ring> BuildInnerRings(List<Ring> rings)
        {
            var inner = new List<Ring>(rings.Count);
            foreach (Ring r in rings) inner.Add(InnerRing(r));
            return inner;
        }

        // Polygon area of a ring's outline (shoelace), used for an accurate volume of any profile.
        private static float RingArea(Ring ring)
        {
            Vector2[] o = ring.outline;
            int n = o.Length;
            float area = 0f;
            for (int j = 0; j < n; j++)
            {
                Vector2 prev = o[(j + n - 1) % n], cur = o[j];
                area += prev.x * cur.y - cur.x * prev.y;
            }
            return Mathf.Abs(area) * 0.5f;
        }

        // Sane size bounds. A node "size" is a cross-section diameter. The hard 100 m cap on the
        // effective (scaled) radius keeps the volume well within the SI formatter's range -- an
        // absurd value made TankContentSwitcher throw "Illegal prefix".
        private float MaxSize => (PPart.diameterMax == float.PositiveInfinity) ? 50f : Mathf.Min(PPart.diameterMax, 50f);
        private float MinSize => Mathf.Max(0.1f, PPart.diameterMin);

        // A section with a zero half-extent has no area -- it has collapsed to a point (both axes) or a
        // line (one axis), so it contributes no volume and nothing renders across it.
        private static bool IsClosedSection(SpineNode n) => n.sizeH <= 0f || n.sizeV <= 0f;

        // The two end nodes may shrink to zero so the loft closes to a point (a proper cone/ogive tip);
        // interior nodes keep the normal minimum, since a pinched waist would just self-intersect.
        // Exception: a two-node spine needs one open end -- closing both leaves a point-to-point line
        // with nothing to loft, i.e. an invisible part. The second one is held at the normal minimum.
        private float MinSizeFor(int i)
        {
            if (!IsEndNode(i)) return MinSize;
            return (nodes.Count == 2 && IsClosedSection(nodes[1 - i])) ? MinSize : 0f;
        }

        // Heal any out-of-range node sizes (e.g. a part bloated by an earlier bug) back into bounds.
        private void ClampNodeSizes()
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                SpineNode n = nodes[i];
                float min = MinSizeFor(i);
                n.sizeH = Mathf.Clamp(n.sizeH, min, MaxSize);
                n.sizeV = Mathf.Clamp(n.sizeV, min, MaxSize);
            }
        }

        // Effective (scaled) half-extents of a node, clamped finite/positive. The lower bound is 0 so a
        // closed end collapses to a point; interior nodes are held at MinSize by the clamps above.
        private float Erh(SpineNode n) => 0.5f * Mathf.Clamp(n.sizeH * hScale, 0f, 100f);
        private float Erv(SpineNode n) => 0.5f * Mathf.Clamp(n.sizeV * vScale, 0f, 100f);
        private float Eoff(SpineNode n) => Mathf.Clamp(n.offsetV * vScale, -100f, 100f);
        private float EoffH(SpineNode n) => Mathf.Clamp(n.offsetH * hScale, -100f, 100f);
        private static float TiltTan(float deg) => Mathf.Tan(Mathf.Deg2Rad * Mathf.Clamp(deg, -85f, 85f));
        private float NodeY(SpineNode n) => (n.position - 0.5f) * length;

        private Ring NodeRing(SpineNode n)
        {
            float rH = Erh(n), rV = Erv(n);
            return new Ring
            {
                y = NodeY(n), rH = rH, rV = rV, offsetZ = Eoff(n), offsetX = EoffH(n),
                tanTiltV = TiltTan(n.tiltV), tanTiltH = TiltTan(n.tiltH),
                outline = RingOutline(n.profile, n.profile, 0f, rH, rV, n.filletTop, n.filletBottom, n.filletTop, n.filletBottom,
                                      n.stretch, n.stretch),
            };
        }

        // Build the loft rings, subdividing each segment per its lower node's slopeAbove so the
        // silhouette curves (concave/convex/waisted), and morphing the cross-section profile from the
        // lower node's to the upper node's across the segment. Linear/same-profile segments stay coarse.
        private List<Ring> BuildRings()
        {
            EnsureNodes();
            SortNodes();
            var rings = new List<Ring> { NodeRing(nodes[0]) };
            for (int i = 1; i < nodes.Count; i++)
            {
                SpineNode lo = nodes[i - 1], hi = nodes[i];
                float aY = NodeY(lo), bY = NodeY(hi), aH = Erh(lo), aV = Erv(lo), bH = Erh(hi), bV = Erv(hi);
                float aOff = Eoff(lo), bOff = Eoff(hi);
                float aOffX = EoffH(lo), bOffX = EoffH(hi);
                float aTv = lo.tiltV, bTv = hi.tiltV, aTh = lo.tiltH, bTh = hi.tiltH;
                SpineSlope slope = lo.slopeAbove;
                // Fillets only change the outline for Rectangle, so a fillet difference only forces
                // subdivision when both ends are rectangles (Mk2/Mk3/Ellipse ignore the fillet values).
                bool filletMorph = lo.profile == SpineProfile.Rectangle && hi.profile == SpineProfile.Rectangle
                                   && (lo.filletTop != hi.filletTop || lo.filletBottom != hi.filletBottom);
                // Likewise a stretch difference between two ellipses only bends the silhouette, so it also
                // needs subdivision to morph smoothly.
                bool stretchMorph = lo.profile == SpineProfile.Ellipse && hi.profile == SpineProfile.Ellipse
                                    && lo.stretch != hi.stretch;
                bool morph = lo.profile != hi.profile || filletMorph || stretchMorph;
                int k = (slope == SpineSlope.Linear && !morph) ? 1 : 10;
                for (int s = 1; s <= k; s++)
                {
                    float t = (float)s / k;
                    float h = SlopeBlend(slope, t);
                    float w = WaistFactor(slope, t);
                    float rH = Mathf.Lerp(aH, bH, h) * w;
                    float rV = Mathf.Lerp(aV, bV, h) * w;
                    rings.Add(new Ring
                    {
                        y = Mathf.Lerp(aY, bY, t),
                        rH = rH,
                        rV = rV,
                        offsetZ = Mathf.Lerp(aOff, bOff, h),
                        offsetX = Mathf.Lerp(aOffX, bOffX, h),
                        tanTiltV = TiltTan(Mathf.Lerp(aTv, bTv, h)),
                        tanTiltH = TiltTan(Mathf.Lerp(aTh, bTh, h)),
                        outline = RingOutline(lo.profile, hi.profile, t, rH, rV,
                                              lo.filletTop, lo.filletBottom, hi.filletTop, hi.filletBottom,
                                              lo.stretch, hi.stretch),
                    });
                }
            }
            return rings;
        }

        // Radius blend across a segment, t in [0,1]. Maps the straight 0..1 chord into a curve.
        private static float SlopeBlend(SpineSlope slope, float t)
        {
            switch (slope)
            {
                case SpineSlope.Concave: return t * t;            // caves inward (below the chord)
                case SpineSlope.Convex: return t * (2f - t);      // bulges outward (above the chord)
                default: return t;                                 // Linear / Waisted use the chord
            }
        }

        // Multiplicative pinch for the waisted profile (narrow in the middle), 1 otherwise.
        private static float WaistFactor(SpineSlope slope, float t) =>
            slope == SpineSlope.Waisted ? 1f - 0.35f * Mathf.Sin(Mathf.PI * t) : 1f;

        // Top-view planform area: trapezoidal integral of the full width (2*rH) along the spine.
        private static float PlanformArea(List<Ring> rings)
        {
            float area = 0f;
            for (int i = 1; i < rings.Count; i++)
            {
                float w0 = 2f * rings[i - 1].rH, w1 = 2f * rings[i].rH;
                area += 0.5f * (w0 + w1) * Mathf.Abs(rings[i].y - rings[i - 1].y);
            }
            return area;
        }

        // Drive the part's stock ModuleLiftingSurface from the planform: lift scales with area when the
        // Airfoil toggle is on, zero when off. No-op if the part has no lifting-surface module.
        private void ApplyLiftingSurface()
        {
            ModuleLiftingSurface ls = part.FindModuleImplementing<ModuleLiftingSurface>();
            if (ls == null) return;
            ls.deflectionLiftCoeff = airfoil ? Mathf.Max(0.01f, _planformArea * PlanformLiftFactor) : 0f;
        }

        // Toggling Airfoil doesn't change geometry, so just re-apply the coeff here and on counterparts.
        private void OnAirfoilChanged()
        {
            ApplyLiftingSurface();
            foreach (Part p in part.symmetryCounterparts)
                if (FindAbstractShapeModule(p, this) is ProceduralShapeSpine pm) { pm.airfoil = airfoil; pm.ApplyLiftingSurface(); }
        }

        // Bounding diameter of an end ring (used for stack node sizing).
        private float EndDiameter(int ringIndex)
        {
            EnsureNodes();
            SpineNode n = nodes[Mathf.Clamp(ringIndex, 0, nodes.Count - 1)];
            return Mathf.Max(n.sizeH * hScale, n.sizeV * vScale);
        }

        public override float CalculateVolume() => CalculateVolume(BuildRings());

        // Trapezoidal integration of the material cross-section area along the spine -- correct for any
        // profile (ellipse/rect/Mk2/Mk3). Solid uses the full outline area; a shell uses only the wall,
        // i.e. the outer section minus the inner (inset) section from the same InnerRing the mesh builds.
        // That respects the thickness clamp and can't double-count once the wall is a big fraction of the
        // radius (perimeter*thickness overcounts the corners there). Both the Volume property and this
        // override use this single definition so SeekVolume / volume bounds stay self-consistent.
        private float CalculateVolume(List<Ring> rings)
        {
            float v = 0f;
            float prevArea = SectionArea(rings[0]);
            for (int i = 1; i < rings.Count; i++)
            {
                float area = SectionArea(rings[i]);
                // Simpson rather than trapezoid: the loft interpolates its vertices linearly between two
                // rings, so the section area varies QUADRATICALLY along the segment (a product of two
                // linear half-extents). The trapezoid rule over-reports every taper -- by 50% on a segment
                // that closes to a point -- and a Linear segment isn't subdivided, so there's no ring
                // density to hide it. Simpson is exact for a quadratic, so this matches the real mesh.
                float midArea = SectionArea(MidRing(rings[i - 1], rings[i]));
                v += (rings[i].y - rings[i - 1].y) * (prevArea + 4f * midArea + area) / 6f;
                prevArea = area;
            }
            return Mathf.Abs(v);
        }

        // The section halfway between two rings. The mesh lerps its vertices, so the midpoint outline is
        // the per-point average; only the outline and half-extents matter here (area, and the shell's
        // inset clamp). Placement fields are irrelevant -- the outline is in local section coordinates.
        private static Ring MidRing(Ring a, Ring b)
        {
            Ring m = a;
            m.rH = 0.5f * (a.rH + b.rH);
            m.rV = 0.5f * (a.rV + b.rV);
            var o = new Vector2[a.outline.Length];
            for (int j = 0; j < o.Length; j++) o[j] = 0.5f * (a.outline[j] + b.outline[j]);
            m.outline = o;
            return m;
        }

        // Material cross-section of a ring: the full outline when solid, or the annulus (outer minus the
        // clamped inner outline) in shell mode -- bounded by the outer area, so it never exceeds the solid.
        private float SectionArea(Ring ring) =>
            shellMode ? Mathf.Max(0f, RingArea(ring) - RingArea(InnerRing(ring))) : RingArea(ring);

        public override void AdjustDimensionBounds()
        {
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).minValue = PPart.lengthMin;
            (Fields[nameof(length)].uiControlEditor as UI_FloatEdit).maxValue = PPart.lengthMax;

            // Scale ranges are derived from the part's diameter limits relative to the largest node.
            float largest = 0.01f;
            foreach (SpineNode n in nodes) largest = Mathf.Max(largest, Mathf.Max(n.sizeH, n.sizeV));
            float minScale = Mathf.Max(0.05f, PPart.diameterMin / largest);
            float maxScale = (PPart.diameterMax == float.PositiveInfinity) ? 10f : PPart.diameterMax / largest;
            foreach (string fn in new[] { nameof(hScale), nameof(vScale) })
            {
                (Fields[fn].uiControlEditor as UI_FloatEdit).minValue = minScale;
                (Fields[fn].uiControlEditor as UI_FloatEdit).maxValue = Mathf.Max(minScale + 0.1f, maxScale);
            }
        }

        public override bool SeekVolume(float targetVolume, int dir) => SeekVolume(targetVolume, Fields[nameof(length)], dir);

        public override void UpdateTFInterops()
        {
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new object[] { part, "diam1", MaxDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new object[] { part, "diam2", MaxDiameter, "ProceduralParts" });
            ProceduralPart.tfInterface.InvokeMember("AddInteropValue", ProceduralPart.tfBindingFlags, null, null, new object[] { part, "length", length, "ProceduralParts" });
        }

        public override void TranslateAttachmentsAndNodes(BaseField f, object obj)
        {
            if (f.name == nameof(length) && obj is float oldLen)
                HandleLengthChange((float)f.GetValue(this), oldLen);
            else if ((f.name == nameof(hScale) || f.name == nameof(vScale)) && obj is float oldScale && oldScale > 0f)
                HandleDiameterChange((float)f.GetValue(this), oldScale);
        }

        // Phase-1 circular approximation: normalize by the widest radius. Phase 2 makes this
        // per-(angle, height) against the actual elliptical rings for accurate surface attach.
        public override void NormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            float r = Mathf.Max(0.01f, MaxDiameter / 2);
            coords.r /= r;
            coords.y /= Mathf.Max(0.01f, length);
        }

        public override void UnNormalizeCylindricCoordinates(ShapeCoordinates coords)
        {
            coords.r *= Mathf.Max(0.01f, MaxDiameter / 2);
            coords.y *= Mathf.Max(0.01f, length);
        }

        // --- Attachment following ---------------------------------------------------------------------
        // The per-node PAW edits and gizmo drags change the surface but don't run through
        // TranslateAttachmentsAndNodes (that only fires for the whole-part length / scale sliders). So we
        // snapshot the attachments against the pre-edit geometry (SampleSurface + SnapshotAttachments),
        // apply the edit, then re-place them on the post-edit geometry (ReplaceAttachments) so stack parts
        // and surface-attached children stay glued to the skin as nodes are resized / offset / moved.

        // Centre (offsetX/Z) + half-extents (rH/rV) of the body at a height y, sampled from the loft rings.
        private struct SurfSlice { public float y, cX, cZ, sX, sZ; }

        private List<SurfSlice> SampleSurface()
        {
            List<Ring> rings = BuildRings();   // sorted by y ascending
            var s = new List<SurfSlice>(rings.Count);
            foreach (Ring r in rings)
                s.Add(new SurfSlice { y = r.y, cX = r.offsetX, cZ = r.offsetZ,
                                      sX = Mathf.Max(1e-3f, r.rH), sZ = Mathf.Max(1e-3f, r.rV) });
            return s;
        }

        // Interpolate the section centre / size at an arbitrary height y (clamped to the end rings).
        private static SurfSlice SurfaceAt(List<SurfSlice> s, float y)
        {
            int n = s.Count;
            if (n == 0) return new SurfSlice { y = y, sX = 1f, sZ = 1f };
            if (y <= s[0].y) { SurfSlice r0 = s[0]; r0.y = y; return r0; }
            if (y >= s[n - 1].y) { SurfSlice r1 = s[n - 1]; r1.y = y; return r1; }
            for (int i = 1; i < n; i++)
            {
                if (y > s[i].y) continue;
                SurfSlice a = s[i - 1], b = s[i];
                float t = (y - a.y) / Mathf.Max(1e-3f, b.y - a.y);
                return new SurfSlice { y = y, cX = Mathf.Lerp(a.cX, b.cX, t), cZ = Mathf.Lerp(a.cZ, b.cZ, t),
                                       sX = Mathf.Lerp(a.sX, b.sX, t), sZ = Mathf.Lerp(a.sZ, b.sZ, t) };
            }
            SurfSlice last = s[n - 1]; last.y = y; return last;
        }

        // One attachment captured before an edit: `local` is the attach point in this part's frame (used
        // to sample the surface), `world` its absolute position (used as the move origin, so a child still
        // moves correctly even if a stack push has since shifted our own transform).
        private struct AttachSnap { public AttachNode node; public bool stack; public Vector3 local, world; }

        private List<AttachSnap> SnapshotAttachments()
        {
            var snaps = new List<AttachSnap>();
            foreach (AttachNode n in part.attachNodes)
                if (n.nodeType == AttachNode.NodeType.Stack)
                    snaps.Add(new AttachSnap { node = n, stack = true, local = n.position });
            foreach (Part c in part.children)
                if (c.FindAttachNodeByPart(part) is AttachNode cn && cn.nodeType == AttachNode.NodeType.Surface)
                {
                    Vector3 world = cn.owner.transform.TransformPoint(cn.position);
                    snaps.Add(new AttachSnap { node = cn, stack = false,
                                               local = part.transform.InverseTransformPoint(world), world = world });
                }
            return snaps;
        }

        // Move each snapshotted attachment onto the current (post-edit) surface. A stack part sits on the
        // axis, so its node follows only the section-centre offset; a surface child also scales radially
        // with the per-axis size change (its offset from the section centre grows/shrinks with the skin).
        private void ReplaceAttachments(List<SurfSlice> oldS, List<AttachSnap> snaps)
        {
            if (snaps == null || snaps.Count == 0) return;
            List<SurfSlice> newS = SampleSurface();
            foreach (AttachSnap s in snaps)
            {
                Vector3 p = s.local;
                SurfSlice a = SurfaceAt(oldS, p.y), b = SurfaceAt(newS, p.y);
                if (s.stack)
                {
                    Vector3 d = new Vector3(b.cX - a.cX, 0f, b.cZ - a.cZ);
                    if (d.sqrMagnitude < 1e-8f) continue;
                    TranslateNode(s.node, d);
                    if (s.node.attachedPart is Part ap) TranslatePart(ap, d);
                }
                else
                {
                    Vector3 np = new Vector3(b.cX + (p.x - a.cX) * b.sX / a.sX, p.y,
                                             b.cZ + (p.z - a.cZ) * b.sZ / a.sZ);
                    if ((np - p).sqrMagnitude < 1e-8f) continue;
                    Vector3 wNew = part.transform.TransformPoint(np);
                    s.node.owner.transform.Translate(wNew - s.world, Space.World);
                }
            }
        }

        // Drag baseline: geometry + attachment snapshot captured at gizmo mouse-down, consumed on release.
        private List<SurfSlice> _dragSurf0;
        private List<AttachSnap> _dragAttach0;

        #endregion

        #region Node editing

        private void RefreshSelector()
        {
            if (Fields[nameof(selectedNode)].uiControlEditor is UI_FloatRange fr)
                fr.maxValue = Mathf.Max(0, nodes.Count - 1);
            selectedNode = SelIndex;
        }

        // Pull the selected node's data into the PAW proxy fields.
        private void LoadProxyFromNode()
        {
            if (nodes.Count == 0) return;
            SpineNode n = nodes[SelIndex];
            nodeSlopeOpt = n.slopeAbove.ToString();
            nodeProfileOpt = n.profile.ToString();
            nodePos = n.position;
            nodeWidth = n.sizeH;
            nodeHeight = n.sizeV;
            nodeTiltV = n.tiltV;
            nodeTiltH = n.tiltH;
            nodeOffsetV = n.offsetV;
            nodeOffsetH = n.offsetH;
            nodeFilletTop = n.filletTop;
            nodeFilletBottom = n.filletBottom;
            nodeStretch = n.stretch;
            Fields[nameof(nodePos)].guiActiveEditor = !IsEndNode(SelIndex);   // end nodes are pinned fore/aft
            UpdateSizeFieldBounds();
            UpdateProfileFieldVisibility(n.profile);
            UpdateAddButtons();
        }

        // The width/height sliders bottom out at 0 on an end node (closed tip) and at MinSize elsewhere,
        // so the lower bound is re-applied whenever the selection changes.
        private void UpdateSizeFieldBounds()
        {
            float min = MinSizeFor(SelIndex);
            foreach (string fn in new[] { nameof(nodeWidth), nameof(nodeHeight) })
                if (Fields[fn].uiControlEditor is UI_FloatEdit e) e.minValue = min;
        }

        // Profile-specific sliders: the fillets only apply to Rectangle (corner rounding), the stretch
        // sliders only to Ellipse (face flattening).
        private void UpdateProfileFieldVisibility(SpineProfile p)
        {
            bool rect = p == SpineProfile.Rectangle;
            Fields[nameof(nodeFilletTop)].guiActiveEditor = rect;
            Fields[nameof(nodeFilletBottom)].guiActiveEditor = rect;
            bool ellipse = p == SpineProfile.Ellipse;
            Fields[nameof(nodeStretch)].guiActiveEditor = ellipse;
        }

        // The shell-thickness slider only matters when the hollow shell is on.
        private void UpdateShellVisibility() => Fields[nameof(shellThickness)].guiActiveEditor = shellMode;

        // Scale the shell-thickness slider's range to the part: the wall can inset at most
        // ShellInsetFraction of the narrowest section's smaller half-extent, so cap the slider there
        // (and scale its steps) so every value on the slider is actually achievable. Also heals a saved
        // thickness that's now too big for a shrunken part.
        private void UpdateShellThicknessRange()
        {
            if (!(Fields[nameof(shellThickness)].uiControlEditor is UI_FloatEdit e)) return;
            float minHalf = float.MaxValue;
            foreach (SpineNode n in nodes)
            {
                // A closed end collapses to a point and has no wall to inset, so it constrains nothing.
                float half = Mathf.Min(Erh(n), Erv(n));
                if (half > 0f) minHalf = Mathf.Min(minHalf, half);
            }
            if (minHalf == float.MaxValue || minHalf <= 0f) minHalf = 0.5f;
            float max = Mathf.Max(2f * MinShellThickness, ShellInsetFraction * minHalf);
            e.minValue = MinShellThickness;
            e.maxValue = max;
            e.incrementLarge = max * 0.25f;
            e.incrementSmall = max * 0.05f;
            shellThickness = Mathf.Clamp(shellThickness, MinShellThickness, max);
        }

        // At each end of the spine only the inward insert makes sense: the top node (highest position /
        // last index) hides "in front", the bottom node (index 0) hides "behind". The end nodes are also
        // pinned, so they can't be removed (Remove hidden there too).
        private void UpdateAddButtons()
        {
            bool isEnd = IsEndNode(SelIndex);
            BaseEvent front = Events[nameof(AddNodeInFrontEvent)];
            BaseEvent behind = Events[nameof(AddNodeBehindEvent)];
            BaseEvent remove = Events[nameof(RemoveNodeEvent)];
            if (front != null) front.guiActiveEditor = SelIndex != nodes.Count - 1;
            if (behind != null) behind.guiActiveEditor = SelIndex != 0;
            if (remove != null) remove.guiActiveEditor = !isEnd;
        }

        private void OnSelectedNodeChanged(BaseField f, object obj)
        {
            // Ignore re-fires triggered by our own rebuild, and no-op re-fires from PAW construction.
            if (_rebuilding) return;
            if (Equals(f.GetValue(this), obj)) return;
            LoadProxyFromNode();
            MonoUtilities.RefreshPartContextWindow(part);
        }

        // A single proxy field changed -> write just that field back to the selected node (the other
        // proxies stay authoritative, so they can't be clobbered if load/change ever drift out of sync).
        private void OnNodeFieldChanged(BaseField f, object obj)
        {
            // Ignore re-fires triggered by our own rebuild (the Volume setter can synchronously
            // rebuild the PAW), and the no-op re-fires PAW construction issues.
            if (_rebuilding) return;
            if (Equals(f.GetValue(this), obj)) return;
            if (nodes.Count == 0) return;
            SpineNode n = nodes[SelIndex];
            Debug.Log($"{ModTag} OnNodeFieldChanged {f.name}: {obj} -> {f.GetValue(this)} | sel={SelIndex}/{nodes.Count} before: {Dump(n)}");
            // Snapshot attachments against the pre-edit surface (before the switch mutates the node).
            List<SurfSlice> oldSurf = SampleSurface();
            List<AttachSnap> attachSnaps = SnapshotAttachments();
            try
            {
                switch (f.name)
                {
                    case nameof(nodeSlopeOpt): n.slopeAbove = ParseEnum(nodeSlopeOpt, SpineSlope.Linear); break;
                    case nameof(nodeWidth): n.sizeH = nodeWidth = Mathf.Clamp(nodeWidth, MinSizeFor(SelIndex), MaxSize); break;
                    case nameof(nodeHeight): n.sizeV = nodeHeight = Mathf.Clamp(nodeHeight, MinSizeFor(SelIndex), MaxSize); break;
                    case nameof(nodeTiltV): n.tiltV = nodeTiltV = Mathf.Clamp(nodeTiltV, -60f, 60f); break;
                    case nameof(nodeTiltH): n.tiltH = nodeTiltH = Mathf.Clamp(nodeTiltH, -60f, 60f); break;
                    case nameof(nodeOffsetV): n.offsetV = nodeOffsetV = Mathf.Clamp(nodeOffsetV, -10f, 10f); break;
                    case nameof(nodeOffsetH): n.offsetH = nodeOffsetH = Mathf.Clamp(nodeOffsetH, -10f, 10f); break;
                    case nameof(nodeFilletTop): n.filletTop = nodeFilletTop = Mathf.Clamp01(nodeFilletTop); break;
                    case nameof(nodeFilletBottom): n.filletBottom = nodeFilletBottom = Mathf.Clamp01(nodeFilletBottom); break;
                    case nameof(nodeStretch): n.stretch = nodeStretch = Mathf.Clamp01(nodeStretch); break;
                    case nameof(nodeProfileOpt):
                        n.profile = ParseEnum(nodeProfileOpt, SpineProfile.Ellipse);
                        UpdateProfileFieldVisibility(n.profile);
                        MonoUtilities.RefreshPartContextWindow(part);
                        break;
                    case nameof(nodePos):
                        // Position is clamped strictly between the neighbours; end nodes are pinned.
                        if (!IsEndNode(SelIndex))
                        {
                            float loPos = (SelIndex > 0) ? nodes[SelIndex - 1].position + 0.01f : 0f;
                            float hiPos = (SelIndex < nodes.Count - 1) ? nodes[SelIndex + 1].position - 0.01f : 1f;
                            if (loPos > hiPos) loPos = hiPos = 0.5f * (loPos + hiPos);
                            n.position = Mathf.Clamp(nodePos, loPos, hiPos);
                        }
                        nodePos = n.position;   // reflect the clamp / pin in the slider
                        break;
                }

                // A position edit can reorder; keep the selection on the same node.
                SortNodes();
                selectedNode = Mathf.Clamp(nodes.IndexOf(n), 0, nodes.Count - 1);
                RebuildAndPropagate();
                ReplaceAttachments(oldSurf, attachSnaps);   // keep attached parts on the new surface
                Debug.Log($"{ModTag}  applied -> sel={SelIndex} after: {Dump(n)}");
            }
            catch (Exception e)
            {
                Debug.LogError($"{ModTag} OnNodeFieldChanged({f.name}) FAILED: {e}\n  nodes: {DumpAll()}");
            }
        }

        private static string Dump(SpineNode n) => $"[pos={n.position:F3} H={n.sizeH:F3} V={n.sizeV:F3} off={n.offsetV:F3}/{n.offsetH:F3} tilt={n.tiltV:F1}/{n.tiltH:F1} fil={n.filletTop:F2}/{n.filletBottom:F2} str={n.stretch:F2} prof={n.profile} slope={n.slopeAbove}]";
        private string DumpAll() => string.Join(" ", nodes.ConvertAll(Dump).ToArray()) + $" | len={length:F3} hS={hScale:F3} vS={vScale:F3}";

        private static T ParseEnum<T>(string s, T fallback) where T : struct =>
            Enum.TryParse(s, out T v) ? v : fallback;

        private void RebuildAndPropagate()
        {
            UpdateShape();          // sets Volume -> fires OnPartVolumeChanged + onEditorShipModified
            UpdateInterops();       // FAR / TestFlight
            SyncToSymmetry();
        }

        // Lightweight rebuild for continuous gizmo drags: refresh this part's mesh only, deferring the
        // expensive FAR/TestFlight interop and the per-counterpart symmetry rebuild to drag end (a full
        // RebuildAndPropagate fires once on mouse-up), so a drag doesn't re-voxelise and re-mesh the whole
        // symmetry group every frame.
        private void RebuildLive() => UpdateShape();

        // The node list isn't a scalar KSPField, so symmetry isn't auto-mirrored. Copy our state to
        // each counterpart's spine module and rebuild it.
        private void SyncToSymmetry()
        {
            foreach (Part p in part.symmetryCounterparts)
            {
                if (FindAbstractShapeModule(p, this) is ProceduralShapeSpine pm)
                {
                    pm.length = length;
                    pm.hScale = hScale;
                    pm.vScale = vScale;
                    pm.airfoil = airfoil;
                    pm.shellMode = shellMode;
                    pm.shellThickness = shellThickness;
                    pm.selectedNode = selectedNode;
                    pm.nodes.Clear();
                    foreach (SpineNode n in nodes) pm.nodes.Add(n.Clone());
                    pm.UpdateShape();
                    pm.UpdateInterops();
                    pm.LoadProxyFromNode();
                }
            }
        }

        #endregion

        #region Editor gizmo (interactive)

        // Per node, billboarded glyphs drawn through the hull:
        //   * a circle marker at the node centre          -> select; drag = move the node up/down
        //                                                    (vertical offset); wheel = taller/shorter
        //   * the section-type glyph just above            -> select; middle-click cycles Ellipse ->
        //                                                    Rectangle -> Mk2 -> Mk3
        //   * two tip handles at the section's left/right  -> B9-style: drag out/in = widen/narrow,
        //     edges                                          drag along the spine = move forward/back
        // Update() only runs while this shape is the active (enabled) one.
        private enum HoverKind { None, Node, Profile, TipLeft, TipRight }
        private readonly List<GameObject> _nodeHandles = new List<GameObject>();   // circle marker
        private readonly List<GameObject> _profIcons = new List<GameObject>();     // section glyph (above)
        private readonly List<GameObject> _leftTips = new List<GameObject>();      // left mesh-edge handle
        private readonly List<GameObject> _rightTips = new List<GameObject>();     // right mesh-edge handle
        private int _dragIndex = -1;     // node being centre-offset dragged
        private int _dragAxis;           // 0 = vertical (left-drag), 1 = horizontal (right-drag)
        private int _tipDragIndex = -1;  // node whose tip is being dragged
        private int _tipSide;            // -1 = left tip, +1 = right tip
        // Offset between the grabbed value and the cursor at mouse-down, so a drag moves the value by the
        // cursor delta instead of snapping it to wherever the (possibly off-centre) grab landed.
        private float _grabDeltaV, _grabDeltaH, _grabDeltaW, _grabDeltaP;
        private bool _inputLocked;
        private const ControlTypes GizmoLockMask = ControlTypes.EDITOR_PAD_PICK_PLACE | ControlTypes.CAMERACONTROLS;
        private string GizmoLockId => "SpineGizmo_" + GetInstanceID();

        public void Update()
        {
            // Only the active Spine shape on a selected part drives gizmos. A disabled shape module stops
            // getting Update() (so it would leave its handles orphaned); guarding here also covers a stale
            // active shape and hides the gizmos once the part's PAW closes (part deselected).
            bool selected = part.PartActionWindow != null && part.PartActionWindow.isActiveAndEnabled;
            if (!HighLogic.LoadedSceneIsEditor || nodes.Count == 0 || PPart == null || PPart.CurrentShape != this || !selected)
            {
                CleanupGizmo();
                return;
            }

            SyncHandleCount(_nodeHandles, "SpineNodeHandle");
            SyncHandleCount(_profIcons, "SpineProfileIcon");
            SyncHandleCount(_leftTips, "SpineTipL");
            SyncHandleCount(_rightTips, "SpineTipR");

            Camera cam = GizmoCam;
            int sel = SelIndex;
            HoverKind hoverKind = HoverKind.None;
            int hover = -1;
            if (_dragIndex >= 0) { hover = _dragIndex; hoverKind = HoverKind.Node; }
            else if (_tipDragIndex >= 0) { hover = _tipDragIndex; hoverKind = (_tipSide < 0) ? HoverKind.TipLeft : HoverKind.TipRight; }
            else if (cam != null) hover = ComputeHover(cam, out hoverKind);

            Quaternion rot = (cam != null) ? cam.transform.rotation : Quaternion.identity;
            Vector3 up = (cam != null) ? cam.transform.up : Vector3.up;
            SpineHandleVis vis = ParseEnum(handleVisOpt, SpineHandleVis.Active);

            for (int i = 0; i < nodes.Count; i++)
            {
                bool isSel = (i == sel);
                // All = every node's edge tips, Active = the selected node's only, None = no tips and no
                // centre marker either (it swamps a small section), leaving just the section glyph above
                // the node -- which still selects the node on click and cycles the profile on middle-click.
                bool showTips = (vis == SpineHandleVis.All) || (vis == SpineHandleVis.Active && isSel);
                bool showNode = vis != SpineHandleVis.None;

                float y = (nodes[i].position - 0.5f) * length;
                float rH = Erh(nodes[i]), off = Eoff(nodes[i]), offX = EoffH(nodes[i]);
                Vector3 baseW = part.transform.TransformPoint(new Vector3(offX, y, off));   // node centre (incl. offsets)

                bool nodeHot = (i == hover && hoverKind == HoverKind.Node);
                float s = (isSel || nodeHot) ? 0.30f : 0.22f;
                Material nodeMat = nodeHot ? HandleMat(2) : (isSel ? HandleMat(0) : HandleMat(1));
                // End nodes are drawn as arrows pointing fore/aft along the spine (they can't move
                // forward/back); interior nodes are circles.
                bool isLast = (i == nodes.Count - 1);
                Mesh nodeMesh = IsEndNode(i) ? (_meshTriangle ??= BuildTriangle()) : (_meshCircle ??= BuildCircle());
                Quaternion nodeRot = IsEndNode(i) ? SpineBillboard(cam, rot, isLast) : rot;
                if (_nodeHandles[i] != null && _nodeHandles[i].activeSelf != showNode) _nodeHandles[i].SetActive(showNode);
                if (showNode) PlaceIcon(_nodeHandles[i], baseW, nodeRot, s, nodeMesh, nodeMat, true, cam);

                bool profHot = (i == hover && hoverKind == HoverKind.Profile);
                PlaceIcon(_profIcons[i], baseW + up * 0.32f, rot, profHot ? 0.22f : 0.16f,
                          ProfileIconMesh(nodes[i].profile), profHot ? HandleMat(2) : ProfileMat, true, cam);

                Vector3 lW = part.transform.TransformPoint(new Vector3(-rH + offX, y, off));
                Vector3 rW = part.transform.TransformPoint(new Vector3(rH + offX, y, off));
                bool lHot = (i == hover && hoverKind == HoverKind.TipLeft);
                bool rHot = (i == hover && hoverKind == HoverKind.TipRight);
                PlaceTip(_leftTips[i], lW, rot, showTips, lHot, cam);
                PlaceTip(_rightTips[i], rW, rot, showTips, rHot, cam);
            }

            if (cam != null) HandleGizmoInput(cam, hoverKind, hover);
            else SetInputLock(false);
        }

        private void SyncHandleCount(List<GameObject> list, string name)
        {
            while (list.Count < nodes.Count) list.Add(CreateHandle(name));
            while (list.Count > nodes.Count)
            {
                GameObject extra = list[list.Count - 1];
                list.RemoveAt(list.Count - 1);
                if (extra != null) Destroy(extra);
            }
        }

        private void PlaceIcon(GameObject go, Vector3 worldPos, Quaternion rot, float scale, Mesh mesh, Material mat, bool outline, Camera cam)
        {
            if (go == null) return;
            go.transform.SetParent(null, false);
            go.transform.position = worldPos;
            go.transform.rotation = rot;                       // billboard
            go.transform.localScale = Vector3.one * scale;
            go.layer = part.gameObject.layer;
            if (go.GetComponent<MeshFilter>() is MeshFilter mf) mf.sharedMesh = mesh;
            if (go.GetComponent<Renderer>() is Renderer r) r.sharedMaterial = mat;
            if (outline) PlaceOutline(go, mesh, scale, cam);
        }

        // A black copy of the icon, one outline-pixel larger, drawn first (lower render queue) so it
        // shows as a thin outline behind the coloured icon. The child scale is solved each frame so the
        // ring is a constant ~1 px wide regardless of zoom. Parented to the icon so it billboards with it.
        private const float OutlinePixels = 1f;
        private void PlaceOutline(GameObject go, Mesh mesh, float parentScale, Camera cam)
        {
            Transform ot = go.transform.Find("outline");
            GameObject o = (ot != null) ? ot.gameObject : new GameObject("outline", typeof(MeshFilter), typeof(MeshRenderer));
            o.transform.SetParent(go.transform, false);
            o.transform.localPosition = Vector3.zero;
            o.transform.localRotation = Quaternion.identity;
            o.layer = go.layer;

            float child = 1.1f;
            if (cam != null)
            {
                Vector3 c = go.transform.position;
                float pxPerWorld = Vector2.Distance(cam.WorldToScreenPoint(c), cam.WorldToScreenPoint(c + cam.transform.right));
                float coloredR = 0.5f * parentScale;          // world radius of the coloured disc
                if (pxPerWorld > 1e-4f && coloredR > 1e-5f)
                    child = (coloredR + OutlinePixels / pxPerWorld) / coloredR;
            }
            o.transform.localScale = Vector3.one * child;
            o.GetComponent<MeshFilter>().sharedMesh = mesh;
            o.GetComponent<Renderer>().sharedMaterial = OutlineMat;
        }

        // A tip handle (diamond) at a section edge; shown only for the selected node.
        private void PlaceTip(GameObject go, Vector3 worldPos, Quaternion rot, bool show, bool hot, Camera cam)
        {
            if (go == null) return;
            if (go.activeSelf != show) go.SetActive(show);
            if (!show) return;
            PlaceIcon(go, worldPos, rot, hot ? 0.26f : 0.20f, _meshDiamond ??= BuildDiamond(),
                      hot ? HandleMat(2) : TipMat, true, cam);
        }

        // Cursor-projected width and spine position from a tip-drag ray, in the section's width/length
        // plane. Returns false if the ray misses the plane.
        private bool RawTip(int i, Ray ray, out float rawW, out float rawP)
        {
            SpineNode n = nodes[i];
            rawW = n.sizeH; rawP = n.position;
            Vector3 center = part.transform.TransformPoint(new Vector3(0f, NodeY(n), Eoff(n)));
            Plane plane = new Plane(part.transform.forward, center);   // local-Z normal -> the X/Y plane
            if (!plane.Raycast(ray, out float enter)) return false;
            Vector3 local = part.transform.InverseTransformPoint(ray.GetPoint(enter));
            rawW = 2f * Mathf.Abs(local.x - EoffH(n)) / Mathf.Max(0.01f, hScale);   // width about the (possibly offset) centre
            rawP = local.y / Mathf.Max(0.01f, length) + 0.5f;
            return true;
        }

        // Drag a node's edge tip in the section's width/length plane: |x| sets the width, the spine (y)
        // coordinate repositions the node (clamped between neighbours). Either move alone or together.
        private void DragTip(int i, int side, Ray ray)
        {
            SpineNode n = nodes[i];
            if (!RawTip(i, ray, out float rawW, out float rawP)) return;

            float newH = Mathf.Clamp(rawW + _grabDeltaW, MinSizeFor(i), MaxSize);
            // End nodes are pinned fore/aft -> width only.
            float pos = n.position;
            if (!IsEndNode(i))
            {
                pos = rawP + _grabDeltaP;
                float lo = (i > 0) ? nodes[i - 1].position + 0.01f : 0f;
                float hi = (i < nodes.Count - 1) ? nodes[i + 1].position - 0.01f : 1f;
                if (lo > hi) lo = hi = 0.5f * (lo + hi);
                pos = Mathf.Clamp(pos, lo, hi);
            }

            if (Mathf.Approximately(newH, n.sizeH) && Mathf.Approximately(pos, n.position)) return;   // no change
            n.sizeH = newH;
            n.position = pos;
            if (i == SelIndex) { nodeWidth = n.sizeH; nodePos = pos; }   // live slider update
            RebuildLive();   // full sync deferred to drag end
        }

        private void HandleGizmoInput(Camera cam, HoverKind hoverKind, int hover)
        {
            if (_dragIndex >= 0)
            {
                int btn = (_dragAxis == 1) ? 1 : 0;   // left-drag = vertical, right-drag = horizontal
                if (Input.GetMouseButton(btn))
                {
                    SetInputLock(true);
                    Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                    if (_dragAxis == 1) DragNodeHorizontal(_dragIndex, ray);
                    else DragNodeVertical(_dragIndex, ray);
                }
                else
                {
                    _dragIndex = -1;
                    RebuildAndPropagate();   // one full interop + symmetry sync on release
                    ReplaceAttachments(_dragSurf0, _dragAttach0);   // settle attached parts onto the new surface
                    _dragSurf0 = null; _dragAttach0 = null;
                    if (_dragAxis == 1) nodeOffsetH = nodes[SelIndex].offsetH;
                    else nodeOffsetV = nodes[SelIndex].offsetV;
                    _dragAxis = 0;
                    MonoUtilities.RefreshPartContextWindow(part);
                }
                return;
            }

            if (_tipDragIndex >= 0)
            {
                if (Input.GetMouseButton(0))
                {
                    SetInputLock(true);
                    DragTip(_tipDragIndex, _tipSide, cam.ScreenPointToRay(Input.mousePosition));
                }
                else
                {
                    _tipDragIndex = -1;
                    RebuildAndPropagate();   // one full interop + symmetry sync on release
                    ReplaceAttachments(_dragSurf0, _dragAttach0);   // settle attached parts onto the new surface
                    _dragSurf0 = null; _dragAttach0 = null;
                    SpineNode sn = nodes[SelIndex];
                    nodeWidth = sn.sizeH;
                    nodePos = sn.position;
                    MonoUtilities.RefreshPartContextWindow(part);
                }
                return;
            }

            SetInputLock(hover >= 0);
            if (hover < 0) return;

            switch (hoverKind)
            {
                case HoverKind.Profile:
                    if (Input.GetMouseButtonDown(0)) SelectNode(hover);
                    else if (Input.GetMouseButtonDown(2)) CycleProfile(hover);
                    return;

                case HoverKind.TipLeft:
                case HoverKind.TipRight:
                    // Tip handle: drag out/in widens/narrows, drag along the spine repositions.
                    if (Input.GetMouseButtonDown(0))
                    {
                        SelectNode(hover);
                        _tipDragIndex = hover;
                        _tipSide = (hoverKind == HoverKind.TipLeft) ? -1 : 1;
                        _dragSurf0 = SampleSurface(); _dragAttach0 = SnapshotAttachments();
                        // Capture the grab offset so the drag moves by the cursor delta, not snapping the
                        // value to the (possibly off-tip) grab point.
                        if (RawTip(hover, cam.ScreenPointToRay(Input.mousePosition), out float rw, out float rp))
                        { _grabDeltaW = nodes[hover].sizeH - rw; _grabDeltaP = nodes[hover].position - rp; }
                        else { _grabDeltaW = _grabDeltaP = 0f; }
                    }
                    return;

                default:
                    // Node circle. With "must select before dragging" on, the first click only selects
                    // (so you can click a node without moving it) and only the already-selected node
                    // drags; with it off, any node drags immediately. Left-drag = vertical offset,
                    // right-drag = horizontal offset, wheel = height.
                    if (Input.GetMouseButtonDown(0))
                    {
                        bool canDrag = !requireSelectToDrag || hover == SelIndex;
                        SelectNode(hover);
                        if (canDrag)
                        {
                            _dragIndex = hover;
                            _dragAxis = 0;
                            _grabDeltaV = nodes[hover].offsetV - RawVertical(hover, cam.ScreenPointToRay(Input.mousePosition));
                            _dragSurf0 = SampleSurface(); _dragAttach0 = SnapshotAttachments();
                        }
                    }
                    else if (Input.GetMouseButtonDown(1))
                    {
                        bool canDrag = !requireSelectToDrag || hover == SelIndex;
                        SelectNode(hover);
                        if (canDrag)
                        {
                            _dragIndex = hover;
                            _dragAxis = 1;
                            _grabDeltaH = nodes[hover].offsetH - RawHorizontal(hover, cam.ScreenPointToRay(Input.mousePosition));
                            _dragSurf0 = SampleSurface(); _dragAttach0 = SnapshotAttachments();
                        }
                    }
                    else if (!Mathf.Approximately(Input.mouseScrollDelta.y, 0f)) ResizeNode(hover, Input.mouseScrollDelta.y);
                    return;
            }
        }

        // Nearest icon whose rendered disc the mouse is actually inside; the hit radius is the icon's
        // own on-screen size (incl. outline) so the hotzone matches what you see. The kind tells the
        // caller which control the mouse is over.
        private int ComputeHover(Camera cam, out HoverKind kind)
        {
            Vector2 mouse = Input.mousePosition;
            int hover = -1;
            kind = HoverKind.None;
            float best = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (i < _nodeHandles.Count && _nodeHandles[i] != null && _nodeHandles[i].activeInHierarchy)
                {
                    float d = ScreenDist(cam, _nodeHandles[i].transform.position, mouse);
                    float rad = IconScreenRadius(cam, _nodeHandles[i]);
                    if (d >= 0f && d <= rad && d < best) { best = d; hover = i; kind = HoverKind.Node; }
                }
                if (i < _profIcons.Count && _profIcons[i] != null && _profIcons[i].activeInHierarchy)
                {
                    float d = ScreenDist(cam, _profIcons[i].transform.position, mouse);
                    float rad = IconScreenRadius(cam, _profIcons[i]);
                    if (d >= 0f && d <= rad && d < best) { best = d; hover = i; kind = HoverKind.Profile; }
                }
                if (i < _leftTips.Count && _leftTips[i] != null && _leftTips[i].activeInHierarchy)
                {
                    float d = ScreenDist(cam, _leftTips[i].transform.position, mouse);
                    float rad = IconScreenRadius(cam, _leftTips[i]);
                    if (d >= 0f && d <= rad && d < best) { best = d; hover = i; kind = HoverKind.TipLeft; }
                }
                if (i < _rightTips.Count && _rightTips[i] != null && _rightTips[i].activeInHierarchy)
                {
                    float d = ScreenDist(cam, _rightTips[i].transform.position, mouse);
                    float rad = IconScreenRadius(cam, _rightTips[i]);
                    if (d >= 0f && d <= rad && d < best) { best = d; hover = i; kind = HoverKind.TipRight; }
                }
            }
            return hover;
        }

        private static float ScreenDist(Camera cam, Vector3 world, Vector2 mouse)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z < 0f) return -1f;   // behind camera
            return Vector2.Distance(new Vector2(sp.x, sp.y), mouse);
        }

        // On-screen pixel radius of an icon disc (mesh radius 0.5 * its outline scale 1.25), plus a few
        // pixels of slack so the very edge is still grabbable.
        private static float IconScreenRadius(Camera cam, GameObject go)
        {
            Vector3 c = go.transform.position;
            Vector3 e = c + cam.transform.right * (0.5f * go.transform.localScale.x);
            Vector3 sc = cam.WorldToScreenPoint(c);
            Vector3 se = cam.WorldToScreenPoint(e);
            return Vector2.Distance(new Vector2(sc.x, sc.y), new Vector2(se.x, se.y)) + 3f;
        }

        private void SelectNode(int i)
        {
            selectedNode = Mathf.Clamp(i, 0, nodes.Count - 1);
            LoadProxyFromNode();
            MonoUtilities.RefreshPartContextWindow(part);
        }

        private void CycleProfile(int i)
        {
            SpineNode n = nodes[i];
            n.profile = (SpineProfile)(((int)n.profile + 1) % 4);   // Ellipse -> Rectangle -> Mk2 -> Mk3
            RebuildAndPropagate();                                  // section type changes geometry
            if (i == SelIndex) { LoadProxyFromNode(); MonoUtilities.RefreshPartContextWindow(part); }
        }

        // Mouse wheel on a node makes it taller / shorter (its vertical size).
        private void ResizeNode(int i, float scroll)
        {
            SpineNode n = nodes[i];
            List<SurfSlice> oldSurf = SampleSurface();
            List<AttachSnap> snaps = SnapshotAttachments();
            float step = PPart.diameterSmallStep * Mathf.Sign(scroll);
            n.sizeV = Mathf.Clamp(n.sizeV + step, MinSizeFor(i), MaxSize);
            RebuildAndPropagate();
            ReplaceAttachments(oldSurf, snaps);
            if (i == SelIndex) { LoadProxyFromNode(); MonoUtilities.RefreshPartContextWindow(part); }
        }

        // Cursor-projected vertical offset (m, pre-vScale) from a drag ray along the part's local Z axis.
        private float RawVertical(int i, Ray ray)
        {
            Vector3 axisPoint = part.transform.TransformPoint(new Vector3(0f, NodeY(nodes[i]), 0f));   // un-offset centre
            float zWorld = ClosestParamOnAxis(axisPoint, part.transform.forward, ray);                 // metres along local Z
            return (vScale > 0f) ? zWorld / vScale : zWorld;
        }

        // Dragging a node moves its cross-section up/down (vertical centre offset), tracking the mouse
        // along the part's local vertical (Z) axis.
        private void DragNodeVertical(int i, Ray ray)
        {
            SpineNode n = nodes[i];
            float newOffset = Mathf.Clamp(RawVertical(i, ray) + _grabDeltaV, -10f, 10f);
            if (Mathf.Approximately(newOffset, n.offsetV)) return;   // no change -> skip rebuild
            n.offsetV = newOffset;
            if (i == SelIndex) nodeOffsetV = newOffset;   // PAW value updates; refresh on drag end
            RebuildLive();   // full sync deferred to drag end
        }

        // Cursor-projected horizontal offset (m, pre-hScale) from a drag ray along the part's local X axis.
        private float RawHorizontal(int i, Ray ray)
        {
            Vector3 axisPoint = part.transform.TransformPoint(new Vector3(0f, NodeY(nodes[i]), 0f));   // un-offset centre
            float xWorld = ClosestParamOnAxis(axisPoint, part.transform.right, ray);                   // metres along local X
            return (hScale > 0f) ? xWorld / hScale : xWorld;
        }

        // Right-dragging a node moves its cross-section left/right (horizontal centre offset), tracking
        // the mouse along the part's local horizontal (X) axis.
        private void DragNodeHorizontal(int i, Ray ray)
        {
            SpineNode n = nodes[i];
            float newOffset = Mathf.Clamp(RawHorizontal(i, ray) + _grabDeltaH, -10f, 10f);
            if (Mathf.Approximately(newOffset, n.offsetH)) return;   // no change -> skip rebuild
            n.offsetH = newOffset;
            if (i == SelIndex) nodeOffsetH = newOffset;   // PAW value updates; refresh on drag end
            RebuildLive();   // full sync deferred to drag end
        }

        // Parameter (signed metres along axisDir from axisPoint) of the point on the axis line closest
        // to the mouse ray.
        private static float ClosestParamOnAxis(Vector3 axisPoint, Vector3 axisDir, Ray ray)
        {
            Vector3 d1 = axisDir.normalized;
            Vector3 d2 = ray.direction.normalized;
            Vector3 r = axisPoint - ray.origin;
            float b = Vector3.Dot(d1, d2);
            float denom = 1f - b * b;            // a = d1.d1 = 1, e = d2.d2 = 1
            if (denom < 1e-6f) return Vector3.Dot(d1, r);   // parallel
            float c = Vector3.Dot(d1, r);
            float f = Vector3.Dot(d2, r);
            return (b * f - c) / denom;
        }

        private void SetInputLock(bool on)
        {
            if (on && !_inputLocked) { InputLockManager.SetControlLock(GizmoLockMask, GizmoLockId); _inputLocked = true; }
            else if (!on && _inputLocked) { InputLockManager.RemoveControlLock(GizmoLockId); _inputLocked = false; }
        }

        private static Camera GizmoCam =>
            (EditorCamera.Instance != null && EditorCamera.Instance.cam != null) ? EditorCamera.Instance.cam : Camera.main;

        private GameObject CreateHandle(string name) => new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));

        // Flat billboarded icon meshes (white vertex colours, tinted by the handle material).
        private static Mesh _meshCircle;

        // Section-type glyph drawn above the node circle: the cross-section's own silhouette so it
        // reads at a glance (ellipse squished, Mk3 round, rectangle square, Mk2 lifting-body).
        private static Mesh _iconEllipse, _iconRect, _iconMk2, _iconMk3;
        private static Mesh ProfileIconMesh(SpineProfile p)
        {
            switch (p)
            {
                case SpineProfile.Rectangle: return _iconRect ??= FanMesh(UnitRect, "Rect", new Vector2(1f, 0.6f));
                case SpineProfile.Mk2: return _iconMk2 ??= FanMesh(UnitMk2, "Mk2", new Vector2(1f, 0.6f));
                case SpineProfile.Mk3: return _iconMk3 ??= FanMesh(BuildMk3Outline(0.43f, 0.5f), "Mk3", Vector2.one);
                default: return _iconEllipse ??= FanMesh(UnitCircle, "Ellipse", new Vector2(1f, 0.62f));
            }
        }

        private static Mesh BuildCircle() => FanMesh(UnitCircle, "Circle", Vector2.one);

        private static Mesh _meshDiamond;
        private static Mesh BuildDiamond() => FanMesh(new[] { new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-1f, 0f), new Vector2(0f, -1f) }, "Diamond", Vector2.one);

        // Triangle pointing +Y (oriented along the spine by SpineBillboard for the end nodes).
        private static Mesh _meshTriangle;
        private static Mesh BuildTriangle()
        {
            var v = new[] { new Vector3(0f, 0.62f, 0f), new Vector3(-0.54f, -0.42f, 0f), new Vector3(0.54f, -0.42f, 0f) };
            var col = new[] { Color.white, Color.white, Color.white };
            var tris = new[] { 0, 1, 2, 0, 2, 1 };   // double-sided
            return new Mesh { name = "SpineTriangle", vertices = v, colors = col, triangles = tris };
        }

        // Billboard that keeps the glyph facing the camera but points its +Y along the spine (fore/aft).
        private Quaternion SpineBillboard(Camera cam, Quaternion fallback, bool front)
        {
            if (cam == null) return fallback;
            Vector3 spine = part.transform.up;                  // world dir toward the top node
            Vector3 dir = front ? spine : -spine;
            Vector3 fwd = cam.transform.forward;
            Vector3 proj = dir - Vector3.Dot(dir, fwd) * fwd;   // onto the camera plane
            if (proj.sqrMagnitude < 1e-6f) return fallback;     // looking straight down the axis
            return Quaternion.LookRotation(fwd, proj.normalized);
        }

        // Filled convex polygon (triangle fan from the centre) of a unit outline, fit into the icon box.
        private static Mesh FanMesh(Vector2[] outline, string name, Vector2 scale)
        {
            int n = outline.Length;
            var v = new Vector3[n + 1];
            var col = new Color[n + 1];
            v[0] = Vector3.zero; col[0] = Color.white;
            for (int i = 0; i < n; i++)
            {
                v[i + 1] = new Vector3(outline[i].x * scale.x * 0.5f, outline[i].y * scale.y * 0.5f, 0f);
                col[i + 1] = Color.white;
            }
            var tris = new int[n * 3];
            for (int i = 0; i < n; i++) { tris[i * 3] = 0; tris[i * 3 + 1] = 1 + i; tris[i * 3 + 2] = 1 + (i + 1) % n; }
            return new Mesh { name = "SpineProf" + name, vertices = v, colors = col, triangles = tris };
        }

        private static Material _matSel, _matNorm, _matHover, _matProf;
        // 0 = selected (yellow), 1 = normal (cyan), 2 = hovered (white).
        private static Material HandleMat(int kind)
        {
            switch (kind)
            {
                case 0: return _matSel ??= MakeMat(Color.yellow);
                case 2: return _matHover ??= MakeMat(Color.white);
                default: return _matNorm ??= MakeMat(new Color(0.2f, 0.8f, 1f));
            }
        }

        // Section glyph idle tint (green), so it reads as a distinct control from the node circle.
        private static Material ProfileMat => _matProf ??= MakeMat(new Color(0.5f, 1f, 0.55f));

        // Edge tip handle idle tint (orange).
        private static Material _matTip;
        private static Material TipMat => _matTip ??= MakeMat(new Color(1f, 0.6f, 0.15f));

        // Black outline drawn one queue earlier (behind) the coloured icons so they stand out.
        private static Material _matOutline;
        private static Material OutlineMat
        {
            get { if (_matOutline == null) { _matOutline = MakeMat(Color.black); _matOutline.renderQueue = 4999; } return _matOutline; }
        }

        // Unlit, solid, always-on-top material (renders through the mesh).
        private static Material MakeMat(Color col)
        {
            Material m = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            m.SetColor("_Color", col);
            m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            m.SetInt("_ZWrite", 0);
            m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            m.renderQueue = 5000;
            return m;
        }

        private void CleanupGizmo()
        {
            _dragIndex = -1;
            _tipDragIndex = -1;
            SetInputLock(false);
            DestroyAll(_nodeHandles);
            DestroyAll(_profIcons);
            DestroyAll(_leftTips);
            DestroyAll(_rightTips);
        }

        private void DestroyAll(List<GameObject> list)
        {
            foreach (GameObject h in list) if (h != null) Destroy(h);
            list.Clear();
        }

        public void OnDisable() => CleanupGizmo();
        public void OnDestroy() => CleanupGizmo();

        #endregion

        #region Meshes

        private static void WriteToAppropriateMesh(UncheckedMesh mesh, Mesh iconMesh, Mesh normalMesh)
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
                mesh.WriteTo(iconMesh);
            else
                mesh.WriteTo(normalMesh);
        }

        private void GenerateSideMesh(List<Ring> rings)
        {
            int rc = rings.Count;

            // Shell: the inner wall is the outline inset by the shell thickness (not coincident), so the
            // wall has real depth. Built once and reused for every ring's inner-skin vertex.
            List<Ring> inner = shellMode ? BuildInnerRings(rings) : null;

            // Hard-edge columns: outline indices where the section turns sharply (Rectangle/Mk2
            // corners). Detected globally (union over rings) so the loft topology stays a clean
            // grid; a corner that rounds out along a profile morph keeps its column but the two
            // duplicated normals converge, so the crease fades smoothly into the rounded section
            // instead of cutting across it.
            bool[] hard = ComputeHardColumns(rings);

            // Expand each ring into columns, duplicating every hard column so the two flats it
            // joins carry their own one-sided normals -> a crisp lighting break at the corner.
            var cols = new List<int>();    // original outline index of each emitted column
            var side = new List<int>();    // -1 = left copy, +1 = right copy, 0 = smooth/centred
            for (int j = 0; j < Sides; j++)
            {
                if (hard[j]) { cols.Add(j); side.Add(-1); cols.Add(j); side.Add(+1); }
                else { cols.Add(j); side.Add(0); }
            }
            int K = cols.Count;
            int per = K + 1;                 // +1 wrap column for a clean UV seam

            // A shell also gets an inner skin: the same vertices with flipped normals and reversed
            // winding, so the interior renders and lights correctly (useful for shrouds/shields).
            int vCount = shellMode ? 2 * rc * per : rc * per;
            int triCount = shellMode ? 2 * (rc - 1) * K * 2 : (rc - 1) * K * 2;
            int innerBase = rc * per;
            var mesh = new UncheckedMesh(vCount, triCount);

            float span = rings[rc - 1].y - rings[0].y;
            if (span <= 0f) span = 1f;

            for (int r = 0; r < rc; r++)
            {
                float vCoord = (rings[r].y - rings[0].y) / span;
                Ring below = rings[Mathf.Max(0, r - 1)];
                Ring above = rings[Mathf.Min(rc - 1, r + 1)];
                Ring ring = rings[r];
                float[] arcFrac = RingArcFractions(ring);   // cumulative perimeter fraction -> even texel density
                for (int k = 0; k <= K; k++)
                {
                    int c = k % K;
                    int j = cols[c];
                    int sd = side[c];
                    int idx = r * per + k;
                    Vector3 pos = RingV(ring, j);
                    mesh.vertices[idx] = pos;

                    Vector3 tSpine = RingV(above, j) - RingV(below, j);
                    // One-sided ring tangent on a hard column gives each flat its own face normal;
                    // the centred difference keeps curved sections smooth.
                    Vector3 tRing = (sd < 0) ? RingV(ring, j) - RingV(ring, j - 1)
                                  : (sd > 0) ? RingV(ring, j + 1) - RingV(ring, j)
                                             : RingV(ring, j + 1) - RingV(ring, j - 1);
                    Vector3 radial = new Vector3(pos.x - ring.offsetX, 0f, pos.z - ring.offsetZ);   // from the ring centre
                    // A zero-size end node collapses its ring to a point, leaving no ring tangent or radial
                    // to build a normal from. Borrow both from the neighbouring ring so the tip shades like
                    // the cone it closes instead of snapping to a fixed axis.
                    if (tRing.sqrMagnitude < 1e-12f || radial.sqrMagnitude < 1e-12f)
                    {
                        Ring nbr = (r == 0) ? above : below;
                        if (tRing.sqrMagnitude < 1e-12f)
                            tRing = (sd < 0) ? RingV(nbr, j) - RingV(nbr, j - 1)
                                  : (sd > 0) ? RingV(nbr, j + 1) - RingV(nbr, j)
                                             : RingV(nbr, j + 1) - RingV(nbr, j - 1);
                        if (radial.sqrMagnitude < 1e-12f)
                        {
                            Vector3 np = RingV(nbr, j);
                            radial = new Vector3(np.x - nbr.offsetX, 0f, np.z - nbr.offsetZ);
                        }
                    }
                    Vector3 normal = Vector3.Cross(tSpine, tRing).normalized;
                    if (Vector3.Dot(normal, radial) < 0f) normal = -normal;
                    if (normal == Vector3.zero) normal = (radial.sqrMagnitude > 0f) ? radial.normalized : Vector3.right;
                    mesh.normals[idx] = normal;
                    Vector3 tan = (tRing.sqrMagnitude > 0f) ? tRing.normalized : Vector3.right;
                    mesh.tangents[idx] = new Vector4(tan.x, tan.y, tan.z, 1f);
                    mesh.uv[idx] = new Vector2((k == K) ? 1f : arcFrac[j], vCoord);

                    if (shellMode)   // inner skin: inset inward by the wall thickness, normal flipped
                    {
                        int iidx = innerBase + idx;
                        mesh.vertices[iidx] = RingV(inner[r], j);
                        mesh.normals[iidx] = -normal;
                        mesh.tangents[iidx] = new Vector4(-tan.x, -tan.y, -tan.z, 1f);
                        mesh.uv[iidx] = mesh.uv[idx];
                    }
                }
            }

            int t = 0;
            for (int r = 0; r < rc - 1; r++)
            {
                for (int k = 0; k < K; k++)
                {
                    int a = r * per + k;
                    int b = r * per + k + 1;
                    int cc = (r + 1) * per + k;
                    int d = (r + 1) * per + k + 1;
                    mesh.triangles[t++] = a; mesh.triangles[t++] = cc; mesh.triangles[t++] = b;
                    mesh.triangles[t++] = b; mesh.triangles[t++] = cc; mesh.triangles[t++] = d;
                    if (shellMode)   // inner skin: reversed winding
                    {
                        int ia = innerBase + a, ib = innerBase + b, ic = innerBase + cc, id = innerBase + d;
                        mesh.triangles[t++] = ia; mesh.triangles[t++] = ib; mesh.triangles[t++] = ic;
                        mesh.triangles[t++] = ib; mesh.triangles[t++] = id; mesh.triangles[t++] = ic;
                    }
                }
            }

            float avgCirc = 0f;
            foreach (Ring r in rings) avgCirc += OutlinePerimeter(r.outline);
            avgCirc /= rc;
            RaiseChangeTextureScale("sides", PPart.legacyTextureHandler.SidesMaterial, new Vector2(avgCirc, length));
            WriteToAppropriateMesh(mesh, PPart.SidesIconMesh, SidesMesh);
        }

        // Cumulative perimeter fraction at each outline index (length Sides+1, [0]=0 .. [Sides]=1), so the
        // side texture U maps by arc length -> uniform texel density on flats and curves alike.
        private static float[] RingArcFractions(Ring ring)
        {
            Vector2[] o = ring.outline;
            int n = o.Length;
            var cum = new float[n + 1];
            for (int j = 0; j < n; j++) cum[j + 1] = cum[j] + (o[(j + 1) % n] - o[j]).magnitude;
            float total = (cum[n] > 1e-6f) ? cum[n] : 1f;
            for (int j = 0; j <= n; j++) cum[j] /= total;
            return cum;
        }

        // A column is "hard" if the outline turns sharply there in any ring (a Rectangle/Mk2
        // corner). Curved profiles (~15 deg/step at Sides=24) stay under the threshold and remain
        // smoothly shaded.
        private static bool[] ComputeHardColumns(List<Ring> rings)
        {
            const float thresholdDeg = 30f;
            var hard = new bool[Sides];
            foreach (Ring ring in rings)
            {
                for (int j = 0; j < Sides; j++)
                {
                    if (hard[j]) continue;
                    Vector3 a = RingV(ring, j) - RingV(ring, j - 1);
                    Vector3 b = RingV(ring, j + 1) - RingV(ring, j);
                    Vector2 a2 = new Vector2(a.x, a.z), b2 = new Vector2(b.x, b.z);
                    if (a2.sqrMagnitude < 1e-10f || b2.sqrMagnitude < 1e-10f) continue;
                    if (Vector2.Angle(a2, b2) > thresholdDeg) hard[j] = true;
                }
            }
            return hard;
        }

        private void GenerateCapMesh(List<Ring> rings)
        {
            if (shellMode)   // open ends: close the wall edge with a thin rim so it doesn't look paper-thin
            {
                var rim = new UncheckedMesh(4 * Sides, 8 * Sides);
                WriteRim(rim, rings[0], 0, 0, false);                              // bottom rim
                WriteRim(rim, rings[rings.Count - 1], 2 * Sides, 4 * Sides, true); // top rim
                WriteToAppropriateMesh(rim, PPart.EndsIconMesh, EndsMesh);
                return;
            }
            var mesh = new UncheckedMesh(Sides * 2, (Sides - 2) * 2);
            WriteCapVertices(mesh, rings[0], 0, false);
            WriteCapVertices(mesh, rings[rings.Count - 1], Sides, true);
            WriteCapTriangles(mesh, false, 0, 0);
            WriteCapTriangles(mesh, true, Sides, Sides - 2);
            WriteToAppropriateMesh(mesh, PPart.EndsIconMesh, EndsMesh);
        }

        // One open-end rim: a thin annulus from the outer outline to the inner (inset) outline, flat-shaded
        // along the spine axis so the wall reads solid at the mouth. vOff/triOff index into the shared mesh.
        // Emitted double-sided so it's visible from either side regardless of the winding convention.
        private void WriteRim(UncheckedMesh mesh, Ring outer, int vOff, int triOff, bool up)
        {
            Ring inner = InnerRing(outer);
            Vector3 nrm = new Vector3(0f, up ? 1f : -1f, 0f);
            for (int j = 0; j < Sides; j++)
            {
                int oi = vOff + j, ii = vOff + Sides + j;
                mesh.vertices[oi] = RingV(outer, j); mesh.normals[oi] = nrm; mesh.tangents[oi] = new Vector4(1f, 0f, 0f, 1f);
                mesh.uv[oi] = new Vector2((float)j / Sides, 1f);
                mesh.vertices[ii] = RingV(inner, j); mesh.normals[ii] = nrm; mesh.tangents[ii] = new Vector4(1f, 0f, 0f, 1f);
                mesh.uv[ii] = new Vector2((float)j / Sides, 0f);
            }
            int t = triOff * 3;
            for (int j = 0; j < Sides; j++)
            {
                int jn = (j + 1) % Sides;
                int o = vOff + j, on = vOff + jn, ic = vOff + Sides + j, inn = vOff + Sides + jn;
                mesh.triangles[t++] = o; mesh.triangles[t++] = on; mesh.triangles[t++] = inn;
                mesh.triangles[t++] = o; mesh.triangles[t++] = inn; mesh.triangles[t++] = ic;
                mesh.triangles[t++] = o; mesh.triangles[t++] = inn; mesh.triangles[t++] = on;
                mesh.triangles[t++] = o; mesh.triangles[t++] = ic; mesh.triangles[t++] = inn;
            }
        }

        private void WriteCapVertices(UncheckedMesh mesh, Ring ring, int offset, bool up)
        {
            float denom = Mathf.Max(0.01f, 2f * Mathf.Max(ring.rH, ring.rV));
            for (int j = 0; j < Sides; j++)
            {
                int idx = offset + j;
                Vector3 pos = RingV(ring, j);
                mesh.vertices[idx] = pos;
                mesh.normals[idx] = new Vector3(0f, up ? 1f : -1f, 0f);
                mesh.tangents[idx] = new Vector4(1f, 0f, 0f, 1f);
                mesh.uv[idx] = new Vector2(pos.x / denom + 0.5f, pos.z / denom + 0.5f);
            }
        }

        private void WriteCapTriangles(UncheckedMesh mesh, bool up, int vertexOffset, int triOffset)
        {
            int tio = triOffset * 3;
            for (int i = 0; i < Sides - 2; i++)
            {
                mesh.triangles[i * 3 + tio] = vertexOffset;
                mesh.triangles[i * 3 + 1 + tio] = (up ? i + 2 : i + 1) + vertexOffset;
                mesh.triangles[i * 3 + 2 + tio] = (up ? i + 1 : i + 2) + vertexOffset;
            }
        }

        private void GenerateColliderMesh(List<Ring> rings)
        {
            int rc = rings.Count;
            int per = ColliderSides;
            int sideTris = (rc - 1) * ColliderSides * 2;
            int capTris = shellMode ? 0 : (ColliderSides - 2) * 2;   // open ends in shell mode
            var mesh = new UncheckedMesh(rc * per, sideTris + capTris);

            int stride = Mathf.Max(1, Sides / ColliderSides);
            for (int r = 0; r < rc; r++)
            {
                Ring ring = rings[r];
                for (int j = 0; j < ColliderSides; j++)
                    mesh.vertices[r * per + j] = RingV(ring, j * stride);
            }

            int t = 0;
            for (int r = 0; r < rc - 1; r++)
            {
                for (int j = 0; j < ColliderSides; j++)
                {
                    int a = r * per + j;
                    int b = r * per + (j + 1) % ColliderSides;
                    int c = (r + 1) * per + j;
                    int d = (r + 1) * per + (j + 1) % ColliderSides;
                    mesh.triangles[t++] = a; mesh.triangles[t++] = c; mesh.triangles[t++] = b;
                    mesh.triangles[t++] = b; mesh.triangles[t++] = c; mesh.triangles[t++] = d;
                }
            }
            if (!shellMode)
            {
                int botOff = 0, topOff = (rc - 1) * per;
                for (int i = 0; i < ColliderSides - 2; i++)
                {
                    mesh.triangles[t++] = botOff; mesh.triangles[t++] = botOff + i + 1; mesh.triangles[t++] = botOff + i + 2;
                }
                for (int i = 0; i < ColliderSides - 2; i++)
                {
                    mesh.triangles[t++] = topOff; mesh.triangles[t++] = topOff + i + 2; mesh.triangles[t++] = topOff + i + 1;
                }
            }

            var colliderMesh = new Mesh();
            mesh.WriteTo(colliderMesh);
            PPart.UpdateOrReplaceSingleCollider(colliderMesh);
        }

        private void UpdateNodeSize(string nodeName, float diameter)
        {
            AttachNode node = part.attachNodes.Find(n => n.id == nodeName);
            if (node == null) return;
            node.size = Math.Min((int)(diameter / PPart.diameterLargeStep), 3);
            node.breakingTorque = node.breakingForce = Mathf.Max(50 * node.size * node.size, 50);
            RaiseChangeAttachNodeSize(node, diameter, Mathf.PI * diameter * diameter * 0.25f);
            RaiseChangeTextureScale(nodeName, PPart.legacyTextureHandler.EndsMaterial, new Vector2(diameter, diameter));
        }

        private static float OutlinePerimeter(Vector2[] o)
        {
            float p = 0f;
            for (int j = 0; j < o.Length; j++) p += (o[(j + 1) % o.Length] - o[j]).magnitude;
            return p;
        }

        #endregion
    }
}
