namespace VoiceAgent.Tests.Portal;

/// <summary>
/// The portal is served by the API with MapStaticAssets. Build-time placeholders such as
/// "blazor.webassembly#[.{fingerprint}].js" are not rewritten in that set-up and leave the portal stuck
/// on its loading screen once published, so index.html must use the plain framework path.
/// </summary>
public class IndexHtmlTests
{
    private static string IndexHtml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VoiceAgent.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "source", "VoiceAgent.Portal", "wwwroot", "index.html"));
    }

    [Fact]
    public void Index_html_loads_the_plain_blazor_script_without_build_placeholders()
    {
        var html = IndexHtml();

        Assert.Contains("<script src=\"_framework/blazor.webassembly.js\"></script>", html);
        Assert.DoesNotContain("#[", html);
        Assert.DoesNotContain("{fingerprint}", html);
    }
}
