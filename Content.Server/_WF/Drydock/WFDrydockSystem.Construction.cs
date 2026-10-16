using Content.Server.Construction.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Construction.Steps;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

public sealed partial class WFDrydockSystem
{
    /// <summary>Construction materials by entity prototype, in material units.</summary>
    private readonly Dictionary<string, Dictionary<string, int>> _constructionMaterials = new();

    /// <summary>
    /// Materials to build an entity by hand: every material step on its construction graph from the start node to its
    /// node, and for a machine the sheets its board calls for, such as a jump drive's fissile. Empty without a graph.
    /// </summary>
    private Dictionary<string, int> ConstructionMaterials(EntityPrototype proto)
    {
        if (_constructionMaterials.TryGetValue(proto.ID, out var cached))
            return cached;

        var materials = new Dictionary<string, int>();
        _constructionMaterials[proto.ID] = materials;
        if (!proto.TryGetComponent<ConstructionComponent>(out var construction, Factory) ||
            !_prototype.TryIndex<ConstructionGraphPrototype>(construction.Graph, out var graph) ||
            graph.Start is not { } start ||
            !graph.TryPath(start, construction.Node, out var path))
        {
            return materials;
        }

        var current = graph.Nodes[start];
        foreach (var node in path)
        {
            var edge = current.GetEdge(node.Name);
            current = node;
            if (edge == null)
                continue;

            foreach (var step in edge.Steps)
            {
                if (step is MaterialConstructionGraphStep materialStep)
                    AddSheets(materials, materialStep.MaterialPrototypeId, materialStep.Amount);
            }
        }

        if (proto.TryGetComponent<MachineComponent>(out var machine, Factory) && machine.Board is { } board &&
            _prototype.Index(board).TryGetComponent<MachineBoardComponent>(out var boardComp, Factory))
        {
            // Sheets only: a break strips them with the machine, while cable comes off whole with its parts.
            foreach (var (stack, amount) in boardComp.StackRequirements)
            {
                if (_prototype.TryIndex(stack, out var stackProto) &&
                    _prototype.Index(stackProto.Spawn).TryGetComponent<MaterialComponent>(out _, Factory))
                    AddSheets(materials, stack.Id, amount);
            }
        }

        return materials;
    }

    /// <summary>Adds the materials of a number of sheets of a stack.</summary>
    private void AddSheets(Dictionary<string, int> materials, string stackId, int count)
    {
        if (!_prototype.TryIndex<StackPrototype>(stackId, out var stack) ||
            !_prototype.TryIndex(stack.Spawn, out var sheet) ||
            !sheet.TryGetComponent<PhysicalCompositionComponent>(out var composition, Factory))
        {
            return;
        }

        foreach (var (material, amount) in composition.MaterialComposition)
        {
            materials[material] = materials.GetValueOrDefault(material) + amount * count;
        }
    }

    /// <summary>Market value of a set of materials, from each material's price per unit.</summary>
    private double MaterialValue(Dictionary<string, int> materials)
    {
        var value = 0d;
        foreach (var (id, amount) in materials)
        {
            if (_prototype.TryIndex<MaterialPrototype>(id, out var material))
                value += material.Price * amount;
        }

        return value;
    }
}
