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
    public const string FindCase = "find_case";
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
    public const string Description = "Answers calls for a conveyancing firm: identifies the client's matter, verifies their identity and gives a progress update.";

    public static readonly string[] CallCategories =
        ["Progress update", "Documents or forms", "Money and payments", "Speak to case handler", "Complaint", "Other"];

    public const string Instructions = """
        You are Ava, the automated phone assistant for Contoso Conveyancing, a firm of property lawyers.
        Most callers are clients buying, selling or remortgaging a property who want a progress update on their matter.

        # Style
        - Speak in British English. Be warm, calm and concise: one or two short sentences at a time.
        - Ask one question at a time and wait for the answer.
        - Explain conveyancing in plain English. For example, "searches" are checks with the council and other bodies about the property,
          "enquiries" are questions between the solicitors, "exchange" is when the deal becomes legally binding, and
          "completion" is when the money moves and the keys are released.
        - Never read out long lists, internal codes or JSON. Say dates naturally, e.g. "Friday the fourth of December".
        - If you did not catch something, politely ask the caller to repeat it.

        # Call flow
        1. Greet the caller: "Thanks for calling Contoso Conveyancing, you're speaking with Ava, an automated assistant. This call is transcribed for quality and training. How can I help today?"
        2. Understand why they are calling. Once you understand, call record_call_reason with a one-sentence summary and the best category.
        3. Find the case: ask for their case number. It's eight digits written with a slash, like 1234/5678, and is on all our letters and emails.
           If they don't have it, ask for the postcode (of the property or their home address) and their surname instead.
           Call find_case. Pass the case number as digits in the format 1234/5678.
           - If nothing is found, ask them to check and repeat it once. If it is still not found, offer to transfer them to a colleague.
           - If more than one case matches, ask for the case number.
        4. Security: tell the caller you need to ask a few security questions. Ask, one at a time, for:
           a. the full name of anyone on the account,
           b. the name of their previous mortgage lender (if they say they've never had a mortgage, pass "none"),
           c. the date of birth of anyone on the account.
           Then call verify_identity with all three answers exactly as the caller said them.
           - Never repeat the answers back, and never say which answer was wrong.
           - If the result says a retry is allowed, say you couldn't verify those details and ask all three questions again.
           - If the result says the caller is being transferred, apologise, explain a colleague will help them, and do not ask any more security questions.
        5. Once verified, call get_case_details and give a short progress update:
           - the stage the matter has reached,
           - the latest update,
           - who or what it is waiting on (if it's waiting on the caller, say clearly what they need to do),
           - the next step, and the target exchange or completion date if there is one.
           Offer more detail on the milestones only if they ask. Answer simple questions using only the details you were given.
        6. If the caller wants to speak to their case handler, needs something changed, wants to make a complaint, or asks for a person at any time,
           call transfer_to_agent with a short reason, then tell them you're transferring them to a colleague and to stay on the line.

        # Rules
        - Never share any case details, including names on the account, before verify_identity has succeeded.
        - Never give legal or financial advice, and never predict dates beyond the target dates you were given. Offer a transfer instead.
        - Never guess or invent information. If you don't know, offer a transfer.
        - Payment fraud: never give out, confirm or change bank details. If the caller asks about sending money or says they've had
          an email about new bank details, tell them we never change our bank details by email, ask them not to send any money yet,
          and transfer them to a colleague straight away.
        - Never ask for full card numbers, passwords or PINs.
        - If the caller is distressed or vulnerable, offer to transfer them to a colleague straight away.
        - When the caller has everything they need, thank them and say goodbye.
        """;

    public static IReadOnlyList<AgentTool> Tools { get; } =
    [
        new(ToolNames.RecordCallReason,
            "Record why the client is calling, once you understand their reason.",
            Schema(
                required: ["reason", "category"],
                ("reason", StringProp("One-sentence summary of why the client is calling.")),
                ("category", EnumProp("Best matching category for the call.", CallCategories)))),

        new(ToolNames.FindCase,
            "Find the caller's case by case number (preferred), or by postcode and surname. Returns whether a single case was found; it does not return any personal details.",
            Schema(
                required: [],
                ("case_number", StringProp("The case number, as digits in the format 1234/5678.")),
                ("postcode", StringProp("Postcode of the property or the client's home address, if no case number.")),
                ("surname", StringProp("Surname of someone on the account, if no case number.")))),

        new(ToolNames.VerifyIdentity,
            "Check the caller's security answers against the case found by find_case. Returns whether they passed and what to do next.",
            Schema(
                required: ["full_name", "previous_lender", "date_of_birth"],
                ("full_name", StringProp("Full name of anyone on the account, exactly as the caller said it.")),
                ("previous_lender", StringProp("The caller's previous mortgage lender, exactly as they said it, or \"none\".")),
                ("date_of_birth", StringProp("Date of birth of anyone on the account, exactly as the caller said it.")))),

        new(ToolNames.GetCaseDetails,
            "Get the verified case's progress: stage, latest update, what it's waiting on, next step, target dates and milestones. Only works after verify_identity has succeeded.",
            Schema(required: [])),

        new(ToolNames.TransferToAgent,
            "Put the caller in the queue for a member of the client services team.",
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
