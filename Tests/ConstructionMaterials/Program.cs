using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Khemistry;
using KhemistryConstructionOverhaul;

internal static class Program
{
    private static int _assertions;

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new Exception(message);
    }

    private static KhemistryMaterial MakeMaterial()
    {
        ConfigNode node = new ConfigNode("KHEMISTRY_MATERIAL");
        node.AddValue("name", "Test Wood");
        ConfigNode shapes = node.AddNode("SHAPES");
        shapes.AddValue("name", "Log");
        ConfigNode parameters = node.AddNode("PARAMS");
        parameters.AddValue("grade", "A");
        KhemistryMaterial material = new KhemistryMaterial(node);
        Assert(material.IsValid, "test material definition must be valid");
        return material;
    }

    private static KhemistryMaterialInstance MakeInstance(KhemistryMaterial definition,
        string grade, int amount)
    {
        return new KhemistryMaterialInstance(definition, "Log", "none", 2f,
            new Dictionary<string, string> { ["grade"] = grade })
        {
            amount = amount
        };
    }

    private static void InstallShared(KhemistryMaterial material,
        List<KhemistryMaterialInstance> contents)
    {
        KShared shared = (KShared)FormatterServices.GetUninitializedObject(typeof(KShared));
        shared.materialList = new List<KhemistryMaterial> { material };
        shared.ResourceDict = new Dictionary<string, double>();
        typeof(KShared).GetField("KSCMaterialContents")?.SetValue(shared, contents);
        typeof(KShared).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, shared);
        Assert(ReferenceEquals(KShared.Instance, shared), "KShared test instance installation");
    }

    private static void Main()
    {
        KhemistryMaterial definition = MakeMaterial();
        List<KhemistryMaterialInstance> contents = new List<KhemistryMaterialInstance>();
        InstallShared(definition, contents);

        Assert(KhemistryConstructionMaterials.AddNormal(contents,
            MakeInstance(definition, "A", 2)), "add first exact stack");
        Assert(KhemistryConstructionMaterials.AddNormal(contents,
            MakeInstance(definition, "A", 1)), "merge second exact stack");
        Assert(KhemistryConstructionMaterials.AddNormal(contents,
            MakeInstance(definition, "B", 4)), "add parameter variant");
        Assert(contents.Count == 2, "normal merge must not contamination-merge variants");
        Assert(contents[0].amount == 3, "normal merge combines exact amounts");

        ConfigNode costNode = new ConfigNode("MATERIAL_COST");
        costNode.AddValue("name", "Test Wood");
        costNode.AddValue("shape", "Log");
        costNode.AddValue("size", "none");
        costNode.AddValue("amount", "4");
        costNode.AddNode("PARAM_REQUIREMENTS").AddValue("grade", "A");
        Assert(KhemistryConstructionMaterials.TryParseCost(costNode, "testPart",
            out KhemistryISRURecipe.ResourceInputMaterial cost), "parse MATERIAL_COST");
        Assert(cost.id == null && cost.amount == 4 && cost.parameters.Single(p => p.Key == "grade").Value == "A",
            "MATERIAL_COST mirrors INPUT_MATERIAL without id");

        Assert(!KhemistryConstructionMaterials.TryConsumeRequirements(contents,
                new[] { cost }, out _, out string error),
            "parameter-aware shortage must fail");
        Assert(error == "Missing 1 of Test Wood with shape Log!",
            "shortage message must omit parameter details");
        Assert(contents.Count == 2 && contents[0].amount == 3 && contents[1].amount == 4,
            "failed dry run must not mutate KSC contents");

        cost.amount = 2;
        Assert(KhemistryConstructionMaterials.TryConsumeRequirements(contents,
                new[] { cost }, out List<KhemistryMaterialInstance> remaining, out error),
            "satisfied material cost");
        Assert(string.IsNullOrEmpty(error), "successful check has no error");
        Assert(remaining.Count == 2 && remaining[0].amount + remaining[1].amount == 5,
            "successful dry run returns deducted copy");
        Assert(contents[0].amount + contents[1].amount == 7,
            "successful dry run still leaves live ledger unchanged");

        Console.WriteLine(_assertions + " assertions passed.");
    }
}
