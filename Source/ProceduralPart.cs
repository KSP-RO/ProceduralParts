using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;
using System.Reflection;

namespace ProceduralParts
{
    //[PartMessageDelegate]
    //public delegate void ChangeTextureScaleDelegate(string name, [UseLatest] Material material, [UseLatest] Vector2 targetScale);

    public class ProceduralPart : PartModule, IPartCostModifier
    {
        public static readonly string ModTag = "[ProceduralParts]";

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

        #region Initialization

        public static bool installedFAR = false;
        public static bool staticallyInitialized = false;
        public static void StaticInit()
        {
            if (staticallyInitialized) return;

            if (AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.assembly.GetName().Name == "TestFlight") is AssemblyLoader.LoadedAssembly tfAssembly)
                tfInterface = Type.GetType("TestFlightCore.TestFlightInterface, TestFlightCore", false);
            installedFAR = AssemblyLoader.loadedAssemblies.Any(a => a.assembly.GetName().Name == "FerramAerospaceResearch");
            TextureSet.LoadTextureSets(textureSets);
            staticallyInitialized = true;
        }


        public override void OnAwake()
        {
            StaticInit();
            base.OnAwake();
        }

        public void Update()
        {
            if (HighLogic.LoadedSceneIsEditor)
                OnUpdateEditor();
        }

        public void LateUpdate()
        {
            if(HighLogic.LoadedSceneIsEditor)
            while (toAttach.Count() > 0)
            {
                toAttach.Dequeue().Invoke();
            }
        }

        [KSPField(isPersistant = true)]
        private  Vector3 tempColliderCenter;

        [KSPField(isPersistant = true)]
        private Vector3 tempColliderSize;

        private BoxCollider tempCollider;
        public override void OnLoad(ConfigNode node)
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
                LoadTechLimits(node);

