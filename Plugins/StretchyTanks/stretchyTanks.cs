using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;


public class StretchyTanks : PartModule
{
    //SW Added: initialBasemass variable to store MFT's basemass calculation using the tank's initial during KSP startup.
    [KSPField(isPersistant = true)]
    public float initialBasemass = 0.0f;

    [KSPField(isPersistant = true)]
    public float stretchFactor = 1f;

    [KSPField(isPersistant = true)]
    public float radialFactor = 1f;

    [KSPField(isPersistant = true)]
    public bool flightStarted = false;

    [KSPField(isPersistant = true)]
    public int tankType = 0;

    [KSPField(isPersistant = true)]
    public int textureType = -1;

    [KSPField(isPersistant = true)]
    public string textureSet = "Original";

    [KSPField(isPersistant = true)]
    public float burnTime = 60f; // NK for SRBs

    [KSPField]
    public float initialDryMass;

    [KSPField]
    public bool superStretch;

    [KSPField]
    public bool stretchSRB = false; // NK

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

    public bool newAttachTop = false;

    public bool newAttachBottom = false;

    public bool newAttachSurface = false;

    public bool triggerUpdate = false; // NK
    public bool triggerRounding = false;

    public bool rescaled = false;

    public bool GUIon = false;

    public bool GUIdisabled = false;

    [KSPField]
    public Vector3 origScale = new Vector3(-1, -1, -1); // NK, for changed way parts resize

