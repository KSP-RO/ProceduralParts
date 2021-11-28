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
        public bool autoScaleU;
        public bool autoScaleV;
        public bool endsAutoScale;
        public bool autoWidthDivide;
        public float autoHeightSteps;
        public Vector2 scale = new Vector2(2f, 1f);

        public Texture sides;
        public Texture sidesBump = null;
        public Texture ends;
        public Texture endsBump = null;
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
            if (!(node.GetNode("sides") is ConfigNode sidesNode && node.GetNode("ends") is ConfigNode endsNode &&
                sidesNode.HasValue("texture") && endsNode.HasValue("texture")))
            {
                Debug.LogError($"{ModTag} LoadTextureSet found invalid Textureset {node.name}");
                return null;
            }

            TextureSet tex = new TextureSet
            {
                name = node.name,
                sidesName = sidesNode.GetValue("texture"),
                endsName = endsNode.GetValue("texture"),
                sidesBumpName = sidesNode.HasValue("bump") ? sidesNode.GetValue("bump") : string.Empty,
                endsBumpName = endsNode.HasValue("bump") ? endsNode.GetValue("bump") : string.Empty
            };

            if (sidesNode.HasValue("uScale"))
                float.TryParse(sidesNode.GetValue("uScale"), out tex.scale.x);
            if (sidesNode.HasValue("vScale"))
                float.TryParse(sidesNode.GetValue("vScale"), out tex.scale.y);

            if (sidesNode.HasValue("autoScale"))
                bool.TryParse(sidesNode.GetValue("autoScale"), out tex.autoScale);
            if (sidesNode.HasValue("autoScaleU"))
                bool.TryParse(sidesNode.GetValue("autoScaleU"), out tex.autoScaleU);
            if (sidesNode.HasValue("autoScaleV"))
                bool.TryParse(sidesNode.GetValue("autoScaleV"), out tex.autoScaleV);
            tex.autoScaleU |= tex.autoScale;
            tex.autoScaleV |= tex.autoScale;

            if (endsNode.HasValue("autoScale"))
                bool.TryParse(endsNode.GetValue("autoScale"), out tex.endsAutoScale);
            if (sidesNode.HasValue("autoWidthDivide"))
                bool.TryParse(sidesNode.GetValue("autoWidthDivide"), out tex.autoWidthDivide);
            if (sidesNode.HasValue("autoHeightSteps"))
                float.TryParse(sidesNode.GetValue("autoHeightSteps"), out tex.autoHeightSteps);

            if (sidesNode.HasValue("specular"))
                tex.sidesSpecular = ConfigNode.ParseColor(sidesNode.GetValue("specular"));
            if (sidesNode.HasValue("shininess"))
                float.TryParse(sidesNode.GetValue("shininess"), out tex.sidesShininess);
            if (endsNode.HasValue("specular"))
                tex.endsSpecular = ConfigNode.ParseColor(endsNode.GetValue("specular"));
            if (endsNode.HasValue("shininess"))
                float.TryParse(endsNode.GetValue("shininess"), out tex.endsShininess);

            Texture[] textures = Resources.FindObjectsOfTypeAll(typeof(Texture)) as Texture[];

            if (!TryFindTexture(textures, ref tex.sidesName, out tex.sides))
            {
                Debug.LogError($"{ModTag} LoadTextureSet Sides textures not found for {node.name}");
                return null;
            }

            if (!TryFindTexture(textures, ref tex.endsName, out tex.ends))
            {
                Debug.LogError($"{ModTag} LoadTextureSet Ends textures not found for {node.name}");
                return null;
            }

            if (!string.IsNullOrEmpty(tex.sidesBumpName) && !TryFindTexture(textures, ref tex.sidesBumpName, out tex.sidesBump))
            {
                Debug.LogError($"{ModTag} LoadTextureSet Side Bump textures not found for {node.name}");
                return null;
            }

            if (!string.IsNullOrEmpty(tex.endsBumpName) && !TryFindTexture(textures, ref tex.endsBumpName, out tex.endsBump))
            {
                Debug.LogError($"{ModTag} LoadTextureSet Cap bump textures not found for {node.name}");
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
                    {
                        if (dict.ContainsKey(textureSet.name))
                        {
                            Debug.LogError($"Duplicate legacy TextureSet {textureSet.name} found in {node}, skipping!");
                        } else
                        {
                            dict.Add(textureSet.name, textureSet);
                        }
                    }
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
            if (tex is null)
                return false;

            textureName = substName;
            return true;
        }

        private static Texture FindTexture(Texture[] textures, string textureName) =>
            textures.FirstOrDefault(t => t.name == textureName);

        internal Vector2 GetScaleUv(Vector2 sideTextureScale)
        {
            var scaleUV = scale;
            if (autoScaleU)
            {
                scaleUV.x = Math.Max(1, (float)Math.Round(scaleUV.x * sideTextureScale.x / 8f));
            }
            if (autoScaleU && autoScaleV)
            {
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
            } else if (autoScaleV)
            {
                float scaleFactor = autoWidthDivide ? (sideTextureScale.x / 8f) : 1;
                if (autoHeightSteps > 0)
                    scaleUV.y = (float)Math.Max(Math.Round(scaleUV.y * sideTextureScale.y / (autoHeightSteps * scaleFactor)), 1f) * autoHeightSteps;
                else
                    scaleUV.y *= sideTextureScale.y / scaleFactor;
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

            if (bumpMap != null && material.HasProperty("_BumpMap"))
            {
                material.SetTextureScale("_BumpMap", scaleVec);
                material.SetTextureOffset("_BumpMap", offsetVec);
                material.SetTexture("_BumpMap", bumpMap);
            }
        }
    }
}
