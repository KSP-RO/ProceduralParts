using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using KSPAPIExtensions;

public class ProceduralTank : PartModule
{    
    #region Initialization

    public override void OnAwake()
    {
        if (HighLogic.LoadedSceneIsEditor)
            InitializeTechLimits();
    }

    public override void OnLoad(ConfigNode node)
    {
        // Load stuff from config files
        try
        {
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                LoadTechLimits(node);
                LoadTextureSets();
            }
        }
        catch(Exception ex)
        {
            Debug.LogError("OnLoad exception: " + ex);
            throw ex;
        }
    }

    [SerializeField]
    private bool deserialized = false;

    public override void OnStart(PartModule.StartState state)
    {
        // Update internal state
        try
        {
            initializeObjects();
            initializeTextureSet();
            initializeShapes();
            if (HighLogic.LoadedSceneIsEditor)
                initializeNodes();

            deserialized = true;
        }
        catch (Exception ex)
        {
            print(ex);
            enabled = false;
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
            if (HighLogic.LoadedSceneIsEditor)
                updateShape();

            updateTexture();

            if (HighLogic.LoadedSceneIsEditor)
                updatePartsAttached();
        }
        catch (Exception ex)
        {
            print(ex);
            enabled = false;
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
    public string tankModelName = "stretchyTank";

    [KSPField]
    public string sidesName = "sides";

    [KSPField]
    public string endsName = "ends";

    [KSPField]
    public string collisionName = "collisionMesh";

    protected Material sidesMaterial { get; private set; }
    protected Material endsMaterial { get; private set; }

    public Mesh sidesMesh { get; private set; }
    public Mesh endsMesh { get; private set; }

    private Transform tankModel;

    private void initializeObjects()
    {
        tankModel = part.FindModelTransform(tankModelName);

        Transform sides = part.FindModelTransform(sidesName);
        Transform ends = part.FindModelTransform(endsName);
        Transform colliderTr = part.FindModelTransform(collisionName);

        sidesMaterial = sides.renderer.material;
        endsMaterial = ends.renderer.material;

        // Instantiate meshes. The mesh method unshares any shared meshes.
        sidesMesh = sides.GetComponent<MeshFilter>().mesh;
        endsMesh = ends.GetComponent<MeshFilter>().mesh;
        tankCollider = colliderTr.GetComponent<MeshCollider>();

        // Will need to destroy any old transform position followers, they will be rebuilt in due course
        if (deserialized)
            foreach (TransformFollower follower in tankModel.GetComponentsInChildren<TransformFollower>())
                Destroy(follower);
    }

    #endregion

    #region Collider mesh management methods

    private MeshCollider tankCollider;

    // The tankCollider mesh. This must be called whenever the contents of the mesh changes, even if the object remains the same.
    public Mesh colliderMesh
    {
        get
        {
            return tankCollider.sharedMesh;
        }
        set
        {
            if (ownColliderMesh)
            {
                Destroy(tankCollider.sharedMesh);
                ownColliderMesh = false;
            }
            tankCollider.sharedMesh = value;
            tankCollider.enabled = false;
            tankCollider.enabled = true;
        }
    }

    [SerializeField]
    private bool ownColliderMesh = true;

    /// <summary>
    /// Call by base classes to update the tankCollider mesh.
    /// </summary>
    /// <param name="meshes">List of meshes to set the tankCollider to</param>
    public void SetColliderMeshes(params Mesh[] meshes)
    {
        if (ownColliderMesh)
            Destroy(tankCollider.sharedMesh);

        if (meshes.Length == 1)
        {
            tankCollider.sharedMesh = meshes[0];
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
            tankCollider.sharedMesh = colliderMesh;
            ownColliderMesh = true;
        }

        // If we don't do this, the tankCollider doesn't work properly.
        tankCollider.enabled = false;
        tankCollider.enabled = true;
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

        MemoryStream stream = new MemoryStream();
        using (stream)
        {
            BinaryFormatter fmt = new BinaryFormatter();
            fmt.Serialize(stream, techLimits);
        }
        techLimitsSerialized = stream.ToArray();
    }

    private void InitializeTechLimits() 
    {
        if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.Mode != Game.Modes.CAREER || techLimitsSerialized == null)
            return;

        List<TechLimit> techLimits;
        using (MemoryStream stream = new MemoryStream(techLimitsSerialized))
        {
            BinaryFormatter fmt = new BinaryFormatter();
            techLimits = (List<TechLimit>)fmt.Deserialize(stream);
        }

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

    private void initializeTextureSet()
    {
        BaseField field = Fields["textureSet"];
        UI_ChooseOption range = (UI_ChooseOption)field.uiControlEditor;

        range.options = loadedTextureSetNames;
        if(textureSet == null || !loadedTextureSetNames.Contains(textureSet))
            textureSet = loadedTextureSetNames[0];
    }

    private Vector2 tankTextureScale = Vector2.one;

    public void UpdateTankTextureScale(Vector2 tankTextureScale)
    {
        this.tankTextureScale = tankTextureScale;
        oldTextureSet = null;
        updateTexture();
    }

    private void updateTexture()
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
    
    #region Attachments

    private List<object> nodeAttachments = new List<object>(4);

    private void initializeNodes()
    {
        // When the object is deserialized, the nodes will be already in position, not in standard position.
        foreach (AttachNode node in part.attachNodes)
            initializeNode(node);
        if (part.attachRules.allowSrfAttach)
            initializeNode(part.srfAttachNode);
    }

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

    private void initializeNode(AttachNode node)
    {
        if (deserialized)
        {
            // for some reason KSP resets the node positions after deserializing, but *after* start is called. 
            // Will undo this issue.
            node.originalPosition = node.position;
            node.originalOrientation = node.orientation;
        }
        Vector3 position = transform.position + transform.TransformDirection(node.position);
        PartAttachment att = addTankOrBellAttachment(position, new NodeTransformable(part, node), !deserialized);
        nodeAttachments.Add(att.data);
    }

    private class PartAttachment
    {
        public Part child;
        public TransformFollower follower;
        public TransformFollower srbFollower;
        public object data;

        public PartAttachment(TransformFollower follower, TransformFollower srbFollower, object data)
        {
            this.follower = follower;
            this.srbFollower = srbFollower;
            this.data = data;
        }
    }


    private PartAttachment parentAttachment = null;
    private LinkedList<PartAttachment> childAttachments = new LinkedList<PartAttachment>();

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

    private void updatePartsAttached()
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
                removeTankOrBellAttachment(parentAttachment);
                //print("*ST* Removing old parent childAttachment:" + parentAttachment.child);

                // Fix bug in KSP, if we don't do this then findAttachNodeByPart will return the surface attach node next time
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
                    print("*ST* unable to find attach node from parent: " + part.transform);
                    return;
                }
                print("Attaching new parent: " + parent + " to " + childToParent.id);

                Vector3 position = transform.position + transform.TransformDirection(childToParent.position);
                Part root = EditorLogic.SortedShipList[0];

                // we need to translate this childAttachment down so that when the translation from the parent reaches here i ends in the right spot
                parentAttachment = addTankOrBellAttachment(position, new ParentTransformable(root, part, childToParent));
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
                removeChildPartAttachment(ref node);
            }
            if (node == null)
            {
                // Node has been attached.
                addChildPartAttachment(child);
            }
            else if (node.Value.child == child)
            {
                node = node.Next; 
            }
        }
        while (node != null)
        {
            removeChildPartAttachment(ref node);
        }
    }

    private void removeChildPartAttachment(ref LinkedListNode<PartAttachment> node)
    {
        LinkedListNode<PartAttachment> delete = node;
        node = node.Next;

        removeTankOrBellAttachment(delete.Value);
        childAttachments.Remove(delete);
        //print("*ST* Child childAttachment removed: " + delete.Value.child);
    }

    private PartAttachment addChildPartAttachment(Part child)
    {
        AttachNode node = child.findAttachNodeByPart(part);

        if (node == null)
        {
            print("*ST* unable to find attach node for child: " + child.transform);
            return null;
        }

        Vector3 worldOffset = child.transform.TransformDirection(node.position);
        PartAttachment attach = addTankOrBellAttachment(child.transform.position + worldOffset, new TransformFollower.TransformTransformable(child.transform, node.position));
        attach.child = child;

        childAttachments.AddLast(attach);
        print("*ST* Attaching child childAttachment: " + child.transform.name + " from child node " + node.id + (attach.srbFollower==null?"To Tank":"To SRB") + " Offset=" + node.position.ToString("F3"));
        
        return attach;
    }

    private PartAttachment addTankOrBellAttachment(Vector3 position, TransformFollower.Transformable target, bool normalized = false)
    {
        TransformFollower srbFollower = null;
        /*
        if (isSRB)
        {
            // Check if attached to tank or to SRB nozzle
            Vector3 childPos = position - transform.position;
            Vector3 thrustPos = srbBell.position - transform.position;
            Vector3 thrustVector = srbBell.up;

            float thrustFromOrigin = Vector3.Dot(thrustVector, childPos);
            float attachFromOrigin = Vector3.Dot(thrustVector, thrustPos);

            if (attachFromOrigin > thrustFromOrigin)
            {
                // If we have an SRB attachment, we need to account for both the movement with respect to the tank
                // and also the movement with respect to the SRB.

                // put the srb follower at the actual position.
                srbFollower = TransformFollower.createFollower(srbBell, position, target);
                
                // The follower for the tank childAttachment is in the position of the srb attachment
                position = srbBell.position;
            }
        }
        */

        TransformFollower follower = TransformFollower.createFollower(tankModel, position, target);
        object data = shape.AddTankAttachment(follower, normalized);

        return new PartAttachment(follower, srbFollower, data);
    }

    private void removeTankOrBellAttachment(PartAttachment delete)
    {
        shape.RemoveTankAttachment(delete.data);
        Destroy(delete.follower.gameObject);
        if (delete.srbFollower != null)
            Destroy(delete.srbFollower.gameObject);
    }

    #endregion

    #region Tank shape

    [KSPField(isPersistant=true, guiActiveEditor=true, guiActive = false, guiName = "Shape"), UI_ChooseOption()]
    public string shapeName;
    private string oldShapeName = "****";

    private ProceduralTankShape shape;
    private Dictionary<string, ProceduralTankShape> availableShapes = new Dictionary<string, ProceduralTankShape>();

    private void initializeShapes()
    {
        List<string> shapeNames = new List<string>(); 
        foreach (ProceduralTankShape compShape in GetComponents<ProceduralTankShape>())
        {
            if (!string.IsNullOrEmpty(compShape.techRequired) && ResearchAndDevelopment.GetTechnologyState(compShape.techRequired) != RDTech.State.Available)
            {
                compShape.enabled = false;
                continue;
            }

            availableShapes.Add(compShape.displayName, compShape);
            shapeNames.Add(compShape.displayName);
            if (string.IsNullOrEmpty(shapeName)?compShape.enabled:compShape.displayName == shapeName)
            {
                shape = compShape;
                oldShapeName = shapeName = shape.displayName;
            }
            else if (!compShape.enabled)
            {
                // guiActiveEditor doesn't properly serialize, so call OnDisable again.
                compShape.OnDisable();
            }
            else
                compShape.enabled = false;
        }
        
        // last ditch effort
        if (string.IsNullOrEmpty(shapeName) || shape == null)
        {
            shape = GetComponent<ProceduralTankShape>();
            if (shape == null)
                throw new InvalidProgramException("No stretchy tank shapes available for this tank");
            oldShapeName = shapeName = shape.displayName;
        }

        BaseField field = Fields["shapeName"];
        if (shapeNames.Count == 1)
        {
            field.guiActiveEditor = false;
        }
        else
        {
            field.guiActiveEditor = true;
            UI_ChooseOption range = (UI_ChooseOption)field.uiControlEditor;
            range.options = shapeNames.ToArray();
        }

        shape.enabled = true;
        shape.ForceNextUpdate();
        // Call update now to allow setting the node positions in non-normalized position.
        if(deserialized)
            shape.Update();
    }

    private void updateShape()
    {
        if (shapeName == oldShapeName)
            return;

        ProceduralTankShape newShape;
        if (!availableShapes.TryGetValue(shapeName, out newShape))
        {
            print("*ST* Unable to find compShape: " + shapeName);
            shapeName = oldShapeName;
            return;
        }

        if (shape != null)
        {
            shape.enabled = false;

            // Pull off all the attachments, resetting them to standard position, then reattach in the new shape.
            for (int i = 0; i < nodeAttachments.Count; ++i)
            {
                TransformFollower follower = shape.RemoveTankAttachment(nodeAttachments[i], true);
                nodeAttachments[i] = newShape.AddTankAttachment(follower, true);
            }
            if (parentAttachment != null)
            {
                shape.RemoveTankAttachment(parentAttachment.data, true);
                parentAttachment.data = newShape.AddTankAttachment(parentAttachment.follower, true);
            }
            foreach (PartAttachment childAttachment in childAttachments)
            {
                shape.RemoveTankAttachment(childAttachment.data, true);
                childAttachment.data = newShape.AddTankAttachment(childAttachment.follower, true);
            }
        }

        shape = newShape;
        shape.enabled = true;

        oldShapeName = shapeName;
    }

    #endregion

}
