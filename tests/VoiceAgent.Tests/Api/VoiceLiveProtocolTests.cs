using VoiceAgent.Api.Features.Media;
using VoiceAgent.Api.Features.Telephony;
using VoiceAgent.Api.Features.VoiceLive;

namespace VoiceAgent.Tests.Api;

public class VoiceLiveProtocolTests
{
    [Fact]
    public void Agent_mode_url_targets_the_foundry_agent()
    {
        var uri = VoiceLiveProtocol.BuildUri(new VoiceLiveOptions
        {
            Endpoint = "https://my-foundry.services.ai.azure.com/",
            ProjectName = "cs-project",
            ApiVersion = "2026-04-10",
        });

        Assert.Equal("wss", uri.Scheme);
        Assert.Equal("/voice-live/realtime", uri.AbsolutePath);
        Assert.Contains("agent-name=customer-service-voice-agent", uri.Query);
        Assert.Contains("agent-project-name=cs-project", uri.Query);
        Assert.Contains("api-version=2026-04-10", uri.Query);
        Assert.DoesNotContain("model=", uri.Query);
        Assert.DoesNotContain("authorization", uri.Query);
    }

    [Fact]
    public void Model_mode_url_uses_the_model_and_can_carry_the_token()
    {
        var uri = VoiceLiveProtocol.BuildUri(new VoiceLiveOptions
        {
            Endpoint = "https://my-foundry.services.ai.azure.com",
            Mode = VoiceLiveMode.Model,
            AuthMode = VoiceLiveAuthMode.Query,
        }, accessToken: "abc");

        Assert.Contains("model=gpt-realtime", uri.Query);
        Assert.Contains("authorization=Bearer%20abc", uri.Query);
    }

    [Fact]
    public void Agent_mode_requires_a_project_name() =>
        Assert.Throws<InvalidOperationException>(() => VoiceLiveProtocol.BuildUri(new VoiceLiveOptions { Endpoint = "https://x.services.ai.azure.com" }));

