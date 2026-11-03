using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using VoiceAgent.Portal.Services;

namespace VoiceAgent.Tests.Portal;

/// <summary>
/// bUnit context with MudBlazor and fake portal services. MudBlazor services must be disposed
/// asynchronously, so the context is wrapped and disposed through <see cref="IAsyncLifetime"/>.
/// </summary>
public abstract class PortalTestBase : IAsyncLifetime
{
    protected BunitContext Ctx { get; } = new();
    protected FakeLiveCalls Live { get; } = new();
    protected FakeCallsApi Api { get; } = new();

    protected PortalTestBase()
    {
        Ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        Ctx.Services.AddMudServices();
        Ctx.Services.AddSingleton<ILiveCalls>(Live);
        Ctx.Services.AddSingleton<ICallsApi>(Api);
        Ctx.Services.AddScoped<AgentProfile>();
        Ctx.Render<MudPopoverProvider>();
    }

    protected IRenderedComponent<T> Render<T>(Action<ComponentParameterCollectionBuilder<T>>? parameters = null)
        where T : IComponent =>
        parameters is null ? Ctx.Render<T>() : Ctx.Render<T>(parameters);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await Ctx.DisposeAsync();
}
