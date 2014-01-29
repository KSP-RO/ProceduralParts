using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSPAPIExtensions;

public abstract class AbstractStretchyTank : PartModule
{    
    #region Initialization

    public override void OnAwake()
    {
        base.OnAwake();

        // Load stuff from config files
        try
        {
            loadTextureSets();
        }
        catch (Exception ex)
        {
            print("OnAwake exception: " + ex);
            throw ex;
        }
    }

    public override void OnInitialize()
    {
        base.OnInitialize();
        try
        {
            if (isSRB && part.Modules.Contains("ModuleEngineConfigs"))
                changeThrust();
        }
        catch(Exception ex)
        {
            print("OnInitialize exception: " + ex);
            throw ex;
        }
    }

    public override void OnLoad(ConfigNode node)
    {
        try {
            if (isSRB && part.Modules.Contains("ModuleEngineConfigs"))
                changeThrust();
        }
        catch(Exception ex)
        {
            print("OnLoad exception: " + ex);
            throw ex;
        }
        print("*ST* OnLoad");
    }

    public override void OnStart(PartModule.StartState state)
    {
        // Update internal state

        // Note: need to be quite careful about the order or initialization / attaching things on loading up
        // 1. Pieces internal to the part (eg attach nodes and the bell) are not stored in the config file, so they need to be attached 
        //    in OnStart. Attach nodes aren't used in flight, so that can be skipped.
        // 2. The initial rescaling of the model can then occur in the tail of OnStart in the base class
        // 3. The next update needs to be skipped, as the SRB will be moved to the correct position by the TransformPositionFollowers
        //    Failing to do this will result in things being attached to the SRB, when they should be attached to the tank if they're 
        //    low down.
        // 4. Child and parent part positions are stored in the config, so they need to be reattached in the next update cycle 
        //    This is only required in the VAB, as they don't move in flight.

        // Base classes are responsible for making sure this fixed order happens.

        initializeTextureSet();
        initializeSRB();
        if (HighLogic.LoadedSceneIsEditor)
        {
            initializeMaxDimensions();
            initializeNodes();
        }
    }

    public virtual void Update()
    {
        updateTexture();
        updateMass();
        updateSRB();

        if(HighLogic.LoadedSceneIsEditor)
            updatePartsAttached();
    }

    #endregion

    #region Maximum dimensions

    /// <summary>
    /// Maximum radial diameter in meters
    /// </summary>
    public float maxRadialDiameter = 10.0f;

    /// <summary>
    /// Minimum radial diameter in meters
    /// </summary>
    public float minRadialDiameter = 0.05f;

    /// <summary>
    /// Maximum length in meters
    /// </summary>
    public float maxLength = 10.0f;

    /// <summary>
    /// Minimum length in meters
    /// </summary>
    public float minLength = 0.05f;

    public void initializeMaxDimensions()
    {
        if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.Mode != Game.Modes.CAREER) {
            maxRadialDiameter = 10.0f;
            minRadialDiameter = 0.05f;
            maxLength = 10.0f;
            minLength = 0.05f;
            return;
        }

        foreach(ConfigNode tech in GameDatabase.Instance.GetConfigNodes("STRETCHYTANKTECH")) {
            
        }

