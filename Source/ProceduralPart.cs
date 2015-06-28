using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSPAPIExtensions;
using KSPAPIExtensions.PartMessage;

namespace ProceduralParts
{
    [PartMessageDelegate]
    public delegate void ChangeTextureScaleDelegate(string name, [UseLatest] Material material, [UseLatest] Vector2 targetScale);

    public class ProceduralPart : PartModule, IPartCostModifier
    {
        #region Initialization

        bool installedFAR = false;

        public override void OnAwake()
        {
            // Check if FAR is installed
            installedFAR = AssemblyLoader.loadedAssemblies.Any(a => a.assembly.GetName().Name == "FerramAerospaceResearch");

            base.OnAwake();
            PartMessageService.Register(this);
            //this.RegisterOnUpdateEditor(OnUpdateEditor);

            if (GameSceneFilter.AnyInitializing.IsLoaded())
                LoadTextureSets();
            InitializeTextureSet();
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
            // Load stuff from config files
            try
            {
                if (GameSceneFilter.AnyInitializing.IsLoaded())
                    LoadTechLimits(node);
            }
            catch (Exception ex)
            {
                Debug.LogError("OnLoad exception: " + ex);
                throw;
            }

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
            OnStart(StartState.Editor);

            // Need to rescale everything to make it look good in the icon, but reenable otherwise OnStart won't get called again.
            isEnabled = enabled = true;

            return base.GetInfo();
        }

        [SerializeField]
        private bool symmetryClone;

        public override void OnStart(StartState state)
        {
            if(tempCollider!=null)
            {
                // delete the temporary collider, if there is one
                Component.Destroy(tempCollider);
                tempCollider = null;
                Debug.Log("destroyed temporary collider");
            }

            // Update internal state
            try
            {
                InitializeObjects();

                if (HighLogic.LoadedSceneIsEditor)
                    InitializeTechLimits();

                InitializeShapes();
                if(GameSceneFilter.AnyEditorOrFlight.IsLoaded())
                    InitializeNodes();

                if (!HighLogic.LoadedSceneIsEditor)
                {
                    // Force the first update, then disable.
                    shape.ForceNextUpdate();
                    shape.OnUpdateEditor();

                    UpdateTexture();

                    isEnabled = enabled = false;
                }

                if (HighLogic.LoadedSceneIsEditor)
                    symmetryClone = true;

                if (GameSceneFilter.AnyEditor.IsLoaded())
                    GameEvents.onEditorPartEvent.Add(OnEditorPartEvent);

                Fields["costDisplay"].guiActiveEditor = displayCost;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                isEnabled = enabled = false;
            }
        }

        public void OnDestroy()
        {
            if (GameSceneFilter.AnyEditor.IsLoaded())
                GameEvents.onEditorPartEvent.Remove(OnEditorPartEvent);
        }


        public void OnUpdateEditor()
        {
            if (skipNextUpdate)
            {
                skipNextUpdate = false;
                return;
            }

            try
            {
                if(needsTechInit)
                    InitializeTechLimits();

                UpdateTexture();
                UpdateShape();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                isEnabled = enabled = false;
            }
        }

        private bool skipNextUpdate;

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


            SidesMaterial = sides.renderer.material;
            EndsMaterial = ends.renderer.material;
            SidesIconMaterial = iconSides.renderer.material;
            EndsIconMaterial = iconEnds.renderer.material;

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

        // This should be private, only KSP is daft sometimes.
        [SerializeField]
        public byte[] techLimitsSerialized;

        private void LoadTechLimits(ConfigNode node)
        {
            List<TechLimit> techLimits = new List<TechLimit>();
            foreach (ConfigNode tNode in node.GetNodes("TECHLIMIT"))
            {
                TechLimit limit = new TechLimit();
                limit.Load(tNode);
                techLimits.Add(limit);
            }
            if (techLimits.Count == 0)
                return;

            techLimitsSerialized = ObjectSerializer.Serialize(techLimits);
        }

        private bool needsTechInit;

