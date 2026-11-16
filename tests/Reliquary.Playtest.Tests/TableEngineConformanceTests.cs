using System.Text.Json;
using System.Text.Json.Nodes;
using Reliquary.Playtest.Engine;

namespace Reliquary.Playtest.Tests;

/// <summary>
/// Replays the conformance vectors of web/src/playtest/engine.ts with the C# engine.
/// The vectors are in tests/vectors/table-engine. The web test engineVectors.test.ts writes them and keeps them up to date.
/// Each state of the C# engine must have the same JSON as the state of engine.ts.
/// </summary>
public sealed class TableEngineConformanceTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "vectors", "table-engine");

    public static TheoryData<string> Vectors()
    {
        var names = Directory.GetFiles(Folder, "*.json").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal).ToList();
        return new TheoryData<string>(names!);
    }

    [Fact]
    public void FolderHasTheVectorsOfEachTableSize()
    {
        var names = Vectors().Select(row => row.Data).ToList();

        Assert.Contains("2-players", names);
        Assert.Contains("3-players-commander", names);
        Assert.Contains("4-players", names);
        Assert.Contains("2-players-unsigned-seed", names);
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void EngineGivesTheStatesOfTheTypeScriptEngine(string name)
    {
        var vector = JsonNode.Parse(File.ReadAllText(Path.Combine(Folder, $"{name}.json")))!.AsObject();
        var setups = vector["setups"].Deserialize<List<PlayerSetup>>(TableJson.Options)!;

        var state = TableEngine.CreateTable(
            setups,
            vector["seed"]!.GetValue<long>(),
            vector["startingLife"]!.GetValue<int>(),
            vector["commander"]!.GetValue<bool>());

        var expected = vector["initial"]!.DeepClone();
        AssertSameState(expected, state, $"{name}: initial state");

        var steps = vector["steps"]!.AsArray();
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index]!;
            var action = step["action"].Deserialize<TableAction>(TableJson.Options)!;
            state = TableEngine.Apply(state, action);
            expected = ApplyChanges(expected, step["changes"]!.AsArray());
            AssertSameState(expected, state, $"{name}: step {index}, action {step["action"]!.ToJsonString()}");
        }

        AssertSameState(vector["final"]!, state, $"{name}: final state");
    }

    private static void AssertSameState(JsonNode expected, TableState state, string message)
    {
        var actual = JsonSerializer.Serialize(state, TableJson.Options);

        // The same text: the same values, the same order of the properties and the same numbers.
        Assert.True(expected.ToJsonString() == actual, $"{message}{Environment.NewLine}expected: {expected.ToJsonString()}{Environment.NewLine}actual:   {actual}");
    }

    /// <summary>
    /// Applies the changes of one step, as engineVectors.ts writes them: the path of a value in the JSON of the state, and its new value.
    /// </summary>
    private static JsonNode ApplyChanges(JsonNode state, JsonArray changes)
    {
        foreach (var change in changes)
        {
            var path = change![0]!.AsArray();
            var value = change[1]?.DeepClone();
            if (path.Count == 0)
            {
                state = value!;
                continue;
            }

            var target = state;
            for (var index = 0; index < path.Count - 1; index++)
            {
                target = Child(target, path[index]!)!;
            }

            var last = path[^1]!;
            if (last.GetValueKind() == JsonValueKind.Number)
            {
                target.AsArray()[last.GetValue<int>()] = value;
            }
            else
            {
                target.AsObject()[last.GetValue<string>()] = value;
            }
        }

        return state;
    }

    private static JsonNode? Child(JsonNode node, JsonNode key) =>
        key.GetValueKind() == JsonValueKind.Number ? node.AsArray()[key.GetValue<int>()] : node.AsObject()[key.GetValue<string>()];
}
