using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;
using System.Reflection;

namespace ProceduralParts
{
    public class ProceduralPart : PartModule, IPartCostModifier
    {
        public static readonly string ModTag = "[ProceduralParts]";
        public const string PAWGroupName = "ProcParts";
        public const string PAWGroupDisplayName = "ProceduralParts";

        internal LegacyTextureHandler legacyTextureHandler;

        #region Initialization

        [KSPField(isPersistant = true)]
        private Vector3 tempColliderCenter;

        [KSPField(isPersistant = true)]
        private Vector3 tempColliderSize;

        private BoxCollider tempCollider;
        private bool isInitialized = false;
        public static bool installedFAR = false;
        public static bool staticallyInitialized = false;
        public static void StaticInit()
        {
            if (staticallyInitialized) return;

            if (AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.assembly.GetName().Name == "TestFlight") is AssemblyLoader.LoadedAssembly tfAssembly)
                tfInterface = Type.GetType("TestFlightCore.TestFlightInterface, TestFlightCore", false);
            installedFAR = AssemblyLoader.loadedAssemblies.Any(a => a.assembly.GetName().Name == "FerramAerospaceResearch");
            TextureSet.LoadTextureSets(LegacyTextureHandler.textureSets);
            staticallyInitialized = true;
        }

        public override void OnAwake()
        {
            StaticInit();
            base.OnAwake();
        }

        public void Update()
        {
            if (HighLogic.LoadedSceneIsEditor && needsTechInit) InitializeTechLimits();
        }

        public override void OnLoad(ConfigNode node)
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
                LoadTechLimits(node);

            // An existing vessel part or .craft file that has never set this value before, but not the availablePart
            if (HighLogic.LoadedScene != GameScenes.LOADING && !node.HasValue(nameof(forceLegacyTextures)))
                forceLegacyTextures = true;

