using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;


public class StretchyTanks : PartModule
{
    [KSPField(isPersistant = true)]
    public float stretchFactor = 1f;

    [KSPField(isPersistant = true)]
    public float radialFactor = 1f;

    public const int TANK_MIXED = 0;
    public const int TANK_LIQUID_FUEL = 1;
    public const int TANK_MONOPROP = 2;
    public const int TANK_OXIDIZER = 3;
    public const int TANK_SOLID = 4;
    public const int TANK_STRUCTURAL = 5;

    [KSPField(isPersistant = true)]
    public int tankType = TANK_MIXED;

    [KSPField(isPersistant = true)]
    public int textureType = -1;

    [KSPField(isPersistant = true)]
    public string textureSet = "Original";

    [KSPField(isPersistant = true)]
    public float burnTime = 60f; // NK for SRBs

    [KSPField]
    public float initialDryMass;
    
    [KSPField]
    public float volMultiplier = 1.0f; // for MFS

    [KSPField]
    public bool superStretch;

    [KSPField]
    public bool stretchSRB = false; // NK

    [KSPField]
    public float srbNozzleLength = 0;

    [KSPField]
    public float topPosition;

    [KSPField]
    public float bottomPosition;

    [KSPField]
    public float attach;

    [KSPField]
    public string stretchKey = "r";

    [KSPField]
    public string radialKey = "f";

    [KSPField]
    public string tankTypeKey = "g";

    [KSPField]
    public string textureKey = "t";

    public List<SurfaceNode> nodeList = new List<SurfaceNode>();

    public float lastUpdateFactor = 0f;

    public float topStretchPosition = 0f;

    public float bottomStretchPosition = 0f;

    public float srbNodeOffset = 0f; // NK

    public bool topCheck = true;

    public bool bottomCheck = true;

    public bool attachTop = false;

    public bool attachBottom = false;

    public bool triggerUpdate = false; // NK

    public bool rescaled = false;

    public bool GUIon = false;

    public bool GUIdisabled = false;

    [KSPField]
    public Vector3 origScale = new Vector3(-1, -1, -1); // NK, for changed way parts resize

    [KSPField]
    public float nodeSizeScalar = 1.0f; // for setting node size for superstretchies

    public float maxRFactor = 100f;

    public virtual void updateMaxRFactor()
    {
        if (!superStretch) return;
        maxRFactor = 100f;
        if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.Mode != Game.Modes.CAREER)
            return;

        foreach (ConfigNode tech in GameDatabase.Instance.GetConfigNodes("STRETCHYTANKMAXRAD"))
        {
            maxRFactor = 0.01f;
            for (int i = 0; i < tech.values.Count; i++)
            {
                var value = tech.values[i];
                if (value.name.Equals("name"))
                    continue;
                float r = float.Parse(value.value);
                if (r < maxRFactor) continue;
                if (ResearchAndDevelopment.GetTechnologyState(value.name) != RDTech.State.Available) continue;
                maxRFactor = r;
            }
        }

