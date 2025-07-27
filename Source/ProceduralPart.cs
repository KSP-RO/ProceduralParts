using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;
using System.Reflection;
using UnityEngine.Profiling;
using ROUtils;

namespace ProceduralParts
{
    public class ProceduralPart : PartModule, IPartCostModifier, IPartMassModifier
    {
        public static readonly string ModTag = "[ProceduralParts]";
        public const string PAWGroupName = "ProcParts";
        public const string PAWGroupDisplayName = "ProceduralParts";
        private const float TranslateTolerance = 0.01f;

        internal LegacyTextureHandler legacyTextureHandler;
        internal TUTexturePickerGUI texturePickerGUI;

        #region Initialization

        [KSPField(guiActiveEditor = true, groupName = PAWGroupName, groupDisplayName = PAWGroupDisplayName, guiName = "Texture Selector GUI"),
         UI_Toggle(disabledText = "Show", enabledText = "Hide", scene = UI_Scene.Editor, affectSymCounterparts = UI_Scene.None)]
        public bool showTUPickerGUI = false;

        internal bool isInitialized = false;
        public static bool installedFAR = false;
        public static bool installedTU = false;
        public static bool staticallyInitialized = false;
        private static bool staticallyResetPartUpgrades = false;
        public bool TUEnabled => installedTU && part.GetComponent("KSPTextureSwitch") is Component;
        public float Volume => CurrentShape.Volume;
        public float Diameter => CurrentShape.MaxDiameter;
        public float Length => CurrentShape.Length;

        [KSPField] public float diameterMax = float.PositiveInfinity;
        [KSPField] public float lengthMax = float.PositiveInfinity;
        [KSPField] public float volumeMax = float.PositiveInfinity;
        [KSPField] public float diameterMin = 0.01f;
        [KSPField] public float lengthMin = 0.01f;
        [KSPField] public float volumeMin = 0;
        [KSPField] public float diameterLargeStep = 1.25f;
        [KSPField] public float diameterSmallStep = 0.125f;
        [KSPField] public float lengthLargeStep = 1.0f;
        [KSPField] public float lengthSmallStep = 0.125f;
        [KSPField] public bool allowCurveTweaking = true;

        [KSPField(isPersistant = true, guiActiveEditor = false, guiName = "Stringer Mass", groupName = PAWGroupName, groupDisplayName = PAWGroupDisplayName)]
        [UI_FloatRange(minValue = 0f, maxValue = 1.0f, stepIncrement = 0.01f)]
        public float density = -1;
        public const float NominalDensity = 0.15f;
        [KSPField] public float minDensity = 0f;
        [KSPField] public float maxDensity = 0f;
        [KSPField] public float stepDensity = 0.01f;

        public static void StaticInit()
        {
            // "All Part Upgrades Applied In Sandbox" required for this mod to be usable in sandbox
            if (!staticallyResetPartUpgrades && HighLogic.CurrentGame != null)
                staticallyResetPartUpgrades = HighLogic.CurrentGame.Parameters.CustomParams<GameParameters.AdvancedParams>().PartUpgradesInSandbox = true;

            if (staticallyInitialized) return;

            if (AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.assembly.GetName().Name == "TestFlight") is AssemblyLoader.LoadedAssembly tfAssembly)
                tfInterface = Type.GetType("TestFlightCore.TestFlightInterface, TestFlightCore", false);
            installedFAR = AssemblyLoader.loadedAssemblies.Any(a => a.assembly.GetName().Name == "FerramAerospaceResearch");
            installedTU = AssemblyLoader.loadedAssemblies.Any(a => a.assembly.GetName().Name == "TexturesUnlimited");
            TextureSet.LoadTextureSets(LegacyTextureHandler.textureSets);
            staticallyInitialized = true;
        }

        public override void OnAwake() => StaticInit();