        // Deprecated old style config
        foreach (ConfigNode tech in GameDatabase.Instance.GetConfigNodes("STRETCHYTANKMAXRAD"))
        {
            maxRadialDiameter = 0.01f;
            for (int i = 0; i < tech.values.Count; i++)
            {
                var value = tech.values[i];
                if (value.name.Equals("name"))
                    continue;
                float r = float.Parse(value.value);
                if (r < maxRadialDiameter) continue;
                if (ResearchAndDevelopment.GetTechnologyState(value.name) != RDTech.State.Available) continue;
                maxRadialDiameter = r;
            }
        }


    }
    #endregion

    #region Tank Volume

    [KSPField]
    public float volMultiplier = 1.0f; // for MFS

    /// <summary>
    /// Volume of tank in kilolitres. This needs to be set by base classes when it changes
    /// </summary>
    [KSPField(guiActive = false, guiActiveEditor = true, guiName = "Volume", guiFormat = "F3", guiUnits = "kL")]
    public float tankVolume = 1.0f;
    private float oldTankVolume = 1.0f;

    [KSPField(guiActive = false, guiActiveEditor = true, guiName = "Dry Mass", guiFormat = "F2", guiUnits = "t")]
    public float dryMass = 1.0f;

    /// <summary>
    /// Tank dry density in ton per litre
    /// </summary>
    public virtual float tankTypeDryDensity
    {
        get
        {
            switch (tankType)
            {
                case "*Mixed":
                case "MonoPropellant":
                    return 0.1089f;
                case "LiquidFuel":
                    return 0.0450f;
                case "Oxidizer":
                    return 0.0815f;
                case "SolidFuel":
                    // NK add solid fuel, dry mass = 0.0015 per unit, or 1:6 given SF's mass of 0.0075t per unit
                    return 1.5f;
                case "*Structural":
                    // Dry mass of "Structural Fuselage" divided by volume gives us a mass higher than anything
                    // This really doesn't make any sense as you could just empty out a liquid fuel tank. 
                    return 0.0400f; //0.1738f;
            }
            return 1f;
        }
    }

    /// <summary>
    /// Call whenever tank's mass could change - with changing size or changing resources.
    /// Subclasses should set tankVolume on volume changes, rather than call this.
    /// </summary>
    private void updateMass()
    {
        if (part.Modules.Contains("ModuleFuelTanks"))
            return;
        if (oldTankVolume == tankVolume && oldTankType == tankType)
            return;

        dryMass = part.mass = Mathf.Round(tankTypeDryDensity * tankVolume * volMultiplier * 1000f) / 1000f;

        // TODO: breaking force
        part.breakingForce = 50f;
        part.breakingTorque = 50f;

        //float massFactor = initialDryMass * calcVolumeFactor();
        //part.breakingForce = 969.47f * Mathf.Pow(massFactor, 0.3684f);
        //part.breakingTorque = 969.47f * Mathf.Pow(massFactor, 0.3684f);

        updateResources();
        rescaleTexture();

        oldTankVolume = tankVolume;
        oldTankType = tankType;
        foreach (Part sym in part.symmetryCounterparts)
        {
            AbstractStretchyTank counterpart = sym.Modules.OfType<AbstractStretchyTank>().FirstOrDefault();
            counterpart.tankVolume = tankVolume;
            counterpart.tankType = tankType;
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

    public static void loadTextureSets()
    {
        if (loadedTextureSets != null)
            return;

        loadedTextureSets = new List<TextureSet>();
        print("*ST* Loading texture sets");
        foreach (ConfigNode texInfo in GameDatabase.Instance.GetConfigNodes("STRETCHYTANKTEXTURES"))
            for (int i = 0; i < texInfo.nodes.Count; i++)
            {
                TextureSet textureSet = loadTextureSet(texInfo.nodes[i]);

                if(textureSet != null)
                    loadedTextureSets.Add(textureSet);
            }

        if(loadedTextureSets.Count == 0)
            print("*ST* No Texturesets found!");

        loadedTextureSetNames = new string[loadedTextureSets.Count];
        for (int i = 0; i < loadedTextureSets.Count; ++i)
            loadedTextureSetNames[i] = loadedTextureSets[i].name;
    }

    private static TextureSet loadTextureSet(ConfigNode texInfo) 
    {
        string textureSet = texInfo.name;

        // Sanity check
        if (texInfo.GetNode("sides") == null || texInfo.GetNode("ends") == null)
        {
            print("*ST* Invalid Textureset " + textureSet);
            return null;
        }
        if (!texInfo.GetNode("sides").HasValue("texture") || !texInfo.GetNode("ends").HasValue("texture"))
        {
            print("*ST* Invalid Textureset " + textureSet);
            return null;
        }


        // get settings
        TextureSet tex = new TextureSet();
        tex.name = textureSet;
        tex.sidesName = texInfo.GetNode("sides").GetValue("texture");
        tex.endsName = texInfo.GetNode("ends").GetValue("texture");
        tex.sidesBumpName = "";
        if (texInfo.GetNode("sides").HasValue("bump"))
            tex.sidesBumpName = texInfo.GetNode("sides").GetValue("bump");

        if (texInfo.GetNode("sides").HasValue("uScale"))
            float.TryParse(texInfo.GetNode("sides").GetValue("uScale"), out tex.scale.x);
        if (texInfo.GetNode("sides").HasValue("vScale"))
            float.TryParse(texInfo.GetNode("sides").GetValue("vScale"), out tex.scale.y);


        if (texInfo.GetNode("sides").HasValue("autoScale"))
            bool.TryParse(texInfo.GetNode("sides").GetValue("autoScale"), out tex.autoScale);
        if (texInfo.GetNode("sides").HasValue("autoWidthDivide"))
            bool.TryParse(texInfo.GetNode("sides").GetValue("autoWidthDivide"), out tex.autoWidthDivide);
        if (texInfo.GetNode("sides").HasValue("autoHeightSteps"))
            float.TryParse(texInfo.GetNode("sides").GetValue("autoHeightSteps"), out tex.autoHeightSteps);

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

    public void initializeTextureSet()
    {
        BaseField field = Fields["textureSet"];
        UI_ChooseOption range = (UI_ChooseOption)field.uiControlEditor;

        range.options = loadedTextureSetNames;
        if(textureSet == null || !loadedTextureSetNames.Contains(textureSet))
            textureSet = loadedTextureSetNames[0];
    }

    /// <summary>
    /// Call when the textures need to be rescaled.
    /// </summary>
    public void rescaleTexture()
    {
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

        // Update symmetry counterparts
        foreach (Part sym in part.symmetryCounterparts)
        {
            AbstractStretchyTank counterpart = sym.Modules.OfType<AbstractStretchyTank>().FirstOrDefault();
            counterpart.textureSet = textureSet;
        }

        TextureSet tex = loadedTextureSets[newIdx];

        Material sidesMaterial;
        Material endsMaterial;
        Vector2 sideScale;
        GetMaterialsAndScale(out sidesMaterial, out endsMaterial, out sideScale);

        // Set shaders
        if (!part.Modules.Contains("ModulePaintable"))
        {
            if (tex.sidesBump != null)
                sidesMaterial.shader = Shader.Find("KSP/Bumped");
            else
                sidesMaterial.shader = Shader.Find("KSP/Diffuse");

            // top is no longer specular ever, just diffuse.
            endsMaterial.shader = Shader.Find("KSP/Diffuse");
        }

        // TODO: shove into config file.
        float scale = 0.93f;
        float offset = (1f/scale - 1f)/2f;
        endsMaterial.mainTextureScale = new Vector2(scale, scale);
        endsMaterial.mainTextureOffset = new Vector2(offset, offset);
        // set up UVs
        Vector2 scaleUV = tex.scale;
        if (tex.autoScale)
        {
            // default is to scale x4, will divide out again.
            scaleUV.x = (float)Math.Round(scaleUV.x * sideScale.x / 4.0f);
            if (scaleUV.x < 1)
                scaleUV.x = 1;
            if (tex.autoWidthDivide)
            {
                if (tex.autoHeightSteps > 0)
                    scaleUV.y = (float)Math.Ceiling(scaleUV.y * sideScale.y / scaleUV.x * (1f / tex.autoHeightSteps)) * tex.autoHeightSteps;
                else
                    scaleUV.y *= sideScale.y / scaleUV.x;
            }
            else
            {
                scaleUV.y *= sideScale.y;
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

    public abstract void GetMaterialsAndScale(out Material sidesMaterial, out Material endsMaterial, out Vector2 sideScale);

    #endregion

    #region SRB stuff

    [KSPField]
    public bool isSRB;

    [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "SRB Burn Time", guiFormat = "F2"), UI_FloatRange(minValue = 0.25f, maxValue = 360f, stepIncrement = 0.25f)]
    public float srbBurnTime = 60f;
    private float oldSRBBurnTime; // NK for SRBs

    [KSPField(isPersistant = false, guiName = "SRB Thrust", guiActive = false, guiActiveEditor = true, guiFormat = "F2")]
    public float srbThrust;

    [KSPField(isPersistant = false, guiName = "SRB Heat", guiActive = false, guiActiveEditor = true, guiFormat = "F2")]
    public float srbHeatProduction;

    [KSPField]
    public string srbBellName = null;

    public Transform srbBell
    {
        get { return _srbBell; }
    }
    private Transform _srbBell;

    private void initializeSRB()
    {
        if (isStructural || tankType == "*Structural")
        {
            tankType = "*Structural";
            isStructural = true;
            isSRB = false;

            // Can't change the tank type if it's structural. No point displaying volume.
            Fields["tankType"].guiActiveEditor = false;
            Fields["tankVolume"].guiActiveEditor = false;
        }
        else if (isSRB || tankType == "SolidFuel")
        {
            tankType = "SolidFuel";
            isStructural = false;
            isSRB = true;

            // Can't change the tank type if it's an SRB
            Fields["tankType"].guiActiveEditor = false;

            _srbBell = part.FindModelTransform(srbBellName);
            if (srbBell == null)
                print("*ST* Unable to find SRB Bell");

            addTankAttachment(TransformPositionFollower.createFollower(_srbBell));
        }
        else
        {
            // Disable all the SRB related controls.
            Fields["srbBurnTime"].guiActiveEditor = false;
            Fields["srbThrust"].guiActiveEditor = false;
            Fields["srbHeatProduction"].guiActiveEditor = false;
            oldSRBBurnTime = srbBurnTime = 0.0f;

            UI_ChooseOption opts = (UI_ChooseOption)Fields["tankType"].uiControlEditor;
            opts.options = new string[] { "*Mixed", "LiquidFuel", "Oxidizer", "MonoPropellant" };
            opts.display = new string[] { "Mixed", "Liquid Fuel", "Oxidizer", "MonoPropellant" };

            if (tankType == null || tankType == "")
                tankType = opts.options[0];
        }
    }

    private void updateSRB()
    {
        if (oldSRBBurnTime == srbBurnTime)
            return;
        changeThrust();

        oldSRBBurnTime = srbBurnTime;
        foreach (Part sym in part.symmetryCounterparts)
        {
            AbstractStretchyTank counterpart = sym.Modules.OfType<AbstractStretchyTank>().FirstOrDefault();
            counterpart.srbBurnTime = srbBurnTime;
        }
    }


    private void changeThrust()
    {
        if (!isSRB)
            return;

        try
        {
            ModuleEngines mE = (ModuleEngines)part.Modules["ModuleEngines"];
            PartResource solidFuel = part.Resources["SolidFuel"];
            mE.DumpObjectFields();
            srbThrust = mE.maxThrust = (float)Math.Round(mE.atmosphereCurve.Evaluate(0) * part.Resources["SolidFuel"].maxAmount * part.Resources["SolidFuel"].info.density * 9.81f / srbBurnTime, 2);
            srbHeatProduction = mE.heatProduction = (float)Math.Round((200f + 5200f / Math.Pow((srbBurnTime + 20f), 0.75f)) * 0.5f);

            if (part.Modules.Contains("ModuleEngineConfigs"))
            {

                var mEC = part.Modules["ModuleEngineConfigs"];
                if (mEC != null)
                {
                    Type engineType = mEC.GetType();
                    engineType.GetMethod("ChangeThrust").Invoke(mEC, new object[] { srbThrust });
                }
            }
        }
        catch (Exception e)
        {
            print("*ST* ChangeThrust, caught " + e.Message);
        }
    }
    #endregion
    
    #region Resources

    [KSPField(isPersistant = true, guiActive=false, guiActiveEditor=true, guiName="Tank Type"), UI_ChooseOption]
    public string tankType;
    private string oldTankType = "******";

    [KSPField]
    public bool isStructural = false;

    private void updateResources()
    {
        if (part.Modules.Contains("ModuleFuelTanks"))
            return;

        // add resources
        switch (tankType)
        {
            case "*Mixed":
                changeResource("LiquidFuel", 78.22784f * tankVolume);
                changeResource("Oxidizer", 95.6118f * tankVolume);
                break;
            case "LiquidFuel":
                changeResource("LiquidFuel", 49.9789f * tankVolume);
                break;
            case "MonoPropellant":
                changeResource("MonoPropellant", 203.718f * tankVolume);
                break;
            case "Oxidizer":
                changeResource("Oxidizer", 81.4873f * tankVolume);
                break;
            case "SolidFuel":
                // NK add Solid Fuel
                // Yields 850 for 1.5t dry mass, like BACC (because dry mass = 1.5 LF/Ox dry mass)
                // But the RT-10 has way better dry:wet ratio, so pick something inbetween: 1.2794x BACC
                changeResource("SolidFuel", 192f * tankVolume, false);
                changeThrust();
                break;
            case "*Structural":
                break;
            default:
                print("*ST*: Unknown tank type " + tankType);
                break;
        }

        // remove additional resources
        cleanResources();
        changeThrust();
    }

    private void changeResource(string resName, float amount, bool isTweakable = true)
    {
        double fillFraction = 1.0d;
        int idx = part.Resources.list.FindIndex(res => res.resourceName == resName);
        if(idx >= 0) {
            PartResource oldRes = part.Resources.list[idx];
            part.Resources.list.RemoveAt(idx);
            fillFraction = oldRes.amount / oldRes.maxAmount;
            DestroyImmediate(oldRes);
        }

        ConfigNode node = new ConfigNode("RESOURCE");
        node.AddValue("name", resName);
        node.AddValue("amount", Math.Round(amount * fillFraction, 2));
        node.AddValue("maxAmount", Math.Round(amount, 2));
        node.AddValue("isTweakable", isTweakable);
        part.AddResource(node);
    }

    public void cleanResources()
    {
        Predicate<PartResource> match;
        switch (tankType)
        {
            case "*Mixed":
                match = (res => (res.resourceName != "LiquidFuel" && res.resourceName != "Oxidizer"));
                break;
            case "LiquidFuel":
            case "MonoPropellant":
            case "Oxidizer":
            case "SolidFuel":
                match = (res => (res.resourceName != tankType));
                break;
            case "*Structural":
                match = (res => true);
                break;
            default:
                return;
        }
        for(int i = part.Resources.list.Count-1; i >= 0; --i) {
            if(match(part.Resources.list[i])) {
                DestroyImmediate(part.Resources.list[i]);
                part.Resources.list.RemoveAt(i);                
            }
        }
    }
    #endregion

    #region Attachments

    private void initializeNodes()
    {
        foreach (AttachNode node in part.attachNodes)
        {
            Vector3 position = transform.position + transform.TransformDirection(node.position);
            PartAttachment att = addTankOrBellAttachment(position, (translate => { node.position += transform.InverseTransformDirection(translate); return true; }));
        }
        if(part.attachRules.allowSrfAttach) {
            AttachNode node = part.srfAttachNode;
            Vector3 position = transform.position + transform.TransformDirection(node.position);
            PartAttachment att = addTankOrBellAttachment(position, (translate => { node.position += transform.InverseTransformDirection(translate); return true; }));
        }
    }

    private class PartAttachment
    {
        public Part child;
        public TransformPositionFollower follower;
        public TransformPositionFollower srbFollower;
        public object data;

        public PartAttachment(TransformPositionFollower follower, TransformPositionFollower srbFollower, object data)
        {
            this.follower = follower;
            this.srbFollower = srbFollower;
            this.data = data;
        }
    }


    private PartAttachment parentAttachment = null;
    private LinkedList<PartAttachment> childAttachments = new LinkedList<PartAttachment>();

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
                //print("*ST* Removing old parent part:" + parentAttachment.child);

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
                //print("Attaching new parent: " + parent + " to " + childToParent.id);

                Vector3 position = transform.position + transform.TransformDirection(childToParent.position);
                Part root = EditorLogic.SortedShipList[0];

                int siblings = part.symmetryCounterparts == null ? 1 : (part.symmetryCounterparts.Count + 1);

                // we need to translate this part down so that when the translation from the parent reaches here it ends in the right spot
                parentAttachment = addTankOrBellAttachment(position, trans =>
                {
                    root.transform.Translate(trans, Space.World);
                    transform.Translate(-trans/siblings, Space.World);
                    return parent.transform.gameObject;
                });
                parentAttachment.child = parent;
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
        //print("*ST* Child part removed: " + delete.Value.child);
    }

    private PartAttachment addChildPartAttachment(Part child)
    {
        AttachNode node = child.findAttachNodeByPart(part);

        if (node == null)
        {
            print("*ST* unable to find attach node for child: " + child.transform);
            return null;
        }

        Vector3 offset = child.transform.TransformDirection(node.position);
        PartAttachment attach = addTankOrBellAttachment(child.transform.position + offset, TransformPositionFollower.createFollowerTranslate(child.transform));
        attach.child = child;

        childAttachments.AddLast(attach);
        //print("*ST* Attaching child part: " + child.transform.name + " from child node " + node.id + (attach.srbFollower==null?"To Tank":"To SRB"));
        
        return attach;
    }

    private PartAttachment addTankOrBellAttachment(Vector3 position, Predicate<Vector3> Translate)
    {
        TransformPositionFollower srbFollower = null;
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
                srbFollower = TransformPositionFollower.createFollower(srbBell, position, Translate);
                
                // The follower for the tank part is in the position of the srb attachment
                position = srbBell.position;
            }
        }

        TransformPositionFollower follower = TransformPositionFollower.createFollower(position, Translate);
        object data = addTankAttachment(follower);

        return new PartAttachment(follower, srbFollower, data);
    }

    private void removeTankOrBellAttachment(PartAttachment delete)
    {
        removeTankAttachment(delete.data);
        Destroy(delete.follower.gameObject);
        if (delete.srbFollower != null)
            Destroy(delete.srbFollower.gameObject);
    }

    /// <summary>
    /// Add object attached to the surface of this tank.
    /// Base classes should proportionally move the location and orientation (rotation) as the tank stretches.
    /// The return value will be passed back to removeTankAttachment when it's detached
    /// </summary>
    /// <param name="location"></param>
    public abstract object addTankAttachment(TransformPositionFollower attach);

    /// <summary>
    /// Remove object attached to the surface of this tank.
    /// </summary>
    /// <param name="location"></param>
    public abstract void removeTankAttachment(object data);
    #endregion
}
