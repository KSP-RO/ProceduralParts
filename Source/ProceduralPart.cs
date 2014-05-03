using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;
using KSPAPIExtensions.PartMessage;

namespace ProceduralParts
{
    [PartMessageDelegate]
    public delegate void ChangeTextureScaleDelegate(string name, [UseLatest] Material material, [UseLatest] Vector2 targetScale);

    public class ProceduralPart : PartModule
    {
        #region Initialization

        public override void OnAwake()
        {
            base.OnAwake();
            PartMessageService.Register(this);

            if (GameSceneFilter.AnyInitializing.IsLoaded())
                LoadTextureSets();
            InitializeTextureSet();
        }

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
                throw ex;
            }
        }

        public override void OnSave(ConfigNode node)
        {
            // Force saved value for enabled to be true.
            node.SetValue("isEnabled", "True");
        }

        public override string GetInfo()
        {
            OnStart(PartModule.StartState.Editor);

            // Need to rescale everything to make it look good in the icon, but reenable otherwise OnStart won't get called again.
            isEnabled = enabled = true;

            return base.GetInfo();
        }

        [SerializeField]
        private bool symmetryClone = false;

        public override void OnStart(PartModule.StartState state)
        {
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
                    shape.Update();

                    UpdateTexture();

                    isEnabled = enabled = false;
                }

                if (HighLogic.LoadedSceneIsEditor)
                    symmetryClone = true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                isEnabled = enabled = false;
            }
        }

        public virtual void Update()
        {
            if (!HighLogic.LoadedSceneIsEditor)
                return;

            if (skipNextUpdate)
            {
                skipNextUpdate = false;
                return;
            }

            try
            {
                UpdateTexture();
                UpdateShape();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                isEnabled = enabled = false;
            }
        }

        private bool skipNextUpdate = false;

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

        public Material sidesMaterial { get; private set; }
        public Material endsMaterial { get; private set; }

        public Mesh sidesMesh { get; private set; }
        public Mesh endsMesh { get; private set; }

        private Transform partModel;

        private void InitializeObjects()
        {
            partModel = part.FindModelTransform(partModelName);

            Transform sides = part.FindModelTransform(sidesName);
            Transform ends = part.FindModelTransform(endsName);
            Transform colliderTr = part.FindModelTransform(collisionName);

            sidesMaterial = sides.renderer.material;
            endsMaterial = ends.renderer.material;

            // Instantiate meshes. The mesh method unshares any shared meshes.
            sidesMesh = sides.GetComponent<MeshFilter>().mesh;
            endsMesh = ends.GetComponent<MeshFilter>().mesh;
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
        public Mesh colliderMesh
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
                    combine[i] = new CombineInstance();
                    combine[i].mesh = meshes[i];
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
        public float aspectMin = 0f;

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

        private void InitializeTechLimits()
        {
            if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.Mode != Game.Modes.CAREER || techLimitsSerialized == null)
                return;

            List<TechLimit> techLimits;
            ObjectSerializer.Deserialize(techLimitsSerialized, out techLimits);

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

            this.diameterMax = Mathf.Min(this.diameterMax, diameterMax);
            this.diameterMin = Mathf.Max(this.diameterMin, diameterMin);
            this.lengthMax = Mathf.Min(this.lengthMax, lengthMax);
            this.lengthMin = Mathf.Max(this.lengthMin, lengthMin);
            this.volumeMax = Mathf.Min(this.volumeMax, volumeMax);
            this.volumeMin = Mathf.Max(this.volumeMin, volumeMin);
            this.aspectMax = Mathf.Min(this.aspectMax, aspectMax);
            this.aspectMin = Mathf.Max(this.aspectMin, aspectMin);
            this.allowCurveTweaking = this.allowCurveTweaking && allowCurveTweaking;

            //Debug.Log(string.Format("TechLimits applied: diameter=({0:G3}, {1:G3}) length=({2:G3}, {3:G3}) volume=({4:G3}, {5:G3}) allowCurveTweaking={6}", diameterMin, diameterMax, lengthMin, lengthMax, volumeMin, volumeMax, allowCurveTweaking));
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

        private class TextureSet
        {
            public string name;

            public bool autoScale = false;
            public bool autoWidthDivide = false;
            public float autoHeightSteps = 0f;
            public Vector2 scale = new Vector2(2f, 1f);

            public Texture sides = null;
            public Texture sidesBump = null;
            public Texture ends = null;
            public string sidesName;
            public string endsName;
            public string sidesBumpName;
        }
        private static List<TextureSet> loadedTextureSets;
        private static string[] loadedTextureSetNames;

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
            return s1.name.CompareTo(s2.name);
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
            TextureSet tex = new TextureSet();
            tex.name = textureSet;
            tex.sidesName = node.GetNode("sides").GetValue("texture");
            tex.endsName = node.GetNode("ends").GetValue("texture");
            tex.sidesBumpName = "";
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

        private static Texture FindTexture(Texture[] textures, string textureName)
        {
            foreach (Texture t in textures)
                if (t.name == textureName)
                    return t;
            return null;
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
        private void ChangeTextureScale(string name, Material material, Vector2 targetScale)
        {
            if (name != "sides")
                return;
            this.sideTextureScale = targetScale;
            oldTextureSet = null;
            UpdateTexture();
        }

        private void UpdateTexture()
        {
            if (textureSet == oldTextureSet)
                return;

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
                if (tex.sidesBump != null)
                    sidesMaterial.shader = Shader.Find("KSP/Bumped");
                else
                    sidesMaterial.shader = Shader.Find("KSP/Diffuse");

                // pt is no longer specular ever, just diffuse.
                if (endsMaterial != null)
                    endsMaterial.shader = Shader.Find("KSP/Diffuse");
            }

            // TODO: shove into config file.
            if (endsMaterial != null)
            {
                float scale = 0.93f;
                float offset = (1f / scale - 1f) / 2f;
                endsMaterial.mainTextureScale = new Vector2(scale, scale);
                endsMaterial.mainTextureOffset = new Vector2(offset, offset);
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
                    scaleUV.y *= sideTextureScale.y;
                }
            }

            // apply
            sidesMaterial.mainTextureScale = scaleUV;
            sidesMaterial.SetTexture("_MainTex", tex.sides);
            if (tex.sidesBump != null)
            {
                sidesMaterial.SetTextureScale("_BumpMap", scaleUV);
                sidesMaterial.SetTexture("_BumpMap", tex.sidesBump);
            }
            if (endsMaterial != null)
                endsMaterial.SetTexture("_MainTex", tex.ends);
        }

        #endregion

        #region Node Attachments

        private List<object> nodeAttachments = new List<object>(4);
        private Dictionary<string, Func<Vector3>> nodeOffsets = new Dictionary<string, Func<Vector3>>();

        private class NodeTransformable : TransformFollower.Transformable, PartMessagePartProxy
        {
            // leave as not serializable so will be null when deserialzied.
            private Part part;
            private AttachNode node;

            [PartMessageEvent]
            public event PartAttachNodePositionChanged NodePositionChanged;

            public NodeTransformable(Part part, AttachNode node)
            {
                this.part = part;
                this.node = node;
                PartMessageService.Register(this);
            }

            public override bool destroyed
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
                shape.Update();
            }

            foreach (AttachNode node in part.attachNodes)
                InitializeNode(node);
            if (part.attachRules.allowSrfAttach)
                InitializeNode(part.srfAttachNode);

            // Update the shape to put the nodes into their positions.
            shape.ForceNextUpdate();
            shape.Update();

            // In flight mode, discard all the transform followers because the are not required
            if (HighLogic.LoadedSceneIsFlight)
                foreach (object att in nodeAttachments)
                    shape.RemoveAttachment(att, false);
        }

        private void InitializeNode(AttachNode node)
        {
            Vector3 position = transform.TransformPoint(node.position);
            node.originalPosition = node.position *= 1.00001f;

            TransformFollower follower = TransformFollower.createFollower(partModel, position, new NodeTransformable(part, node));

            // When the object is symmetryClone, the nodes will be already in offset, not in standard offset.
            object data = shape.AddAttachment(follower, !symmetryClone);

            nodeAttachments.Add(data);
        }

        public void AddNodeOffset(string nodeId, Func<Vector3> GetOffset)
        {
            AttachNode node = part.findAttachNode(nodeId);
            if (node == null)
                throw new ArgumentException("Node " + nodeId + " is not the ID of an attachment node");

            nodeOffsets.Add(nodeId, GetOffset);
        }

        #endregion

        #region Part Attachments

        private PartAttachment parentAttachment = null;
        private LinkedList<PartAttachment> childAttachments = new LinkedList<PartAttachment>();

        private class PartAttachment
        {
            public Part child;
            public TransformFollower follower;
            public object data;

            public PartAttachment(TransformFollower follower, object data)
            {
                this.follower = follower;
                this.data = data;
            }
        }

        private class ParentTransformable : TransformFollower.Transformable
        {
            private Part root;
            private Part part;
            private AttachNode childToParent;

            public ParentTransformable(Part root, Part part, AttachNode childToParent)
            {
                this.root = root;
                this.part = part;
                this.childToParent = childToParent;
            }

            public override bool destroyed
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

        [PartMessageListener(typeof(PartChildAttached), scenes: GameSceneFilter.AnyEditor)]
        private void PartChildAttached(Part child)
        {
            AttachNode node = child.findAttachNodeByPart(part);
            if (node == null)
            {
                Debug.LogError("*ST* unable to find child node for child: " + child.transform);
                return;
            }
            Vector3 position = child.transform.TransformPoint(node.position);

            // Handle node offsets
            if (child.attachMode != AttachModes.SRF_ATTACH)
            {
                AttachNode ourNode = part.findAttachNodeByPart(child);
                if (ourNode == null)
                {
                    Debug.LogError("*ST* unable to find our node for child: " + child.transform);
                    return;
                }
                Func<Vector3> Offset;
                if (nodeOffsets.TryGetValue(ourNode.id, out Offset))
                    position -= Offset();
            }

            //Debug.LogWarning("Attaching to parent: " + part + " child: " + child.transform.name);

            PartAttachment attach = AddPartAttachment(position, new TransformFollower.TransformTransformable(child.transform, node.position));
            attach.child = child;

            childAttachments.AddLast(attach);

            shape.ForceNextUpdate();
        }

        [PartMessageListener(typeof(PartChildDetached), scenes: GameSceneFilter.AnyEditor)]
        private void PartChildDetached(Part child)
        {
            for (var node = childAttachments.First; node != null; node = node.Next )
                if (node.Value.child == child)
                {
                    RemovePartAttachment(node.Value);
                    childAttachments.Remove(node);
                    //Debug.LogWarning("Detaching from: " + part + " child: " + child.transform.name);
                    return;
                }
            Debug.LogWarning("*ST* Message recieved removing child, but can't find child");
        }

        [PartMessageListener(typeof(PartParentChanged), scenes: GameSceneFilter.AnyEditor)]
        private void PartParentChanged(Part newParent)
        {
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
            
            Func<Vector3> Offset;
            if (nodeOffsets.TryGetValue(childToParent.id, out Offset))
                position -= Offset();

            Part root = EditorLogic.SortedShipList[0];

            //Debug.LogWarning("Attaching: " + part + " to new parent: " + newParent + " node:" + childToParent.id + " position=" + childToParent.position.ToString("G3"));

            // we need to delta this childAttachment down so that when the translation from the parent reaches here i ends in the right spot
            parentAttachment = AddPartAttachment(position, new ParentTransformable(root, part, childToParent));
            parentAttachment.child = newParent;

            // for symetric attachments, seems required. Don't know why.
            shape.ForceNextUpdate();
        }

        private PartAttachment AddPartAttachment(Vector3 position, TransformFollower.Transformable target, bool normalized = false)
        {
            TransformFollower follower = TransformFollower.createFollower(partModel, position, target);
            object data = shape.AddAttachment(follower, normalized);

            return new PartAttachment(follower, data);
        }

        private void RemovePartAttachment(PartAttachment delete)
        {
            shape.RemoveAttachment(delete.data);
            Destroy(delete.follower.gameObject);
        }

        #endregion

        #region Model Attachments

        private class ModelAttachment
        {
            public Transform child;
            public object data;
        }

        private LinkedList<ModelAttachment> attachments = new LinkedList<ModelAttachment>();

        /// <summary>
        /// Attach a gameObject to the surface of the part. The object must have a transform that is a child of the part.
        /// </summary>
        public void AddAttachment(Transform child, bool normalized)
        {
            AddAttachment(child, Vector3.zero, normalized);
        }

        public void AddAttachment(Transform child, Vector3 offset, bool normalized)
        {
            ModelAttachment attach = new ModelAttachment();

            attach.child = child;
            Vector3 position = child.TransformPoint(offset);

            TransformFollower follower = TransformFollower.createFollower(partModel, position, new TransformFollower.TransformTransformable(child, position, Space.World));
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
            TransformFollower follower = shape.RemoveAttachment(node.Value.data);
            Destroy(follower);

            attachments.Remove(node);
        }

        #endregion

        #region Tank shape

        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Shape"), UI_ChooseOption(scene = UI_Scene.Editor)]
        public string shapeName;
        private string oldShapeName = "****";

        private ProceduralAbstractShape shape;
        private Dictionary<string, ProceduralAbstractShape> availableShapes = new Dictionary<string, ProceduralAbstractShape>();

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
        }

        #endregion

    }
}