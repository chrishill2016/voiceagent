using System.Security.Cryptography;
using System.Text;

namespace VoiceAgent.Api.Features.Telephony;

/// <summary>Checks the shared ?token= on webhook and media URLs so only ACS / Event Grid can call them.</summary>
public static class CallbackToken
{
    public static bool IsValid(HttpContext context, AcsOptions options)
    {
        if (string.IsNullOrEmpty(options.CallbackToken)) return true;

        var supplied = context.Request.Query["token"].ToString();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(supplied),
            Encoding.UTF8.GetBytes(options.CallbackToken));
    }

    public static string Append(string url, AcsOptions options) =>
        string.IsNullOrEmpty(options.CallbackToken) ? url : $"{url}?token={Uri.EscapeDataString(options.CallbackToken)}";
}