            if (HighLogic.LoadedSceneIsFlight)
            {
                // Create a temporary collider for KSP so that it can set the craft on the ground properly
                tempCollider = gameObject.AddComponent<BoxCollider>();
                tempCollider.center = tempColliderCenter;
                tempCollider.size = tempColliderSize;
                tempCollider.enabled = true;
            }
        }

        public override string GetInfo()
        {
            if (!isInitialized)
            {
                isInitialized = true;
                InitializeObjects();
                InitializeShapes();
                if (shape is ProceduralAbstractShape)
                {
                    shape.UpdateShape();
                    if (HighLogic.LoadedScene == GameScenes.LOADING)
                        shape.FixEditorIconScale();
                }
                UpdateTexture();
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
            Debug.Log($"{ModTag} OnStart()");
            isInitialized = true;
            InitializeObjects();
            InitializeShapes();
            InitializeTechLimits();

            if (HighLogic.LoadedSceneIsEditor)
                legacyTextureHandler.ValidateSelectedTexture();

            if (shape is ProceduralAbstractShape)
                shape.UpdateShape();
            if (part.variants is ModulePartVariants)
                part.variants.useMultipleDragCubes = false;

            if (HighLogic.LoadedSceneIsFlight && vessel is Vessel && vessel.rootPart == part)
                GameEvents.onFlightReady.Add(DragCubeFixer);

            if (HighLogic.LoadedSceneIsEditor)
            {
                BaseField field = Fields[nameof(textureSet)];
                UI_ChooseOption opt = (UI_ChooseOption)field.uiControlEditor;
                opt.options = LegacyTextureHandler.textureSets.Keys.ToArray();
                opt.onSymmetryFieldChanged = opt.onFieldChanged = new Callback<BaseField, object>(OnTextureChanged);

                BaseField capTextureField = Fields[nameof(capTextureIndex)];
                opt = (UI_ChooseOption)capTextureField.uiControlEditor;
                opt.options = Enum.GetNames(typeof(LegacyTextureHandler.CapTextureMode));
                opt.onSymmetryFieldChanged = opt.onFieldChanged = new Callback<BaseField, object>(OnTextureChanged);

                Fields[nameof(shapeName)].guiActiveEditor = availableShapes.Count > 1;
                opt = Fields[nameof(shapeName)].uiControlEditor as UI_ChooseOption;
                opt.options = availableShapes.Keys.ToArray();
                // The onSymmetryFieldChanged callbacks do not have the correct previous object assigned.
                // Since this matters, we need to handle it ourselves.
                opt.onFieldChanged = new Callback<BaseField, object>(OnShapeSelectionChanged);

                Fields[nameof(costDisplay)].guiActiveEditor = displayCost;

                GameEvents.onVariantApplied.Add(OnVariantApplied);
            }
            base.OnStart(state);

            if (tempCollider != null)
            {
                // delete the temporary collider, if there is one, probably too soon to do this
                Component.Destroy(tempCollider);
                tempCollider = null;
            }
        }

        public override void OnStartFinished(StartState state)
        {
            Debug.Log($"{ModTag} OnStartFinished for {this}");
            shape.InitializeAttachmentNodes();
            UpdateTexture();
            FixStackAttachments();
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
                        if (delta.magnitude > 0.1)
                        {
                            Debug.Log($"{ModTag} FixStackAttachments() for {part} @{RelativePos(selfW, root)}(rr), peer {p} @{RelativePos(peerW, root)}(rr)");
                            Debug.Log($"{ModTag} Attachment {node.id} on {node.owner} @{selfWorld}(w), REALIGNING TO: {peer.id} on {peer.owner} @{peerWorld}(w). Delta: {delta}");
                            Part partToTranslate = (part.parent == p) ? part : p;   // Move child closer to parent  (translating parent also translates child!)
                            float dir = (partToTranslate == p) ? 1 : -1;            // delta = Movement of the peer, so invert if moving the parent
                            Debug.Log($"{ModTag} {(translateParts ? string.Empty : "(DISABLED)")} Translating {partToTranslate} by {RelativeDir(dir * delta, root)}(rr)");
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

        public void OnDestroy()
        {
            if (HighLogic.LoadedSceneIsFlight)
                GameEvents.onFlightReady.Remove(DragCubeFixer);
            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onVariantApplied.Remove(OnVariantApplied);
        }

        public void OnTextureChanged(BaseField f, object obj)
        {
            UpdateTexture();
        }

        // onSymmetryFieldChanged() callback has the incorrect value for parameter obj
        // So we manually invoke things for our symmetry counterparts.
        public void OnShapeSelectionChanged(BaseField f, object obj)
        {
            Debug.Log($"{ModTag} OnShapeSelectionChanged for {this} from {obj} to {f.GetValue(this)}");
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

        #endregion

        #region Object references

        [KSPField]
        public string partModelName = "stretchyTank";

        [KSPField]
        public string sidesName = "sides";

        [KSPField]
        public string endsName = "ends";

        [KSPField]
        public string collisionName = "collisionMesh";

        public Mesh SidesMesh { get; private set; }
        public Mesh EndsMesh { get; private set; }
        public Mesh SidesIconMesh { get; private set; }
        public Mesh EndsIconMesh { get; private set; }

        private void InitializeObjects()
        {
            Debug.Log($"{ModTag} InitializeObjects() - Transforms, Materials and Meshes");
            //Transform partModel = part.FindModelTransform(partModelName);
            Transform colliderTr = part.FindModelTransform(collisionName);
            partCollider = colliderTr.GetComponent<MeshCollider>();
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

        private MeshCollider partCollider;

        // The partCollider mesh. This must be called whenever the contents of the mesh changes, even if the object remains the same.
        public Mesh ColliderMesh
        {
            get => partCollider.sharedMesh;
            set
            {
                if (ownColliderMesh)
                {
                    Destroy(partCollider.sharedMesh);
                    ownColliderMesh = false;
                }
                partCollider.sharedMesh = value;
                partCollider.enabled = false;
                partCollider.enabled = true;
            }
        }

        [SerializeField]
        private bool ownColliderMesh = true;

        /// <summary>
        /// Call by base classes to update the partCollider mesh.
        /// </summary>
        /// <param name="meshes">List of meshes to set the partCollider to</param>
        public void SetColliderMeshes(params Mesh[] meshes)
        {
            if (ownColliderMesh)
                Destroy(partCollider.sharedMesh);

            if (meshes.Length == 1)
            {
                partCollider.sharedMesh = meshes[0];
                ownColliderMesh = false;
            }
            else
            {
                CombineInstance[] combine = new CombineInstance[meshes.Length];
                for (int i = 0; i < meshes.Length; ++i)
                {
                    combine[i] = new CombineInstance
                    {
                        mesh = meshes[i]
                    };
                }
                Mesh colliderMesh = new Mesh();
                colliderMesh.CombineMeshes(combine, true, false);
                partCollider.sharedMesh = colliderMesh;
                ownColliderMesh = true;
            }

            // If we don't do this, the partCollider doesn't work properly.
            partCollider.enabled = false;
            partCollider.enabled = true;
        }

        #endregion

        #region Maximum dimensions and shape constraints

        [KSPField]
        public float diameterMax = 0;

        [KSPField]
        public float diameterMin = 0.01f;

        [KSPField]
        public float diameterLargeStep = 1.25f;

        [KSPField]
        public float diameterSmallStep = 0.125f;

        [KSPField]
        public float lengthMax = 0;

        [KSPField]
        public float lengthMin = 0.01f;

        [KSPField]
        public float lengthLargeStep = 1.0f;

        [KSPField]
        public float lengthSmallStep = 0.125f;

        [KSPField]
        public float volumeMax = 0;

        /// <summary>
        /// Set to false if user is not allowed to tweak the fillet / curve Id.
        /// </summary>
        [KSPField]
        public bool allowCurveTweaking = true;

        private readonly List<TechLimit> techLimits = new List<TechLimit>();
        public TechLimit currentLimit;
        private bool needsTechInit;

        private void LoadTechLimits(ConfigNode node)
        {
            foreach (ConfigNode tNode in node.GetNodes("TECHLIMIT"))
            {
                TechLimit limit = new TechLimit();
                limit.Load(tNode);
                techLimits.Add(limit);
                Debug.Log($"{ModTag} LoadTechLimits loading {limit}");
            }
        }

        private void InitializeTechLimits()
        {
            techLimits.Clear();
            techLimits.AddRange(part.partInfo.partPrefab.FindModuleImplementing<ProceduralPart>().techLimits);

            needsTechInit = false;
            currentLimit = new TechLimit
            {
                diameterMax = this.diameterMax,
                diameterMin = this.diameterMin,
                lengthMax = this.lengthMax,
                lengthMin = this.lengthMin,
                volumeMax = this.volumeMax,
                allowCurveTweaking = true
            };

            if (HighLogic.CurrentGame is Game &&
                (HighLogic.CurrentGame.Mode == Game.Modes.CAREER || HighLogic.CurrentGame.Mode == Game.Modes.SCIENCE_SANDBOX))
            {
                if (ResearchAndDevelopment.Instance is null)
                {
                    Debug.LogError($"{ModTag} InitializeTechLimits() but R&D Instance is null!");
                    needsTechInit = true;
                    return;
                }

                foreach (TechLimit limit in techLimits)
                {
                    if (ResearchAndDevelopment.GetTechnologyState(limit.name) == RDTech.State.Available)
                        currentLimit.ApplyLimit(limit);
                }
            } else
            {
                Debug.Log($"{ModTag} Skipping Tech Limits because Game is {HighLogic.CurrentGame?.Mode}");
            }
            currentLimit.Validate();
            SetFromLimit(currentLimit);

            Debug.Log($"{ModTag} TechLimits applied: diameter=({diameterMin:G3}, {diameterMax:G3}) length=({lengthMin:G3}, {lengthMax:G3}) volumeMax={volumeMax:G3} )");

            foreach (ProceduralAbstractShape shape in GetComponents<ProceduralAbstractShape>())
                shape.UpdateTechConstraints();
        }

        private void SetFromLimit(TechLimit limit)
        {
            diameterMax = limit.diameterMax;
            diameterMin = limit.diameterMin;
            lengthMax = limit.lengthMax;
            lengthMin = limit.lengthMin;
            volumeMax = limit.volumeMax;
            allowCurveTweaking = limit.allowCurveTweaking;
        }

        #endregion

        #region Textures (deprecated)

        public bool ApplyLegacyTextures() => forceLegacyTextures || !(part.GetComponent("KSPTextureSwitch") is Component);

        [KSPField(guiName = "Legacy Textures", guiActive = false, guiActiveEditor = true, isPersistant = true, groupName = PAWGroupName, groupDisplayName = PAWGroupDisplayName, groupStartCollapsed = false),
         UI_Toggle(disabledText = "Disabled", enabledText = "Enabled", scene = UI_Scene.Editor)]
        public bool forceLegacyTextures = false;

        [KSPField(guiName = "Texture", guiActive = false, guiActiveEditor = true, isPersistant = true, groupName = PAWGroupName), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string textureSet = "Original";

        [KSPField(guiName = "Ends Texture", guiActive = false, guiActiveEditor = true, isPersistant = true, groupName = PAWGroupName), UI_ChooseOption(scene = UI_Scene.Editor)]
        public int capTextureIndex = 0;
        private void UpdateTexture()
        {
            if (ApplyLegacyTextures())
                legacyTextureHandler.UpdateTexture();
        }

        #endregion

        #region Tank shape

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Shape", groupName = PAWGroupName), UI_ChooseOption(scene = UI_Scene.Editor)]
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
            Debug.Log($"{ModTag} InitializeShapes - Discovering available shapes and selecting for {shapeName}");
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
                Debug.Log($"{ModTag} InitailizeShapes() Shape {shapeName} not available, defaulting to {availableShapes.Keys.First()}");
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
            shape.UpdateShape();
            if (HighLogic.LoadedSceneIsEditor) 
            {
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }
            UpdateTexture();

            UpdateTFInterops();
        }

        public bool SeekVolume(float targetVolume) => shape.SeekVolume(targetVolume);

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

        [KSPField(guiActiveEditor=true, guiName="cost")]
        private string costDisplay = "";

        [KSPField]
        public bool displayCost = true;

		public ModifierChangeWhen GetModuleCostChangeWhen () => ModifierChangeWhen.FIXED;

        private bool ContainsMFT(Part p)
        {
            foreach (PartModule pm in p.Modules)
            {
                if (pm.name.Equals("ModuleFuelTanks")) return true;
            }
            return false;
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

        [KSPEvent(guiActive = false, active = true)]
        public void OnPartModelChanged()
        {
            if(partCollider!=null)
            {
                tempColliderCenter = Vector3.zero;

                Vector3 min = transform.InverseTransformPoint(partCollider.bounds.min);
                Vector3 max = transform.InverseTransformPoint(partCollider.bounds.max);

                tempColliderSize.x = Mathf.Max(max.x - min.x, 0.001f);
                tempColliderSize.y = Mathf.Max(max.y - min.y, 0.001f);
                tempColliderSize.z = Mathf.Max(max.z - min.z, 0.001f);

                //Debug.Log(tempColliderSize);
            }
        }

        [KSPField]
        bool updateDragCubesInEditor = false;

        [KSPEvent(guiActive = false, active = true)]
        public void OnPartColliderChanged()
        {
            if (HighLogic.LoadedSceneIsFlight || (HighLogic.LoadedSceneIsEditor && updateDragCubesInEditor))
            {
                DragCube dragCube = DragCubeSystem.Instance.RenderProceduralDragCube(base.part);
                part.DragCubes.ClearCubes();
                part.DragCubes.Cubes.Add(dragCube);
                part.DragCubes.ResetCubeWeights();
                part.DragCubes.ForceUpdate(true, true, false);
            }
        }

        private void DragCubeFixer()
        {
            /* FlightIntegrator resets our dragcube after loading, so we need to rerender it.
             * Exactly when FlightIntegrator does this is unpredictable, so we resort to the OnFlightReady event.
            */
            Debug.Log($"{ModTag} DragCubeFixer rebuilding root part drag cubes");
            OnPartColliderChanged();
            GameEvents.onVesselWasModified.Fire(part.vessel);
        }

        public void UpdateProps()
        {
            foreach (var pm in GetComponents<PartModule>())
            {
                if (pm is IProp prop) prop.UpdateProp();
            }
        }

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
    }
}