    public override void OnStart(StartState state)
    {
        if (HighLogic.LoadedSceneIsEditor)
        {
            changeTextures();
            //triggerUpdate = true;
            // instead:
            updateMass();
            changeResources();
            updateScale();
            updateSurfaceNodes();
        }
        if (HighLogic.LoadedSceneIsFlight)
        {
            if (flightStarted != true)
            {
                if (!part.Modules.Contains("ModuleFuelTanks"))
                {
                    updateMass();
                    changeResources();
                }
                flightStarted = true;
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
            if(stretchSRB)
                return "While mousing over the tank:\n* Hold '" + stretchKey + "' then move the mouse up or down to stretch its length." +
                "\n* Hold '" + radialKey + "' then move the mouse side to side to stretch its width." +
                "\n* Press 'g' to change its burn time." +
                "\n* Press 't' to change its texture.";
            else
                return "While mousing over the tank:\n* Hold '" + stretchKey + "' then move the mouse up or down to stretch its length." +
                "\n* Hold '" + radialKey + "' then move the mouse side to side to stretch its width." +
                "\n* Press 'g' to change its fuel type." +
                "\n* Press 't' to change its texture.";
        }
        else
        {
            return "While mousing over the tank:\n* Hold '" + stretchKey + "' then move the mouse up or down to stretch its length." +
                "\n* Press 'g' to change its fuel type." +
                "\n* Press 't' to change its texture.";
        }
    }

    public void updateMass()
    {
        if (part.Modules.Contains("ModuleFuelTanks"))
            return;
        if (tankType == 0)
        {
            part.mass = Mathf.Round(initialDryMass * stretchFactor * radialFactor * radialFactor * 1000f) / 1000f;
        }
        else if (tankType == 1)
        {
            part.mass = Mathf.Round(initialDryMass * 0.575f * stretchFactor * radialFactor * radialFactor * 1000f) / 1000f;
        }
        else if (tankType == 2)
        {
            part.mass = Mathf.Round(initialDryMass * 1f * stretchFactor * radialFactor * radialFactor * 1000f) / 1000f;
        }
        else if (tankType == 3)
        {
            part.mass = Mathf.Round(initialDryMass * 0.75f * stretchFactor * radialFactor * radialFactor * 1000f) / 1000f;
        }
        // NK add solid fuel, dry mass = 0.0015 per unit, or 1:6 given SF's mass of 0.0075t per unit
        else if (tankType == 4)
        {
            part.mass = Mathf.Round(initialDryMass * 1.5f * stretchFactor * radialFactor * radialFactor * 1000f) / 1000f;
        }
    }

    public override void OnLoad(ConfigNode node)
    {
        if (HighLogic.LoadedSceneIsFlight && tankType != 0)
        {
            part.Resources.list.Clear();
        }
        updateScale(); // NK TEST
    }

    public string getResourceNames()
    {
        String total = "";
        foreach (PartResource resource in part.Resources)
        {
            /*if (resource.resourceName == "LiquidFuel")
            {
                total = "LiquidFuel: " + resource.amount;
            }
            if (resource.resourceName == "Oxidizer")
            {
                if (total == "")
                {
                    total = "Oxidizer: " + resource.amount;
                }
                else
                {
                    total += "\nOxidizer: " + resource.amount;
                }
            }
            if (resource.resourceName == "MonoPropellant")
            {
                if (total == "")
                {
                    total = "MonoPropellent: " + resource.amount;
                }
                else
                {
                    total += "\nMonoPropellent: " + resource.amount;
                }
            }*/
            if (total != "")
                total += "\n";
            total += resource.resourceName + ": " + resource.amount;
            //NK Add Solid Fuel
            if (resource.resourceName == "SolidFuel")
            {
                /*if (total == "")
                {
                    total = "SolidFuel: " + resource.amount;
                }
                else
                {
                    total += "\nSolidFuel: " + resource.amount;
                }*/
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
        float massFactor = initialDryMass * stretchFactor * radialFactor * radialFactor;
        part.breakingForce = 969.47f * Mathf.Pow(massFactor, 0.3684f);
        part.breakingTorque = 969.47f * Mathf.Pow(massFactor, 0.3684f);
    }

    public void changeResources()
    {
        if (!part.Modules.Contains("ModuleFuelTanks"))
        {
            if(stretchSRB)
                tankType = 4; // NK
            part.Resources.list.Clear();
            float volume = initialDryMass * 9.203885f * stretchFactor * radialFactor * radialFactor;
            if (tankType == 0)
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
            }
            if (tankType == 1)
            {
                ConfigNode node = new ConfigNode("RESOURCE");
                node.AddValue("amount", Math.Round(49.9789d * volume, 2));
                node.AddValue("maxAmount", Math.Round(49.9789d * volume, 2));
                node.AddValue("name", "LiquidFuel");
                part.AddResource(node);
            }
            if (tankType == 2)
            {
                ConfigNode node = new ConfigNode("RESOURCE");
                node.AddValue("amount", Math.Round(203.718d * volume, 2));
                node.AddValue("maxAmount", Math.Round(203.718d * volume, 2));
                node.AddValue("name", "MonoPropellant");
                part.AddResource(node);
            }
            if (tankType == 3)
            {
                ConfigNode node = new ConfigNode("RESOURCE");
                node.AddValue("amount", Math.Round(81.4873d * volume, 2));
                node.AddValue("maxAmount", Math.Round(81.4873d * volume, 2));
                node.AddValue("name", "Oxidizer");
                part.AddResource(node);
            }
            // NK add Solid Fuel
            // Yields 850 for 1.5t dry mass, like BACC (because dry mass = 1.5 LF/Ox dry mass)
            // But the RT-10 has way better dry:wet ratio, so pick something inbetween: 1.2794x BACC
            if (tankType == 4)
            {
                ConfigNode node = new ConfigNode("RESOURCE");
                node.AddValue("amount", Math.Round(92.38057d * 1.2794d * volume, 2));
                node.AddValue("maxAmount", Math.Round(92.38057d * 1.2794d * volume, 2));
                node.AddValue("name", "SolidFuel");
                part.AddResource(node);
                if (stretchSRB)
                    changeThrust();
            }
        }
    }

    public void changeThrust()
    {
        ModuleEngines mE = (ModuleEngines)part.Modules["ModuleEngines"];
        float mThrust = (float)Math.Round(mE.atmosphereCurve.Evaluate(0) * part.Resources["SolidFuel"].maxAmount * part.Resources["SolidFuel"].info.density * 9.81f / burnTime, 2);
        mE.maxThrust = mThrust;
        mE.heatProduction = (float)Math.Round((200f + 5200f / Math.Pow((burnTime + 20f), 0.75f))*0.5f);
        if (part.Modules.Contains("ModuleEngineConfigs"))
        {

            PartModule mEC = part.Modules["ModuleEngineConfigs"];
            if (mEC != null)
            {
                mEC.SendMessage("ChangeThrust", mThrust);
                /*List<ConfigNode> configs = (List<ConfigNode>)mEC.Fields.GetValue("configs");
                ConfigNode cfg = configs.Find(c => c.GetValue("name").Equals("Normal"));
                cfg.SetValue("maxThrust", mThrust.ToString());
                mEC.GetType().GetField("configs").SetValue(mEC, configs);*/
            }
        }

    }

    public virtual void updateScale()
    {
        try // run on OnLoad() now, so may be nulls
        {
            //transform.GetChild(0).GetChild(0).GetChild(0).localScale = new Vector3(radialFactor, radialFactor, stretchFactor);
            if (origScale.x < 0)
                origScale = transform.GetChild(0).GetChild(0).localScale;
            Vector3 scale = new Vector3(radialFactor, stretchFactor, radialFactor);
            scale.Scale(origScale);
            transform.GetChild(0).GetChild(0).localScale = scale;

            float srbHeight = 1f;
            float srbOffset = -0.18f;
            float newsrbNodeOffset = 0.0f;
            if (stretchSRB)
            {
                if (stretchFactor < 1f)
                {
                    srbHeight = stretchFactor;
                    srbOffset *= srbHeight;
                }
                else
                {
                    if (radialFactor > 1f)
                    {
                        srbHeight = Math.Min(radialFactor, stretchFactor);
                    }
                    if (srbHeight > 1f)
                    {
                        srbOffset += ((bottomPosition - srbOffset) - (bottomPosition - srbOffset) * srbHeight);
                    }
                    srbOffset += bottomPosition * stretchFactor - bottomPosition;
                }
                transform.GetChild(0).GetChild(1).localScale = new Vector3(radialFactor * 1.1f, srbHeight * 0.8f, radialFactor * 1.1f);
                transform.GetChild(0).GetChild(1).localPosition = new Vector3(0f, srbOffset, 0f);
                newsrbNodeOffset = -1.25127f * 0.802f * srbHeight + srbOffset - (bottomPosition * stretchFactor);
                /*if (srbHeight < 1f)
                    newsrbNodeOffset -= srbOffset + srbOffset / (srbHeight * srbHeight);*/
                if (srbNodeOffset == 0f)
                    srbNodeOffset = newsrbNodeOffset;
            }
            if (HighLogic.LoadedSceneIsEditor)
            {
                if (part.attachMode == AttachModes.SRF_ATTACH && superStretch == true)
                {
                    var diff = attach * radialFactor - part.srfAttachNode.position.x;
                    var x = part.transform.localPosition.x;
                    var z = part.transform.localPosition.z;
                    var angle = Mathf.Atan2(x, z);
                    part.transform.Translate(diff * Mathf.Sin(angle), 0f, diff * Mathf.Cos(angle), part.parent.transform);
                }
                float length = topPosition - bottomPosition;
                part.findAttachNode("top").position.y = topPosition * stretchFactor;
                if (part.findAttachNode("top").attachedPart != null)
                {
                    float stretchDifference = (stretchFactor - 1f) / 2f * length;
                    if (topCheck)
                    {
                        topStretchPosition = stretchDifference;
                        topCheck = false;
                    }
                    if (part.findAttachNode("top").attachedPart == part.parent)
                    {
                        var p = EditorLogic.SortedShipList[0];
                        if (part.symmetryCounterparts != null)
                        {
                            float count = 1f;
                            foreach (Part P in part.symmetryCounterparts)
                            {
                                count += 1f;
                            }
                            p.transform.Translate(0f, (stretchDifference - topStretchPosition) / count, 0f, part.transform);
                            part.transform.Translate(0f, -(stretchDifference - topStretchPosition), 0f);
                        }
                        else
                        {
                            p.transform.Translate(0f, stretchDifference - topStretchPosition, 0f, part.transform);
                            part.transform.Translate(0f, -(stretchDifference - topStretchPosition), 0f);
                        }
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
                part.findAttachNode("bottom").position.y = bottomPosition * stretchFactor + newsrbNodeOffset; // NK
                if (part.findAttachNode("bottom").attachedPart != null)
                {
                    float stretchDifference = (stretchFactor - 1f) / 2f * length + newsrbNodeOffset - srbNodeOffset;
                    if (bottomCheck)
                    {
                        bottomStretchPosition = stretchDifference;
                        bottomCheck = false;
                    }
                    if (part.findAttachNode("bottom").attachedPart == part.parent)
                    {
                        var p = EditorLogic.SortedShipList[0];
                        if (part.symmetryCounterparts != null)
                        {
                            float count = 1f;
                            foreach (Part P in part.symmetryCounterparts)
                            {
                                count += 1f;
                            }
                            p.transform.Translate(0f, (-stretchDifference + bottomStretchPosition) / count, 0f, part.transform);
                            part.transform.Translate(0f, -(-stretchDifference + bottomStretchPosition), 0f);
                        }
                        else
                        {
                            p.transform.Translate(0f, -stretchDifference + bottomStretchPosition, 0f, part.transform);
                            part.transform.Translate(0f, -(-stretchDifference + bottomStretchPosition), 0f);
                        }
                    }
                    else
                    {
                        part.findAttachNode("bottom").attachedPart.transform.Translate(0f, -stretchDifference + bottomStretchPosition, 0f, part.transform);
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
                part.srfAttachNode.position.x = attach * radialFactor;
                refreshSrfAttachNodes();
                // NK rescale attach nodes
                part.findAttachNode("top").size = (int)Math.Round((radialFactor - 0.07) * 2f * (stretchSRB ? 0.5f : 1f));
                part.findAttachNode("bottom").size = (int)Math.Round((radialFactor - 0.07) * 2f * (stretchSRB ? 0.5f : 1f));
            }
            srbNodeOffset = newsrbNodeOffset; // NK
        }
        catch
        {
        }
    }

    public void detectNewAttach()
    {
        if (part.findAttachNode("top").attachedPart != null)
        {
            if (attachTop == false)
            {
                newAttachTop = true;
                attachTop = true;
            }
            else
            {
                newAttachTop = false;
            }
        }
        else
        {
            attachTop = false;
            newAttachTop = false;
        }
        if (part.findAttachNode("bottom").attachedPart != null)
        {
            if (attachBottom == false)
            {
                newAttachBottom = true;
                attachBottom = true;
            }
            else
            {
                newAttachBottom = false;
            }
        }
        else
        {
            attachBottom = false;
            newAttachBottom = false;
        }
        foreach (Part p in part.children)
        {
            if (p.attachMode == AttachModes.SRF_ATTACH)
            {
                if (nodeList.Count == 0)
                {
                    newAttachSurface = true;
                }
                else if (!nodeList.Exists(x => x.id == p.uid))
                {
                    newAttachSurface = true;
                }
            }
        }
    }

    public void updateNodeList()
    {
        for (int i = 0; i < nodeList.Count; i++)
        {
            bool stillAttached = false;
            foreach (Part p in part.children)
            {
                if (nodeList[i].id == p.uid)
                {
                    stillAttached = true;
                }
            }
            if (stillAttached == false)
            {
                nodeList.RemoveAt(i);
            }
        }
    }

    public void addSurfaceNodes()
    {
        foreach (Part p in part.children)
        {
            if (p.attachMode == AttachModes.SRF_ATTACH)
            {
                if (!nodeList.Exists(x => x.id == p.uid))
                {
                    SurfaceNode newNode = new SurfaceNode();
                    newNode.id = p.uid;
                    newNode.rad = p.srfAttachNode.position.x;
                    newNode.xPos = p.transform.localPosition.x;
                    newNode.yPos = p.transform.localPosition.y;
                    newNode.zPos = p.transform.localPosition.z;
                    newNode.prevFactor = stretchFactor;
                    newNode.prevRFactor = radialFactor;
                    nodeList.Add(newNode);
                  }
            }
        }
        newAttachSurface = false;
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

    public void updateSurfaceNodes()
    {
        foreach (Part p in part.children)
        {
            foreach (SurfaceNode node in nodeList)
            {
                if (node.id == p.uid)
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

        // Set shaders
        if(bump != null)
            transform.GetChild(0).GetChild(0).GetChild(0).renderer.material.shader = Shader.Find("KSP/Bumped");
        else
            transform.GetChild(0).GetChild(0).GetChild(0).renderer.material.shader = Shader.Find("KSP/Diffuse");

        // top is no longer specular ever, just diffuse.
        transform.GetChild(0).GetChild(0).GetChild(0).GetChild(0).renderer.material.shader = Shader.Find("KSP/Diffuse");

        // set up UVs
        if(autoScale)
        {
            scaleUV.x = (float)Math.Round(scaleUV.x * radialFactor);
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

        transform.GetChild(0).GetChild(0).GetChild(0).renderer.material.mainTextureScale = scaleUV;
        transform.GetChild(0).GetChild(0).GetChild(0).renderer.material.SetTexture("_MainTex", main);
        if (bump != null)
        {
            transform.GetChild(0).GetChild(0).GetChild(0).renderer.material.SetTextureScale("_BumpMap", scaleUV);
            transform.GetChild(0).GetChild(0).GetChild(0).renderer.material.SetTexture("_BumpMap", bump);
        }
        transform.GetChild(0).GetChild(0).GetChild(0).GetChild(0).renderer.material.SetTexture("_MainTex", secondary);
    }

    void OnGUI()
    {
        if (GUIon == true  && GUIdisabled == false)
        {
            Vector2 screenPoint = Camera.main.WorldToScreenPoint(transform.position);
            Rect posMass = new Rect(screenPoint.x - 88f, Screen.height - screenPoint.y, 176f, 35f);
            Rect posRes = new Rect(screenPoint.x - 88f, Screen.height - screenPoint.y - 35, 176f, 35f);
            GUIStyle styMass = new GUIStyle();
            GUIStyle styRes = new GUIStyle();
            styMass.alignment = TextAnchor.MiddleCenter;
            styRes.alignment = TextAnchor.MiddleCenter;
            styMass.normal.textColor = Color.black;
            if (tankType == 0)
            {
                styRes.normal.textColor = Color.blue;
            }
            if (tankType == 1)
            {
                styRes.normal.textColor = Color.green;
            }
            if (tankType == 2)
            {
                styRes.normal.textColor = Color.yellow;
            }
            if (tankType == 3)
            {
                styRes.normal.textColor = Color.cyan;
            }
            // NK add Solid Fuel
            if (tankType == 4)
            {
                styRes.normal.textColor = Color.red;
            }
            GUI.Label(posRes, getResourceNames(), styRes);
            GUI.Label(posMass, "Total Mass: " + Math.Round(part.mass + part.GetResourceMass(), 3) + " tons\nDry Mass: " + part.mass + " tons", styMass);
        }
    }

    public void Update()
    {
        if (HighLogic.LoadedSceneIsEditor)
        {
            detectNewAttach();
            updateNodeList();
            if (newAttachSurface)
            {
                addSurfaceNodes();
                triggerUpdate = true;
            }
            if (newAttachTop || newAttachBottom || triggerUpdate)
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
                    if (counterpart.stretchFactor != stretchFactor || counterpart.radialFactor != radialFactor)
                    {
                        counterpart.stretchFactor = stretchFactor;
                        counterpart.radialFactor = radialFactor;
                        counterpart.triggerUpdate = true;
                    }
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
                // SW Added: On first run: Store basemass (already set by ModularFuelTanks) in initialBasemass.
                // All part.mass and basemass calculations will be based on initialBasemass AFTER volume has been determined.
                if (part.Modules.Contains("ModuleFuelTanks"))
                {
                    int vol = (int)Math.Round(initialDryMass * stretchFactor * radialFactor * radialFactor * 1600*10);
                    int mVol = (int)Math.Round((float)(part.Modules["ModuleFuelTanks"].Fields.GetValue("volume"))*10);
                    if (mVol != vol)
                    {
                        part.Modules["ModuleFuelTanks"].SendMessage("RoundOn", false);
                        part.Modules["ModuleFuelTanks"].SendMessage("ChangeVolume", ((float)vol) * 0.1f);
                        part.Modules["ModuleFuelTanks"].SendMessage("RoundOn", true);
                        triggerRounding = true;
                    }
                }
                triggerUpdate = false;
            }
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
            GUIon = false;
            if (triggerRounding && part.Modules.Contains("ModuleFuelTanks"))
            {
                triggerRounding = false;
                part.Modules["ModuleFuelTanks"].SendMessage("ChangeVolume", (float)Math.Round(initialDryMass * stretchFactor * radialFactor * radialFactor * 1600*10)*.1f);
                foreach (Part p in part.symmetryCounterparts)
                {
                    try
                    {
                        p.Modules["ModuleFuelTanks"].SendMessage("ChangeVolume", (float)Math.Round(initialDryMass * stretchFactor * radialFactor * radialFactor * 1600 * 10) * .1f);
                        var counterpart = p.Modules.OfType<StretchyTanks>().FirstOrDefault();
                        counterpart.triggerRounding = false;
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    public void OnMouseOver()
    {
        if (HighLogic.LoadedSceneIsEditor)
        {
            EditorLogic editor = EditorLogic.fetch;
            if (Input.GetKey(stretchKey) && editor.editorScreen != EditorLogic.EditorScreen.Actions)
            {
                float initialValue = stretchFactor;
                stretchFactor += Input.GetAxis("Mouse Y") * 0.125f;
                if (stretchFactor < 0.125f )
                {
                    stretchFactor = 0.125f;
                }
                if (stretchFactor > 25f)
                {
                    stretchFactor = 25f;
                }
                if (initialValue != stretchFactor)
                {
                    triggerUpdate = true;
                    rescaled = true;
                }
            }
            if (Input.GetKey(radialKey) && editor.editorScreen != EditorLogic.EditorScreen.Actions && superStretch == true)
            {
                float initialValue = radialFactor;
                radialFactor += Input.GetAxis("Mouse X") * 0.075f;
                if (radialFactor < 0.075f)
                {
                    radialFactor = 0.075f;
                }
                if (radialFactor > 7.5f)
                {
                    radialFactor = 7.5f;
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
                burnTime += (Input.GetAxis("Mouse Y") * 2.0f);
                if (burnTime < 1f)
                {
                    burnTime = 1f;
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
                tankType++;
                if (tankType == 5) // NK add solid fuel
                {
                    tankType = 0;
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