        if (radialFactor > maxRFactor) radialFactor = maxRFactor;
    }

    public override void OnStart(StartState state)
    {
        nodeList.Clear();
        if (HighLogic.LoadedSceneIsEditor)
        {
            updateMaxRFactor();
            changeTextures();
            //triggerUpdate = true;
            // instead:
            updateMass();
            changeResources();
            updateScale();
            //updateSurfaceNodes(); -- will do on triggered update.
        }
        if (HighLogic.LoadedSceneIsFlight)
        {
            if (!part.Modules.Contains("ModuleFuelTanks"))
            {
                updateMass();
                cleanResources();
            }
            changeTextures();
            updateScale();
            updateForceTorque();
        }
        if (stretchSRB)
            changeThrust();
    }

    public override string GetInfo()
    {
        if (superStretch)
        {
            if (stretchSRB)
                return "While mousing over the tank:\n* Hold '" + stretchKey + "' then move the mouse up or down to stretch its length." +
                "\n* Hold '" + radialKey + "' then move the mouse side to side to stretch its width." +
                "\n* Press 'g' to change its burn time." +
                "\n* Press 't' to change its texture.";
            else
                return "While mousing over the tank:\n* Hold '" + stretchKey + "' then move the mouse up or down to stretch its length." +
                "\n* Hold '" + radialKey + "' then move the mouse side to side to stretch its width." +
                "\n* Press 't' to change its texture." +
                ((!part.Modules.Contains("ModuleFuelTanks")) ? "\n* Press 'g' to change its fuel type." : "\n* Go to Action Editor and select tank to edit fuels.");
        }
        else
        {
            return "While mousing over the tank:\n* Hold '" + stretchKey + "' then move the mouse up or down to stretch its length." +
                "\n* Press 'g' to change its fuel type." +
                "\n* Press 't' to change its texture.";
        }
    }

    public virtual float calcVolumeFactor() { return stretchFactor * radialFactor * radialFactor * volMultiplier; }

    public void updateMass()
    {
        if (part.Modules.Contains("ModuleFuelTanks"))
            return;
        switch(tankType) {
            case TANK_MIXED:
                part.mass = Mathf.Round(initialDryMass * calcVolumeFactor() * 1000f / volMultiplier) / 1000f;
                break;
            case TANK_LIQUID_FUEL:
                part.mass = Mathf.Round(initialDryMass * 0.575f * calcVolumeFactor() * 1000f / volMultiplier) / 1000f;
                break;
            case TANK_MONOPROP:
                part.mass = Mathf.Round(initialDryMass * 1f * calcVolumeFactor() * 1000f / volMultiplier) / 1000f;
                break;
            case TANK_OXIDIZER:
                part.mass = Mathf.Round(initialDryMass * 0.75f * calcVolumeFactor() * 1000f / volMultiplier) / 1000f;
                break;
            case TANK_SOLID:
                // NK add solid fuel, dry mass = 0.0015 per unit, or 1:6 given SF's mass of 0.0075t per unit
                part.mass = Mathf.Round(initialDryMass * 1.5f * calcVolumeFactor() * 1000f / volMultiplier) / 1000f;
                break;
            case TANK_STRUCTURAL:
                // Structural Fuselage / Dry Mass FL-T400
                part.mass = Mathf.Round(initialDryMass * 0.8f * calcVolumeFactor() * 1000f / volMultiplier) / 1000f;
                break;
        }
    }

    public override void OnLoad(ConfigNode node)
    {
        updateScale();
    }

    public string getResourceNames()
    {
        String total = "";
        foreach (PartResource resource in part.Resources)
        {
            if (total != "")
                total += "\n";
            total += resource.resourceName + ": " + resource.amount;
            //NK Add Solid Fuel
            if (resource.resourceName == "SolidFuel")
            {
                if (stretchSRB)
                {
                    total += "\nThrust: " + Math.Round(((ModuleEngines)part.Modules["ModuleEngines"]).maxThrust,2);
                    total += "kN (Burn time: " + Math.Round(burnTime, 2) + "s, heat " + Math.Round(((ModuleEngines)part.Modules["ModuleEngines"]).heatProduction, 2) + ")";
                }
            }
        }
        return total;
    }

    public void updateForceTorque()
    {
        float massFactor = initialDryMass * calcVolumeFactor();
        part.breakingForce = 969.47f * Mathf.Pow(massFactor, 0.3684f);
        part.breakingTorque = 969.47f * Mathf.Pow(massFactor, 0.3684f);
    }

    public void changeResources()
    {
        if (part.Modules.Contains("ModuleFuelTanks"))
            return;

        if(stretchSRB)
            tankType = TANK_SOLID; // NK

        // remove resources
        foreach(PartResource res in part.GetComponents<PartResource>())
            DestroyImmediate(res);
        part.Resources.list.Clear();

        // find volume
        float volume = initialDryMass * 9.203885f * calcVolumeFactor();

        // add resources
        switch (tankType)
        {
            case TANK_MIXED:
                {
                    ConfigNode nodeF = new ConfigNode("RESOURCE");
                    nodeF.AddValue("amount", Math.Round(78.22784d * volume, 2));
                    nodeF.AddValue("maxAmount", Math.Round(78.22784d * volume, 2));
                    nodeF.AddValue("name", "LiquidFuel");
                    part.AddResource(nodeF);
                    ConfigNode nodeO = new ConfigNode("RESOURCE");
                    nodeO.AddValue("amount", Math.Round(95.6118d * volume, 2));
                    nodeO.AddValue("maxAmount", Math.Round(95.6118d * volume, 2));
                    nodeO.AddValue("name", "Oxidizer");
                    part.AddResource(nodeO);
                    break;
                }
            case TANK_LIQUID_FUEL:
                {
                    ConfigNode node = new ConfigNode("RESOURCE");
                    node.AddValue("amount", Math.Round(49.9789d * volume, 2));
                    node.AddValue("maxAmount", Math.Round(49.9789d * volume, 2));
                    node.AddValue("name", "LiquidFuel");
                    part.AddResource(node);
                    break;
                }
            case TANK_MONOPROP:
                {
                    ConfigNode node = new ConfigNode("RESOURCE");
                    node.AddValue("amount", Math.Round(203.718d * volume, 2));
                    node.AddValue("maxAmount", Math.Round(203.718d * volume, 2));
                    node.AddValue("name", "MonoPropellant");
                    part.AddResource(node);
                    break;
                }
            case TANK_OXIDIZER:
                {
                    ConfigNode node = new ConfigNode("RESOURCE");
                    node.AddValue("amount", Math.Round(81.4873d * volume, 2));
                    node.AddValue("maxAmount", Math.Round(81.4873d * volume, 2));
                    node.AddValue("name", "Oxidizer");
                    part.AddResource(node);
                    break;
                }
            case TANK_SOLID:
                {
                    // NK add Solid Fuel
                    // Yields 850 for 1.5t dry mass, like BACC (because dry mass = 1.5 LF/Ox dry mass)
                    // But the RT-10 has way better dry:wet ratio, so pick something inbetween: 1.2794x BACC
                    ConfigNode node = new ConfigNode("RESOURCE");
                    node.AddValue("amount", Math.Round(192f * volume, 2));
                    node.AddValue("maxAmount", Math.Round(192f * volume, 2));
                    node.AddValue("name", "SolidFuel");
                    node.AddValue("isTweakable", false);
                    part.AddResource(node);
                    if (stretchSRB)
                        changeThrust();
                    break;
                }
            case TANK_STRUCTURAL:
                {
                    break;
                }
            default:
                print("*ST*: Unknown tank type " + tankType);
                break;
        }
    }

    public void cleanResources()
    {
        if (part.Modules.Contains("ModuleFuelTanks"))
            return;
        Predicate<PartResource> match;
        switch (tankType)
        {
            case TANK_MIXED:
                match = (res => (res.resourceName != "LiquidFuel" && res.resourceName != "Oxidizer"));
                break;
            case TANK_LIQUID_FUEL:
                match = (res => (res.resourceName != "LiquidFuel"));
                break;
            case TANK_MONOPROP:
                match = (res => (res.resourceName != "MonoPropellant"));
                break;
            case TANK_OXIDIZER:
                match = (res => (res.resourceName != "Oxidizer"));
                break;
            case TANK_SOLID:
                match = (res => (res.resourceName != "SolidFuel"));
                break;
            case TANK_STRUCTURAL:
                match = (res => true);
                break;
            default:
                return;
        }
        foreach (PartResource res in part.GetComponents<PartResource>())
            if(match(res))
                DestroyImmediate(res);
        part.Resources.list.RemoveAll(match);
    }

    public void changeThrust()
    {
        try
        {
            ModuleEngines mE = (ModuleEngines)part.Modules["ModuleEngines"];
            float mThrust = (float)Math.Round(mE.atmosphereCurve.Evaluate(0) * part.Resources["SolidFuel"].maxAmount * part.Resources["SolidFuel"].info.density * 9.81f / burnTime, 2);
            mE.maxThrust = mThrust;
            mE.heatProduction = (float)Math.Round((200f + 5200f / Math.Pow((burnTime + 20f), 0.75f)) * 0.5f);
            if (part.Modules.Contains("ModuleEngineConfigs"))
            {

                var mEC = part.Modules["ModuleEngineConfigs"];
                if (mEC != null)
                {
                    //mEC.SendMessage("ChangeThrust", mThrust);
                    // thanks to ferram
                    Type engineType = mEC.GetType();
                    //
                    engineType.GetMethod("ChangeThrust").Invoke(mEC, new object[] { mThrust });

                    // unneeded - engineType.GetMethod("SetConfiguration").Invoke(mEC, new object[] { null });

                    /*List<ConfigNode> configs = (List<ConfigNode>)mEC.Fields.GetValue("configs");
                    ConfigNode cfg = configs.Find(c => c.GetValue("name").Equals("Normal"));
                    cfg.SetValue("maxThrust", mThrust.ToString());
                    mEC.GetType().GetField("configs").SetValue(mEC, configs);*/
                }
            }
        }
        catch (Exception e)
        {
            print("*ST* ChangeThrust, caught " + e.Message);
        }
    }

    public virtual void rescaleModel()
    {
        //transform.GetChild(0).GetChild(0).GetChild(0).localScale = new Vector3(radialFactor, radialFactor, stretchFactor);
        /*if (origScale.x < 0)
            origScale = transform.GetChild(0).GetChild(0).localScale;*/
        Vector3 scale = new Vector3(radialFactor, stretchFactor, radialFactor);
        scale.Scale(origScale);
        transform.GetChild(0).GetChild(0).localScale = scale;
    }

    public void updateScale()
    {
        try // run on OnLoad() now, so may be nulls
        {
            updateMaxRFactor();

            rescaleModel();

            if (stretchSRB)
            {
                transform.GetChild(0).GetChild(1).localScale = new Vector3(radialFactor, radialFactor, radialFactor);
                transform.GetChild(0).GetChild(1).localPosition = new Vector3(0f, bottomPosition * stretchFactor, 0f);
            }
            if (part.attachMode == AttachModes.SRF_ATTACH && superStretch == true)
            {
                var diff = attach * getAttachFactor() - part.srfAttachNode.position.x;
                var x = part.transform.localPosition.x;
                var z = part.transform.localPosition.z;
                var angle = Mathf.Atan2(x, z);
                if (HighLogic.LoadedSceneIsEditor && triggerUpdate)
                    part.transform.Translate(diff * Mathf.Sin(angle), 0f, diff * Mathf.Cos(angle), part.parent.transform);
            }
            part.findAttachNode("top").position.y = topPosition * stretchFactor;
            float stretchDifference = part.findAttachNode("top").position.y - topPosition;
            if (HighLogic.LoadedSceneIsEditor && triggerUpdate)
            {
                if (part.findAttachNode("top").attachedPart != null)
                {
                    if (topCheck)
                    {
                        topStretchPosition = stretchDifference;
                        topCheck = false;
                    }
                    if (part.findAttachNode("top").attachedPart == part.parent)
                    {
                        var p = EditorLogic.SortedShipList[0];
                        float count = 1;
                        if(part.symmetryCounterparts != null)
                            count += part.symmetryCounterparts.Count;
                        p.transform.Translate(0f, (stretchDifference - topStretchPosition) / count, 0f, part.transform);
                        part.transform.Translate(0f, -(stretchDifference - topStretchPosition), 0f);
                    }
                    else
                    {
                        part.findAttachNode("top").attachedPart.transform.Translate(0f, stretchDifference - topStretchPosition, 0f, part.transform);
                    }
                    topStretchPosition = stretchDifference;
                }
                else
                {
                    topCheck = true;
                }
            }
            part.findAttachNode("bottom").position.y = bottomPosition * stretchFactor - radialFactor * srbNozzleLength;
            stretchDifference = part.findAttachNode("bottom").position.y - bottomPosition + srbNozzleLength;
            if (HighLogic.LoadedSceneIsEditor && triggerUpdate)
            {
                if (part.findAttachNode("bottom").attachedPart != null)
                {
                    if (bottomCheck)
                    {
                        bottomStretchPosition = stretchDifference;
                        bottomCheck = false;
                    }
                    if (part.findAttachNode("bottom").attachedPart == part.parent)
                    {
                        var p = EditorLogic.SortedShipList[0];
                        float count = 1;
                        if (part.symmetryCounterparts != null)
                            count += part.symmetryCounterparts.Count;
                        p.transform.Translate(0f, (stretchDifference - bottomStretchPosition) / count, 0f, part.transform);
                        part.transform.Translate(0f, -(stretchDifference - bottomStretchPosition), 0f);
                    }
                    else
                    {
                        part.findAttachNode("bottom").attachedPart.transform.Translate(0f, stretchDifference - bottomStretchPosition, 0f, part.transform);
                    }
                    bottomStretchPosition = stretchDifference;
                }
                else
                {
                    bottomCheck = true;
                }
            }
            if (superStretch == true)
            {
                part.srfAttachNode.position.x = attach * getAttachFactor();
                refreshSrfAttachNodes();
                // NK rescale attach nodes
                part.findAttachNode("top").size = (int)Math.Round(getTopFactor() * 2f * nodeSizeScalar-0.01);
                part.findAttachNode("bottom").size = (int)Math.Round(radialFactor * 2f * nodeSizeScalar-0.01);
            }
        }
        catch (Exception e)
        {
            print("*ST* UpdateScale caught: " + e.Message);
        }
    }

    public virtual float getTopFactor() { return radialFactor; }
    public virtual float getAttachFactor() { return radialFactor; }

    public bool detectNewAttach()
    {
        bool detected = false;
        if (part.findAttachNode("top").attachedPart != null)
        {
            if (attachTop == false)
            {
                attachTop = true;
                detected = true;
            }
        }
        else
        {
            attachTop = false;
        }
        if (part.findAttachNode("bottom").attachedPart != null)
        {
            if (attachBottom == false)
            {
                attachBottom = true;
                detected = true;
            }
        }
        else
        {
            attachBottom = false;
        }

        // update node list
        nodeList.RemoveAll(n => !part.children.Exists(p => p.uid == n.id));
        foreach (Part p in part.children)
        {
            if (p.attachMode == AttachModes.SRF_ATTACH)
            {
                if (!nodeList.Exists(x => x.id == p.uid))
                {
                    nodeList.Add(newSurfaceNode(p));
                    detected = true;
                }
            }
        }
        return detected;
    }


    public virtual SurfaceNode newSurfaceNode(Part p)
    {
        SurfaceNode newNode = new SurfaceNode();
        newNode.id = p.uid;
        newNode.rad = p.srfAttachNode.position.x;
        newNode.xPos = p.transform.localPosition.x;
        newNode.yPos = p.transform.localPosition.y;
        newNode.zPos = p.transform.localPosition.z;
        newNode.prevFactor = stretchFactor;
        newNode.prevRFactor = radialFactor;
        return newNode;
    }

    public void refreshSrfAttachNodes()
    {
        foreach (Part p in part.children)
        {
            foreach (SurfaceNode node in nodeList)
            {
                if (node.id == p.uid)
                {
                    node.rad = p.srfAttachNode.position.x;
                }
            }
        }
    }

    public virtual void updateSurfaceNode(SurfaceNode node, Part p)
    {
        if (node.yPos > 0)
        {
            p.transform.Translate(0f, (stretchFactor / node.prevFactor) * (node.yPos - node.rad) + node.rad - node.yPos, 0f, part.transform);
        }
        else
        {
            p.transform.Translate(0f, (stretchFactor / node.prevFactor) * (node.yPos + node.rad) - node.rad - node.yPos, 0f, part.transform);
        }
        node.prevFactor = stretchFactor;
        node.yPos = p.transform.localPosition.y;

        float radius = Mathf.Sqrt(node.xPos * node.xPos + node.zPos * node.zPos);
        float angle = Mathf.Atan2(node.xPos, node.zPos);
        float newRad = (radialFactor / node.prevRFactor) * (radius - node.rad) + node.rad - radius;
        p.transform.Translate(newRad * Mathf.Sin(angle), 0f, newRad * Mathf.Cos(angle), part.transform);
        node.xPos = p.transform.localPosition.x;
        node.zPos = p.transform.localPosition.z;
        node.prevRFactor = radialFactor;
    }

    public void updateSurfaceNodes()
    {
        foreach (Part p in part.children)
        {
            foreach (SurfaceNode node in nodeList)
            {
                if (node.id == p.uid)
                    updateSurfaceNode(node, p);
            }
        }
    }

    public class SurfaceNode
    {
        public uint id { set; get; }
        public float xPos { set; get; }
        public float yPos { set; get; }
        public float zPos { set; get; }
        public float rad { set; get; }
        public float prevFactor { set; get; }
        public float prevRFactor { set; get; }
        public float prevTFactor { set; get; }
    }

    List<ConfigNode> GetTextures()
    {
        List<ConfigNode> textureSets = new List<ConfigNode>();
        foreach (ConfigNode STT in GameDatabase.Instance.GetConfigNodes("STRETCHYTANKTEXTURES"))
            for (int i = 0; i < STT.nodes.Count; i++)
                textureSets.Add(STT.nodes[i]);

        //print("*ST* Found " + textureSets.Count + " textures");
        return textureSets;
    }

    public void changeTextures()
    {
        // upgrade to new system
        if (textureType >= 0)
        {
            switch (textureType)
            {
                case 1:
                    textureSet = "Stockalike";
                    break;
                case 2:
                    textureSet = "German";
                    break;
                case 3:
                    textureSet = "Saturn";
                    break;
                case 4:
                    textureSet = "SegmentedSRB";
                    break;
                default:
                    textureSet = "Original";
                    break;
            }
            textureType = -1;
        }

        // get texture
        List<ConfigNode> textureSets = GetTextures();
        ConfigNode texInfo = null;
        foreach(ConfigNode t in textureSets)
            if (t.name.Equals(textureSet))
                texInfo = t;
        if(texInfo == null)
        {
            print("*ST* Missing texture " + textureSet);
            if(textureSets.Count > 0)
                texInfo = textureSets[0];
            else
            {
                print("*ST* No Texturesets found!");
                return;
            }
        }

        // Sanity check
        if (texInfo.GetNode("sides") == null || texInfo.GetNode("ends") == null)
        {
            print("*ST* Invalid Textureset " + textureSet);
            return;
        }
        if (!texInfo.GetNode("sides").HasValue("texture") || !texInfo.GetNode("ends").HasValue("texture"))
        {
            print("*ST* Invalid Textureset " + textureSet);
            return;
        }

        // Apply texture

        // get settings
        Vector2 scaleUV = new Vector2(2f, 1f);
        string sides, ends, sidesBump = "";
        bool autoScale = false;
        bool autoWidthDivide = false;
        float autoHeightSteps = 0f;
        sides = texInfo.GetNode("sides").GetValue("texture");
        ends = texInfo.GetNode("ends").GetValue("texture");
        if (texInfo.GetNode("sides").HasValue("bump"))
            sidesBump = texInfo.GetNode("sides").GetValue("bump");

        float ftmp;
        bool btmp;
        if (texInfo.GetNode("sides").HasValue("uScale"))
            if(float.TryParse(texInfo.GetNode("sides").GetValue("uScale"), out ftmp))
                scaleUV.x = ftmp;
        if (texInfo.GetNode("sides").HasValue("vScale"))
            if(float.TryParse(texInfo.GetNode("sides").GetValue("vScale"), out ftmp))
                scaleUV.y = ftmp;


        if (texInfo.GetNode("sides").HasValue("autoScale"))
            if(bool.TryParse(texInfo.GetNode("sides").GetValue("autoScale"), out btmp))
                autoScale = btmp;
        if (texInfo.GetNode("sides").HasValue("autoWidthDivide"))
            if (bool.TryParse(texInfo.GetNode("sides").GetValue("autoWidthDivide"), out btmp))
                autoWidthDivide = btmp;
        if (texInfo.GetNode("sides").HasValue("autoHeightSteps"))
            if(float.TryParse(texInfo.GetNode("sides").GetValue("autoHeightSteps"), out ftmp))
                autoHeightSteps = ftmp;

        Texture main = null;
        Texture bump = null;
        Texture secondary = null;

        bool bumpEnabled = !sidesBump.Equals("");

        Texture[] textures = Resources.FindObjectsOfTypeAll(typeof(Texture)) as Texture[];
        foreach (Texture t in textures)
        {
            if (t.name.Equals(sides))
            {
                main = t;
            }
            if (bumpEnabled && t.name.Equals(sidesBump))
            {
                bump = t;
            }
            if (t.name.Equals(ends))
            {
                secondary = t;
            }
        }
        if (main == null || secondary == null)
        {
            print("*ST* Textures not found for " + textureSet);
            return;
        }

        Transform tankModel = transform.GetChild(0).GetChild(0).GetChild(0);

        // Set shaders
        if (!part.Modules.Contains("ModulePaintable"))
        {
            if (bump != null)
                tankModel.renderer.material.shader = Shader.Find("KSP/Bumped");
            else
                tankModel.renderer.material.shader = Shader.Find("KSP/Diffuse");

            // top is no longer specular ever, just diffuse.
            tankModel.GetChild(0).renderer.material.shader = Shader.Find("KSP/Diffuse");
        }

        // set up UVs
        if(autoScale)
        {
            scaleUV.x = (float)Math.Round(scaleUV.x * getMappingRadialFactor());
            if (scaleUV.x < 1)
                    scaleUV.x = 1;
            if(autoWidthDivide)
            {
                if(autoHeightSteps > 0)
                    scaleUV.y = (float)Math.Ceiling(scaleUV.y * stretchFactor / scaleUV.x * (1f/autoHeightSteps)) * autoHeightSteps;
                else
                    scaleUV.y = scaleUV.y * stretchFactor / scaleUV.x;
            }
            else
            {
                scaleUV.y *= stretchFactor;
            }
        }

        // apply
        tankModel.renderer.material.mainTextureScale = scaleUV;
        tankModel.renderer.material.SetTexture("_MainTex", main);
        if (bump != null)
        {
            tankModel.renderer.material.SetTextureScale("_BumpMap", scaleUV);
            tankModel.renderer.material.SetTexture("_BumpMap", bump);
        }
        tankModel.GetChild(0).renderer.material.SetTexture("_MainTex", secondary);
    }

    public virtual float getMappingRadialFactor() { return radialFactor; }

    void OnGUI()
    {
        if (GUIon == true  && GUIdisabled == false)
        {
            Vector2 screenPoint = Camera.main.WorldToScreenPoint(transform.position);
            Rect posMass = new Rect(screenPoint.x - 88f, Screen.height - screenPoint.y, 176f, 35f);
            Rect posRes = new Rect(screenPoint.x - 88f, Screen.height - screenPoint.y - 35, 176f, 35f);
            Rect posSize = new Rect(screenPoint.x - 88f, Screen.height - screenPoint.y + 35, 176f, 35f);
            GUIStyle styMass = new GUIStyle();
            GUIStyle styRes = new GUIStyle();
            styMass.alignment = TextAnchor.MiddleCenter;
            styRes.alignment = TextAnchor.MiddleCenter;
            styMass.normal.textColor = Color.black;
            switch (tankType)
            {
                case TANK_MIXED:
                    styRes.normal.textColor = Color.blue;
                    break;
                case TANK_LIQUID_FUEL:
                    styRes.normal.textColor = Color.green;
                    break;
                case TANK_MONOPROP:
                    styRes.normal.textColor = Color.yellow;
                    break;
                case TANK_OXIDIZER:
                    styRes.normal.textColor = Color.cyan;
                    break;
                case TANK_SOLID:
                    styRes.normal.textColor = Color.red;
                    break;
            }
            if (tankType != TANK_STRUCTURAL)
            {
                GUI.Label(posRes, getResourceNames(), styRes);
                GUI.Label(posMass, "Total Mass: " + Math.Round(part.mass + part.GetResourceMass(), 3) + " tons\nDry Mass: " + part.mass + " tons", styMass);
            }
            else
            {
                GUI.Label(posRes, "Structural Fuselage", styRes);
                GUI.Label(posMass, "Mass: " + part.mass + " tons", styMass);
            }
            GUI.Label(posSize, "Size: " + getSizeText(), styRes);
        }
    }

    public virtual string getSizeText()
    {
        return Math.Round(radialFactor * origScale.x * 2.5, 3) + "m x " + Math.Round(stretchFactor * origScale.y * 1.875, 3) + "m";
    }

    public virtual void updateConterpartSize(StretchyTanks counterpart)
    {
        if (counterpart.stretchFactor != stretchFactor || counterpart.radialFactor != radialFactor)
        {
            counterpart.stretchFactor = stretchFactor;
            counterpart.radialFactor = radialFactor;
            counterpart.triggerUpdate = true;
        }
    }

    public void Update()
    {
        if (!HighLogic.LoadedSceneIsEditor)
            return;
        bool newTopBottom = detectNewAttach();

        if (triggerUpdate || newTopBottom)
        {
            updateScale();
        }
        if (triggerUpdate)
        {
            updateMass();
            changeResources();
            updateSurfaceNodes();
            foreach (Part p in part.symmetryCounterparts)
            {
                var counterpart = p.Modules.OfType<StretchyTanks>().FirstOrDefault();
                updateConterpartSize(counterpart);
                if (counterpart.tankType != tankType || counterpart.burnTime != burnTime) // NK SRB
                {
                    counterpart.tankType = tankType;
                    counterpart.burnTime = burnTime;
                    counterpart.changeResources();
                    counterpart.triggerUpdate = true;
                }
                if (!counterpart.textureSet.Equals(textureSet))
                {
                    counterpart.textureSet = textureSet;
                    counterpart.textureType = -1;
                    counterpart.changeTextures();
                    counterpart.triggerUpdate = true;
                }
            }
            // update MFS
            if (part.Modules.Contains("ModuleFuelTanks"))
            {
                try
                {
                    float curVolume = initialDryMass * calcVolumeFactor() * 1600;
                    const float DELTA = 0.01f;
                    float mVol = (float)(part.Modules["ModuleFuelTanks"].Fields.GetValue("volume"));
                    if (mVol > curVolume + DELTA || mVol < curVolume - DELTA)
                    {
                        part.Modules["ModuleFuelTanks"].SendMessage("ChangeVolume", (curVolume));
                    }
                }
                catch (Exception e)
                {
                    print("*ST* changing volume, caught: " + e.Message);
                }
            }
            triggerUpdate = false;
        }
    }

    public void OnMouseEnter()
    {
        if (HighLogic.LoadedSceneIsEditor)
            GUIon = true;
    }

    public void OnMouseExit()
    {
        if (HighLogic.LoadedSceneIsEditor)
        {
            // NK update texture scale
            changeTextures();
            foreach (Part p in part.symmetryCounterparts)
            {
                var counterpart = p.Modules.OfType<StretchyTanks>().FirstOrDefault();
                counterpart.changeTextures();
            }
            GUIon = false;
        }
    }

    public virtual void OnMouseOver()
    {
        if (HighLogic.LoadedSceneIsEditor)
        {
            EditorLogic editor = EditorLogic.fetch;
            if (Input.GetKey(stretchKey) && editor.editorScreen != EditorLogic.EditorScreen.Actions)
            {
                float initialValue = stretchFactor;
                stretchFactor += Input.GetAxis("Mouse Y") * (1f/18.75f) * (Input.GetKey(KeyCode.LeftShift) ? 10f : 1f);
                if (stretchFactor < 0.1f )
                {
                    stretchFactor = 0.1f;
                }
                /*if (stretchFactor > 25f)
                {
                    stretchFactor = 25f;
                }*/
                if (initialValue != stretchFactor)
                {
                    triggerUpdate = true;
                    rescaled = true;
                }
            }
            if (Input.GetKey(radialKey) && editor.editorScreen != EditorLogic.EditorScreen.Actions && superStretch == true)
            {
                float initialValue = radialFactor;
                radialFactor += Input.GetAxis("Mouse X") * 0.01f * (Input.GetKey(KeyCode.LeftShift) ? 10f : 1f);
                if (radialFactor < 0.01f)
                {
                    radialFactor = 0.01f;
                }
                if (radialFactor > maxRFactor)
                {
                    radialFactor = maxRFactor;
                }
                if (initialValue != radialFactor)
                {
                    triggerUpdate = true;
                    rescaled = true;
                }
            }
            // NK change thrust with this key
            if (stretchSRB && Input.GetKey(tankTypeKey) && editor.editorScreen != EditorLogic.EditorScreen.Actions)
            {
                float initialValue = burnTime;
                burnTime += Input.GetAxis("Mouse Y") * 1.0f * (Input.GetKey(KeyCode.LeftShift) ? 10f : 1f); ;
                if (burnTime < 0.25f)
                {
                    burnTime = 0.25f;
                }
                if (burnTime > 360f)
                {
                    burnTime = 360f;
                }
                if (initialValue != burnTime)
                {
                    triggerUpdate = true;
                }
            }
            if (!stretchSRB && Input.GetKeyDown(tankTypeKey) && !part.Modules.Contains("ModuleFuelTanks"))
            {
                switch (tankType)
                {
                    case TANK_MIXED:
                        tankType = TANK_LIQUID_FUEL;
                        break;
                    case TANK_LIQUID_FUEL:
                        tankType = TANK_OXIDIZER;
                        break;
                    case TANK_MONOPROP:
                        tankType = TANK_STRUCTURAL;
                        break;
                    case TANK_OXIDIZER:
                        tankType = TANK_MONOPROP;
                        break;
                    case TANK_STRUCTURAL:
                        tankType = TANK_MIXED;
                        break;

                }
                triggerUpdate = true;
            }
            if (Input.GetKeyDown(textureKey))
            {
                /*if (textureType >= 0)
                    changeTextures();*/
                List<ConfigNode> textureSets = GetTextures();
                int idx = 0;
                for (int i = 0; i < textureSets.Count; i++)
                    if (textureSets[i].name.Equals(textureSet))
                    {
                        idx = i;
                        break;
                    }
                idx++;
                if (idx >= textureSets.Count)
                    idx = 0;
                textureSet = textureSets[idx].name;
                changeTextures();
                triggerUpdate = true;
            }
        }
    }
}