        public override void OnLoad(ConfigNode node)
        {
            if (node.name == "CURRENTUPGRADE") return;
            // An existing vessel part or .craft file that has never set this value before, but not the availablePart
            if (HighLogic.LoadedScene != GameScenes.LOADING && !node.HasValue(nameof(forceLegacyTextures)))
                forceLegacyTextures = true;
        }

        public override string GetInfo()
        {
            if (!isInitialized)
            {
                InitializeObjects();
                InitializeShapes();
                if (shape is ProceduralAbstractShape)
                {
                    shape.UpdateShape();
                    if (HighLogic.LoadedScene == GameScenes.LOADING)
                        shape.FixEditorIconScale();
                }
                UpdateTexture();
                isInitialized = true;
            }

            // Need to rescale everything to make it look good in the icon, but reenable otherwise OnStart won't get called again.
            if (!isEnabled || !enabled)
            {
                Debug.LogError($"{ModTag} isEnabled {isEnabled} or enabled {enabled} were false in GetInfo()");
                isEnabled = enabled = true;
            }

            return base.GetInfo();
        }

        public override void OnStart(StartState state)
        {
            Profiler.BeginSample("PP-OnStart");
            InitializeObjects();
            InitializeShapes();

            if (HighLogic.LoadedSceneIsEditor)
            {
                Debug.Log($"{ModTag} TechLimits: {part.name} diameter=({diameterMin:G3}, {diameterMax:G3}) length=({lengthMin:G3}, {lengthMax:G3}) volume=({volumeMin:G3}, {volumeMax:G3})");
                legacyTextureHandler.ValidateSelectedTexture();
                BaseField bf = Fields[nameof(showTUPickerGUI)];
                bf.guiActiveEditor = installedTU && !forceLegacyTextures;
                if (bf.guiActiveEditor)
                {
                    bf.uiControlEditor.onFieldChanged = OnShowTUPickerGUIChanged;
                }
            }

            if (shape is ProceduralAbstractShape)
                shape.UpdateShape();
            if (part.variants is ModulePartVariants)
                part.variants.useMultipleDragCubes = false;

            isInitialized = true;

            if (HighLogic.LoadedSceneIsEditor)
            {
                if (!TUEnabled)
                    forceLegacyTextures = true;
                SetTextureFieldVisibility();

                Fields[nameof(forceLegacyTextures)].uiControlEditor.onFieldChanged = OnForceLegacyTextureChanged;

                BaseField field = Fields[nameof(textureSet)];
                UI_ChooseOption opt = field.uiControlEditor as UI_ChooseOption;
                opt.options = LegacyTextureHandler.textureSets.Keys.ToArray();
                opt.onSymmetryFieldChanged = opt.onFieldChanged = OnTextureChanged;

                BaseField capTextureField = Fields[nameof(capTextureIndex)];
                opt = capTextureField.uiControlEditor as UI_ChooseOption;
                opt.options = Enum.GetNames(typeof(LegacyTextureHandler.CapTextureMode));
                opt.onSymmetryFieldChanged = opt.onFieldChanged = OnTextureChanged;

                Fields[nameof(shapeName)].guiActiveEditor = availableShapes.Count > 1;
                opt = Fields[nameof(shapeName)].uiControlEditor as UI_ChooseOption;
                opt.options = availableShapes.Keys.ToArray();
                // The onSymmetryFieldChanged callbacks do not have the correct previous object assigned.
                // Since this matters, we need to handle it ourselves.
                opt.onFieldChanged = OnShapeSelectionChanged;

                Fields[nameof(costDisplay)].guiActiveEditor = displayCost;
                SetPFFields();

                GameEvents.onVariantApplied.Add(OnVariantApplied);
                GameEvents.onGameSceneSwitchRequested.Add(OnEditorExit);
            }

            // Done here and done in OnStartFinished in TankContentSwitcher to get ordering right
            // since these get fired in reverse-add order
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorShipModified.Add(OnShipModified);
            Profiler.EndSample();
        }

        public override void OnStartFinished(StartState state)
        {
            shape.InitializeAttachmentNodes();
            UpdateTexture();
            FixStackAttachments();
        }

