using System;
using System.Linq;
using SaveUpgradePipeline;
using UnityEngine;

namespace ProceduralParts
{
    [UpgradeModule(LoadContext.SFS | LoadContext.Craft, sfsNodeUrl = "GAME/FLIGHTSTATE/VESSEL/PART", craftNodeUrl = "PART")]
    public class ShapeBezierConeUpgrade : UpgradeScript
    {
        public override string Name { get => "ProceduralParts 2.1 Bezier Cone Upgrader"; }
        public override string Description { get => "Updates ProceduralParts Bezier Shapes to Custom Settings"; }
        public override Version EarliestCompatibleVersion { get => new Version(0, 0, 0); }
        public override Version TargetVersion { get => new Version(2, 1, 0); }

        public override TestResult OnTest(ConfigNode node, LoadContext loadContext, ref string nodeName)
        {
            nodeName = NodeUtil.GetPartNodeNameValue(node, loadContext);
            TestResult res = TestResult.Pass;
            if (node.GetNode("MODULE", "name", "ProceduralShapeBezierCone") is ConfigNode bezierNode)
                res = bezierNode.HasValue("shapePoints") ? TestResult.Pass : TestResult.Upgradeable;
            return res;
        }

        public override void OnUpgrade(ConfigNode node, LoadContext loadContext, ConfigNode parentNode)
        {
            string selectedShape = "Custom";
            Vector4 shapePoints = Vector4.zero;
            var bezierNode = node.GetNode("MODULE", "name", "ProceduralShapeBezierCone");
            if (bezierNode.TryGetValue("selectedShape", ref selectedShape)
                && ProceduralShapeBezierCone.shapePresets.Values.FirstOrDefault(x => x.displayName.Equals(selectedShape)) is var preset)
            {
                    selectedShape = preset.name;
                    shapePoints = preset.points;
            }
            bezierNode.SetValue("selectedShape", selectedShape, true);
            bezierNode.SetValue("shapePoints", shapePoints, true);
            Debug.Log($"[ProceduralParts] UpgradePipeline context {loadContext} updated part {NodeUtil.GetPartNodeNameValue(node, loadContext)} to {selectedShape}: {shapePoints}");
        }
    }

    public class ShapeBezierConeUpgrade_KCTBase : ShapeBezierConeUpgrade
    {
        public override string Name { get => "ProceduralParts 2.1 Bezier Cone Upgrader KCT-" + nodeUrlSFS; }
        public override TestResult OnTest(ConfigNode node, LoadContext loadContext, ref string nodeName) =>
            loadContext == LoadContext.Craft ? TestResult.Pass : base.OnTest(node, loadContext, ref nodeName);
    }

    [UpgradeModule(LoadContext.SFS, sfsNodeUrl = "GAME/SCENARIO/KSC/VABList/KCTVessel/ShipNode/PART", craftNodeUrl = "PART")]
    public class ShapeBezierConeUpgrade_KCT1 : ShapeBezierConeUpgrade_KCTBase { }

    [UpgradeModule(LoadContext.SFS, sfsNodeUrl = "GAME/SCENARIO/KSC/SPHList/KCTVessel/ShipNode/PART", craftNodeUrl = "PART")]
    public class ShapeBezierConeUpgrade_KCT2 : ShapeBezierConeUpgrade_KCTBase { }

    [UpgradeModule(LoadContext.SFS, sfsNodeUrl = "GAME/SCENARIO/KSC/VABWarehouse/KCTVessel/ShipNode/PART", craftNodeUrl = "PART")]
    public class ShapeBezierConeUpgrade_KCT3 : ShapeBezierConeUpgrade_KCTBase { }

    [UpgradeModule(LoadContext.SFS, sfsNodeUrl = "GAME/SCENARIO/KSC/SPHWarehouse/KCTVessel/ShipNode/PART", craftNodeUrl = "PART")]
    public class ShapeBezierConeUpgrade_KCT4 : ShapeBezierConeUpgrade_KCTBase { }

    [UpgradeModule(LoadContext.SFS, sfsNodeUrl = "GAME/SCENARIO/KSC/VABPlans/KCTVessel/ShipNode/PART", craftNodeUrl = "PART")]
    public class ShapeBezierConeUpgrade_KCT5 : ShapeBezierConeUpgrade_KCTBase { }

    [UpgradeModule(LoadContext.SFS, sfsNodeUrl = "GAME/SCENARIO/KSC/SPHPlans/KCTVessel/ShipNode/PART", craftNodeUrl = "PART")]
    public class ShapeBezierConeUpgrade_KCT6 : ShapeBezierConeUpgrade_KCTBase { }
}