        private void InitializeTechLimits()
        {
            if (HighLogic.CurrentGame == null || 
                (HighLogic.CurrentGame.Mode != Game.Modes.CAREER && HighLogic.CurrentGame.Mode != Game.Modes.SCIENCE_SANDBOX) || 
                techLimitsSerialized == null)
                return;

            if (ResearchAndDevelopment.Instance == null)
            {
                needsTechInit = true;
                return;
            }
            needsTechInit = false;
  
            List<TechLimit> techLimits;
            ObjectSerializer.Deserialize(techLimitsSerialized, out techLimits);

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

            //Debug.Log(string.Format("TechLimits applied: diameter=({0:G3}, {1:G3}) length=({2:G3}, {3:G3}) volume=({4:G3}, {5:G3}) allowCurveTweaking={6}", diameterMin, diameterMax, lengthMin, lengthMax, volumeMin, volumeMax, allowCurveTweaking));

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

            public override string ToString()
            {
                return string.Format("TechLimits(TechRequired={6} diameter=({0:G3}, {1:G3}) length=({2:G3}, {3:G3}) volume=({4:G3}, {5:G3}) )", diameterMin, diameterMax, lengthMin, lengthMax, volumeMin, volumeMax, name);
            }
        }

        #endregion

        #region Texture Sets