        public void OnDestroy()
        {
            GameEvents.onVariantApplied.Remove(OnVariantApplied);
            GameEvents.onGameSceneSwitchRequested.Remove(OnEditorExit);
            GameEvents.onEditorShipModified.Remove(OnShipModified);
        }

        private void OnShowTUPickerGUIChanged(BaseField f, object obj)
        {
            if (showTUPickerGUI && texturePickerGUI == null)
            {
                texturePickerGUI = gameObject.AddComponent<TUTexturePickerGUI>();
                texturePickerGUI.ShowForPart(this);
            }
            else if (!showTUPickerGUI && texturePickerGUI != null)
            {
                Destroy(texturePickerGUI);
                texturePickerGUI = null;
            }
        }

        private void OnEditorExit(GameEvents.FromToAction<GameScenes, GameScenes> _) => showTUPickerGUI = false;
        private void OnForceLegacyTextureChanged(BaseField f, object obj) { SetTextureFieldVisibility(); UpdateTexture(); }
        public void OnTextureChanged(BaseField f, object obj) => UpdateTexture();

        // onSymmetryFieldChanged() callback has the incorrect value for parameter obj
        // So we manually invoke things for our symmetry counterparts.
        public void OnShapeSelectionChanged(BaseField f, object obj)
        {
//            Debug.Log($"{ModTag} OnShapeSelectionChanged for {this} from {obj} to {f.GetValue(this)}");
            ChangeShape(fromShape: availableShapes[obj as string]);
            foreach (Part p in part.symmetryCounterparts)
            {
                ProceduralPart pPart = p.FindModuleImplementing<ProceduralPart>();
                pPart.ChangeShape(fromShape: pPart.availableShapes[obj as string]);
            }
        }

        public void OnVariantApplied(Part p, PartVariant pv)
        {
            if (p is Part && this.part == p)
            {
                // Applying a PartVariant moves the attachment nodes around.  Reinitialize them.
                Debug.Log($"{ModTag} OnVariantApplied(Part {p}, PartVariant {pv?.Name}) from {this}/{part}");
                shape.InitializeAttachmentNodes();
                FixStackAttachments();
            }
        }

        private void FixStackAttachments(bool translateParts = false)
        {
            foreach (AttachNode node in this.part.attachNodes)
            {
                if (node.attachedPart is Part p)
                {
                    Vector3 selfW = part.transform.TransformPoint(Vector3.zero);
                    Vector3 peerW = p.transform.TransformPoint(Vector3.zero);
                    Part root = HighLogic.LoadedSceneIsFlight ? vessel.rootPart : EditorLogic.fetch.ship.Parts.First();
                    if (node.FindOpposingNode() is AttachNode peer)
                    {
                        Vector3 selfWorld = part.transform.TransformPoint(node.position);
                        Vector3 peerWorld = p.transform.TransformPoint(peer.position);
                        Vector3 delta = selfWorld - peerWorld;
                        if (delta.magnitude > TranslateTolerance)
                        {
                            //                            Debug.Log($"{ModTag} FixStackAttachments() for {part} @{RelativePos(selfW, root)}(rr), peer {p} @{RelativePos(peerW, root)}(rr)");
                            //                            Debug.Log($"{ModTag} Attachment {node.id} on {node.owner} @{selfWorld}(w), REALIGNING TO: {peer.id} on {peer.owner} @{peerWorld}(w). Delta: {delta}");
                            Part partToTranslate = (part.parent == p) ? part : p;   // Move child closer to parent  (translating parent also translates child!)
                            float dir = (partToTranslate == p) ? 1 : -1;            // delta = Movement of the peer, so invert if moving the parent
                                                                                    //                            Debug.Log($"{ModTag} {(translateParts ? string.Empty : "(DISABLED)")} Translating {partToTranslate} by {RelativeDir(dir * delta, root)}(rr)");
                            if (translateParts)
                            {
                                //partToTranslate.transform.Translate(dir * delta, Space.World);
                                partToTranslate.orgPos = partToTranslate.transform.position += (dir * delta);
                            }
                        }
                    }
                }
            }
        }

