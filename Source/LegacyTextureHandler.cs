using KSPAPIExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProceduralParts
{
    class LegacyTextureHandler
    {
        private readonly Part part;
        private readonly ProceduralPart PPart;
        private static readonly string ModTag = "[ProceduralParts.LegacyTextureHandler]";
        public static readonly Dictionary<string, TextureSet> textureSets = new Dictionary<string, TextureSet>();
        int CapTextureIndex => PPart.capTextureIndex;
        public enum CapTextureMode
        {
            Ends, Side, GreySide, PlainWhite
        }
        private CapTextureMode CapTexture => (CapTextureMode)CapTextureIndex;

        public Material SidesIconMaterial { get; private set; }
        public Material EndsIconMaterial { get; private set; }

        public Material SidesMaterial { get; private set; }
        public Material EndsMaterial { get; private set; }

        string SidesName => PPart.sidesName;
        string EndsName => PPart.endsName;
        string textureSet  { get => PPart.textureSet; set => PPart.textureSet = value;}

        [SerializeField]
        private Vector2 sideTextureScale = Vector2.one;

        public LegacyTextureHandler(Part part, ProceduralPart ppart) 
        {
            this.part = part;
            this.PPart = ppart;

            Transform sides = part.FindModelTransform(SidesName);
            Transform ends = part.FindModelTransform(EndsName);

            Transform iconModelTransform = part.partInfo.iconPrefab.transform.FindDecendant("model");

            Transform iconSides = iconModelTransform.FindDecendant(SidesName);
            Transform iconEnds = iconModelTransform.FindDecendant(EndsName);

            SidesIconMaterial = (iconSides is Transform) ? iconSides.GetComponent<Renderer>().material : null;
            EndsIconMaterial = (iconEnds is Transform) ? iconEnds.GetComponent<Renderer>().material : null;

            SidesMaterial = sides.GetComponent<Renderer>().material;
            EndsMaterial = ends.GetComponent<Renderer>().material;
        }

        public void ValidateSelectedTexture() 
        {
            if (!textureSets.ContainsKey(textureSet))
            {
                Debug.Log($"{ModTag} Defaulting invalid TextureSet {textureSet} to {textureSets.Keys.First()}");
                textureSet = textureSets.Keys.First();
            }
        }

        public void UpdateTexture()
        {
            // Reset the Material, because TU may steal it.
            SidesMaterial = part.FindModelTransform(SidesName).GetComponent<Renderer>().material;
            EndsMaterial = part.FindModelTransform(EndsName).GetComponent<Renderer>().material;

            Material endsMaterial = (HighLogic.LoadedScene == GameScenes.LOADING) ? EndsIconMaterial : EndsMaterial;
            Material sidesMaterial = (HighLogic.LoadedScene == GameScenes.LOADING) ? SidesIconMaterial : SidesMaterial;

            if (!textureSets.ContainsKey(textureSet))
            {
                Debug.LogError($"{ModTag} UpdateTexture() {textureSet} missing from global list!");
                textureSet = textureSets.Keys.First();
            }
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
            if (tex.sidesBump is Texture)
            {
                sidesMaterial.SetTextureScale("_BumpMap", scaleUV);
                sidesMaterial.SetTextureOffset("_BumpMap", Vector2.zero);
                sidesMaterial.SetTexture("_BumpMap", tex.sidesBump);
            }
            if (endsMaterial is Material)
            {
                SetupEndsTexture(endsMaterial, tex, scaleUV);
            }
        }

        public string MatToStr(Material m) => $"Material {m.name} | {m.color} | {m.mainTexture} | scale: {m.mainTextureScale} | {m.shader}: {string.Join(",", m.shaderKeywords)}";

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
    }
}