        [KSPField(guiName = "Texture", guiActive = false, guiActiveEditor = true, isPersistant = true), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string textureSet = "Original";
        private string oldTextureSet = "*****";

        public class TextureSet
        {
            public string name;

            public bool autoScale;
            public bool autoWidthDivide;
            public float autoHeightSteps;
            public Vector2 scale = new Vector2(2f, 1f);

            public Texture sides;
            public Texture sidesBump;
            public Texture ends;
            public string sidesName;
            public string endsName;
            public string sidesBumpName;

            public Color sidesSpecular = new Color(0.2f, 0.2f, 0.2f);
            public float sidesShininess = 0.4f;
        }
        private static List<TextureSet> loadedTextureSets;
        private static string[] loadedTextureSetNames;

        public TextureSet[] TextureSets
        {
            get
            {
                return loadedTextureSets.ToArray();
            }
        }

        public static void LoadTextureSets()
        {
            if (loadedTextureSets != null)
                return;

            loadedTextureSets = new List<TextureSet>();
            //print("*ST* Loading texture sets");
            foreach (ConfigNode texInfo in GameDatabase.Instance.GetConfigNodes("STRETCHYTANKTEXTURES"))
                for (int i = 0; i < texInfo.nodes.Count; i++)
                {
                    TextureSet textureSet = LoadTextureSet(texInfo.nodes[i]);

                    if (textureSet != null)
                        loadedTextureSets.Add(textureSet);
                }

            if (loadedTextureSets.Count == 0)
                Debug.LogError("*ST* No Texturesets found!");

            loadedTextureSets.Sort(TextureSetNameComparison);

            loadedTextureSetNames = new string[loadedTextureSets.Count];
            for (int i = 0; i < loadedTextureSets.Count; ++i)
                loadedTextureSetNames[i] = loadedTextureSets[i].name;
        }

        private static int TextureSetNameComparison(TextureSet s1, TextureSet s2)
        {
            bool s1Start = s1.sidesName.StartsWith("ProceduralParts");
            if (s1Start != s2.sidesName.StartsWith("ProceduralParts"))
            {
                return s1Start ? -1 : 1;
            }
            return string.Compare(s1.name, s2.name, StringComparison.Ordinal);
        }

        private static TextureSet LoadTextureSet(ConfigNode node)
        {
            string textureSet = node.name;

            // Sanity check
            if (node.GetNode("sides") == null || node.GetNode("ends") == null)
            {
                Debug.LogError("*ST* Invalid Textureset " + textureSet);
                return null;
            }
            if (!node.GetNode("sides").HasValue("texture") || !node.GetNode("ends").HasValue("texture"))
            {
                Debug.LogError("*ST* Invalid Textureset " + textureSet);
                return null;
            }


            // get settings
            TextureSet tex = new TextureSet
            {
                name = textureSet,
                sidesName = node.GetNode("sides").GetValue("texture"),
                endsName = node.GetNode("ends").GetValue("texture"),
                sidesBumpName = ""
            };
            if (node.GetNode("sides").HasValue("bump"))
                tex.sidesBumpName = node.GetNode("sides").GetValue("bump");

            if (node.GetNode("sides").HasValue("uScale"))
                float.TryParse(node.GetNode("sides").GetValue("uScale"), out tex.scale.x);
            if (node.GetNode("sides").HasValue("vScale"))
                float.TryParse(node.GetNode("sides").GetValue("vScale"), out tex.scale.y);


            if (node.GetNode("sides").HasValue("autoScale"))
                bool.TryParse(node.GetNode("sides").GetValue("autoScale"), out tex.autoScale);
            if (node.GetNode("sides").HasValue("autoWidthDivide"))
                bool.TryParse(node.GetNode("sides").GetValue("autoWidthDivide"), out tex.autoWidthDivide);
            if (node.GetNode("sides").HasValue("autoHeightSteps"))
                float.TryParse(node.GetNode("sides").GetValue("autoHeightSteps"), out tex.autoHeightSteps);

            if (node.GetNode("sides").HasValue("specular"))
                tex.sidesSpecular = ConfigNode.ParseColor(node.GetNode("sides").GetValue("specular"));
            if (node.GetNode("sides").HasValue("shininess"))
                float.TryParse(node.GetNode("sides").GetValue("shininess"), out tex.sidesShininess);

            Texture[] textures = Resources.FindObjectsOfTypeAll(typeof(Texture)) as Texture[];

            if (!TryFindTexture(textures, ref tex.sidesName, out tex.sides))
            {
                Debug.LogError("*ST* Sides textures not found for " + textureSet);
                return null;
            }

            if (!TryFindTexture(textures, ref tex.endsName, out tex.ends))
            {
                Debug.LogError("*ST* Ends textures not found for " + textureSet);
                return null;
            }

            if (string.IsNullOrEmpty(tex.sidesBumpName))
                tex.sidesBump = null;
            else if (!TryFindTexture(textures, ref tex.sidesBumpName, out tex.sidesBump))
            {
                Debug.LogError("*ST* Side bump textures not found for " + textureSet);
                return null;
            }


            return tex;
        }

        private static bool TryFindTexture(Texture[] textures, ref string textureName, out Texture tex)
        {
            tex = FindTexture(textures, textureName);
            if (tex != null)
                return true;
            if (!textureName.StartsWith("StretchyTanks"))
                return false;

            string substName = "ProceduralParts" + textureName.Substring("StretchyTanks".Length);
            tex = FindTexture(textures, substName);
            if (tex == null)
                return false;

            textureName = substName;
            return true;
        }

        // ReSharper disable once ParameterTypeCanBeEnumerable.Local
        private static Texture FindTexture(Texture[] textures, string textureName)
        {
            return textures.FirstOrDefault(t => t.name == textureName);
        }

        private void InitializeTextureSet()
        {
            BaseField field = Fields["textureSet"];
            UI_ChooseOption range = (UI_ChooseOption)field.uiControlEditor;

            range.options = loadedTextureSetNames;
            if (textureSet == null || !loadedTextureSetNames.Contains(textureSet))
                textureSet = loadedTextureSetNames[0];
        }

        [SerializeField]
        private Vector2 sideTextureScale = Vector2.one;

        [PartMessageListener(typeof(ChangeTextureScaleDelegate))]
        public void ChangeTextureScale(string texName, Material material, Vector2 targetScale)
        {
            if (texName != "sides")
                return;
            sideTextureScale = targetScale;
            oldTextureSet = null;
            UpdateTexture();
        }

        private void UpdateTexture()
        {
            if (textureSet == oldTextureSet)
                return;

            Material EndsMaterial;
            Material SidesMaterial;

            if(HighLogic.LoadedScene== GameScenes.LOADING)
            {
                // if we are in loading screen, all changes have to be made to the icon materials. Otherwise all icons will have the same texture 
                EndsMaterial = this.EndsIconMaterial;
                SidesMaterial = this.SidesIconMaterial;
            }
            else
            {
                EndsMaterial = this.EndsMaterial;
                SidesMaterial = this.SidesMaterial;
            }

            int newIdx = loadedTextureSets.FindIndex(set => set.name == textureSet);
            if (newIdx < 0)
            {
                Debug.LogError("*ST* Unable to find texture set: " + textureSet);
                textureSet = oldTextureSet;
                return;
            }
            oldTextureSet = textureSet;

            TextureSet tex = loadedTextureSets[newIdx];

            // Set shaders
            if (!part.Modules.Contains("ModulePaintable"))
            {
                SidesMaterial.shader = Shader.Find(tex.sidesBump != null ? "KSP/Bumped Specular" : "KSP/Specular");

                // pt is no longer specular ever, just diffuse.
                if (EndsMaterial != null)
                    EndsMaterial.shader = Shader.Find("KSP/Diffuse");
            }

            SidesMaterial.SetColor("_SpecColor", tex.sidesSpecular);
            SidesMaterial.SetFloat("_Shininess", tex.sidesShininess);

            // TODO: shove into config file.
            if (EndsMaterial != null)
            {
                const float scale = 0.93f;
                const float offset = (1f / scale - 1f) / 2f;
                EndsMaterial.mainTextureScale = new Vector2(scale, scale);
                EndsMaterial.mainTextureOffset = new Vector2(offset, offset);
            }

            // set up UVs
            Vector2 scaleUV = tex.scale;
            if (tex.autoScale)
            {
                scaleUV.x = (float)Math.Round(scaleUV.x * sideTextureScale.x / 8f);
                if (scaleUV.x < 1)
                    scaleUV.x = 1;
                if (tex.autoWidthDivide)
                {
                    if (tex.autoHeightSteps > 0)
                        scaleUV.y = (float)Math.Ceiling(scaleUV.y * sideTextureScale.y / scaleUV.x * (1f / tex.autoHeightSteps)) * tex.autoHeightSteps;
                    else
                        scaleUV.y *= sideTextureScale.y / scaleUV.x;
                }
                else
                {
                    if (tex.autoHeightSteps > 0)
                        scaleUV.y = (float)Math.Max(Math.Round(sideTextureScale.y / tex.autoHeightSteps), 1f) * tex.autoHeightSteps;
                    else
                        scaleUV.y *= sideTextureScale.y;
                }
            }

            // apply
            SidesMaterial.mainTextureScale = scaleUV;
            SidesMaterial.mainTextureOffset = Vector2.zero;
            SidesMaterial.SetTexture("_MainTex", tex.sides);
            if (tex.sidesBump != null)
            {
                SidesMaterial.SetTextureScale("_BumpMap", scaleUV);
                SidesMaterial.SetTextureOffset("_BumpMap", Vector2.zero);
                SidesMaterial.SetTexture("_BumpMap", tex.sidesBump);
            }
            if (EndsMaterial != null)
                EndsMaterial.SetTexture("_MainTex", tex.ends);
        }

        #endregion

        #region Node Attachments

        private readonly List<object> nodeAttachments = new List<object>(4);
        private readonly Dictionary<string, Func<Vector3>> nodeOffsets = new Dictionary<string, Func<Vector3>>();

        private class NodeTransformable : TransformFollower.Transformable, IPartMessagePartProxy
        {
            // leave as not serializable so will be null when deserialzied.
            private readonly Part part;
            private readonly AttachNode node;

            // ReSharper disable once EventNeverSubscribedTo.Local
            [PartMessageEvent]
            public event PartAttachNodePositionChanged NodePositionChanged;

            public NodeTransformable(Part part, AttachNode node)
            {
                this.part = part;
                this.node = node;
                PartMessageService.Register(this);
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
            AttachNode node = part.findAttachNode(nodeId);
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
                rotate = rotate.Inverse();

                part.transform.Translate(childToParent.position);
                part.transform.rotation = rotate * part.transform.rotation;
                part.transform.Translate(-childToParent.position);
            }
        }

        private Queue<Action> toAttach = new Queue<Action>();


        public void OnEditorPartEvent(ConstructionEventType type, Part part)
        {        
            switch (type)
            {
                case ConstructionEventType.PartRootSelected:

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

                break;

                case ConstructionEventType.PartOffset:
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

        [PartMessageListener(typeof(PartAttachNodePositionChanged), PartRelationship.Child, GameSceneFilter.AnyEditor)]
        public void PartAttachNodePositionChanged(AttachNode node, [UseLatest] Vector3 location, [UseLatest] Vector3 orientation, [UseLatest] Vector3 secondaryAxis)
        {
            Debug.LogWarning("PartNode position changed");
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

        [PartMessageListener(typeof(PartChildAttached), scenes: GameSceneFilter.AnyEditor)]
        public void PartChildAttached(Part child)
        {
            if (shape == null) //OnUpdate hasn't fired yet
            {
                toAttach.Enqueue(() => PartChildAttached(child));
                return;
            }
            //Debug.Log("PartChildAttached");
            AttachNode node = child.findAttachNodeByPart(part);
            if (node == null)
            {
                Debug.LogError("*ST* unable to find child node for child: " + child.transform);
                return;
            }
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

                    AttachNode ourNode = part.findAttachNodeByPart(child);
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

        [PartMessageListener(typeof(PartChildDetached), scenes: GameSceneFilter.AnyEditor)]
        public void PartChildDetached(Part child)
        {
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
                    //Debug.LogWarning("Detaching from: " + part + " child: " + child.transform.name);
                    return;
                }
            Debug.LogWarning("*ST* Message recieved removing child, but can't find child");
        }

        [PartMessageListener(typeof(PartParentChanged), scenes: GameSceneFilter.AnyEditor)]
        public void PartParentChanged(Part newParent)
        {
            if (shape == null) //OnUpdate hasn't fired yet
            {
                toAttach.Enqueue(() => PartParentChanged(newParent));
                return;
            }
            //Debug.Log("PartParentChanged");
            if (parentAttachment != null)
            {
                RemovePartAttachment(parentAttachment);
                //Debug.LogWarning("Detatching: " + part + " from parent: " + newParent);
                parentAttachment = null;
            }

            if (newParent == null)
                return;

            AttachNode childToParent = part.findAttachNodeByPart(newParent);
            if (childToParent == null)
            {
                Debug.LogError("*ST* unable to find parent node from child: " + part.transform);
                return;
            }
            Vector3 position = transform.TransformPoint(childToParent.position);
            
            // ReSharper disable once InconsistentNaming
            Func<Vector3> Offset;
            if (nodeOffsets.TryGetValue(childToParent.id, out Offset))
                position -= Offset();

            Part root = EditorLogic.SortedShipList[0];

            //Debug.LogWarning("Attaching: " + part + " to new parent: " + newParent + " node:" + childToParent.id + " position=" + childToParent.position.ToString("G3"));

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
            List<string> shapeNames = new List<string>();
            availableShapes.Clear();
            foreach (ProceduralAbstractShape compShape in GetComponents<ProceduralAbstractShape>())
            {
                if (!string.IsNullOrEmpty(compShape.techRequired) && ResearchAndDevelopment.GetTechnologyState(compShape.techRequired) != RDTech.State.Available)
                    goto disableShape;
                if (!string.IsNullOrEmpty(compShape.techObsolete) && ResearchAndDevelopment.GetTechnologyState(compShape.techObsolete) == RDTech.State.Available)
                    goto disableShape;

                availableShapes.Add(compShape.displayName, compShape);
                shapeNames.Add(compShape.displayName);

                if (string.IsNullOrEmpty(shapeName) ? (availableShapes.Count == 1) : compShape.displayName == shapeName)
                {
                    shape = compShape;
                    oldShapeName = shapeName = shape.displayName;
                    shape.isEnabled = shape.enabled = true;
                    continue;
                }

            disableShape:
                compShape.isEnabled = compShape.enabled = false;
            }

            BaseField field = Fields["shapeName"];
            switch (availableShapes.Count)
            {
                case 0:
                    throw new InvalidProgramException("No shapes available");
                case 1:
                    field.guiActiveEditor = false;
                    break;
                default:
                    field.guiActiveEditor = true;
                    UI_ChooseOption range = (UI_ChooseOption)field.uiControlEditor;
                    range.options = shapeNames.ToArray();
                    break;
            }

            if (string.IsNullOrEmpty(shapeName) || !availableShapes.ContainsKey(shapeName))
            {
                oldShapeName = shapeName = shapeNames[0];
                shape = availableShapes[shapeName];
                shape.isEnabled = shape.enabled = true;
            }
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

        public float GetModuleCost(float stdCost)
        {
            if (HighLogic.LoadedScene == GameScenes.EDITOR)
            {
                //Debug.Log("GetModuleCost");
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
                float dryCost = cost;
                float actualCost = cost;
                if (!part.Modules.Contains("ModuleFuelTanks") && (object)PartResourceLibrary.Instance != null)
                {
                    foreach (PartResource r in part.Resources)
                    {
                        PartResourceDefinition d = PartResourceLibrary.Instance.GetDefinition(r.resourceName);
                        if ((object)d != null)
                        {
                            if (!costsIncludeResources)
                            {
                                cost += (float)(r.maxAmount * d.unitCost);
                                actualCost += (float)(r.amount * d.unitCost);
                            }
                            else
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

       
        [PartMessageListener(typeof(PartModelChanged), scenes: ~GameSceneFilter.Flight)]
        public void PartModelChanged()
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

                tempColliderSize.x = max.x - min.x;
                tempColliderSize.y = max.y - min.y;
                tempColliderSize.z = max.z - min.z;

                //Debug.Log(tempColliderSize);
            }


        }

        [PartMessageListener(typeof(PartColliderChanged), scenes: GameSceneFilter.AnyEditorOrFlight)]
        public void PartColliderChanged()
        {
            if (!installedFAR)
            {
                DragCube dragCube = DragCubeSystem.Instance.RenderProceduralDragCube(base.part);

                base.part.DragCubes.ClearCubes();
                base.part.DragCubes.Cubes.Add(dragCube);
                base.part.DragCubes.ResetCubeWeights();
            }

        }

    }
}