        Vector3 RelativePos(Vector3 worldPos, Part origin) => origin.transform.InverseTransformPoint(worldPos);
        Vector3 RelativeDir(Vector3 worldDir, Part origin) => origin.transform.InverseTransformDirection(worldDir);

        #endregion

        #region Object references

        [KSPField]
        public string partModelName = "stretchyTank";

        [KSPField]
        public string sidesName = "sides";

        [KSPField]
        public string endsName = "ends";

        [KSPField]
        public string colliderHolder = "colliderHolder";

        public Mesh SidesMesh { get; private set; }
        public Mesh EndsMesh { get; private set; }
        public Mesh SidesIconMesh { get; private set; }
        public Mesh EndsIconMesh { get; private set; }

        private void InitializeObjects()
        {
            // Find or create ColliderHolder and attach to the StretchyTank
            ColliderHolder = gameObject.GetChild(colliderHolder);
            if (ColliderHolder is null)
            {
                ColliderHolder = new GameObject(colliderHolder);
                ColliderHolder.transform.SetParent(gameObject.transform.FindDecendant(partModelName).transform, false);
            }
            legacyTextureHandler = new LegacyTextureHandler(part, this);
            InitializeMeshes();
        }

        private void InitializeMeshes()
        {
            // Instantiate meshes. The mesh method unshares any shared meshes.
            Transform sides = part.FindModelTransform(sidesName);
            Transform ends = part.FindModelTransform(endsName);
            Transform iconModelTransform = part.partInfo.iconPrefab.transform.FindDecendant("model");
            Transform iconSides = iconModelTransform.FindDecendant(sidesName);
            Transform iconEnds = iconModelTransform.FindDecendant(endsName);
            SidesMesh = sides.GetComponent<MeshFilter>().mesh;
            EndsMesh = ends.GetComponent<MeshFilter>().mesh;
            SidesIconMesh = (iconSides is Transform) ? iconSides.GetComponent<MeshFilter>().mesh : null;
            EndsIconMesh = (iconEnds is Transform) ? iconEnds.GetComponent<MeshFilter>().mesh : null;
        }

        #endregion

        #region Collider mesh management methods

        public GameObject ColliderHolder { get; private set; }

        /// <summary>
        /// Clears the colliderHolder GameObject, deleting all colliders previously attached.
        /// </summary>
        public void ClearColliderHolder()
        {
            foreach (Transform child in ColliderHolder.transform)
            {
                child.gameObject.DestroyGameObject();
            }
        }

        /// <summary>
        /// Used for updating a single collider mesh. If the colliderHolder only has one child, that mesh is updated, otherwise the children are cleared and a new collider is generated.
        /// </summary>
        public void UpdateOrReplaceSingleCollider(Mesh colliderMesh)
        {
            if (ColliderHolder.transform.childCount == 1)
            {
                var currMeshColl = ColliderHolder.GetComponentInChildren<MeshCollider>();
                currMeshColl.sharedMesh = colliderMesh;
            } else
            {
                ClearColliderHolder();
                var colliderObject = new GameObject("collisionMesh");
                var coll = colliderObject.AddComponent<MeshCollider>();
                colliderObject.transform.SetParent(ColliderHolder.transform, false);
                coll.convex = true;
                coll.sharedMesh = colliderMesh;
            }
        }

        #endregion

        #region Textures (deprecated)

        public bool ApplyLegacyTextures() => forceLegacyTextures || !TUEnabled;

        [KSPField(guiName = "Legacy Textures", isPersistant = true, groupName = PAWGroupName, groupDisplayName = PAWGroupDisplayName, groupStartCollapsed = false),
         UI_Toggle(disabledText = "Disabled", enabledText = "Enabled", scene = UI_Scene.Editor)]
        public bool forceLegacyTextures = false;