    [Fact]
    public void Session_update_sends_instructions_and_tools_only_in_model_mode()
    {
        var agent = VoiceLiveProtocol.SessionUpdate(new VoiceLiveOptions());
        var model = VoiceLiveProtocol.SessionUpdate(new VoiceLiveOptions { Mode = VoiceLiveMode.Model });

        Assert.Null(agent["session"]!["tools"]);
        Assert.Equal(5, model["session"]!["tools"]!.AsArray().Count);
        Assert.Equal("en-GB-SoniaNeural", agent["session"]!["voice"]!["name"]!.GetValue<string>());
        Assert.Equal(24000, agent["session"]!["input_audio_sampling_rate"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("""{"type":"response.audio.delta","delta":"AAA="}""", typeof(VoiceLiveEvent.AudioDelta))]
    [InlineData("""{"type":"response.output_audio.delta","delta":"AAA="}""", typeof(VoiceLiveEvent.AudioDelta))]
    [InlineData("""{"type":"input_audio_buffer.speech_started"}""", typeof(VoiceLiveEvent.CallerStartedSpeaking))]
    [InlineData("""{"type":"conversation.item.input_audio_transcription.completed","transcript":"hi"}""", typeof(VoiceLiveEvent.CallerTranscript))]
    [InlineData("""{"type":"response.audio_transcript.done","transcript":"hello"}""", typeof(VoiceLiveEvent.AgentTranscript))]
    [InlineData("""{"type":"response.function_call_arguments.done","call_id":"c1","name":"find_customer","arguments":"{}"}""", typeof(VoiceLiveEvent.FunctionCall))]
    [InlineData("""{"type":"response.done"}""", typeof(VoiceLiveEvent.ResponseDone))]
    [InlineData("""{"type":"response.done","response":{"status":"failed","status_details":{"type":"failed","error":{"code":"agent_error","message":"Run failed"}}}}""", typeof(VoiceLiveEvent.ResponseDone))]
    [InlineData("""{"type":"error","error":{"message":"bad"}}""", typeof(VoiceLiveEvent.Error))]
    [InlineData("""{"type":"session.created"}""", typeof(VoiceLiveEvent.SessionCreated))]
    [InlineData("""{"type":"session.updated"}""", typeof(VoiceLiveEvent.SessionReady))]
    [InlineData("""{"type":"response.created"}""", typeof(VoiceLiveEvent.ResponseCreated))]
    [InlineData("""{"type":"rate_limits.updated"}""", typeof(VoiceLiveEvent.Other))]
    [InlineData("not json", typeof(VoiceLiveEvent.Error))]
    public void Parses_voice_live_events(string json, Type expected) =>
        Assert.IsType(expected, VoiceLiveProtocol.Parse(json));

    [Fact]
    public void Failed_responses_carry_their_reason()
    {
        var done = Assert.IsType<VoiceLiveEvent.ResponseDone>(VoiceLiveProtocol.Parse(
            """{"type":"response.done","response":{"status":"failed","status_details":{"type":"failed","error":{"code":"agent_error","message":"Run failed: no user message"}}}}"""));
        Assert.True(done.Failed);
        Assert.Equal("Run failed: no user message", done.Reason);

        Assert.False(Assert.IsType<VoiceLiveEvent.ResponseDone>(VoiceLiveProtocol.Parse("""{"type":"response.done","response":{"status":"completed"}}""")).Failed);
    }

    [Fact]
    public void The_greeting_note_is_a_user_message() =>
        Assert.Equal("user", VoiceLiveProtocol.CallConnectedNote()["item"]!["role"]!.GetValue<string>());

    [Fact]
    public void Function_call_output_links_back_to_the_call() =>
        Assert.Equal("c1", VoiceLiveProtocol.FunctionCallOutput("c1", "{}")["item"]!["call_id"]!.GetValue<string>());
}

public class AcsMediaMessagesTests
{
    [Fact]
    public void Parses_inbound_audio()
    {
        var media = AcsMediaMessages.Parse("""{"kind":"AudioData","audioData":{"timestamp":"t","participantRawID":"4:+44","data":"AQI=","silent":true}}""");
        var audio = Assert.IsType<InboundMedia.Audio>(media);
        Assert.Equal("AQI=", audio.Base64Pcm);
        Assert.True(audio.Silent);
    }

    [Fact]
    public void Parses_metadata_and_ignores_unknown_messages()
    {
        Assert.IsType<InboundMedia.Metadata>(AcsMediaMessages.Parse("""{"kind":"AudioMetadata","audioMetadata":{"sampleRate":24000,"channels":1}}"""));
        Assert.IsType<InboundMedia.Ignored>(AcsMediaMessages.Parse("""{"kind":"DtmfData"}"""));
        Assert.IsType<InboundMedia.Ignored>(AcsMediaMessages.Parse("garbage"));
    }

    [Fact]
    public void Builds_outbound_audio_and_stop_messages_in_the_casing_acs_expects()
    {
        // ACS ignores outbound media in the wrong casing, which leaves callers in silence.
        Assert.Contains("\"Kind\":\"AudioData\"", AcsMediaMessages.OutboundAudio("AQI="));
        Assert.Contains("\"Data\":\"AQI=\"", AcsMediaMessages.OutboundAudio("AQI="));
        Assert.Contains("\"Kind\":\"StopAudio\"", AcsMediaMessages.StopAudio());
    }

    [Fact]
    public void Outbound_messages_match_the_acs_sdk_exactly()
    {
        Assert.Equal(Azure.Communication.CallAutomation.OutStreamingData.GetAudioDataForOutbound([1, 2]), AcsMediaMessages.OutboundAudio("AQI="));
        Assert.Equal(Azure.Communication.CallAutomation.OutStreamingData.GetStopAudioForOutbound(), AcsMediaMessages.StopAudio());
    }

    [Fact]
    public void Inbound_audio_in_the_format_acs_sends_is_understood_by_both_the_sdk_and_the_app()
    {
        const string fromAcs = """{"kind":"AudioData","audioData":{"timestamp":"2026-11-04T17:00:00Z","participantRawID":"4:+447700900101","data":"AQI=","silent":false}}""";
        Assert.IsType<Azure.Communication.CallAutomation.AudioData>(Azure.Communication.CallAutomation.StreamingData.Parse(fromAcs));
        Assert.Equal("AQI=", Assert.IsType<InboundMedia.Audio>(AcsMediaMessages.Parse(fromAcs)).Base64Pcm);
    }

    [Theory]
    [InlineData("""{"kind":"AudioData","audioData":{"data":"AQI=","silent":false}}""")]
    [InlineData("""{"Kind":"AudioData","AudioData":{"Data":"AQI=","IsSilent":false}}""")]
    public void Parses_inbound_audio_in_either_casing(string json) =>
        Assert.Equal("AQI=", Assert.IsType<InboundMedia.Audio>(AcsMediaMessages.Parse(json)).Base64Pcm);

    [Theory]
    [InlineData("+447700900999", "4:+447700900999")]
    [InlineData("8:acs:abc", "8:acs:abc")]
    public void Transfer_targets_accept_phone_numbers_and_raw_ids(string target, string raw) =>
        Assert.Equal(raw, AcsCallControl.ToRawId(target));
}
