using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ProceduralParts
{
    public class TextureSet
    {
        public string name;

        public bool autoScale;
        public bool endsAutoScale;
        public bool autoWidthDivide;
        public float autoHeightSteps;
        public Vector2 scale = new Vector2(2f, 1f);

        public Texture sides;
        public Texture sidesBump;
        public Texture ends;
        public Texture endsBump;
        public string sidesName;
        public string endsName;
        public string sidesBumpName;
        public string endsBumpName;

        public Color sidesSpecular = new Color(0.2f, 0.2f, 0.2f);
        public float sidesShininess = 0.4f;

        public Color endsSpecular = new Color(0.2f, 0.2f, 0.2f);
        public float endsShininess = 0.4f;

        public static readonly string ModTag = "[ProceduralParts.TextureSet]";

        public static TextureSet LoadTextureSet(ConfigNode node)
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
                sidesBumpName = "",
                endsBumpName = ""
            };
            if (node.GetNode("sides").HasValue("bump"))
                tex.sidesBumpName = node.GetNode("sides").GetValue("bump");
            if (node.GetNode("ends").HasValue("bump"))
                tex.endsBumpName = node.GetNode("ends").GetValue("bump");

            if (node.GetNode("sides").HasValue("uScale"))
                float.TryParse(node.GetNode("sides").GetValue("uScale"), out tex.scale.x);
            if (node.GetNode("sides").HasValue("vScale"))
                float.TryParse(node.GetNode("sides").GetValue("vScale"), out tex.scale.y);


            if (node.GetNode("sides").HasValue("autoScale"))
                bool.TryParse(node.GetNode("sides").GetValue("autoScale"), out tex.autoScale);
            if (node.GetNode("ends").HasValue("autoScale"))
                bool.TryParse(node.GetNode("ends").GetValue("autoScale"), out tex.endsAutoScale);
            if (node.GetNode("sides").HasValue("autoWidthDivide"))
                bool.TryParse(node.GetNode("sides").GetValue("autoWidthDivide"), out tex.autoWidthDivide);
            if (node.GetNode("sides").HasValue("autoHeightSteps"))
                float.TryParse(node.GetNode("sides").GetValue("autoHeightSteps"), out tex.autoHeightSteps);

            if (node.GetNode("sides").HasValue("specular"))
                tex.sidesSpecular = ConfigNode.ParseColor(node.GetNode("sides").GetValue("specular"));
            if (node.GetNode("sides").HasValue("shininess"))
                float.TryParse(node.GetNode("sides").GetValue("shininess"), out tex.sidesShininess);
            if (node.GetNode("ends").HasValue("specular"))
                tex.endsSpecular = ConfigNode.ParseColor(node.GetNode("ends").GetValue("specular"));
            if (node.GetNode("ends").HasValue("shininess"))
                float.TryParse(node.GetNode("ends").GetValue("shininess"), out tex.endsShininess);

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

            if (string.IsNullOrEmpty(tex.endsBumpName))
                tex.endsBump = null;
            else if (!TryFindTexture(textures, ref tex.endsBumpName, out tex.endsBump))
            {
                Debug.LogError("*ST* Cap bump textures not found for " + textureSet);
                return null;
            }

            return tex;
        }

        public static void LoadTextureSets(Dictionary<string, TextureSet> dict)
        {
            if (dict is null) return;
            foreach (ConfigNode texInfo in GameDatabase.Instance.GetConfigNodes("STRETCHYTANKTEXTURES"))
            {
                foreach (ConfigNode node in texInfo.nodes)
                {
                    if (LoadTextureSet(node) is TextureSet textureSet)
                        dict.Add(textureSet.name, textureSet);
                }
            }

            if (dict.Count == 0)
                Debug.LogError($"{ModTag} No TextureSets found!");
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

        internal Vector2 GetScaleUv(Vector2 sideTextureScale)
        {
            var scaleUV = scale;
            if (autoScale)
            {
                scaleUV.x = Math.Max(1, (float)Math.Round(scaleUV.x * sideTextureScale.x / 8f));
                if (autoWidthDivide)
                {
                    if (autoHeightSteps > 0)
                        scaleUV.y = (float)Math.Ceiling(scaleUV.y * sideTextureScale.y / scaleUV.x * (1f / autoHeightSteps)) * autoHeightSteps;
                    else
                        scaleUV.y *= sideTextureScale.y / scaleUV.x;
                }
                else
                {
                    if (autoHeightSteps > 0)
                        scaleUV.y = (float)Math.Max(Math.Round(sideTextureScale.y / autoHeightSteps), 1f) * autoHeightSteps;
                    else
                        scaleUV.y *= sideTextureScale.y;
                }
            }

            return scaleUV;
        }

        internal static void SetupShader(Material material, Texture bumpMap)
        {
            if (HighLogic.LoadedScene != GameScenes.LOADING)
            {
                material.shader = Shader.Find(bumpMap != null ? "KSP/Bumped Specular" : "KSP/Specular");
            }
            else
            {
                material.shader = Shader.Find("KSP/ScreenSpaceMask");
            }
        }

        internal static void SetTextureProperties(Material material, Texture texture, Texture bumpMap, Color specular, float shininess, bool autoScale, Vector2 scaleUV)
        {
            var scaleFactor = autoScale ? scaleUV.x / Mathf.PI * 2 : 0.95f;
            var scaleVec = new Vector2(scaleFactor, scaleFactor);
            var offset = 0.5f - 0.5f * scaleFactor;
            var offsetVec = new Vector2(offset, offset);
            material.mainTextureScale = scaleVec;
            material.mainTextureOffset = offsetVec;

            material.SetColor("_SpecColor", specular);
            material.SetFloat("_Shininess", shininess);
            material.SetTexture("_MainTex", texture);

            SetupShader(material, bumpMap);

            if (bumpMap != null)
            {
                material.SetTextureScale("_BumpMap", scaleVec);
                material.SetTextureOffset("_BumpMap", offsetVec);
                material.SetTexture("_BumpMap", bumpMap);
            }
        }
    }
}