        [KSPField(guiName = "Texture", isPersistant = true, groupName = PAWGroupName, groupDisplayName = PAWGroupDisplayName, groupStartCollapsed = false), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string textureSet = "Original";

        [KSPField(guiName = "Ends Texture", isPersistant = true, groupName = PAWGroupName), UI_ChooseOption(scene = UI_Scene.Editor)]
        public int capTextureIndex = 0;

        private void SetTextureFieldVisibility()
        {
            Fields[nameof(forceLegacyTextures)].guiActiveEditor = TUEnabled;
            Fields[nameof(showTUPickerGUI)].guiActiveEditor = !forceLegacyTextures;
            Fields[nameof(textureSet)].guiActiveEditor = forceLegacyTextures;
            Fields[nameof(capTextureIndex)].guiActiveEditor = forceLegacyTextures;
            if (TUEnabled)
            {
                foreach (PartModule pm in part.Modules)
                {
                    if (pm.moduleName == "KSPTextureSwitch" 
                        && pm.Fields["transformName"] is BaseField f
                        && f.GetValue(pm) is string s
                        && (s.Equals("sides") || s.Equals("ends"))
                        && pm.Fields["currentTextureSet"] is BaseField visibleField)
                    {
                        visibleField.guiActiveEditor = !forceLegacyTextures;
                    }
                }
            }
        }

        private void UpdateTexture()
        {
            if (ApplyLegacyTextures())
                legacyTextureHandler.UpdateTexture();
        }

        [KSPEvent(active = true)]
        public void OnChangeTextureScale(BaseEventDetails data) 
        {
            if (ApplyLegacyTextures())
                legacyTextureHandler.ChangeTextureScale(data.Get<string>("meshName"), data.Get<Vector2>("targetScale"));
        }

        #endregion

        #region Tank shape

        [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Shape", groupName = PAWGroupName), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string shapeName;
        public void SetShapeName(string s)
        {
            string old = shapeName;
            shapeName = s;
            OnShapeSelectionChanged(Fields[nameof(shapeName)], old);
        }

        private ProceduralAbstractShape shape;
        private readonly Dictionary<string, ProceduralAbstractShape> availableShapes = new Dictionary<string, ProceduralAbstractShape>();
        public ProceduralAbstractShape CurrentShape { get => shape; }

        private void InitializeShapes()
        {
            availableShapes.Clear();
            foreach (ProceduralAbstractShape compShape in GetComponents<ProceduralAbstractShape>())
            {
                if (compShape.IsAvailable && !compShape.IsObsolete)
                {
                    availableShapes.Add(compShape.displayName, compShape);
                }
                compShape.isEnabled = compShape.enabled = false;
            }
            if (string.IsNullOrEmpty(shapeName) || !availableShapes.ContainsKey(shapeName))
            {
                Debug.LogWarning($"{ModTag} InitailizeShapes() Shape \"{shapeName}\" not available, defaulting to {availableShapes.Keys.First()}");
                shapeName = availableShapes.Keys.First();
            }
            shape = availableShapes[shapeName];
            shape.isEnabled = shape.enabled = true;
        }

        // Entry condition:  shape == fromShape
        private void ChangeShape(ProceduralAbstractShape fromShape)
        {
            Debug.Log($"{ModTag} ChangeShape from {fromShape} to {shapeName} for {this} @{this.transform.position}");
            if (fromShape is null)
                throw new ArgumentNullException(nameof(fromShape));

            if (!availableShapes.ContainsKey(shapeName))
            {
                Debug.LogError($"{ModTag} UpdateShape requested {shapeName} but that is not available!  Defaulting to {availableShapes.Keys.First()}");
                shapeName = availableShapes.Keys.First();
            }
            shape = availableShapes[shapeName];

            fromShape.isEnabled = fromShape.enabled = false;

            if (shape != fromShape)
            {
                ProceduralAbstractShape.ShapeCoordinates coord = new ProceduralAbstractShape.ShapeCoordinates();
                // For each surface-attached child, get the cylindrical coordinates, normalize them, and re-attach.
                foreach (Part child in part.children)
                {
                    if (child.FindAttachNodeByPart(part) is AttachNode node)
                    {
                        if (node.nodeType == AttachNode.NodeType.Surface)
                        {
                            Vector3 oldWorldPos = child.transform.TransformPoint(node.position);
                            Vector3 oldLocalPos = part.transform.InverseTransformPoint(oldWorldPos);
                            fromShape.GetCylindricCoordinates(oldLocalPos, coord);
                            fromShape.NormalizeCylindricCoordinates(coord);
                            shape.UnNormalizeCylindricCoordinates(coord);
                            Vector3 newPos = shape.FromCylindricCoordinates(coord);
                            Vector3 newWorldPos = part.transform.TransformPoint(newPos);
                            child.transform.Translate(newWorldPos - oldWorldPos, Space.World);
                        }
                    }
                }
                shape.InitializeAttachmentNodes();
                FixStackAttachments(true);
            }

            shape.isEnabled = shape.enabled = true;
            shape.AdjustDimensionBounds();
            shape.UpdateShape();
            if (HighLogic.LoadedSceneIsEditor) 
            {
                shape.ChangeVolume(shape.volumeName, shape.Volume);
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }
            UpdateTexture();

            UpdateTFInterops();
        }

        public bool SeekVolume(float targetVolume, int dir=0) => shape.SeekVolume(targetVolume, dir);

        #endregion

        #region Cost
        [KSPField]
        public bool costsIncludeResources = false; // set this true to define the costs KSP style including the containing resources WARNING: (May be 
//            incompatible with RF/MFT)

        [KSPField]
        public float baseCost = 0;

        [KSPField]
        public float costPerkL = 245f;

        [KSPField(isPersistant=true)]
        public float moduleCost = 0f;

        [KSPField(guiActiveEditor = true, guiName = "cost")]
        private string costDisplay = "";

        [KSPField]
        public bool displayCost = true;

        private bool? _hasMFT = null;

		public ModifierChangeWhen GetModuleCostChangeWhen () => ModifierChangeWhen.FIXED;

        private bool ContainsMFT(Part p)
        {
            if (!_hasMFT.HasValue)
                _hasMFT = part.Modules.Contains("ModuleFuelTanks");

            return _hasMFT.Value;
        }

        private void GetResourceCosts(Part p, out float maxCost, out float actualCost)
        {
            maxCost = actualCost = 0;
            foreach (PartResource r in p.Resources)
            {
                if (PartResourceLibrary.Instance.GetDefinition(r.resourceName) is PartResourceDefinition d)
                {
                    maxCost += (float)(r.maxAmount * d.unitCost);
                    actualCost += (float)(r.amount * d.unitCost);
                }
            }
        }

        public float GetModuleCost(float stdCost, ModifierStagingSituation sit)
        {
            if (HighLogic.LoadedScene == GameScenes.EDITOR)
            {
                //Debug.Log("stdCost: " + stdCost);
                float cost = baseCost;
                if (shape is ProceduralAbstractShape)
                    cost += shape.GetCurrentCostMult() * shape.Volume * costPerkL;

                foreach (PartModule pm in part.Modules)
                {
                    if(pm is ICostMultiplier)
                    {
                       cost *= (pm as ICostMultiplier).GetCurrentCostMult();
                    }
                }
                float dryCost=0;
                float actualCost=0;

                if (!ContainsMFT(part) && PartResourceLibrary.Instance != null)
                {
                    if (!costsIncludeResources)
                    {
                        dryCost = cost;
                        actualCost = cost;
                        GetResourceCosts(part, out float resMaxCost, out float resActualCost);
                        cost += resMaxCost;
                        actualCost += resActualCost;
                    }
                    else
                    {
                        GetResourceCosts(part, out float resMaxCost, out float _);
                        float minimumCosts = resMaxCost;
                        cost = Mathf.Max(minimumCosts, cost);

                        dryCost = cost;
                        actualCost = cost;

                        GetResourceCosts(part, out resMaxCost, out float resActualCost);
                        dryCost -= resMaxCost;
                        actualCost -= (resMaxCost - resActualCost);
                    }
                }
                moduleCost = cost;
                
                costDisplay = $"Dry: {dryCost:N0} Wet: {actualCost:N0}";
            }
            return moduleCost;
        }
        #endregion

        [KSPField]
        public bool updateDragCubesInEditor = false;

        [KSPEvent(guiActive = false, active = true)]
        public void OnPartColliderChanged()
        {
            if (!HighLogic.LoadedSceneIsEditor || updateDragCubesInEditor)
            {
                DragCubeTool.UpdateDragCubes(part, CurrentShape.ShapeKey);
            }
        }

        public void UpdateProps()
        {
            foreach (var prop in GetComponents<IProp>())
                prop.UpdateProp();
        }

        #region Mass override for hollow tanks

        [KSPField]
        public float densityForHollowVolume = 0f;

        [KSPField(isPersistant = true)]
        public float moduleMass = 0f;

        public ModifierChangeWhen GetModuleMassChangeWhen() => ModifierChangeWhen.FIXED;

        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit)
        {
            if (shape == null || !HighLogic.LoadedSceneIsEditor)
                return moduleMass;

            if (shape.NominalVolume > shape.Volume)
            {
                moduleMass = densityForHollowVolume * (shape.NominalVolume - shape.Volume);
                if (density > 0f)
                    moduleMass *= density * (1f / NominalDensity);
            }
            else
            {
                moduleMass = 0f;
            }

            return moduleMass;
        }

        private void SetPFFields()
        {
            var fieldDensity = Fields[nameof(density)];
            if (minDensity > 0f && maxDensity > 0f)
            {
                var floatRange = fieldDensity.uiControlEditor as UI_FloatRange;
                floatRange.minValue = minDensity;
                floatRange.maxValue = maxDensity;
                floatRange.stepIncrement = stepDensity;
                fieldDensity.guiActiveEditor = true;
                // The only thing we're doing is in GetModuleMass and GetModuleCost
                // which means that they get fired for free when a slider changes
                // (via OnEditorShipModified)
                //floatRange.onFieldChanged = floatRange.onSymmetryFieldChanged = (a, b) =>
                //{
                //    GetModuleMass(part.prefabMass, ModifierStagingSituation.UNSTAGED);
                //};
            }
            else
            {
                fieldDensity.guiActiveEditor = false;
            }

            if (density < 0f)
                density = Math.Max(minDensity, 0f);
        }

        private void OnShipModified(ShipConstruct _)
        {
            // This will recalc mass
            GetModuleMass(part.prefabMass, ModifierStagingSituation.UNSTAGED);
        }

        #endregion

        #region TestFlight
        public static Type tfInterface = null;
        public static BindingFlags tfBindingFlags = BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Static;

        public void UpdateTFInterops()
        {
            if (tfInterface is Type)
            {
                tfInterface.InvokeMember("AddInteropValue", tfBindingFlags, null, null, new System.Object[] { this.part, "shapeName", shapeName, "ProceduralParts" });
                if (shape != null)
                    shape.UpdateTFInterops();
            }
        }
        #endregion

        /// <summary>
        /// Called from RP-1
        /// </summary>
        /// <param name="validationError"></param>
        /// <param name="canBeResolved"></param>
        /// <param name="costToResolve"></param>
        /// <param name="techToResolve"></param>
        /// <returns></returns>
        public virtual bool Validate(out string validationError, out bool canBeResolved, out float costToResolve, out string techToResolve)
        {
            validationError = null;
            canBeResolved = false;
            costToResolve = 0;
            techToResolve = null;

            if (density + 0.0001 < minDensity)
            {
                validationError = $"density needs to be {minDensity:0.###} or higher";
                return false;
            }

            return true;
        }
    }
}