            if (HighLogic.LoadedSceneIsFlight)
            {     
                // Create a temporary collider for KSP so that it can set the craft on the ground properly
                tempCollider = gameObject.AddComponent<BoxCollider>();
                tempCollider.center = tempColliderCenter;
                tempCollider.size = tempColliderSize;
                tempCollider.enabled = true;
                //Debug.Log("created temp collider with size: " + tempCollider.size);
                //Debug.Log("bounds: " + tempCollider.bounds);
            }
        }

        public override void OnSave(ConfigNode node)
        {
            // Force saved value for enabled to be true.
            node.SetValue("isEnabled", "True");
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
                    shape.ForceNextUpdate();
                    shape.OnUpdateEditor();
                }
                UpdateTexture();
            }

            // Need to rescale everything to make it look good in the icon, but reenable otherwise OnStart won't get called again.
            isEnabled = enabled = true;

            return base.GetInfo();
        }

        [SerializeField]
        private bool symmetryClone;

        private bool isInitialized;

        public override void OnStart(StartState state)
        {
            Debug.Log($"{ModTag} OnStart()");
            isInitialized = true;
            InitializeObjects();
            InitializeShapes();
            InitializeNodes();

            if (HighLogic.LoadedSceneIsFlight)
            {
                // Force the first update, then disable.
                if (shape is ProceduralAbstractShape)
                {
                    Debug.Log($"{ModTag} Forcing shape Update()");
                    shape.ForceNextUpdate();
                    shape.OnUpdateEditor();
                }
                Debug.Log($"{ModTag} Updating texture");
                UpdateTexture();
                if (vessel is Vessel)
                {
                    if (vessel.rootPart == part) // drag cube re-rendering workaround. See FixedUpdate for more info
                        TimingManager.FixedUpdateAdd(TimingManager.TimingStage.FlightIntegrator, DragCubeFixer);
                    else
                    {
                        isEnabled = enabled = false;
                        Debug.Log($"{ModTag} OnStart() disabling PartModule for non-root part {this.part}");
                    }
                }
            }

            if (HighLogic.LoadedSceneIsEditor)
            {
                symmetryClone = true;

                Debug.Log($"{ModTag} Initializing Tech Limits");
                InitializeTechLimits();

                if (!textureSets.ContainsKey(textureSet))
                {
                    Debug.Log($"{ModTag} Defaulting invalid TextureSet {textureSet} to {textureSets.Keys.First()}");
                    textureSet = textureSets.Keys.First();
                }

                BaseField field = Fields[nameof(textureSet)];
                UI_ChooseOption opt = (UI_ChooseOption)field.uiControlEditor;
                opt.options = textureSets.Keys.ToArray();

                BaseField capTextureField = Fields[nameof(capTextureIndex)];
                opt = (UI_ChooseOption)capTextureField.uiControlEditor;
                opt.options = Enum.GetNames(typeof(CapTextureMode));

                Fields[nameof(shapeName)].guiActiveEditor = availableShapes.Count > 1;
                opt = (UI_ChooseOption)Fields[nameof(shapeName)].uiControlEditor;
                opt.options = availableShapes.Keys.ToArray();

                Fields[nameof(costDisplay)].guiActiveEditor = displayCost;

                GameEvents.onPartAttach.Add(OnPartAttach);
                GameEvents.onPartRemove.Add(OnPartRemove);
                GameEvents.onEditorPartEvent.Add(OnEditorPartEvent);
            }
            base.OnStart(state);

            if (tempCollider != null)
            {
                // delete the temporary collider, if there is one, probably too soon to do this
                Component.Destroy(tempCollider);
                tempCollider = null;
            }
        }

        public void OnDestroy()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onEditorPartEvent.Remove(OnEditorPartEvent);
                GameEvents.onPartAttach.Remove(OnPartAttach);
                GameEvents.onPartRemove.Remove(OnPartRemove);
            }
        }

        public void OnUpdateEditor()
        {
            if(needsTechInit)
                InitializeTechLimits();

            UpdateTexture();
            UpdateShape();
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

        public Material SidesMaterial { get; private set; }
        public Material EndsMaterial { get; private set; }

        public Mesh SidesMesh { get; private set; }
        public Mesh EndsMesh { get; private set; }

        public Material SidesIconMaterial { get; private set; }
        public Material EndsIconMaterial { get; private set; }

        public Mesh SidesIconMesh { get; private set; }
        public Mesh EndsIconMesh { get; private set; }

        private Transform partModel;

        private void InitializeObjects()
        {
            Debug.Log($"{ModTag} InitializeObjects() - Transforms, Materials and Meshes");
            partModel = part.FindModelTransform(partModelName);
            
            Transform sides     = part.FindModelTransform(sidesName);
            Transform ends      = part.FindModelTransform(endsName);
            Transform colliderTr = part.FindModelTransform(collisionName);

            Transform iconModelTransform = part.partInfo.iconPrefab.transform.FindDecendant("model");

            Transform iconSides = iconModelTransform.FindDecendant(sidesName);
            Transform iconEnds = iconModelTransform.FindDecendant(endsName);

            if(iconSides != null)
                SidesIconMesh = iconSides.GetComponent<MeshFilter>().mesh;
            if(iconEnds != null)
                EndsIconMesh = iconEnds.GetComponent<MeshFilter>().mesh;

            SidesMaterial = sides.GetComponent<Renderer>().material;
			EndsMaterial = ends.GetComponent<Renderer>().material;
			SidesIconMaterial = iconSides.GetComponent<Renderer>().material;
			EndsIconMaterial = iconEnds.GetComponent<Renderer>().material;

            // Instantiate meshes. The mesh method unshares any shared meshes.
            SidesMesh = sides.GetComponent<MeshFilter>().mesh;
            EndsMesh = ends.GetComponent<MeshFilter>().mesh;
            partCollider = colliderTr.GetComponent<MeshCollider>();

            // Will need to destroy any old transform offset followers, they will be rebuilt in due course
            if (symmetryClone)
                foreach (TransformFollower follower in partModel.GetComponentsInChildren<TransformFollower>())
                    Destroy(follower);
        }

        #endregion

        #region Collider mesh management methods

        private MeshCollider partCollider;

        // The partCollider mesh. This must be called whenever the contents of the mesh changes, even if the object remains the same.
        public Mesh ColliderMesh
        {
            get
            {
                return partCollider.sharedMesh;
            }
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

        /// <summary>
        /// Maximum radial diameter in meters
        /// </summary>
        [KSPField]
        public float diameterMax = float.PositiveInfinity;

        /// <summary>
        /// Minimum radial diameter in meters
        /// </summary>
        [KSPField]
        public float diameterMin = 0.01f;

        /// <summary>
        /// The 'large step' for diameter sliders.
        /// </summary>
        [KSPField]
        public float diameterLargeStep = 1.25f;

        /// <summary>
        /// The 'small step' for diameter sliders.
        /// </summary>
        [KSPField]
        public float diameterSmallStep = 0.125f;

        /// <summary>
        /// Maximum length in meters
        /// </summary>
        [KSPField]
        public float lengthMax = float.PositiveInfinity;

        /// <summary>
        /// Minimum length in meters
        /// </summary>
        [KSPField]
        public float lengthMin = 0.01f;

        /// <summary>
        /// Large length step size
        /// </summary>
        [KSPField]
        public float lengthLargeStep = 1.0f;

        /// <summary>
        /// Small length step size
        /// </summary>
        [KSPField]
        public float lengthSmallStep = 0.125f;

        /// <summary>
        /// Maximum volume in meters
        /// </summary>
        [KSPField]
        public float volumeMax = float.PositiveInfinity;

        /// <summary>
        /// Minimum volume in meters
        /// </summary>
        [KSPField]
        public float volumeMin = 0.001f;

        /// <summary>
        /// Minimum aspect ratio - min ratio of length / diameter.
        /// For cones, the biggest end is the one for the diameter. 
        /// </summary>
        [KSPField]
        public float aspectMin;

        /// <summary>
        /// Minimum aspect ratio - min ratio of length / diameter.
        /// For cones, the biggest end is the one for the diameter. 
        /// </summary>
        [KSPField]
        public float aspectMax = float.PositiveInfinity;

        /// <summary>
        /// Set to false if user is not allowed to tweak the fillet / curve Id.
        /// </summary>
        [KSPField]
        public bool allowCurveTweaking = true;

        private readonly List<TechLimit> techLimits = new List<TechLimit>();

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

        private bool needsTechInit;

        private void InitializeTechLimits()
        {
            if (HighLogic.CurrentGame == null || 
                (HighLogic.CurrentGame.Mode != Game.Modes.CAREER && HighLogic.CurrentGame.Mode != Game.Modes.SCIENCE_SANDBOX))
                return;

            if (ResearchAndDevelopment.Instance == null)
            {
                Debug.LogError($"{ModTag} InitializeTechLimits() but R&D Instance is null!");
                needsTechInit = true;
                return;
            }
            techLimits.Clear();
            techLimits.AddRange(part.partInfo.partPrefab.FindModuleImplementing<ProceduralPart>().techLimits);
            Debug.Log($"{ModTag} InitializeTechLimits() found {techLimits.Count} limits to test");

            needsTechInit = false;

            // ReSharper disable LocalVariableHidesMember
            float diameterMax = 0;
            float diameterMin = float.PositiveInfinity;
            float lengthMax = 0;
            float lengthMin = float.PositiveInfinity;
            float volumeMax = 0;
            float volumeMin = float.PositiveInfinity;
            float aspectMin = 0;
            float aspectMax = float.PositiveInfinity;
            bool allowCurveTweaking = false;

            foreach (TechLimit limit in techLimits)
            {
                Debug.Log($"{ModTag} InitializeTechLimits() testing {limit}");
                if (ResearchAndDevelopment.GetTechnologyState(limit.name) != RDTech.State.Available)
                    continue;

                if (limit.diameterMin < diameterMin)
                    diameterMin = limit.diameterMin;
                if (limit.diameterMax > diameterMax)
                    diameterMax = limit.diameterMax;
                if (limit.lengthMin < lengthMin)
                    lengthMin = limit.lengthMin;
                if (limit.lengthMax > lengthMax)
                    lengthMax = limit.lengthMax;
                if (limit.volumeMin < volumeMin)
                    volumeMin = limit.volumeMin;
                if (limit.volumeMax > volumeMax)
                    volumeMax = limit.volumeMax;
                if (limit.aspectMin < aspectMin)
                    aspectMin = limit.aspectMin;
                if (limit.aspectMax > aspectMax)
                    aspectMax = limit.aspectMax;
                if (limit.allowCurveTweaking)
                    allowCurveTweaking = true;
            }

            // ReSharper disable CompareOfFloatsByEqualityOperator
            if (diameterMax == 0)
                diameterMax = float.PositiveInfinity;
            if (float.IsInfinity(diameterMin))
                diameterMin = 0.01f;
            if (lengthMax == 0)
                lengthMax = float.PositiveInfinity;
            if (float.IsInfinity(lengthMin))
                lengthMin = 0.01f;
            if (volumeMax == 0)
                volumeMax = float.PositiveInfinity;
            if (float.IsInfinity(volumeMin))
                volumeMin = 0.01f;
            if (aspectMax == 0)
                aspectMax = float.PositiveInfinity;
            if (float.IsInfinity(aspectMin))
                aspectMin = 0.01f;
            // ReSharper restore CompareOfFloatsByEqualityOperator

            this.diameterMax = Mathf.Min(this.diameterMax, diameterMax);
            this.diameterMin = Mathf.Max(this.diameterMin, diameterMin);
            this.lengthMax = Mathf.Min(this.lengthMax, lengthMax);
            this.lengthMin = Mathf.Max(this.lengthMin, lengthMin);
            this.volumeMax = Mathf.Min(this.volumeMax, volumeMax);
            this.volumeMin = Mathf.Max(this.volumeMin, volumeMin);
            this.aspectMax = Mathf.Min(this.aspectMax, aspectMax);
            this.aspectMin = Mathf.Max(this.aspectMin, aspectMin);
            this.allowCurveTweaking = this.allowCurveTweaking && allowCurveTweaking;
            // ReSharper restore LocalVariableHidesMember

            Debug.Log($"{ModTag} TechLimits applied: diameter=({diameterMin: G3}, {diameterMax: G3}) length=({lengthMin: G3}, {lengthMax: G3}) volume=({volumeMin: G3}, {volumeMax: G3}) )");

            foreach (ProceduralAbstractShape shape in GetComponents<ProceduralAbstractShape>())
                shape.UpdateTechConstraints();
        }

        [Serializable]
        public class TechLimit : IConfigNode
        {
            [Persistent]
            public string name;
            [Persistent]
            public float diameterMin = float.NaN;
            [Persistent]
            public float diameterMax = float.NaN;
            [Persistent]
            public float lengthMin = float.NaN;
            [Persistent]
            public float lengthMax = float.NaN;
            [Persistent]
            public float volumeMax = float.NaN;
            [Persistent]
            public float volumeMin = float.NaN;
            [Persistent]
            public float aspectMax = float.NaN;
            [Persistent]
            public float aspectMin = float.NaN;
            [Persistent]
            public bool allowCurveTweaking = true;

            public void Load(ConfigNode node)
            {
                ConfigNode.LoadObjectFromConfig(this, node);
                if (name == null)
                {
                    name = node.GetValue("TechRequired");
                }
            }
            public void Save(ConfigNode node)
            {
                ConfigNode.CreateConfigFromObject(this, node);
            }

            public override string ToString() =>
                $"TechLimits(TechRequired={name} diameter=({diameterMin:G3}, {diameterMax:G3}) length=({lengthMin:G3}, {lengthMax:G3}) volume=({volumeMin:G3}, {volumeMax:G3}) )";
        }

        #endregion

        #region Texture Sets
        public enum CapTextureMode
        {
            Ends, Side, GreySide, PlainWhite
        }

        [KSPField(guiName = "Texture", guiActive = false, guiActiveEditor = true, isPersistant = true), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string textureSet = "Original";
        private string oldTextureSet = "*****";

        [KSPField(guiName = "Ends Texture", guiActive = false, guiActiveEditor = true, isPersistant = true), UI_ChooseOption(scene = UI_Scene.Editor)]
        public int capTextureIndex = 0;
        private CapTextureMode CapTexture => (CapTextureMode)capTextureIndex;
        private CapTextureMode oldCapTexture = CapTextureMode.Ends;

        private static readonly Dictionary<string, TextureSet> textureSets = new Dictionary<string, TextureSet>();
        public TextureSet TextureSet => textureSets[textureSet];
        public TextureSet[] TextureSets { get => textureSets.Values.ToArray(); }

        [SerializeField]
        private Vector2 sideTextureScale = Vector2.one;

		[KSPEvent(guiActive = false, active = true)]
		public void OnChangeTextureScale(BaseEventDetails data)
		{
			string meshName = data.Get<string> ("meshName");
			Vector2 targetScale = data.Get<Vector2> ("targetScale");
            if (meshName != "sides")
                return;
            Debug.Log($"{ModTag} OnChangeTextureScale for {this} mesh {meshName} scale {targetScale}");
            sideTextureScale = targetScale;
            oldTextureSet = null;
            UpdateTexture();
        }

        private void UpdateTexture()
        {
            if (textureSet == oldTextureSet && CapTexture == oldCapTexture)
                return;

            Material endsMaterial;
            Material sidesMaterial;

            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                // if we are in loading screen, all changes have to be made to the icon materials. Otherwise all icons will have the same texture 
                endsMaterial = this.EndsIconMaterial;
                sidesMaterial = this.SidesIconMaterial;
            }
            else
            {
                endsMaterial = this.EndsMaterial;
                sidesMaterial = this.SidesMaterial;
            }
            if (!textureSets.ContainsKey(textureSet))
            {
                Debug.LogError($"{ModTag} UpdateTexture() {textureSet} missing from global list!");
                textureSet = oldTextureSet;
            }

            oldTextureSet = textureSet;
            oldCapTexture = CapTexture;

            TextureSet tex = textureSets[textureSet];

            if (!part.Modules.Contains("ModulePaintable"))
            {
                TextureSet.SetupShader(sidesMaterial, tex.sidesBump);
            }

            sidesMaterial.SetColor("_SpecColor", tex.sidesSpecular);
            sidesMaterial.SetFloat("_Shininess", tex.sidesShininess);

            var scaleUV = tex.GetScaleUv(sideTextureScale);

            sidesMaterial.mainTextureScale = scaleUV;
            sidesMaterial.mainTextureOffset = Vector2.zero;
            sidesMaterial.SetTexture("_MainTex", tex.sides);
            if (tex.sidesBump != null)
            {
                sidesMaterial.SetTextureScale("_BumpMap", scaleUV);
                sidesMaterial.SetTextureOffset("_BumpMap", Vector2.zero);
                sidesMaterial.SetTexture("_BumpMap", tex.sidesBump);
            }
            if (endsMaterial != null)
            {
                SetupEndsTexture(endsMaterial, tex, scaleUV);
            }
        }

        private void SetupEndsTexture(Material endsMaterial, TextureSet tex, Vector2 scaleUV)
        {
            switch (CapTexture)
            {
                case CapTextureMode.Ends:
                    TextureSet.SetTextureProperties(endsMaterial, tex.ends, tex.endsBump, tex.endsSpecular, tex.endsShininess, tex.endsAutoScale, scaleUV);
                    break;
                case CapTextureMode.Side:
                    TextureSet.SetTextureProperties(endsMaterial, tex.sides, tex.sidesBump, tex.sidesSpecular, tex.sidesShininess, tex.autoScale, scaleUV);
                    break;
                default:
                    if (textureSets[Enum.GetName(typeof(CapTextureMode), CapTexture)] is TextureSet texture)
                    {
                        var endsScaleUV = texture.GetScaleUv(sideTextureScale);
                        TextureSet.SetTextureProperties(endsMaterial, texture.sides, texture.sidesBump, texture.sidesSpecular, texture.sidesShininess, texture.autoScale, endsScaleUV);
                    }
                    break;
            }
        }

        #endregion

        #region Node Attachments

        private readonly List<object> nodeAttachments = new List<object>(4);
        private readonly Dictionary<string, Func<Vector3>> nodeOffsets = new Dictionary<string, Func<Vector3>>();

        private class NodeTransformable : TransformFollower.Transformable//, IPartMessagePartProxy
        {
            // leave as not serializable so will be null when deserialzied.
            private readonly Part part;
            private readonly AttachNode node;

            // ReSharper disable once EventNeverSubscribedTo.Local
            //[PartMessageEvent]
            //public event PartAttachNodePositionChanged NodePositionChanged;

			public void NodePositionChanged(AttachNode node, Vector3 location, Vector3 orientation, Vector3 secondaryAxis)
			{
				var data = new BaseEventDetails (BaseEventDetails.Sender.USER);
				data.Set<AttachNode> ("node", node);
				data.Set("location", location);
				data.Set("orientation", orientation);
				data.Set("secondaryAxis", secondaryAxis);
				part.SendEvent ("OnPartAttachNodePositionChanged", data, 0);
			}

            public NodeTransformable(Part part, AttachNode node)
            {
                this.part = part;
                this.node = node;
                //PartMessageService.Register(this);
            }

            public override bool Destroyed
            {
                get { return node == null; }
            }

            public override void Translate(Vector3 translation)
            {
                node.originalPosition = node.position += part.transform.InverseTransformPoint(translation + part.transform.position);

                NodePositionChanged(node, node.position, node.orientation, node.secondaryAxis);
                //Debug.LogWarning("Transforming node:" + node.id + " part:" + part.name + " translation=" + translation + " new position=" + node.position.ToString("F3") + " orientation=" + node.orientation.ToString("F3"));
            }

            public override void Rotate(Quaternion rotate)
            {
                Vector3 oldOrientationWorld = part.transform.TransformPoint(node.orientation) - part.transform.position;
                Vector3 newOrientationWorld = rotate * oldOrientationWorld;
                node.originalOrientation = node.orientation = part.transform.InverseTransformPoint(newOrientationWorld + part.transform.position).normalized;

                NodePositionChanged(node, node.position, node.orientation, node.secondaryAxis);
                //Debug.LogWarning("Transforming node:" + node.id + " rotation=" + rotate.ToStringAngleAxis() + " new position=" + node.position.ToString("F3") + " orientation=" + node.orientation.ToString("F3"));
            }

            public Part ProxyPart
            {
                get { return part; }
            }
        }

        private void InitializeNodes()
        {
            Debug.Log($"{ModTag} InitializeNodes()");
            // Since symmetry clone nodes are already offset, we need to update the 
            // shape first to allow non-normalized surface attachments
            if (symmetryClone)
            {
                shape.ForceNextUpdate();
                shape.OnUpdateEditor();
            }

            foreach (AttachNode node in part.attachNodes)
                InitializeNode(node);
            if (part.attachRules.allowSrfAttach)
                InitializeNode(part.srfAttachNode);

            // Update the shape to put the nodes into their positions.
            shape.ForceNextUpdate();
            shape.OnUpdateEditor();

            // In flight mode, discard all the transform followers because the are not required
            if (HighLogic.LoadedSceneIsFlight)
                foreach (object att in nodeAttachments)
                    shape.RemoveAttachment(att, false);
        }

        private void InitializeNode(AttachNode node)
        {
            Vector3 position = transform.TransformPoint(node.position);
            node.originalPosition = node.position *= 1.00001f;

            TransformFollower follower = TransformFollower.CreateFollower(partModel, position, new NodeTransformable(part, node));

            // When the object is symmetryClone, the nodes will be already in offset, not in standard offset.
            object data = shape.AddAttachment(follower, !symmetryClone);

            nodeAttachments.Add(data);
        }

        // ReSharper disable once InconsistentNaming
        public void AddNodeOffset(string nodeId, Func<Vector3> GetOffset)
        {
            AttachNode node = part.FindAttachNode(nodeId);
            if (node == null)
                throw new ArgumentException("Node " + nodeId + " is not the ID of an attachment node");

            nodeOffsets.Add(nodeId, GetOffset);
        }

        #endregion

        #region Part Attachments

        private PartAttachment parentAttachment;
        private readonly LinkedList<PartAttachment> childAttachments = new LinkedList<PartAttachment>();

        private class PartAttachment
        {
            public Part child;
            public readonly TransformFollower follower;
            public object data;

            public PartAttachment(TransformFollower follower, object data)
            {
                this.follower = follower;
                this.data = data;
            }
        }

        private readonly LinkedList<FreePartAttachment> childAttach = new LinkedList<FreePartAttachment>();
        private class FreePartAttachment
        {
            private Part child;
            private AttachNode node; // the attachment node of the child (not the one which it is attached to)

            public Part Child
            {
                get { return child; }
            }

            public AttachNode AttachNode
            {
                get {return node;}
            }

            public FreePartAttachment(Part child, AttachNode node)
            {
                this.child = child;
                this.node = node;
            }
            
            // cylindric coordinates

            public ProceduralAbstractShape.ShapeCoordinates Coordinates = new ProceduralAbstractShape.ShapeCoordinates();

            //public float r;
            //public float u;
            //public float y;
        }

        private class ParentTransformable : TransformFollower.Transformable
        {
            private readonly Part root;
            private readonly Part part;
            private readonly AttachNode childToParent;

            public ParentTransformable(Part root, Part part, AttachNode childToParent)
            {
                this.root = root;
                this.part = part;
                this.childToParent = childToParent;
            }

            public override bool Destroyed
            {
                get { return root == null; }
            }

            public override void Translate(Vector3 trans)
            {
                if (childToParent.nodeType != AttachNode.NodeType.Surface)
                {
                    // For stack nodes, push the parent up instead of moving the part down.
                    int siblings = part.symmetryCounterparts == null ? 1 : (part.symmetryCounterparts.Count + 1);
                    root.transform.Translate(trans / siblings, Space.World);
                }
                // PushSourceInfo the part down, we need to delta this childAttachment away so that when the translation from the parent reaches here it ends in the right spot
                part.transform.Translate(-trans, Space.World);
            }

            public override void Rotate(Quaternion rotate)
            {
                // Apply the inverse rotation to the part itself. Don't involve the parent.
                rotate = Quaternion.Inverse(rotate);

                part.transform.Translate(childToParent.position);
                part.transform.rotation = rotate * part.transform.rotation;
                part.transform.Translate(-childToParent.position);
            }
        }

        private Queue<Action> toAttach = new Queue<Action>();


        public void OnEditorPartEvent(ConstructionEventType type, Part part)
        {
            //Debug.Log("ProceduralPart.OnEditorPartEvent");
            switch (type)
            {
                case ConstructionEventType.PartRootSelected:
                    Debug.Log("[ProceduralPart.OnEditorPartEvent] ConstructionEventType.PartRootSelected");
                    //StartCoroutine(RebuildPartAttachments());
                    Part[] children = childAttach.Select<FreePartAttachment, Part>(x => x.Child).ToArray();

                    foreach (Part attachment in children)
                
                    {
                    
                        PartChildDetached(attachment);
                    }

                    foreach (Transform t in transform)
                    {
                        Part child = t.GetComponent<Part>();
                        if(child != null)
                            PartChildAttached(child);
                    }

                    if (transform.parent == null)
                        PartParentChanged(null);
                    else
                        PartParentChanged(transform.parent.GetComponent<Part>());

                    Debug.Log("[ProceduralPart.OnEditorPartEvent] Finished PartRootSelected");
                    
                break;

                case ConstructionEventType.PartOffset:
                    Debug.Log("[ProceduralPart.OnEditorPartEvent] ConstructionEventType.PartOffset");
                    foreach (FreePartAttachment ca in childAttach)
                    {
                        if (ca.Child == part || ca.Child.isSymmetryCounterPart(part))
                        {
                            Vector3 position = ca.Child.transform.TransformPoint(ca.AttachNode.position);
                            //ca.node.nodeType
                            //shape.GetCylindricCoordinates(part.transform.localPosition, out ca.u, out ca.y, out ca.r);
                            shape.GetCylindricCoordinates(transform.InverseTransformPoint(position), ca.Coordinates);

                            //Debug.Log("y: " + ca.y);
                            //Debug.Log("u: " + ca.u);
                            //Debug.Log("r: " + ca.r);
                            //RemovePartAttachment(pa);
                            //childAttachments.Remove(pa);
                            //PartChildAttached(part);
                            
                            
                            //break;
                        }
                    }
                break;
            }       
        }

        //[PartMessageListener(typeof(PartAttachNodePositionChanged), PartRelationship.Child, GameSceneFilter.AnyEditor)]
        [KSPEvent(guiActive = false, active = true)]
		//public void PartAttachNodePositionChanged(AttachNode node, [UseLatest] Vector3 location, [UseLatest] Vector3 orientation, [UseLatest] Vector3 secondaryAxis)
		public void OnPartAttachNodePositionChanged(BaseEventDetails data)
        {
			//TODO resrict to child

			AttachNode node = data.Get<AttachNode>("node");
			//Vector3 location = data.Get("location");
			//Vector3 orientation = data.Get("orientation");
			//Vector3 secondaryAxis = data.Get("secondaryAxis");


            if(node == null)
            {
                Debug.LogError("PartAttachNodePositionChanged message received, but node is null.");
                return;
            }

            if(node.owner == null)
            {
                Debug.LogWarning("PartAttachNodePositionChanged message received, but node.owner is null. Message ignored");
                return;
            }

            if (node.owner.GetComponent<ProceduralPart>() == null)
            {
                foreach (FreePartAttachment attachment in childAttach)
                {
                    if (node == attachment.AttachNode)
                    {
                        Vector3 position = node.owner.transform.TransformPoint(node.position);
                        shape.GetCylindricCoordinates(transform.InverseTransformPoint(position), attachment.Coordinates);
                    }

                }
            }

        }

		private void OnPartAttach(GameEvents.HostTargetAction<Part, Part> data)
		{
			// Target is the parent, host is the child part
			//Debug.Log ("OnPartAttach: " + data.host.transform + " to " + data.target.transform);
			if (data.target == part) 
				PartChildAttached (data.host);
			else if (data.host == part)
				PartParentChanged (data.target);
		}

		private void OnPartRemove(GameEvents.HostTargetAction<Part, Part> data)
		{
			// host is null, target is the child part.
			//Debug.Log ("OnPartRemove");
			if (data.target == part)
				PartParentChanged (null);
			else if (data.target.parent == part) 
			{
				PartChildDetached (data.target);
			}
		
			//SendAsyncProxy<PartParentChanged>(this, data.target, new object[] { null });
			//SendAsyncProxy<PartChildDetached>(this, data.target.parent, data.target);
			//
			//if (data.target.attachMode == AttachModes.SRF_ATTACH)
			//	data.target.srfAttachNode.attachedPart = null;

		}

        //[PartMessageListener(typeof(PartChildAttached), scenes: GameSceneFilter.AnyEditor)]
        public void PartChildAttached(Part child)
        {
			if (HighLogic.LoadedScene != GameScenes.EDITOR)
				return;

			//Debug.Log ("PartChildAttached");

			AttachNode node = child.FindAttachNodeByPart(part);

			if (shape == null || node == null) //OnUpdate hasn't fired or node not connected yet
            {
                toAttach.Enqueue(() => PartChildAttached(child));
                return;
            }
            //Debug.Log("PartChildAttached");
            
            //if (node == null)
            //{
            //    Debug.LogError("*ST* unable to find child node for child: " + child.transform);
			//	//toAttach.Enqueue(() => PartChildAttached(child));
            //    return;
            //}
			//else
			//	Debug.LogError("*ST* found: " + child.transform);

            Vector3 position = child.transform.TransformPoint(node.position);

            // Handle node offsets
            //if (child.attachMode != AttachModes.SRF_ATTACH)
            //{
            //    AttachNode ourNode = part.findAttachNodeByPart(child);
            //    if (ourNode == null)
            //    {
            //        Debug.LogError("*ST* unable to find our node for child: " + child.transform);
            //        return;
            //    }
            //    // ReSharper disable once InconsistentNaming
            //    Func<Vector3> Offset;
            //    if (nodeOffsets.TryGetValue(ourNode.id, out Offset))
            //        position -= Offset();
            //}

            //Debug.LogWarning("Attaching to parent: " + part + " child: " + child.transform.name);
            FreePartAttachment newAttachment = new FreePartAttachment(child, node);

            switch (child.attachMode)
            {
                case AttachModes.SRF_ATTACH:
                    newAttachment.Coordinates.RadiusMode = ProceduralAbstractShape.ShapeCoordinates.RMode.OFFSET_FROM_SHAPE_RADIUS;
                    newAttachment.Coordinates.HeightMode = ProceduralAbstractShape.ShapeCoordinates.YMode.RELATIVE_TO_SHAPE;
                    break;

                case AttachModes.STACK:
                    newAttachment.Coordinates.RadiusMode = ProceduralAbstractShape.ShapeCoordinates.RMode.RELATIVE_TO_SHAPE_RADIUS;

                    AttachNode ourNode = part.FindAttachNodeByPart(child);
                    if (ourNode == null)
                    {
                        Debug.LogError("*ST* unable to find our node for child: " + child.transform);
                        return;
                    }
                    
                    //Debug.Log("NodeID: " + ourNode.id);

                    if (ourNode.id == "top")
                        newAttachment.Coordinates.HeightMode = ProceduralAbstractShape.ShapeCoordinates.YMode.OFFSET_FROM_SHAPE_TOP;
                    else if (ourNode.id == "bottom")
                        newAttachment.Coordinates.HeightMode = ProceduralAbstractShape.ShapeCoordinates.YMode.OFFSET_FROM_SHAPE_BOTTOM;
                    else
                        newAttachment.Coordinates.HeightMode = ProceduralAbstractShape.ShapeCoordinates.YMode.RELATIVE_TO_SHAPE;
                    break;

                default:
                    Debug.LogError("Unknown AttachMode: " + child.attachMode);
                    break;
            }

           
            shape.GetCylindricCoordinates(transform.InverseTransformPoint(position), newAttachment.Coordinates);
            
            childAttach.AddLast(newAttachment);

            //PartAttachment attach = AddPartAttachment(position, new TransformFollower.TransformTransformable(child.transform, node.position));
            //attach.child = child;

            //childAttachments.AddLast(attach);

            //shape.ForceNextUpdate();
        }

        //[PartMessageListener(typeof(PartChildDetached), scenes: GameSceneFilter.AnyEditor)]
        public void PartChildDetached(Part child)
        {

			if (HighLogic.LoadedScene != GameScenes.EDITOR)
				return;

            //Debug.Log("Child Detached");
            //for (var node = childAttachments.First; node != null; node = node.Next)
            //    if (node.Value.child == child)
            //    {
            //        RemovePartAttachment(node.Value);
            //        childAttachments.Remove(node);
            //        //Debug.LogWarning("Detaching from: " + part + " child: " + child.transform.name);
            //        return;
            //    }

            for (var node = childAttach.First; node != null; node = node.Next)
                if (node.Value.Child == child)
                {

                    childAttach.Remove(node);
                    Debug.LogWarning("Detaching from: " + part + " child: " + child?.transform?.name);
                    return;
                }
            Debug.LogWarning("*ST* Message received removing child, but can't find child");
        }

        //[PartMessageListener(typeof(PartParentChanged), scenes: GameSceneFilter.AnyEditor)]
        public void PartParentChanged(Part newParent)
        {
			if (HighLogic.LoadedScene != GameScenes.EDITOR)
				return;

			AttachNode childToParent = null;
			if (newParent != null) 
			{
				childToParent = part.FindAttachNodeByPart(newParent);
			}

            bool srfAttached = false;
            
            if (childToParent == null && newParent?.srfAttachNode?.attachedPart == part)
            {
                childToParent = newParent.srfAttachNode;
                srfAttached = true;
            }
            
            if (shape == null || (newParent != null && childToParent == null)) //OnUpdate hasn't fired yet
            {
                toAttach.Enqueue(() => PartParentChanged(newParent));
                return;
            }
            
            Debug.Log("ProceduralPart.PartParentChanged");
            if (parentAttachment != null)
            {
                Debug.Log("ProceduralPart.PartParentChanged Detaching: " + part + " from parent: " + newParent);
                RemovePartAttachment(parentAttachment);
                parentAttachment = null;
            }

            if (newParent == null)
                return;

            //AttachNode childToParent = part.findAttachNodeByPart(newParent);
            //if (childToParent == null)
            //{
            //    Debug.LogError("*ST* unable to find parent node from child: " + part.transform);
            //    return;
            //}
            Vector3 position = transform.TransformPoint(childToParent.position);

            
            // ReSharper disable once InconsistentNaming
            Func<Vector3> Offset;
            if (nodeOffsets.TryGetValue(childToParent.id, out Offset))
            {
                position -= Offset();
            }
            else if(srfAttached)
            {
                position -= (childToParent.attachedPart.transform.position - childToParent.owner.transform.position);
            }

            Part root = EditorLogic.SortedShipList[0];

            Debug.Log("ProceduralPart.PartParentChanged Attaching: " + part + " to new parent: " + newParent + " node:" + childToParent.id + " position=" + childToParent.position.ToString("G3"));

            //we need to delta this childAttachment down so that when the translation from the parent reaches here i ends in the right spot
            parentAttachment = AddPartAttachment(position, new ParentTransformable(root, part, childToParent));
            parentAttachment.child = newParent;

            // for symetric attachments, seems required. Don't know why.
            shape.ForceNextUpdate();
        }

        private PartAttachment AddPartAttachment(Vector3 position, TransformFollower.Transformable target, bool normalized = false)
        {
            if ((object)target == null)
                Debug.Log("AddPartAttachment: null target!");
            TransformFollower follower = TransformFollower.CreateFollower(partModel, position, target);
            if((object)follower == null)
                Debug.Log("AddPartAttachment: null follower!");
            object data = shape.AddAttachment(follower, normalized);

            return new PartAttachment(follower, data);
        }

        private void RemovePartAttachment(PartAttachment delete)
        {
            shape.RemoveAttachment(delete.data, false);
            Destroy(delete.follower.gameObject);
        }

        #endregion

        #region Model Attachments

        private class ModelAttachment
        {
            public Transform child;
            public object data;
        }

        private readonly LinkedList<ModelAttachment> attachments = new LinkedList<ModelAttachment>();

        /// <summary>
        /// Attach a gameObject to the surface of the part. The object must have a transform that is a child of the part.
        /// </summary>
        public void AddAttachment(Transform child, bool normalized)
        {
            AddAttachment(child, Vector3.zero, normalized);
        }

        public void AddAttachment(Transform child, Vector3 offset, bool normalized)
        {
            ModelAttachment attach = new ModelAttachment
            {
                child = child
            };

            Vector3 position = child.TransformPoint(offset);

            TransformFollower follower = TransformFollower.CreateFollower(partModel, position, new TransformFollower.TransformTransformable(child, position, Space.World));
            attach.data = shape.AddAttachment(follower, normalized);

            attachments.AddLast(attach);
        }

        public void RemoveAttachment(Transform child)
        {
            LinkedListNode<ModelAttachment> node;
            for (node = attachments.First; node != null; node = node.Next)
                if (node.Value.child == child)
                    goto foundNode;
            // Not found, fail silently
            return;

        foundNode:
            TransformFollower follower = shape.RemoveAttachment(node.Value.data, false);
            Destroy(follower);

            attachments.Remove(node);
        }

        #endregion

        #region Tank shape

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Shape"), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string shapeName;
        private string oldShapeName = "****";

        private ProceduralAbstractShape shape;
        private readonly Dictionary<string, ProceduralAbstractShape> availableShapes = new Dictionary<string, ProceduralAbstractShape>();

        public ProceduralAbstractShape CurrentShape
        {
            get { return shape; }
        }

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
            oldShapeName = shapeName;
        }

        private void UpdateShape()
        {
            if (shapeName == oldShapeName)
                return;

            ProceduralAbstractShape newShape;
            if (!availableShapes.TryGetValue(shapeName, out newShape))
            {
                Debug.LogError("*ST* Unable to find compShape: " + shapeName);
                shapeName = oldShapeName;
                return;
            }

            if (shape != null)
            {
                shape.isEnabled = shape.enabled = false;

                // Pull off all the attachments, resetting them to standard offset, then reattach in the new shape.
                for (int i = 0; i < nodeAttachments.Count; ++i)
                {
                    TransformFollower follower = shape.RemoveAttachment(nodeAttachments[i], true);
                    nodeAttachments[i] = newShape.AddAttachment(follower, true);
                }
                if (parentAttachment != null)
                {
                    shape.RemoveAttachment(parentAttachment.data, true);
                    parentAttachment.data = newShape.AddAttachment(parentAttachment.follower, true);
                }
                foreach (PartAttachment childAttachment in childAttachments)
                {
                    shape.RemoveAttachment(childAttachment.data, true);
                    childAttachment.data = newShape.AddAttachment(childAttachment.follower, true);
                }
                foreach (ModelAttachment attach in attachments)
                {
                    TransformFollower follower = shape.RemoveAttachment(attach.data, true);
                    attach.data = newShape.AddAttachment(follower, true);
                }
            }

            shape = newShape;
            shape.isEnabled = shape.enabled = true;

            oldShapeName = shapeName;

            if (HighLogic.LoadedSceneIsEditor)
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);

            UpdateTFInterops();
        }

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

		public ModifierChangeWhen GetModuleCostChangeWhen ()
		{
			return ModifierChangeWhen.FIXED;
		}

		public float GetModuleCost(float stdCost, ModifierStagingSituation sit)
        {
            if (HighLogic.LoadedScene == GameScenes.EDITOR)
            {
                //Debug.Log("stdCost: " + stdCost);
                float cost = baseCost;
                if ((object)shape != null)
                    cost += shape.GetCurrentCostMult() * shape.Volume * costPerkL;
                //Debug.Log(cost);
                foreach (PartModule pm in part.Modules)
                {
                    if(pm is ICostMultiplier)
                    {
                       cost *= (pm as ICostMultiplier).GetCurrentCostMult();
                    }
                }
                float dryCost=0;
                float actualCost=0;

                bool containsMFT = false;

                try
                {
                    for (int i = 0; i < part.Modules.Count; i++)
                    {
                        if (part.Modules[i].name == "ModuleFuelTanks")
                        {
                            containsMFT = true;
                            break;
                        }
                    }
                }
                catch
                {
                    Debug.Log("Caught error searching for ModuleFuelTanks");
                }

                if (!containsMFT && (object)PartResourceLibrary.Instance != null)
                {
                    if (!costsIncludeResources)
                    {
                        dryCost = cost;
                        actualCost = cost;
                        foreach (PartResource r in part.Resources)
                        {
                            PartResourceDefinition d = PartResourceLibrary.Instance.GetDefinition(r.resourceName);
                            if ((object)d != null)
                            {
                                cost += (float)(r.maxAmount * d.unitCost);
                                actualCost += (float)(r.amount * d.unitCost);      
                            }
                        }
                    }
                    else
                    {
                        float minimumCosts = 0;

                        foreach (PartResource r in part.Resources)
                        {
                            PartResourceDefinition d = PartResourceLibrary.Instance.GetDefinition(r.resourceName);
                            if ((object)d != null)
                            {
                                minimumCosts += (float)(r.maxAmount * d.unitCost);
                            }
                        }
                        cost = Mathf.Max(minimumCosts, cost);
                        dryCost = cost;
                        actualCost = cost;

                        foreach (PartResource r in part.Resources)
                        {
                            PartResourceDefinition d = PartResourceLibrary.Instance.GetDefinition(r.resourceName);
                            if ((object)d != null)
                            {
                                dryCost -= (float)(r.maxAmount * d.unitCost);
                                actualCost -= (float)((r.maxAmount - r.amount) * d.unitCost);
                            }
                        }
                    }
                }
                moduleCost = cost;
                
                costDisplay = String.Format("Dry: {0:N0} Wet: {1:N0}", dryCost, actualCost);
            }
            return moduleCost;
        }
        #endregion

       
        //[PartMessageListener(typeof(PartModelChanged), scenes: ~GameSceneFilter.Flight)]
        [KSPEvent(guiActive = false, active = true)]
		public void OnPartModelChanged()
        {
            //Debug.Log("Shape Changed");
            foreach (FreePartAttachment ca in childAttach)
            {
                Vector3 newPosition = shape.FromCylindricCoordinates(ca.Coordinates);
                newPosition = transform.TransformPoint(newPosition);

                Vector3 oldPosition = ca.Child.transform.TransformPoint(ca.AttachNode.position);

                Vector3 offset = newPosition - oldPosition;

                ca.Child.transform.Translate(offset, Space.World);
                //ca.child.transform.localPosition = shape.FromCylindricCoordinates(ca.u, ca.y, ca.r);// -ca.node.position;
                //Debug.Log(ca.Child.transform.localPosition);
                //Debug.Log("u: " + ca.u);
                //Debug.Log("y: " + ca.y);
                //Debug.Log("r: " + ca.r);
            }

            
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
                if (vessel is null)
                    Debug.LogError($"{ModTag} OnPartColliderChanged() - VESSEL IS NULL");
                //rebuilding the drag cube might mess up the thermal graph. Firing a vessel event should cause it to rebuild
                //GameEvents.onVesselWasModified.Fire(part.vessel);
                //part.DragCubes.Procedural = true;
            }
        }

        private void DragCubeFixer()
        {
            /* FlightIntegrator resets our dragcube after loading, so we need to rerender it. We cannot do it on the first frame because it would
                be executed before the reset. Instead we must do it on the second frame.
                FlightIntegrator doesn't act on the first frame anymore, so we need to wait until it has done something.
                This appears to work on Flight scene starts, but not when eg reverting to launch.
            */
            if (FlightIntegrator.ActiveVesselFI is FlightIntegrator FI && FI.isRunning && !FI.firstFrame)
            {
                Debug.Log($"{ModTag} DragCubeFixer rebuilding root part drag cubes");
                OnPartColliderChanged();
                GameEvents.onVesselWasModified.Fire(part.vessel);
                TimingManager.FixedUpdateRemove(TimingManager.TimingStage.FlightIntegrator, DragCubeFixer);
                isEnabled = enabled = false;
            }
        }
    }
}