using System.Text.Json;
using System.Text.Json.Nodes;
using Reliquary.Playtest.Engine;

namespace Reliquary.Playtest.Tests;

/// <summary>
/// Checks that the JSON of the C# state types is the JSON of the types of web/src/playtest/engine.ts.
/// </summary>
public sealed class TableStateTests
{
    // A state as JSON.stringify writes it in the web app, with the property order of engine.ts.
    private const string EngineStateJson = """
        {
          "players": [
            {
              "id": "player-1",
              "name": "Ana",
              "life": 38,
              "commanderDamage": { "player-2": 4 },
              "counters": { "poison": 1, "energy": 0, "experience": 2 },
              "mulligans": 1,
              "cardsToBottom": 0,
              "zones": {
                "library": [
                  { "id": "c-2", "oracleId": "6ad8011d-3471-4369-9d68-b264cc027487", "name": "Forest", "imageUri": null, "isLand": true, "manaValue": 0, "isToken": false, "tapped": false, "faceDown": false, "counters": 0 }
                ],
                "hand": [],
                "battlefield": [
                  { "id": "c-1", "oracleId": "6ad8011d-3471-4369-9d68-b264cc027488", "name": "Sol Ring", "imageUri": "https://cards.example.org/sol.jpg", "isLand": false, "manaValue": 1, "isToken": false, "tapped": true, "faceDown": false, "counters": 3 },
                  { "id": "token-1", "oracleId": "", "name": "Goblin", "imageUri": null, "isLand": false, "manaValue": 0, "isToken": true, "tapped": false, "faceDown": true, "counters": 0 }
                ],
                "graveyard": [],
                "exile": [],
                "command": [
                  { "id": "c-3", "oracleId": "6ad8011d-3471-4369-9d68-b264cc027489", "name": "Little Girl", "imageUri": null, "isLand": false, "manaValue": 0.5, "isToken": false, "tapped": false, "faceDown": false, "counters": 0 }
                ]
              }
            }
          ],
          "commander": true,
          "activePlayerId": "player-1",
          "turn": 4,
          "seed": -1352854234,
          "nextTokenId": 2
        }
        """;

    [Fact]
    public void StateHasTheJsonOfTheTypeScriptEngine()
    {
        var state = JsonSerializer.Deserialize<TableState>(EngineStateJson, TableJson.Options)!;

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(EngineStateJson), JsonNode.Parse(ToJson(state))), ToJson(state));
        var player = Assert.Single(state.Players);
        Assert.Equal(new PlayerCounters(1, 0, 2), player.Counters);
        Assert.Equal(4, player.CommanderDamage["player-2"]);
        Assert.Equal(["c-1", "token-1"], player.Zones.Get(TableZone.Battlefield).Select(card => card.Id));
        Assert.Equal(0.5, player.Zones.Command[0].ManaValue);
        Assert.Equal(-1352854234L, state.Seed);
        Assert.True(state.Commander);
    }

    [Fact]
    public void SeedKeepsAnUnsignedValue()
    {
        // The web app gives a new game a seed from 0 to 2^32 - 1. engine.ts keeps it until the first shuffle.
        var state = SampleState() with { Seed = uint.MaxValue };

        var copy = JsonSerializer.Deserialize<TableState>(ToJson(state), TableJson.Options)!;

        Assert.Equal(uint.MaxValue, copy.Seed);
    }

    [Theory]
    [InlineData("""{"type":"draw","playerId":"p1"}""")]
    [InlineData("""{"type":"draw","playerId":"p1","count":3}""")]
    [InlineData("""{"type":"shuffle","playerId":"p1"}""")]
    [InlineData("""{"type":"mulligan","playerId":"p1"}""")]
    [InlineData("""{"type":"bottom","playerId":"p1","cardId":"c-1"}""")]
    [InlineData("""{"type":"move","playerId":"p1","cardId":"c-1","to":"graveyard"}""")]
    [InlineData("""{"type":"move","playerId":"p1","cardId":"c-1","to":"library","position":"bottom"}""")]
    [InlineData("""{"type":"tap","playerId":"p1","cardId":"c-1"}""")]
    [InlineData("""{"type":"untapAll","playerId":"p1"}""")]
    [InlineData("""{"type":"flip","playerId":"p1","cardId":"c-1"}""")]
    [InlineData("""{"type":"counter","playerId":"p1","cardId":"c-1","change":-1}""")]
    [InlineData("""{"type":"token","playerId":"p1","name":"Goblin"}""")]
    [InlineData("""{"type":"life","playerId":"p1","change":-3}""")]
    [InlineData("""{"type":"commanderDamage","playerId":"p1","fromPlayerId":"p2","change":5}""")]
    [InlineData("""{"type":"playerCounter","playerId":"p1","counter":"poison","change":1}""")]
    [InlineData("""{"type":"nextTurn"}""")]
    public void EachActionOfTheEngineHasTheSameJson(string json)
    {
        var action = JsonSerializer.Deserialize<TableAction>(json, TableJson.Options)!;

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(JsonSerializer.Serialize(action, TableJson.Options))));
    }

    [Fact]
    public void ActionWithTheTypeAfterTheOtherPropertiesIsRead()
    {
        var action = JsonSerializer.Deserialize<TableAction>("""{"playerId":"p1","count":2,"type":"draw"}""", TableJson.Options);

        Assert.Equal(new DrawAction("p1", 2), action);
    }

    [Theory]
    [InlineData("""{"type":"steal","playerId":"p1"}""")]
    [InlineData("""{"playerId":"p1"}""")]
    [InlineData("""{"type":"move","playerId":"p1","cardId":"c-1","to":"sideboard"}""")]
    public void UnknownActionIsAnError(string json)
    {
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<TableAction>(json, TableJson.Options));
    }

    [Fact]
    public void SetupHasTheJsonOfTheTypeScriptEngine()
    {
        const string json = """
            {"id":"player-1","name":"Ana","library":[{"id":"c-1","oracleId":"o-1","name":"Forest","imageUri":null,"isLand":true,"manaValue":0}],"command":[]}
            """;

        var setup = JsonSerializer.Deserialize<PlayerSetup>(json, TableJson.Options)!;

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(JsonSerializer.Serialize(setup, TableJson.Options))));
    }

    [Fact]
    public void ZonesGiveAndReplaceEachZone()
    {
        var card = SampleState().Players[0].Zones.Hand[0];

        foreach (var zone in Enum.GetValues<TableZone>())
        {
            var zones = TableZones.Empty.With(zone, [card]);
            Assert.Equal([card], zones.Get(zone));
            Assert.Equal(1, Enum.GetValues<TableZone>().Sum(other => zones.Get(other).Count));
        }
    }

    /// <summary>Gives a small state with two players.</summary>
    internal static TableState SampleState()
    {
        static TableCard Card(string id, string name) => new(id, Guid.Empty.ToString(), name, null, false, 1, false, false, false, 0);
        static TablePlayer Player(string id) => new(
            id,
            id,
            40,
            new Dictionary<string, int>(),
            PlayerCounters.None,
            0,
            0,
            TableZones.Empty with { Library = [Card($"{id}-1", "Forest")], Hand = [Card($"{id}-2", "Sol Ring")] });
        return new TableState([Player("player-1"), Player("player-2")], true, "player-1", 1, 42, 1);
    }

    internal static string ToJson(TableState state) => JsonSerializer.Serialize(state, TableJson.Options);
}
