using System.Text.Json.Nodes;

namespace VoiceAgent.Core.Agent;

public sealed record AgentTool(string Name, string Description, JsonObject Parameters)
{
    /// <summary>Function tool in the shape used by Foundry agents and the Voice Live session.update event.</summary>
    public JsonObject ToFunctionTool() => new()
    {
        ["type"] = "function",
        ["name"] = Name,
        ["description"] = Description,
        ["parameters"] = Parameters.DeepClone(),
    };
}

public static class ToolNames
{
    public const string RecordCallReason = "record_call_reason";
    public const string FindCustomer = "find_customer";
    public const string VerifyIdentity = "verify_identity";
    public const string GetCaseDetails = "get_case_details";
    public const string TransferToAgent = "transfer_to_agent";
}

/// <summary>
/// The single source of truth for the voice agent: used by the provisioning tool to create the
/// Foundry agent, and by the API when Voice Live runs in "model" mode.
/// </summary>
public static class AgentDefinition
{
    public const string Name = "customer-service-voice-agent";
    public const string Description = "Answers customer service phone calls, identifies the caller's case and verifies their identity.";

    public static readonly string[] CallCategories =
        ["Billing", "Technical fault", "Delivery", "Complaint", "Account change", "Sales", "Other"];

    public const string Instructions = """
        You are Ava, the automated phone assistant for Contoso Customer Services. You are speaking to a caller on the phone.

        # Style
        - Speak in British English. Be warm, calm and concise: one or two short sentences at a time.
        - Ask one question at a time and wait for the answer.
        - Never read out long lists, internal codes or JSON. Spell out numbers naturally.
        - If you did not catch something, politely ask the caller to repeat it.

        # Call flow
        1. Greet the caller: "Thanks for calling Contoso Customer Services, you're speaking with Ava, an automated assistant. This call is transcribed for quality and training. How can I help today?"
        2. Understand why they are calling. Once you understand, call record_call_reason with a one-sentence summary and the best category.
        3. Identify the case: ask for their account number, or their case reference if they have one (case references look like "CS" followed by four digits). Call find_customer.
           - If no customer is found, ask them to check and repeat it once. If it is still not found, offer to transfer them to a colleague.
        4. Security: tell the caller you need to ask a few security questions. Ask, one at a time, for:
           a. their date of birth,
           b. the postcode on the account,
           c. their memorable word.
           Then call verify_identity with all three answers exactly as the caller said them.
           - Never repeat the answers back, and never say which answer was wrong.
           - If the result says a retry is allowed, say you couldn't verify those details and ask all three questions again.
           - If the result says the caller is being transferred, apologise, explain a colleague will help them complete security, and do not ask any more security questions.
        5. Once verified, call get_case_details and give the caller a short update: the status, the latest update and the next step.
           Answer simple questions using only the case details you were given.
        6. You cannot make changes, take payments, or resolve complaints yourself. If the caller needs any of these, or asks for a person at any time, call transfer_to_agent with a short reason, then tell them you're transferring them to a colleague and to stay on the line.

        # Rules
        - Never share any account or case details before verify_identity has succeeded.
        - Never guess or invent information. If you don't know, offer a transfer.
        - Never ask for full card numbers, passwords or PINs.
        - If the caller is distressed or vulnerable, offer to transfer them to a colleague straight away.
        - When the caller has everything they need, thank them and say goodbye.
        """;

    public static IReadOnlyList<AgentTool> Tools { get; } =
    [
        new(ToolNames.RecordCallReason,
            "Record why the customer is calling, once you understand their reason.",
            Schema(
                required: ["reason", "category"],
                ("reason", StringProp("One-sentence summary of why the customer is calling.")),
                ("category", EnumProp("Best matching category for the call.", CallCategories)))),

        new(ToolNames.FindCustomer,
            "Look up the customer's account using their account number or a case reference. Returns whether a match was found; it does not return personal details.",
            Schema(
                required: [],
                ("account_number", StringProp("The account number exactly as the caller said it.")),
                ("case_reference", StringProp("The case reference exactly as the caller said it, e.g. CS1042.")))),

        new(ToolNames.VerifyIdentity,
            "Check the caller's security answers against the account found by find_customer. Returns whether they passed and what to do next.",
            Schema(
                required: ["date_of_birth", "postcode", "memorable_word"],
                ("date_of_birth", StringProp("Date of birth exactly as the caller said it.")),
                ("postcode", StringProp("Postcode exactly as the caller said it.")),
                ("memorable_word", StringProp("Memorable word exactly as the caller said it.")))),

        new(ToolNames.GetCaseDetails,
            "Get the verified customer's case details. Only works after verify_identity has succeeded.",
            Schema(
                required: [],
                ("case_reference", StringProp("Optional case reference. If omitted, all open cases are returned.")))),

        new(ToolNames.TransferToAgent,
            "Put the caller in the queue for a human customer service agent.",
            Schema(
                required: ["reason"],
                ("reason", StringProp("Short reason for the transfer, shown to the human agent.")))),
    ];

    private static JsonObject StringProp(string description) => new() { ["type"] = "string", ["description"] = description };

    private static JsonObject EnumProp(string description, IEnumerable<string> values) => new()
    {
        ["type"] = "string",
        ["description"] = description,
        ["enum"] = new JsonArray(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()),
    };

    private static JsonObject Schema(string[] required, params (string Name, JsonObject Schema)[] properties)
    {
        var props = new JsonObject();
        foreach (var (name, schema) in properties) props[name] = schema;

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()),
        };
    }
}
