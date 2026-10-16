using System.Text.Json.Nodes;
using Mugirl.GameValidation.Core;

namespace Mugirl.GameValidation.OfflineTests;

/// <summary>按升级清单（K1）的形状拼出测试用的 JSON，再解析成 <see cref="SaveNameMap"/>。</summary>
internal sealed class K1Builder
{
    private readonly JsonArray types = [];
    private readonly JsonArray defs = [];
    private readonly JsonArray labels = [];
    private readonly JsonArray enums = [];
    private readonly JsonArray values = [];
    private readonly JsonArray valueContexts = [];
    private readonly JsonArray enumSites = [];
    private readonly JsonArray defaults = [];
    private readonly JsonArray steps = [];
    private readonly JsonArray jobPolicies = [];
    private readonly JsonArray lordPolicies = [];

    public K1Builder Type(string old, string? now)
    {
        types.Add(new JsonObject { ["old"] = old, ["new"] = now, ["disposition"] = now is null ? "drop" : "rename", ["module"] = "Test", ["reason"] = "test" });
        return this;
    }

    public K1Builder Def(string root, string old, string? now)
    {
        defs.Add(new JsonObject { ["root"] = root, ["old"] = old, ["new"] = now, ["disposition"] = now is null ? "drop" : "rename", ["module"] = "Test", ["reason"] = "test" });
        return this;
    }

    public K1Builder Label(string owner, string old, string? now, string disposition = "rename", string? step = null)
    {
        labels.Add(new JsonObject { ["owner"] = owner, ["old"] = old, ["new"] = now, ["disposition"] = disposition, ["step"] = step, ["module"] = "Test", ["reason"] = "test" });
        return this;
    }

    public K1Builder Enum(string type, string old, string now)
    {
        enums.Add(new JsonObject { ["enum"] = type, ["old"] = old, ["new"] = now, ["module"] = "Test" });
        return this;
    }

    public K1Builder Value(string context, string old, string? now)
    {
        values.Add(new JsonObject { ["context"] = context, ["old"] = old, ["new"] = now, ["module"] = "Test" });
        return this;
    }

    public K1Builder Default(string owner, string label, string oldDefault)
    {
        defaults.Add(new JsonObject { ["owner"] = owner, ["label"] = label, ["oldDefault"] = oldDefault, ["module"] = "Test" });
        return this;
    }

    public K1Builder ValueContext(string name, string owner, string label, string part = "items", string kind = "text", string? defRoot = null)
    {
        valueContexts.Add(new JsonObject { ["name"] = name, ["owner"] = owner, ["label"] = label, ["part"] = part, ["kind"] = kind, ["defRoot"] = defRoot });
        return this;
    }

    public K1Builder EnumSite(string type, string owner, string label, string part = "items", bool flags = false)
    {
        enumSites.Add(new JsonObject { ["enum"] = type, ["owner"] = owner, ["label"] = label, ["part"] = part, ["flags"] = flags });
        return this;
    }

    public K1Builder Step(string id, params string[] consumes)
    {
        steps.Add(new JsonObject { ["id"] = id, ["phase"] = "dom", ["module"] = "Test", ["consumes"] = new JsonArray([.. consumes.Select(c => (JsonNode?)JsonValue.Create(c))]), ["reason"] = "test" });
        return this;
    }

    public K1Builder JobPolicy(string type, string policy)
    {
        jobPolicies.Add(new JsonObject { ["type"] = type, ["policy"] = policy });
        return this;
    }

    public K1Builder LordPolicy(string type, string policy)
    {
        lordPolicies.Add(new JsonObject { ["type"] = type, ["policy"] = policy });
        return this;
    }

    public string Json() => new JsonObject
    {
        ["schema"] = 1,
        ["stampLabel"] = "mugirlSaveSchema",
        ["stampType"] = "Mugirl.Core.GameComponent_Mugirl",
        ["types"] = types.DeepClone(),
        ["defs"] = defs.DeepClone(),
        ["labels"] = labels.DeepClone(),
        ["enums"] = enums.DeepClone(),
        ["values"] = values.DeepClone(),
        ["valueContexts"] = valueContexts.DeepClone(),
        ["enumSites"] = enumSites.DeepClone(),
        ["defaults"] = defaults.DeepClone(),
        ["steps"] = steps.DeepClone(),
        ["jobPolicies"] = jobPolicies.DeepClone(),
        ["lordPolicies"] = lordPolicies.DeepClone(),
        ["settings"] = new JsonObject { ["fileClass"] = new JsonObject { ["old"] = "Mugirl.MugirlSettings", ["new"] = "Mugirl.Core.MugirlSettings" }, ["labels"] = new JsonArray(), ["schema"] = 1 },
        ["moogirlEra"] = new JsonObject { ["typePrefixes"] = new JsonArray(), ["defPatterns"] = new JsonArray() },
    }.ToJsonString();

    public SaveNameMap Build() => SaveNameMap.Parse(Json());
}
