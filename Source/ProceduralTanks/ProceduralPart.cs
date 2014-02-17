using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;

public class ProceduralPart : PartModule
{    
    #region Initialization

    public void Awake()
    {
        InitializeObjects();

        if (HighLogic.LoadedScene == GameScenes.LOADING)
            LoadTextureSets();
        InitializeTextureSet();
    }

    public override void OnLoad(ConfigNode node)
    {
        // Load stuff from config files
        try
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
                LoadTechLimits(node);
        }
        catch(Exception ex)
        {
            Debug.LogError("OnLoad exception: " + ex);
            throw ex;
        }
    }

    [SerializeField]
    private bool symmetryClone = false;

    public override void OnStart(PartModule.StartState state)
    {
        // Update internal state
        try
        {
            InitializeTechLimits();
            InitializeShapes();

            if (HighLogic.LoadedSceneIsEditor)
                InitializeNodes();

            symmetryClone = true;

            if (!HighLogic.LoadedSceneIsEditor)
            {
                // Force the first update, then disable.
                Update();
                isEnabled = enabled = false;
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            isEnabled = enabled = false;
        }
    }

    public virtual void Update()
    {
        if (skipNextUpdate)
        {
            skipNextUpdate = false;
            return;
        }

        try
        {
            UpdateTexture();
            UpdateShape();
            UpdateAttachedParts();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            isEnabled = enabled = false;
        }
    }

    private bool skipNextUpdate = false;

    public void SkipNextUpdate()
    {
        if (skipNextUpdate)
            return;

        skipNextUpdate = true;
        shape.SkipNextUpdate();
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

    #region Maximum dimensions

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

    [KSPField]
    public float diameterLargeStep = 1.25f;

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

    [KSPField]
    public float lengthLargeStep = 1.0f;

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
    public float volumeMin = 0.01f;

    [SerializeField]
    private byte[] techLimitsSerialized;

    private void LoadTechLimits(ConfigNode node)
    {
        if (HighLogic.LoadedScene != GameScenes.LOADING)
            return;

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

        diameterMax = 0;
        diameterMin = float.PositiveInfinity;
        lengthMax = 0;
        lengthMin = float.PositiveInfinity;
        volumeMax = 0;
        volumeMin = float.PositiveInfinity;

        foreach (TechLimit limit in techLimits)
        {
            if (ResearchAndDevelopment.GetTechnologyState(limit.TechRequired) != RDTech.State.Available)
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

        Debug.Log(string.Format("TechLimits applied: diameter=({0:G3}, {1:G3}) length=({2:G3}, {3:G3}) volume=({4:G3}, {5:G3})", diameterMin, diameterMax, lengthMin, lengthMax, volumeMin, volumeMax));
    }

    [Serializable]
    public class TechLimit : IConfigNode
    {
        [Persistent]
        public string TechRequired;
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

        public void Load(ConfigNode node)
        {
            ConfigNode.LoadObjectFromConfig(this, node);
        }
        public void Save(ConfigNode node)
        {
            ConfigNode.CreateConfigFromObject(this, node);
        }

        public override string ToString()
        {
            return string.Format("TechLimits(TechRequired={6} diameter=({0:G3}, {1:G3}) length=({2:G3}, {3:G3}) volume=({4:G3}, {5:G3}) )", diameterMin, diameterMax, lengthMin, lengthMax, volumeMin, volumeMax, TechRequired);
        }
    }

    #endregion

    #region Texture Sets

    [KSPField(guiName = "Texture", guiActive = false, guiActiveEditor = true, isPersistant = true), UI_ChooseOption()]
    public string textureSet;
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
        print("*ST* Loading texture sets");
        foreach (ConfigNode texInfo in GameDatabase.Instance.GetConfigNodes("STRETCHYTANKTEXTURES"))
            for (int i = 0; i < texInfo.nodes.Count; i++)
            {
                TextureSet textureSet = LoadTextureSet(texInfo.nodes[i]);

                if(textureSet != null)
                    loadedTextureSets.Add(textureSet);
            }

        if(loadedTextureSets.Count == 0)
            print("*ST* No Texturesets found!");

        loadedTextureSetNames = new string[loadedTextureSets.Count];
        for (int i = 0; i < loadedTextureSets.Count; ++i)
            loadedTextureSetNames[i] = loadedTextureSets[i].name;
    }

    private static TextureSet LoadTextureSet(ConfigNode node) 
    {
        string textureSet = node.name;

        // Sanity check
        if (node.GetNode("sides") == null || node.GetNode("ends") == null)
        {
            print("*ST* Invalid Textureset " + textureSet);
            return null;
        }
        if (!node.GetNode("sides").HasValue("texture") || !node.GetNode("ends").HasValue("texture"))
        {
            print("*ST* Invalid Textureset " + textureSet);
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
        foreach (Texture t in textures)
        {
            if (t.name.Equals(tex.sidesName))
            {
                tex.sides = t;
            }
            else if (t.name.Equals(tex.sidesBumpName))
            {
                tex.sidesBump = t;
            }
            else if (t.name.Equals(tex.endsName))
            {
                tex.ends = t;
            }
        }
        if (tex.sides == null || tex.ends == null)
        {
            print("*ST* Textures not found for " + textureSet);
            return null;
        }

        return tex;
    }

    private void InitializeTextureSet()
    {
        BaseField field = Fields["textureSet"];
        UI_ChooseOption range = (UI_ChooseOption)field.uiControlEditor;

        range.options = loadedTextureSetNames;
        if(textureSet == null || !loadedTextureSetNames.Contains(textureSet))
            textureSet = loadedTextureSetNames[0];
    }

    [SerializeField]
    private Vector2 tankTextureScale = Vector2.one;

    public void UpdateTankTextureScale(Vector2 tankTextureScale)
    {
        this.tankTextureScale = tankTextureScale;
        oldTextureSet = null;
        UpdateTexture();
    }

    private void UpdateTexture()
    {
        if (textureSet == oldTextureSet)
            return;

        int newIdx = loadedTextureSets.FindIndex(set => set.name == textureSet);
        if(newIdx < 0) {
            print("*ST* Unable to find texture set: " + textureSet);
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
            if(endsMaterial != null)
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
            // default is to scale x4, will divide out again.
            scaleUV.x = (float)Math.Round(scaleUV.x * tankTextureScale.x / 4.0f);
            if (scaleUV.x < 1)
                scaleUV.x = 1;
            if (tex.autoWidthDivide)
            {
                if (tex.autoHeightSteps > 0)
                    scaleUV.y = (float)Math.Ceiling(scaleUV.y * tankTextureScale.y / scaleUV.x * (1f / tex.autoHeightSteps)) * tex.autoHeightSteps;
                else
                    scaleUV.y *= tankTextureScale.y / scaleUV.x;
            }
            else
            {
                scaleUV.y *= tankTextureScale.y;
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
        if(endsMaterial != null)
            endsMaterial.SetTexture("_MainTex", tex.ends);
    }

    #endregion
    
    #region Node Attachments

    private List<object> nodeAttachments = new List<object>(4);

    private class NodeTransformable : TransformFollower.Transformable
    {
        // leave as not serializable so will be null when deserialzied.
        private Part part;
        private AttachNode node;

        public NodeTransformable(Part part, AttachNode node)
        {
            this.part = part;
            this.node = node;
        }

        public override bool destroyed
        {
            get { return node == null; }
        }

        public override void Translate(Vector3 translation)
        {
            translation = part.transform.InverseTransformDirection(translation);
            node.position += translation;
        }

        public override void Rotate(Quaternion rotate)
        {
            Vector3 oldWorldOffset = part.transform.TransformDirection(node.position);
            Vector3 newWorldOffset = rotate * oldWorldOffset;
            node.position = part.transform.InverseTransformDirection(newWorldOffset);

            Vector3 oldOrientationWorld = part.transform.TransformDirection(node.orientation);
            Vector3 newOrientationWorld = rotate * oldOrientationWorld;
            node.orientation = part.transform.InverseTransformDirection(newOrientationWorld);
        }
    }

    private void InitializeNodes()
    {
        // When the object is symmetryClone, the nodes will be already in offset, not in standard offset.
        foreach (AttachNode node in part.attachNodes)
            InitializeNode(node);
        if (part.attachRules.allowSrfAttach)
            InitializeNode(part.srfAttachNode);
    }

    private void InitializeNode(AttachNode node)
    {
        if (symmetryClone)
        {
            // for some reason KSP resets the node positions after deserializing, but *after* start is called. 
            // Will undo this issue.
            node.originalPosition = node.position;
            node.originalOrientation = node.orientation;
        }
        Vector3 position = transform.position + transform.TransformDirection(node.position);


        TransformFollower follower = TransformFollower.createFollower(partModel, position, new NodeTransformable(part, node));
        object data = shape.AddAttachment(follower, !symmetryClone);

        nodeAttachments.Add(data);
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
            int siblings = part.symmetryCounterparts == null ? 1 : (part.symmetryCounterparts.Count + 1);
            root.transform.Translate(trans / siblings, Space.World);
            // we need to translate this childAttachment away so that when the translation from the parent reaches here it ends in the right spot
            part.transform.Translate(-trans, Space.World);
        }

        public override void Rotate(Quaternion rotate)
        {
            // Apply the inverse rotation to the part itself
            rotate = rotate.Inverse();

            Vector3 oldWorldOffset = part.transform.TransformDirection(childToParent.position);
            Vector3 newWorldOffset = rotate * oldWorldOffset;

            part.transform.rotation = rotate * part.transform.rotation;

            childToParent.position = part.transform.InverseTransformDirection(newWorldOffset);
            part.transform.Translate(oldWorldOffset - newWorldOffset, Space.World);

        }
    }

    private void UpdateAttachedParts()
    {
        // Update parent attachements.
        // Explain logic: 
        //     xor nulls -> need to update
        //   then either both are null or both not so:
        //     both null (which means one is null) -> no need
        //     transforms discrepant -> need to update  (can this case actually happen?)
        if ((part.parent == null) != (parentAttachment == null) || (part.parent != null && part.parent != parentAttachment.child))
        {
            //Detach the old parent
            if (parentAttachment != null)
            {
                RemovePartAttachment(parentAttachment);
                //print("*ST* Removing old parent childAttachment:" + parentAttachment.child);

                // Fix bug in KSP, if we don't do this then findAttachNodeByPart will return the surface child node next time
                if (part.attachMode == AttachModes.SRF_ATTACH)
                    part.srfAttachNode.attachedPart = null;
            }

            if (part.parent == null)
            {
                parentAttachment = null;
            }
            else
            {
                Part parent = part.parent;
                AttachNode childToParent;

                childToParent = part.findAttachNodeByPart(parent);
                if (childToParent == null)
                {
                    print("*ST* unable to find child node from parent: " + part.transform);
                    return;
                }
                print("Attaching new parent: " + parent + " to " + childToParent.id);

                Vector3 position = transform.position + transform.TransformDirection(childToParent.position);
                Part root = EditorLogic.SortedShipList[0];

                // we need to translate this childAttachment down so that when the translation from the parent reaches here i ends in the right spot
                parentAttachment = AddPartAttachment(position, new ParentTransformable(root, part, childToParent));
                parentAttachment.child = parent;

                // for symetric attachments, seems required. Don't know why.
                shape.ForceNextUpdate();
            }
        }

        LinkedListNode<PartAttachment> node = childAttachments.First;
        foreach (Part child in part.children)
        {
            while (node != null && node.Value.child != child)
            {
                // node has been removed
                RemoveChildPartAttachment(ref node);
            }
            if (node == null)
            {
                // Node has been attached.
                AddChildPartAttachment(child);
            }
            else if (node.Value.child == child)
            {
                node = node.Next; 
            }
        }
        while (node != null)
        {
            RemoveChildPartAttachment(ref node);
        }
    }

    private void RemoveChildPartAttachment(ref LinkedListNode<PartAttachment> node)
    {
        LinkedListNode<PartAttachment> delete = node;
        node = node.Next;

        RemovePartAttachment(delete.Value);
        childAttachments.Remove(delete);
        //print("*ST* Child childAttachment removed: " + delete.Value.child);
    }

    private PartAttachment AddChildPartAttachment(Part child)
    {
        AttachNode node = child.findAttachNodeByPart(part);

        if (node == null)
        {
            print("*ST* unable to find child node for child: " + child.transform);
            return null;
        }

        Vector3 worldOffset = child.transform.TransformDirection(node.position);
        PartAttachment attach = AddPartAttachment(child.transform.position + worldOffset, new TransformFollower.TransformTransformable(child.transform, node.position));
        attach.child = child;

        childAttachments.AddLast(attach);
        print("*ST* Attaching child childAttachment: " + child.transform.name + " from child node " + node.id + " Offset=" + node.position.ToString("F3"));
        
        return attach;
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
        string path = part.transform.PathToDecendant(child);

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

    [KSPField(isPersistant=true, guiActiveEditor=true, guiActive = false, guiName = "Shape"), UI_ChooseOption()]
    public string shapeName;
    private string oldShapeName = "****";

    private ProceduralAbstractShape shape;
    private Dictionary<string, ProceduralAbstractShape> availableShapes = new Dictionary<string, ProceduralAbstractShape>();

    private void InitializeShapes()
    {
        List<string> shapeNames = new List<string>(); 
        foreach (ProceduralAbstractShape compShape in GetComponents<ProceduralAbstractShape>())
        {
            if (!string.IsNullOrEmpty(compShape.techRequired) && ResearchAndDevelopment.GetTechnologyState(compShape.techRequired) != RDTech.State.Available)
                goto disableShape;

            // Force the first update so that things can get attached.
            if(HighLogic.LoadedSceneIsEditor)
                compShape.Update();

            availableShapes.Add(compShape.displayName, compShape);
            shapeNames.Add(compShape.displayName);

            if (string.IsNullOrEmpty(shapeName)?(availableShapes.Count == 1):compShape.displayName == shapeName)
            {
                shape = compShape;
                oldShapeName = shapeName = shape.displayName;
                shape.isEnabled = shape.enabled = true;
                continue;
            }

        disableShape:
            if (compShape.enabled)
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

        // Force the first update in flight mode since it won't have happened above.
        if (HighLogic.LoadedSceneIsFlight)
            shape.Update();
    }

    private void UpdateShape()
    {
        if (shapeName == oldShapeName)
            return;

        ProceduralAbstractShape newShape;
        if (!availableShapes.TryGetValue(shapeName, out newShape))
        {
            print("*ST* Unable to find compShape: " + shapeName);
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
